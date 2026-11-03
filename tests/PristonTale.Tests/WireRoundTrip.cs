using PristonTale.Application.World;
using PristonTale.Client.Net.Online;
using PristonTale.Client.Net.Presence;
using PristonTale.Client.Net.WorldCatalog;
using PristonTale.Protocol.Binary;
using PristonTale.Protocol.Packets;
using PristonTale.Protocol.Transport;
using PristonTale.Server.Net;

namespace PristonTale.Tests;

// Runs snapshots through the server's PresenceEncoder and the client's PresenceDecoder over original packet bytes,
// answering DescribeEntity the way ClientSession does.
internal sealed class WireRoundTrip
{
    private readonly PresenceEncoder _encoder;
    private readonly PresenceDecoder _decoder;
    private readonly CapturingSink _sink = new();
    private readonly Queue<ulong> _requests = new();

    public WireRoundTrip(ulong self, WorldDataHolder? data = null)
    {
        _encoder = new PresenceEncoder(self, data ?? new WorldDataHolder());
        Catalog = new WorldRenderCatalog();
        _decoder = new PresenceDecoder(Catalog, _requests.Enqueue) { SelfEntityId = self };
    }

    public WorldRenderCatalog Catalog { get; }

    public int BytesSent => _sink.Bytes;

    // The descriptions the client had to ask for.
    public int DescriptionRequests { get; private set; }

    // How many of the next descriptions the server pushes are lost on the way, as the rate limit drops replies.
    public int DropPushedDescriptions { get; set; }

    // The entries of the last snapshot pushed, as the wire carried them.
    public List<PresenceEntry> LastEntries { get; } = [];

    public List<PresenceUpdate> Push(in WorldSnapshot snapshot)
    {
        _encoder.Encode(snapshot, _sink);
        LastEntries.Clear();
        var updates = new List<PresenceUpdate>();
        while (_sink.Packets.Count > 0 || _requests.Count > 0)
        {
            while (_sink.Packets.TryDequeue(out var packet))
            {
                var code = BitConverter.ToUInt32(packet, 4);
                if (code == EtervaleOpcodes.EntityDescription && DropPushedDescriptions > 0 && _requests.Count == 0 && !_answering)
                {
                    DropPushedDescriptions--;
                    continue;
                }

                if (code == EtervaleOpcodes.PresenceEntries)
                {
                    PresenceEntriesReader.Read(packet, (in PresenceEntry entry) => LastEntries.Add(entry));
                }

                if (_decoder.TryHandle(packet, out var update) && update is not null)
                {
                    updates.Add(update);
                }
            }

            _answering = true;
            while (_requests.TryDequeue(out var entityId))
            {
                DescriptionRequests++;
                _encoder.SendRequestedDescription(entityId, _sink);
            }

            _answering = false;
        }

        return updates;
    }

    private bool _answering;

    // The client's decoder, for measuring it alone over packets taken with Encode.
    public PresenceDecoder Decoder => _decoder;

    // The packets the server sends for one snapshot, not yet decoded.
    public List<byte[]> Encode(in WorldSnapshot snapshot)
    {
        _encoder.Encode(snapshot, _sink);
        var packets = _sink.Packets.ToList();
        _sink.Packets.Clear();
        return packets;
    }

    // Merges one snapshot's updates into the single view the scene would observe.
    public PresenceUpdate PushMerged(in WorldSnapshot snapshot)
    {
        var updates = Push(snapshot);
        return new PresenceUpdate(
            snapshot.ServerTick,
            updates.SelectMany(update => update.Spawns).ToList(),
            updates.SelectMany(update => update.States).ToList(),
            updates.SelectMany(update => update.Removed).ToList(),
            updates.SelectMany(update => update.Events).ToList(),
            updates.Select(update => update.FieldId).LastOrDefault(field => field.HasValue),
            updates.Select(update => update.Owner).LastOrDefault(owner => owner is not null))
        {
            GroundAdded = updates.SelectMany(update => update.GroundAdded).ToList(),
            GroundRemoved = updates.SelectMany(update => update.GroundRemoved).ToList(),
            StatusEffects = updates.SelectMany(update => update.StatusEffects).ToList(),
            Warp = updates.Select(update => update.Warp).LastOrDefault(warp => warp is not null)
        };
    }

    private sealed class CapturingSink : IPacketSink
    {
        public Queue<byte[]> Packets { get; } = new();

        public int Bytes { get; private set; }

        public bool Send(ReadOnlySpan<byte> packet)
        {
            Assert.True(packet.Length <= FrameCodec.MaxFrameSize, "presence packet exceeds the 8192-byte frame");
            Packets.Enqueue(packet.ToArray());
            Bytes += packet.Length;
            return true;
        }
    }
}
