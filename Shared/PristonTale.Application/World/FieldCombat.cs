using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Numerics;
using PristonTale.Simulation.Combat;
using PristonTale.Simulation.Items;
using PristonTale.Simulation.Items.Usable;
using PristonTale.Simulation.Modifiers;
using PristonTale.Simulation.Parties;
using PristonTale.Simulation.Quests;
using PristonTale.Simulation.World.Ai;
using PristonTale.Simulation.World.Loot;
using PristonTale.Simulation.Skills;
using PristonTale.Simulation.World.Monsters;
using PristonTale.Simulation.World.Monsters.Skills;
using PristonTale.Contracts.Combat;

namespace PristonTale.Application.World;

internal interface IFieldCombatHost
{
    bool TryGetPlayer(ulong entityId, [MaybeNullWhen(false)] out WorldPlayer player);
    bool TryGetMonster(ulong entityId, [MaybeNullWhen(false)] out MonsterInstance monster);
    // The field's own collections: walked every tick, their struct enumerators box nothing.
    Dictionary<ulong, MonsterInstance>.ValueCollection Monsters { get; }
    IReadOnlyList<PreyCandidate> NearbyPlayers(Vector3 origin, float radius);
    void DespawnMonster(ulong entityId);

    MonsterAiTuning TuningFor(MonsterDefinition def);

    (float DurationMs, float HitAtMs) PlayerSwingTiming(WorldPlayer player);

    // Whether two players of the field may fight each other (the castle's battle zone).
    bool MayFight(WorldPlayer attacker, WorldPlayer target);

    void EmitCombatEvent(in CombatEventView combatEvent);
}

public readonly record struct KillPayout(ulong MonsterEntityId, ulong CreditedPlayerId, ulong ExpAwarded, IReadOnlyList<RolledDrop> Drops);

internal sealed class FieldCombat
{
    public const float CorpseDespawnMs = 5710f;

    private readonly IFieldCombatHost _host;
    private readonly CombatRng _rng;

    private readonly Dictionary<ulong, MonsterDamageLog> _damageLogs = new();
    private readonly Dictionary<ulong, float> _corpseMsLeft = new();
    private readonly Dictionary<ulong, ulong> _expByPlayer = new();

    private readonly List<ulong> _expiredScratch = new();

    public FieldCombat(IFieldCombatHost host, CombatRng rng, int experienceMultiplier = 1)
    {
        _host = host;
        _rng = rng;
        ExperienceMultiplier = experienceMultiplier;
    }

    public int ExperienceMultiplier { get; set; }
    public int ExperienceEventPercent { get; set; }

    public ulong ExpFor(ulong playerId) => _expByPlayer.TryGetValue(playerId, out var e) ? e : 0;

    // Skill effects that join normal attacks on this field (enchants, shields, reflection, evasion).
    internal IPlayerSkillCombat? SkillCombat { get; set; }

    public void StepPlayerAttack(WorldPlayer player)
    {
        ulong targetId = player.AttackTargetId;
        if (targetId == 0 || player.IsDead)
        {
            return;
        }

        if (_host.TryGetPlayer(targetId, out var foe))
        {
            StepAttackOnPlayer(player, foe);
            return;
        }

        if (!_host.TryGetMonster(targetId, out var monster) || !monster.IsAlive || monster.Effects.Has(ControlFlags.Charmed) || monster.IsSummon
            || OwnClan(player, monster))
        {
            player.ClearAttackTarget();
            return;
        }

        SwingAt(player, targetId, monster.Position);
    }

    private void StepAttackOnPlayer(WorldPlayer player, WorldPlayer foe)
    {
        if (!_host.MayFight(player, foe))
        {
            player.ClearAttackTarget();
            return;
        }

        SwingAt(player, foe.EntityId, foe.Position);
    }

    // When idle and in range, the swing begins. The client walks into range and reports it, as in the original; the lock
    // waits for that position.
    private void SwingAt(WorldPlayer player, ulong targetId, Vector3 target)
    {
        if (!player.MaySwing || !player.InAttackRange(target))
        {
            return;
        }

        (float durationMs, float hitAtMs) = _host.PlayerSwingTiming(player);
        player.BeginSwing(targetId, target, durationMs, hitAtMs);
        _host.EmitCombatEvent(new CombatEventView(player.EntityId, targetId, CombatEventKind.SwingBegin, durationMs, hitAtMs));
    }

    private static AttackProfile NormalAttack(WorldPlayer player) =>
        SkillFormulas.NormalAttack(player.Character) with { ExtraPercent = player.Character.Effects.Totals.Flat(StatKind.NextHitDamage) };

    // A player's blow on another still in range: the target's evasion, then the attacker's normal attack against the
    // target's defence, block and flat absorption, scaled by both sides' effects, with the attacker's enchants
    // (Athanasia's guard takes none of it).
    private void ResolveSwingOnPlayer(WorldPlayer player, WorldPlayer foe)
    {
        if (!_host.MayFight(player, foe) || !player.InAttackRange(foe.Position) || SkillCombat?.Evades(player.EntityId, foe) == true)
        {
            return;
        }

        var defender = CombatProfileFactory.ForPlayerDefender(foe.Character);
        var result = CombatResolver.Resolve(_rng, NormalAttack(player), defender, CombatProfileFactory.HasShield(foe.Character));
        if (result.Blocked)
        {
            _host.EmitCombatEvent(new CombatEventView(player.EntityId, foe.EntityId, CombatEventKind.Block));
        }

        if (result.Damage <= 0)
        {
            if (!result.Hit && !result.Blocked)
            {
                _host.EmitCombatEvent(new CombatEventView(player.EntityId, foe.EntityId, CombatEventKind.Miss));
            }

            return;
        }

        SkillCombat?.ConsumeNextHit(player);
        var damage = ModifierRules.ScaleDamage(result.Damage, player.Character.Effects.Totals, foe.Character.Effects.Totals);
        HurtPlayer(player, foe, damage + (SkillCombat?.EnchantOnPlayer(player, defender) ?? 0), result.Crit);
    }

    // A player's blow or skill reaches another player for this damage: the attacker's Might Stone, the target's absorbing
    // shields, then the life it takes (PlayerHurtPlayer) and the hit's event.
    public void HurtPlayer(WorldPlayer attacker, WorldPlayer foe, int damage, bool crit, bool skill = false)
    {
        damage = CastleItems.Strike(attacker.Character.Effects, foe.Character.Class.Class, damage);
        damage = SkillCombat?.DefendFromPlayer(attacker, foe, damage) ?? damage;
        var lifeBefore = foe.Life;
        foe.ApplyDamage(damage);
        if (lifeBefore > foe.Life)
        {
            PlayerHurtPlayer?.Invoke(attacker, foe, lifeBefore - foe.Life);
        }

        _host.EmitCombatEvent(new CombatEventView(attacker.EntityId, foe.EntityId, CombatEventKind.Hit, damage: lifeBefore - foe.Life, crit: crit,
            killed: foe.IsDead) { Skill = skill });
    }

    // A player's blow that took another's life, for the siege's board.
    public Action<WorldPlayer, WorldPlayer, int>? PlayerHurtPlayer { get; set; }

    public KillPayout? ResolvePlayerSwingHit(WorldPlayer player, ulong targetId, long nowMs)
    {
        if (player.IsDead)
            return null;

        if (_host.TryGetPlayer(targetId, out var foe))
        {
            ResolveSwingOnPlayer(player, foe);
            return null;
        }

        if (!_host.TryGetMonster(targetId, out var monster) || !monster.IsAlive)
            return null;

        AttackProfile atk = NormalAttack(player);
        DefenderProfile def = DefenderProfile.FromMonster(monster);
        HitResult result = CombatResolver.Resolve(_rng, atk, def, defenderHasShield: false);

        if (result.Damage <= 0)
        {
            if (!result.Hit)
            {
                _host.EmitCombatEvent(new CombatEventView(player.EntityId, targetId, CombatEventKind.Miss));
            }

            return null;
        }

        SkillCombat?.ConsumeNextHit(player);

        var damage = ModifierRules.ScaleDamage(result.Damage, player.Character.Effects.Totals, monster.Effects.Totals);
        damage = SkillCombat?.BasicStrike(player, monster, damage) ?? damage;
        damage = CombatResolver.CutHit(_rng, damage, monster.Def.Combat.MaxLife, monster.Def.Combat.Level, player.Character.Level);
        return ApplyPlayerDamage(player, monster, damage, result.Crit, nowMs);
    }

    // Shared by swings and skills: credits the hit, emits it and pays out a kill.
    // A summon's or familiar's blow names its own entity as the attacker and still credits the owner.
    // A player's own hit on a wild monster (not a familiar's or a summon's), for the effects that answer each attack.
    public Action<WorldPlayer, MonsterInstance, int>? PlayerHitLanded { get; set; }

    // Every hit of a player (or of its summon) with the life it took, for the siege's board.
    public Action<WorldPlayer, MonsterInstance, int>? DamageDealt { get; set; }

    // A monster of the player's clan (a clan pet, the castle's defenders for the owner clan) never hurts the player nor is
    // hurt by it.
    public static bool OwnClan(WorldPlayer player, MonsterInstance monster) => monster.ClanId != 0 && player.Clan?.ClanId == monster.ClanId;

    // skill: the hit is not the player's basic attack (a skill's, or a pet's strike), reported on its event.
    public KillPayout? ApplyPlayerDamage(WorldPlayer player, MonsterInstance monster, int damage, bool crit, long nowMs, bool forceFlinch = false,
        ulong sourceEntityId = 0, bool skill = false)
    {
        if (damage <= 0 || !monster.IsAlive || monster.DuelPlayerId != 0 && monster.DuelPlayerId != player.EntityId || OwnClan(player, monster))
        {
            return null;
        }

        bool monsterAtFullLife = monster.Life >= monster.Def.Combat.MaxLife;
        Log(monster.EntityId).RecordHit(player.EntityId, damage, monsterAtFullLife, nowMs);
        if (sourceEntityId == 0 && !monster.IsSummon)
        {
            PlayerHitLanded?.Invoke(player, monster, damage);
            FieldInstance.AgingAttack(player, monster, crit, nowMs);
        }

        var lifeBefore = monster.Life;
        monster.ApplyDamage(damage);
        DamageDealt?.Invoke(player, monster, Math.Min(damage, lifeBefore));
        SkillCombat?.MonsterDamaged(monster);

        MonsterAiBrain.ApplyBossRetargetOnHit(monster, sourceEntityId != 0 ? sourceEntityId : player.EntityId);

        _host.EmitCombatEvent(new CombatEventView(
            sourceEntityId != 0 ? sourceEntityId : player.EntityId, monster.EntityId, CombatEventKind.Hit,
            damage: damage, crit: crit, killed: !monster.IsAlive) { Skill = skill });

        if (!monster.IsAlive)
            return OnMonsterDeath(monster, nowMs);

        MonsterAiBrain.ApplyFlinchOnHit(
            monster, _rng, damage, crit || forceFlinch, _host.TuningFor(monster.Def).FlinchDurationMs);

        return null;
    }

    // Monster skill rules (curses, statuses, splash, reinforcements); a field always sets them.
    internal FieldMonsterSkills? MonsterSkills { get; set; }

    public void ResolveMonsterHit(MonsterInstance attacker, in MonsterAttackEvent landed)
    {
        if (!attacker.IsAlive || landed.AttackerEntityId != attacker.EntityId)
        {
            return;
        }

        if (landed.IsSkill)
        {
            MonsterSkills?.ResolveSkill(attacker, landed.TargetEntityId);
            return;
        }

        if (MonsterSkills?.ReplacesBlow(attacker) == true)
        {
            return;
        }

        if (_host.TryGetMonster(landed.TargetEntityId, out var summon) && summon.IsSummon)
        {
            MonsterOnMonster(attacker, summon, skill: false);
            return;
        }

        if (!_host.TryGetPlayer(landed.TargetEntityId, out var target) || target.IsDead
            || !MonsterAttackReach.CanLandBasic(attacker.Def, attacker.Position, target.Position))
        {
            return;
        }

        var (percent, splash) = MonsterSkills?.BeginBlow(attacker, target) ?? (100, false);
        BlowOnPlayer(attacker, target, percent, splash);
    }

    // A monster's normal blow on a player with percent of its damage roll; splash spreads Babel's blow around the target.
    internal void BlowOnPlayer(MonsterInstance attacker, WorldPlayer target, int percent, bool splash)
    {
        if (OwnClan(target, attacker))
        {
            return;
        }

        SkillCombat?.Reflect(attacker, target);
        if (!attacker.IsAlive || SkillCombat?.Evades(attacker.EntityId, target) == true)
        {
            return;
        }

        var combat = attacker.Def.Combat;
        if (percent != 100)
        {
            combat = combat with { AttackDamage = new MonsterRange(combat.AttackDamage.Min * percent / 100, combat.AttackDamage.Max * percent / 100) };
        }

        AttackProfile atk = CombatProfileFactory.ForMonsterBasicAttack(combat);
        DefenderProfile def = CombatProfileFactory.ForPlayerDefender(target.Character);
        HitResult result = CombatResolver.ResolveMonsterAttack(_rng, atk, def, CombatProfileFactory.HasShield(target.Character));
        if (result.Blocked)
        {
            _host.EmitCombatEvent(new CombatEventView(attacker.EntityId, target.EntityId, CombatEventKind.Block));
            SkillCombat?.Blocked(attacker, target);
            FieldInstance.AgingBlock(target, attacker);
        }

        var damage = 0;
        if (result.Damage > 0)
        {
            // Debuffs on the monster (damage dealt) and the player's buffs (damage taken, undead absorption) apply to
            // its normal attacks, as the original applies them before the hit reaches the player.
            damage = ModifierRules.ScaleDamage(result.Damage, attacker.Effects.Totals, target.Character.Effects.Totals);
            damage = ModifierRules.AgainstUndead(damage, attacker.Def.Brood == MonsterBrood.Undead, target.Character.Effects.Totals);
            damage = SkillCombat?.Defend(attacker, target, damage, false) ?? damage;
            damage = CastleItems.Defend(attacker.Def.SoundCode, target.Character.Effects, damage);
            target.ApplyDamage(damage);
            FieldInstance.AgingBlow(target, attacker);
            _host.EmitCombatEvent(new CombatEventView(
                attacker.EntityId, target.EntityId, CombatEventKind.Hit,
                damage: damage, crit: result.Crit, killed: target.IsDead));
            if (attacker.Def.Class == MonsterClass.Hammer)
            {
                HammerLanded?.Invoke(attacker, target);
            }
        }

        MonsterSkills?.AfterBlow(attacker, target, damage, splash);
    }

    // A wild monster's blow on a summon (FieldMonsterSkills.BlowOnMonster).
    internal void MonsterOnMonster(MonsterInstance attacker, MonsterInstance summon, bool skill)
    {
        if (!summon.IsAlive || FieldMonsterSkills.BlowOnMonster(_rng, attacker, summon, skill) is not { } damage)
        {
            return;
        }

        summon.ApplyDamage(damage);
        _host.EmitCombatEvent(new CombatEventView(attacker.EntityId, summon.EntityId, CombatEventKind.Hit, damage: damage, killed: !summon.IsAlive)
        {
            Skill = skill
        });
        if (!summon.IsAlive)
        {
            BeginCorpse(summon.EntityId);
        }
    }

    // A summon that died outside a player's kill (expiry, a monster's blow) leaves a corpse like any monster.
    public void BeginCorpse(ulong monsterId) => _corpseMsLeft.TryAdd(monsterId, CorpseDespawnMs);

    private KillPayout OnMonsterDeath(MonsterInstance monster, long nowMs)
    {
        MonsterDamageLog log = Log(monster.EntityId);

        // Kill credit: killer unless a latched first-attacker dealt ≥40% MaxLife and stayed engaged (15 s).
        ulong credited = log.KillerId;
        bool firstIsKiller = log.FirstAttackerId == log.KillerId;
        bool stillEngaged = (nowMs - log.FirstAttackerLastHitMs) <= KillReward.FirstAttackerDisengageMs;
        if (KillReward.FirstAttackerStealsCredit(
                firstIsKiller, log.FirstAttackerDamage, monster.Def.Combat.MaxLife, stillEngaged))
        {
            credited = log.FirstAttackerId;
        }

        ulong exp = 0;
        var creditedTier = -1;
        if (credited != 0 && _host.TryGetPlayer(credited, out var creditedPlayer))
        {
            creditedTier = creditedPlayer.Character.Tier;
            exp = creditedPlayer.Party is { } party
                ? AwardParty(monster, creditedPlayer, party)
                : Award(creditedPlayer, KillReward.ExpForKill(monster.Def.ExpReward, monster.Def.Combat.Level, creditedPlayer.Level,
                    ExperienceMultiplier, ExperienceEventPercent));
        }

        // The monster's event item goes to the credited player before the drop rolls.
        var eventItem = monster.Def.EventItem != 0 && creditedTier >= 0 && QuestDrops.EventItemDrops(_rng, monster.Def.EventItem, creditedTier);
        var dropBonus = credited != 0 && _host.TryGetPlayer(credited, out var looter) ? PremiumRules.ExtraDropChance(looter.Character.Premium) : 0;
        var level = credited != 0 && _host.TryGetPlayer(credited, out var eventPlayer) ? eventPlayer.Level : -1;
        Func<RolledDrop?>? events = (EventRates.Any || ActionField) && level >= 0 ? () => EventDrop(level, monster.Def.Combat.Level) : null;
        IReadOnlyList<RolledDrop> drops = monster.DropsDisabled ? [] : FinishDrops(monster,
            DropRoller.Roll(_rng, monster.Def.Drops, eventItem, monster.Def.EventItem, dropBonus, events));

        _damageLogs.Remove(monster.EntityId);
        _corpseMsLeft[monster.EntityId] = CorpseDespawnMs;

        return new KillPayout(monster.EntityId, credited, exp, drops);
    }

    // The kill's experience at the party's average level, shared by its size (PartyRules); members in other fields share
    // nothing. Answers the killer's award.
    private ulong AwardParty(MonsterInstance monster, WorldPlayer killer, Party party)
    {
        var exp = KillReward.ExpForKill(monster.Def.ExpReward, monster.Def.Combat.Level, party.AverageLevel, ExperienceMultiplier,
            ExperienceEventPercent);
        var (killerShare, otherShare) = PartyRules.ExpShare(party.Count);
        ulong killerAward = 0;
        foreach (var id in party.Members)
        {
            if (id == killer.EntityId)
            {
                killerAward = Award(killer, PartyRules.Share(exp, killerShare));
            }
            else if (_host.TryGetPlayer(id, out var member) && PartyRules.InShareRange(killer.Position, member.Position))
            {
                Award(member, PartyRules.Share(exp, otherShare));
            }
        }

        return killerAward;
    }

    // Each player's premium boosts its own part. A dead player gains nothing: its summons may still kill, and a level gained
    // lying dead would fill its life.
    private ulong Award(WorldPlayer player, ulong experience)
    {
        var exp = player.IsDead ? 0 : PremiumRules.BoostExperience(experience, player.Character.Premium);
        if (exp == 0)
        {
            return 0;
        }

        _expByPlayer.TryGetValue(player.EntityId, out var total);
        _expByPlayer[player.EntityId] = exp > ulong.MaxValue - total ? ulong.MaxValue : total + exp;
        player.Character.AwardExperience(exp);
        return exp;
    }

    // Items over their daily limit do not fall; the monster's own death gold falls on top of the table with its chance.
    private List<RolledDrop> FinishDrops(MonsterInstance monster, IReadOnlyList<RolledDrop> rolled)
    {
        var drops = new List<RolledDrop>(rolled.Count + 1);
        foreach (var drop in rolled)
        {
            if (drop.Kind != DropKind.Item || DropLedger?.TryTake(drop.ItemCode) != false)
            {
                drops.Add(drop);
            }
        }

        var gold = monster.Def.DeathGold;
        if (monster.Def.DeathGoldChance > 0 && gold.Max > 0 && _rng.RollPercent() < monster.Def.DeathGoldChance)
        {
            drops.Add(RolledDrop.Gold(_rng.Range(gold.Min, gold.Max)));
        }

        return drops;
    }

    // A hammer-class monster's blow landed on a player (the hammer event).
    public Action<MonsterInstance, WorldPlayer>? HammerLanded { get; set; }

    // The server-wide count of daily-limited drops; null leaves every item unlimited (tests).
    public DailyDropLedger? DropLedger { get; set; }

    // The server's item events (rice cakes, chocolates, puzzles, gift boxes); none run by default.
    public ItemEventRates EventRates { get; set; } = ItemEventRates.None;

    // Kills here roll the action field's event item.
    public bool ActionField { get; init; }

    private RolledDrop? EventDrop(int playerLevel, int monsterLevel) =>
        ItemEventDrops.Roll(_rng, EventRates, playerLevel, monsterLevel, ActionField) is { } drop && ItemCodes.TryParse(drop.CatalogKey, out var code)
            ? RolledDrop.Item(code) with { FromMainTable = true, Sealed = drop.Sealed }
            : null;

    public void TickCorpses(float dtMs)
    {
        if (_corpseMsLeft.Count == 0)
            return;

        _expiredScratch.Clear();
        foreach (var kv in _corpseMsLeft)
        {
            float left = kv.Value - dtMs;
            if (left <= 0f)
                _expiredScratch.Add(kv.Key);
            else
                _corpseMsLeft[kv.Key] = left;
        }

        for (int i = 0; i < _expiredScratch.Count; i++)
        {
            ulong id = _expiredScratch[i];
            _corpseMsLeft.Remove(id);
            _host.DespawnMonster(id);
        }
    }

    public void ForgetMonster(ulong entityId)
    {
        _damageLogs.Remove(entityId);
        _corpseMsLeft.Remove(entityId);
    }

    private MonsterDamageLog Log(ulong monsterId)
    {
        if (!_damageLogs.TryGetValue(monsterId, out var log))
        {
            log = new MonsterDamageLog();
            _damageLogs[monsterId] = log;
        }
        return log;
    }
}
