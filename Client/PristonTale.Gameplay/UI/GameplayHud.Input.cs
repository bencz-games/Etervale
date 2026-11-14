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

// Mouse and keyboard: keys, clicks routed to the windows in priority order, menu buttons and toggles, and what the HUD captures.
public sealed partial class GameplayHud
{
    // The bar's buttons and toggles, read every frame for the pointer.
    private static readonly HudMenuButton[] MenuButtons = Enum.GetValues<HudMenuButton>();
    private static readonly HudToggle[] Toggles = Enum.GetValues<HudToggle>();

    private bool ClickBuff(Point pointer)
    {
        if (Statuses is null || SelfId == 0)
        {
            return false;
        }

        var code = _skillHud.BuffAt(pointer);
        if (code == 0)
        {
            return false;
        }

        var now = _clock.Elapsed.TotalSeconds;
        if (code == _lastBuffClicked && now - _lastBuffClickSeconds < PristonTale.Gameplay.UI.Parties.HudPartyWindow.DoubleClickSeconds && PristonTale.Simulation.Skills.SkillRuleCatalog.TryGet(code, out var rule) && rule.Summon is not null)
        {
            _lastBuffClicked = 0;
            SummonModeRequested?.Invoke(code);
            return true;
        }

        _lastBuffClicked = code;
        _lastBuffClickSeconds = now;
        return true;
    }

    private void UpdateKeys(KeyboardState keyboard)
    {
        if (keyboard.IsKeyDown(Keys.LeftControl) || keyboard.IsKeyDown(Keys.RightControl))
        {
            return;
        }

        if (Pressed(keyboard, Keys.I) || Pressed(keyboard, Keys.V))
        {
            TogglePanel(_inventorySlide);
            HelpPetKey("InvenTory");
        }

        if (Pressed(keyboard, Keys.C))
        {
            TogglePanel(_statusSlide);
            HelpPetKey("CarStatus");
        }

        if (Pressed(keyboard, Keys.S))
        {
            TogglePanel(_skillSlide);
            HelpPetKey("Skill");
        }

        if (Pressed(keyboard, Keys.X))
        {
            _systemMenu = !_systemMenu;
            HelpPetKey("System");
        }

        if (Pressed(keyboard, Keys.Q))
        {
            ToggleQuestWindow();
            HelpPetKey("Quest");
        }

        if (Pressed(keyboard, Keys.D))
        {
            TogglePartyWindow();
            HelpPetKey("Party");
        }

        if (Pressed(keyboard, Keys.R))
        {
            RunToggleRequested?.Invoke();
            HelpPetKey("RunAndWalk");
        }

        if (Pressed(keyboard, Keys.Space))
        {
            YahooKeyPressed?.Invoke();
        }

        if (Pressed(keyboard, Keys.E))
        {
            _inventory.ToggleBagPage();
        }

        if (Pressed(keyboard, Keys.P) && _skillSlide.Open)
        {
            _skills.TurnPageByKey();
        }

        if (Pressed(keyboard, Keys.W))
        {
            _inventory.ToggleWeaponPair();
        }

        var shift = keyboard.IsKeyDown(Keys.LeftShift) || keyboard.IsKeyDown(Keys.RightShift);
        for (var box = 0; box < BeltKeys.Length; box++)
        {
            if (Pressed(keyboard, BeltKeys[box]))
            {
                _inventory.BeltKey(box, shift);
                HelpPetKey("Potion_ShortKey", KeyIndex + box);
            }
        }
    }

    // The dismiss question's Yes (a click or Enter); 0 is any other answer.
    private void RequestCrystalPetDismiss(uint statusCode)
    {
        if (statusCode != 0)
        {
            CrystalPetDismissRequested?.Invoke(statusCode);
        }
    }

    private void Click(OwnerState state, HudLayout layout, Point pointer, Point design, KeyboardState keyboard)
    {
        // The count box holds every click until it closes.
        if (_countBox.IsOpen)
        {
            _countBox.Click(pointer);
            return;
        }

        if (_personalShop.ModalOpen)
        {
            _personalShop.Click(pointer);
            return;
        }

        // The help window lies over the helper pet's box.
        if (ClickHelp(pointer) || _helpPet.Click(pointer, _clock.Elapsed.TotalMilliseconds))
        {
            return;
        }

        if (_restart.Click(layout, pointer, RestartOffer?.FieldOpen == true))
        {
            return;
        }

        if (_settingsOpen && HudSettingsPanel.Box.Contains(pointer))
        {
            if (HudSettingsPanel.OkButton.Contains(pointer))
            {
                _settingsOpen = false;
            }
            else
            {
                _settings.Click(pointer, keyboard.IsKeyDown(Keys.LeftShift) || keyboard.IsKeyDown(Keys.RightShift));
            }

            return;
        }

        if (_chat.Window.Click(layout, pointer, SideWindowOpen))
        {
            return;
        }

        if (_itemUse.Click(pointer, state) || _wingGate.Click(pointer, state) || _puzzle.Click(pointer, state, _services))
        {
            return;
        }

        if (_skills.TrainingPending)
        {
            _skills.HandleTrainingClick(PanelOrigin(layout, _skillSlide), pointer, state, _skillMaster, _progression, _notice.Show);
            return;
        }

        if (_crystalDismiss.Click(pointer, out var dismissed))
        {
            RequestCrystalPetDismiss(dismissed);
            return;
        }

        if (ClickClan(pointer) || ClickCastle(pointer) || ClickBellatra(pointer) || ClickQuests(layout, pointer) || ClickTrade(pointer) || ClickParty(layout, pointer))
        {
            return;
        }

        if (_npcOpen && ClickNpc(state, layout, pointer))
        {
            return;
        }

        if (_personalShop.Click(pointer))
        {
            return;
        }

        if (_shop.Contains(pointer))
        {
            ClickShop(state, pointer);
            return;
        }

        if (_systemMenu)
        {
            var entry = HudSystemMenu.At(design);
            if (entry == HudSystemEntry.Settings)
            {
                _settingsOpen = true;
                _systemMenu = false;
                return;
            }

            if (entry == HudSystemEntry.Help)
            {
                _systemMenu = false;
                _helpWindow.OpenHelp();
                return;
            }

            if (entry == HudSystemEntry.Exit)
            {
                _systemMenu = false;
                ExitRequested?.Invoke();
                return;
            }

            if (entry is not null)
            {
                return;
            }

            if (_hoveredButton != HudMenuButton.System)
            {
                _systemMenu = false;
            }
        }

        if (_hoveredButton is { } button)
        {
            PressButton(button);
            return;
        }

        if (ClickBuff(pointer))
        {
            return;
        }

        if (_hoveredToggle == HudToggle.Map)
        {
            _mapVisible = !_mapVisible;
            return;
        }

        if (_hoveredToggle == HudToggle.Walk)
        {
            RunToggleRequested?.Invoke();
            return;
        }

        ClickPanels(state, layout, pointer);
    }

    private bool ClickNpc(OwnerState state, HudLayout layout, Point pointer)
    {
        var current = _npc.Current;
        if (_npc.DestinationAt(pointer) is { } destination && state is not null)
        {
            // A Bellatra team booth offers its ticket as its one destination.
            var (kind, action) = _services.Open == NpcServiceKind.EventBooth
                ? (NpcServiceKind.EventBooth, NpcServiceAction.EnterBellatra)
                : (NpcServiceKind.Teleporter, NpcServiceAction.Teleport);
            if (_services.Request(current.EntityId, current.Name, kind, action, state.InventoryRevision, anchor: destination.Index))
            {
                _prepareField?.Invoke(destination.FieldCode);
            }

            return true;
        }

        if (_npc.ServiceAt(pointer) is { } service)
        {
            _services.Request(current.EntityId, current.Name, service.Kind, NpcServiceAction.Open);
            return true;
        }

        if (HudNpcPanel.OkButton.Contains(pointer))
        {
            _npcOpen = false;
            _npc.Dismiss();
            _quests.LeaveNpc();
            return true;
        }

        return HudNpcPanel.Box.Contains(pointer);
    }

    private void PressButton(HudMenuButton button)
    {
        switch (button)
        {
            case HudMenuButton.Status:
                TogglePanel(_statusSlide);
                break;
            case HudMenuButton.Inventory:
                TogglePanel(_inventorySlide);
                break;
            case HudMenuButton.Skills:
                TogglePanel(_skillSlide);
                break;
            case HudMenuButton.System:
                _systemMenu = !_systemMenu;
                break;
            case HudMenuButton.Quest:
                ToggleQuestWindow();
                break;
            case HudMenuButton.Party:
                TogglePartyWindow();
                break;
        }
    }

    private void ClickPanels(OwnerState state, HudLayout layout, Point pointer)
    {
        if (_inventorySlide.FullyOpen && HudInventoryLayout.Close(PanelOrigin(layout, _inventorySlide)).Contains(pointer))
        {
            _inventorySlide.Open = false;
            return;
        }

        if (_statusSlide.FullyOpen)
        {
            var origin = PanelOrigin(layout, _statusSlide);
            if (HudStatusPanel.Close(origin).Contains(pointer))
            {
                _statusSlide.Open = false;
                return;
            }

            if (state is not null && state.StatPoints > 0 && !_progression.Pending)
            {
                for (var index = 0; index < 5; index++)
                {
                    if (HudStatusPanel.AllocateButton(origin, index).Contains(pointer))
                    {
                        _progression.Submit(new ProgressionCommand(Guid.NewGuid(), state.ProgressionRevision,
                            ProgressionAction.AllocateAttribute, (CharacterAttribute)index, 1));
                    }
                }
            }
        }

        if (_skillSlide.FullyOpen)
        {
            var origin = PanelOrigin(layout, _skillSlide);
            if (HudSkillTreeLayout.Close(origin).Contains(pointer))
            {
                _skillSlide.Open = false;
                _skillMaster = 0;
                return;
            }

            if (_skills.ClickPage(origin, pointer))
            {
                return;
            }

            // The class change button: the master's next rank-up quest, which ends the training as the original does.
            if (_skillMaster != 0 && _quests.ClassChangeNpc == _skillMaster && HudSkillTreeLayout.ClassChange.Contains(pointer))
            {
                _quests.ChangeClass(_skillMaster);
                _skillSlide.Open = false;
                _skillMaster = 0;
                return;
            }

            _skills.HandleTrainingClick(origin, pointer, state, _skillMaster, _progression, _notice.Show);
        }
    }

    private bool ServiceWindowOpen(bool blockingOnly)
    {
        foreach (var window in _serviceWindows)
        {
            if (window.IsOpen && (!blockingOnly || window.BlocksWorld))
            {
                return true;
            }
        }

        return false;
    }

    private bool ServiceWindowContains(Point pointer)
    {
        foreach (var window in _serviceWindows)
        {
            if (window.Contains(pointer))
            {
                return true;
            }
        }

        return false;
    }

    private bool Captures(HudLayout layout, Point pointer, Point design)
    {
        if (BlocksWorld)
        {
            return true;
        }

        if (_helpPet.Captures(pointer) || CapturesHelp(pointer) || CapturesParty(layout, pointer) || CapturesClan(pointer) || CapturesCastle(pointer) || CapturesBellatra(pointer) || _tradeRequest.Captures(pointer) || _personalShop.Captures(pointer)
            || _itemUse.Captures(pointer) || _wingGate.Captures(pointer) || _puzzle.Captures(pointer) || _crystalDismiss.Captures(pointer) || _premium.Contains(pointer))
        {
            return true;
        }

        if (HudBar.Captures(design) || MinimapShown && HudMinimap.Box(layout).Contains(pointer) || _skillHud.BuffAt(pointer) != 0
            || WindowContains(layout, _inventorySlide, pointer) || WindowContains(layout, _statusSlide, pointer) || WindowContains(layout, _skillSlide, pointer)
            || _skills.MasterBoxShown && HudSkillTreeLayout.MasterBox.Contains(pointer))
        {
            return true;
        }

        if (ServiceWindowContains(pointer))
        {
            return true;
        }

        if (_quests.SkillResetBox || _quests.Confirming || _quests.Dialog is not null && HudQuestDialog.Box.Contains(pointer)
            || _questWindow.Contains(layout, pointer) || _skillHud.QuestTimerAt(pointer))
        {
            return true;
        }

        return _restart.Open && HudRestartPanel.Box(layout).Contains(pointer)
            || _systemMenu && HudSystemMenu.Bounds.Contains(design)
            || _settingsOpen && HudSettingsPanel.Box.Contains(pointer)
            || _npcOpen && HudNpcPanel.Box.Contains(pointer)
            || _skills.TrainingPending && HudMessageBox.Bounds.Contains(pointer);
    }

    private static HudMenuButton? ButtonAt(Point design)
    {
        foreach (var button in MenuButtons)
        {
            if (HudBar.MenuButton(button).Contains(design))
            {
                return button;
            }
        }

        return null;
    }

    private static HudToggle? ToggleAt(Point design)
    {
        foreach (var toggle in Toggles)
        {
            if (HudBar.Toggle(toggle).Contains(design))
            {
                return toggle;
            }
        }

        return null;
    }

    private void TogglePanel(HudSlide slide)
    {
        if (slide.Open)
        {
            slide.Open = false;
            if (slide == _skillSlide)
            {
                _skillMaster = 0;
                _skills.CancelTraining();
            }

            return;
        }

        _skillMaster = 0;
        OpenOnly(slide);
    }

    // The status, skill and inventory windows share the same screen area; opening one closes the others (an item on the
    // inventory's cursor stays there, as the original keeps the mouse item).
    private void OpenOnly(HudSlide slide)
    {
        _inventorySlide.Open = slide == _inventorySlide;
        _statusSlide.Open = slide == _statusSlide;
        _skillSlide.Open = slide == _skillSlide;
        _skills.CancelTraining();
    }

    // The potion belt's boxes, left to right.
    private static readonly Keys[] BeltKeys = [Keys.D1, Keys.D2, Keys.D3];

    private bool Pressed(KeyboardState state, Keys key) => state.IsKeyDown(key) && !_previousKeyboard.IsKeyDown(key);
}
