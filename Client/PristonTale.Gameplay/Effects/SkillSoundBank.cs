using System.Collections.Concurrent;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Audio;
using PristonTale.Engine3D.Diagnostics;
using PristonTale.Engine3D.IO;

namespace PristonTale.Gameplay.Effects;

// Skill and effect sounds by asset path. Files are read and decoded on a worker; the sound objects are created on the
// game thread in Update, so playing a sound never waits for disk. A sound that is not ready yet is skipped.
public sealed class SkillSoundBank : IDisposable
{
    private readonly string _assetRoot;
    private readonly IClientLog _log;
    private readonly Dictionary<string, SoundEffect> _ready = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentQueue<(string Path, byte[] Wave)> _decoded = new();
    private readonly HashSet<string> _requested = new(StringComparer.OrdinalIgnoreCase);

    public SkillSoundBank(string assetRoot, IClientLog log)
    {
        _assetRoot = assetRoot;
        _log = log;
    }

    public bool Enabled { get; set; } = true;

    public int ReadyCount => _ready.Count;

    // A sound asked to play, ready or not (diagnostics and cases).
    public event Action<string> Requested;

    public void Preload(IEnumerable<string> paths)
    {
        var batch = paths.Where(path => !string.IsNullOrWhiteSpace(path) && _requested.Add(path)).ToArray();
        if (batch.Length == 0)
        {
            return;
        }

        _ = Task.Run(() =>
        {
            foreach (var path in batch)
            {
                try
                {
                    using var stream = PristonWaveDecoder.OpenRead(Path.Combine(_assetRoot, path));
                    using var copy = new MemoryStream();
                    stream.CopyTo(copy);
                    _decoded.Enqueue((path, copy.ToArray()));
                }
                catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException)
                {
                    _log.Warn($"[Audio] skill sound {path} unavailable: {ex.Message}");
                }
            }
        });
    }

    public void Update()
    {
        while (_decoded.TryDequeue(out var entry))
        {
            using var stream = new MemoryStream(entry.Wave, writable: false);
            _ready[entry.Path] = SoundEffect.FromStream(stream);
        }
    }

    // Plays at the original proximity falloff: full volume within about 33 units, silent beyond about 663. Pitch is in
    // octaves (-1 to 1).
    public void Play(string path, Vector3 source, Vector3 listener, float volume = 1f, float pitch = 0f)
    {
        Requested?.Invoke(path);
        if (!Enabled || string.IsNullOrEmpty(path) || !_ready.TryGetValue(path, out var sound))
        {
            return;
        }

        var level = Audio.SoundFalloff.Volume(source, listener) * Math.Clamp(volume, 0f, 1f);
        if (level > 0.01f)
        {
            sound.Play(level, Math.Clamp(pitch, -1f, 1f), 0f);
        }
    }

    public void Dispose()
    {
        foreach (var sound in _ready.Values)
        {
            sound.Dispose();
        }

        _ready.Clear();
    }
}
