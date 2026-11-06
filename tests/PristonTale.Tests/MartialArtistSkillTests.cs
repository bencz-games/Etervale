using System.Numerics;
using PristonTale.Application.World;
using PristonTale.Simulation.Characters;
using PristonTale.Simulation.Combat;
using PristonTale.Simulation.Items;
using PristonTale.Simulation.Modifiers;
using PristonTale.Simulation.Skills;
using PristonTale.Simulation.World.Monsters;
using PristonTale.Contracts.Characters;
using PristonTale.Contracts.Combat;

namespace PristonTale.Tests;

// The Martial Artist's skills that need more than their table: the leap that cannot miss, the second smash that also
// strikes around its target, the shout that weakens the monsters around it and the training that raises the attributes
// of the caster and its party.
internal static class MartialArtistSkillTests
{
    private const uint LowKick = 0x20010001;
    private const uint WarCry = 0x20040003;
    private const uint LeapSmash = 0x20040004;
    private const uint NervesOfSteel = 0x20080002;
    private const uint LineBreak = 0x20100003;
    private const uint HardTraining = 0x20100004;

    public static IEnumerable<(string, Func<Task>)> Cases()
    {
        yield return ("low kick adds its table to the kick, 30% more against mutant and mechanic monsters", LowKickAsync);
        yield return ("leap smash strikes as it leaps without an accuracy roll, then on landing", LeapSmashAsync);
        yield return ("line break strikes its target twice and the enemies around it with the second strike only", LineBreakAsync);
        yield return ("war cry weakens the monsters around its target for 15 s, turned away only by a full bionic resistance, never stacked or restarted", WarCryAsync);
        yield return ("nerves of steel raises defence and absorption", NervesOfSteelAsync);
        yield return ("hard training raises the strength, agility and spirit the formulas read, for the caster and its party in range", HardTrainingAsync);
    }

    private static Task LowKickAsync()
    {
        SkillRuleCatalog.TryGet(LowKick, out var rule);
        Assert.True(rule.At(rule.Damage.Min, 10) == 48 && rule.At(rule.Damage.Max, 10) == 68, "low kick lost its table");
        Assert.True(SkillFormulas.WithBroodBonus(100, rule, SkillFixtures.Monster(MonsterBrood.Mechanic), 10) == 130
            && SkillFormulas.WithBroodBonus(100, rule, SkillFixtures.Monster(MonsterBrood.Mutant), 10) == 130
            && SkillFormulas.WithBroodBonus(100, rule, SkillFixtures.Monster(MonsterBrood.Normal), 10) == 100,
            "low kick's bonus does not fall on mutant and mechanic monsters alone");
        return Task.CompletedTask;
    }

    // A monster the caster's accuracy can never reach: only a hit without the roll lands.
    private static MonsterDefinition Untouchable() => SkillFixtures.Monster(life: 1_000_000) is var monster
        ? monster with { Combat = monster.Combat with { Defence = 1_000_000 } }
        : monster;

    private static Task LeapSmashAsync()
    {
        var definition = Untouchable();
        var field = SkillFixtures.Field(71, definition);
        var martial = SkillFixtures.Player(field, 1, PlayerClass.MartialArtist, 90, Vector3.Zero, SkillFixtures.Weapon(ItemKind.Vambrace));
        martial.Character.Effects.SetPermanent(0xFFFF0001, EffectSourceKind.System, false, [StatModifier.Flat(StatKind.MaxMana, 500)]);
        martial.Character.Skills.Restore(LeapSmash, 10);
        var monster = SkillFixtures.Spawn(field, definition, new Vector3(30, 0, 0));
        SkillFixtures.Accept("Leap Smash", field, martial, LeapSmash, monster.EntityId);
        SkillFixtures.Finish(field, martial);
        Assert.True(monster.Life < monster.Def.Combat.MaxLife, "the leap's strike rolled accuracy against a monster no accuracy reaches");
        return Task.CompletedTask;
    }

    private static Task LineBreakAsync()
    {
        var definition = SkillFixtures.Monster(life: 1_000_000);
        var field = SkillFixtures.Field(71, definition);
        var martial = SkillFixtures.Player(field, 1, PlayerClass.MartialArtist, 90, Vector3.Zero, SkillFixtures.Weapon(ItemKind.Vambrace));
        martial.Character.Effects.SetPermanent(0xFFFF0001, EffectSourceKind.System, false, [StatModifier.Flat(StatKind.MaxMana, 500)]);
        martial.Character.Skills.Restore(LineBreak, 10);
        var target = SkillFixtures.Spawn(field, definition, new Vector3(30, 0, 0));
        var near = SkillFixtures.Spawn(field, definition, new Vector3(30, 0, 100));
        var far = SkillFixtures.Spawn(field, definition, new Vector3(30, 0, 400));
        martial.Character.Effects.SetPermanent(0xFFFF0002, EffectSourceKind.System, false, [StatModifier.Flat(StatKind.AttackRating, 1_000_000)]);
        SkillFixtures.Accept("Line Break", field, martial, LineBreak, target.EntityId);
        var targetHits = 0;
        var nearHits = 0;
        var nearBeforeSecond = false;
        for (var tick = 0; tick < 200 && (martial.IsCasting || tick < 20); tick++)
        {
            // The monsters stay where they stand, the target within the kick's reach.
            (target.Position, near.Position, far.Position) = (new Vector3(30, 0, 0), new Vector3(30, 0, 100), new Vector3(30, 0, 400));
            field.Step(0.05f);
            foreach (var hit in field.SnapshotEvents() ?? [])
            {
                if (hit.Kind != CombatEventKind.Hit && hit.Kind != CombatEventKind.Miss)
                {
                    continue;
                }

                targetHits += hit.TargetId == target.EntityId ? 1 : 0;
                if (hit.TargetId == near.EntityId)
                {
                    nearHits++;
                    nearBeforeSecond |= targetHits < 2;
                }
            }
        }

        Assert.True(targetHits >= 2 && nearHits == 1 && !nearBeforeSecond && far.Life == far.Def.Combat.MaxLife,
            $"line break struck its target {targetHits} times and the enemy near it {nearHits} times (before the second strike: {nearBeforeSecond})");
        return Task.CompletedTask;
    }

    private static Task WarCryAsync()
    {
        var definition = SkillFixtures.Monster(life: 1_000_000);
        var resistant = SkillFixtures.Monster(life: 1_000_000, resistances: ResistanceBlock.Zero with { Bionic = (short)100 }) with { Code = 8 };
        var halfResistant = SkillFixtures.Monster(life: 1_000_000, resistances: ResistanceBlock.Zero with { Bionic = (short)50 }) with { Code = 9 };
        var field = SkillFixtures.Field(71, definition, resistant, halfResistant);
        var martial = SkillFixtures.Player(field, 1, PlayerClass.MartialArtist, 90, Vector3.Zero, SkillFixtures.Weapon(ItemKind.Vambrace));
        martial.Character.Effects.SetPermanent(0xFFFF0001, EffectSourceKind.System, false, [StatModifier.Flat(StatKind.MaxMana, 500)]);
        martial.Character.Skills.Restore(WarCry, 10);
        var target = SkillFixtures.Spawn(field, definition, new Vector3(30, 0, 0));
        var full = SkillFixtures.Spawn(field, resistant, new Vector3(30, 0, 40));
        var half = SkillFixtures.Spawn(field, halfResistant, new Vector3(30, 0, -40));
        SkillFixtures.Accept("War Cry", field, martial, WarCry, target.EntityId);
        SkillFixtures.Finish(field, martial);
        var totals = target.Effects.Totals;
        Assert.True(totals.Percent(StatKind.DamageDealt) == -34 && totals.Flat(StatKind.CriticalChanceTaken) == 12 && target.Life == target.Def.Combat.MaxLife,
            "war cry did not weaken its target, or it dealt damage");
        Assert.True(full.Effects.Totals.Percent(StatKind.DamageDealt) == 0, "a full bionic resistance did not turn war cry away");
        Assert.True(half.Effects.Totals.Percent(StatKind.DamageDealt) == -34, "a partial bionic resistance turned war cry away");
        SkillFixtures.Wait(field, 10f);
        Assert.True(half.Effects.Totals.Percent(StatKind.DamageDealt) == -34, "a partial bionic resistance shortened war cry");

        // Another Martial Artist's War Cry on the weakened monster neither stacks nor restarts the running one.
        var other = SkillFixtures.Player(field, 2, PlayerClass.MartialArtist, 90, Vector3.Zero, SkillFixtures.Weapon(ItemKind.Vambrace));
        other.Character.Effects.SetPermanent(0xFFFF0001, EffectSourceKind.System, false, [StatModifier.Flat(StatKind.MaxMana, 500)]);
        other.Character.Skills.Restore(WarCry, 10);
        target.Position = new Vector3(30, 0, 0);
        SkillFixtures.Accept("a second War Cry", field, other, WarCry, target.EntityId);
        SkillFixtures.Finish(field, other);
        Assert.True(target.Effects.Totals.Percent(StatKind.DamageDealt) == -34, "a second war cry stacked on the weakened monster");
        SkillFixtures.Wait(field, 6f);
        Assert.True(target.Effects.Totals.Percent(StatKind.DamageDealt) == 0, "war cry outlasted its 15 s, or a second cast restarted it");
        return Task.CompletedTask;
    }

    private static Task NervesOfSteelAsync()
    {
        var field = SkillFixtures.Field();
        var martial = SkillFixtures.Player(field, 1, PlayerClass.MartialArtist, 90, Vector3.Zero, SkillFixtures.Weapon(ItemKind.Vambrace));
        martial.Character.Effects.SetPermanent(0xFFFF0001, EffectSourceKind.System, false, [StatModifier.Flat(StatKind.MaxMana, 500)]);
        martial.Character.Skills.Restore(NervesOfSteel, 10);
        var (defence, absorption) = (martial.Character.Derived.Defence, martial.Character.Derived.Absorption);
        SkillFixtures.Accept("Nerves of Steel", field, martial, NervesOfSteel);
        SkillFixtures.Finish(field, martial);
        var totals = martial.Character.Effects.Totals;
        Assert.True(totals.Percent(StatKind.Defence) == 59 && totals.Flat(StatKind.Absorption) == 23
            && martial.Character.Derived.Defence > defence && martial.Character.Derived.Absorption > absorption,
            "nerves of steel did not raise defence 59% and absorption by 23");
        return Task.CompletedTask;
    }

    private static Task HardTrainingAsync()
    {
        var field = SkillFixtures.Field();
        var martial = SkillFixtures.Player(field, 1, PlayerClass.MartialArtist, 90, Vector3.Zero, SkillFixtures.Weapon(ItemKind.Vambrace, 40, 60));
        var member = SkillFixtures.Player(field, 2, PlayerClass.Fighter, 90, new Vector3(150, 0, 0));
        var distant = SkillFixtures.Player(field, 3, PlayerClass.Fighter, 90, new Vector3(600, 0, 0));
        SkillFixtures.Group(martial, member, distant);
        martial.Character.Effects.SetPermanent(0xFFFF0001, EffectSourceKind.System, false, [StatModifier.Flat(StatKind.MaxMana, 500)]);
        martial.Character.Skills.Restore(HardTraining, 10);
        var attributes = martial.Character.Attributes;
        var damage = WeaponDamage.Compute(martial.Character);
        SkillFixtures.Accept("Hard Training", field, martial, HardTraining);
        SkillFixtures.Finish(field, martial);
        var effective = martial.Character.EffectiveAttributes;
        Assert.True(effective.Strength == (int)(attributes.Strength * 1.12f) && effective.Agility == (int)(attributes.Agility * 1.12f)
            && effective.Spirit == (int)(attributes.Spirit * 1.12f) && effective.Talent == attributes.Talent && martial.Character.Attributes == attributes,
            "hard training did not raise the effective strength, agility and spirit 12% alone, or changed the allocated ones");
        Assert.True(WeaponDamage.Compute(martial.Character).Max > damage.Max, "the raised strength did not reach the attack damage");
        Assert.True(member.Character.Effects.Totals.Percent(StatKind.Strength) == 12 && distant.Character.Effects.Totals.Percent(StatKind.Strength) == 0,
            "hard training missed the party member in range or reached the one beyond it");
        return Task.CompletedTask;
    }
}
