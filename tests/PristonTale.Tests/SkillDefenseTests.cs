using System.Numerics;
using PristonTale.Application.World;
using PristonTale.Simulation.Characters;
using PristonTale.Simulation.Items;
using PristonTale.Simulation.Modifiers;
using PristonTale.Simulation.World.Ai;
using PristonTale.Simulation.World.Monsters;
using PristonTale.Contracts.Combat;
using PristonTale.Contracts.Skills;
using PristonTale.Contracts.Characters;

namespace PristonTale.Tests;

// Defence effects against monster attacks: reflection, absorb shields paid with mana, damage-taken shields, life from
// blocked or landed undead attacks and evasion.
internal static class SkillDefenseTests
{
    public static IEnumerable<(string, Func<Task>)> Cases()
    {
        yield return ("holy reflection returns a share of an undead attacker's damage to it", ReflectionAsync);
        yield return ("an energy shield takes its share of each hit out of mana instead of life", AbsorbAsync);
        yield return ("a godly shield lowers the damage of normal attacks", DamageTakenAsync);
        yield return ("a divine shield turns blocked undead attacks into life and evasion skips attacks", GuardAsync);
        yield return ("evasion mastery evades about its rank's share of normal attacks", EvasionMasteryAsync);
        yield return ("a spark shield strikes back from the spark table when its shield blocks", CounterAsync);
        yield return ("an energy shield's share, a reflection, a divine inhalation's life and the hits on a physical absorption or, from the undead "
            + "only, on a holy body report a Defended impact for their defence effect; a guard without a defence effect "
            + "and a player without these report nothing", DefendedAsync);
    }

    private static Task DefendedAsync()
    {
        var (field, mage, monster) = Duel(PlayerClass.Magician, SkillFixtures.Weapon(ItemKind.Staff), null);
        mage.Character.Skills.Restore(0x08040001, 10);
        SkillFixtures.Accept("Energy Shield", field, mage, 0x08040001);
        SkillFixtures.Finish(field, mage);
        field.Step(0.001f);
        Attack(field, mage, monster, out _);
        Assert.True(Defended(field, 0x08040001, mage, monster.EntityId) == 1, "the energy shield's share did not report its defence");

        var undead = SkillFixtures.Monster(MonsterBrood.Undead, life: 1_000_000, damage: 100);
        var crypt = SkillFixtures.Field(11, undead);
        var priest = SkillFixtures.Player(crypt, 1, PlayerClass.Priestess, 40, Vector3.Zero, SkillFixtures.Weapon(ItemKind.Staff));
        var ghoul = SkillFixtures.Spawn(crypt, undead, new Vector3(40, 0, 0));
        priest.Character.Effects.Apply(new EffectSpec(0x07020003, EffectSourceKind.PlayerSkill, false, []), 1, 60000f);
        SkillFixtures.Strike(crypt, ghoul, priest);
        Assert.True(Defended(crypt, 0x07020003, priest, ghoul.EntityId) == 1, "the reflection did not report its defence");
        crypt.Step(0.001f);

        var knight = SkillFixtures.Player(crypt, 2, PlayerClass.Knight, 80, new Vector3(0, 0, 30), SkillFixtures.Weapon(ItemKind.Sword), SkillFixtures.Shield());
        knight.Character.Skills.Restore(0x05040002, 10);
        SkillFixtures.Accept("Divine Inhalation", crypt, knight, 0x05040002);
        SkillFixtures.Finish(crypt, knight);
        crypt.Step(0.001f);
        var (heals, inhaled) = (0, 0);
        for (var attack = 0; attack < 200; attack++)
        {
            knight.Character.Life = knight.Character.MaxLife / 2;
            SkillFixtures.Strike(crypt, ghoul, knight);
            heals += (crypt.SnapshotEvents() ?? []).Count(value => value.Kind == CombatEventKind.Heal && value.TargetId == knight.EntityId);
            inhaled += Defended(crypt, 0x05040002, knight, ghoul.EntityId);
            crypt.Step(0.001f);
        }

        Assert.True(heals > 0 && inhaled == heals, $"{inhaled} defences reported for {heals} lives a divine inhalation returned");

        var summoner = SkillFixtures.Player(crypt, 4, PlayerClass.Priestess, 80, new Vector3(30, 0, 0), SkillFixtures.Weapon(ItemKind.Staff));
        summoner.Character.Effects.Apply(new EffectSpec(0x07080004, EffectSourceKind.PlayerSkill, false, []), 1, 60000f);
        for (var attack = 0; attack < 20; attack++)
        {
            summoner.Character.Life = summoner.Character.MaxLife / 2;
            SkillFixtures.Strike(crypt, ghoul, summoner);
        }

        Assert.True(crypt.SnapshotSkillEvents().Count(value => (value.Flags & SkillEventFlags.Defended) != 0) == 0,
            "a guard without a defence effect (Summon Muspell) reported one");
        crypt.Step(0.001f);

        var holy = SkillFixtures.Player(crypt, 3, PlayerClass.Knight, 80, new Vector3(0, 0, -30), SkillFixtures.Weapon(ItemKind.Sword));
        holy.Character.Effects.Apply(new EffectSpec(0x05010002, EffectSourceKind.PlayerSkill, false, []), 1, 60000f);
        Attack(crypt, holy, ghoul, out _);
        Assert.True(Defended(crypt, 0x05010002, holy, ghoul.EntityId) == 1, "a holy body hit by the undead did not report its defence");
        crypt.Step(0.001f);

        var (plain, mechanic, beast) = Duel(PlayerClass.Mechanician, SkillFixtures.Weapon(ItemKind.Claw), null);
        mechanic.Character.Effects.Apply(new EffectSpec(0x01010003, EffectSourceKind.PlayerSkill, false, []), 1, 60000f);
        var body = SkillFixtures.Player(plain, 2, PlayerClass.Knight, 70, new Vector3(0, 0, 30), SkillFixtures.Weapon(ItemKind.Sword));
        body.Character.Effects.Apply(new EffectSpec(0x05010002, EffectSourceKind.PlayerSkill, false, []), 1, 60000f);
        var bare = SkillFixtures.Player(plain, 3, PlayerClass.Fighter, 70, new Vector3(0, 0, -30), SkillFixtures.Weapon(ItemKind.Axe));
        Attack(plain, mechanic, beast, out _);
        Attack(plain, body, beast, out _);
        Attack(plain, bare, beast, out _);
        Assert.True(Defended(plain, 0x01010003, mechanic, beast.EntityId) == 1 && Defended(plain, 0x05010002, body, beast.EntityId) == 0
            && plain.SnapshotSkillEvents().Count(value => (value.Flags & SkillEventFlags.Defended) != 0) == 1,
            "a physical absorption did not report its defence, a holy body reported one from a living monster or a player without either did");
        plain.Step(0.001f);

        plain.Combat.HurtPlayer(bare, mechanic, 50, false);
        plain.Combat.HurtPlayer(bare, body, 50, false);
        Assert.True(Defended(plain, 0x01010003, mechanic, bare.EntityId) == 1 && Defended(plain, 0x05010002, body, bare.EntityId) == 0,
            "another player's hit did not show the physical absorption or showed the holy body");
        return Task.CompletedTask;
    }

    private static int Defended(FieldInstance field, uint code, WorldPlayer holder, ulong attackerId) => field.SnapshotSkillEvents().Count(value =>
        (value.Flags & SkillEventFlags.Defended) != 0 && value.SkillCode == code && value.CasterId == holder.EntityId && value.TargetId == attackerId);

    private static int Evaded(int rank)
    {
        var definition = SkillFixtures.Monster(damage: 10);
        var field = SkillFixtures.Field(23, definition);
        var archer = SkillFixtures.Player(field, 1, PlayerClass.Archer, 80, Vector3.Zero);
        if (rank > 0)
        {
            archer.Character.Skills.Restore(0x04080002, rank);
        }

        var monster = SkillFixtures.Spawn(field, definition, new Vector3(40, 0, 0));
        for (var attack = 0; attack < 400; attack++)
        {
            archer.Character.Life = archer.Character.MaxLife;
            SkillFixtures.Strike(field, monster, archer);
        }

        return (field.SnapshotEvents() ?? []).Count(value => value.Kind == CombatEventKind.Evade);
    }

    private static Task EvasionMasteryAsync()
    {
        Assert.True(Evaded(0) == 0, "an archer without the passive evaded");
        var evaded = Evaded(10);
        Assert.True(evaded is >= 70 and <= 150, $"rank 10 evaded {evaded} of 400 attacks, expected about 27 %");
        return Task.CompletedTask;
    }

    private static Task CounterAsync()
    {
        var definition = SkillFixtures.Monster(damage: 10);
        var field = SkillFixtures.Field(29, definition);
        var mechanic = SkillFixtures.Player(field, 1, PlayerClass.Mechanician, 80, Vector3.Zero, SkillFixtures.Weapon(ItemKind.Claw));
        mechanic.Character.Equip(EquipmentSlot.OffHand, SkillFixtures.Shield());
        mechanic.Character.Skills.Restore(0x01040004, 3);
        mechanic.Character.Effects.Apply(new EffectSpec(0x0A00_0002, EffectSourceKind.Item, false, [StatModifier.Flat(StatKind.BlockChance, 100)]), 7,
            float.PositiveInfinity);
        SkillFixtures.Accept("Spark Shield", field, mechanic, 0x01040004);
        SkillFixtures.Finish(field, mechanic);
        var monster = SkillFixtures.Spawn(field, definition, new Vector3(40, 0, 0));
        SkillFixtures.Strike(field, monster, mechanic);
        Assert.True(monster.Life == monster.Def.Combat.MaxLife, "the counter struck without the spark it borrows its table from");

        mechanic.Character.Skills.Restore(0x01020004, 5);
        for (var attack = 0; attack < 10; attack++)
        {
            SkillFixtures.Strike(field, monster, mechanic);
        }

        var lost = monster.Def.Combat.MaxLife - monster.Life;
        Assert.True(lost > 0 && field.SnapshotSkillEvents().Any(value => (value.Flags & SkillEventFlags.Counter) != 0),
            "a blocked attack did not bring the counter strike");
        return Task.CompletedTask;
    }

    private static Task ReflectionAsync()
    {
        var undead = SkillFixtures.Monster(MonsterBrood.Undead, damage: 100);
        var field = SkillFixtures.Field(11, undead);
        var priest = SkillFixtures.Player(field, 1, PlayerClass.Priestess, 40, Vector3.Zero, SkillFixtures.Weapon(ItemKind.Staff));
        var monster = SkillFixtures.Spawn(field, undead, new Vector3(40, 0, 0));
        priest.Character.Skills.Restore(0x07020003, 10);
        SkillFixtures.Accept("Holy Reflection", field, priest, 0x07020003);
        SkillFixtures.Finish(field, priest);
        var life = monster.Life;
        SkillFixtures.Strike(field, monster, priest);
        Assert.True(monster.Life == life - 110, $"reflection dealt {life - monster.Life}, expected 110");
        var normal = SkillFixtures.Monster(MonsterBrood.Normal, damage: 100) with { Code = 8 };
        var other = SkillFixtures.Field(11, normal);
        var plain = SkillFixtures.Player(other, 1, PlayerClass.Priestess, 40, Vector3.Zero, SkillFixtures.Weapon(ItemKind.Staff));
        plain.Character.Effects.Apply(new EffectSpec(0x07020003, EffectSourceKind.PlayerSkill, false, []), 1, 60000f);
        var target = SkillFixtures.Spawn(other, normal, new Vector3(40, 0, 0));
        SkillFixtures.Strike(other, target, plain);
        Assert.True(target.Life == target.Def.Combat.MaxLife, "reflection struck a monster that is not undead");
        return Task.CompletedTask;
    }

    private static Task AbsorbAsync()
    {
        var (field, mage, monster) = Duel(PlayerClass.Magician, SkillFixtures.Weapon(ItemKind.Staff), null);
        var lost = Attack(field, mage, monster, out _);
        mage.Character.Skills.Restore(0x08040001, 10);
        SkillFixtures.Accept("Energy Shield", field, mage, 0x08040001);
        SkillFixtures.Finish(field, mage);
        mage.Character.Life = mage.Character.MaxLife;
        mage.Character.Mana = mage.Character.MaxMana;
        var shielded = Attack(field, mage, monster, out var manaLost);
        Assert.True(shielded == lost - lost * 30 / 100 && manaLost == lost * 30 / 100, $"shield split {lost} into {shielded} life and {manaLost} mana");
        mage.Character.Mana = 0;
        var empty = Attack(field, mage, monster, out _);
        Assert.True(empty == shielded && mage.Character.Mana == 0, "the shield stopped at zero mana or drove mana negative");
        return Task.CompletedTask;
    }

    private static Task DamageTakenAsync()
    {
        var (field, knight, monster) = Duel(PlayerClass.Knight, SkillFixtures.Weapon(ItemKind.Sword), SkillFixtures.Shield());
        var lost = Attack(field, knight, monster, out _);
        knight.Character.Skills.Restore(0x05080002, 10);
        SkillFixtures.Accept("Godly Shield", field, knight, 0x05080002);
        SkillFixtures.Finish(field, knight);
        knight.Character.Life = knight.Character.MaxLife;
        var shielded = Attack(field, knight, monster, out _);
        Assert.True(shielded == lost * 80 / 100, $"Godly Shield let {shielded} of {lost} through");
        return Task.CompletedTask;
    }

    private static Task GuardAsync()
    {
        var undead = SkillFixtures.Monster(MonsterBrood.Undead, life: 1_000_000, damage: 100);
        var field = SkillFixtures.Field(5, undead);
        var knight = SkillFixtures.Player(field, 1, PlayerClass.Knight, 80, Vector3.Zero, SkillFixtures.Weapon(ItemKind.Sword), SkillFixtures.Shield());
        var monster = SkillFixtures.Spawn(field, undead, new Vector3(40, 0, 0));
        knight.Character.Skills.Restore(0x05040002, 10);
        SkillFixtures.Accept("Divine Inhalation", field, knight, 0x05040002);
        SkillFixtures.Finish(field, knight);
        var heals = 0;
        for (var attack = 0; attack < 300; attack++)
        {
            knight.Character.Life = knight.Character.MaxLife / 2;
            SkillFixtures.Strike(field, monster, knight);
            heals += (field.SnapshotEvents() ?? []).Count(value => value.Kind == CombatEventKind.Heal && value.TargetId == knight.EntityId && value.Damage == 90);
            field.Step(0.001f);
        }

        Assert.True(heals > 10, $"only {heals} blocked attacks returned 90% of their damage as life");

        var atalanta = SkillFixtures.Player(field, 2, PlayerClass.Atalanta, 80, new Vector3(0, 0, 30), SkillFixtures.Weapon(ItemKind.Bow));
        atalanta.Character.Skills.Restore(0x06080001, 10);
        SkillFixtures.Accept("Hall of Valhalla", field, atalanta, 0x06080001);
        SkillFixtures.Finish(field, atalanta);
        var evaded = 0;
        for (var attack = 0; attack < 300; attack++)
        {
            atalanta.Character.Life = atalanta.Character.MaxLife;
            SkillFixtures.Strike(field, monster, atalanta);
            evaded += (field.SnapshotEvents() ?? []).Count(value => value.Kind == CombatEventKind.Evade);
            field.Step(0.001f);
        }

        Assert.True(evaded is > 60 and < 140, $"32% evasion evaded {evaded} of 300 attacks");
        return Task.CompletedTask;
    }

    private static (FieldInstance Field, WorldPlayer Player, MonsterInstance Monster) Duel(PlayerClass playerClass, ItemInstance weapon, ItemInstance? offHand)
    {
        var definition = SkillFixtures.Monster(MonsterBrood.Normal, damage: 100) with { Combat = SkillFixtures.Monster().Combat with { AttackRatingAccuracy = 1_000_000 } };
        var field = SkillFixtures.Field(3, definition);
        var player = SkillFixtures.Player(field, 1, playerClass, 70, Vector3.Zero, weapon, offHand);
        var monster = SkillFixtures.Spawn(field, definition, new Vector3(40, 0, 0));
        return (field, player, monster);
    }

    // One landed monster attack: the life it took and the mana it drained (repeated until it is not blocked or missed).
    private static int Attack(FieldInstance field, WorldPlayer player, MonsterInstance monster, out int manaLost)
    {
        for (var attempt = 0; attempt < 200; attempt++)
        {
            var life = player.Character.Life;
            var mana = player.Character.Mana;
            SkillFixtures.Strike(field, monster, player);
            if (player.Character.Life < life)
            {
                manaLost = mana - player.Character.Mana;
                return life - player.Character.Life;
            }
        }

        throw new InvalidOperationException("no monster attack landed");
    }
}
