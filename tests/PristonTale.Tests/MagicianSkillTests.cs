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

// The Magician's skills that need more than their table: the spell rolls, the shrinking curse that takes life and
// damage for a while, and the anima that strengthens spells, takes mana and trades places with Spirit Elemental.
internal static class MagicianSkillTests
{
    private const uint SpiritElemental = 0x08040003;
    private const uint Silraphim = 0x08100001;
    private const uint VisTenus = 0x08100002;
    private const uint PrimaIgnis = 0x08100003;
    private const uint ThirdAnima = 0x08100004;

    private static readonly CharacterClassRegistry Classes = new();

    public static IEnumerable<(string, Func<Task>)> Cases()
    {
        yield return ("silraphim and prima ignis roll their spell terms and the raised attack", SpellsAsync);
        yield return ("vis tenus shrinks a monster's life, size and damage while it lasts, kills a weak one and spares bosses", VisTenusAsync);
        yield return ("third anima strengthens spells by its rank and spirit elemental's, lowers maximum mana and replaces it", ThirdAnimaAsync);
    }

    private static Task SpellsAsync()
    {
        var rng = new CombatRng(59);
        var magician = Classes.CreatePlayer(PlayerClass.Magician, "Spells", 90);
        magician.Equip(EquipmentSlot.Weapon, SkillFixtures.Weapon(ItemKind.Staff, 40, 60));
        var weapon = SkillFormulas.WeaponItemDamage(magician);
        var spirit = magician.Attributes.Spirit;
        SkillRuleCatalog.TryGet(Silraphim, out var silraphim);
        for (var roll = 0; roll < 300; roll++)
        {
            var power = SkillFormulas.RollPower(magician, silraphim, 10, rng);
            Assert.True(power >= 710 + magician.Level + spirit / 2 + weapon.Min * 5 && power <= 770 + magician.Level + spirit + weapon.Max * 5,
                $"silraphim rank 10 power {power} outside its spell terms");
        }

        var attack = WeaponDamage.Compute(magician);
        SkillRuleCatalog.TryGet(PrimaIgnis, out var prima);
        for (var roll = 0; roll < 300; roll++)
        {
            var power = SkillFormulas.RollPower(magician, prima, 10, rng);
            Assert.True(power >= 850 + attack.Min * 930 / 100 && power <= 950 + attack.Max * 930 / 100, $"prima ignis rank 10 power {power}");
        }

        return Task.CompletedTask;
    }

    private static Task VisTenusAsync()
    {
        var definition = SkillFixtures.Monster(life: 100_000);
        var (field, magician, monster) = Shrinking(definition, 100_000);
        Assert.True(monster.Effects.TryGet(VisTenus, out _), "vis tenus did not land on the monster it struck");
        // Rank 1: 10% of the maximum life away, 10% less damage, half size, for 10 s.
        Assert.True(monster.Life <= 90_000 && monster.ModelScale is > 0.58f and < 0.59f && monster.Effects.Totals.Flat(StatKind.DamageDealt) == -10,
            $"vis tenus left life {monster.Life} and scale {monster.ModelScale}");
        SkillFixtures.Refuse(SkillCastVerdict.AlreadyActive, "a second vis tenus while the first runs", field, magician, VisTenus, monster.EntityId);
        var shrunk = monster.Life;
        SkillFixtures.Wait(field, 11f);
        Assert.True(!monster.Effects.TryGet(VisTenus, out _) && monster.Life >= shrunk + 10_000 - 1000 && monster.ModelScale == 0f,
            $"vis tenus did not give back life and size: {monster.Life}, {monster.ModelScale}");

        var (_, _, weak) = Shrinking(definition, 5_000);
        Assert.True(!weak.IsAlive, "a monster with less life than vis tenus takes survived it");
        var (_, _, boss) = Shrinking(definition with { Class = MonsterClass.Boss }, 100_000);
        Assert.True(!boss.Effects.TryGet(VisTenus, out _) && boss.ModelScale == 0f, "vis tenus shrank a boss");
        return Task.CompletedTask;
    }

    // A monster struck by rank 1 Vis Tenus, starting with the given life.
    private static (FieldInstance Field, WorldPlayer Caster, MonsterInstance Monster) Shrinking(MonsterDefinition definition, int life)
    {
        var field = SkillFixtures.Field(61, definition);
        var magician = SkillFixtures.Player(field, 1, PlayerClass.Magician, 90, Vector3.Zero, SkillFixtures.Weapon(ItemKind.Staff));
        WithMana(magician);
        magician.Character.Skills.Restore(VisTenus, 1);
        var monster = SkillFixtures.Spawn(field, definition, new Vector3(30, 0, 0));
        SkillFixtures.Wait(field, 3f);
        for (var attempt = 0; attempt < 10 && monster.IsAlive && !monster.Effects.TryGet(VisTenus, out _); attempt++)
        {
            monster.Position = new Vector3(30, 0, 0);
            monster.Life = Math.Min(monster.Life, life);
            SkillFixtures.Accept("Vis Tenus", field, magician, VisTenus, monster.EntityId);
            SkillFixtures.Finish(field, magician);
            if (monster.IsAlive && !monster.Effects.TryGet(VisTenus, out _))
            {
                // A missed hit still starts the caster's wait.
                SkillFixtures.Wait(field, 11f);
            }

            if (monster.Def.IsBoss && monster.Life < life)
            {
                break;
            }
        }

        return (field, magician, monster);
    }

    private static Task ThirdAnimaAsync()
    {
        var field = SkillFixtures.Field();
        var magician = SkillFixtures.Player(field, 1, PlayerClass.Magician, 90, Vector3.Zero, SkillFixtures.Weapon(ItemKind.Staff));
        var knight = SkillFixtures.Player(field, 2, PlayerClass.Knight, 80, new Vector3(40, 0, 0));
        WithMana(magician);
        magician.Character.Skills.Restore(SpiritElemental, 10);
        magician.Character.Skills.Restore(ThirdAnima, 10);
        SkillFixtures.Refuse(SkillCastVerdict.InvalidTarget, "third anima cast on a knight", field, magician, ThirdAnima, knight.EntityId);
        SkillFixtures.Accept("Spirit Elemental", field, magician, SpiritElemental);
        SkillFixtures.Finish(field, magician);
        var maxMana = magician.Character.MaxMana;
        SkillFixtures.Wait(field, 18f);
        SkillFixtures.Accept("Third Anima", field, magician, ThirdAnima);
        SkillFixtures.Finish(field, magician);
        var effects = magician.Character.Effects;
        Assert.True(!effects.TryGet(SpiritElemental, out _) && effects.TryGet(ThirdAnima, out _), "third anima did not replace spirit elemental");
        Assert.True(effects.Totals.Flat(StatKind.SpellPower) == 150 + 35, $"third anima rank 10 spell power {effects.Totals.Flat(StatKind.SpellPower)}");
        Assert.True(magician.Character.MaxMana < maxMana && effects.Totals.Percent(StatKind.MaxMana) == -50, "third anima did not halve maximum mana");
        return Task.CompletedTask;
    }

    private static void WithMana(WorldPlayer player) =>
        player.Character.Effects.SetPermanent(0xFFFF0001, EffectSourceKind.System, false, [StatModifier.Flat(StatKind.MaxMana, 1000)]);
}
