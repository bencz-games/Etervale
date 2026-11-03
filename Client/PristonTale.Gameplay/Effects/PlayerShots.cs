using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using PristonTale.Effects.Vfx;
using PristonTale.Engine3D.Formats;
using PristonTale.Engine3D.Rendering;
using PristonTale.Simulation.Items;
using PristonTale.Contracts.Motion;
using PristonTale.Contracts.Units;
using System.Runtime.InteropServices;

namespace PristonTale.Gameplay.Effects;

// The basic shots of ranged weapons, thrown as the original throws them at the first event of a player's attack clip:
// a bow's arrow (weapons/arrow.smd, 8 units a frame of 70 per second), a javelin itself (6 a frame, its front along its
// length; the held javelin hides while it flies) and, for magicians and priestesses, a staff's fire ball (the Imp's
// shot); other classes strike with a staff. An arrow or javelin flies
// from the weapon toward its target's chest (24 above its feet) in a straight line; once within 64 units across and 80
// up of the target it flies 7 frames more and ends, as it does after 120 frames. A glowing bow or javelin sheds sparks of
// its glow colour (half of it plus 128, Effect/ImageData/Particle/cc.tga) along its flight, which fade over 0.7 s.
public sealed class PlayerShots
{
    public const string ArrowModel = "weapons/arrow.smd";
    public const string StaffShot = "vfx.monster.imp.attack.hit";
    public const string SparkTexture = "Effect/ImageData/Particle/cc.tga";
    private const float ArrowStep = 8f;
    private const float JavelinStep = 6f;
    private const int MaxFrames = 120;
    private const int FramesAfterHit = 7;
    private const float SparkLife = 0.7f;
    private const float SparksPerFrame = 3.5f;

    private readonly Dictionary<ulong, ulong> _targets = new();
    private readonly Dictionary<ulong, ClipEventCursor> _clips = new();
    private readonly List<Shot> _shots = new();
    private readonly List<Spark> _sparks = new();
    private readonly HashSet<ulong> _seen = new();
    private readonly List<ulong> _gone = new();
    private readonly Random _random = new();
    private Texture2D _sparkTexture;

    public int Flying => _shots.Count;
    public int Sparks => _sparks.Count;

    // The attacker's next basic attack aims at this target.
    public void ObserveSwing(ulong attackerId, ulong targetId) => _targets[attackerId] = targetId;

    // True while the attacker's thrown javelin flies (its held one is hidden).
    public bool Throwing(ulong attackerId)
    {
        foreach (var shot in _shots)
        {
            if (shot.AttackerId == attackerId && shot.Javelin)
            {
                return true;
            }
        }

        return false;
    }

    // One player this frame: its body clip, its weapon (catalog key, world matrix, glow); a shot is thrown when the
    // attack clip passes its first event.
    public void Hold(SkillPresentation skills, ulong attackerId, PristonMotionInfo motion, float ticks, string weaponKey, Matrix weaponWorld,
        in HeldItemGlow glow, Vector3 attackerPosition, bool staffCaster, Func<ulong, Vector3?> targetOf)
    {
        _seen.Add(attackerId);
        ref var clip = ref CollectionsMarshal.GetValueRefOrAddDefault(_clips, attackerId, out _);
        clip.Advance(motion, ticks);
        if (motion.State != (uint)MotionCode.Attack || weaponKey.Length < 3 || !clip.Passed(0) || !_targets.TryGetValue(attackerId, out var targetId)
            || targetOf(targetId) is not { } target)
        {
            return;
        }

        var kind = ItemEquipmentMetadata.KindOf(weaponKey);
        var chest = target + Vector3.Up * 24f;
        if (kind == ItemKind.Staff)
        {
            if (staffCaster)
            {
                skills.Spawn(StaffShot, attackerId, targetId, attackerPosition);
            }

            return;
        }

        if (kind is not (ItemKind.Bow or ItemKind.ThrowingWeapon))
        {
            return;
        }

        var from = weaponWorld.Translation;
        var direction = chest - from;
        if (direction.LengthSquared() < 1e-4f)
        {
            return;
        }

        var javelin = kind == ItemKind.ThrowingWeapon;
        if (javelin && !World.Remote.EquipmentVisuals.HasModel(weaponKey))
        {
            return;
        }

        // The flying model is built when the shot leaves (once per model, cached by the content), never in Draw; a shot
        // without one still flies, strikes and sheds its sparks.
        var model = Content?.LoadModel(javelin ? World.Remote.EquipmentVisuals.MeshPath(weaponKey) : ArrowModel);
        _shots.Add(new Shot(attackerId, targetId, model, javelin, from, Vector3.Normalize(direction) * (javelin ? JavelinStep : ArrowStep),
            glow.Glows ? GlowColor(glow) : null));
    }

    // Moves the shots and their sparks; players not reported this frame forget their clip.
    public void Update(float seconds, Func<ulong, Vector3?> targetOf)
    {
        _gone.Clear();
        foreach (var attacker in _clips.Keys)
        {
            if (!_seen.Contains(attacker))
            {
                _gone.Add(attacker);
            }
        }

        foreach (var attacker in _gone)
        {
            _clips.Remove(attacker);
            _targets.Remove(attacker);
        }

        _seen.Clear();
        for (var index = _sparks.Count - 1; index >= 0; index--)
        {
            var spark = _sparks[index] with { Age = _sparks[index].Age + seconds };
            if (spark.Age >= SparkLife)
            {
                _sparks.RemoveAt(index);
            }
            else
            {
                _sparks[index] = spark;
            }
        }

        for (var index = _shots.Count - 1; index >= 0; index--)
        {
            var shot = _shots[index];
            shot.Elapsed += seconds;
            while (shot.Elapsed >= GameUnits.TickSeconds && !shot.Done)
            {
                shot.Elapsed -= GameUnits.TickSeconds;
                StepShot(shot, targetOf(shot.TargetId));
            }

            if (shot.Done)
            {
                _shots.RemoveAt(index);
            }
        }
    }

    // The effect content the flying models come from; null until the effect library is loaded.
    public VfxContent Content { get; set; }

    // Queues the flying weapons and the sparks for the next effect draw.
    public void Draw(VfxRenderer renderer, VfxLibrary library)
    {
        foreach (var shot in _shots)
        {
            if (shot.Model is not { } model)
            {
                continue;
            }

            var forward = Vector3.Normalize(shot.Velocity);
            var facing = Matrix.CreateWorld(Vector3.Zero, forward, MathF.Abs(forward.Y) > 0.999f ? Vector3.Forward : Vector3.Up);
            // A javelin's length runs along its model's Y; it turns to lie along the flight.
            var lie = shot.Javelin ? Matrix.CreateRotationX(-MathHelper.PiOver2) : Matrix.Identity;
            renderer.AddModel(model, lie * facing * Matrix.CreateTranslation(shot.Position), VfxBlend.Alpha);
        }

        if (_sparks.Count == 0)
        {
            return;
        }

        _sparkTexture ??= library.GetTexture(SparkTexture);
        foreach (var spark in _sparks)
        {
            var life = spark.Age / SparkLife;
            var color = Vector3.Lerp(spark.Color, spark.Color / 2f, life);
            renderer.AddBillboard(_sparkTexture, VfxBlend.Additive, spark.Position, 1f, new Vector4(color, 255f * (1f - life)));
        }
    }

    private void StepShot(Shot shot, Vector3? target)
    {
        shot.Position += shot.Velocity;
        shot.Frames++;
        if (shot.HitFrames > 0)
        {
            shot.HitFrames++;
            if (target is { } struck)
            {
                Shed(shot, new Vector3(struck.X, shot.Position.Y, struck.Z));
            }

            shot.Done = shot.HitFrames > FramesAfterHit;
            return;
        }

        Shed(shot, shot.Position);
        if (target is { } aim)
        {
            var delta = shot.Position - aim;
            if (MathF.Abs(delta.X) < 64f && MathF.Abs(delta.Z) < 64f && MathF.Abs(delta.Y) < 80f)
            {
                shot.HitFrames = 1;
            }
        }

        shot.Done = shot.Frames > MaxFrames;
    }

    private void Shed(Shot shot, Vector3 at)
    {
        if (shot.Spark is not { } color)
        {
            return;
        }

        shot.SparkDue += SparksPerFrame;
        for (; shot.SparkDue >= 1f; shot.SparkDue -= 1f)
        {
            var drift = new Vector3(_random.NextSingle() - 0.5f, _random.NextSingle() - 0.5f, _random.NextSingle() - 0.5f) * 0.5f;
            _sparks.Add(new Spark(at + drift, color, 0f));
        }
    }

    private static Vector3 GlowColor(in HeldItemGlow glow) => new((glow.R >> 1) + 128, (glow.G >> 1) + 128, (glow.B >> 1) + 128);

    private sealed class Shot(ulong attackerId, ulong targetId, PristonMeshEffect model, bool javelin, Vector3 position, Vector3 velocity, Vector3? spark)
    {
        public ulong AttackerId { get; } = attackerId;
        public ulong TargetId { get; } = targetId;
        public PristonMeshEffect Model { get; } = model;
        public bool Javelin { get; } = javelin;
        public Vector3 Position { get; set; } = position;
        public Vector3 Velocity { get; } = velocity;
        public Vector3? Spark { get; } = spark;
        public float Elapsed { get; set; }
        public int Frames { get; set; }
        public int HitFrames { get; set; }
        public float SparkDue { get; set; }
        public bool Done { get; set; }
    }

    private readonly record struct Spark(Vector3 Position, Vector3 Color, float Age);
}
