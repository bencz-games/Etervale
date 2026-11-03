using Microsoft.Xna.Framework;
using PristonTale.Effects.Vfx;
using PristonTale.Simulation.Items;
using PristonTale.Contracts.Units;

namespace PristonTale.Gameplay.Effects;

// The class-up light a weapon's effect setting gives it (HeldItemGlow.ClassUp): two lights circle a point of the held
// weapon, each a pair of particle scripts (ClassUpWeapon1 follows the light, ClassUpWeapon2 trails behind it). The point
// lies along the weapon's length by its kind (a sword near the tip, a staff or bow further up, a javelin behind the
// grip, a claw beside the hand, anything else at the middle); the circle turns 70/4096 of a turn and its radius moves 10
// of 1000..1600 fixed units each 70 Hz frame, in the plane across the weapon (a claw's and a bow's along it).
public sealed class ClassUpLights
{
    public const string Light = "particle.ClassUpWeapon1";
    public const string Trail = "particle.ClassUpWeapon2";

    private readonly Dictionary<ulong, Holder> _held = new();
    private readonly HashSet<ulong> _seen = new();
    private readonly List<ulong> _gone = new();

    // One holder this frame: its weapon's world matrix, its reach along the weapon (model units), its catalog key, whether
    // the weapon is held in the left hand and two-handed. Holders not reported before EndFrame lose their lights.
    public void Hold(SkillPresentation skills, ulong entityId, Matrix weaponWorld, float reach, string weaponKey, bool leftHand, bool twoHanded,
        float seconds)
    {
        _seen.Add(entityId);
        if (!_held.TryGetValue(entityId, out var holder))
        {
            holder = new Holder();
            _held.Add(entityId, holder);
        }

        holder.Advance(seconds);
        var center = Center(weaponKey, reach, leftHand, twoHanded);
        var across = Across(weaponKey, leftHand);
        var radius = holder.Radius / GameUnits.FixedScale;
        var turn = holder.Angle * GameUnits.RadiansPerAngle;
        var offset = across
            ? new Vector3(0f, MathF.Sin(turn) * radius, MathF.Cos(turn) * radius)
            : new Vector3(-MathF.Sin(turn) * radius, 0f, MathF.Cos(turn) * radius);
        holder.Place(skills, 0, Vector3.Transform(center + offset, weaponWorld));
        holder.Place(skills, 1, Vector3.Transform(center - offset, weaponWorld));
    }

    public void EndFrame()
    {
        _gone.Clear();
        foreach (var (entity, holder) in _held)
        {
            if (!_seen.Contains(entity))
            {
                holder.Stop();
                _gone.Add(entity);
            }
        }

        foreach (var entity in _gone)
        {
            _held.Remove(entity);
        }

        _seen.Clear();
    }

    // The lights' centre in the weapon's model space, its length along Y.
    public static Vector3 Center(string weaponKey, float reach, bool leftHand, bool twoHanded)
    {
        var kind = ItemEquipmentMetadata.KindOf(weaponKey);
        Vector3 point;
        if (leftHand && kind == ItemKind.Bow)
        {
            // A bow in the left hand: just short of its end, turned a quarter across.
            point = new Vector3(reach - 10f, 0f, 0f);
        }
        else if (kind == ItemKind.ThrowingWeapon)
        {
            point = new Vector3(0f, -0.4f * reach, 0f);
        }
        else if (kind == ItemKind.Sword)
        {
            point = new Vector3(0f, reach * (reach * GameUnits.FixedScale > 5000f ? 0.75f : 0.8f), 0f);
        }
        else if (kind == ItemKind.Claw)
        {
            point = new Vector3(2f * reach, 0f, 0f);
        }
        else if (kind == ItemKind.Staff)
        {
            point = new Vector3(0f, 0.7f * reach, 0f);
        }
        else if (kind == ItemKind.Bow)
        {
            point = new Vector3(0.7f * reach * MathF.Sin(MathHelper.PiOver4), 0.7f * reach * MathF.Cos(MathHelper.PiOver4), 0f);
        }
        else
        {
            point = new Vector3(0f, 0.5f * reach, 0f);
        }

        if (twoHanded)
        {
            // A two-handed weapon's point leans a fifth of 45 degrees.
            var lean = -MathHelper.PiOver4 / 5f;
            point = new Vector3(point.X, point.Y * MathF.Cos(lean), -point.Y * MathF.Sin(lean));
        }

        return point;
    }

    private static bool Across(string weaponKey, bool leftHand)
    {
        return leftHand || ItemEquipmentMetadata.KindOf(weaponKey) is ItemKind.Claw or ItemKind.Bow;
    }

    private sealed class Holder
    {
        private readonly VfxHandle[] _effects = new VfxHandle[4];
        private float _frames;
        private bool _growing;

        public float Angle { get; private set; }
        public float Radius { get; private set; } = 1500f;

        public void Advance(float seconds)
        {
            _frames += seconds * GameUnits.TicksPerSecond;
            while (_frames >= 1f)
            {
                _frames -= 1f;
                Angle = (Angle + 70f) % GameUnits.AngleTurn;
                if (!_growing)
                {
                    Radius -= 10f;
                    _growing = Radius < 1000f;
                }
                else
                {
                    Radius += 10f;
                    _growing = !(Radius > 1600f);
                }
            }
        }

        // Light 0 or 1: its two scripts move to the point, started again once they ended.
        public void Place(SkillPresentation skills, int light, Vector3 point)
        {
            for (var part = 0; part < 2; part++)
            {
                var slot = light * 2 + part;
                if (!_effects[slot].Alive)
                {
                    _effects[slot] = skills.SpawnAt(part == 0 ? Light : Trail, point, Matrix.Identity);
                }

                _effects[slot].MoveTo(point);
            }
        }

        public void Stop()
        {
            for (var slot = 0; slot < _effects.Length; slot++)
            {
                _effects[slot].Stop();
                _effects[slot] = default;
            }
        }
    }
}
