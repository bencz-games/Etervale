using System.Diagnostics.CodeAnalysis;
using System.Numerics;
using PristonTale.Simulation.Combat;
using PristonTale.Simulation.Items.Usable;
using PristonTale.Simulation.Modifiers;
using PristonTale.Simulation.World.Ai;
using PristonTale.Simulation.World.Monsters;
using PristonTale.Simulation.World.Monsters.Skills;
using PristonTale.Contracts.Motion;
using PristonTale.Contracts.Combat;
using PristonTale.Contracts.Units;
using PristonTale.Simulation.World.Npcs;
using PristonTale.Simulation.Physics;

namespace PristonTale.Application.World;

internal interface IFieldMonsterSkillHost
{
    bool TryGetPlayer(ulong entityId, [MaybeNullWhen(false)] out WorldPlayer player);
    bool TryGetMonster(ulong entityId, [MaybeNullWhen(false)] out MonsterInstance monster);
    // The field's own collections: walked every tick, their struct enumerators box nothing.
    Dictionary<ulong, MonsterInstance>.ValueCollection Monsters { get; }
    Dictionary<ulong, WorldPlayer>.ValueCollection Players { get; }
    IReadOnlyList<PreyCandidate> NearbyPlayers(Vector3 origin, float radius);
    IReadOnlyList<MonsterDefinition> MonsterDefinitions { get; }

    // MonsterCatalog.Family.
    IReadOnlyList<MonsterDefinition> MonsterFamily(int soundCode);
    MonsterInstance SpawnMonster(MonsterDefinition definition, Vector3 position, float facing, int spawnPointIndex);
    void RemoveMonster(ulong entityId);
    void EmitCombatEvent(in CombatEventView combatEvent);
    bool ApplyStatus(ulong entity, EffectSpec spec, float durationMs, ulong source);
    void EndStuns(MonsterInstance monster);
}

// Server authority for monster skills and the rules some monsters add to their blows: curses, the power one monster
// lends another, statuses and drains on struck players, Babel's splash and regeneration, virtual lives and the
// reinforcements they call, potions, shape changes, the bees of the vampiric machine and the mystic's last explosion.
// The brain picks the swing; this class completes it on the field tick (docs/world.md "Monster skills").
internal sealed class FieldMonsterSkills
{
    private readonly IFieldMonsterSkillHost _host;
    private readonly CombatRng _rng;
    private readonly MonsterHitRider[] _riders = new MonsterHitRider[MonsterSkillRules.MaxRiders];
    private readonly List<MonsterInstance> _pointScratch = new();
    private readonly List<(MonsterInstance Monster, MonsterDefinition Into)> _changes = new();
    private readonly Dictionary<ulong, (MotionState Motion, float Ms, bool Regenerated)> _babelMotion = new();

    public FieldMonsterSkills(IFieldMonsterSkillHost host, CombatRng rng)
    {
        _host = host;
        _rng = rng;
    }

    internal FieldCombat? Combat { get; set; }

    // Mystics left alive explode on their own after 1920 updates of four frames.
    public const float MysticLifetimeMs = 1920 * 4 * GameUnits.TickMs;

    // Reinforcement caps, counted over the monsters standing on the caller's spawn point.
    public const int VirtualLifeReinforcementCap = 8;
    public const int BeeCap = 4;
    public const float ReinforcementScatter = 100f;

    public const int MagicMasterDrinkPercent = 20;

    public void Prepare(MonsterInstance monster)
    {
        MonsterLifeRules.Prepare(monster, _rng);
    }

    // Runs after the brain's tick of a wild monster: finishes completed clips, starts the rules of a new swing and reports
    // the swing to clients. `before` is the position the monster had before the brain moved it.
    public void AfterTick(MonsterInstance monster, MonsterAiTuning tuning, Vector3 before, float dtMs)
    {
        if (monster.AliveMs >= 0f)
        {
            monster.AliveMs += dtMs;
        }

        if (monster.Def.SoundCode == CharacterSoundCodes.VampiricMachine)
        {
            monster.Position = before;
        }

        if (monster.Def.SoundCode == CharacterSoundCodes.Mystic)
        {
            StepMystic(monster);
        }

        if (!monster.IsAlive)
        {
            _babelMotion.Remove(monster.EntityId);
            return;
        }

        CompleteSpecial(monster);
        LendPowerWhenDone(monster);
        SpendLives(monster);
        StepBabel(monster, dtMs);
        DropFarSummonTarget(monster);
        if (!monster.SwingJustBegan)
        {
            return;
        }

        monster.SwingJustBegan = false;
        if (monster.Def.SoundCode == CharacterSoundCodes.VampiricMachine && PointCount(monster) > BeeCap - 1)
        {
            // The machine waits while four of its brood stand on its point.
            monster.AiState = MonsterAiState.Chase;
            monster.Motion = MotionState.Stand;
            monster.AttackHitFired = true;
            return;
        }

        if (monster.AttackIsSkill && monster.Def.SoundCode == CharacterSoundCodes.MagicMaster && _rng.RollPercent() < MagicMasterDrinkPercent)
        {
            // The magic master sometimes drinks a mana potion instead of casting; it restores no life.
            monster.AttackIsSkill = false;
            monster.SkillImpactPending = false;
            monster.AttackHitFired = true;
            monster.AiState = MonsterAiState.Chase;
            monster.Motion = MotionState.Eat;
            monster.SpecialMsLeft = tuning.EatDurationMs;
            monster.DrinkHeals = false;
            return;
        }

        if (monster.AttackIsSkill)
        {
            BeginSkill(monster);
        }

        _host.EmitCombatEvent(new CombatEventView(monster.EntityId, monster.AllyTargetId != 0 ? monster.AllyTargetId : monster.TargetId,
            CombatEventKind.SwingBegin, monster.ActionDurationMs, monster.ActionHitAtMs)
        {
            Clip = monster.ActionSlot,
            Skill = monster.AttackIsSkill
        });
    }

    // Skill start: the curse, the power lent to another monster or Ratoo's wake-up call.
    private void BeginSkill(MonsterInstance monster)
    {
        var definition = monster.Def;
        switch (MonsterSkillRules.Curse(definition))
        {
            case MonsterCurse.Defence:
                if (_host.TryGetPlayer(monster.TargetId, out var defended) && !defended.IsDead)
                {
                    var (min, max) = MonsterSkillRules.CurseDefenceRange(definition);
                    _host.ApplyStatus(defended.EntityId, MonsterStatuses.CurseDefenceSpec(_rng.Range(min, max)), MonsterStatuses.CurseMs, monster.EntityId);
                }

                break;
            case MonsterCurse.Attack:
                if (_host.TryGetPlayer(monster.TargetId, out var attacker) && !attacker.IsDead)
                {
                    var (min, max) = MonsterSkillRules.CurseAttackRange;
                    _host.ApplyStatus(attacker.EntityId, MonsterStatuses.CurseAttackSpec(_rng.Range(min, max)), MonsterStatuses.CurseMs, monster.EntityId);
                }

                break;
            case MonsterCurse.AllyPower:
                if (FindAlly(monster) is { } ally)
                {
                    monster.AllyTargetId = ally.EntityId;
                    monster.Facing = Planar.Heading(monster.Position, ally.Position);
                }
                else
                {
                    // Nobody to strengthen: the swing stays the normal attack it was drawn as.
                    monster.AttackIsSkill = false;
                    monster.Motion = MotionState.Attack;
                    var attack = monster.AttackVariant;
                    monster.ActionSlot = attack.Slot;
                    monster.ActionKey = attack.Key;
                    monster.ActionDurationMs = attack.DurationMs;
                    monster.ActionHitAtMs = attack.HitAtMs;
                }

                break;
        }

        if (definition.SoundCode == CharacterSoundCodes.Ratoo)
        {
            foreach (var other in _host.Monsters)
            {
                if (other != monster && other.IsAlive && Near(monster.Position, other.Position, MonsterSkillRules.WakeRadius, MonsterSkillRules.AllyHeight))
                {
                    _host.EndStuns(other);
                }
            }
        }
    }

    private MonsterInstance? FindAlly(MonsterInstance monster)
    {
        foreach (var other in _host.Monsters)
        {
            if (other != monster && other.IsAlive && !other.IsSummon && !other.Effects.TryGet(MonsterStatuses.Power, out _)
                && Near(monster.Position, other.Position, MonsterSkillRules.AllyRadius, MonsterSkillRules.AllyHeight))
            {
                return other;
            }
        }

        return null;
    }

    // The lent power takes hold when the skill clip ends.
    private void LendPowerWhenDone(MonsterInstance monster)
    {
        if (monster.AllyTargetId == 0 || monster.AiState == MonsterAiState.Attack && !monster.SwingJustBegan)
        {
            return;
        }

        var allyId = monster.AllyTargetId;
        monster.AllyTargetId = 0;
        if (_host.TryGetMonster(allyId, out var ally) && ally.IsAlive)
        {
            var (min, max) = MonsterSkillRules.PowerRange;
            _host.ApplyStatus(allyId, MonsterStatuses.PowerSpec(_rng.Range(min, max)), MonsterStatuses.PowerMs, monster.EntityId);
        }
    }

    // A potion's clip heals half the life; a finished shape change replaces the monster with its other form.
    private void CompleteSpecial(MonsterInstance monster)
    {
        var completed = monster.CompletedSpecial;
        if (completed == MotionState.Stand)
        {
            return;
        }

        monster.CompletedSpecial = MotionState.Stand;
        if (completed == MotionState.Warp && monster.VanishOnWarp)
        {
            _vanished.Add(monster);
            return;
        }

        if (completed == MotionState.Eat && monster.DrinkHeals)
        {
            var max = monster.Def.Combat.MaxLife;
            monster.Life = Math.Min(max, monster.Life + MonsterLifeRules.PotionHeal(monster.Def));
            _host.EmitCombatEvent(new CombatEventView(monster.EntityId, monster.EntityId, CombatEventKind.PotionApplied, resources: 1));
        }
        else if (completed == MotionState.Warp && ChangeInto(monster) is { } into)
        {
            _changes.Add((monster, into));
        }
    }

    private MonsterDefinition? ChangeInto(MonsterInstance monster)
    {
        MonsterDefinition? chosen = null;
        foreach (var candidate in _host.MonsterDefinitions)
        {
            if (MonsterTransformRules.Accepts(monster, candidate) && (chosen is null || candidate.Code < chosen.Code))
            {
                chosen = candidate;
            }
        }

        return chosen;
    }

    private readonly List<MonsterInstance> _vanished = new();

    // Shape changes swap the instance after every monster has stepped; monsters that warped away leave.
    public void ApplyChanges()
    {
        foreach (var monster in _vanished)
        {
            _host.RemoveMonster(monster.EntityId);
        }

        _vanished.Clear();
        foreach (var (monster, into) in _changes)
        {
            if (!monster.IsAlive || !_host.TryGetMonster(monster.EntityId, out _))
            {
                continue;
            }

            _host.RemoveMonster(monster.EntityId);
            var changed = _host.SpawnMonster(into, monster.Position, monster.Facing, monster.SpawnPointIndex);
            changed.TargetId = monster.TargetId;
            changed.AiState = monster.TargetId != 0 ? MonsterAiState.Chase : MonsterAiState.Idle;
            changed.Motion = MotionState.Stand;
        }

        _changes.Clear();
    }

    // Every second life Kelvezu spends calls two Chaos Cara, Mokova two Dark Guards and Hestian two Xetans of level 117,
    // who fight only her duel's player.
    private const int HestianXetanLevel = 117;

    private void SpendLives(MonsterInstance monster)
    {
        while (monster.LivesSpent > 0)
        {
            monster.LivesSpent--;
            var remaining = monster.VirtualLives + monster.LivesSpent;
            var called = monster.Def.SoundCode switch
            {
                CharacterSoundCodes.Kelvezu => CharacterSoundCodes.ChaosCara,
                CharacterSoundCodes.Mokova => CharacterSoundCodes.DarkGuard,
                CharacterSoundCodes.Hestian => CharacterSoundCodes.Xetan,
                _ => 0
            };
            if (called == 0 || (remaining & 1) != 0)
            {
                continue;
            }

            for (var count = 0; count < 2; count++)
            {
                var offset = new Vector3(_rng.Range(0, 199) - ReinforcementScatter, 0f, _rng.Range(0, 199) - ReinforcementScatter);
                var level = called == CharacterSoundCodes.Xetan ? HestianXetanLevel : 0;
                if (CallReinforcement(monster, called, monster.Position + offset, VirtualLifeReinforcementCap, level) is { } reinforcement)
                {
                    reinforcement.DuelPlayerId = monster.DuelPlayerId;
                }
            }
        }
    }

    private MonsterInstance? CallReinforcement(MonsterInstance caller, int soundCode, Vector3 position, int cap, int level = 0)
    {
        if (PointCount(caller) >= cap || Reinforcement(soundCode, level) is not { } definition)
        {
            return null;
        }

        return _host.SpawnMonster(definition, position, caller.Facing, caller.SpawnPointIndex);
    }

    // The reinforcement of a family: its ordinary definition of the lowest level, then the lowest code; with a level, the
    // first of that level.
    private MonsterDefinition? Reinforcement(int soundCode, int level = 0)
    {
        MonsterDefinition? chosen = null;
        foreach (var candidate in _host.MonsterFamily(soundCode))
        {
            if (level > 0 && candidate.Combat.Level != level)
            {
                continue;
            }

            if (chosen is null || Rank(candidate) < Rank(chosen))
            {
                chosen = candidate;
            }
        }

        return chosen;

        static long Rank(MonsterDefinition definition) => (definition.IsBoss ? 1L << 32 : 0L) + definition.Combat.Level;
    }

    // Live monsters standing on the caller's spawn point, the caller included.
    private int PointCount(MonsterInstance caller)
    {
        if (caller.SpawnPointIndex < 0)
        {
            return 1;
        }

        var count = 0;
        foreach (var other in _host.Monsters)
        {
            if (other.IsAlive && !other.IsSummon && other.SpawnPointIndex == caller.SpawnPointIndex)
            {
                count++;
            }
        }

        return count;
    }

    // Babel heals once per clip that runs 120 frames while it hunts a player.
    private void StepBabel(MonsterInstance monster, float dtMs)
    {
        if (!MonsterLifeRules.Regenerates(monster.Def))
        {
            return;
        }

        var (motion, ms, regenerated) = _babelMotion.GetValueOrDefault(monster.EntityId, (monster.Motion, 0f, false));
        if (motion != monster.Motion || monster.SwingJustBegan)
        {
            (motion, ms, regenerated) = (monster.Motion, 0f, false);
        }

        ms += dtMs;
        if (!regenerated && ms >= MonsterSkillRules.LateSkillHitMs && _host.TryGetPlayer(monster.TargetId, out _))
        {
            regenerated = true;
            var near = 0;
            foreach (var player in _host.Players)
            {
                if (!player.IsDead && Near(monster.Position, player.Position, MonsterLifeRules.BabelRegenRadius, MonsterLifeRules.BabelRegenHeight))
                {
                    near++;
                }
            }

            var max = monster.Def.Combat.MaxLife;
            var life = (long)monster.Life + MonsterLifeRules.BabelRegen(near, monster.Life, max);
            monster.Life = (int)Math.Min(max, life);
        }

        _babelMotion[monster.EntityId] = (motion, ms, regenerated);
    }

    // Valento ignores a summon that keeps more than 120 units away.
    private void DropFarSummonTarget(MonsterInstance monster)
    {
        const float limit = 120f;
        if (monster.Def.SoundCode == CharacterSoundCodes.DeathKnight && monster.AiState != MonsterAiState.Attack
            && _host.TryGetMonster(monster.TargetId, out var summon) && summon.IsSummon
            && Vector2.DistanceSquared(new Vector2(monster.Position.X, monster.Position.Z), new Vector2(summon.Position.X, summon.Position.Z)) > limit * limit)
        {
            monster.TargetId = 0;
        }
    }

    // The mystic explodes when it dies, or by itself after its lifetime, with its skill damage on every player within its
    // skill range; a monster it fought takes double.
    private void StepMystic(MonsterInstance monster)
    {
        if (monster.AliveMs < 0f)
        {
            return;
        }

        if (monster.IsAlive)
        {
            if (monster.AliveMs < MysticLifetimeMs)
            {
                return;
            }

            monster.Life = 1;
            monster.ApplyDamage(1);
            Combat?.BeginCorpse(monster.EntityId);
        }

        monster.AliveMs = -1f;
        var skill = monster.Def.Skill;
        _host.EmitCombatEvent(new CombatEventView(monster.EntityId, monster.TargetId, CombatEventKind.MonsterSkillImpact,
            sourceSoundCode: monster.Def.SoundCode, position: monster.Position, facing: monster.Facing));
        var candidates = _host.NearbyPlayers(monster.Position, MonsterOffensiveSkill.QueryRadius(skill));
        for (var index = 0; index < candidates.Count; index++)
        {
            if (_host.TryGetPlayer(candidates[index].EntityId, out var player)
                && MonsterOffensiveSkill.Contains(skill, monster.Position, monster.Position, player.Position))
            {
                SkillOnPlayer(monster, player, applyRiders: false);
            }
        }

        if (_host.TryGetMonster(monster.TargetId, out var fought) && fought.IsSummon && fought.IsAlive)
        {
            var damage = _rng.Range(skill.SkillDamage.Min, Math.Max(skill.SkillDamage.Min, skill.SkillDamage.Max)) * 2;
            fought.ApplyDamage(damage);
            _host.EmitCombatEvent(new CombatEventView(monster.EntityId, fought.EntityId, CombatEventKind.Hit, damage: damage, killed: !fought.IsAlive)
            {
                Skill = true
            });
        }
    }

    // The skill's blow. Areas are placed when the swing began; the host's checks filter live victims.
    public void ResolveSkill(MonsterInstance attacker, ulong targetId)
    {
        if (!attacker.AttackIsSkill || !attacker.SkillImpactPending || targetId != attacker.SkillTargetId)
        {
            return;
        }

        attacker.SkillImpactPending = false;
        _host.EmitCombatEvent(new CombatEventView(attacker.EntityId, attacker.AllyTargetId != 0 ? attacker.AllyTargetId : targetId,
            CombatEventKind.MonsterSkillImpact, sourceSoundCode: attacker.Def.SoundCode, position: attacker.SkillOrigin, facing: attacker.SkillFacing));
        if (attacker.AllyTargetId != 0 || !MonsterSkillRules.DealsSkillDamage(attacker.Def))
        {
            return;
        }

        var skill = attacker.Def.Skill;
        if (_host.TryGetMonster(targetId, out var monster) && monster.IsSummon)
        {
            SkillOnMonster(attacker, monster);
            return;
        }

        if (skill.SkillRange == 0)
        {
            if (_host.TryGetPlayer(targetId, out var target)
                && MonsterOffensiveSkill.Contains(skill, attacker.SkillOrigin, attacker.SkillCenter, target.Position))
            {
                SkillOnPlayer(attacker, target, applyRiders: true);
            }

            return;
        }

        var candidates = _host.NearbyPlayers(attacker.SkillOrigin, MonsterOffensiveSkill.QueryRadius(skill));
        for (var index = 0; index < candidates.Count; index++)
        {
            if (_host.TryGetPlayer(candidates[index].EntityId, out var player)
                && MonsterOffensiveSkill.Contains(skill, attacker.SkillOrigin, attacker.SkillCenter, player.Position))
            {
                SkillOnPlayer(attacker, player, applyRiders: true);
            }
        }
    }

    // A skill blow never misses and is never blocked: the power less the player's absorption, at least 1.
    private void SkillOnPlayer(MonsterInstance attacker, WorldPlayer target, bool applyRiders)
    {
        if (target.IsDead || target.Life <= 0)
        {
            return;
        }

        var range = attacker.Def.Skill.SkillDamage;
        var power = _rng.Range(range.Min, Math.Max(range.Min, range.Max));
        var count = applyRiders ? MonsterSkillRules.SkillRiders(attacker.Def, attacker.ActionKey, _riders) : 0;
        for (var index = 0; index < count; index++)
        {
            if (_riders[index].Kind == MonsterRiderKind.ResistedPower && _riders[index].ResistedBy is { } element)
            {
                power = MonsterStatuses.Resisted(power, target.Character.Derived.Resistances[element]);
            }
        }

        var defender = CombatProfileFactory.ForPlayerDefender(target.Character);
        var damage = (int)Math.Clamp((long)power - defender.Absorption, 1, int.MaxValue);
        damage = ModifierRules.AgainstUndead(damage, attacker.Def.Brood == MonsterBrood.Undead, target.Character.Effects.Totals);
        damage = Combat?.SkillCombat?.Defend(attacker, target, damage, true) ?? damage;
        damage = CastleItems.Defend(attacker.Def.SoundCode, target.Character.Effects, damage);
        target.ApplyDamage(damage);
        _host.EmitCombatEvent(new CombatEventView(attacker.EntityId, target.EntityId, CombatEventKind.Hit, damage: damage, killed: target.IsDead)
        {
            Skill = true
        });
        ApplyRiders(attacker, target, count, power);
    }

    // A skill on a summon strikes it alone; the Guardian Saint's strikes every monster around it.
    private void SkillOnMonster(MonsterInstance attacker, MonsterInstance target)
    {
        if (attacker.Def.SoundCode != CharacterSoundCodes.GuardianSaint)
        {
            Combat?.MonsterOnMonster(attacker, target, skill: true);
            return;
        }

        var skill = attacker.Def.Skill;
        var power = _rng.Range(skill.SkillDamage.Min, Math.Max(skill.SkillDamage.Min, skill.SkillDamage.Max));
        foreach (var other in _host.Monsters)
        {
            if (other == attacker || !other.IsAlive || !Near(attacker.Position, other.Position, skill.SkillRange, MonsterSkillRules.AllyHeight))
            {
                continue;
            }

            var damage = power - power * other.Def.Combat.Absorption / 100;
            if (damage <= 0)
            {
                continue;
            }

            other.ApplyDamage(damage);
            _host.EmitCombatEvent(new CombatEventView(attacker.EntityId, other.EntityId, CombatEventKind.Hit, damage: damage, killed: !other.IsAlive)
            {
                Skill = true
            });
            if (!other.IsAlive && other.IsSummon)
            {
                Combat?.BeginCorpse(other.EntityId);
            }
        }
    }

    // Babel's roll for a normal blow: whether it splashes and the percent of the roll its target takes.
    public (int Percent, bool Splash) BeginBlow(MonsterInstance attacker, WorldPlayer target)
    {
        if (attacker.Def.SoundCode != CharacterSoundCodes.Babel || _rng.RollPercent() >= MonsterSkillRules.BabelSplashChance)
        {
            return (100, false);
        }

        var far = Vector2.DistanceSquared(new Vector2(attacker.Position.X, attacker.Position.Z), new Vector2(target.Position.X, target.Position.Z))
            > MonsterSkillRules.BabelFarDistance * MonsterSkillRules.BabelFarDistance;
        return (far ? MonsterSkillRules.BabelFarPercent : 100, true);
    }

    // After a normal blow on a player: its riders, and Babel's splash on the players around the target.
    public void AfterBlow(MonsterInstance attacker, WorldPlayer target, int damage, bool splash)
    {
        if (damage > 0)
        {
            var count = MonsterSkillRules.BlowRiders(attacker.Def, _riders);
            ApplyRiders(attacker, target, count, damage);
        }

        if (!splash || Combat is null)
        {
            return;
        }

        var struck = 1;
        var candidates = _host.NearbyPlayers(target.Position, MonsterSkillRules.BabelSplashRadius);
        for (var index = 0; index < candidates.Count && struck < 16; index++)
        {
            if (candidates[index].EntityId != target.EntityId && _host.TryGetPlayer(candidates[index].EntityId, out var other) && !other.IsDead
                && Near(target.Position, other.Position, MonsterSkillRules.BabelSplashRadius, MonsterSkillRules.BabelSplashHeight))
            {
                struck++;
                Combat.BlowOnPlayer(attacker, other, MonsterSkillRules.BabelSplashPercent, splash: false);
            }
        }
    }

    // The vampiric machine's blow sends a bee out of it instead of striking.
    public bool ReplacesBlow(MonsterInstance attacker)
    {
        if (attacker.Def.SoundCode != CharacterSoundCodes.VampiricMachine)
        {
            return false;
        }

        CallReinforcement(attacker, CharacterSoundCodes.VampiricBee, attacker.Position, BeeCap);
        return true;
    }

    // A monster's blow on another monster, a wild one's on a summon or a summon's on a wild one alike: its accuracy against
    // the target's defence at their levels, a roll of its damage (its skill damage for a skill that has any), eight times
    // as much from the bosses, less the target's absorption; Kelvezu kills outright. Null on a miss.
    public static int? BlowOnMonster(CombatRng rng, MonsterInstance attacker, MonsterInstance target, bool skill)
    {
        var combat = attacker.Def.Combat;
        var attack = CombatProfileFactory.ForMonsterBasicAttack(combat);
        if (!CombatResolver.RollHit(rng, CombatResolver.HitChance(attack, DefenderProfile.FromMonster(target))))
        {
            return null;
        }

        if (MonsterSkillRules.KillsMonsters(attacker.Def))
        {
            return Math.Max(1, target.Life);
        }

        var range = skill && attacker.Def.Skill.SkillDamage.Max > 0 ? attacker.Def.Skill.SkillDamage : combat.AttackDamage;
        var roll = rng.Range(range.Min, Math.Max(range.Min, range.Max));
        if (MonsterSkillRules.MultipliesAgainstMonsters(attacker.Def))
        {
            roll *= MonsterSkillRules.MonsterDamageMultiplier;
        }

        return CombatResolver.ApplyAbsorbMonster(roll, target.Def.Combat.Absorption);
    }

    private void ApplyRiders(MonsterInstance attacker, WorldPlayer target, int count, int power)
    {
        for (var index = 0; index < count && !target.IsDead; index++)
        {
            var rider = _riders[index];
            var character = target.Character;
            switch (rider.Kind)
            {
                case MonsterRiderKind.Status:
                    if (rider.UnlessStunned && character.Effects.Has(ControlFlags.Stun))
                    {
                        break;
                    }

                    var duration = rider.ResistedBy is { } element
                        ? MonsterStatuses.ResistedMs(rider.Frames, character.Derived.Resistances[element])
                        : MonsterStatuses.FramesMs(rider.Frames);
                    _host.ApplyStatus(target.EntityId, StatusSpec(rider), duration, attacker.EntityId);
                    break;
                case MonsterRiderKind.DrainPercent when rider.Resource == ResourceKind.Mana:
                    character.Mana = Math.Max(0, character.Mana - character.MaxMana * rider.Percent / 100);
                    break;
                case MonsterRiderKind.DrainPercent:
                    character.Stamina = Math.Max(0, character.Stamina - character.MaxStamina * rider.Percent / 100);
                    break;
                case MonsterRiderKind.ManaByPower:
                    character.Mana = Math.Max(0, character.Mana - power);
                    break;
            }
        }
    }

    private static EffectSpec StatusSpec(in MonsterHitRider rider) => rider.Status switch
    {
        MonsterStatuses.Poison => MonsterStatuses.PoisonSpec(),
        MonsterStatuses.IceSlow or MonsterStatuses.WebSlow => MonsterStatuses.SlowSpec(rider.Status, rider.Speed),
        _ => MonsterStatuses.StunSpec(rider.Status)
    };

    private static bool Near(Vector3 a, Vector3 b, float radius, float height) => MathF.Abs(a.Y - b.Y) < height && Planar.Within(a, b, radius);

    // Views show a boss with lives as one bar over all its lives.
    public static (int Life, int Max) ViewLife(MonsterInstance monster) =>
        monster.VirtualLivesMax > 0 ? MonsterLifeRules.DisplayLife(monster) : (monster.Life, monster.Def.Combat.MaxLife);
}
