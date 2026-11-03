using Microsoft.Xna.Framework;
using PristonTale.Engine3D.Formats;
using PristonTale.Gameplay.UI.Rendering;
using PristonTale.Simulation.World;
using PristonTale.Contracts.Social;
using PristonTale.Contracts.Units;
using PristonTale.Engine3D;

namespace PristonTale.Gameplay.UI.Widgets;

// NeighbourFieldId is another loaded field whose map is drawn under the active one where the two meet.
public readonly record struct HudWorldView(
    string FieldId,
    bool Underground,
    PristonIntRectangle MapRectangle,
    Vector3 PlayerPosition,
    Vector3 PlayerForward,
    float TimeOfDay,
    bool Running,
    bool MapHidden = false,
    string NeighbourFieldId = null,
    PristonIntRectangle NeighbourMapRectangle = default);

// Original field minimap: 128x128 box at (656,426), right and bottom anchored, black 50% backdrop, the
// field\map\<id>.tga region around the player (the neighbouring field's map first), NPC dots, the heading arrow,
// MapBox.tga and the field title. North is up; the map does not rotate.
public sealed class HudMinimap
{
    // The view radius starts at 8 cells and eases toward the field's size by 8 units per 70 Hz frame.
    private const float InitialViewSize = 8 * 64f;
    private const float ZoomSpeed = 8f * GameUnits.TicksPerSecond;
    // The original adds a specular 96 to the frame (a quarter of it to the title) under the cursor; adding the sprite's own
    // colour once (a quarter for the title) gives the same brightening on the grey frame art.
    private const int HoverHighlight = 255;

    private string _field;
    private string _neighbour;
    private string _fieldMap;
    private string _fieldTitle;
    private string _neighbourMap;
    private float _viewSize = InitialViewSize;

    public static Rectangle Box(HudLayout layout) => layout.BottomRight(new Rectangle(656, 426, 128, 128));

    // 16 (dungeon) or 24 (other fields) cells of 64 world units from the centre to the box edge.
    public static float ViewSize(bool underground) => (underground ? 16 : 24) * 64f;

    public float CurrentViewSize => _viewSize;

    // The original shows no minimap in the Bellatra arena, the quest arena, the boss field and the action fields.
    public static bool HiddenOn(int fieldCode, FieldState state) =>
        fieldCode is FieldCodes.Bellatra or FieldCodes.QuestArena or FieldCodes.Boss
        || state is FieldState.QuestArena or FieldState.Room or FieldState.Action;

    // Original stage coordinates are fixed point with Z mirrored relative to the engine (stage vertex Z = -z).
    public static Vector2 MapPoint(PristonIntRectangle rectangle, Vector3 position) =>
        new(position.X - rectangle.Left / GameUnits.FixedScale, rectangle.Bottom / GameUnits.FixedScale + position.Z);

    // Returns the texture source (normalized) and destination (relative to the 126x126 interior) after the original clipping.
    public static bool MapRegion(PristonIntRectangle rectangle, Vector3 position, bool underground, out Vector4 source, out Vector4 destination) =>
        MapRegion(rectangle, position, ViewSize(underground), out source, out destination);

    public static bool MapRegion(PristonIntRectangle rectangle, Vector3 position, float size, out Vector4 source, out Vector4 destination)
    {
        source = default;
        destination = default;
        var width = (rectangle.Right - rectangle.Left) / GameUnits.FixedScale;
        var height = (rectangle.Bottom - rectangle.Top) / GameUnits.FixedScale;
        if (width <= 0 || height <= 0 || size <= 0)
        {
            return false;
        }

        var point = MapPoint(rectangle, position);
        var fpx = size / width;
        var fpy = size / height;
        var fx = point.X / width - fpx;
        var fw = point.X / width + fpx;
        var fy = point.Y / height - fpy;
        var fh = point.Y / height + fpy;
        if (fx < 0 && fw < 0 || fx > 1 && fw > 1 || fy < 0 && fh < 0 || fy > 1 && fh > 1)
        {
            return false;
        }

        float dx = 0;
        float dy = 0;
        float dw = 126;
        float dh = 126;
        if (fx < 0)
        {
            var clip = -fx * (64 / fpx);
            dx += clip;
            dw -= clip;
            fx = 0;
        }

        if (fw > 1)
        {
            dw -= (fw - 1) * (64 / fpx);
            fw = 1;
        }

        if (fy < 0)
        {
            var clip = -fy * (64 / fpy);
            dy += clip;
            dh -= clip;
            fy = 0;
        }

        if (fh > 1)
        {
            dh -= (fh - 1) * (64 / fpy);
            fh = 1;
        }

        source = new Vector4(fx, fy, fw - fx, fh - fy);
        destination = new Vector4(dx, dy, dw, dh);
        return dw > 0 && dh > 0;
    }

    // The minimap arrow bitmap points down and is turned half a circle before applying the heading.
    public static float ArrowRotation(Vector3 forward) =>
        forward.X == 0 && forward.Z == 0 ? MathF.PI : WorldSpace.TransportFacing(forward) + MathF.PI;

    // A world offset from the player to the box centre: x east, y south (engine z), 64 pixels per view radius.
    public static Vector2 MarkerOffset(Vector3 player, Vector3 marker, float size) =>
        new((marker.X - player.X) * 64 / size, (marker.Z - player.Z) * 64 / size);

    public static float EaseViewSize(float current, float target, double elapsedSeconds)
    {
        var step = (float)(ZoomSpeed * Math.Max(0, elapsedSeconds));
        return current < target ? MathF.Min(target, current + step) : MathF.Max(target, current - step);
    }

    public void Update(HudSprites sprites, HudWorldView world, double elapsedSeconds)
    {
        var neighbour = string.Equals(world.NeighbourFieldId, world.FieldId, StringComparison.OrdinalIgnoreCase) ? null : world.NeighbourFieldId;
        if (!string.Equals(world.FieldId, _field, StringComparison.OrdinalIgnoreCase) || !string.Equals(neighbour, _neighbour, StringComparison.OrdinalIgnoreCase))
        {
            // Field art is not reference counted: when the two fields swap, keep what stays in use.
            var fieldMap = world.FieldId is null ? null : HudArt.FieldMap(world.FieldId);
            var fieldTitle = world.FieldId is null ? null : HudArt.FieldTitle(world.FieldId);
            var neighbourMap = neighbour is null ? null : HudArt.FieldMap(neighbour);
            ReleaseUnless(sprites, _fieldMap, fieldMap, neighbourMap);
            ReleaseUnless(sprites, _fieldTitle, fieldTitle, null);
            ReleaseUnless(sprites, _neighbourMap, fieldMap, neighbourMap);
            _field = world.FieldId;
            _neighbour = neighbour;
            _fieldMap = fieldMap;
            _fieldTitle = fieldTitle;
            _neighbourMap = neighbourMap;
            Request(sprites, _fieldMap);
            Request(sprites, _fieldTitle);
            Request(sprites, _neighbourMap);
        }

        _viewSize = EaseViewSize(_viewSize, ViewSize(world.Underground), elapsedSeconds);
    }

    // party: the other members' places, marked red past PartyLimits.NearRange, as the original marks them.
    public void Draw(HudPainter painter, HudLayout layout, HudWorldView world, IReadOnlyList<Vector3> npcs, IReadOnlyList<Vector3> party, bool hovered)
    {
        var box = Box(layout);
        painter.ColorBox(box, 0, 0, 0, 128);
        if (_neighbour is not null)
        {
            DrawMap(painter, box, _neighbourMap, world.NeighbourMapRectangle, world.PlayerPosition);
        }

        if (_field is not null)
        {
            DrawMap(painter, box, _fieldMap, world.MapRectangle, world.PlayerPosition);
        }

        var center = new Vector2(box.X + 64, box.Y + 64);
        for (var index = 0; index < npcs.Count; index++)
        {
            var npc = npcs[index];
            if (MathF.Abs(npc.X - world.PlayerPosition.X) < _viewSize && MathF.Abs(npc.Z - world.PlayerPosition.Z) < _viewSize)
            {
                var offset = MarkerOffset(world.PlayerPosition, npc, _viewSize);
                painter.Sprite(HudArt.MapNpc, (int)(center.X + offset.X) - 3, (int)(center.Y + offset.Y) - 3);
            }
        }

        for (var index = 0; index < party.Count; index++)
        {
            var member = party[index];
            var dx = member.X - world.PlayerPosition.X;
            var dz = member.Z - world.PlayerPosition.Z;
            if (MathF.Abs(dx) < _viewSize && MathF.Abs(dz) < _viewSize)
            {
                var offset = MarkerOffset(world.PlayerPosition, member, _viewSize);
                var near = dx * dx + dz * dz < PartyLimits.NearRange * PartyLimits.NearRange;
                painter.Stretch(Parties.HudPartyArt.MapMark, new Rectangle((int)(center.X + offset.X) - 3, (int)(center.Y + offset.Y) - 3, 8, 8), null,
                    near ? Color.White : Color.Red);
            }
        }

        painter.Rotated(HudArt.MapArrow, center, ArrowRotation(world.PlayerForward));
        painter.Sprite(HudArt.MapBox, box.X, box.Y);
        if (hovered)
        {
            painter.Highlight(HudArt.MapBox, box, HoverHighlight);
        }

        if (_field is not null)
        {
            var title = new Rectangle(box.X, box.Y - 16, 128, 16);
            painter.Stretch(_fieldTitle, title);
            if (hovered)
            {
                painter.Highlight(_fieldTitle, title, HoverHighlight >> 2);
            }
        }
    }

    private void DrawMap(HudPainter painter, Rectangle box, string path, PristonIntRectangle rectangle, Vector3 player)
    {
        var map = painter.Texture(path);
        if (map is null || !MapRegion(rectangle, player, _viewSize, out var source, out var destination))
        {
            return;
        }

        var from = new Rectangle((int)(source.X * map.Width), (int)(source.Y * map.Height),
            Math.Max(1, (int)(source.Z * map.Width)), Math.Max(1, (int)(source.W * map.Height)));
        var to = new Rectangle(box.X + 1 + (int)destination.X, box.Y + 1 + (int)destination.Y, (int)destination.Z, (int)destination.W);
        painter.Stretch(path, to, from);
    }

    private static void Request(HudSprites sprites, string path)
    {
        if (path is not null)
        {
            sprites.Request(path);
        }
    }

    private static void ReleaseUnless(HudSprites sprites, string path, string keep, string keepToo)
    {
        if (path is not null && !string.Equals(path, keep, StringComparison.OrdinalIgnoreCase) && !string.Equals(path, keepToo, StringComparison.OrdinalIgnoreCase))
        {
            sprites.Release(path);
        }
    }
}
