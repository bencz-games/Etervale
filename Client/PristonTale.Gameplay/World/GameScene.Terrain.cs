using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using PristonTale.Effects.Vfx;
using PristonTale.Engine3D.Formats;
using PristonTale.Simulation.World;
using PristonTale.Contracts.Motion;
using PristonTale.Contracts.Units;

namespace PristonTale.Gameplay.World;

// The marks the local player's steps leave on the ground. Wading in shallow water (its surface above the feet and less
// than 32 units up) splashes the Water particle half a unit over the surface: at each step 8 units ahead, and every 64
// frames of 70 per second standing, every 32 walking (8 ahead) and every 16 running (14 ahead), attacking or casting.
// Out of the water, each step in an ice field leaves a footprint on an ice floor (image/mk-r.tga for the right foot 3
// units right and 5 ahead, mk-l.tga for the left), 12 x 12 units, tilted with the ice and turned with the player,
// fading to a fifth over five seconds before it goes; at most 20 lie at a time.
public sealed partial class GameScene
{
    public const string WaterSplash = "particle.water";
    public const string RightFootprint = "image/mk-r.tga";
    public const string LeftFootprint = "image/mk-l.tga";
    private const float WaterDepth = 32f;
    private const float FootprintLifeMs = 5000f;
    private const float FootprintHalfSize = 6f;
    private const int FootprintMax = 20;

    private readonly List<Footprint> _footprints = new();
    private readonly Texture2D[] _footprintTextures = new Texture2D[2];
    private float _terrainFrames;
    private int _terrainCounter;

    // A step of the local player's walk or run clip: foot 0 the right, 1 the left.
    private void LeaveStep(int foot)
    {
        if (_playerBody is null || _activeField is null || _skillFx is null)
        {
            return;
        }

        var feet = _playerBody.Position + Vector3.Up * GroundTraversal.VisualOffset;
        var forward = PlanarForward();
        if (TryWaterSurface(feet, out var surface))
        {
            Splash(feet + forward * 8f, surface);
            return;
        }

        if (_activeField.Definition.State != FieldState.Ice || _footprints.Count >= FootprintMax)
        {
            return;
        }

        var right = Vector3.Cross(forward, Vector3.Up);
        var at = feet + right * (foot == 0 ? 3f : -3f) + forward * 5f + Vector3.Up * 10f;
        if (_activeField.Collision.TryGetIceFloor(at, 10f, out var floor, out var normal))
        {
            _footprints.Add(new Footprint(foot, floor + Vector3.Up, normal, forward, _skillClockMs));
        }
    }

    // The splashes of standing, walking and running in shallow water, at the original's frame counter.
    private void UpdateTerrain(float seconds)
    {
        if (_playerBody is null || _skillFx is null)
        {
            return;
        }

        _terrainFrames += seconds * GameUnits.TicksPerSecond;
        while (_terrainFrames >= 1f)
        {
            _terrainFrames -= 1f;
            _terrainCounter++;
            var feet = _playerBody.Position + Vector3.Up * GroundTraversal.VisualOffset;
            if (!TryWaterSurface(feet, out var surface))
            {
                continue;
            }

            var (mask, ahead) = _playerController.Current switch
            {
                MotionCode.Walk => (0x1F, 8f),
                MotionCode.Run => (0xF, 14f),
                MotionCode.Attack or MotionCode.Skill => (0xF, 0f),
                _ => (0x3F, 0f)
            };
            if ((_terrainCounter & mask) == 0)
            {
                Splash(feet + PlanarForward() * ahead, surface);
            }
        }
    }

    private void QueueFootprints()
    {
        if (_footprints.Count == 0 || _skillFx is null)
        {
            return;
        }

        for (var index = _footprints.Count - 1; index >= 0; index--)
        {
            if (_skillClockMs - _footprints[index].BornMs >= FootprintLifeMs)
            {
                _footprints.RemoveAt(index);
            }
        }

        _footprintTextures[0] ??= _skillFx.Library.GetTexture(RightFootprint);
        _footprintTextures[1] ??= _skillFx.Library.GetTexture(LeftFootprint);
        foreach (var footprint in _footprints)
        {
            var fade = (float)(_skillClockMs - footprint.BornMs) * 200f / FootprintLifeMs;
            var along = OnPlane(footprint.Forward, footprint.Normal);
            var across = OnPlane(Vector3.Cross(footprint.Forward, Vector3.Up), footprint.Normal);
            _skillFx.Renderer.AddGroundQuad(_footprintTextures[footprint.Foot], VfxBlend.Alpha, footprint.Position, across * FootprintHalfSize,
                -along * FootprintHalfSize, new Vector4(1f, 1f, 1f, (255f - fade) / 255f));
        }
    }

    private bool TryWaterSurface(Vector3 feet, out float surface)
    {
        surface = 0f;
        return _activeField is not null && _activeField.Collision.TryGetWaterHeight(feet, out surface) && surface > feet.Y
            && surface < feet.Y + WaterDepth;
    }

    private void Splash(Vector3 at, float surface) => _skillFx?.SpawnAt(WaterSplash, new Vector3(at.X, surface + 0.5f, at.Z), Matrix.Identity);

    private Vector3 PlanarForward()
    {
        var forward = new Vector3(_playerForward.X, 0f, _playerForward.Z);
        return forward.LengthSquared() > 1e-6f ? Vector3.Normalize(forward) : Vector3.Forward;
    }

    private static Vector3 OnPlane(Vector3 direction, Vector3 normal)
    {
        var projected = direction - normal * Vector3.Dot(direction, normal);
        return projected.LengthSquared() > 1e-6f ? Vector3.Normalize(projected) : direction;
    }

    private readonly record struct Footprint(int Foot, Vector3 Position, Vector3 Normal, Vector3 Forward, double BornMs);
}
