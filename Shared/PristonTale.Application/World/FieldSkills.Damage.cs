using System.Numerics;
using PristonTale.Simulation.Combat;
using PristonTale.Simulation.Modifiers;
using PristonTale.Simulation.Skills;
using PristonTale.Simulation.World;
using PristonTale.Simulation.World.Ai;
using PristonTale.Simulation.World.Monsters;
using PristonTale.Contracts.Motion;
using PristonTale.Simulation.Physics;

namespace PristonTale.Application.World;

// Damage of single-target skills: weapon hits (volleys, element adds, enchants) and bolts, the burst that follows a
// weapon hit, and the knock-backs, pushes and taunts that areas apply (see FieldSkills.Areas).
internal sealed partial class FieldSkills
{
    private static bool Immovable(MonsterInstance monster) => MonsterAiTuning.IsUnyielding(monster.Def);

    private bool WeaponHit(WorldPlayer caster, ActiveCast cast, SkillRule rule, int hit, long nowMs)
    {
        if (cast.Missed && cast.Rule.StopAfterMiss)
        {
            return false;
        }

        if (_host.TryGetPlayer(cast.TargetId, out var foe))
        {
            return WeaponHitOnPlayer(caster, cast, rule, hit, foe);
        }

        var rank = cast.Rank;
        if (!_host.TryGetMonster(cast.TargetId, out var monster) || !Strikeable(caster, monster)
            || !InRange(caster, monster.Position, SkillFormulas.Range(cast.Rule, rank, caster.Character)))
        {
            return false;
        }

        var character = caster.Character;
        var damageRule = rule.Damage ?? new SkillDamageRule();
        var attack = SkillFormulas.WeaponAttack(character, rule, rank, monster.Def, _rng, hit);
        attack = attack with { ExtraPercent = attack.ExtraPercent + NextHitPercent(character) };
        var defence = DefenderProfile.FromMonster(monster);
        if (rule.At(rule.Volley, rank) > 0)
        {
            return VolleyHit(caster, cast, rule, monster, attack, defence);
        }

        // Element damage added to the hit (a fire javelin): rolled apart so its own resistance and brood bonus apply to it.
        var elemental = 0;
        if (damageRule.FlatElement is not null && damageRule.Min.Length > 0)
        {
            elemental = SkillFormulas.WithBroodBonus(_rng.Range(rule.At(damageRule.Min, rank), rule.At(damageRule.Max, rank)), damageRule, monster.Def, rank);
            attack = attack with { SkillFlatAdd = attack.SkillFlatAdd + elemental };
        }

        var result = CombatResolver.Resolve(_rng, attack, defence, alwaysHit: damageRule.NeverMiss || rule.NoAccuracy || rule.NoAccuracyHits.Contains(hit + 1));
        if (result.Damage <= 0)
        {
            cast.Missed = true;
            return false;
        }

        ConsumeNextHit(caster);
        var damage = result.Damage;
        if (damageRule.FlatElement is { } flatElement)
        {
            var resisted = elemental - CombatResolver.ApplyResistance(elemental, monster.Def.Combat.Resistances[flatElement]);
            damage = Math.Max(1, damage - resisted);
        }
        else
        {
            damage = SkillFormulas.WithBroodBonus(damage, damageRule, monster.Def, rank);
        }

        damage = SkillFormulas.WithUndeadBonus(damage, character.Effects.Totals, monster.Def);
        if (damageRule.Element == Element.Bionic && damageRule.FlatElement is null)
        {
            damage += EnchantStrike(caster, monster, basicAttack: false);
        }

        damage = ModifierRules.ScaleDamage(damage, character.Effects.Totals, monster.Effects.Totals);
        damage = CombatResolver.CutHit(_rng, damage, monster.Def.Combat.MaxLife, monster.Def.Combat.Level, character.Level);
        PayLifeCost(caster, cast);
        var burst = cast.Rule.Burst;
        var origin = caster.Position;
        var lifeBefore = monster.Life;
        Pay(caster, monster, damage, result.Crit, cast.Rule.ForceFlinch);
        AfflictStruck(caster, cast, monster);
        DrainAfterHit(caster, damageRule, rank, monster, lifeBefore);
        if (burst is not null && burst.AfterHit == hit)
        {
            Burst(caster, cast, rule, origin);
        }

        return true;
    }

    // A weapon skill's harmful effect lands on the monster its hit struck (a dash that stuns).
    private void AfflictStruck(WorldPlayer caster, ActiveCast cast, MonsterInstance monster)
    {
        var buff = cast.Rule.Buff;
        if (buff is { Harmful: true } && monster.IsAlive && Takes(buff, monster) && (buff.LifeCut.Length == 0 || Shrink(caster, monster, buff, cast.Rank)))
        {
            ApplyEffect(monster.Effects, monster.EntityId, caster.EntityId, cast.Rule.Code, buff, cast.Rank, ResistanceOf(buff, monster), caster.Character);
        }
    }

    // Several shots in one strike: each rolls accuracy with the skill's rating, the hits add up their own damage rolls.
    private bool VolleyHit(WorldPlayer caster, ActiveCast cast, SkillRule rule, MonsterInstance monster, AttackProfile attack, DefenderProfile defence)
    {
        var damage = VolleyDamage(attack, defence, rule.At(rule.Volley, cast.Rank));
        if (damage <= 0)
        {
            cast.Missed = true;
            return false;
        }

        var character = caster.Character;
        damage = CombatResolver.ApplyElement(damage, attack.Element, defence.Resistances);
        damage = CombatResolver.ApplyAbsorbMonster(damage, defence.Absorption);
        damage = SkillFormulas.WithBroodBonus(damage, rule.Damage, monster.Def, cast.Rank);
        damage = SkillFormulas.WithUndeadBonus(damage, character.Effects.Totals, monster.Def);
        Pay(caster, monster, ModifierRules.ScaleDamage(damage, character.Effects.Totals, monster.Effects.Totals), false);
        AfflictStruck(caster, cast, monster);
        return true;
    }

    // The shots of a volley that hit, each its own damage roll with the skill's percent (no flat add).
    private int VolleyDamage(in AttackProfile attack, in DefenderProfile defence, int shots)
    {
        var chance = CombatResolver.HitChance(attack, defence);
        var damage = 0;
        for (var shot = 0; shot < shots; shot++)
        {
            if (CombatResolver.RollHit(_rng, chance))
            {
                damage += CombatResolver.ApplySkill(CombatResolver.RollBaseDamage(_rng, attack), attack with { SkillFlatAdd = 0 });
            }
        }

        return damage;
    }

    // The fire burst of a weapon hit: the caster's rolled attack damage with the skill's percent, scaled by the burst's
    // percent, to every enemy around the caster.
    private void Burst(WorldPlayer caster, ActiveCast cast, SkillRule rule, Vector3 origin)
    {
        var burst = cast.Rule.Burst;
        var character = caster.Character;
        var weapon = WeaponDamage.Compute(character);
        var power = _rng.Range(weapon.Min, Math.Max(weapon.Min, weapon.Max));
        power += power * SkillFormulas.At(rule.Damage?.Percent, cast.Rank) / 100;
        power = power * burst.Percent / 100;
        var damage = new SkillDamageRule { Element = burst.Element };
        _targets.Clear();
        CollectCircle(caster, origin, burst.Radius, rule.MaxTargets);
        CollectFoes(caster, origin, burst.Radius, Vector2.UnitY, 0f, rule.MaxTargets);
        foreach (var monster in _targets)
        {
            if (monster is null)
            {
                continue;
            }

            var dealt = SkillFormulas.PowerAgainst(Math.Max(1, power), damage, monster.Def, character.Effects.Totals, monster.Effects.Totals, cast.Rank);
            Pay(caster, monster, dealt, false);
        }

        foreach (var foe in _foes)
        {
            _host.HurtPlayer(caster, foe, PowerOnPlayer(caster, foe, Math.Max(1, power), damage, cast.Rank), false);
        }
    }

    private bool MagicHit(WorldPlayer caster, ActiveCast cast, SkillRule rule)
    {
        if (_host.TryGetPlayer(cast.TargetId, out var foe))
        {
            return MagicHitOnPlayer(caster, cast, rule, foe);
        }

        if (!_host.TryGetMonster(cast.TargetId, out var monster) || !Strikeable(caster, monster))
        {
            return false;
        }

        var character = caster.Character;
        var power = SkillFormulas.RollPower(character, rule, rule.Damage, cast.Rank, _rng);
        var damage = SkillFormulas.PowerAgainst(power, rule.Damage, monster.Def, character.Effects.Totals, monster.Effects.Totals, cast.Rank);
        var lifeBefore = monster.Life;
        Pay(caster, monster, SkillFormulas.WithUndeadBonus(damage, character.Effects.Totals, monster.Def), false);
        DrainAfterHit(caster, rule.Damage, cast.Rank, monster, lifeBefore);
        return true;
    }

    // The radius the area of this rank covers: the table, or the distance to the clicked target plus a pad.
    private float AreaRadius(SkillRule rule, int rank, WorldPlayer caster, Vector3 aim)
    {
        if (rule.RadiusTargetPad > 0)
        {
            return Planar.Distance(aim, caster.Position) + rule.RadiusTargetPad;
        }

        if (rule.AreaShape == SkillAreaShape.Box && rule.Radius.Length == 0)
        {
            return SkillFormulas.Range(rule, rank, caster.Character) + rule.BoxLengthPad;
        }

        return rule.At(rule.Radius, rank);
    }

    private void CollectCircle(WorldPlayer caster, Vector3 center, float radius, int limit)
    {
        foreach (var monster in _host.Monsters)
        {
            if (!Strikeable(caster, monster) || MathF.Abs(monster.Position.Y - center.Y) > HeightTolerance)
            {
                continue;
            }

            if (Planar.DistanceSquared(monster.Position, center) <= radius * radius)
            {
                _targets.Add(monster);
                if (_targets.Count >= limit)
                {
                    break;
                }
            }
        }
    }

    private static bool InBox(Vector2 offset, Vector2 forward, float halfWidth, float length)
    {
        var along = Vector2.Dot(offset, forward);
        var side = forward.X * offset.Y - forward.Y * offset.X;
        return along >= 0f && along <= length && MathF.Abs(side) <= halfWidth;
    }

    // Moves the monster straight away from the caster to the given distance, unless a wall is in the way; it drops its
    // target and stands, as the original push does.
    private void Push(WorldPlayer caster, MonsterInstance monster, int pushTo)
    {
        if (pushTo <= 0 || !monster.IsAlive || Immovable(monster))
        {
            return;
        }

        var away = new Vector2(monster.Position.X - caster.Position.X, monster.Position.Z - caster.Position.Z);
        var distance = away.Length();
        if (distance >= pushTo)
        {
            return;
        }

        away = distance > 1e-3f ? away / distance : new Vector2(MathF.Sin(caster.Facing), MathF.Cos(caster.Facing));
        var destination = new Vector3(caster.Position.X + away.X * pushTo, monster.Position.Y, caster.Position.Z + away.Y * pushTo);
        var terrain = _host.Terrain;
        if (terrain.CrossesWall(monster.Position, destination))
        {
            return;
        }

        monster.Position = terrain.SettleNear(destination, FieldTerrain.DropTolerance);
        monster.TargetId = 0;
        monster.Motion = MotionState.Stand;
        monster.AiState = MonsterAiState.Idle;
    }

    // A short knock-back away from the caster with the flinch motion (bosses only flinch).
    private void KnockBack(WorldPlayer caster, MonsterInstance monster, int distance)
    {
        if (distance <= 0 || !monster.IsAlive)
        {
            return;
        }

        if (!Immovable(monster))
        {
            var away = new Vector2(monster.Position.X - caster.Position.X, monster.Position.Z - caster.Position.Z);
            away = away.LengthSquared() > 1e-4f ? Vector2.Normalize(away) : new Vector2(MathF.Sin(caster.Facing), MathF.Cos(caster.Facing));
            var destination = new Vector3(monster.Position.X + away.X * distance, monster.Position.Y, monster.Position.Z + away.Y * distance);
            if (!_host.Terrain.CrossesWall(monster.Position, destination))
            {
                monster.Position = _host.Terrain.SettleNear(destination, FieldTerrain.DropTolerance);
            }
        }

        if (monster.Motion != MotionState.Dead)
        {
            MonsterAiBrain.ApplyFlinchOnHit(monster, _rng, monster.Def.Combat.MaxLife, crit: true, _host.TuningFor(monster.Def).FlinchDurationMs);
        }
    }

    // The monster drops its prey and targets the caster for the duration; the target lock keeps it from retargeting.
    private static void Taunt(WorldPlayer caster, MonsterInstance monster, float durationMs)
    {
        monster.Facing = Planar.Heading(monster.Position, caster.Position);
        MonsterAiBrain.ForceTarget(monster, caster.EntityId, durationMs);
    }

    // The resistance that shortens a debuff on a monster.
    private static int ResistanceOf(SkillBuffRule buff, MonsterInstance monster)
    {
        if (buff is null)
        {
            return 0;
        }

        return buff.ResistanceFrom(ModifierRules.Resistances(monster.Effects.Totals, monster.Def.Combat.Resistances));
    }
}
