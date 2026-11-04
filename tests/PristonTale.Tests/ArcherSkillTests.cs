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

// The Archer's skills that need more than their table: the kick's arrow that stuns (bosses spared), the two lightning
// arrows, and the areas at the target that roll the attack raised by a rate, one of them slowing what it strikes.
internal static class ArcherSkillTests
{
    private const uint EvadeShot = 0x04100001;
    private const uint ThunderRoop = 0x04100002;
    private const uint NatureSplash = 0x04100003;
    private const uint CircleTrap = 0x04100004;

    private static readonly CharacterClassRegistry Classes = new();

    public static IEnumerable<(string, Func<Task>)> Cases()
    {
        yield return ("evade shot stuns the monster its arrow struck, never a boss", EvadeShotAsync);
        yield return ("thunder roop shoots twice, as it starts and at its event, beyond the bow's reach", ThunderRoopAsync);
        yield return ("nature splash and circle trap roll the attack raised by their rate, circle trap slowing what it strikes", AreasAsync);
    }

    private static Task EvadeShotAsync()
    {
        Assert.True(Stuns(boss: false), "evade shot did not stun the monster it struck");
        Assert.True(!Stuns(boss: true), "evade shot stunned a boss");
        return Task.CompletedTask;
    }

    private static bool Stuns(bool boss)
    {
        var definition = SkillFixtures.Monster(life: 1_000_000);
        if (boss)
        {
            definition = definition with { Class = MonsterClass.Boss };
        }

        var field = SkillFixtures.Field(23, definition);
        var archer = SkillFixtures.Player(field, 1, PlayerClass.Archer, 90, Vector3.Zero, SkillFixtures.Weapon(ItemKind.Bow));
        WithMana(archer);
        archer.Character.Skills.Restore(EvadeShot, 1);
        var monster = SkillFixtures.Spawn(field, definition, new Vector3(150, 0, 0));
        SkillFixtures.Wait(field, 3f);
        for (var attempt = 0; attempt < 10; attempt++)
        {
            monster.Position = new Vector3(150, 0, 0);
            SkillFixtures.Accept("Evade Shot", field, archer, EvadeShot, monster.EntityId);
            SkillFixtures.Finish(field, archer);
            if (monster.Effects.TryGet(EvadeShot, out _) || monster.Life < monster.Def.Combat.MaxLife)
            {
                break;
            }

            SkillFixtures.Wait(field, 18f);
        }

        Assert.True(monster.Life < monster.Def.Combat.MaxLife, "evade shot never struck");
        return monster.Effects.TryGet(EvadeShot, out _) && monster.Effects.Has(ControlFlags.Stun);
    }

    private static Task ThunderRoopAsync()
    {
        var definition = SkillFixtures.Monster(life: 1_000_000);
        var field = SkillFixtures.Field(29, definition);
        var archer = SkillFixtures.Player(field, 1, PlayerClass.Archer, 90, Vector3.Zero, SkillFixtures.Weapon(ItemKind.Bow));
        WithMana(archer);
        archer.Character.Skills.Restore(ThunderRoop, 10);
        SkillRuleCatalog.TryGet(ThunderRoop, out var rule);
        var bow = SkillFormulas.Range(rule with { Range = [], RangeMode = SkillRangeMode.Table }, 10, archer.Character);
        Assert.True(SkillFormulas.Range(rule, 10, archer.Character) == bow + 65, "thunder roop rank 10 does not reach 65 beyond the bow");
        var monster = SkillFixtures.Spawn(field, definition, new Vector3(100, 0, 0));
        SkillFixtures.Wait(field, 3f);
        monster.Position = new Vector3(100, 0, 0);
        SkillFixtures.Accept("Thunder Roop", field, archer, ThunderRoop, monster.EntityId);
        var impacts = 0;
        for (var tick = 0; tick < 200 && (archer.IsCasting || tick < 20); tick++)
        {
            field.Step(0.05f);
            impacts += field.SnapshotSkillEvents().Count(value => value.SkillCode == ThunderRoop && value.Phase == SkillEventPhase.Impact);
        }

        Assert.True(impacts == 2, $"thunder roop shot {impacts} times");
        return Task.CompletedTask;
    }

    private static Task AreasAsync()
    {
        var rng = new CombatRng(31);
        var archer = Classes.CreatePlayer(PlayerClass.Archer, "Areas", 90);
        archer.Equip(EquipmentSlot.Weapon, SkillFixtures.Weapon(ItemKind.Bow, 40, 60));
        var attack = WeaponDamage.Compute(archer);
        SkillRuleCatalog.TryGet(NatureSplash, out var splash);
        for (var roll = 0; roll < 300; roll++)
        {
            var power = SkillFormulas.RollPower(archer, splash, 10, rng);
            Assert.True(power >= attack.Min * 350 / 100 && power <= attack.Max * 350 / 100, $"nature splash rank 10 power {power} outside the attack raised 250%");
        }

        var definition = SkillFixtures.Monster(life: 1_000_000);
        var field = SkillFixtures.Field(37, definition);
        var hunter = SkillFixtures.Player(field, 1, PlayerClass.Archer, 90, Vector3.Zero, SkillFixtures.Weapon(ItemKind.Bow));
        WithMana(hunter);
        hunter.Character.Skills.Restore(CircleTrap, 10);
        var spots = new[] { new Vector3(150, 0, 0), new Vector3(150, 0, 60), new Vector3(150, 0, 400) };
        var monsters = spots.Select(spot => SkillFixtures.Spawn(field, definition, spot)).ToArray();
        SkillFixtures.Wait(field, 3f);
        for (var index = 0; index < monsters.Length; index++)
        {
            monsters[index].Position = spots[index];
        }

        SkillFixtures.Accept("Circle Trap", field, hunter, CircleTrap, monsters[0].EntityId);
        for (var tick = 0; tick < 200 && !monsters[0].Effects.TryGet(CircleTrap, out _); tick++)
        {
            field.Step(0.05f);
        }

        Assert.True(monsters[0].Effects.Totals.Percent(StatKind.MoveSpeed) == -28 && monsters[1].Effects.Has(ControlFlags.Slow),
            "circle trap rank 10 did not slow the monsters it struck by 28%");
        Assert.True(!monsters[2].Effects.Has(ControlFlags.Slow), "circle trap slowed a monster beyond its radius");
        return Task.CompletedTask;
    }

    private static void WithMana(WorldPlayer player) =>
        player.Character.Effects.SetPermanent(0xFFFF0001, EffectSourceKind.System, false, [StatModifier.Flat(StatKind.MaxMana, 500)]);
}
