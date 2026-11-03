namespace PristonTale.Simulation.Items;

// How a held weapon or shield glows: the colour offsets and pulse of its glow, the scale at the pulse's peak, the
// item mix texture that scrolls over it (-1 none) with its scroll form, and the class-up light a weapon's effect
// setting gives it. A unique's file sets the glow; an item aged to +4..+20 takes its level's colour instead. Colours
// that belong to the aging or mixing tables bring their mix texture with them, whatever gave the colour.
public readonly record struct HeldItemGlow(short R, short G, short B, short A, int Blink, float Scale, int Mix, int MixScroll, bool ClassUp)
{
    public static readonly HeldItemGlow None = new(0, 0, 0, 0, 0, 0f, -1, 0, false);

    public const int FirstGlowingAge = 4;
    public const int LastGlowingAge = 20;
    public const int AgedBlink = 9;

    // The mix textures by index: the five small and the five large item mixes.
    public static readonly string[] MixTextures =
    [
        "image/sinImage/Items/DropItem/mixS_01.bmp", "image/sinImage/Items/DropItem/mixS_02.bmp", "image/sinImage/Items/DropItem/mixS_03.bmp",
        "image/sinImage/Items/DropItem/mixS_04.bmp", "image/sinImage/Items/DropItem/mixS_05.bmp", "image/sinImage/Items/DropItem/mixM_01.bmp",
        "image/sinImage/Items/DropItem/mixM_02.bmp", "image/sinImage/Items/DropItem/mixM_03.bmp", "image/sinImage/Items/DropItem/mixM_04.bmp",
        "image/sinImage/Items/DropItem/mixM_05.bmp"
    ];

    // Aged +4 to +20: the colour, the mix texture (-1 none) and its scroll form.
    private static readonly (short R, short G, short B, short A, int Mix, int Scroll)[] AgedColors =
    [
        (100, 40, 90, 0, -1, 0),
        (120, 100, 10, 0, -1, 0),
        (0, 110, 30, 0, -1, 0),
        (0, 50, 140, 0, 0, 9),
        (100, 0, 90, 0, 1, 10),
        (150, 60, 0, 0, 2, 10),
        (150, 10, 0, 0, 3, 12),
        (20, 220, 190, 0, 4, 12),
        (10, 220, 30, 0, 4, 12),
        (170, 40, 170, 0, 4, 13),
        (250, 30, 160, 0, 4, 13),
        (30, 190, 255, 0, 4, 13),
        (250, 130, 30, 0, 4, 13),
        (120, 30, 30, 0, 5, 14),
        (130, 0, 255, 0, 5, 14),
        (220, 240, 70, 0, 5, 14),
        (240, 240, 240, 0, 5, 14)
    ];

    // The mixing colours (classic and the five later mixes) with their mix texture and scroll form.
    private static readonly (short R, short G, short B, short A, int Mix, int Scroll)[] MixedColors =
    [
        (13, 0, 5, 0, 9, 8),
        (13, 0, 6, 0, 5, 8),
        (13, 0, 7, 0, 6, 9),
        (13, 0, 8, 0, 7, 9),
        (13, 0, 9, 0, 8, 9),
        (13, 0, 10, 0, 9, 9)
    ];

    public bool Glows => Blink != 0;
    public bool IsNone => !Glows && !ClassUp;
    public string MixTexture => Mix >= 0 && Mix < MixTextures.Length ? MixTextures[Mix] : "";

    public static HeldItemGlow For(ItemInstance item)
    {
        if (item is null)
        {
            return None;
        }

        var glow = item.Definition.Glow;
        var held = new HeldItemGlow(glow.R, glow.G, glow.B, glow.A, glow.Blink, glow.Scale, -1, 0, item.Definition.DisplayEffect != 0);
        if (item.UpgradeLevel is >= FirstGlowingAge and <= LastGlowingAge)
        {
            var aged = AgedColors[item.UpgradeLevel - FirstGlowingAge];
            held = held with { R = aged.R, G = aged.G, B = aged.B, A = aged.A, Blink = AgedBlink };
        }

        // A mix whose highest sheltom is Transparo (6) or above, up to Mirage (10), glows in its colour; the first colour is a
        // classic mix's, which no mix here makes.
        if (item.MixSheltom is >= 6 and <= 10)
        {
            var mixed = MixedColors[item.MixSheltom - 5];
            held = held with { R = mixed.R, G = mixed.G, B = mixed.B, A = mixed.A, Blink = AgedBlink };
        }

        if (!held.Glows)
        {
            return held with { R = 0, G = 0, B = 0, A = 0, Scale = 0f };
        }

        foreach (var table in (ReadOnlySpan<(short R, short G, short B, short A, int Mix, int Scroll)[]>)[AgedColors, MixedColors])
        {
            foreach (var entry in table)
            {
                if (entry.R == held.R && entry.G == held.G && entry.B == held.B && entry.A == held.A)
                {
                    return held with { Mix = entry.Mix, MixScroll = entry.Scroll };
                }
            }
        }

        return held;
    }

    // The glow at a clock in milliseconds: the light it adds (1 = a full white light), the opacity and the scale.
    public HeldGlowSample Sample(long nowMs)
    {
        if (!Glows)
        {
            return HeldGlowSample.Plain;
        }

        if (Blink < 0)
        {
            return new HeldGlowSample(R / 255f, G / 255f, B / 255f, Math.Clamp((255f + A) / 255f, 0f, 1f), 1f);
        }

        var period = 1L << Blink;
        var mask = period - 1;
        var step = nowMs & mask;
        if ((nowMs & period) == 0)
        {
            step = mask - step;
        }

        var alpha = A < 0 ? (step * A) >> Blink : -A;
        var level = Math.Max(1, (int)((step << 8) >> Blink));
        var scale = Scale == 0f ? 1f : Scale < 0f ? -Scale : level * Scale / 256f;
        return new HeldGlowSample(((step * R) >> Blink) / 255f, ((step * G) >> Blink) / 255f, ((step * B) >> Blink) / 255f,
            Math.Clamp((255f + alpha) / 255f, 0f, 1f), scale);
    }
}

public readonly record struct HeldGlowSample(float R, float G, float B, float Opacity, float Scale)
{
    public static readonly HeldGlowSample Plain = new(0f, 0f, 0f, 1f, 1f);
}
