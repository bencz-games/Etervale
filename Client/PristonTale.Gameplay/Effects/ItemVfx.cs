using Microsoft.Xna.Framework;
using PristonTale.Contracts.Units;
using PristonTale.Effects.Vfx;

namespace PristonTale.Gameplay.Effects;

// Effects of used items built on the effect system: the skill-learn light of hair tints and force orbs, the weapon glow
// of a running force orb, the potion drops, the firecracker's launch sparks and flash, and the return sparkle of ether
// cores and crystal summons. Motions are the original's in fixed units per 70 Hz tick: a speed of s is s x 70 / 256
// units a second, an acceleration of a is a x 70 x 70 / 256 units a second squared.
public static class ItemVfx
{
    public const string Learn = "vfx.item.learn";
    // A mastery item's effect on its user.
    public const string Mastery = "vfx.agony";
    public const string FirecrackerRocket = "vfx.item.firecracker.rocket";
    public const string FirecrackerFlash = "vfx.item.firecracker.flash";
    public const string Return = "vfx.item.return";

    private const float PerTick = GameUnits.TicksPerSecond / GameUnits.FixedScale;
    private const float PerTickSquared = GameUnits.TicksPerSecond * GameUnits.TicksPerSecond / GameUnits.FixedScale;
    // Degrees a second of an angle of one original unit a tick.
    private const float DegreesPerAngleTick = 360f * GameUnits.TicksPerSecond / GameUnits.AngleTurn;
    public const float GlowSeconds = 30f;
    private const string LearnTexture = "Effect/ImageData/SkillUp1/SkillUp1.tga";
    private const string Flare = "Effect/AssaEffect/flare.tga";

    // The orb colours by orb number (1..12).
    public static readonly Color[] OrbColors =
    [
        new(80, 20, 150), new(80, 20, 150), new(80, 20, 150), new(80, 20, 150), new(140, 90, 20), new(20, 100, 20), new(0, 100, 200),
        new(190, 30, 150), new(255, 40, 20), new(220, 255, 10), new(10, 255, 10), new(255, 255, 255)
    ];

    public static string OrbGlow(int orbIndex) => $"vfx.item.forceorb.{orbIndex}";

    // The drops of a potion by resource: 0 life (red), 1 mana (blue), 2 stamina (green).
    public static string Potion(int resource) => $"vfx.item.potion.{resource}";

    public static void Register(VfxLibrary library)
    {
        if (library is null)
        {
            return;
        }

        library.Register(LearnDefinition());
        library.Register(FirecrackerRocketDefinition());
        library.Register(FirecrackerFlashDefinition());
        library.Register(ReturnDefinition());
        for (var resource = 0; resource < 3; resource++)
        {
            library.Register(PotionDefinition(resource));
        }

        for (var index = 0; index < OrbColors.Length; index++)
        {
            library.Register(OrbDefinition(index));
        }
    }

    // The skill-learn light: thin streaks rise from a ring around the player, 60 short ones from 11 units out and 50
    // taller ones from 13 units out that climb higher and later, with a white light.
    private static VfxDefinition LearnDefinition() => new()
    {
        Name = Learn,
        Duration = 0.75f,
        Emitters =
        [
            Streaks(60, 0.3f, [11f, 15.5f], [0.55f, 0.65f], [9f, 11f], 2.2f),
            Streaks(50, 0.7f, [13f, 15.5f], [0.9f, 1.05f], [18f, 22f], 1.6f) with { Delay = 0.2f }
        ],
        Lights = [new VfxLightDefinition { Anchor = VfxAnchor.Caster, Offset = [0f, 20f, 0f], Color = [0.78f, 0.78f, 0.78f], Power = 260, Decay = 1 }]
    };

    private static VfxEmitterDefinition Streaks(int count, float emitSeconds, float[] radius, float[] lifetime, float[] speed, float width) => new()
    {
        Texture = LearnTexture,
        Anchor = VfxAnchor.Caster,
        Shape = VfxShape.Ring,
        Radius = radius,
        EmitTime = emitSeconds,
        Rate = count / emitSeconds,
        MaxParticles = count,
        Lifetime = lifetime,
        Speed = speed,
        Direction = [0f, 1f, 0f],
        Spread = 2f,
        Align = VfxAlign.Velocity,
        Stretch = 0.45f,
        Softness = 2f,
        Intensity = 2f,
        Size = [new VfxKey(0f, [width])],
        Color = [new VfxKey(0f, [1f, 1f, 1f, 0f]), new VfxKey(0.3f, [1f, 1f, 1f, 0.4f]), new VfxKey(0.6f, [1f, 1f, 1f, 0.7f]),
            new VfxKey(1f, [1f, 1f, 1f, 0f])]
    };

    // A potion: the Light1 halo, and 30 drops that leave in random directions at 70 outward and up, fall at 3 to 5 and
    // whose path turns a third of a turn plus 500 angle units about the drinker while they move (55 to 74 ticks), then
    // settle and fade through their animation's last frame. Sizes are full extents.
    private static VfxDefinition PotionDefinition(int resource)
    {
        const float speed = 70f * PerTick;
        const float firstTick = 55f * GameUnits.TickSeconds;
        const float lastTick = 74f * GameUnits.TickSeconds;
        const float turn = (GameUnits.AngleTurn / 3 + 500) * 360f / GameUnits.AngleTurn;
        return new VfxDefinition
        {
            Name = Potion(resource),
            Duration = 1.1f,
            Parts =
            [
                new VfxPartDefinition { Animation = "Light1.ini", Size = [120f, 120f] },
                new VfxPartDefinition
                {
                    Count = 30,
                    Animation = $"Potion{resource + 1}.ini",
                    Loop = true,
                    Size = [8f, 8f],
                    RingRandom = true,
                    Radial = [speed, speed],
                    Rise = [speed, speed],
                    Gravity = [-5f * PerTickSquared, -3f * PerTickSquared],
                    MoveTime = [firstTick, lastTick],
                    Swirl = [turn / lastTick, turn / firstTick],
                    Spiral = true,
                    End = VfxPartEnd.Hold
                }
            ]
        };
    }

    // The return sparkle, 12.8 units above the feet: 20 sparks leave 39 units up every 2 ticks at 70 outward and 200 up,
    // falling at 6 to 8, and 20 more from 19.5 units down every 4 ticks from tick 60, rising at 2 to 4; each one's path
    // turns faster and faster (half a turn's step over its 60 to 79 ticks above, a third of a turn's over 80 to 99
    // below), then it settles and fades. From tick 100, 25 streaks rise around 11.7 units out, 12 degrees apart; from
    // tick 130, 20 lines 93.75 units long rise around 7.8 units out and 19.5 up, 18 degrees apart; both start up to 35
    // ticks late at random. Lights at the start and at ticks 100 and 160.
    private static VfxDefinition ReturnDefinition()
    {
        const float origin = 48f - 9000f / GameUnits.FixedScale;
        const float lift = 5000f / GameUnits.FixedScale;
        return new VfxDefinition
        {
            Name = Return,
            Duration = 3.4f,
            Parts =
            [
                ReturnSparks(0f, 2f, origin + 10000f / GameUnits.FixedScale, 10f, [-8f, -6f], 60f, 79f, GameUnits.AngleTurn / 2),
                ReturnSparks(60f, 4f, origin - lift, 16f, [2f, 4f], 80f, 99f, GameUnits.AngleTurn / 3),
                new VfxPartDefinition
                {
                    Count = 25,
                    Delay = 100f * GameUnits.TickSeconds,
                    DelayRandom = 35f * GameUnits.TickSeconds,
                    Shape = VfxPartShape.Streak,
                    Animation = "Return1.ini",
                    Loop = true,
                    Width = [900f / GameUnits.FixedScale, 900f / GameUnits.FixedScale],
                    Offset = [0f, origin, 0f],
                    Ring = [3000f / GameUnits.FixedScale, 3000f / GameUnits.FixedScale],
                    RingStep = 12f,
                    Radial = [6f * PerTick, 6f * PerTick],
                    Rise = [799f * PerTick, 799f * PerTick],
                    MoveTime = [16f * GameUnits.TickSeconds, 16f * GameUnits.TickSeconds],
                    End = VfxPartEnd.Hold
                },
                new VfxPartDefinition
                {
                    Count = 20,
                    Delay = 130f * GameUnits.TickSeconds,
                    DelayRandom = 35f * GameUnits.TickSeconds,
                    Shape = VfxPartShape.Line,
                    LineSeconds = 30f * GameUnits.TickSeconds,
                    Animation = "Return1.ini",
                    Loop = true,
                    Width = [800f / GameUnits.FixedScale, 800f / GameUnits.FixedScale],
                    Offset = [0f, origin + lift, 0f],
                    Ring = [2000f / GameUnits.FixedScale, 2000f / GameUnits.FixedScale],
                    RingStep = 18f,
                    Rise = [800f * PerTick, 800f * PerTick],
                    MoveTime = [6f * GameUnits.TickSeconds, 6f * GameUnits.TickSeconds],
                    End = VfxPartEnd.Hold
                }
            ],
            Lights =
            [
                new VfxLightDefinition { Offset = [0f, origin, 0f], Color = [180f / 255f, 180f / 255f, 180f / 255f], Power = 200, Decay = 1 },
                new VfxLightDefinition
                {
                    Offset = [0f, origin + lift, 0f], Color = [250f / 255f, 250f / 255f, 250f / 255f], Power = 200, Decay = 1,
                    Delay = 100f * GameUnits.TickSeconds
                },
                new VfxLightDefinition
                {
                    Offset = [0f, origin + lift, 0f], Color = [250f / 255f, 250f / 255f, 250f / 255f], Power = 170, Decay = 1,
                    Delay = 160f * GameUnits.TickSeconds
                }
            ]
        };
    }

    // One ring of return sparks. The angle after m ticks is step x m (m + 1) / 2 with step = turn / life, so the swirl
    // starts at half a step a tick and speeds up by a step a tick every tick.
    private static VfxPartDefinition ReturnSparks(float delayTicks, float stepTicks, float height, float size, float[] gravity, float firstLife,
        float lastLife, int turn)
    {
        var fastStep = turn / firstLife * DegreesPerAngleTick;
        var slowStep = turn / lastLife * DegreesPerAngleTick;
        return new VfxPartDefinition
        {
            Count = 20,
            Delay = delayTicks * GameUnits.TickSeconds,
            DelayStep = stepTicks * GameUnits.TickSeconds,
            Animation = "ReturnParticle1.ini",
            Loop = true,
            Size = [size, size],
            Offset = [0f, height, 0f],
            RingRandom = true,
            Radial = [70f * PerTick, 70f * PerTick],
            Rise = [200f * PerTick, 200f * PerTick],
            Gravity = [gravity[0] * PerTickSquared, gravity[1] * PerTickSquared],
            MoveTime = [firstLife * GameUnits.TickSeconds, lastLife * GameUnits.TickSeconds],
            Swirl = [slowStep / 2f, fastStep / 2f],
            SwirlGrowth = [slowStep * GameUnits.TicksPerSecond, fastStep * GameUnits.TicksPerSecond],
            Spiral = true,
            End = VfxPartEnd.Hold
        };
    }

    // The 24 sparks a firecracker rocket throws where it lifts off: FireParticle1 billboards bursting in every direction
    // at 20 to 45 units a second and falling at 60.
    private static VfxDefinition FirecrackerRocketDefinition() => new()
    {
        Name = FirecrackerRocket,
        Duration = 1f,
        Parts = [new VfxPartDefinition { Count = 24, Animation = "FireParticle1.ini", Size = [10f, 10f], Burst = [20f, 45f], Gravity = [-60f, -60f] }]
    };

    // The flash where the rocket bursts.
    private static VfxDefinition FirecrackerFlashDefinition() => new()
    {
        Name = FirecrackerFlash,
        Duration = 0.5f,
        Parts = [new VfxPartDefinition { Animation = "Power1.ini", Size = [100f, 100f] }]
    };

    private static VfxDefinition OrbDefinition(int index)
    {
        var color = OrbColors[index].ToVector3();
        return new VfxDefinition
        {
            Name = OrbGlow(index),
            Duration = GlowSeconds,
            Emitters =
            [
                new VfxEmitterDefinition
                {
                    Texture = Flare,
                    Anchor = VfxAnchor.Caster,
                    Bone = "Bip weapon01",
                    Local = true,
                    Rate = 24f,
                    MaxParticles = 48,
                    Lifetime = [0.35f, 0.5f],
                    Shape = VfxShape.Sphere,
                    Radius = [0f, 10f],
                    Softness = 0f,
                    Intensity = 2f,
                    Size = [new VfxKey(0f, [5f]), new VfxKey(0.5f, [9f]), new VfxKey(1f, [5f])],
                    Color = [new VfxKey(0f, [color.X, color.Y, color.Z, 0f]), new VfxKey(0.3f, [color.X, color.Y, color.Z, 0.8f]),
                        new VfxKey(1f, [color.X, color.Y, color.Z, 0f])]
                }
            ]
        };
    }
}
