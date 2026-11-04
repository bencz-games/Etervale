using System.Numerics;
using PristonTale.Application.World;
using PristonTale.Simulation.Characters;
using PristonTale.Simulation.Combat;
using PristonTale.Simulation.Items;
using PristonTale.Simulation.Modifiers;
using PristonTale.Simulation.Skills;
using PristonTale.Simulation.World.Monsters;
using PristonTale.Contracts.Combat;
using PristonTale.Contracts.Skills;
using PristonTale.Contracts.Characters;

namespace PristonTale.Tests;

// The Mechanician's skills that need more than their table: the mines around the caster, the weapon-bound attack speed,
// the four rated strikes and the party's attack damage.
internal static class MechanicianSkillTests
{
    private const uint LandMining = 0x01100001;
    private const uint HyperSonic = 0x01100002;
    private const uint RollingSmash = 0x01100003;
    private const uint PowerEnhancement = 0x01100004;

    private static readonly CharacterClassRegistry Classes = new();

    public static IEnumerable<(string, Func<Task>)> Cases()
    {
        yield return ("land mining rolls its mine damage raised by its rate plus the attack, half more on mechanic monsters", LandMiningPowerAsync);
        yield return ("land mining strikes at most its rank's count of enemies around the caster, without a target", LandMiningAreaAsync);
        yield return ("hyper sonic speeds the attack only with its weapons and is not recast while it runs", HyperSonicAsync);
        yield return ("rolling smash strikes four times with a flat then percent attack rating and its damage percent", RollingSmashAsync);
        yield return ("power enhancement raises the caster's attack damage and half of the party's maximum, and ends without its weapons",
            PowerEnhancementAsync);
    }

    private static Task LandMiningPowerAsync()
    {
        var rng = new CombatRng(3);
        var mechanician = Classes.CreatePlayer(PlayerClass.Mechanician, "Mines", 90);
        mechanician.Equip(EquipmentSlot.Weapon, SkillFixtures.Weapon(ItemKind.Sword, 40, 60));
        SkillRuleCatalog.TryGet(LandMining, out var rule);
        var attack = WeaponDamage.Compute(mechanician);
        // Rank 1: 300..350 raised 30%; rank 10: 480..620 raised 102%; the attack damage adds to both ends.
        foreach (var (rank, low, high) in new[] { (1, 390, 455), (10, 969, 1252) })
        {
            for (var roll = 0; roll < 300; roll++)
            {
                var power = SkillFormulas.RollPower(mechanician, rule, rank, rng);
                Assert.True(power >= low + attack.Min && power <= high + attack.Max, $"land mining rank {rank} power {power} outside {low}..{high} + attack");
            }
        }

        var mechanic = SkillFixtures.Monster(MonsterBrood.Mechanic);
        Assert.True(SkillFormulas.PowerAgainst(1000, rule, mechanic, ModifierTotals.Empty, ModifierTotals.Empty) == 1500,
            "land mining lacks its mechanic bonus");
        Assert.True(SkillFormulas.PowerAgainst(1000, rule, SkillFixtures.Monster(), ModifierTotals.Empty, ModifierTotals.Empty) == 1000,
            "land mining raised a normal monster");
        return Task.CompletedTask;
    }

    private static Task LandMiningAreaAsync()
    {
        var definition = SkillFixtures.Monster(life: 1_000_000);
        var field = SkillFixtures.Field(5, definition);
        var mechanician = SkillFixtures.Player(field, 1, PlayerClass.Mechanician, 90, Vector3.Zero, SkillFixtures.Weapon(ItemKind.Sword));
        mechanician.Character.Skills.Restore(LandMining, 1);
        // Rank 1: a radius of 80 and five targets; eight monsters stand at 60, one at 150.
        for (var index = 0; index < 8; index++)
        {
            SkillFixtures.Spawn(field, definition, new Vector3(MathF.Sin(index) * 60, 0, MathF.Cos(index) * 60));
        }

        var far = SkillFixtures.Spawn(field, definition, new Vector3(150, 0, 0));
        SkillFixtures.Accept("Land Mining", field, mechanician, LandMining);
        var struck = SkillFixtures.FinishCollect(field, mechanician).Where(value => value.Kind == CombatEventKind.Hit && value.Damage > 0)
            .Select(value => value.TargetId).ToHashSet();
        Assert.True(struck.Count is >= 1 and <= 5, $"land mining struck {struck.Count} enemies");
        Assert.True(!struck.Contains(far.EntityId), "land mining reached beyond its radius");
        return Task.CompletedTask;
    }

    private static Task HyperSonicAsync()
    {
        var field = SkillFixtures.Field();
        var mechanician = SkillFixtures.Player(field, 1, PlayerClass.Mechanician, 90, Vector3.Zero, SkillFixtures.Weapon(ItemKind.Sword));
        WithManaForTopRanks(mechanician);
        mechanician.Character.Skills.Restore(HyperSonic, 10);
        SkillFixtures.Accept("Hyper Sonic", field, mechanician, HyperSonic);
        SkillFixtures.Finish(field, mechanician);
        var totals = mechanician.Character.Effects.Totals;
        Assert.True(totals.Percent(StatKind.AttackSpeed) == 19 && totals.Flat(StatKind.AttackMotionSpeed) == 19,
            $"hyper sonic rank 10 gave {totals.Percent(StatKind.AttackSpeed)}% speed and {totals.Flat(StatKind.AttackMotionSpeed)} motion");
        SkillFixtures.Refuse(SkillCastVerdict.AlreadyActive, "hyper sonic recast while running", field, mechanician, HyperSonic);

        mechanician.Character.Equip(EquipmentSlot.Weapon, SkillFixtures.Weapon(ItemKind.Staff));
        totals = mechanician.Character.Effects.Totals;
        Assert.True(mechanician.Character.Effects.TryGet(HyperSonic, out _), "hyper sonic ended with another weapon");
        Assert.True(totals.Percent(StatKind.AttackSpeed) == 0 && totals.Flat(StatKind.AttackMotionSpeed) == 0, "hyper sonic sped up a staff");
        return Task.CompletedTask;
    }

    private static Task RollingSmashAsync()
    {
        var definition = SkillFixtures.Monster(life: 1_000_000);
        var field = SkillFixtures.Field(13, definition);
        var mechanician = SkillFixtures.Player(field, 1, PlayerClass.Mechanician, 90, Vector3.Zero, SkillFixtures.Weapon(ItemKind.Sword));
        mechanician.Character.Skills.Restore(RollingSmash, 10);
        var monster = SkillFixtures.Spawn(field, definition, new Vector3(30, 0, 0));
        SkillRuleCatalog.TryGet(RollingSmash, out var rule);
        var basic = CombatProfileFactory.ForPlayerBasicAttack(mechanician.Character);
        var profile = SkillFormulas.WeaponAttack(mechanician.Character, rule, 10, definition, new CombatRng(1));
        var rating = basic.AttackRating + 240;
        Assert.True(profile.AttackRating == rating + rating * 50 / 100, $"rolling smash rank 10 attack rating {profile.AttackRating} from {basic.AttackRating}");

        SkillFixtures.Accept("Rolling Smash", field, mechanician, RollingSmash, monster.EntityId);
        var impacts = 0;
        for (var tick = 0; tick < 200 && (mechanician.IsCasting || tick < 20); tick++)
        {
            field.Step(0.05f);
            impacts += field.SnapshotSkillEvents().Count(value => value.SkillCode == RollingSmash && value.Phase == SkillEventPhase.Impact);
        }

        Assert.True(impacts == 4, $"rolling smash struck {impacts} times");
        return Task.CompletedTask;
    }

    private static Task PowerEnhancementAsync()
    {
        var field = SkillFixtures.Field();
        var mechanician = SkillFixtures.Player(field, 1, PlayerClass.Mechanician, 90, Vector3.Zero, SkillFixtures.Weapon(ItemKind.Sword));
        var member = SkillFixtures.Player(field, 2, PlayerClass.Priestess, 80, new Vector3(200, 0, 0), SkillFixtures.Weapon(ItemKind.Staff));
        var distant = SkillFixtures.Player(field, 3, PlayerClass.Fighter, 80, new Vector3(600, 0, 0));
        var stranger = SkillFixtures.Player(field, 4, PlayerClass.Knight, 80, new Vector3(-100, 0, 0));
        SkillFixtures.Group(mechanician, member, distant);
        WithManaForTopRanks(mechanician);
        mechanician.Character.Skills.Restore(PowerEnhancement, 10);
        SkillFixtures.Accept("Power Enhancement", field, mechanician, PowerEnhancement);
        SkillFixtures.Finish(field, mechanician);
        // Rank 10: 58% on both ends for the caster, 29% on the maximum for the party members within 380.
        var caster = mechanician.Character.Effects.Totals;
        Assert.True(caster.Percent(StatKind.DamageMin) == 58 && caster.Percent(StatKind.DamageMax) == 58, "power enhancement on the caster");
        var share = member.Character.Effects.Totals;
        Assert.True(share.Percent(StatKind.DamageMin) == 0 && share.Percent(StatKind.DamageMax) == 29, "power enhancement on a party member");
        Assert.True(distant.Character.Effects.Totals.Percent(StatKind.DamageMax) == 0, "power enhancement reached a member beyond its range");
        Assert.True(stranger.Character.Effects.Totals.Percent(StatKind.DamageMax) == 0, "power enhancement reached a player outside the party");
        SkillFixtures.Refuse(SkillCastVerdict.AlreadyActive, "power enhancement recast while running", field, mechanician, PowerEnhancement);

        mechanician.Character.Equip(EquipmentSlot.Weapon, SkillFixtures.Weapon(ItemKind.Staff));
        SkillFixtures.Wait(field, 0.2f);
        Assert.True(!mechanician.Character.Effects.TryGet(PowerEnhancement, out _), "power enhancement stayed on a mechanician without its weapons");
        Assert.True(member.Character.Effects.TryGet(PowerEnhancement, out _), "a staff ended power enhancement on a priestess");
        return Task.CompletedTask;
    }

    // A level-90 Mechanician without gear lacks the mana of the buffs' top ranks (125 and 130).
    private static void WithManaForTopRanks(WorldPlayer player) =>
        player.Character.Effects.SetPermanent(0xFFFF0001, EffectSourceKind.System, false, [StatModifier.Flat(StatKind.MaxMana, 500)]);
}
