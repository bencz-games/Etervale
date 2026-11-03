using Microsoft.Xna.Framework;
using PristonTale.Gameplay.UI.Npc;
using PristonTale.Gameplay.UI.Rendering;
using PristonTale.Gameplay.UI.Widgets;
using PristonTale.Simulation.Characters;
using PristonTale.Simulation.Items.Usable;
using PristonTale.Simulation.World.Npcs;

namespace PristonTale.Gameplay.UI.Inventory;

// The puzzle collector: with a finished puzzle on the board he asks whether to exchange it for an item; otherwise he
// explains the puzzle. The server checks the board again, takes the pieces and hands out the reward.
public sealed class HudPuzzleExchange
{
    public const string Title = "Completed Puzzle";
    public const string Help = "Lay the eight pieces of one puzzle on the board of your inventory, then bring it to me.";

    private ulong _npcId;
    private string _npcName = "";

    public bool Confirming { get; private set; }

    public bool Captures(Point pointer) => Confirming && HudMessageBox.Bounds.Contains(pointer);

    // Opens the question for a finished puzzle; returns the help line otherwise.
    public string Offer(OwnerState state, ulong npcId, string npcName)
    {
        if (state is null || !HudInventoryPanel.PuzzleFinished(state.Items.Where(static item => item.InBag)))
        {
            return Help;
        }

        _npcId = npcId;
        _npcName = npcName;
        Confirming = true;
        return null;
    }

    public bool Click(Point pointer, OwnerState state, HudNpcServiceController services)
    {
        if (!Confirming)
        {
            return false;
        }

        if (HudMessageBox.Accept.Contains(pointer) && state is not null)
        {
            services.Request(_npcId, _npcName, NpcServiceKind.QuestEvent, NpcServiceAction.Exchange, state.InventoryRevision);
            Confirming = false;
        }
        else if (HudMessageBox.Reject.Contains(pointer))
        {
            Confirming = false;
        }

        return true;
    }

    public void Close() => Confirming = false;

    public void Draw(HudPainter painter, Point pointer)
    {
        if (!Confirming)
        {
            return;
        }

        painter.Flush();
        HudMessageBox.Draw(painter, Title, "Do you wanna", pointer, "exchange to the item?");
    }
}
