using System.Numerics;
using PristonTale.Simulation.Characters;
using PristonTale.Simulation.Combat;
using PristonTale.Simulation.Items;
using PristonTale.Simulation.Modifiers;
using PristonTale.Contracts.Combat;
using PristonTale.Contracts.Characters;

namespace PristonTale.Tests;

// The damage rules the balance is measured with: the hit chances' one table lookup, the cut of a hit on a stronger
// monster, no critical with a staff or a phantom, and the evasion and block a monster's skill blow still meets.
internal static class CombatRulesTests
{
    public static IEnumerable<(string, Func<Task>)> Cases()
    {
        yield return ("a player's and a monster's hit chances take their bracket's upper end, the first below the table and the last above it", HitChancesAsync);
        yield return ("a hit above a quarter of a stronger monster's life is rolled again between an eighth and a quarter of it, at least 1", CutHitAsync);
        yield return ("a staff or a phantom in hand deals no critical hit", StaffCriticalAsync);
        yield return ("a monster's skill blow never misses but a shield still blocks it, one that lands loses the player's absorption as flat points, and a player without a block chance never blocks", SkillBlockAsync);
    }

    private static AttackProfile Attack(int rating, int level = 10) => new(rating, 1, 1, level, Element.Bionic, 0, 0, 0);

    private static DefenderProfile Defender(int defence, int level = 10, int block = 0) =>
        new(defence, 0, block, level, ResistanceBlock.Zero, 100, 100, false);

    private static Task HitChancesAsync()
    {
        // (993 - 1000) x 1.4 is -9: the bracket from -20 to 0 gives its upper end's 69, not its lower end's 68.
        Assert.True(CombatResolver.HitChance(Attack(993), Defender(1000)) == 69 && CombatResolver.HitChance(Attack(800), Defender(1000)) == 50
            && CombatResolver.HitChance(Attack(4000), Defender(1000)) == 95, "the player's hit chance did not take its bracket's upper end");
        // (810 - 1000) x 2 is -380, the table's first threshold: past it, the next bracket's 51.
        Assert.True(CombatResolver.MonsterHitChance(Attack(810), Defender(1000)) == 51 && CombatResolver.MonsterHitChance(Attack(809), Defender(1000)) == 50
            && CombatResolver.MonsterHitChance(Attack(3000), Defender(1000)) == 95, "the monster's hit chance missed a row of the table");
        return Task.CompletedTask;
    }

    private static Task CutHitAsync()
    {
        var rng = new CombatRng(3);
        for (var roll = 0; roll < 50; roll++)
        {
            var cut = CombatResolver.CutHit(rng, 600, 1000, 20, 10);
            Assert.True(cut is >= 125 and <= 250, $"a cut hit dealt {cut}");
        }

        Assert.True(CombatResolver.CutHit(rng, 250, 1000, 20, 10) == 250 && CombatResolver.CutHit(rng, 600, 1000, 10, 10) == 600
            && CombatResolver.CutHit(rng, 50, 3, 20, 10) == 1, "a hit within a quarter, on a monster not above the attacker or of almost no life was cut");
        return Task.CompletedTask;
    }

    private static Task StaffCriticalAsync()
    {
        var classes = new CharacterClassRegistry();
        foreach (var (kind, critical) in new[] { (ItemKind.Staff, false), (ItemKind.Phantom, false), (ItemKind.Sword, true) })
        {
            var character = classes.CreatePlayer(PlayerClass.Magician, $"Wielder {kind}", 50);
            character.Equip(EquipmentSlot.Weapon, SkillFixtures.Weapon(kind));
            character.Effects.Apply(new EffectSpec(0x0A00_0003, EffectSourceKind.Item, false, [StatModifier.Flat(StatKind.CriticalChance, 30)]), 7,
                float.PositiveInfinity);
            var attack = CombatProfileFactory.ForPlayerBasicAttack(character);
            Assert.True(CombatProfileFactory.WeaponCanCritical(character) == critical && (attack.CritChance > 0) == critical,
                $"a {kind} in hand gives a critical chance of {attack.CritChance}");
        }

        return Task.CompletedTask;
    }

    private static Task SkillBlockAsync()
    {
        var rng = new CombatRng(4);
        for (var roll = 0; roll < 200; roll++)
        {
            Assert.True(!CombatResolver.RollBlock(rng, Defender(0, level: 100), 1, true), "a player without a block chance blocked");
        }

        var definition = MonsterSkillField.Monster(0, skillMin: 30, skillMax: 30);
        var world = new MonsterSkillField(definition);
        var player = world.AddPlayer(1, new Vector3(50, 0, 0));
        player.Character.Equip(EquipmentSlot.OffHand, SkillFixtures.Shield());
        player.Character.Effects.Apply(new EffectSpec(0x0A00_0002, EffectSourceKind.Item, false, [StatModifier.Flat(StatKind.BlockChance, 100)]), 7,
            float.PositiveInfinity);
        var monster = world.Spawn(definition, Vector3.Zero, 1);
        world.StepUntil(() => world.Events.Count(value => value.Kind == CombatEventKind.MonsterSkillImpact && value.AttackerId == monster.EntityId) >= 40, 4000,
            "the monster did not cast its skill forty times");
        var blocked = world.Events.Count(value => value.Kind == CombatEventKind.Block && value.AttackerId == monster.EntityId);
        var struck = world.Events.Where(value => value.Kind == CombatEventKind.Hit && value.AttackerId == monster.EntityId && value.Skill).ToList();
        Assert.True(blocked > 0 && struck.Count > 0 && world.Events.All(value => value.Kind != CombatEventKind.Miss || value.AttackerId != monster.EntityId),
            $"the skill blows missed, or the shield blocked {blocked} and let {struck.Count} through");
        var absorption = CombatProfileFactory.ForPlayerDefender(player.Character).Absorption;
        Assert.True(struck.All(value => value.Damage == Math.Max(1, 30 - absorption)), "a landed skill blow did not lose the player's absorption as flat points");
        return Task.CompletedTask;
    }
}
