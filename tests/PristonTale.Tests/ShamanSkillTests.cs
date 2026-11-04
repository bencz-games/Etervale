using System.Numerics;
using PristonTale.Application.World;
using PristonTale.Simulation.Characters;
using PristonTale.Simulation.Combat;
using PristonTale.Simulation.Items;
using PristonTale.Simulation.Modifiers;
using PristonTale.Simulation.Skills;
using PristonTale.Contracts.Characters;

namespace PristonTale.Tests;

// The Shaman's skills that need more than their table: the spell rolls, the creed that raises mana and stamina and
// the passive that raises life.
internal static class ShamanSkillTests
{
    private const uint Creed = 0x50100001;
    private const uint PressOfDeity = 0x50100002;
    private const uint GhostlyNail = 0x50100003;
    private const uint HighRegeneration = 0x50100004;

    private static readonly CharacterClassRegistry Classes = new();

    public static IEnumerable<(string, Func<Task>)> Cases()
    {
        yield return ("press of deity and ghostly nail roll their spell terms", SpellsAsync);
        yield return ("creed raises maximum mana and stamina by its rank", CreedAsync);
        yield return ("high regeneration raises maximum life by its rank", HighRegenerationAsync);
    }

    private static Task SpellsAsync()
    {
        var rng = new CombatRng(73);
        var shaman = Classes.CreatePlayer(PlayerClass.Shaman, "Spells", 90);
        shaman.Equip(EquipmentSlot.Weapon, SkillFixtures.Weapon(ItemKind.Phantom, 40, 60));
        var weapon = SkillFormulas.WeaponItemDamage(shaman);
        var spirit = shaman.Attributes.Spirit;
        foreach (var (code, name, min, max, times) in new[] { (PressOfDeity, "press of deity", 810, 870, 6), (GhostlyNail, "ghostly nail", 970, 1080, 13) })
        {
            SkillRuleCatalog.TryGet(code, out var rule);
            for (var roll = 0; roll < 300; roll++)
            {
                var power = SkillFormulas.RollPower(shaman, rule, 10, rng);
                Assert.True(power >= min + shaman.Level + spirit / 2 + weapon.Min * times && power <= max + shaman.Level + spirit + weapon.Max * times,
                    $"{name} rank 10 power {power} outside its spell terms");
            }
        }

        return Task.CompletedTask;
    }

    private static Task CreedAsync()
    {
        var field = SkillFixtures.Field();
        var shaman = SkillFixtures.Player(field, 1, PlayerClass.Shaman, 90, Vector3.Zero, SkillFixtures.Weapon(ItemKind.Phantom));
        shaman.Character.Effects.SetPermanent(0xFFFF0001, EffectSourceKind.System, false, [StatModifier.Flat(StatKind.MaxMana, 1000)]);
        shaman.Character.Skills.Restore(Creed, 10);
        var (mana, stamina) = (shaman.Character.MaxMana, shaman.Character.MaxStamina);
        SkillFixtures.Accept("Creed", field, shaman, Creed);
        SkillFixtures.Finish(field, shaman);
        var totals = shaman.Character.Effects.Totals;
        Assert.True(totals.Percent(StatKind.MaxMana) == 28 && totals.Percent(StatKind.MaxStamina) == 28
            && shaman.Character.MaxMana > mana && shaman.Character.MaxStamina > stamina, "creed rank 10 did not raise mana and stamina 28%");
        return Task.CompletedTask;
    }

    private static Task HighRegenerationAsync()
    {
        var shaman = Classes.CreatePlayer(PlayerClass.Shaman, "Life", 90);
        var life = shaman.MaxLife;
        shaman.Skills.Restore(HighRegeneration, 10);
        Assert.True(shaman.MaxLife == life + life * 32 / 100, $"high regeneration rank 10 left maximum life {shaman.MaxLife} from {life}");
        return Task.CompletedTask;
    }
}
