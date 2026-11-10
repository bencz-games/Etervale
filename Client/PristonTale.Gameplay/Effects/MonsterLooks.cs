using Microsoft.Xna.Framework;
using PristonTale.Simulation.World.Npcs;

namespace PristonTale.Gameplay.Effects;

// The look a monster keeps while it is present: light added to its body (the original's light units over LightUnits, as
// the presentation's status tints), its opacity (1 + the original's alpha offset over 255) and whether that light pulses
// (a boss's, BossPulseTicks a wave).
public readonly record struct MonsterLook(Vector3 Light, float Opacity, bool Pulses = false)
{
    public static MonsterLook Normal => new(Vector3.Zero, 1f);
}

// Looks by family as the original sets them when a monster appears: the translucent Illusion Knight and Mr. Ghost, the
// yellow wild Wolverine, the golden Guardian Saint and the coloured Furies of levels 118 (yellow), 119 (red) and 120
// (blue), translucent too. A wild boss glows red-violet instead; any boss's light pulses, down from full to none and back
// over 64 ticks of 70 Hz. A player's summon keeps the wild look only where the original gives it regardless of side.
public static class MonsterLooks
{
    public const float LightUnits = 256f;
    public const int BossPulseTicks = 64;
    private const float AlphaUnits = 255f;

    public static MonsterLook Of(int soundCode, int level, bool boss, bool summoned)
    {
        var (light, alpha) = soundCode switch
        {
            CharacterSoundCodes.IllusionKnight => (Vector3.Zero, -180f),
            CharacterSoundCodes.Wolverine when !summoned => (new Vector3(100f, 100f, 0f), 0f),
            CharacterSoundCodes.MrGhost => (Vector3.Zero, -80f),
            CharacterSoundCodes.Fury => level switch
            {
                118 => (new Vector3(100f, 100f, 0f), -80f),
                119 => (new Vector3(100f, 0f, 0f), -80f),
                120 => (new Vector3(0f, 0f, 100f), -80f),
                _ => (Vector3.Zero, 0f)
            },
            CharacterSoundCodes.GuardianSaint => (new Vector3(80f, 80f, 20f), 0f),
            _ => (Vector3.Zero, 0f)
        };

        if (boss && !summoned)
        {
            light = new Vector3(130f, 50f, 70f);
        }

        return new MonsterLook(light / LightUnits, 1f + alpha / AlphaUnits, boss);
    }

    // The share of a pulsing light shown at a tick: 31/32 down to 0 over the wave's first half, then back up.
    public static float Pulse(int tick)
    {
        const int half = BossPulseTicks / 2;
        var phase = tick & (half - 1);
        return ((tick & half) == 0 ? half - 1 - phase : phase) / (float)half;
    }
}
