using System.Numerics;
using PristonTale.Engine3D.Rendering;
using PristonTale.Gameplay.Localization;
using PristonTale.Gameplay.UI.Widgets;
using PristonTale.Simulation.Progression;
using PristonTale.Application.World;
using PristonTale.Protocol.Binary;
using PristonTale.Protocol.Packets;
using PristonTale.Simulation.Characters;
using PristonTale.Simulation.Combat;
using PristonTale.Simulation.Items;
using PristonTale.Simulation.Modifiers;
using PristonTale.Simulation.Skills;
using PristonTale.Simulation.World.Ai;
using PristonTale.Simulation.World.Fields;
using PristonTale.Simulation.World.Monsters;
using PristonTale.Simulation.World.Spawning;
using PristonTale.Contracts.Motion;
using PristonTale.Contracts.World;
using PristonTale.Contracts.Combat;
using PristonTale.Contracts.Skills;
using PristonTale.Contracts.Characters;

namespace PristonTale.Tests;

internal static class SkillSystemTests
{
    public static IEnumerable<(string, Func<Task>)> Cases()
    {
        yield return ("skill rules cover all 32 first-tier skills of the eight original classes with consistent kinds", RulesAsync);
        yield return ("skill descriptions quote the next rank's gold price, castle tax and no rank beyond ten", TrainingPriceTooltipAsync);
        yield return ("skill cooldown follows the original mastery gauge from 0.49 s to 17.15 s", CooldownAsync);
        yield return ("skill formulas: weapon bonuses, magic power, area share, heal and buff rolls", FormulasAsync);
        yield return ("modifier engine stacks, refreshes, keeps the strongest, removes by source and feeds derived stats", ModifierEngineAsync);
        yield return ("modifier engine ticks periodic effects and expires timed ones but keeps permanent ones", PeriodicAsync);
        yield return ("regeneration follows the original per-second rules and the running drain", RegenerationAsync);
        yield return ("skill packets round-trip and reject malformed counts, phases and timers", PacketsAsync);
        yield return ("cast validation rejects unlearned, cooldown, mana, weapon, range, village and busy casts", ValidationAsync);
        yield return ("server casts a first-tier damage skill end to end on the motion timeline", DamageScenarioAsync);
        yield return ("server casts a buff and a stun, ticks their timers and removes them on expiry", BuffScenarioAsync);
        yield return ("skill and status events reach the client through the presence encoder", WireAsync);
    }

    private static readonly CharacterClassRegistry Classes = new();

    private static Task TrainingPriceTooltipAsync()
    {
        var previousLanguage = ClientText.Language;
        try
        {
            ClientText.Use(ClientSettings.EnglishLanguage);
            Assert.True(HudSkillsPanel.NextTrainingPrice(0, 0, 0) == "Rank 1: 500 gold", "first-rank price missing from skill description");
            Assert.True(HudSkillsPanel.NextTrainingPrice(0, 1, 0) == "Rank 2: 1,000 gold", "next-rank price did not rise");
            Assert.True(HudSkillsPanel.NextTrainingPrice(0, 0, 10) == "Rank 1: 550 gold", "castle tax is missing from skill description");
            Assert.True(HudSkillsPanel.NextTrainingPrice(0, 10, 0) == "", "a max-rank skill was offered another purchase");

            ClientText.Use(ClientSettings.BrazilianPortugueseLanguage);
            Assert.True(HudSkillsPanel.NextTrainingPrice(0, 0, 0) == "Rank 1: 500 de ouro", "Portuguese training price differs from the confirmation");
        }
        finally
        {
            ClientText.Use(previousLanguage);
        }

        return Task.CompletedTask;
    }

    private static Task RulesAsync()
    {
        var classes = new[]
        {
            PlayerClass.Mechanician, PlayerClass.Fighter, PlayerClass.Pikeman, PlayerClass.Archer, PlayerClass.Knight, PlayerClass.Atalanta,
            PlayerClass.Priestess, PlayerClass.Magician
        };
        var count = 0;
        foreach (var playerClass in classes)
        {
            foreach (var skill in ClassSkillCatalog.For(playerClass).Where(static skill => skill.Tier == JobTier.Job1).ToArray())
            {
                Assert.True(SkillRuleCatalog.TryGet(skill.Code, out var rule), $"{skill.Name} has no rule");
                Assert.True(rule.Name == skill.Name, $"rule name {rule.Name} disagrees with {skill.Name}");
                Assert.True((rule.Kind == SkillKind.Passive) == (skill.Category == SkillCategory.Passive), $"{skill.Name} passive mismatch");
                if (rule.Kind != SkillKind.Passive)
                {
                    Assert.True(rule.MasteryBase is >= 73 and <= 115, $"{skill.Name} mastery base outside the original first-tier range");
                    var timing = SkillFormulas.Timing(rule, 1, 1f, (800f, 400f));
                    Assert.True(timing.DurationMs > 0 && timing.HitsAtMs.Length > 0 && timing.HitsAtMs.All(hit => hit > 0 && hit <= timing.DurationMs + 1),
                        $"{skill.Name} timing is invalid");
                }

                count++;
            }
        }

        Assert.True(count == 32, $"expected 32 first-tier rules, got {count}");
        Assert.True(SkillRuleCatalog.TryGet(0x02010004, out var impact) && impact.MotionEvents.Length == 2, "Impact lost its second hit");
        Assert.True(SkillRuleCatalog.TryGet(0x02010003, out var raving) && raving.At(raving.Loops, 10) == 4, "Raving hits changed");
        Assert.True(SkillRuleCatalog.TryGet(0x08010004, out var fireBall) && fireBall.At(fireBall.Radius, 10) == 88
            && fireBall.Damage.Element == Element.Fire, "Fire Ball area or element changed");
        return Task.CompletedTask;
    }

    private static Task CooldownAsync()
    {
        SkillRuleCatalog.TryGet(0x08010002, out var fireBolt);
        Assert.True(SkillFormulas.CooldownMs(fireBolt, 1, 0, 0, 0) == 17150, "low mastery is not the gauge's longest fill");
        // Talent 150 gives proficiency 5000: M = 84 + 2 - 50 = 36 -> still the 36-70 bracket.
        Assert.True(SkillFormulas.CooldownMs(fireBolt, 1, 150, 0, 0) == 17150, "M 36 left the slowest bracket");
        Assert.True(SkillFormulas.CooldownMs(fireBolt, 1, 150, 0, 300) == 8330, "M 33 should fill in 8.33 s");
        Assert.True(SkillFormulas.CooldownMs(fireBolt, 1, 0, 50, 300) == 8330, "item mastery did not count toward proficiency");
        Assert.True(SkillFormulas.CooldownMs(fireBolt, 1, 150, 0, 5000) == 490, "full proficiency should reach the 0.49 s floor");
        Assert.True(SkillFormulas.UsesAfterCast(0, 5, 0, 1) == 100 && SkillFormulas.UsesAfterCast(0, 4, 0, 1) == 0, "use counter changed");
        Assert.True(SkillFormulas.UsesAfterCast(0, 6, 0, 4) == 100 && SkillFormulas.UsesAfterCast(0, 5, 0, 4) == 0, "rank did not lengthen the streak");
        Assert.True(SkillFormulas.UsesAfterCast(0, 9, 8, 1) == 100 && SkillFormulas.UsesAfterCast(0, 17, 15, 3) == 100, "window position streak changed");
        Assert.True(SkillFormulas.UsesAfterCast(9950, 10, 0, 1) == 10000, "proficiency exceeded its cap");
        Assert.True(SkillFormulas.Proficiency(0x08010002, 150, 10, 300) == 5300 && SkillFormulas.Proficiency(0x08010002, 0, 0, 12000) == 10000
            && SkillFormulas.Proficiency(0x08080002, 0, 0, 0) == 10000, "the training gauge's proficiency differs (fourth tier is always trained)");

        var mage = Classes.CreatePlayer(PlayerClass.Magician, "Trainer", 30);
        mage.Skills.Restore(0x08010002, 1);
        var revision = mage.Skills.Revision;
        mage.Skills.RecordUses(0x08010002, 0);
        Assert.True(mage.Skills.Revision == revision, "an unchanged use count raised the skills revision");
        mage.Skills.RecordUses(0x08010002, 100);
        Assert.True(mage.Skills.Revision == revision + 1 && OwnerState.Capture(mage).Skills.Single().Uses == 100,
            "a trained skill did not reach the owner's skill list");
        var buffer = new byte[64];
        var read = SkillsPacket.Read(buffer.AsSpan(0, new SkillsPacket(7, [(0x08010002u, 3, 4200)]).Write(buffer)));
        Assert.True(read.Revision == 7 && read.Skills.Single() == (0x08010002u, 3, 4200), "the skills packet lost the uses");
        return Task.CompletedTask;
    }

    private static Task FormulasAsync()
    {
        var rng = new CombatRng(5);
        var mage = Classes.CreatePlayer(PlayerClass.Magician, "Formula", 30);
        SkillRuleCatalog.TryGet(0x08010002, out var fireBolt);
        for (var roll = 0; roll < 200; roll++)
        {
            var power = SkillFormulas.RollPower(mage, fireBolt, 10, rng);
            var spirit = mage.Attributes.Spirit;
            var weapon = SkillFormulas.WeaponItemDamage(mage);
            Assert.True(power >= 36 + weapon.Min / 3 + spirit / 8 && power <= 56 + weapon.Max / 3 + spirit / 6, $"fire bolt power {power} outside table");
        }

        var monster = Monster(MonsterBrood.Undead, absorption: 50);
        SkillRuleCatalog.TryGet(0x07010002, out var holyBolt);
        var dealt = SkillFormulas.PowerAgainst(100, holyBolt, monster, ModifierTotals.Empty, ModifierTotals.Empty);
        Assert.True(dealt == 100, $"undead double then 50% absorb should give 100, got {dealt}");
        var fighter = Classes.CreatePlayer(PlayerClass.Fighter, "Formula", 30);
        SkillRuleCatalog.TryGet(0x02010004, out var impact);
        var attack = SkillFormulas.WeaponAttack(fighter, impact, 3, monster, rng);
        var basic = CombatProfileFactory.ForPlayerBasicAttack(fighter);
        Assert.True(attack.AttackRating == basic.AttackRating + 60 && attack.SkillMultiplierPercent == 29, "Impact accuracy or damage bonus wrong");
        SkillRuleCatalog.TryGet(0x05010004, out var crash);
        Assert.True(SkillFormulas.WeaponAttack(fighter, crash, 10, monster, rng).CritChance == basic.CritChance + 7, "Double Crash critical bonus lost");
        var priest = Classes.CreatePlayer(PlayerClass.Priestess, "Formula", 30);
        SkillRuleCatalog.TryGet(0x07010001, out var heal);
        var healed = SkillFormulas.RollHeal(priest, heal, 1, rng);
        Assert.True(healed >= 64 + priest.Attributes.Spirit / 8, "heal ignored its table");
        SkillRuleCatalog.TryGet(0x01010003, out var absorb);
        var modifiers = SkillFormulas.BuffModifiers(absorb, 10, rng);
        Assert.True(modifiers.Length == 1 && modifiers[0].Stat == StatKind.Absorption && modifiers[0].Value is >= 7 and <= 13, "absorb roll range");
        SkillRuleCatalog.TryGet(0x07010004, out var holyMind);
        Assert.True(SkillFormulas.BuffDurationMs(holyMind, 1, 40) == 9000f, "bionic resistance did not shorten Holy Mind");
        return Task.CompletedTask;
    }

    private static Task ModifierEngineAsync()
    {
        var container = new EffectContainer();
        var block = new EffectSpec(1, EffectSourceKind.PlayerSkill, false, [StatModifier.Flat(StatKind.BlockChance, 5)]);
        Assert.True(container.Apply(block, 10, 1000f)?.Kind == EffectChangeKind.Added, "add");
        Assert.True(container.Apply(new EffectSpec(1, EffectSourceKind.PlayerSkill, false, [StatModifier.Flat(StatKind.BlockChance, 8)]), 10, 1000f)?.Kind
            == EffectChangeKind.Refreshed && container.Totals.Flat(StatKind.BlockChance) == 8, "refresh did not take the new magnitude");
        var poison = new EffectSpec(2, EffectSourceKind.MonsterSkill, true, [StatModifier.Percent(StatKind.MoveSpeed, -20)], stacking: StackRule.Stack,
            maxStacks: 3);
        for (var index = 0; index < 5; index++)
        {
            container.Apply(poison, 20, 1000f);
        }

        Assert.True(container.Totals.Percent(StatKind.MoveSpeed) == -60, "stacks were not capped at three");
        var strong = new EffectSpec(3, EffectSourceKind.Consumable, false, [StatModifier.Flat(StatKind.Defence, 50)], stacking: StackRule.KeepStrongest);
        var weak = new EffectSpec(3, EffectSourceKind.Consumable, false, [StatModifier.Flat(StatKind.Defence, 10)], stacking: StackRule.KeepStrongest);
        container.Apply(strong, 0, 1000f);
        Assert.True(container.Apply(weak, 0, 1000f) is null && container.Totals.Flat(StatKind.Defence) == 50, "weaker effect replaced stronger");
        var mind = new EffectSpec(4, EffectSourceKind.PlayerSkill, true, [StatModifier.Percent(StatKind.DamageDealt, -10)], stacking: StackRule.PerSource);
        container.Apply(mind, 100, 1000f);
        container.Apply(mind, 101, 1000f);
        Assert.True(container.Totals.Percent(StatKind.DamageDealt) == -20, "per-source copies merged");

        var player = Classes.CreatePlayer(PlayerClass.Knight, "Modifiers", 20);
        var baseLife = player.MaxLife;
        var ring = new EffectSpec(0x0A000001, EffectSourceKind.Item, false, [StatModifier.Percent(StatKind.MaxLife, 10), StatModifier.Flat(StatKind.MaxLife, 5)]);
        player.Effects.Apply(ring, 77, float.PositiveInfinity);
        Assert.True(player.MaxLife == (baseLife + 5) * 110 / 100, $"derived max life ignored the item modifier: {player.MaxLife}");
        var changes = new List<EffectChange>();
        player.Effects.Step(60000f, 1, changes);
        Assert.True(player.Effects.Count == 1 && changes.Count == 0, "permanent effect expired");
        Assert.True(player.Effects.RemoveFrom(EffectSourceKind.Item, 77, 1, changes) == 1 && player.MaxLife == baseLife, "unequip did not restore stats");
        Assert.True(player.Life <= player.MaxLife, "life stayed above the lowered maximum");
        return Task.CompletedTask;
    }

    private static Task PeriodicAsync()
    {
        var container = new EffectContainer();
        var drain = new EffectSpec(9, EffectSourceKind.MonsterSkill, true, [], [new PeriodicEffect(ResourceKind.Mana, -3, 500f)],
            ControlFlags.Slow);
        container.Apply(drain, 5, 2000f);
        var changes = new List<EffectChange>();
        for (var step = 0; step < 50; step++)
        {
            container.Step(50f, 1, changes);
        }

        var ticks = changes.Count(change => change.Kind == EffectChangeKind.Ticked);
        Assert.True(ticks == 4 && changes.Count(change => change.Kind == EffectChangeKind.Removed) == 1, $"periodic ticks {ticks}");
        Assert.True(changes.First(change => change.Kind == EffectChangeKind.Ticked).Periodic.Amount == -3, "periodic value lost");
        Assert.True(container.Count == 0 && !container.Has(ControlFlags.Slow), "control state survived expiry");
        return Task.CompletedTask;
    }

    private static Task RegenerationAsync()
    {
        var fighter = Classes.CreatePlayer(PlayerClass.Fighter, "Regen", 14);
        var priest = Classes.CreatePlayer(PlayerClass.Priestess, "Regen", 14);
        var standing = Regeneration.StaminaPerSecond(fighter, RegenerationPosture.Standing);
        Assert.Near((3.8f + 2f) * 17.5f / 17f, standing, 0.001f);
        Assert.Near(standing * 0.6f, Regeneration.StaminaPerSecond(fighter, RegenerationPosture.Walking), 0.001f);
        Assert.True(Regeneration.StaminaPerSecond(fighter, RegenerationPosture.Running) < 0f, "running did not drain stamina");
        Assert.True(Regeneration.StaminaPerSecond(priest, RegenerationPosture.Standing) < standing, "priestess used the fighter's stamina function");
        priest.Mana = 0;
        priest.Stamina = 0;
        var state = new RegenerationState();
        for (var tick = 0; tick < 20 * 60; tick++)
        {
            state.Step(priest, RegenerationPosture.Standing, 50f);
        }

        var expectedMana = Regeneration.ManaPerSecond(priest) * 60f;
        Assert.True(Math.Abs(priest.Mana - expectedMana) <= 1.01f, $"mana after a minute {priest.Mana}, expected {expectedMana}");
        var regen = new EffectSpec(5, EffectSourceKind.Item, false, [StatModifier.Flat(StatKind.ManaRegen, 20)]);
        priest.Effects.Apply(regen, 0, float.PositiveInfinity);
        Assert.Near(expectedMana / 60f + 2f * 17.5f / 17f, Regeneration.ManaPerSecond(priest), 0.001f);
        return Task.CompletedTask;
    }

    private static Task PacketsAsync()
    {
        var buffer = new byte[FrameCodec.MaxFrameSize];
        var cast = new SkillCastPacket(7, 0x08010002, 99, 256, -512, 1024, 300, 12345);
        Assert.True(SkillCastPacket.Read(buffer.AsSpan(0, cast.Write(buffer))) == cast, "cast round trip");
        var result = new SkillCastResultPacket(7, 0x08010002, SkillCastVerdict.Cooldown, 3, 17150, 900, 40, 70, 88);
        Assert.True(SkillCastResultPacket.Read(buffer.AsSpan(0, result.Write(buffer))) == result, "cast result round trip");
        SkillEventEntry[] events =
        [
            new(1, 2, 0x08010004, SkillEventPhase.Start, 4, SkillEventFlags.Area | SkillEventFlags.Projectile, 714f, 414.5f, 320, 640, -960, 1024, 0, 76),
            new(1, 2, 0x08010004, SkillEventPhase.Impact, 4, SkillEventFlags.Area, 0f, 0f, 320, 640, -960, 1024, 3, 76)
        ];
        var length = SkillEventList.Write(buffer, events, NoEventHandles.Instance, out _);
        Assert.True(SkillEventList.Read(buffer.AsSpan(0, length), NoEventHandles.Instance).SequenceEqual(events), "skill events round trip");
        StatusEffectEntry[] statuses =
        [
            new(2, 1, 0x06010001, StatusEffectOp.Add, 1, (byte)EffectSourceKind.PlayerSkill, (byte)ControlFlags.Stun, StatusEffectFlags.Harmful, 10000, 10000, 0),
            new(1, 0, 0x0A000001, StatusEffectOp.Remove, 1, (byte)EffectSourceKind.Item, 0, StatusEffectFlags.Permanent, 0, 0, 5)
        ];
        length = StatusEffectList.Write(buffer, statuses, NoEventHandles.Instance, out _);
        Assert.True(StatusEffectList.Read(buffer.AsSpan(0, length), NoEventHandles.Instance).SequenceEqual(statuses), "status round trip");
        var cooldowns = new SkillCooldownsPacket(55, [new SkillCooldownEntry(0x08010002, 1000, 17150)]);
        var read = SkillCooldownsPacket.Read(buffer.AsSpan(0, cooldowns.Write(buffer)));
        Assert.True(read.ServerTick == 55 && read.Cooldowns.SequenceEqual(cooldowns.Cooldowns), "cooldowns round trip");

        length = SkillEventList.Write(buffer, events, NoEventHandles.Instance, out _);
        buffer[EventWire.HeaderSize] = 9;
        Assert.Throws<InvalidDataException>(() => SkillEventList.Read(buffer.AsSpan(0, length).ToArray(), NoEventHandles.Instance), "a malformed SkillEventList was read");
        length = StatusEffectList.Write(buffer, [statuses[0] with { RemainingMs = 20000 }], NoEventHandles.Instance, out _);
        Assert.Throws<InvalidDataException>(() => StatusEffectList.Read(buffer.AsSpan(0, length).ToArray(), NoEventHandles.Instance), "a malformed StatusEffectList was read");
        length = SkillEventList.Write(buffer, events, NoEventHandles.Instance, out _);
        BitConverter.TryWriteBytes(buffer.AsSpan(EventWire.HeaderSize - 2), (ushort)3);
        Assert.Throws<InvalidDataException>(() => SkillEventList.Read(buffer.AsSpan(0, length).ToArray(), NoEventHandles.Instance), "a malformed SkillEventList was read");
        return Task.CompletedTask;
    }

    private static Task ValidationAsync()
    {
        var (field, player, monster) = Scenario(PlayerClass.Magician, Staff());
        var request = new SkillCastRequest(1, 0x08010002, monster.EntityId, default, false);
        Assert.True(field.CastSkill(player, request).Verdict == SkillCastVerdict.NotLearned, "unlearned cast accepted");
        player.Character.Skills.Restore(0x08010002, 1);
        player.Character.Skills.Restore(0x08010001, 1);
        Assert.True(field.CastSkill(player, request with { SkillCode = 0x01010001 }).Verdict == SkillCastVerdict.NotCastable, "other class skill cast");
        player.Character.Mana = 0;
        Assert.True(field.CastSkill(player, request).Verdict == SkillCastVerdict.NotEnoughMana, "cast without mana");
        player.Character.Mana = player.Character.MaxMana;
        var bare = Scenario(PlayerClass.Magician, null);
        bare.Player.Character.Skills.Restore(0x08010002, 1);
        Assert.True(bare.Field.CastSkill(bare.Player, request with { TargetId = bare.Monster.EntityId }).Verdict == SkillCastVerdict.WeaponNotAllowed,
            "staff skill cast bare-handed");
        monster.Position = new Vector3(5000, 0, 0);
        Assert.True(field.CastSkill(player, request).Verdict == SkillCastVerdict.OutOfRange, "out of range accepted");
        Assert.True(field.CastSkill(player, request with { TargetId = 4242 }).Verdict == SkillCastVerdict.InvalidTarget, "missing target accepted");
        monster.Position = new Vector3(120, 0, 0);
        var accepted = field.CastSkill(player, request);
        Assert.True(accepted.Accepted && accepted.CooldownMs == 17150 && player.IsCasting, $"valid cast refused: {accepted.Verdict}");
        Assert.True(field.CastSkill(player, request).Verdict == SkillCastVerdict.Busy, "second cast during the motion accepted");
        for (var tick = 0; tick < 40; tick++)
        {
            field.Step(0.05f);
        }

        var cooldown = field.CastSkill(player, request);
        Assert.True(cooldown.Verdict == SkillCastVerdict.Cooldown && cooldown.RemainingMs > 0, "cooldown not enforced on the server");
        Assert.True(field.CastSkill(player, request with { SkillCode = 0x08010001 }).Accepted, "Agony blocked by another skill's cooldown");
        var village = new WorldPlayer(50, Vector3.Zero, player.Character, true, default);
        field.Add(village);
        Assert.True(field.CastSkill(village, request with { SkillCode = 0x08010003 }).Verdict == SkillCastVerdict.InVillage, "cast inside a village");
        return Task.CompletedTask;
    }

    private static Task DamageScenarioAsync()
    {
        var (field, player, monster) = Scenario(PlayerClass.Magician, Staff());
        player.Character.Skills.Restore(0x08010002, 5);
        var manaBefore = player.Character.Mana;
        var outcome = field.CastSkill(player, new SkillCastRequest(1, 0x08010002, monster.EntityId, default, false));
        Assert.True(outcome.Accepted && player.Character.Mana == manaBefore - 25, "mana not spent on the server");
        field.Step(0.05f);
        var start = field.SnapshotSkillEvents().Single();
        Assert.True(start.Phase == SkillEventPhase.Start && start.HitAtMs > 0 && start.DurationMs > start.HitAtMs, "start timing missing");
        var lifeBefore = monster.Life;
        var impactTick = -1;
        var hits = new List<CombatEventView>();
        for (var tick = 1; tick < 60 && impactTick < 0; tick++)
        {
            field.Step(0.05f);
            hits.AddRange((field.SnapshotEvents() ?? []).Where(value => value.Kind == CombatEventKind.Hit && value.AttackerId == player.EntityId));
            if (field.SnapshotSkillEvents().Any(value => value.Phase == SkillEventPhase.Impact))
            {
                impactTick = tick;
            }
        }

        var launchMs = start.HitAtMs;
        var flight = SkillProjectile.FlightMs(120f);
        Assert.True(impactTick > 0 && impactTick * 50f >= launchMs + flight - 50f, $"impact at tick {impactTick} before launch {launchMs} + flight {flight}");
        Assert.True(hits.Count == 1 && monster.Life == lifeBefore - hits[0].Damage && hits[0].Damage > 0, "bolt damage did not land exactly once");
        return Task.CompletedTask;
    }

    private static Task BuffScenarioAsync()
    {
        var (field, player, monster) = Scenario(PlayerClass.Mechanician, null);
        player.Character.Skills.Restore(0x01010003, 2);
        var absorption = player.Character.Derived.Absorption;
        Assert.True(field.CastSkill(player, new SkillCastRequest(1, 0x01010003, 0, default, false)).Accepted, "Physical Absorption refused");
        var added = new List<StatusEventView>();
        var removed = new List<StatusEventView>();
        for (var tick = 0; tick < 20 * 60; tick++)
        {
            field.Step(0.05f);
            added.AddRange(field.SnapshotStatusEvents().Where(value => value.Op == StatusEffectOp.Add));
            removed.AddRange(field.SnapshotStatusEvents().Where(value => value.Op == StatusEffectOp.Remove));
            if (tick == 100)
            {
                Assert.True(player.Character.Derived.Absorption >= absorption + 3, "absorb buff did not change derived stats");
            }
        }

        Assert.True(added.Count == 1 && added[0].StatusCode == 0x01010003 && added[0].DurationMs == 50000 && !added[0].Harmful, "buff add event wrong");
        Assert.True(removed.Count == 1 && player.Character.Effects.Count == 0 && player.Character.Derived.Absorption == absorption, "buff did not expire");

        var (strikeField, atalanta, target) = Scenario(PlayerClass.Atalanta, Shield());
        atalanta.Character.Skills.Restore(0x06010001, 1);
        target.Position = new Vector3(0, 0, 80);
        Assert.True(strikeField.CastSkill(atalanta, new SkillCastRequest(1, 0x06010001, target.EntityId, default, false)).Accepted, "Shield Strike refused");
        var stunned = false;
        for (var tick = 0; tick < 40; tick++)
        {
            strikeField.Step(0.05f);
            stunned |= target.Effects.Has(ControlFlags.Stun);
        }

        Assert.True(stunned && target.Life < target.Def.Combat.MaxLife, "Shield Strike did not damage and stun");
        Assert.True(target.Motion == MotionState.Stand && target.AiState != MonsterAiState.Attack, "stunned monster kept acting");
        return Task.CompletedTask;
    }

    private static Task WireAsync()
    {
        var round = new WireRoundTrip(1);
        var view = new EntityView(1, EntityKind.Player, true, Vector3.Zero, 0, MotionState.Skill, 10, 100, 100, default);
        var snapshot = new WorldSnapshot(5, [view], fieldId: 3)
        {
            SkillEvents = [new SkillEventView(1, 1, 0x05010002, SkillEventPhase.Start, 2, SkillEventFlags.SelfCast, 900f, 400f, Vector3.Zero, 0f, 0, 0f)],
            StatusEvents = [new StatusEventView(1, 1, 0x05010002, StatusEffectOp.Add, 1, 2, 0, false, false, 5000, 5000, 12)],
            Cooldowns = [new SkillCooldownView(0x05010002, 17000, 17150)]
        };
        var updates = round.Push(snapshot);
        var skill = updates.SelectMany(update => update.SkillEvents).Single();
        var status = updates.SelectMany(update => update.StatusEffects).Single();
        var cooldowns = updates.Select(update => update.Cooldowns).Single(value => value is not null)!;
        Assert.True(skill.SkillCode == 0x05010002 && skill.HitAtMs == 400f, "skill event lost on the wire");
        Assert.True(status.StatusCode == 0x05010002 && status.RemainingMs == 5000 && status.Value == 12, "status lost on the wire");
        Assert.True(cooldowns.Cooldowns.Single().RemainingMs == 17000, "cooldowns lost on the wire");
        Assert.True(updates.Where(update => update.SkillEvents.Count > 0).All(update => update.ServerTimeMs == 250), "skill event left the presence timeline");
        return Task.CompletedTask;
    }

    private static MonsterDefinition Monster(MonsterBrood brood = MonsterBrood.Normal, int absorption = 0) => new(
        1, "Skill fixture", MonsterFaction.Enemy, MonsterClass.Normal, brood,
        new MonsterCombatStats(1, 5000, new MonsterRange(1, 1), 6, 30, 0, absorption, 0, 1000, ResistanceBlock.Zero), "")
    {
        Ai = new MonsterAi(6, MonsterNature.Evil, 200, 1, 2000, MonsterRange.Zero, MonsterActiveHour.Any)
    };

    private static (FieldInstance Field, WorldPlayer Player, MonsterInstance Monster) Scenario(PlayerClass playerClass, ItemInstance? equipment)
    {
        var definition = Monster();
        var field = new FieldInstance(3, FieldSpawnData.Empty, new MonsterCatalog([definition]), new EntityIdAllocator(), new CombatRng(11));
        var character = Classes.CreatePlayer(playerClass, "Caster", 20);
        if (equipment is not null)
        {
            character.Equip(equipment.Definition.EquipSlot!.Value, equipment);
        }

        var player = new WorldPlayer(1, Vector3.Zero, character, false, default);
        field.Add(player);
        var monster = ((ISpawnSink)field).Spawn(definition, new Vector3(120, 0, 0), 0, 0);
        return (field, player, monster);
    }

    private static ItemInstance Staff() => new(new ItemDefinition(0x0100_0010, "Staff", ItemKind.Staff, EquipmentSlot.Weapon)
    {
        CatalogKey = "WM101",
        BaseStats = new ItemStatBlock(4, 8, 0, 0, 0, 0, 0, 0, 0, 0)
    });

    private static ItemInstance Shield() => new(new ItemDefinition(0x0100_0011, "Shield", ItemKind.Shield, EquipmentSlot.OffHand) { CatalogKey = "DS101" });
}
