using System.Numerics;
using PristonTale.Application.Chat;
using PristonTale.Application.Sessions;
using PristonTale.Simulation.Social;
using PristonTale.Protocol.Binary;
using PristonTale.Protocol.Packets;
using PristonTale.Protocol.Transport;

namespace PristonTale.Server.Net;

// The chat side of a connection: typed lines go to the chat service, and the lines, warps and close requests of the chat
// and game-master commands come back through the link registered while the player is in the world.
internal sealed partial class ClientSession
{
    private readonly IPacketSink _countingSink;
    private long _packetsReceived;
    private long _packetsSent;
    private ChatLink? _chatLink;

    private async Task SayAsync(ChatSayPacket input)
    {
        var session = _session;
        if (session is null || _player is null)
        {
            return;
        }

        if (input.Party)
        {
            await _services.Chat.PartyAsync(session.EntityId, input.Text);
        }
        else
        {
            await _services.Chat.SayAsync(session.EntityId, input.Text);
        }
    }

    private void JoinChat(PlayerSession session, ContactBook contacts)
    {
        var link = new ChatLink(this);
        _chatLink = link;
        var name = session.CharacterName ?? session.Character?.Name ?? "";
        _services.Chat.Directory.Join(new ChatMember(session, name, link, Environment.TickCount64, session.Loaded?.Money ?? 0) { Contacts = contacts });
    }

    private async Task<ContactBook> LoadContactsAsync(PlayerSession session)
    {
        try
        {
            return await _services.Contacts.LoadAsync(session.Loaded!.Id);
        }
        catch (System.Data.Common.DbException ex)
        {
            _services.Logger.LogWarning("Could not load the contacts of {Name}: {Message}", session.CharacterName, ex.Message);
            return ContactBook.Unavailable();
        }
    }

    private void LeaveChat(PlayerSession session)
    {
        if (_chatLink is { } link)
        {
            _services.Chat.Directory.Leave(session.EntityId, link);
            _chatLink = null;
        }
    }

    private void SendChat(ChatLine line)
    {
        var packet = new ChatLinePacket(line.Kind, line.SpeakerId, line.Text, line.Line);
        SendLarge(packet.Write, ChatLinePacket.MaxSize);
    }

    private sealed class ChatLink(ClientSession owner) : IChatLink
    {
        public string RemoteAddress => NetworkLimits.AddressOf(owner._connection.RemoteEndPoint)?.ToString() ?? "";

        public long PacketsReceived => Interlocked.Read(ref owner._packetsReceived);

        public long PacketsSent => Interlocked.Read(ref owner._packetsSent);

        public void Deliver(ChatLine line) => owner.SendChat(line);

        public void Warp(int fieldId, Vector3 position) => owner.SendWarp(fieldId, position);

        public void RequestClose() => owner.SendLarge(CloseRequestPacket.Write, CloseRequestPacket.Size);

        public void Disconnect(string reason) => owner._connection.Close(reason);
    }

    // The presence packets count toward the packets this connection sent.
    private sealed class CountingSink(ClientSession owner) : IPacketSink
    {
        public bool Send(ReadOnlySpan<byte> packet)
        {
            Interlocked.Increment(ref owner._packetsSent);
            return owner._connection.Send(packet);
        }
    }
}
