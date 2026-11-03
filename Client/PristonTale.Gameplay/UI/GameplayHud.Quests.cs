using System.Diagnostics;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using PristonTale.Gameplay.Screens.Framework;
using PristonTale.Gameplay.UI.Chat;
using PristonTale.Gameplay.UI.Inventory;
using PristonTale.Gameplay.UI.Npc;
using PristonTale.Gameplay.UI.Quests;
using PristonTale.Gameplay.UI.Rendering;
using PristonTale.Gameplay.UI.Travel;
using PristonTale.Gameplay.UI.Widgets;
using PristonTale.Simulation.Characters;
using PristonTale.Simulation.Items;
using PristonTale.Simulation.Progression;
using PristonTale.Simulation.Quests;
using PristonTale.Simulation.World.Npcs;
using PristonTale.Contracts.Characters;

namespace PristonTale.Gameplay.UI;

// Quests in the HUD: the dialog, its boxes and the quest window.
public sealed partial class GameplayHud
{
    // Quest notices go to the combat log (chat lines), the notice strip (events and messages) or the fanfare; a new quest
    // dialog takes the help box from the NPC dialog until it closes.
    private void ApplyQuests()
    {
        while (_quests.TryTakeNotice(out var notice))
        {
            switch (notice.Kind)
            {
                case QuestNoticeKind.Chat:
                    _chat.Assist(notice.Text);
                    break;
                case QuestNoticeKind.Fanfare:
                    QuestFanfare?.Invoke();
                    break;
                default:
                    _notice.Show(notice.Text);
                    break;
            }
        }

        if (_quests.DialogsOpened != _questDialogs)
        {
            _questDialogs = _quests.DialogsOpened;
            _skills.CancelTraining();
            _settingsOpen = false;
            foreach (var document in _quests.Dialog?.ChoiceDocuments ?? [])
            {
                _questDocuments.Lines(HudQuestDocuments.DialogPath(document));
            }
        }

        // Quest texts load off the game thread; asking early has them ready when the dialog or window draws.
        if (_quests.Dialog is not null)
        {
            _questDocuments.Lines(HudQuestDocuments.DialogPath(_quests.DialogDocument));
        }

        if (!ReferenceEquals(_quests.View, _questView))
        {
            _questView = _quests.View;
            _questViewMs = _clock.Elapsed.TotalMilliseconds;
            _weaponHunt = _questView is { Code: RankUpByQuestWeapon.Code }
                ? RankUpByQuestWeapon.HuntLine(QuestClasses.WeaponHunt(_class), _questView.WeaponStage, _questView.WeaponKillsLeft)
                : "";
            _questWindow.Refresh(_questView);
            foreach (var entry in _questView is null ? [] : _questView.Progress.Concat(_questView.Finished))
            {
                _questDocuments.Lines(HudQuestDocuments.BoardPath(entry.Description));
            }
        }
    }

    // The quest dialog, its yes/no box and the skill redistribution offer are modal: they take every click.
    private bool ClickQuests(HudLayout layout, Point pointer)
    {
        if (_quests.SkillResetBox)
        {
            if (HudMessageBox.Accept.Contains(pointer) || HudMessageBox.Reject.Contains(pointer))
            {
                _quests.AnswerSkillReset(HudMessageBox.Accept.Contains(pointer));
            }

            return true;
        }

        if (_quests.Confirming)
        {
            if (HudMessageBox.Accept.Contains(pointer) || HudMessageBox.Reject.Contains(pointer))
            {
                _quests.Answer(HudMessageBox.Accept.Contains(pointer));
            }

            return true;
        }

        if (_quests.Dialog is { } dialog)
        {
            if (HudQuestDialog.OkButton.Contains(pointer))
            {
                _quests.Ok();
            }
            else if (HudQuestDialog.ChoiceAt(dialog, pointer) is var choice and >= 0)
            {
                _quests.Choose(choice);
            }
            else if (dialog.Followup == QuestFollowup.ConfirmChoice && HudQuestDialog.PreviousArrow.Contains(pointer))
            {
                _quests.BrowseBy(-1);
            }
            else if (dialog.Followup == QuestFollowup.ConfirmChoice && HudQuestDialog.NextArrow.Contains(pointer))
            {
                _quests.BrowseBy(1);
            }

            return HudQuestDialog.Box.Contains(pointer);
        }

        return _questWindow.Click(layout, pointer, _questView);
    }
}
