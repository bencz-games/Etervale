using System.Globalization;
using System.Text;
using PristonTale.Effects.Vfx;

namespace PristonTale.Effects.Scripts;

public readonly record struct BillboardFrame(int Image, int DelayTicks, int Alpha, int Size, float AngleDegrees);

// What an animated billboard varies per frame besides its image and opacity. The original only interpolates sizes and
// angles when the animation lists exactly those channels; a file with sizes but no angles keeps a constant angle.
public enum BillboardChannels
{
    Opacity,
    Size,
    SizeAndAngle
}

// An original animated billboard: the animation file (Effect/AnimationData/<name>.ini, section [ANIMATION]) lists per
// frame an image of its image set, a hold time in 70 Hz ticks, an opacity (0..255) and optionally a size in world units
// and an angle in degrees; the image set (Effect/ImageData/<DataFile>, section [IMAGE]) names "<stem>.<ext>" and a
// count, and the images are "<stem>1.<ext>" .. "<stem><count>.<ext>". BlendType 0 colour, 1 alpha, 2 lamp, 3 shadow.
public sealed class BillboardSequence
{

    public BillboardSequence(string name, IReadOnlyList<string> images, IReadOnlyList<BillboardFrame> frames, int startAlpha, int blendType,
        BillboardChannels channels)
    {
        if (images.Count == 0 || frames.Count == 0)
        {
            throw new InvalidDataException($"Billboard animation {name} has no images or frames.");
        }

        foreach (var frame in frames)
        {
            if (frame.Image < 0 || frame.Image >= images.Count || frame.DelayTicks < 0)
            {
                throw new InvalidDataException($"Billboard animation {name} has a frame outside its image set.");
            }
        }

        Name = name;
        Images = images;
        Frames = frames;
        StartAlpha = startAlpha;
        BlendType = blendType;
        Channels = channels;
    }

    public string Name { get; }
    public IReadOnlyList<string> Images { get; }
    public IReadOnlyList<BillboardFrame> Frames { get; }
    public int StartAlpha { get; }
    public int BlendType { get; }

    // The authored blend codes.
    public const int ColourBlend = 0;
    public const int AlphaBlend = 1;
    public const int LampBlend = 2;
    public const int ShadowBlend = 3;
    public BillboardChannels Channels { get; }

    // Reads an animation by file name from Effect/AnimationData and its image set from Effect/ImageData; open returns a
    // stream for an asset-relative path.
    public static BillboardSequence Read(string animationName, Func<string, Stream> open)
    {
        var animation = ReadSection(open(Path.Combine("Effect", "AnimationData", animationName)), "ANIMATION");
        var dataFile = Required(animation, "DataFile", animationName);
        var imageSet = ReadSection(open(Path.Combine("Effect", "ImageData", dataFile)), "IMAGE");
        var source = Required(imageSet, "Name", dataFile).Replace("\\\\", "/").Replace('\\', '/');
        var count = int.Parse(Required(imageSet, "Count", dataFile), CultureInfo.InvariantCulture);
        if (count is < 1 or > 30)
        {
            throw new InvalidDataException($"Image set {dataFile} has {count} images.");
        }

        if (source.LastIndexOf('.') <= 0)
        {
            throw new InvalidDataException($"Image set {dataFile} names no extension.");
        }

        var images = new string[count];
        for (var index = 0; index < count; index++)
        {
            images[index] = VfxCatalog.FrameName(source, index + 1);
        }

        var imageNumbers = Numbers(Required(animation, "ImageNum", animationName));
        var delays = Numbers(Required(animation, "Delay", animationName));
        var alphas = Numbers(Required(animation, "BlendValue", animationName));
        var sizes = animation.TryGetValue("Size", out var sizeText) && sizeText.Length > 0 ? Numbers(sizeText) : null;
        var angles = sizes is not null && animation.TryGetValue("Angle", out var angleText) && angleText.Length > 0 ? Numbers(angleText) : null;
        var channels = angles is not null ? BillboardChannels.SizeAndAngle : sizes is not null ? BillboardChannels.Size : BillboardChannels.Opacity;
        var frames = new BillboardFrame[imageNumbers.Length];
        for (var index = 0; index < frames.Length; index++)
        {
            // A list shorter than the image list leaves the remaining entries at the original's fill value (257).
            frames[index] = new BillboardFrame(imageNumbers[index], At(delays, index, 257), At(alphas, index, 1) & 0xFF, At(sizes, index, 257),
                At(angles, index, 0));
        }

        var startAlpha = animation.TryGetValue("StartBlendValue", out var start) ? int.Parse(start, CultureInfo.InvariantCulture) : 0;
        var blend = animation.TryGetValue("BlendType", out var blendText) ? int.Parse(blendText, CultureInfo.InvariantCulture) : 0;
        return new BillboardSequence(animationName, images, frames, startAlpha, blend, channels);
    }

    private static int At(int[]? values, int index, int fallback) => values is not null && index < values.Length ? values[index] : fallback;

    private static string Required(Dictionary<string, string> values, string key, string file) =>
        values.TryGetValue(key, out var value) && value.Length > 0 ? value : throw new InvalidDataException($"{file} lacks {key}.");

    private static int[] Numbers(string text)
    {
        var parts = text.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        var values = new int[parts.Length];
        for (var index = 0; index < parts.Length; index++)
        {
            values[index] = int.Parse(parts[index], NumberStyles.Integer, CultureInfo.InvariantCulture);
        }

        return values;
    }

    private static Dictionary<string, string> ReadSection(Stream stream, string section)
    {
        using var reader = new StreamReader(stream, Encoding.Latin1);
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var active = false;
        while (reader.ReadLine() is { } raw)
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith(';') || line.StartsWith("//", StringComparison.Ordinal))
            {
                continue;
            }

            if (line.StartsWith('['))
            {
                active = line.Equals($"[{section}]", StringComparison.OrdinalIgnoreCase);
                continue;
            }

            var separator = line.IndexOf('=', StringComparison.Ordinal);
            if (active && separator > 0)
            {
                values.TryAdd(line[..separator].Trim(), line[(separator + 1)..].Trim().Trim('"'));
            }
        }

        return values;
    }
}

public enum BillboardPlayback
{
    // Every frame once, then the billboard ends.
    Once,
    // After the first frame, frames 1 .. count - 2 repeat until Exit; the last frame then plays once.
    Loop
}

// The tick-exact state of one playing billboard animation: at each frame change the opacity, size and angle steps are
// recomputed towards that frame's values over its hold time, and every tick adds them.
public struct BillboardPlayer
{
    public int Frame;
    public int TimeCount;
    public float Alpha;
    public float AlphaStep;
    public float Size;
    public float SizeStep;
    public float Angle;
    public float AngleStep;
    public bool Finished;
    public bool Exiting;

    // sized: the caller gave a constant size (only the frame changes move it); otherwise sizes start from zero.
    public static BillboardPlayer Start(BillboardSequence sequence, float initialSize, bool sized)
    {
        var first = sequence.Frames[0];
        var delay = Math.Max(1, first.DelayTicks);
        var player = new BillboardPlayer
        {
            Alpha = sequence.StartAlpha,
            Size = sized ? initialSize : 0f,
            AlphaStep = (first.Alpha - sequence.StartAlpha) / (float)delay
        };
        if (!sized && sequence.Channels != BillboardChannels.Opacity)
        {
            // The original divides the first size by the first hold time in integers.
            player.SizeStep = first.Size / delay;
        }

        if (sequence.Channels == BillboardChannels.SizeAndAngle)
        {
            player.AngleStep = first.AngleDegrees / delay;
        }

        return player;
    }

    public int Image(BillboardSequence sequence) => sequence.Frames[Math.Min(Frame, sequence.Frames.Count - 1)].Image;

    // The billboard's movement ended: jump to the last frame from the opacity of the one before and finish once.
    public void Exit(BillboardSequence sequence)
    {
        if (Exiting || Finished)
        {
            return;
        }

        var frames = sequence.Frames;
        Exiting = true;
        Frame = frames.Count - 1;
        TimeCount = 0;
        if (frames.Count > 1)
        {
            Alpha = frames[^2].Alpha;
        }

        AlphaStep = (frames[^1].Alpha - Alpha) / (float)Math.Max(1, frames[^1].DelayTicks);
    }

    public void Tick(BillboardSequence sequence, BillboardPlayback playback)
    {
        if (Finished)
        {
            return;
        }

        var frames = sequence.Frames;
        if (TimeCount >= frames[Frame].DelayTicks)
        {
            TimeCount = 0;
            Frame++;
            if (playback == BillboardPlayback.Once || Exiting)
            {
                if (Frame >= frames.Count)
                {
                    Frame = frames.Count - 1;
                    Finished = true;
                    return;
                }
            }
            else if (Frame >= frames.Count - 1)
            {
                Frame = Math.Min(1, frames.Count - 1);
            }

            var next = frames[Frame];
            var delay = Math.Max(1, next.DelayTicks);
            AlphaStep = (next.Alpha - Alpha) / delay;
            if (sequence.Channels != BillboardChannels.Opacity)
            {
                SizeStep = (next.Size - Size) / delay;
            }

            if (sequence.Channels == BillboardChannels.SizeAndAngle)
            {
                AngleStep = (next.AngleDegrees - Angle) / delay;
            }
        }

        Alpha += AlphaStep;
        if (sequence.Channels != BillboardChannels.Opacity)
        {
            Size += SizeStep;
        }

        if (sequence.Channels == BillboardChannels.SizeAndAngle)
        {
            Angle += AngleStep;
        }

        TimeCount++;
    }
}
