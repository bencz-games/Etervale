using Microsoft.Xna.Framework;
using PristonTale.Client.Net.Presence;
using PristonTale.Engine3D.Rendering;
using PristonTale.Contracts.World;
using PristonTale.Gameplay.World.Remote;

namespace PristonTale.Gameplay.World;

// The hover outline, as the original draws it around the character under the cursor (PristonRenderer.DrawOutlines): red
// for monsters, white for players; NPCs green only with classicNamePlates, none otherwise. Full width within 300 units
// of the camera, gone at 1300. It is set
// again every frame, so it survives a change of the entity's models.
public sealed partial class GameScene
{
    private const float OutlineFullDistance = 300f;
    private const float OutlineFadeDistance = 1000f;
    private static readonly Vector3 MonsterOutline = new(1f, 30f / 255f, 0f);
    private static readonly Vector3 NpcOutline = new(0f, 1f, 100f / 255f);
    private ulong _outlinedId;

    // The outline's width share at this distance from the camera.
    public static float OutlineStrength(float distance) => Math.Clamp(1f - (distance - OutlineFullDistance) / OutlineFadeDistance, 0f, 1f);

    private void UpdateOutline(ulong hovered, EntityKind kind)
    {
        if (_renderer is null || _remote is null || _camera is null)
        {
            return;
        }

        if (_outlinedId != 0 && _outlinedId != hovered)
        {
            SetEntityOutline(_outlinedId, Vector3.Zero, 0f);
        }

        _outlinedId = hovered;
        var classic = _presentation?.Requested.ClassicNamePlates == true;
        if (hovered == 0 || (kind == EntityKind.Npc && !classic) || !_remote.TryGetDrawnPosition(hovered, out var position))
        {
            return;
        }

        var distance = Vector3.Distance(position, _camera.Position);
        var strength = OutlineStrength(distance);
        var color = kind switch
        {
            EntityKind.Monster => MonsterOutline,
            EntityKind.Npc => NpcOutline,
            _ => Vector3.One
        };
        SetEntityOutline(hovered, color, strength);
    }

    private void SetEntityOutline(ulong entityId, Vector3 color, float strength)
    {
        Span<ActorHandle> handles = stackalloc ActorHandle[PlayerActorHandles.Most];
        var count = _remote.CopyActorHandles(entityId, handles);
        for (var index = 0; index < count; index++)
        {
            _renderer.SetActorOutline(handles[index], color, strength);
        }
    }
}
