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

// The Knight's skills that need more than their table: the strike that leaves with the cast, the holy blade whose
// second hit borrows Grand Cross's accuracy and bites the undead harder, and the blessing against the undead that
// reaches the party.
internal static class KnightSkillTests
{
    private const uint GrandCross = 0x05040004;
    private const uint SoulBreaker = 0x05100001;
    private const uint SaintBlade = 0x05100003;
    private const uint HolyBenedic = 0x05100004;

    private static readonly CharacterClassRegistry Classes = new();

    public static IEnumerable<(string, Func<Task>)> Cases()
    {
        yield return ("soul breaker strikes as the cast starts with its flat attack rating", SoulBreakerAsync);
        yield return ("saint blade's second hit borrows grand cross's accuracy and both bite the undead harder", SaintBladeAsync);
        yield return ("holy benedic reaches the caster or a clicked party member and the party within 200", HolyBenedicAsync);
    }

    private static Task SoulBreakerAsync()
    {
        var definition = SkillFixtures.Monster(life: 1_000_000);
        var field = SkillFixtures.Field(41, definition);
        var knight = SkillFixtures.Player(field, 1, PlayerClass.Knight, 90, Vector3.Zero, SkillFixtures.Weapon(ItemKind.Sword));
        WithMana(knight);
        knight.Character.Skills.Restore(SoulBreaker, 10);
        SkillRuleCatalog.TryGet(SoulBreaker, out var rule);
        var basic = CombatProfileFactory.ForPlayerBasicAttack(knight.Character);
        Assert.True(SkillFormulas.WeaponAttack(knight.Character, rule, 10, definition, new CombatRng(1)).AttackRating == basic.AttackRating + 85,
            "soul breaker rank 10 does not add 85 attack rating");
        var monster = SkillFixtures.Spawn(field, definition, new Vector3(30, 0, 0));
        SkillFixtures.Accept("Soul Breaker", field, knight, SoulBreaker, monster.EntityId);
        var first = -1;
        for (var tick = 0; tick < 100 && first < 0; tick++)
        {
            field.Step(0.05f);
            if (field.SnapshotSkillEvents().Any(value => value.SkillCode == SoulBreaker && value.Phase == SkillEventPhase.Impact))
            {
                first = tick;
            }
        }

        Assert.True(first is >= 0 and <= 2, $"soul breaker struck {first} ticks into the cast");
        return Task.CompletedTask;
    }

    private static Task SaintBladeAsync()
    {
        var knight = Classes.CreatePlayer(PlayerClass.Knight, "Blade", 90);
        knight.Equip(EquipmentSlot.Weapon, SkillFixtures.Weapon(ItemKind.Sword));
        SkillRuleCatalog.TryGet(SaintBlade, out var rule);
        var monster = SkillFixtures.Monster(MonsterBrood.Undead);
        var before = SkillFormulas.WeaponAttack(knight, rule, 1, monster, new CombatRng(1), 1).AttackRating;
        knight.Skills.Restore(GrandCross, 10);
        var first = SkillFormulas.WeaponAttack(knight, rule, 1, monster, new CombatRng(1), 0).AttackRating;
        var second = SkillFormulas.WeaponAttack(knight, rule, 1, monster, new CombatRng(1), 1).AttackRating;
        Assert.True(first == before && second == before + before * 80 / 100, $"saint blade ratings {before}, {first}, {second}");
        Assert.True(SkillFormulas.WithBroodBonus(1000, rule, monster, 10) == 1500, "saint blade rank 10 lacks its 50% against the undead");
        Assert.True(SkillFormulas.WithBroodBonus(1000, rule, SkillFixtures.Monster(), 10) == 1000, "saint blade raised a living monster");
        return Task.CompletedTask;
    }

    private static Task HolyBenedicAsync()
    {
        var field = SkillFixtures.Field();
        var knight = SkillFixtures.Player(field, 1, PlayerClass.Knight, 90, Vector3.Zero, SkillFixtures.Weapon(ItemKind.Sword));
        var member = SkillFixtures.Player(field, 2, PlayerClass.Priestess, 80, new Vector3(150, 0, 0));
        var distant = SkillFixtures.Player(field, 3, PlayerClass.Magician, 80, new Vector3(400, 0, 0));
        var stranger = SkillFixtures.Player(field, 4, PlayerClass.Fighter, 80, new Vector3(-100, 0, 0));
        SkillFixtures.Group(knight, member, distant);
        WithMana(knight);
        knight.Character.Skills.Restore(HolyBenedic, 10);
        SkillFixtures.Accept("Holy Benedic", field, knight, HolyBenedic);
        SkillFixtures.Finish(field, knight);
        Assert.True(Undead(knight) == 70 && Undead(member) == 70, "holy benedic rank 10 missing on the caster or a party member");
        Assert.True(Undead(distant) == 0 && Undead(stranger) == 0, "holy benedic reached a far member or a player outside the party");

        var other = SkillFixtures.Field();
        var caster = SkillFixtures.Player(other, 1, PlayerClass.Knight, 90, Vector3.Zero, SkillFixtures.Weapon(ItemKind.Sword));
        var friend = SkillFixtures.Player(other, 2, PlayerClass.Atalanta, 80, new Vector3(40, 0, 0));
        SkillFixtures.Group(caster, friend);
        WithMana(caster);
        caster.Character.Skills.Restore(HolyBenedic, 1);
        SkillFixtures.Accept("Holy Benedic on a player", other, caster, HolyBenedic, friend.EntityId);
        SkillFixtures.Finish(other, caster);
        Assert.True(Undead(friend) == 25 && Undead(caster) == 25, "holy benedic on a clicked player missed that player or the caster");
        return Task.CompletedTask;
    }

    private static int Undead(WorldPlayer player) => player.Character.Effects.Totals.Flat(StatKind.DamageVsUndead);

    private static void WithMana(WorldPlayer player) =>
        player.Character.Effects.SetPermanent(0xFFFF0001, EffectSourceKind.System, false, [StatModifier.Flat(StatKind.MaxMana, 500)]);
}
