using System.Numerics;
using PristonTale.Application.Services;
using PristonTale.Application.World;
using PristonTale.Simulation.Characters;
using PristonTale.Simulation.Progression;
using PristonTale.Simulation.Quests;
using PristonTale.Simulation.World;
using PristonTale.Contracts.World;
using PristonTale.Contracts.Characters;

namespace PristonTale.Tests;

internal static class DeathPenaltyTests
{
    private const int Forest1 = 2;

    public static IEnumerable<(string, Func<Task>)> Cases()
    {
        yield return ("death penalty shares, the level 85 reduction, the castle exemption, the field rule and the restart towns", RulesAsync);
        yield return ("a dead player stays down until it restarts; town and field restarts take their shares and store the place", RestartsAsync);
        yield return ("a carried Resurrection Scroll is used up and waives both shares; a refused field restart changes nothing", ScrollAndRefusalAsync);
        yield return ("leaving while dead stores the town restart with the death's share", LeaveDeadAsync);
        yield return ("the restart window opens after the death motion, keeps a closed field shut, chooses the town after twenty seconds", WindowAsync);
    }

    private static PlayerCharacter At(int level, ulong above = 0, PlayerClass playerClass = PlayerClass.Fighter)
    {
        var character = new CharacterClassRegistry().CreatePlayer(playerClass, "Fallen");
        character.RestoreProgress(0, 0, tier: 3);
        character.AwardExperience(ExperienceTable.ForLevel(level) + above);
        return character;
    }

    private static ulong Span(int level) => ExperienceTable.ForLevel(level + 1) - ExperienceTable.ForLevel(level);

    private static Task RulesAsync()
    {
        Assert.True(DeathPenalty.ExperienceShare(10, DeathPenalty.StandardDivisor) == 0 && DeathPenalty.GoldShare(10, 3300, 33) == 0,
            "a player below level 11 paid for dying");
        Assert.True(DeathPenalty.ExperienceShare(20, 33) == Span(20) / 33 && DeathPenalty.GoldShare(20, 3300, 33) == 100, "the death's share differs");
        Assert.True(DeathPenalty.ExperienceShare(ExperienceTable.MaximumLevel, 100) == (ExperienceTable.Ceiling - ExperienceTable.ForLevel(ExperienceTable.MaximumLevel)) / 100,
            "the last level's span does not reach the ceiling");
        var fighter = At(85);
        Assert.True(DeathPenalty.Divisor(fighter, FieldState.Forest) == 33 && DeathPenalty.Divisor(fighter, FieldState.Castle) == 0, "divisors differ");
        fighter.Quests.LevelLog |= LevelQuestLog.Level85;
        Assert.True(DeathPenalty.Divisor(fighter, FieldState.Forest) == 50, "the level 85 quest did not reduce the penalty");

        Assert.True(DeathPenalty.CanRestartInField(At(5), FieldState.Forest) && !DeathPenalty.CanRestartInField(At(5), FieldState.Dungeon),
            "below level 11 the field restart is open only in a forest");
        Assert.True(!DeathPenalty.CanRestartInField(At(20), FieldState.Forest), "a level start covers the field restart");
        Assert.True(DeathPenalty.CanRestartInField(At(20, Span(20) / 100), FieldState.Dungeon), "enough experience was refused the field restart");
        Assert.True(!DeathPenalty.CanRestartInField(At(20, Span(20) / 2), FieldState.Castle), "the castle field allowed a field restart");

        Assert.True(DeathPenalty.TownField(PlayerClass.Pikeman) == FieldCodes.Ricarten && DeathPenalty.TownField(PlayerClass.Knight) == FieldCodes.Pillai
            && DeathPenalty.TownField(PlayerClass.Assassin) == FieldCodes.Ricarten, "restart towns differ");

        var floored = At(30, 5);
        Assert.True(floored.LoseExperience(Span(30)) == 5 && floored.Experience == ExperienceTable.ForLevel(30) && floored.Level == 30,
            "a loss went below the level start");
        return Task.CompletedTask;
    }

    private static async Task<SpecialItemTests.Harness> Fallen(int level, ulong above, long gold, params string[] keys)
    {
        var harness = await SpecialItemTests.Harness.CreateAsync(Forest1, keys);
        var character = harness.Session.Character!;
        character.RestoreProgress(0, 0, tier: 3);
        character.AwardExperience(ExperienceTable.ForLevel(level) + above);
        Assert.True(character.TryChangeMoney(gold), "fixture gold");
        harness.Player.ApplyDamage(character.MaxLife * 100);
        harness.Tick(20 * 30);
        Assert.True(harness.Player.IsDead, "the player stood up by itself");
        return harness;
    }

    private static async Task<RestartReply> Restart(SpecialItemTests.Harness harness, RestartChoice choice)
    {
        var service = new RestartAppService(harness.Sessions, new ImmediateSaves(harness.Repository), harness.World);
        var reply = service.RestartAsync(harness.Session.SessionToken, new RestartCommand(Guid.NewGuid(), choice));
        await harness.AdvanceAsync(reply);
        return await reply;
    }

    private static async Task RestartsAsync()
    {
        var harness = await Fallen(20, Span(20) / 2, 3300);
        var character = harness.Session.Character!;
        var before = character.Experience;
        var town = await Restart(harness, RestartChoice.Town);
        Assert.True(town.Outcome.Ok && town.Outcome.ExperienceLost == Span(20) / 33 && town.Outcome.GoldLost == 100 && !town.Outcome.ScrollUsed,
            $"town restart costs differ: {town.Outcome}");
        Assert.True(character.Experience == before - Span(20) / 33 && character.Money == 3200 && !harness.Player.IsDead
            && character.Life == character.MaxLife / 2 && harness.Player.GuardMsLeft > 9000f, "the town restart was not applied");
        Assert.True(town.FieldId == FieldCodes.Ricarten && harness.Repository.Record.FieldId == FieldCodes.Ricarten
            && harness.Repository.Record.Exp == (long)character.Experience, "the town restart was not stored in Ricarten");
        Assert.True(harness.Session.FieldId == FieldCodes.Ricarten, "the player did not move to Ricarten");

        var field = await Fallen(20, Span(20) / 2, 3300);
        var fallen = field.Player.Position;
        var expected = Span(20) / 33 + Span(20) / 100;
        var restart = await Restart(field, RestartChoice.Field);
        Assert.True(restart.Outcome.Ok && restart.Outcome.ExperienceLost == expected && restart.Outcome.GoldLost == 100 + 32,
            $"field restart costs differ: {restart.Outcome}");
        var nearest = Simulation.World.Fields.Forest1Field.Definition.StartPoints
            .MinBy(point => (point.X - fallen.X) * (point.X - fallen.X) + (point.Z - fallen.Z) * (point.Z - fallen.Z))!;
        Assert.True(restart.FieldId == Forest1 && Vector2.Distance(new Vector2(field.Player.Position.X, field.Player.Position.Z), new Vector2(nearest.X, nearest.Z)) < 1f,
            "the field restart did not stand up at the nearest start point");
    }

    private static async Task ScrollAndRefusalAsync()
    {
        // The field rule looks at the experience whatever the scroll: at the level's start the field stays closed.
        var harness = await Fallen(20, 0, 3300, DeathPenalty.ResurrectionScroll);
        var refused = await Restart(harness, RestartChoice.Field);
        Assert.True(refused.Outcome.Status == RestartStatus.FieldRefused && harness.Item(DeathPenalty.ResurrectionScroll) is not null && harness.Player.IsDead,
            $"a refused field restart used the scroll: {refused.Outcome}");

        var bare = await Fallen(20, 0, 3300);
        var bareCharacter = bare.Session.Character!;
        var refusal = await Restart(bare, RestartChoice.Field);
        Assert.True(refusal.Outcome.Status == RestartStatus.FieldRefused && bare.Player.IsDead && bareCharacter.Experience == ExperienceTable.ForLevel(20)
            && bareCharacter.Money == 3300, "a refused field restart changed the character or stood it up");

        var scrolled = await Fallen(20, Span(20) / 2, 3300, DeathPenalty.ResurrectionScroll);
        var scrolledCharacter = scrolled.Session.Character!;
        var before = scrolledCharacter.Experience;
        var used = await Restart(scrolled, RestartChoice.Field);
        Assert.True(used.Outcome.Ok && used.Outcome.ScrollUsed && used.Outcome.ExperienceLost == 0 && used.Outcome.GoldLost == 0
            && scrolledCharacter.Experience == before && scrolledCharacter.Money == 3300 && scrolled.Item(DeathPenalty.ResurrectionScroll) is null,
            $"the scroll did not waive the shares or was not used up: {used.Outcome}");
    }

    private static async Task WindowAsync()
    {
        var layout = Gameplay.UI.HudLayout.Create(1024, 768);
        var panel = new Gameplay.UI.Widgets.HudRestartPanel(Engine3D.Diagnostics.NullClientLog.Instance);
        var asked = new List<RestartChoice>();
        var answer = new TaskCompletionSource<RestartOutcome>();
        panel.Restart = choice =>
        {
            asked.Add(choice);
            return answer.Task;
        };
        var exited = false;
        panel.ExitRequested += () => exited = true;
        Microsoft.Xna.Framework.Point Center(RestartChoice choice) => Gameplay.UI.Widgets.HudRestartPanel.Button(layout, choice).Center;

        panel.Update(dead: true, 0);
        Assert.True(!panel.Open, "the window opened at the fall");
        panel.Update(dead: true, Gameplay.UI.Widgets.HudRestartPanel.OpenDelayMs + 1);
        Assert.True(panel.Open, "the window did not open after the death motion");
        Assert.True(panel.Click(layout, Center(RestartChoice.Field), fieldOpen: false) && asked.Count == 0, "a closed field restart was sent");
        panel.Update(dead: true, Gameplay.UI.Widgets.HudRestartPanel.OpenDelayMs + Gameplay.UI.Widgets.HudRestartPanel.TimeoutMs + 2);
        Assert.True(asked.SequenceEqual([RestartChoice.Town]) && !panel.Open, "the timeout did not choose the town");
        answer.SetResult(new RestartOutcome(RestartStatus.Busy, RestartChoice.Town));
        panel.Update(dead: true, 30_000);
        Assert.True(panel.Open, "a busy answer did not open the window again");

        answer = new TaskCompletionSource<RestartOutcome>();
        Assert.True(panel.Click(layout, Center(RestartChoice.Exit), fieldOpen: true), "the exit click missed");
        answer.SetResult(new RestartOutcome(RestartStatus.Restarted, RestartChoice.Exit));
        panel.Update(dead: true, 30_100);
        Assert.True(exited && !panel.Open, "a stored exit restart did not quit or the window reopened while the player was still shown down");
        panel.Update(dead: false, 30_200);
        panel.Update(dead: true, 40_000);
        Assert.True(!panel.Open, "a new death reopened the window at once");
        await Task.CompletedTask;
    }

    private static async Task LeaveDeadAsync()
    {
        var harness = await Fallen(20, Span(20) / 2, 3300);
        var character = harness.Session.Character!;
        var leave = harness.World.LeaveAsync(harness.Session.FieldId, harness.Player.EntityId, harness.Outbound.Writer);
        await harness.AdvanceAsync(leave);
        var left = await leave;
        Assert.True(left.Left && left.Save is not null, "the dead player did not leave with a capture");
        var record = left.Save!.Encode();
        Assert.True(record.FieldId == FieldCodes.Ricarten && (ulong)record.Exp == character.Experience - Span(20) / 33 && record.Money == 3200
            && record.Life == character.MaxLife / 2, "leaving while dead skipped the penalty or kept the player down where it fell");
        Assert.True(harness.Player.IsDead && character.Money == 3300, "saving a dead player changed the live character");
    }
}
