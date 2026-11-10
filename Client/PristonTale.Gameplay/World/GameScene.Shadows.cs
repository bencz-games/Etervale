using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using PristonTale.Effects.Vfx;
using PristonTale.Contracts.Units;

namespace PristonTale.Gameplay.World;

// While shadow maps are off every character casts the original's ground shadow: image/bshadow.bmp darkening the floor
// one unit above the character's feet, turned with it and sized by its shadow size (players medium; a monster's or NPC's
// from its description, none for -1). A dead character's shadow goes 40 frames of 70 per second into its death.
public sealed partial class GameScene
{
    public const string BlobShadowTexture = "image/bshadow.bmp";
    private const double BlobShadowDeathMs = 40 * 1000d / GameUnits.TicksPerSecond;

    // Half extents of the four shadows across and along the character.
    private static readonly Vector2[] BlobShadowSizes = [new(12f, 10f), new(17f, 14f), new(26f, 20f), new(38f, 30f)];

    private readonly Dictionary<ulong, double> _shadowDeaths = new();
    private readonly HashSet<ulong> _shadowSeen = new();
    private readonly List<ulong> _shadowGone = new();
    private Action<ulong, Vector3, float, int, bool> _queueShadow;
    private Texture2D _blobShadow;
    private double _shadowClockMs;

    private void QueueBlobShadows()
    {
        if (ShadowsEnabled || _skillFx is null || _remote is null)
        {
            _shadowDeaths.Clear();
            return;
        }

        _blobShadow ??= _skillFx.Library.GetTexture(BlobShadowTexture);
        _shadowClockMs = _skillClockMs;
        if (_playerBody is not null)
        {
            var feet = PlayerFeet;
            QueueBlobShadow(_remote.SelfEntityId, feet, PlayerYaw, 1, _playerController.IsDead);
        }

        _remote.ForEachShadow(_queueShadow ??= QueueBlobShadow);
        _shadowGone.Clear();
        foreach (var entity in _shadowDeaths.Keys)
        {
            if (!_shadowSeen.Contains(entity))
            {
                _shadowGone.Add(entity);
            }
        }

        foreach (var entity in _shadowGone)
        {
            _shadowDeaths.Remove(entity);
        }

        _shadowSeen.Clear();
    }

    private void QueueBlobShadow(ulong entityId, Vector3 position, float yaw, int size, bool dead)
    {
        if (dead)
        {
            _shadowSeen.Add(entityId);
            if (!_shadowDeaths.TryGetValue(entityId, out var since))
            {
                since = _shadowClockMs;
                _shadowDeaths[entityId] = since;
            }

            if (_shadowClockMs - since >= BlobShadowDeathMs)
            {
                return;
            }
        }

        var extent = BlobShadowSizes[Math.Clamp(size, 0, BlobShadowSizes.Length - 1)];
        var turn = Matrix.CreateRotationY(yaw);
        _skillFx.Renderer.AddGroundQuad(_blobShadow, VfxBlend.Shadow, position + Vector3.Up, Vector3.TransformNormal(Vector3.Right, turn) * extent.X,
            Vector3.TransformNormal(Vector3.Backward, turn) * extent.Y, Vector4.One);
    }
}
