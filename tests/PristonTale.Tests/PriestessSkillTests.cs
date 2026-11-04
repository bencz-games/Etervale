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

// The Priestess's skills that need more than their table: the shockwave's spell roll, the ice spear's table plus the
// raised attack, the angel that excludes Muspell and the blessing's defence and absorption.
internal static class PriestessSkillTests
{
    private const uint SummonMuspell = 0x07080004;
    private const uint SpiritImpact = 0x07100001;
    private const uint PiercingIce = 0x07100002;
    private const uint SummonRamiel = 0x07100003;
    private const uint BlessingKrishna = 0x07100004;

    private static readonly CharacterClassRegistry Classes = new();

    public static IEnumerable<(string, Func<Task>)> Cases()
    {
        yield return ("spirit impact and piercing ice roll their spell terms and the raised attack", SpellsAsync);
        yield return ("summon ramiel and summon muspell exclude each other", RamielAsync);
        yield return ("blessing krishna raises defence by a percent and absorption", KrishnaAsync);
    }

    private static Task SpellsAsync()
    {
        var rng = new CombatRng(53);
        var priestess = Classes.CreatePlayer(PlayerClass.Priestess, "Spells", 90);
        priestess.Equip(EquipmentSlot.Weapon, SkillFixtures.Weapon(ItemKind.Staff, 40, 60));
        var weapon = SkillFormulas.WeaponItemDamage(priestess);
        var spirit = priestess.Attributes.Spirit;
        var level = priestess.Level * 50 / 100;
        SkillRuleCatalog.TryGet(SpiritImpact, out var impact);
        for (var roll = 0; roll < 300; roll++)
        {
            var power = SkillFormulas.RollPower(priestess, impact, 10, rng);
            Assert.True(power >= 710 + level + spirit / 2 + weapon.Min * 5 && power <= 770 + level + spirit + weapon.Max * 5,
                $"spirit impact rank 10 power {power} outside its spell terms");
        }

        var attack = WeaponDamage.Compute(priestess);
        SkillRuleCatalog.TryGet(PiercingIce, out var ice);
        for (var roll = 0; roll < 300; roll++)
        {
            var power = SkillFormulas.RollPower(priestess, ice, 10, rng);
            Assert.True(power >= 870 + attack.Min * 930 / 100 && power <= 980 + attack.Max * 930 / 100,
                $"piercing ice rank 10 power {power} outside its table plus the attack raised 830%");
        }

        return Task.CompletedTask;
    }

    private static Task RamielAsync()
    {
        var field = SkillFixtures.Field();
        var priestess = SkillFixtures.Player(field, 1, PlayerClass.Priestess, 90, Vector3.Zero, SkillFixtures.Weapon(ItemKind.Staff));
        WithMana(priestess);
        priestess.Character.Skills.Restore(SummonMuspell, 1);
        priestess.Character.Skills.Restore(SummonRamiel, 10);
        SkillFixtures.Accept("Summon Ramiel", field, priestess, SummonRamiel);
        SkillFixtures.Finish(field, priestess);
        Assert.True(priestess.Character.Effects.Totals.Flat(StatKind.Evasion) == 14, "summon ramiel rank 10 evasion");
        SkillFixtures.Refuse(SkillCastVerdict.AlreadyActive, "summon muspell called beside ramiel", field, priestess, SummonMuspell);
        return Task.CompletedTask;
    }

    private static Task KrishnaAsync()
    {
        var field = SkillFixtures.Field();
        var priestess = SkillFixtures.Player(field, 1, PlayerClass.Priestess, 90, Vector3.Zero, SkillFixtures.Weapon(ItemKind.Staff));
        WithMana(priestess);
        priestess.Character.Skills.Restore(BlessingKrishna, 10);
        SkillFixtures.Accept("Blessing Krishna", field, priestess, BlessingKrishna);
        SkillFixtures.Finish(field, priestess);
        var totals = priestess.Character.Effects.Totals;
        Assert.True(totals.Percent(StatKind.Defence) == 55 && totals.Flat(StatKind.Absorption) == 10, "blessing krishna rank 10 values");
        return Task.CompletedTask;
    }

    private static void WithMana(WorldPlayer player) =>
        player.Character.Effects.SetPermanent(0xFFFF0001, EffectSourceKind.System, false, [StatModifier.Flat(StatKind.MaxMana, 1000)]);
}
