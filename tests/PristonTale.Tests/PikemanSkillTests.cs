using System.Numerics;
using PristonTale.Application.World;
using PristonTale.Simulation.Characters;
using PristonTale.Simulation.Items;
using PristonTale.Simulation.Modifiers;
using PristonTale.Simulation.World.Monsters;
using PristonTale.Contracts.Combat;
using PristonTale.Contracts.Skills;
using PristonTale.Contracts.Characters;

namespace PristonTale.Tests;

// The Pikeman's skills that need more than their table: the spears under each enemy around the caster, the amplified
// attack and the spinning strikes that drain a monster's life.
internal static class PikemanSkillTests
{
    private const uint FinalSpear = 0x03100002;
    private const uint Amplified = 0x03100003;
    private const uint SideSpinAttack = 0x03100004;

    public static IEnumerable<(string, Func<Task>)> Cases()
    {
        yield return ("final spear strikes at most six enemies around the caster without missing and reports each one struck", FinalSpearAsync);
        yield return ("amplified raises the attack damage and rating and lowers absorption", AmplifiedAsync);
        yield return ("side spin attack drains a monster above half its life, bosses too, never the undead", SideSpinAsync);
    }

    private static Task FinalSpearAsync()
    {
        var definition = SkillFixtures.Monster(life: 1_000_000);
        var field = SkillFixtures.Field(5, definition);
        var pikeman = SkillFixtures.Player(field, 1, PlayerClass.Pikeman, 90, Vector3.Zero, SkillFixtures.Weapon(ItemKind.Pole));
        WithMana(pikeman);
        pikeman.Character.Skills.Restore(FinalSpear, 1);
        // Rank 1: a radius of 120; nine monsters stand within 70, one at 300.
        var spots = new List<Vector3> { new(40, 0, 0) };
        for (var index = 0; index < 8; index++)
        {
            spots.Add(new Vector3(MathF.Sin(index) * 70, 0, MathF.Cos(index) * 70));
        }

        spots.Add(new Vector3(-300, 0, 0));
        var monsters = spots.Select(spot => SkillFixtures.Spawn(field, definition, spot)).ToArray();
        // A spawned monster walks out for a while; then each is put back on its spot.
        SkillFixtures.Wait(field, 3f);
        for (var index = 0; index < monsters.Length; index++)
        {
            monsters[index].Position = spots[index];
        }

        var main = monsters[0];
        var far = monsters[^1];
        SkillFixtures.Accept("Final Spear", field, pikeman, FinalSpear, main.EntityId);
        var struck = new HashSet<ulong>();
        var reported = new HashSet<ulong>();
        for (var tick = 0; tick < 200 && (pikeman.IsCasting || tick < 20); tick++)
        {
            field.Step(0.05f);
            struck.UnionWith((field.SnapshotEvents() ?? []).Where(value => value.Kind == CombatEventKind.Hit && value.Damage > 0).Select(value => value.TargetId));
            reported.UnionWith(field.SnapshotSkillEvents().Where(value => (value.Flags & SkillEventFlags.Struck) != 0).Select(value => value.TargetId));
        }

        Assert.True(struck.Count == 6 && !struck.Contains(far.EntityId), $"final spear struck {struck.Count} enemies, reported {reported.Count}");
        Assert.True(reported.SetEquals(struck), "final spear did not report each enemy it struck");
        return Task.CompletedTask;
    }

    private static Task AmplifiedAsync()
    {
        var field = SkillFixtures.Field();
        var pikeman = SkillFixtures.Player(field, 1, PlayerClass.Pikeman, 90, Vector3.Zero, SkillFixtures.Weapon(ItemKind.Pole));
        WithMana(pikeman);
        var areaBefore = Simulation.Combat.WeaponDamage.Compute(pikeman.Character);
        pikeman.Character.Skills.Restore(Amplified, 10);
        SkillFixtures.Accept("Amplified", field, pikeman, Amplified);
        SkillFixtures.Finish(field, pikeman);
        var totals = pikeman.Character.Effects.Totals;
        Assert.True(totals.Percent(StatKind.HitDamageMin) == 120 && totals.Percent(StatKind.HitDamageMax) == 120 && totals.Percent(StatKind.AttackRating) == 75
            && totals.Flat(StatKind.Absorption) == -23, "amplified rank 10 values");
        var area = Simulation.Combat.WeaponDamage.Compute(pikeman.Character);
        var hit = Simulation.Combat.CombatProfileFactory.ForPlayerBasicAttack(pikeman.Character);
        Assert.True(area == areaBefore && hit.DamageMin == area.Min + area.Min * 120 / 100 && hit.DamageMax == area.Max + area.Max * 120 / 100,
            "amplified did not raise a single-target hit's attack range, or raised the attack areas read");
        SkillFixtures.Refuse(SkillCastVerdict.AlreadyActive, "amplified recast while running", field, pikeman, Amplified);
        return Task.CompletedTask;
    }

    private static Task SideSpinAsync()
    {
        // The drained life returns to the caster (up to 40% of its maximum life): a wounded caster heals.
        Assert.True(Heals(MonsterBrood.Normal, boss: false, lifePercent: 100), "side spin attack did not drain a healthy monster");
        Assert.True(Heals(MonsterBrood.Normal, boss: true, lifePercent: 100), "side spin attack spared a boss");
        Assert.True(!Heals(MonsterBrood.Undead, boss: false, lifePercent: 100), "side spin attack drained the undead");
        Assert.True(!Heals(MonsterBrood.Normal, boss: false, lifePercent: 50), "side spin attack drained a monster at half its life");
        return Task.CompletedTask;
    }

    private static bool Heals(MonsterBrood brood, bool boss, int lifePercent)
    {
        var definition = SkillFixtures.Monster(brood, life: 100_000);
        if (boss)
        {
            definition = definition with { Class = MonsterClass.Boss };
        }

        var field = SkillFixtures.Field(19, definition);
        var pikeman = SkillFixtures.Player(field, 1, PlayerClass.Pikeman, 90, Vector3.Zero, SkillFixtures.Weapon(ItemKind.Pole));
        WithMana(pikeman);
        pikeman.Character.Skills.Restore(SideSpinAttack, 1);
        var monster = SkillFixtures.Spawn(field, definition, new Vector3(30, 0, 0));
        monster.Life = definition.Combat.MaxLife * lifePercent / 100;
        SkillFixtures.Ready(field, pikeman);
        pikeman.Character.Life = pikeman.Character.MaxLife / 4;
        var before = pikeman.Character.Life;
        Assert.True(SkillFixtures.Cast(field, pikeman, SideSpinAttack, monster.EntityId).Accepted, "side spin attack refused");
        SkillFixtures.Finish(field, pikeman);
        // Natural regeneration over the cast stays far below the drained share returned.
        return pikeman.Character.Life - before > pikeman.Character.MaxLife / 10;
    }

    private static void WithMana(WorldPlayer player) =>
        player.Character.Effects.SetPermanent(0xFFFF0001, EffectSourceKind.System, false, [StatModifier.Flat(StatKind.MaxMana, 500)]);
}
