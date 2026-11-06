using Microsoft.Xna.Framework;
using PristonTale.Gameplay.UI.Rendering;
using PristonTale.Simulation.Items.Usable;
using PristonTale.Gameplay.Localization;

namespace PristonTale.Gameplay.UI.Widgets;

// A right click on a crystal pet's icon in the running-effects bar asks whether to dismiss the pet; Yes asks the server
// to dismiss the pet of that status (each pet has its own), and the icon goes with it.
public sealed class HudCrystalDismiss
{
    public static string Title => ClientText.Get("Crystal.Title");

    private uint _statusCode;

    public bool Confirming { get; private set; }

    // The status the open question is about.
    public uint StatusCode => _statusCode;

    public bool Captures(Point pointer) => Confirming && HudMessageBox.Bounds.Contains(pointer);

    // Opens the question for a crystal pet's status; false for any other status.
    public bool Offer(uint statusCode)
    {
        if (!MonsterCrystals.TryGetByPetStatus(statusCode, out _))
        {
            return false;
        }

        _statusCode = statusCode;
        Confirming = true;
        return true;
    }

    // True when the box took the click; dismissed is the status code to dismiss when Yes was clicked, else 0.
    public bool Click(Point pointer, out uint dismissed)
    {
        dismissed = 0;
        if (!Confirming)
        {
            return false;
        }

        if (HudMessageBox.Accept.Contains(pointer))
        {
            dismissed = Accept();
        }
        else if (HudMessageBox.Reject.Contains(pointer))
        {
            Confirming = false;
        }

        return true;
    }

    // Yes from the keyboard (Enter): the status code to dismiss, or 0 when no question is open.
    public uint Accept()
    {
        if (!Confirming)
        {
            return 0;
        }

        Confirming = false;
        return _statusCode;
    }

    public void Close() => Confirming = false;

    public void Draw(HudPainter painter, Point pointer)
    {
        if (!Confirming)
        {
            return;
        }

        painter.Flush();
        HudMessageBox.Draw(painter, Title, ClientText.Get("ItemUse.Confirm"), pointer, ClientText.Get("Crystal.Dismiss"));
    }
}
