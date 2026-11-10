using Microsoft.Xna.Framework;

namespace PristonTale.Gameplay.UI.Bellatra;

// How a key's image is drawn: a screen image at its corner (clipped to its size), a texture centred on its point with
// its half size, rotation and alpha, or the effect's name in text.
public enum BellatraImageKind
{
    Screen = 0,
    Texture = 1,
    Text = 2
}

// One track of keys, at 70 frames a second. A key lasts Delay frames, moving from the previous key's offset, half size
// growth, angle (degrees) and alpha to its own; the first moves from nothing to its own with the first key's alpha. A
// key of Hold frames stays until the effect is stopped, one of Loop starts the track again. Image -1 draws nothing.
public sealed record BellatraTrack(BellatraImageKind Kind, BellatraKey[] Keys);

public readonly record struct BellatraKey(int Image, int Delay, Vector2 Offset, Vector2 Growth, float Angle, byte Alpha)
{
    public const int Loop = -1;
    public const int Hold = -2;
}

// A track of an effect: drawn at Origin with Size (a screen image's clip, a texture's half size before its growth), in
// front of the layers above it (0 the front).
public readonly record struct BellatraLayer(int Track, int Layer, Point Origin, Point Size);

// Bellatra's banners and screen changes (image/Bellatra/Bellatra.dat): 50 tracks and 40 effects by BellatraEffect.
public sealed class BellatraEffectTable
{
    public const string Path = "image/Bellatra/Bellatra.dat";

    private const int Tracks = 50;
    private const int TrackKeys = 40;
    private const int Effects = 40;
    private const int EffectLayers = 20;
    private const int LayerCount = 3;
    private const int TrackBytes = 8 + TrackKeys * (4 + 4 + 8 + 8 + 12 + 1);
    private const int EffectBytes = 8 + EffectLayers * (4 + 4 + 8 + 8);

    private readonly BellatraTrack[] _tracks;
    private readonly BellatraLayer[][] _effects;

    private BellatraEffectTable(BellatraTrack[] tracks, BellatraLayer[][] effects) => (_tracks, _effects) = (tracks, effects);

    public BellatraTrack Track(int index) => _tracks[index];

    // An effect's layers; none for an effect the table lacks or one naming a track out of the table.
    public IReadOnlyList<BellatraLayer> Layers(BellatraEffect effect) => (int)effect is >= 0 and < Effects ? _effects[(int)effect] : [];

    public static BellatraEffectTable Read(ReadOnlySpan<byte> data)
    {
        if (data.Length != Tracks * TrackBytes + Effects * EffectBytes)
        {
            throw new InvalidDataException("Bellatra's effect table has an unexpected size.");
        }

        var tracks = new BellatraTrack[Tracks];
        for (var index = 0; index < Tracks; index++)
        {
            tracks[index] = ReadTrack(data.Slice(index * TrackBytes, TrackBytes));
        }

        var effects = new BellatraLayer[Effects][];
        var start = Tracks * TrackBytes;
        for (var index = 0; index < Effects; index++)
        {
            effects[index] = ReadEffect(data.Slice(start + index * EffectBytes, EffectBytes), tracks);
        }

        return new BellatraEffectTable(tracks, effects);
    }

    private static BellatraTrack ReadTrack(ReadOnlySpan<byte> track)
    {
        var kind = (BellatraImageKind)Int(track, 0);
        var count = Int(track, 4);
        if (count is < 0 or > TrackKeys || !Enum.IsDefined(kind))
        {
            return new BellatraTrack(BellatraImageKind.Screen, []);
        }

        const int images = 8;
        const int delays = images + TrackKeys * 4;
        const int offsets = delays + TrackKeys * 4;
        const int growths = offsets + TrackKeys * 8;
        const int angles = growths + TrackKeys * 8;
        const int alphas = angles + TrackKeys * 12;
        var keys = new BellatraKey[count];
        for (var key = 0; key < count; key++)
        {
            keys[key] = new BellatraKey(Int(track, images + key * 4), Int(track, delays + key * 4),
                new Vector2(Int(track, offsets + key * 8), Int(track, offsets + key * 8 + 4)),
                new Vector2(Int(track, growths + key * 8), Int(track, growths + key * 8 + 4)), Int(track, angles + key * 12 + 8), track[alphas + key]);
        }

        return new BellatraTrack(kind, keys);
    }

    private static BellatraLayer[] ReadEffect(ReadOnlySpan<byte> effect, BellatraTrack[] tracks)
    {
        var count = Int(effect, 4);
        if (count is < 0 or > EffectLayers)
        {
            return [];
        }

        const int trackIndexes = 8;
        const int layers = trackIndexes + EffectLayers * 4;
        const int origins = layers + EffectLayers * 4;
        const int sizes = origins + EffectLayers * 8;
        var result = new BellatraLayer[count];
        for (var index = 0; index < count; index++)
        {
            var track = Int(effect, trackIndexes + index * 4);
            var layer = Int(effect, layers + index * 4);
            if (track is < 0 or >= Tracks || layer is < 0 or >= LayerCount || tracks[track].Keys.Length == 0)
            {
                return [];
            }

            result[index] = new BellatraLayer(track, layer, new Point(Int(effect, origins + index * 8), Int(effect, origins + index * 8 + 4)),
                new Point(Int(effect, sizes + index * 8), Int(effect, sizes + index * 8 + 4)));
        }

        return result;
    }

    private static int Int(ReadOnlySpan<byte> data, int offset) => System.Buffers.Binary.BinaryPrimitives.ReadInt32LittleEndian(data[offset..]);
}
