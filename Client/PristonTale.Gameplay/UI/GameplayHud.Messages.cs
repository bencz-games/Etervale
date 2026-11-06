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
using PristonTale.Gameplay.Localization;

namespace PristonTale.Gameplay.UI;

// What the HUD tells the player: completed requests to the chat or the notice strip.
public sealed partial class GameplayHud
{
    private void ForwardMessages()
    {
        if (_inventory.Completions != _inventoryCompletions)
        {
            _inventoryCompletions = _inventory.Completions;
            Report(_inventory.LastOk, _inventory.Message);
        }

        if (_progression.Completions != _progressionCompletions)
        {
            _progressionCompletions = _progression.Completions;
            Report(_progression.LastOk, _progression.Message);
        }

        if (_services.Completions != _serviceCompletions)
        {
            _serviceCompletions = _services.Completions;
            Report(_services.LastOk, _services.Message);
            ShowWorkResult(_aging);
            ShowWorkResult(_mix);
        }

        if (_itemUse.Completions != _itemUseCompletions)
        {
            _itemUseCompletions = _itemUse.Completions;
            Report(_itemUse.LastOutcome?.Ok == true, _itemUse.Message);
        }

        if (_wingGate.Completions != _wingGateCompletions)
        {
            _wingGateCompletions = _wingGate.Completions;
            Report(_wingGate.LastOutcome?.Ok == true, _wingGate.Message);
        }

        if (_itemUse.ShopsOpened != _shopsOpened)
        {
            _shopsOpened = _itemUse.ShopsOpened;
            CloseServices();
            _services.Request(0, ClientText.Get("Shop.Mobile"), NpcServiceKind.Shop, NpcServiceAction.Open);
        }

        if (_quests.Completions != _questCompletions)
        {
            _questCompletions = _quests.Completions;
            _notice.Show(_quests.Message);
        }

        if (_bindings.Refusals != _bindingRefusals)
        {
            _bindingRefusals = _bindings.Refusals;
            _chat.System(_bindings.Refusal);
        }
    }

    // A refusal shows in the notice strip as the classic message box; a confirmation (which the classic client did not show)
    // goes to the chat as a server line. A plain success has no message.
    private void Report(bool ok, string message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return;
        }

        if (!ok)
        {
            _notice.Show(message);
        }
        else
        {
            _chat.System(message);
        }
    }
}
