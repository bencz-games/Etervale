using System.Buffers.Binary;
using System.Text;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using PristonTale.Protocol.Binary;
using PristonTale.Protocol.Packets;
using PristonTale.Protocol.Transport;
using PristonTale.Contracts.Motion;
using PristonTale.Contracts.Characters;
using PristonTale.Contracts.Texts;

namespace PristonTale.Tests;

internal static class BinaryProtocolTests
{
    public static IEnumerable<(string, Func<Task>)> Cases()
    {
        yield return ("frame header handles partial reads and rejects invalid lengths", HeaderAsync);
        yield return ("malformed UTF-8 text and overlong or out-of-range variable-length integers are refused", MalformedAsync);
        yield return ("hello, login, character list and commands and heartbeats round-trip with UTF-8 text", AccountAsync);
        yield return ("movement reports, attack requests and field warps round-trip with 64-bit targets and sequences", PlayerActionsAsync);
        yield return ("world entry, join and leave messages round-trip with 64-bit ids and UTF-8 names", WorldEntryAsync);
        yield return ("TLS connections deliver frames in order, coalesce sends and check the server certificate", ConnectionAsync);
        yield return ("a connection counts its backlog until the stream takes it, and a batch of sends leaves in one write", BacklogAsync);
        yield return ("a connection whose backlog stops draining closes as stalled", StallAsync);
        yield return ("a server address takes a host, host:port or tcp:// form, the game port when none is given", ServerAddressAsync);
        yield return ("operation results carry their line as a game text, and more arguments than its field holds are refused", GameTextAsync);
    }

    private static Task MalformedAsync()
    {
        var buffer = new byte[64];
        var writer = new PacketWriter(buffer, EtervaleOpcodes.ChatSay);
        writer.UInt16(3);
        writer.Bytes([0xC3, 0x28, 0x41]);
        var packet = buffer.AsSpan(0, writer.Finish()).ToArray();
        Assert.True(Refused(() =>
        {
            var reader = new PacketReader(packet, packet.Length);
            Utf8Text.Read(ref reader, 64, "Chat line");
        }), "a malformed UTF-8 sequence was accepted");

        var written = new PacketWriter(buffer, EtervaleOpcodes.ChatSay);
        Utf8Text.Write(ref written, "Olá, mundo", 64);
        var text = buffer.AsSpan(0, written.Finish()).ToArray();
        var back = new PacketReader(text, text.Length);
        Assert.True(Utf8Text.Read(ref back, 64, "Chat line") == "Olá, mundo", "UTF-8 text did not round-trip");

        foreach (var bad in new byte[][] { [0x80, 0x00], [0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0x02] })
        {
            Assert.True(Refused(() =>
            {
                var offset = 0;
                VarInt.Read(bad, ref offset);
            }), $"variable-length integer {Convert.ToHexString(bad)} was accepted");
        }

        var maximum = new byte[10];
        var length = VarInt.Write(maximum, ulong.MaxValue);
        var at = 0;
        Assert.True(length == 10 && VarInt.Read(maximum, ref at) == ulong.MaxValue, "the largest value did not round-trip");
        Assert.True(VarInt.UnZigZag(VarInt.ZigZag(int.MinValue)) == int.MinValue, "the smallest zigzag value did not round-trip");
        Assert.Throws<InvalidDataException>(() => VarInt.UnZigZag((ulong)uint.MaxValue + 1), "a zigzag value wider than 32 bits was truncated");
        return Task.CompletedTask;
    }

    private static bool Refused(Action read)
    {
        try
        {
            read();
            return false;
        }
        catch (InvalidDataException)
        {
            return true;
        }
    }

    private static Task HeaderAsync()
    {
        Span<byte> packet = stackalloc byte[FrameCodec.HeaderSize];
        Assert.True(FrameCodec.ReadHeader(packet[..7], out _, out _) == FrameHeaderStatus.NeedMoreData, "partial TCP header was accepted");
        Assert.True(FrameCodec.WriteFrame(packet, 0x48470080, []) == 8, "empty command frame has the wrong length");
        Assert.True(FrameCodec.ReadHeader(packet, out var size, out var code) == FrameHeaderStatus.Complete
            && size == 8 && code == 0x48470080, "little-endian frame header changed");
        BinaryPrimitives.WriteInt32LittleEndian(packet, 8193);
        Assert.True(FrameCodec.ReadHeader(packet, out _, out _) == FrameHeaderStatus.Invalid, "oversized frame was accepted");
        BinaryPrimitives.WriteInt32LittleEndian(packet, 7);
        Assert.True(FrameCodec.ReadHeader(packet, out _, out _) == FrameHeaderStatus.Invalid, "undersized frame was accepted");
        return Task.CompletedTask;
    }

    private static Task AccountAsync()
    {
        var buffer = new byte[FrameCodec.MaxFrameSize];
        var hello = new ServerHelloPacket(ProtocolVersion.Current, ProtocolVersion.Minimum, 12);
        Assert.True(ServerHelloPacket.Read(buffer.AsSpan(0, hello.Write(buffer))) == hello, "server hello round trip changed fields");
        var login = new LoginPacket(ProtocolVersion.Current, "conta", "sênha✓", "pt-BR");
        Assert.True(LoginPacket.Read(buffer.AsSpan(0, login.Write(buffer))) == login, "login round trip changed the account, password or language");
        // A client before protocol 4 sends no language; its login still reads, so the server can tell it is outdated.
        var outdated = buffer.AsSpan(0, login.Write(buffer) - 2 - "pt-BR".Length).ToArray();
        System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(outdated, outdated.Length);
        var read3 = LoginPacket.Read(outdated);
        Assert.True(read3.Language == "" && read3.Password == "sênha✓", "a login without the language did not read");
        Assert.True(LoginResultPacket.Read(buffer.AsSpan(0, new LoginResultPacket(Contracts.Sessions.LoginFailure.Busy).Write(buffer))).Status == Contracts.Sessions.LoginFailure.Busy,
            "login result lost its status");

        var entry = new CharacterListEntry("Álvaro", PlayerClass.Knight, 42, 1, 2, 1, 0x01010300, 0x02010100, 0, true);
        var list = new CharacterListPacket([entry, entry with { Name = "Beta", InVillage = false, ClanName = "Leões" }]);
        var read = CharacterListPacket.Read(buffer.AsSpan(0, list.Write(buffer)));
        Assert.True(read.Characters.SequenceEqual(list.Characters), "character list round trip changed fields");
        Assert.True(CharacterListPacket.Read(buffer.AsSpan(0, new CharacterListPacket([]).Write(buffer))).Characters.Count == 0, "an empty list did not read back");

        var create = new CharacterCommandPacket(CharacterAction.Create, PlayerClass.Archer, 2, "Nova");
        Assert.True(CharacterCommandPacket.Read(buffer.AsSpan(0, create.Write(buffer))) == create, "character command round trip changed fields");
        var length = create.Write(buffer);
        buffer[10] = 9;
        Assert.Throws<InvalidDataException>(() => CharacterCommandPacket.Read(buffer.AsSpan(0, length).ToArray()), "a malformed CharacterCommandPacket was read");
        var result = new CharacterResultPacket(CharacterAction.Create, false, 4, "Nova");
        Assert.True(CharacterResultPacket.Read(buffer.AsSpan(0, result.Write(buffer))) == result, "character result round trip changed fields");
        var beat = new HeartbeatPacket(uint.MaxValue, long.MaxValue);
        Assert.True(HeartbeatPacket.Read(buffer.AsSpan(0, beat.Write(buffer))) == beat, "heartbeat round trip changed fields");
        return Task.CompletedTask;
    }

    private static Task PlayerActionsAsync()
    {
        var buffer = new byte[FrameCodec.MaxFrameSize];
        var report = new MovementReportPacket(MotionState.Run, 3, new System.Numerics.Vector3(1024.5f, -12.25f, -999_999f), MathF.PI / 2,
            0x1_0000_0002UL);
        Assert.True(report.Write(buffer) == MovementReportPacket.Size, "movement report size changed");
        var read = MovementReportPacket.Read(buffer.AsSpan(0, MovementReportPacket.Size));
        Assert.True(read with { Facing = report.Facing } == report && MathF.Abs(read.Facing - report.Facing) < 1e-4f && read.Moving,
            $"movement report read back as {read}");
        buffer[10] = (byte)MotionState.Attack;
        Assert.Throws<InvalidDataException>(() => MovementReportPacket.Read(buffer.AsSpan(0, MovementReportPacket.Size).ToArray()), "a malformed MovementReportPacket was read");

        var attack = new AttackRequestPacket(0x1_0000_0003UL, true, ulong.MaxValue - 1);
        Assert.True(AttackRequestPacket.Read(buffer.AsSpan(0, attack.Write(buffer))) == attack, "attack request round trip changed fields");
        buffer[10] = 2;
        Assert.Throws<InvalidDataException>(() => AttackRequestPacket.Read(buffer.AsSpan(0, AttackRequestPacket.Size).ToArray()), "a malformed AttackRequestPacket was read");

        var warp = new FieldWarpPacket(21, 512.5f, 300.75f, -256.25f, true);
        Assert.True(FieldWarpPacket.Read(buffer.AsSpan(0, warp.Write(buffer))) == warp, "field warp round trip changed fields");
        return Task.CompletedTask;
    }

    private static Task WorldEntryAsync()
    {
        var buffer = new byte[FrameCodec.MaxFrameSize];
        var enter = new EnterWorldPacket("Hélio");
        Assert.True(EnterWorldPacket.Read(buffer.AsSpan(0, enter.Write(buffer))) == enter, "world entry request round trip changed the name");
        var entry = new WorldEntryPacket(0, 0x1_0000_0005UL, 3, 1000.5f, -1000.25f);
        var read = WorldEntryPacket.Read(buffer.AsSpan(0, entry.Write(buffer)));
        Assert.True(read == entry && read.Entered, "world entry round trip changed fields");
        Assert.True(!WorldEntryPacket.Read(buffer.AsSpan(0, new WorldEntryPacket(5, 0, 0, 0, 0).Write(buffer))).Entered, "a refused entry read as entered");
        Assert.True(JoinWorldPacket.Read(buffer.AsSpan(0, new JoinWorldPacket(ulong.MaxValue).Write(buffer))).EntityId == ulong.MaxValue,
            "world join lost its 64-bit id");
        var joined = new WorldJoinedPacket(7, long.MaxValue, 3, 1_793_000_000_123);
        Assert.True(WorldJoinedPacket.Read(buffer.AsSpan(0, joined.Write(buffer))) == joined, "world joined round trip changed fields");
        Assert.True(LeaveWorldPacket.Read(buffer.AsSpan(0, new LeaveWorldPacket(true).Write(buffer))).Saved, "leave answer lost its saved flag");
        return Task.CompletedTask;
    }

    private static async Task ConnectionAsync()
    {
        using var certificate = X509CertificateLoader.LoadPkcs12(Server.Net.ServerCertificate.CreateSelfSignedPfx(), null);
        var context = SslStreamCertificateContext.Create(certificate, additionalCertificates: null);
        using var listener = new Socket(SocketType.Stream, ProtocolType.Tcp);
        listener.Bind(new IPEndPoint(IPAddress.Loopback, 0));
        listener.Listen(4);
        var port = ((IPEndPoint)listener.LocalEndPoint!).Port;
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));

        var accept = AcceptAsync();
        await using var sender = await TlsTransport.ConnectAsync("127.0.0.1", port, new ServerCertificatePolicy("127.0.0.1"), timeout.Token);
        await using var server = await accept;
        var received = new List<byte[]>();
        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var serverRun = server.RunAsync(packet =>
        {
            received.Add(packet.ToArray());
            if (received.Count == 101)
            {
                done.TrySetResult();
            }

            return ValueTask.CompletedTask;
        }, timeout.Token);
        var senderRun = sender.RunAsync(_ => ValueTask.CompletedTask, timeout.Token);
        var buffer = new byte[HeartbeatPacket.Size];
        var beat = new HeartbeatPacket(1, 0x0102_0304_0506);
        beat.Write(buffer);
        Assert.True(sender.Send(buffer), "send was refused");
        for (var index = 0; index < 100; index++)
        {
            new HeartbeatPacket((uint)index, 0).Write(buffer);
            sender.Send(buffer);
        }

        await done.Task.WaitAsync(timeout.Token);
        Assert.True(HeartbeatPacket.Read(received[0]) == beat, "frame changed in transit");
        Assert.True(received.Skip(1).Select(packet => (int)HeartbeatPacket.Read(packet).Counter).SequenceEqual(Enumerable.Range(0, 100)), "frames reordered");
        Assert.True(sender.BytesSent == 101 * 24, "frame byte count changed");
        sender.Close("test done");
        await Task.WhenAll(serverRun, senderRun);

        // A pinned certificate is accepted, another pin is refused even on this machine.
        var pin = SHA256.HashData(certificate.RawData);
        var pinnedAccept = AcceptAsync();
        await using (var pinned = await TlsTransport.ConnectAsync("localhost", port, new ServerCertificatePolicy("localhost", pin), timeout.Token))
        {
            await using var pinnedServer = await pinnedAccept;
        }

        var wrong = (byte[])pin.Clone();
        wrong[0] ^= 1;
        var refusedAccept = AcceptAsync();
        var refused = false;
        try
        {
            await using var _ = await TlsTransport.ConnectAsync("localhost", port, new ServerCertificatePolicy("localhost", wrong), timeout.Token);
        }
        catch (AuthenticationException)
        {
            refused = true;
        }

        Assert.True(refused, "a certificate with another SHA-256 than the pin was accepted");
        try
        {
            await using var _ = await refusedAccept;
        }
        catch (Exception ex) when (ex is AuthenticationException or IOException)
        {
        }

        var remote = ServerCertificatePolicy.FromAddress(new Uri("tcp://play.example.com:10009"));
        Assert.True(!remote.Accepts(certificate, SslPolicyErrors.RemoteCertificateChainErrors), "an untrusted certificate of a remote host was accepted");
        Assert.True(remote.Accepts(certificate, SslPolicyErrors.None), "a trusted certificate of a remote host was refused");
        var address = new Uri($"tcp://play.example.com:10009?cert={Convert.ToHexString(pin)}");
        Assert.True(ServerCertificatePolicy.FromAddress(address).Accepts(certificate, SslPolicyErrors.RemoteCertificateChainErrors),
            "the cert pin in the address was ignored");

        async Task<FrameConnection> AcceptAsync() =>
            await TlsTransport.AcceptAsync(await listener.AcceptAsync(timeout.Token), context, TlsTransport.DefaultHandshakeTimeout, timeout.Token);
    }

    private static Task GameTextAsync()
    {
        var buffer = new byte[OperationResultPacket.MaxSize + 64];
        var named = new OperationResultPacket(EtervaleOpcodes.InventoryResult, Guid.NewGuid(), false, 12, GameText.Of(GameTextId.ItemsReceived, "Ração", "3"));
        Assert.True(OperationResultPacket.Read(buffer.AsSpan(0, named.Write(buffer))) == named, "an operation result with arguments changed");
        var none = named with { Ok = true, Message = GameText.None };
        var size = none.Write(buffer);
        Assert.True(size == OperationResultPacket.MinSize && OperationResultPacket.Read(buffer.AsSpan(0, size)).Message.IsNone, "an operation result without a line changed");
        var full = named with { Message = GameText.Of(GameTextId.RequestRefused, "a", "b", "c", "d") };
        size = full.Write(buffer);
        BinaryPrimitives.WriteUInt16LittleEndian(buffer.AsSpan(8 + 16 + 4 + 8 + 2), GameTextWire.MaxArgs + 1);
        Assert.Throws<InvalidDataException>(() => OperationResultPacket.Read(buffer.AsSpan(0, size).ToArray()), "a game text past its argument limit was read");
        return Task.CompletedTask;
    }

    private static Task ServerAddressAsync()
    {
        var host = TlsTransport.ParseAddress(" play.example.com ");
        Assert.True(host.Host == "play.example.com" && host.Port == TlsTransport.DefaultPort, $"a bare host became {host}");
        var ip = TlsTransport.ParseAddress("203.0.113.7:10037");
        Assert.True(ip.Host == "203.0.113.7" && ip.Port == 10037, $"an IP with its port became {ip}");
        var local = TlsTransport.ParseAddress(TlsTransport.LocalAddress);
        Assert.True(local.Host == "localhost" && local.Port == TlsTransport.DefaultPort, $"the local address became {local}");
        var pin = new string('a', SHA256.HashSizeInBytes * 2);
        var pinned = TlsTransport.ParseAddress($"[::1]?cert={pin}");
        Assert.True(pinned.Port == TlsTransport.DefaultPort && pinned.Query == $"?cert={pin}", $"an IPv6 address with a pin became {pinned}");
        Assert.True(TlsTransport.ParseAddress(pinned.AbsoluteUri) == pinned, "a parsed address did not parse to itself");
        foreach (var invalid in new[] { "", "http://play.example.com", "play.example.com:0", "play.example.com:99999", "play.example.com?cert=zz", $"host?cert={pin}00" })
        {
            Assert.Throws<ArgumentException>(() => TlsTransport.ParseAddress(invalid), $"\"{invalid}\" was accepted as a server address");
        }

        return Task.CompletedTask;
    }

    private static async Task BacklogAsync()
    {
        using var socket = new Socket(SocketType.Stream, ProtocolType.Tcp);
        var stream = new GatedStream();
        await using var connection = new FrameConnection(socket, stream);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var run = connection.RunAsync(_ => ValueTask.CompletedTask, timeout.Token);
        var buffer = new byte[HeartbeatPacket.Size];
        new HeartbeatPacket(1, 0).Write(buffer);
        using (connection.Batch())
        {
            connection.Send(buffer);
            connection.Send(buffer);
            await Task.Delay(50);
            Assert.True(stream.Writes.IsEmpty, "a send inside a batch woke the writer");
        }

        await stream.Started.Task.WaitAsync(timeout.Token);
        connection.Send(buffer);
        Assert.True(connection.WaitForBacklogAsync(3 * HeartbeatPacket.Size, Timeout.InfiniteTimeSpan).IsCompleted, "the backlog counted bytes never sent");
        var drained = connection.WaitForBacklogAsync(HeartbeatPacket.Size, Timeout.InfiniteTimeSpan);
        Assert.True(!drained.IsCompleted, "the bytes on their way to the stream were not counted");
        stream.Open();
        await drained.WaitAsync(timeout.Token);
        Assert.True(stream.Writes.First() == 2 * HeartbeatPacket.Size, "the batch did not leave in one write");
        connection.Close("test done");
        await run;
    }

    private static async Task StallAsync()
    {
        using var socket = new Socket(SocketType.Stream, ProtocolType.Tcp);
        await using var connection = new FrameConnection(socket, new GatedStream());
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var run = connection.RunAsync(_ => ValueTask.CompletedTask, timeout.Token);
        var buffer = new byte[HeartbeatPacket.Size];
        new HeartbeatPacket(1, 0).Write(buffer);
        connection.Send(buffer);
        await connection.WaitForBacklogAsync(0, TimeSpan.FromMilliseconds(100)).WaitAsync(timeout.Token);
        Assert.True(connection is { IsClosed: true, CloseReason: "send stalled" }, $"a peer that stopped reading kept its connection: {connection.CloseReason}");
        await run;
    }

    // A stream whose writes wait until it is opened, recording each write's length.
    private sealed class GatedStream : Stream
    {
        private readonly TaskCompletionSource _open = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public System.Collections.Concurrent.ConcurrentQueue<int> Writes { get; } = new();
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public void Open() => _open.TrySetResult();

        public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        {
            Writes.Enqueue(buffer.Length);
            Started.TrySetResult();
            await _open.Task.WaitAsync(cancellationToken);
        }

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            await Task.Delay(Timeout.Infinite, cancellationToken);
            return 0;
        }

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
