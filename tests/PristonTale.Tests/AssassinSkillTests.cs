using System.Numerics;
using PristonTale.Application.World;
using PristonTale.Simulation.Characters;
using PristonTale.Simulation.Combat;
using PristonTale.Simulation.Items;
using PristonTale.Simulation.Modifiers;
using PristonTale.Simulation.Skills;
using PristonTale.Contracts.Skills;
using PristonTale.Contracts.Characters;

namespace PristonTale.Tests;

// The Assassin's skills that need more than their table: the bomb around the caster, raised by Land Mining's rate as
// the original server raises it, and the strikes with their hit counts.
internal static class AssassinSkillTests
{
    private const uint ShadowBomb = 0x10100001;
    private const uint ViolentStab = 0x10100003;

    private static readonly CharacterClassRegistry Classes = new();

    public static IEnumerable<(string, Func<Task>)> Cases()
    {
        yield return ("shadow bomb rolls its table raised by land mining's rate plus the attack around the caster", ShadowBombAsync);
        yield return ("violent stab strikes twice", ViolentStabAsync);
    }

    private static Task ShadowBombAsync()
    {
        var rng = new CombatRng(67);
        var assassin = Classes.CreatePlayer(PlayerClass.Assassin, "Bomb", 90);
        assassin.Equip(EquipmentSlot.Weapon, SkillFixtures.Weapon(ItemKind.Dagger, 40, 60));
        SkillRuleCatalog.TryGet(ShadowBomb, out var rule);
        var attack = WeaponDamage.Compute(assassin);
        for (var roll = 0; roll < 300; roll++)
        {
            var power = SkillFormulas.RollPower(assassin, rule, 10, rng);
            Assert.True(power >= 969 + attack.Min && power <= 1252 + attack.Max, $"shadow bomb rank 10 power {power} outside 480..620 raised 102% plus the attack");
        }

        Assert.True(rule.Target == SkillTarget.Self && rule.AreaCenter == SkillAreaCenter.Caster && rule.At(rule.MaxTargetsByRank, 10) == 9,
            "shadow bomb is not an area of nine around the caster at rank 10");
        return Task.CompletedTask;
    }

    private static Task ViolentStabAsync()
    {
        var definition = SkillFixtures.Monster(life: 1_000_000);
        var field = SkillFixtures.Field(71, definition);
        var assassin = SkillFixtures.Player(field, 1, PlayerClass.Assassin, 90, Vector3.Zero, SkillFixtures.Weapon(ItemKind.Dagger));
        assassin.Character.Effects.SetPermanent(0xFFFF0001, EffectSourceKind.System, false, [StatModifier.Flat(StatKind.MaxMana, 500)]);
        assassin.Character.Skills.Restore(ViolentStab, 10);
        var monster = SkillFixtures.Spawn(field, definition, new Vector3(30, 0, 0));
        SkillFixtures.Accept("Violent Stab", field, assassin, ViolentStab, monster.EntityId);
        var impacts = 0;
        for (var tick = 0; tick < 200 && (assassin.IsCasting || tick < 20); tick++)
        {
            field.Step(0.05f);
            impacts += field.SnapshotSkillEvents().Count(value => value.SkillCode == ViolentStab && value.Phase == SkillEventPhase.Impact);
        }

        Assert.True(impacts == 2, $"violent stab struck {impacts} times");
        return Task.CompletedTask;
    }
}
