using System.Runtime.InteropServices;
using Microsoft.Xna.Framework;
using PristonTale.Gameplay.UI.Rendering;
using PristonTale.Gameplay.World.Remote;

namespace PristonTale.Gameplay.UI.Widgets;

// Drop names over the scene: the name box 36 pixels above the drop's point, the text 9 pixels in at the HUD text size,
// its capitals centred with room for descenders (HudPainter.NameBoxLineAt). Labels come nearest first; each keeps its
// spot unless it would cover a label already placed, and then rises just above it, so a pile of drops reads as a column
// of names. Labels are moved by whole pixels, not snapped to a grid, and come in an order kept from frame to frame, so
// the column follows the pile without reshuffling.
// A box that would leave the screen is not drawn. Labels draw before the monster and NPC names, which cover them; the
// text is lavender, brighter under the cursor.
public sealed class HudGroundLabels
{
    private const int Lift = 36;
    private const int Gap = 2;
    private const int PadY = 1;
    private static readonly Color Named = new(150, 144, 206);
    private static readonly Color Hovered = new(196, 186, 255);
    private readonly List<Rectangle> _placed = [];

    public void Draw(HudPainter painter, HudLayout layout, IReadOnlyList<GroundLabel> labels, Action<Guid, Rectangle> placed)
    {
        var scale = Math.Max(1, layout.Scale);
        var (line, pad) = painter.NameBoxLineAt(HudPainter.TextScale, PadY);
        var height = line + HudPainter.NameBoxBorders;
        var textTop = (int)(HudPainter.NameBoxTop + pad - HudPainter.CapTop);
        _placed.Clear();
        foreach (var label in labels)
        {
            var width = painter.Measure(label.Text) + 18;
            var natural = new Rectangle((int)(label.Anchor.X / scale) - width / 2, (int)(label.Anchor.Y / scale) - Lift, width, height);
            var box = Stack(natural, CollectionsMarshal.AsSpan(_placed));
            if (box.X < 0 || box.Y < 0 || box.Right > layout.Width || box.Bottom > layout.Height)
            {
                continue;
            }

            _placed.Add(box);
            painter.Flush();
            painter.NameBox(box);
            painter.Flush();
            painter.Text(label.Text, box.X + 9, box.Y + textTop, label.Hovered ? Hovered : Named, HudPainter.TextScale);
            placed?.Invoke(label.Id, new Rectangle(box.X * scale, box.Y * scale, box.Width * scale, box.Height * scale));
        }
    }

    // The box raised just above every placed box it would cover, Gap pixels apart; each rise moves it strictly up, so the
    // search ends.
    public static Rectangle Stack(Rectangle box, ReadOnlySpan<Rectangle> placed)
    {
        for (var index = 0; index < placed.Length; index++)
        {
            var guard = placed[index];
            guard.Inflate(0, Gap);
            if (box.Intersects(guard))
            {
                box.Y = guard.Top - box.Height;
                index = -1;
            }
        }

        return box;
    }
}
