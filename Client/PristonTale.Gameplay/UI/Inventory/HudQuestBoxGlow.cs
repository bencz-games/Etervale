using Microsoft.Xna.Framework;
using PristonTale.Contracts.Units;
using PristonTale.Gameplay.UI.Rendering;

namespace PristonTale.Gameplay.UI.Inventory;

// The original's glow over a carried item set into the closed special item box: two copies of UC-hall.tga, 20 px square
// at 12 px right of the item's top left, each tick 2 px larger about the same centre and 6 of 255 fainter, gone after 40
// ticks.
public sealed class HudQuestBoxGlow
{
    private const int Copies = 2;
    private const int Ticks = 40;
    private const int Size = 20;
    private const int OffsetX = 12;
    private const int Growth = 2;
    private const int Fade = 6;

    private Point _at;
    private double _startMs = double.NaN;

    public void Start(Rectangle item, double nowMs)
    {
        _at = new Point(item.X + OffsetX, item.Y);
        _startMs = nowMs;
    }

    // The tick of the glow at a time, or -1 when it shows nothing (not started, not yet past its first tick, or over).
    public int Tick(double nowMs)
    {
        var tick = double.IsNaN(_startMs) ? -1 : (int)((nowMs - _startMs) * GameUnits.TicksPerSecond / 1000d);
        return tick is > 0 and <= Ticks ? tick : -1;
    }

    public Rectangle Bounds(int tick) =>
        new(_at.X - tick * Growth / 2, _at.Y - tick * Growth / 2, Size + tick * Growth, Size + tick * Growth);

    public void Draw(HudPainter painter, double nowMs)
    {
        var tick = Tick(nowMs);
        if (tick < 0)
        {
            return;
        }

        var tint = Color.White * ((255 - tick * Fade) / 255f);
        var bounds = Bounds(tick);
        for (var copy = 0; copy < Copies; copy++)
        {
            painter.Stretch(HudArt.QuestBoxGlow, bounds, null, tint);
        }
    }
}
