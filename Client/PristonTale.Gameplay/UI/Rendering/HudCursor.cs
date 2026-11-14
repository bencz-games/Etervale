using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using PristonTale.Assets;
using PristonTale.Contracts.World;
using PristonTale.Engine3D.Diagnostics;
using PristonTale.Engine3D.Rendering;

namespace PristonTale.Gameplay.UI.Rendering;

public enum HudCursorKind
{
    Default,
    Attack,
    Talk,
    GetItem,
    GetItemHeld,
    Buy,
    Sell,
    Repair,
    // No pointer: the dragged item's icon takes its place.
    Hidden
}

// The original pointer: the yellow arrow, its gem red over an attackable monster, a speech balloon over an NPC,
// and the hand variants over drops and shop items. Render the original TGA as an in-game overlay with point sampling:
// native SDL colour cursors can be rescaled/filtered by the desktop compositor (notably under fractional DPI), and
// also force a GPU readback while they are being created. The top-left pixel remains the pointer hotspot.
public sealed class HudCursor : IDisposable
{
    private const string Folder = "image/Sinimage/Cursor/";

    private readonly Texture2D[] _textures;

    private HudCursor(Texture2D[] textures) => _textures = textures;

    // If the original default image is absent, leave the native system arrow visible.
    public bool HasDefaultArt => _textures[(int)HudCursorKind.Default] is not null;

    // A summon (a monster with an owner) and other players keep the arrow, as the original only changed it for
    // monsters of the field and NPCs.
    public static HudCursorKind ForHovered(EntityKind kind, ulong ownerId) => kind switch
    {
        EntityKind.Monster when ownerId == 0 => HudCursorKind.Attack,
        EntityKind.Npc => HudCursorKind.Talk,
        _ => HudCursorKind.Default
    };

    // A drop under the pointer wins over the monster or NPC behind it.
    public static HudCursorKind ForHovered(bool dropHovered, bool leftHeld, EntityKind kind, ulong ownerId) =>
        dropHovered ? leftHeld ? HudCursorKind.GetItemHeld : HudCursorKind.GetItem : ForHovered(kind, ownerId);

    public static string ArtPath(HudCursorKind kind) => Folder + kind switch
    {
        HudCursorKind.Attack => "Attack_Cursor.tga",
        HudCursorKind.Talk => "Talk_Cursor.tga",
        HudCursorKind.GetItem => "GetItem_Cursor1.tga",
        HudCursorKind.GetItemHeld => "GetItem_Cursor2.tga",
        HudCursorKind.Buy => "BuyCursor.tga",
        HudCursorKind.Sell => "SellCursor.tga",
        HudCursorKind.Repair => "RepairCursor.tga",
        _ => "DefaultCursor.tga"
    };

    public static HudCursor Load(GraphicsDevice device, string assetRoot, IClientLog log)
    {
        var kinds = Enum.GetValues<HudCursorKind>();
        var textures = new Texture2D[kinds.Length];
        foreach (var kind in kinds)
        {
            if (kind == HudCursorKind.Hidden)
            {
                continue;
            }

            var path = AssetFiles.Combine(assetRoot, ArtPath(kind));
            try
            {
                if (!AssetFiles.Exists(path))
                {
                    log.Warn($"[Cursor] missing {ArtPath(kind)}; the system arrow stays.");
                    continue;
                }

                var image = PristonImageDecoder.Decode(AssetFiles.ReadAllBytes(path), Path.GetExtension(path));
                if (!image.IsValid)
                {
                    log.Warn($"[Cursor] unreadable {ArtPath(kind)}; the system arrow stays.");
                    continue;
                }

                var texture = new Texture2D(device, image.Width, image.Height, false, SurfaceFormat.Color);
                texture.SetData(image.Pixels);
                textures[(int)kind] = texture;
            }
            catch (Exception exception) when (exception is IOException or ArgumentException or InvalidOperationException)
            {
                log.Error($"[Cursor] {ArtPath(kind)} failed", exception);
            }
        }

        return new HudCursor(textures);
    }

    public void Draw(SpriteBatch batch, Point pointer, HudCursorKind kind)
    {
        // Item dragging draws its own icon at the pointer. An unavailable cursor falls back to the original arrow.
        if (kind == HudCursorKind.Hidden || !HasDefaultArt)
        {
            return;
        }

        var texture = _textures[(int)kind] ?? _textures[(int)HudCursorKind.Default];
        // TGA colour channels contain straight (non-premultiplied) alpha. Never bilinearly blur 32-pixel pixel art.
        batch.Begin(SpriteSortMode.Deferred, BlendState.NonPremultiplied, SamplerState.PointClamp,
            DepthStencilState.None, RasterizerState.CullNone);
        batch.Draw(texture, new Vector2(pointer.X, pointer.Y), Color.White);
        batch.End();
    }

    public void Dispose()
    {
        foreach (var texture in _textures)
        {
            texture?.Dispose();
        }
    }
}
