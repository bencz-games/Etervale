using System.Numerics;
using PristonTale.Application.World;
using PristonTale.Simulation.Characters;
using PristonTale.Simulation.Skills;
using PristonTale.Simulation.World.Monsters;
using PristonTale.Contracts.Skills;
using PristonTale.Contracts.Characters;

namespace PristonTale.Tests;

internal static class SkillSummonTests
{
    private const uint MetalGolem = 0x01080004;
    private const uint RecallWolverine = 0x04080001;

    public static IEnumerable<(string, Func<Task>)> Cases()
    {
        yield return ("a summon skill calls one owned monster with its rank values, fights for its owner and is never a target", CallAsync);
        yield return ("a summon follows its owner, is recalled from afar, obeys its mode and ends on death, expiry or its owner leaving", LifecycleAsync);
        yield return ("fire elemental calls a faster summon and a charmed undead fights for its knight until the charm ends", MorionSummonsAsync);
        yield return ("a summon whose monster is missing from the catalog is refused before it spends mana", MissingMonsterAsync);
    }

    // Monsters far from every player are despawned, so the enemy is placed only when the scenario needs it.
    private static SkillScenario Golem(int rank = 3)
    {
        var scenario = SkillScenario.Create(PlayerClass.Mechanician, 80, catalog: [SkillScenario.Enemy(), SkillScenario.SummonModel("Metal Golem", 40)]);
        scenario.Learn(MetalGolem, rank);
        return scenario;
    }

    private static Task CallAsync()
    {
        var scenario = Golem();
        var character = scenario.Character;
        Assert.True(scenario.Cast(MetalGolem).Accepted, "Metal Golem refused");
        Assert.True(!scenario.Summons.Any(), "the golem appeared before the motion event");
        Assert.True(scenario.Run(80, () => scenario.Summons.Any()) > 0, "the golem never appeared");
        var golem = scenario.Summons.Single();
        Assert.True(golem.OwnerId == scenario.Player.EntityId && golem.SummonSkill == MetalGolem && golem.Def.Faction == MonsterFaction.Npc
            && golem.Def.Brood == MonsterBrood.User, "the summon is not owned");
        Assert.True(golem.Def.Combat.MaxLife == 4500 + character.Attributes.Strength * 2 && golem.Def.Combat.Defence == 445
            && golem.Def.Combat.AttackDamage == new MonsterRange(105, 125) && golem.Def.Combat.AttackRatingAccuracy == 700,
            "rank 3 golem values differ from the skill table");
        Assert.Near((256f + 2 * 15f) / 256f, golem.ModelScale, 0.0001f);
        Assert.True(golem.Aggressive && Vector3.Distance(golem.Position, scenario.Player.Position) < 60f, "the golem is not an aggressive helper beside its owner");
        Assert.True(character.Effects.TryGet(MetalGolem, out var status) && Math.Abs(status.RemainingMs - 355000f) < 5000f,
            "the owner has no status with the summon's lifetime");
        Assert.True(scenario.Statuses.Any(value => value.StatusCode == MetalGolem && value.Op == StatusEffectOp.Add), "the keep icon was not sent");
        var view = scenario.Field.BuildViews().Single(value => value.EntityId == golem.EntityId);
        Assert.True(view.OwnerId == scenario.Player.EntityId && Math.Abs(view.ModelScale - golem.ModelScale) < 0.0001f, "the view lost the owner or scale");

        Assert.True(!scenario.Field.SetAttackTarget(scenario.Player.EntityId, golem.EntityId, true), "a player could attack a summon");
        var enemy = scenario.Spawn(SkillScenario.Enemy(), new Vector3(120, 0, 0));
        var life = enemy.Life;
        Assert.True(scenario.Run(20 * 20, () => enemy.Life < life) > 0, "the aggressive golem never struck a nearby enemy");
        Assert.True(life - enemy.Life is >= 1 and <= 125, $"golem blow {life - enemy.Life} outside its damage range");
        Assert.True(enemy.TargetId == golem.EntityId, "the struck enemy did not turn on the golem");
        Assert.True(scenario.Combat.Any(value => value.AttackerId == golem.EntityId && value.TargetId == enemy.EntityId), "the golem's blow was not shown");
        return Task.CompletedTask;
    }

    private static Task MissingMonsterAsync()
    {
        var scenario = SkillScenario.Create(PlayerClass.Mechanician, 80, catalog: [SkillScenario.Enemy()]);
        scenario.Learn(MetalGolem, 3);
        var mana = scenario.Character.Mana;
        var outcome = scenario.Cast(MetalGolem);
        Assert.True(outcome.Verdict == SkillCastVerdict.NotCastable, $"a summon without its monster was {outcome.Verdict}");
        Assert.True(scenario.Character.Mana == mana && !scenario.Summons.Any(), "the refused summon spent mana or called a monster");
        return Task.CompletedTask;
    }

    private static Task MorionSummonsAsync()
    {
        var model = SkillScenario.SummonModel("Fire Elemental", 42);
        var mage = SkillScenario.Create(PlayerClass.Magician, 100, SkillScenario.Weapon(Simulation.Items.ItemKind.Staff, "WM101"), catalog: [model]);
        mage.Learn(0x08080001, 10);
        var call = mage.Cast(0x08080001);
        Assert.True(call.Accepted, $"Fire Elemental refused: {call.Verdict}");
        Assert.True(mage.Run(80, () => mage.Summons.Any()) > 0, "the fire elemental never appeared");
        var elemental = mage.Summons.Single();
        Assert.True(elemental.Def.Combat.MaxLife == 3800 && elemental.Def.Combat.AttackDamage == new MonsterRange(125, 140)
            && elemental.Def.Ai.MoveSpeed == model.Ai.MoveSpeed * 2 && elemental.Aggressive, "fire elemental values differ from rank 10");

        var undead = SkillScenario.Enemy("Charm target", life: 1000, brood: MonsterBrood.Undead);
        var knight = SkillScenario.Create(PlayerClass.Knight, 100, SkillScenario.Weapon(Simulation.Items.ItemKind.Sword, "WS201"), catalog: [undead]);
        knight.Learn(0x05040003, 10);
        var ghost = knight.Spawn(undead, new Vector3(0, 0, 80));
        var enemy = knight.Spawn(SkillScenario.Enemy(), new Vector3(0, 0, 160));
        for (var attempt = 0; attempt < 8 && !ghost.IsSummon; attempt++)
        {
            knight.Refill();
            knight.ClearCooldowns();
            ghost.Position = new Vector3(0, 0, 80);
            var charm = knight.Cast(0x05040003, ghost.EntityId);
            Assert.True(charm.Accepted, $"Holy Incantation refused: {charm.Verdict}");
            knight.Run(80, () => !knight.Player.IsCasting);
        }

        Assert.True(ghost.IsSummon && ghost.OwnerId == knight.Player.EntityId && ghost.Effects.Has(Simulation.Modifiers.ControlFlags.Charmed),
            "the charmed undead did not join its knight");
        knight.Player.CombatTargetId = enemy.EntityId;
        var life = enemy.Life;
        Assert.True(knight.Run(20 * 10, () => enemy.Life < life) > 0, "the charmed undead did not fight its knight's target");
        ghost.ExpiresAtMs = knight.Field.NowMs;
        knight.Run(2);
        Assert.True(!ghost.IsAlive, "the charmed undead outlived its charm");
        return Task.CompletedTask;
    }

    private static Task LifecycleAsync()
    {
        var scenario = Golem();
        Assert.True(scenario.Cast(MetalGolem).Accepted, "Metal Golem refused");
        scenario.Run(80, () => scenario.Summons.Any());
        var golem = scenario.Summons.Single();

        scenario.Player.Teleport(new Vector3(0, 0, 260));
        Assert.True(scenario.Run(20 * 10, () => Vector3.Distance(golem.Position, scenario.Player.Position) < 120f) > 0, "the golem did not follow");
        scenario.Player.Teleport(new Vector3(0, 0, 1200));
        scenario.Run(2);
        Assert.True(MathF.Abs(Vector3.Distance(golem.Position, scenario.Player.Position) - 48f) < 2f, "the golem was not recalled beside a far owner");

        var enemy = scenario.Spawn(SkillScenario.Enemy(), scenario.Player.Position + new Vector3(110, 0, 0));
        Assert.True(scenario.Cast(MetalGolem, command: SkillCastCommand.SummonPassive).Accepted && !golem.Aggressive, "passive order refused");
        Assert.True(scenario.Cast(RecallWolverine, command: SkillCastCommand.SummonAggressive).Verdict == SkillCastVerdict.Rejected,
            "an order for another summon skill was accepted");
        var life = enemy.Life;
        scenario.Run(20 * 6);
        Assert.True(enemy.Life == life, "a passive golem attacked an enemy its owner does not fight");
        scenario.Player.CombatTargetId = enemy.EntityId;
        Assert.True(scenario.Run(20 * 10, () => enemy.Life < life) > 0, "a passive golem ignored its owner's target");
        scenario.Player.CombatTargetId = 0;

        scenario.Refill();
        scenario.ClearCooldowns();
        Assert.True(scenario.Cast(MetalGolem).Verdict == SkillCastVerdict.AlreadyActive, "a second golem was called while the first one lives");
        golem.ApplyDamage(golem.Life);
        scenario.Run(2);
        Assert.True(!scenario.Character.Effects.TryGet(MetalGolem, out _), "the owner kept the status of a dead summon");
        var again = scenario.Cast(MetalGolem);
        Assert.True(again.Accepted, $"a new call after the summon died was refused: {again.Verdict}");
        scenario.Run(80, () => scenario.Summons.Any());
        var second = scenario.Summons.Single();
        Assert.True(second.EntityId != golem.EntityId && second.Aggressive, "the new call kept the old summon or its order");

        second.ExpiresAtMs = scenario.Field.NowMs;
        scenario.Run(2);
        Assert.True(!second.IsAlive && !scenario.Character.Effects.TryGet(MetalGolem, out _), "an expired summon or its status survived");
        Assert.True(scenario.Statuses.Any(value => value.StatusCode == MetalGolem && value.Op == StatusEffectOp.Remove), "the keep icon was not removed");

        scenario.Refill();
        scenario.ClearCooldowns();
        scenario.Cast(MetalGolem);
        scenario.Run(80, () => scenario.Summons.Any());
        var third = scenario.Summons.Single();
        scenario.Field.Remove(scenario.Player.EntityId);
        scenario.Run(2);
        Assert.True(!third.IsAlive && !scenario.Character.Effects.TryGet(MetalGolem, out _),
            "the summon outlived its owner leaving the field, or its keep icon stayed on the owner");

        var archer = SkillScenario.Create(PlayerClass.Archer, 100, catalog: [SkillScenario.SummonModel("Wolverin", 41)]);
        archer.Learn(RecallWolverine, 3);
        var call = archer.Cast(RecallWolverine);
        Assert.True(call.Accepted, $"Recall Wolverine refused: {call.Verdict}");
        archer.Run(80, () => archer.Summons.Any());
        var wolverine = archer.Summons.Single();
        Assert.True(!wolverine.Aggressive && wolverine.Def.Combat.MaxLife == 2400 && wolverine.Def.Combat.Defence == 1100 + archer.Character.Attributes.Agility * 2,
            "the wolverine's rank 3 values or passive start differ");
        return Task.CompletedTask;
    }
}
