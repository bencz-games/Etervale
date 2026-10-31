using System.Buffers;

namespace PristonTale.Server.Net;

// The packets a session handles, in the order they arrived, apart from its receive loop: a handler waiting on storage
// (a purchase, a game master's account command, a slow or locked database) holds only the packets behind it, never the
// connection's reading or its heartbeats. A movement report queued right behind another one replaces it, as the newer
// report carries the newer position, so a held session catches up with one move instead of a burst that would fill its
// player's command quota. Capacity bounds what a session may leave waiting; past it the session is closed.
internal sealed class SessionPacketQueue : IDisposable
{
    public const int Capacity = 512;

    private readonly Queue<QueuedPacket> _packets = new();
    private readonly SemaphoreSlim _ready = new(0);
    private QueuedPacket? _lastMovement;
    private bool _completed;

    // False when the queue is complete or full.
    public bool TryEnqueue(ReadOnlySpan<byte> frame, bool movement)
    {
        lock (_packets)
        {
            if (_completed)
            {
                return false;
            }

            if (movement && _lastMovement is { } waiting && waiting.Buffer.Length >= frame.Length)
            {
                frame.CopyTo(waiting.Buffer);
                waiting.Length = frame.Length;
                return true;
            }

            if (_packets.Count >= Capacity)
            {
                return false;
            }

            var packet = new QueuedPacket(ArrayPool<byte>.Shared.Rent(frame.Length), frame.Length);
            frame.CopyTo(packet.Buffer);
            _packets.Enqueue(packet);
            _lastMovement = movement ? packet : null;
        }

        _ready.Release();
        return true;
    }

    // The next packet, or null once the queue is complete and empty. The caller gives it back with Return.
    public async ValueTask<QueuedPacket?> DequeueAsync()
    {
        await _ready.WaitAsync();
        lock (_packets)
        {
            if (!_packets.TryDequeue(out var packet))
            {
                return null;
            }

            if (ReferenceEquals(packet, _lastMovement))
            {
                _lastMovement = null;
            }

            return packet;
        }
    }

    public static void Return(QueuedPacket packet) => ArrayPool<byte>.Shared.Return(packet.Buffer);

    // No packet is taken after this; packets still queued are handed out, then DequeueAsync returns null.
    public void Complete()
    {
        lock (_packets)
        {
            if (_completed)
            {
                return;
            }

            _completed = true;
        }

        _ready.Release();
    }

    public void Dispose()
    {
        lock (_packets)
        {
            while (_packets.TryDequeue(out var packet))
            {
                Return(packet);
            }

            _lastMovement = null;
        }

        _ready.Dispose();
    }
}

internal sealed class QueuedPacket(byte[] buffer, int length)
{
    public byte[] Buffer { get; } = buffer;
    public int Length { get; set; } = length;
    public ReadOnlyMemory<byte> Memory => Buffer.AsMemory(0, Length);
}
