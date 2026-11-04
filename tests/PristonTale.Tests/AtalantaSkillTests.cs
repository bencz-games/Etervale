using System.Numerics;
using PristonTale.Application.World;
using PristonTale.Simulation.Characters;
using PristonTale.Simulation.Combat;
using PristonTale.Simulation.Items;
using PristonTale.Simulation.Modifiers;
using PristonTale.Simulation.Skills;
using PristonTale.Simulation.World.Monsters;
using PristonTale.Contracts.Skills;
using PristonTale.Contracts.Characters;

namespace PristonTale.Tests;

// The Atalanta's skills that need more than their table: the winged boots that lift the step limit, the javelins that
// roll their accuracy around the target, the summoned bear and the two javelins with their relative critical.
internal static class AtalantaSkillTests
{
    private const uint Talaria = 0x06100001;
    private const uint SummonArcuda = 0x06100003;
    private const uint SnippyFear = 0x06100004;

    private static readonly CharacterClassRegistry Classes = new();

    public static IEnumerable<(string, Func<Task>)> Cases()
    {
        yield return ("talaria adds move speed and lifts the step limit of the caster or a clicked player", TalariaAsync);
        yield return ("the step level holds the move speed to nine unless the limit is lifted, then steps one above it", StepLevelAsync);
        yield return ("summon arcuda calls the bear with its rank's life, damage, rating, defence and size", ArcudaAsync);
        yield return ("snippy fear throws two javelins with a relative critical beyond the javelin's reach", SnippyFearAsync);
    }

    private static Task TalariaAsync()
    {
        var field = SkillFixtures.Field();
        var atalanta = SkillFixtures.Player(field, 1, PlayerClass.Atalanta, 90, Vector3.Zero, SkillFixtures.Weapon(ItemKind.ThrowingWeapon));
        var friend = SkillFixtures.Player(field, 2, PlayerClass.Knight, 80, new Vector3(100, 0, 0));
        WithMana(atalanta);
        atalanta.Character.Skills.Restore(Talaria, 10);
        var before = atalanta.Character.Derived.MoveSpeed;
        SkillFixtures.Accept("Talaria", field, atalanta, Talaria);
        SkillFixtures.Finish(field, atalanta);
        var totals = atalanta.Character.Effects.Totals;
        Assert.True(totals.Flat(StatKind.MoveSpeed) == 4 && totals.Flat(StatKind.MoveSpeedUnbound) > 0, "talaria rank 10 values on the caster");
        // An ungeared caster stands below the step limit: four more speed and the lifted limit's level step.
        Assert.True(before < Locomotion.StepLimit && atalanta.Character.Derived.MoveSpeed == before + 5,
            $"talaria raised the caster's step level from {before} to {atalanta.Character.Derived.MoveSpeed}");
        // An ally rule refuses a holder that has it once the cooldown let the cast through.
        SkillFixtures.Wait(field, 18f);
        SkillFixtures.Refuse(SkillCastVerdict.AlreadyActive, "talaria recast on the caster", field, atalanta, Talaria);

        var other = SkillFixtures.Field();
        var caster = SkillFixtures.Player(other, 1, PlayerClass.Atalanta, 90, Vector3.Zero, SkillFixtures.Weapon(ItemKind.ThrowingWeapon));
        var target = SkillFixtures.Player(other, 2, PlayerClass.Knight, 80, new Vector3(100, 0, 0));
        WithMana(caster);
        caster.Character.Skills.Restore(Talaria, 1);
        SkillFixtures.Accept("Talaria on a player", other, caster, Talaria, target.EntityId);
        SkillFixtures.Finish(other, caster);
        Assert.True(target.Character.Effects.Totals.Flat(StatKind.MoveSpeed) == 1 && !caster.Character.Effects.TryGet(Talaria, out _),
            "talaria on a clicked player missed that player or reached the caster");
        Assert.True(friend.Character.Effects.Totals.Flat(StatKind.MoveSpeed) == 0, "talaria reached a player it was not cast on");
        return Task.CompletedTask;
    }

    private static Task StepLevelAsync()
    {
        Assert.True(Locomotion.StepLevel(12, false) == Locomotion.StepLimit && Locomotion.StepLevel(5, false) == 5, "the step level is not held to nine");
        Assert.True(Locomotion.StepLevel(12, true) == 13 && Locomotion.BaseTranslation(13) == 250 + 10 * 12, "a lifted step limit does not step by the move speed");
        Assert.True(Locomotion.BaseTranslation(0) == 250, "a zero step level does not walk at the slowest step");
        return Task.CompletedTask;
    }

    private static Task ArcudaAsync()
    {
        var scenario = SkillScenario.Create(PlayerClass.Atalanta, 90, catalog: [SkillScenario.SummonModel("Arcuda", 43)]);
        scenario.Character.Effects.SetPermanent(0xFFFF0001, EffectSourceKind.System, false, [StatModifier.Flat(StatKind.MaxMana, 500)]);
        scenario.Refill();
        scenario.Learn(SummonArcuda, 3);
        Assert.True(scenario.Cast(SummonArcuda).Accepted, "Summon Arcuda refused");
        Assert.True(scenario.Run(80, () => scenario.Summons.Any()) > 0, "the bear never appeared");
        var bear = scenario.Summons.Single();
        var agility = scenario.Character.Attributes.Agility;
        Assert.True(bear.Def.Combat.MaxLife == 2900 && bear.Def.Combat.Defence == 1500 + 2 * agility && bear.Def.Combat.AttackDamage == new MonsterRange(140, 160)
            && bear.Def.Combat.AttackRatingAccuracy == 1200, "rank 3 bear values differ from the skill table");
        Assert.Near((256f + 2 * 15f) / 256f, bear.ModelScale, 0.0001f);
        return Task.CompletedTask;
    }

    private static Task SnippyFearAsync()
    {
        var atalanta = Classes.CreatePlayer(PlayerClass.Atalanta, "Javelins", 90);
        atalanta.Equip(EquipmentSlot.Weapon, SkillFixtures.Weapon(ItemKind.ThrowingWeapon));
        SkillRuleCatalog.TryGet(SnippyFear, out var rule);
        var basic = CombatProfileFactory.ForPlayerBasicAttack(atalanta);
        var profile = SkillFormulas.WeaponAttack(atalanta, rule, 10, SkillFixtures.Monster(), new CombatRng(1));
        Assert.True(profile.CritChance == basic.CritChance * 132 / 100, $"snippy fear rank 10 critical {profile.CritChance} from {basic.CritChance}");
        var reach = SkillFormulas.Range(rule with { Range = [], RangeMode = SkillRangeMode.Table }, 10, atalanta);
        Assert.True(SkillFormulas.Range(rule, 10, atalanta) == reach + 65, "snippy fear rank 10 does not reach 65 beyond the javelin");

        var definition = SkillFixtures.Monster(life: 1_000_000);
        var field = SkillFixtures.Field(47, definition);
        var thrower = SkillFixtures.Player(field, 1, PlayerClass.Atalanta, 90, Vector3.Zero, SkillFixtures.Weapon(ItemKind.ThrowingWeapon));
        WithMana(thrower);
        thrower.Character.Skills.Restore(SnippyFear, 10);
        var monster = SkillFixtures.Spawn(field, definition, new Vector3(100, 0, 0));
        SkillFixtures.Wait(field, 3f);
        monster.Position = new Vector3(100, 0, 0);
        SkillFixtures.Accept("Snippy Fear", field, thrower, SnippyFear, monster.EntityId);
        var impacts = 0;
        for (var tick = 0; tick < 200 && (thrower.IsCasting || tick < 20); tick++)
        {
            field.Step(0.05f);
            impacts += field.SnapshotSkillEvents().Count(value => value.SkillCode == SnippyFear && value.Phase == SkillEventPhase.Impact);
        }

        Assert.True(impacts == 2, $"snippy fear threw {impacts} javelins");
        return Task.CompletedTask;
    }

    private static void WithMana(WorldPlayer player) =>
        player.Character.Effects.SetPermanent(0xFFFF0001, EffectSourceKind.System, false, [StatModifier.Flat(StatKind.MaxMana, 500)]);
}
