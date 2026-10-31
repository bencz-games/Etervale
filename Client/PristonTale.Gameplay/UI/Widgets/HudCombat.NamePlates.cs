using Microsoft.Xna.Framework;
using PristonTale.Client.Net.Presence;
using PristonTale.Gameplay.UI.Chat;
using PristonTale.Gameplay.UI.Rendering;
using PristonTale.Contracts.World;

namespace PristonTale.Gameplay.UI.Widgets;

// Name boxes over the scene: an NPC's name always within 768 units of the camera, a party member's always with its life
// bar, a monster's only while it is under the cursor or is the clicked target. A summon or crystal pet's box shows within
// the same range, with its owner's name in parentheses on a second line and, on the viewer's own, its life bar. Farther
// boxes draw first; the hovered and the clicked one draw last. Every box is HudPainter.NameBox,
// the speech bubble's fill and gold rim with rounded corners, highlighted under the cursor. Classic (classicNamePlates)
// is the original's layout: the HUD text size and the life bar while the cursor is on the monster. Otherwise the same box
// holds smaller text, and the bar shows once the monster was clicked or the local player swung at it, not for the
// cursor alone.
public sealed partial class HudCombat
{
    private const float NpcPlateDepth = 12 * 64;
    // Classic: the frame's top this far above the head point. Compact: the fill's bottom this far above it.
    private const int ClassicPlateLift = 36;
    private const int PlateLift = 12;
    private const int PlatePadY = 1;
    private const float PlateTextScale = 0.6f;
    // The original's 100x16 life bar drawn at 0.6 x 0.7.
    private const int PlateBarWidth = 60;
    private const int PlateBarHeight = 11;
    private static readonly Color PlateMonster = new(255, 210, 210);
    private static readonly Color PlateTracedMonster = new(255, 230, 200);
    private static readonly Color PlateNpc = new(180, 180, 255);
    private static readonly Color PlateParty = new(220, 255, 160);
    private static readonly Color PlateOwner = new(255, 255, 200);
    private readonly List<(ulong Id, HudOverheadPoint Point, bool Focus)> _plates = new();

    // Whether a player is in the local player's party.
    public Func<ulong, bool> InParty { get; set; } = static _ => false;

    // Whether a monster's name box carries its life bar.
    public static bool ShowsPlateBar(bool classic, bool hovered, bool traced, bool attacked) => classic ? hovered : traced || attacked;

    public void DrawNamePlates(HudPainter painter, HudLayout layout, Func<ulong, HudOverheadPoint?> project, ulong hovered, ulong traced, bool classic)
    {
        _plates.Clear();
        foreach (var (id, entity) in _entities)
        {
            var focus = id == hovered || id == traced;
            var kind = entity.Spawn.Kind;
            var member = kind == EntityKind.Player && InParty(id);
            var summon = kind == EntityKind.Monster && entity.Spawn.OwnerId != 0;
            var shown = kind == EntityKind.Npc || member || (kind == EntityKind.Monster && entity.Life > 0 && (summon || focus));
            if (shown && project(id) is { } point && (focus || member || point.Depth < NpcPlateDepth))
            {
                _plates.Add((id, point, focus));
            }
        }

        _plates.Sort(static (left, right) => left.Focus != right.Focus ? (left.Focus ? 1 : -1) : right.Point.Depth.CompareTo(left.Point.Depth));
        var scale = Math.Max(1, layout.Scale);
        foreach (var (id, point, _) in _plates)
        {
            var entity = _entities[id];
            var monster = entity.Spawn.Kind == EntityKind.Monster;
            var member = entity.Spawn.Kind == EntityKind.Player;
            var summon = monster && entity.Spawn.OwnerId != 0;
            var color = member || summon ? PlateParty : !monster ? PlateNpc : id == traced ? PlateTracedMonster : PlateMonster;
            var owner = summon ? OwnerLabel(entity) : null;
            var x = (int)(point.Screen.X / scale);
            var y = (int)(point.Screen.Y / scale);
            var ownSummon = summon && entity.Spawn.OwnerId == _self;
            if (classic)
            {
                DrawClassicPlate(painter, layout, Name(id), owner, x, y, color, id == hovered,
                    member || ownSummon || monster && !summon && ShowsPlateBar(true, id == hovered, id == traced, entity.Attacked) ? entity : null);
            }
            else
            {
                DrawPlate(painter, layout, Name(id), owner, x, y, color, id == hovered,
                    member || ownSummon || monster && !summon && ShowsPlateBar(false, id == hovered, id == traced, entity.Attacked) ? entity : null);
            }
        }
    }

    // "(owner)" under a summon's name, formatted once per summon; null while its owner is not in view.
    private string OwnerLabel(Entity summon)
    {
        if (summon.OwnerLabel is null && PlayerName(summon.Spawn.OwnerId) is { } owner)
        {
            summon.OwnerLabel = $"({owner})";
        }

        return summon.OwnerLabel;
    }

    // A box that would leave the screen is not drawn, as in the original. A second line (owner) grows the box upwards.
    private static void DrawClassicPlate(HudPainter painter, HudLayout layout, string name, string owner, int anchorX, int anchorY, Color color, bool hovered,
        Entity bar)
    {
        var nameWidth = painter.Measure(name);
        var ownerWidth = owner is null ? 0 : painter.Measure(owner);
        var width = Math.Max(nameWidth, ownerWidth);
        var lineHeight = Math.Max(16, painter.TextHeight);
        var extraLine = owner is null ? 0 : lineHeight;
        var frameHeight = 4 + lineHeight + extraLine + 6 + 2;
        var x = anchorX - width / 2 - 9;
        var y = anchorY - ClassicPlateLift - extraLine;
        if (x < 0 || y < 0 || x + width + 18 > layout.Width || y + frameHeight + PlateBarHeight > layout.Height)
        {
            return;
        }

        painter.Flush();
        painter.NameBox(new Rectangle(x, y, width + 18, frameHeight), hovered);
        painter.Flush();
        painter.Text(name, anchorX - nameWidth / 2, y + 5 + 2, color, false);
        if (owner is not null)
        {
            painter.Text(owner, anchorX - ownerWidth / 2, y + 5 + 2 + lineHeight, PlateOwner, false);
        }

        if (bar is not null)
        {
            DrawHealthBar(painter, new Rectangle(anchorX - PlateBarWidth / 2, y + frameHeight - 2, PlateBarWidth, PlateBarHeight), bar.Life, bar.Spawn.MaxLife);
        }
    }

    // The name box around the smaller text, the capitals centred inside with room for descenders.
    private static void DrawPlate(HudPainter painter, HudLayout layout, string name, string owner, int anchorX, int anchorY, Color color, bool hovered, Entity bar)
    {
        var nameWidth = painter.MeasureAt(name, PlateTextScale);
        var ownerWidth = owner is null ? 0 : painter.MeasureAt(owner, PlateTextScale);
        var (line, pad) = painter.NameBoxLineAt(PlateTextScale, PlatePadY);
        var content = owner is null ? line : line * 2;
        // The frame: 9 pixels each side of the text.
        var width = Math.Max(nameWidth, ownerWidth) + 18;
        var height = content + HudPainter.NameBoxBorders;
        var x = anchorX - width / 2;
        var y = anchorY - PlateLift - height;
        if (x < 0 || y < 0 || x + width > layout.Width || y + height + 1 + PlateBarHeight > layout.Height)
        {
            return;
        }

        painter.Flush();
        painter.NameBox(new Rectangle(x, y, width, height), hovered);
        painter.Flush();
        var top = (int)(y + HudPainter.NameBoxTop + pad - HudPainter.CapTop);
        painter.Text(name, anchorX - nameWidth / 2, top, color, PlateTextScale);
        if (owner is not null)
        {
            painter.Text(owner, anchorX - ownerWidth / 2, top + line, PlateOwner, PlateTextScale);
        }

        if (bar is not null)
        {
            DrawHealthBar(painter, new Rectangle(anchorX - PlateBarWidth / 2, y + height + 1, PlateBarWidth, PlateBarHeight), bar.Life, bar.Spawn.MaxLife);
        }
    }
}
