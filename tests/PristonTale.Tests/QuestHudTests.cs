using System.Text;
using Microsoft.Xna.Framework;
using PristonTale.Client.Net.Presence;
using PristonTale.Engine3D.Diagnostics;
using PristonTale.Gameplay.UI;
using PristonTale.Gameplay.UI.Parties;
using PristonTale.Gameplay.UI.Quests;
using PristonTale.Gameplay.UI.Widgets;
using PristonTale.Simulation.Quests;
using PristonTale.Contracts.Texts;
using PristonTale.Gameplay.Localization;

namespace PristonTale.Tests;

// The quest HUD without a device: request flows of the controller, quest text decoding and the quest window layout.
internal static class QuestHudTests
{
    public static IEnumerable<(string, Func<Task>)> Cases()
    {
        yield return ("the quest HUD follows dialogs with proceed, confirmation, reward lists and the redistribution offer", ControllerAsync);
        yield return ("quest texts decode from Windows-1252 and the quest window keeps the original layout", LayoutAsync);
    }

    private static async Task ControllerAsync()
    {
        var sent = new List<QuestCommand>();
        QuestOutcome? next = null;
        var quests = new HudQuestController(command =>
        {
            sent.Add(command);
            return Task.FromResult(next ?? new QuestOutcome(true, QuestStatus.Ok, GameText.None, 1, null, []));
        }, ConsoleClientLog.Instance);

        next = new QuestOutcome(true, QuestStatus.Ok, GameText.None, 1, QuestSteps.Confirm(QuestCode.Level30, 0, "LevelQuest30start", QuestTexts.SearchQuest),
            [new QuestNotice(QuestNoticeKind.Fanfare, GameText.None)]) { ClassChangeOffered = true };
        quests.Talk(9);
        await Settle(quests);
        Assert.True(quests.Dialog is { Code: QuestCode.Level30 } && quests.DialogNpc == 9 && quests.ClassChangeNpc == 9 && quests.DialogsOpened == 1,
            "the talk did not open the dialog");
        Assert.True(quests.TryTakeNotice(out var fanfare) && fanfare.Kind == QuestNoticeKind.Fanfare, "the talk's notice was lost");
        quests.Ok();
        Assert.True(quests.Confirming && sent.Count == 1, "a confirm dialog sent before the answer");
        next = null;
        quests.Answer(true);
        await Settle(quests);
        Assert.True(sent[^1] is { Action: QuestAction.Accept, Code: QuestCode.Level30, State: 0, NpcEntityId: 9 } && quests.Dialog is null,
            "the yes answer did not accept the step");

        next = new QuestOutcome(true, QuestStatus.Ok, GameText.None, 2, QuestSteps.Proceed(QuestCode.Level30, 2, "LevelQuest30end"), []);
        quests.Talk(9);
        await Settle(quests);
        next = null;
        quests.Ok();
        await Settle(quests);
        Assert.True(sent[^1] is { Action: QuestAction.Proceed, State: 2 } && quests.Dialog is null, "OK did not proceed");

        var list = new QuestPrompt(QuestCode.FuryOfPhantom, 12, "100LVQuest_7End", QuestFollowup.ChooseFromList)
        {
            Choices = ["A", "B", "C"],
            ChoiceDocuments = ["d1", "d2", "d3"]
        };
        next = new QuestOutcome(true, QuestStatus.Ok, GameText.None, 3, list, []);
        quests.Talk(4);
        await Settle(quests);
        next = new QuestOutcome(true, QuestStatus.Ok, GameText.None, 4, list with { State = 13, Followup = QuestFollowup.ConfirmChoice, Choice = 3 }, []);
        quests.Choose(2);
        await Settle(quests);
        Assert.True(sent[^1] is { Action: QuestAction.ChooseReward, Choice: 3 } && quests.Browse == 3 && quests.DialogDocument == "d3", "the reward list differs");
        quests.BrowseBy(1);
        Assert.True(quests.Browse == 1 && quests.DialogDocument == "d1" && quests.ConfirmDetail == "A", "browsing did not wrap");
        next = null;
        quests.Ok();
        quests.Answer(true);
        await Settle(quests);
        Assert.True(sent[^1] is { Action: QuestAction.Accept, State: 13, Choice: 1 }, "the browsed reward was not claimed");

        var offer = new QuestView(9, QuestCode.None, 0, "", 0, 0, QuestTimerKind.None, 0, [], []) { SkillResetOffered = true };
        quests.Observe(new PresenceUpdate(1, [], [], [], []) { Quest = offer });
        Assert.True(quests.SkillResetBox, "the redistribution offer did not show");
        quests.AnswerSkillReset(false);
        await Settle(quests);
        Assert.True(sent[^1] is { Action: QuestAction.DeclineSkillReset }, "No did not end the offer on the server");
        quests.Observe(new PresenceUpdate(2, [], [], [], []) { Quest = offer with { Revision = 10 } });
        Assert.True(!quests.SkillResetBox, "a declined offer showed again");
        quests.Observe(new PresenceUpdate(3, [], [], [], []) { Quest = offer with { Revision = 11, SkillResetOffered = false } });
        quests.Observe(new PresenceUpdate(4, [], [], [], []) { Quest = offer with { Revision = 12 } });
        quests.AnswerSkillReset(true);
        await Settle(quests);
        Assert.True(sent[^1] is { Action: QuestAction.ResetSkills, NpcEntityId: 0 }, "a new offer could not be taken");

        var victory = new QuestPrompt(QuestCode.RankUpMasterTrial, 8, "ChangeJob4_4End", QuestFollowup.Confirm);
        quests.Observe(new PresenceUpdate(5, [], [], [], []) { QuestPrompt = victory, QuestNotices = [new QuestNotice(QuestNoticeKind.Message, QuestTexts.FuryWon)] });
        Assert.True(quests.Dialog == victory && quests.DialogNpc == 0 && quests.TryTakeNotice(out var won) && won.Text == QuestTexts.FuryWon,
            "the world's dialog did not open");

        next = QuestOutcome.Failed(QuestStatus.Busy, GameText.Of(GameTextId.CharacterBusy));
        quests.CloseDialog();
        var messages = quests.Completions;
        quests.Talk(3);
        await Settle(quests);
        Assert.True(quests.Completions == messages, "a busy talk showed a message");
        next = QuestOutcome.Failed(QuestStatus.InProgress, QuestTexts.QuestInProgress);
        quests.ChangeClass(3);
        quests.Talk(3);
        await Settle(quests);
        Assert.True(quests.Completions == messages + 1 && quests.Message == ClientText.Format(QuestTexts.QuestInProgress), "a refused talk stayed silent");
    }

    private static async Task Settle(HudQuestController quests)
    {
        for (var step = 0; step < 100 && quests.Pending; step++)
        {
            await Task.Yield();
            quests.Update();
        }

        Assert.True(!quests.Pending, "the quest request never finished");
    }

    private static Task LayoutAsync()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        var bytes = Encoding.GetEncoding(1252).GetBytes("\r\nAlgo aconteceu! Poções\r\n\tem Navisko\r\n\r\n");
        var lines = HudQuestDocuments.Decode(bytes);
        Assert.True(lines.Length == 2 && lines[0] == "Algo aconteceu! Poções" && lines[1] == " em Navisko", "quest text decoding differs");
        Assert.True(HudQuestDocuments.WithSubject("Hunt 50 $$%s quickly.", "Omega") == "Hunt 50 Omega quickly."
            && HudQuestDocuments.WithSubject("No mark here", "Omega") == "No mark here", "a dialog's subject mark was not replaced by the subject");
        var paragraphs = HudQuestDocuments.Paragraphs(["Algo aconteceu! Um", " grupo de Bargon", "", "", "- primeiro", "segue", "2) segundo",
            "Poder de Ataque: 63-80       Add. HP : 50", "Vel. da Arma   : 8", "==========", ""]);
        Assert.True(paragraphs.SequenceEqual(["Algo aconteceu! Um grupo de Bargon", "", "- primeiro segue", "2) segundo",
            "Poder de Ataque: 63-80   Add. HP : 50", "Vel. da Arma   : 8", "=========="]), "quest text paragraphs differ");
        Assert.True(HudQuestDocuments.DialogPath("LevelQuest30start") == "image/Sinimage/Quest/LevelQuest30start.sin"
            && HudQuestDocuments.BoardPath("LevelQuest\\Quest30.Msg") == "image/party/party_DATA/LevelQuest/Quest30.Msg", "quest text paths differ");

        var layout = HudLayout.Create(800, 600);
        var origin = HudSidePanel.Origin(layout);
        Assert.True(origin == new Point(539, 155) && HudSidePanel.Origin(HudLayout.Create(1024, 768)) == new Point(1024 - 261, 155 + 168)
            && HudSidePanel.Origin(HudLayout.Create(1024, 768)) == new Point(1024 - 261, 155 + 168), "the quest and party windows are not right and bottom anchored");
        Assert.True(HudQuestWindow.ListButton(origin, 0) == new Rectangle(639, 375, 20, 20) && HudQuestWindow.ListButton(origin, 4).X == 639 + 4 * 23
            && HudQuestWindow.Row(origin, 1) == new Rectangle(649, 225, 120, 20) && HudQuestWindow.DescriptionBounds(origin).X == 344,
            "the quest window layout differs");

        var window = new HudQuestWindow();
        window.Toggle();
        window.Advance(2);
        Assert.True(window.FullyOpen, "the quest window did not open");
        var view = new QuestView(1, QuestCode.Level30, 1, "Bargon", 3, 20, QuestTimerKind.None, 0,
            [new QuestBoardEntry("For her", "LevelQuest\\Quest30.Msg")], [new QuestBoardEntry("Done", "x.msg")]);
        Assert.True(window.Click(layout, HudQuestWindow.Row(origin, 0).Center, view) && window.DescriptionOpen, "a quest title did not open its description");
        Assert.True(window.Click(layout, HudQuestWindow.ListButton(origin, 3).Center, view) && window.Page == HudQuestPage.Finished && !window.DescriptionOpen,
            "the finished tab did not switch");
        Assert.True(!window.Click(layout, new Point(10, 10), view), "a click outside the window was taken");
        Assert.True(window.Click(layout, HudQuestWindow.ListButton(origin, 4).Center, view) && !window.Open, "close did not close");
        Assert.True(HudQuestDialog.Box == HudNpcPanel.Box && HudQuestDialog.ChoiceButton(0, 8).Width == HudNpcPanel.ButtonWidth,
            "the quest dialog does not share the help box");
        return Task.CompletedTask;
    }
}
