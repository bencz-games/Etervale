using System.Buffers.Binary;
using System.Net;
using System.Numerics;
using PristonTale.Application.Contacts;
using PristonTale.Application.Chat;
using PristonTale.Application.Services;
using PristonTale.Application.Sessions;
using PristonTale.Application.World;
using PristonTale.Protocol.Binary;
using PristonTale.Protocol.Packets;
using PristonTale.Protocol.Transport;
using PristonTale.Server.Hosting;
using PristonTale.Simulation.Characters;
using PristonTale.Simulation.Items;
using PristonTale.Simulation.Items.Usable;
using PristonTale.Simulation.Progression;
using PristonTale.Simulation.Quests;
using PristonTale.Simulation.Skills;
using PristonTale.Simulation.World;
using PristonTale.Simulation.World.Npcs;
using PristonTale.Contracts.Motion;
using PristonTale.Contracts.World;
using PristonTale.Contracts.Skills;
using PristonTale.Contracts.Sessions;
using PristonTale.Contracts.Units;

namespace PristonTale.Server.Net;

internal sealed record SessionServices(
    AuthAppService Auth,
    CharacterAppService Characters,
    SessionAppService Sessions,
    ISessionRegistry Registry,
    WorldServer World,
    WorldDataHolder WorldData,
    ILogger Logger,
    NetworkLimits Limits,
    LoginThrottle LoginThrottle,
    NpcAppService Npc,
    ItemUseAppService ItemUse,
    RestartAppService Restart,
    WingGateAppService WingGates,
    QuestAppService Quests,
    PersonalShopAppService PersonalShops,
    PartyAppService Parties,
    TradeAppService Trades,
    ContactAppService Contacts,
    ChatAppService Chat,
    GameServerControl Control)
{
    // Shared by every connection so a flood from many peers still yields one warning per interval.
    public LogSampler RejectedPackets { get; } = new(5000);
}

// One client connection: ServerHello, Login -> CharacterList + LoginResult, character commands, EnterWorld ->
// WorldEntry, JoinWorld -> WorldJoined, then play traffic.
internal sealed partial class ClientSession : IMovementVerdictSink
{
    private const long CorrectionIntervalMs = 300;
    internal const int PresenceBacklogBytes = 64 * 1024;
    // A backlog that stays above PresenceBacklogBytes this long means the client stopped reading.
    private static readonly TimeSpan SendStallTimeout = TimeSpan.FromSeconds(30);

    private readonly FrameConnection _connection;
    private readonly SessionServices _services;
    private readonly byte[] _scratch = new byte[FrameCodec.MaxFrameSize];
    private readonly object _sendGate = new();
    private string? _token;
    private PlayerSession? _session;
    private WorldPlayer? _player;
    private WorldOutbox? _outbox;
    private PresenceEncoder? _encoder;
    private Task? _pump;
    private long _lastCorrectionMs;
    private readonly PacketRateLimiter _packetRate;
    private readonly TokenBucket _playerInfoRate;
    private readonly TokenBucket _storedRate;
    private readonly SessionPacketQueue _packets = new();

    public ClientSession(FrameConnection connection, SessionServices services)
    {
        _connection = connection;
        _services = services;
        ConnectedMs = Environment.TickCount64;
        LastReceivedMs = ConnectedMs;
        _packetRate = new PacketRateLimiter(services.Limits.PacketLimit, services.Limits.PacketWindowMs);
        _playerInfoRate = new TokenBucket(services.Limits.PlayerInfoBurst, services.Limits.PlayerInfoPerSecond);
        _storedRate = new TokenBucket(services.Limits.StoredOperationBurst, services.Limits.StoredOperationsPerSecond);
        _countingSink = new CountingSink(this);
    }

    public long ConnectedMs { get; }

    public long LastReceivedMs { get; private set; }

    public bool IsAuthenticated => Volatile.Read(ref _token) is not null;

    public FrameConnection Connection => _connection;

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        var players = _services.World.PlayerCount;
        var hello = new ServerHelloPacket(ProtocolVersion.Current, ProtocolVersion.Minimum, _services.Control.Full(players) ? Math.Max(1, players) : 0,
            _services.Control.ServerName);
        SendLarge(hello.Write, ServerHelloPacket.MaxSize);
        var handling = HandleQueuedAsync();
        try
        {
            await _connection.RunAsync(ReceiveAsync, cancellationToken);
        }
        finally
        {
            _packets.Complete();
            try
            {
                await handling;
            }
            finally
            {
                await ClosePlayAsync();
                if (_token is not null)
                {
                    _services.Registry.Remove(_token);
                }

                _packets.Dispose();
            }
        }
    }

    // The receive loop only reads: it answers heartbeats at once and queues every other packet for HandleQueuedAsync, so
    // nothing a handler waits on (storage above all) stops the connection from being read or kept alive.
    private ValueTask ReceiveAsync(ReadOnlyMemory<byte> memory)
    {
        var now = Environment.TickCount64;
        LastReceivedMs = now;
        Interlocked.Increment(ref _packetsReceived);
        var code = BinaryPrimitives.ReadUInt32LittleEndian(memory.Span[4..]);
        // Description requests have their own bucket: one is sent per newly seen entity, so a crowd can exceed the
        // original's 28-entity updates; every other packet counts toward the flood limit.
        if (code != EtervaleOpcodes.DescribeEntity && !_packetRate.Admit(now))
        {
            LogRejected(code, "packet flood, closing");
            _connection.Close("packet flood");
        }
        else if (code == EtervaleOpcodes.Heartbeat)
        {
            AnswerHeartbeat(HeartbeatPacket.Read(memory.Span));
        }
        else if (!_packets.TryEnqueue(memory.Span, code == EtervaleOpcodes.MovementReport))
        {
            LogRejected(code, "command backlog, closing");
            _connection.Close("command backlog");
        }

        return ValueTask.CompletedTask;
    }

    private void AnswerHeartbeat(HeartbeatPacket heartbeat)
    {
        var beat = heartbeat with { TimeMs = Environment.TickCount64 };
        SendLarge(beat.Write, HeartbeatPacket.Size);
        if (_player is not null && _session is { } playing)
        {
            _services.Chat.Heartbeat(playing.EntityId);
        }
    }

    // Handles the queued packets one at a time, in order. A handler's unexpected failure closes the connection and is
    // rethrown once the session has closed its play, as when it escaped the receive loop.
    private async Task HandleQueuedAsync()
    {
        while (await _packets.DequeueAsync() is { } packet)
        {
            try
            {
                if (!_connection.IsClosed)
                {
                    await HandleAsync(packet.Memory);
                }
            }
            catch
            {
                _connection.Close("handler failed");
                throw;
            }
            finally
            {
                SessionPacketQueue.Return(packet);
            }
        }
    }

    private async ValueTask HandleAsync(ReadOnlyMemory<byte> memory)
    {
        var code = BinaryPrimitives.ReadUInt32LittleEndian(memory.Span[4..]);
        try
        {
            switch (code)
            {
                case EtervaleOpcodes.ChatSay:
                    await SayAsync(ChatSayPacket.Read(memory.Span));
                    break;
                case EtervaleOpcodes.Login:
                    await LoginAsync(LoginPacket.Read(memory.Span));
                    break;
                case EtervaleOpcodes.CharacterCommand:
                    await ApplyCharacterCommandAsync(CharacterCommandPacket.Read(memory.Span));
                    break;
                case EtervaleOpcodes.EnterWorld:
                    await EnterWorldAsync(EnterWorldPacket.Read(memory.Span));
                    break;
                case EtervaleOpcodes.JoinWorld:
                    await JoinWorldAsync(JoinWorldPacket.Read(memory.Span));
                    break;
                case EtervaleOpcodes.MovementReport:
                    ReportMovement(MovementReportPacket.Read(memory.Span));
                    break;
                case EtervaleOpcodes.AttackRequest:
                    await AttackAsync(AttackRequestPacket.Read(memory.Span));
                    break;
                case EtervaleOpcodes.YahooKey:
                    YahooKeyPacket.Read(memory.Span);
                    if (_player is not null && _session is { } cheering)
                    {
                        _ = _services.World.YahooAsync(cheering.EntityId, key: true);
                    }

                    break;
                case EtervaleOpcodes.DismissCrystalPets:
                    var dismissal = DismissCrystalPetsPacket.Read(memory.Span);
                    if (_player is not null && _session is { } owner)
                    {
                        _ = _services.World.DismissCrystalPetsAsync(owner.EntityId, dismissal.StatusCode);
                    }

                    break;
                case EtervaleOpcodes.DescribeEntity:
                    DescribeObject(DescribeEntityPacket.Read(memory.Span));
                    break;
                case EtervaleOpcodes.InventoryCommand:
                    await ApplyInventoryAsync(InventoryCommandPacket.Read(memory.Span));
                    break;
                case EtervaleOpcodes.ProgressionCommand:
                    await ApplyProgressionAsync(ProgressionCommandPacket.Read(memory.Span));
                    break;
                case EtervaleOpcodes.NpcServiceRequest:
                    await RequestNpcServiceAsync(NpcServiceRequestPacket.Read(memory.Span));
                    break;
                case EtervaleOpcodes.ItemUseCommand:
                    await UseItemAsync(ItemUseCommandPacket.Read(memory.Span));
                    break;
                case EtervaleOpcodes.RestartRequest:
                    await RestartAsync(RestartRequestPacket.Read(memory.Span));
                    break;
                case EtervaleOpcodes.WingGateRequest:
                    await TravelWingGateAsync(WingGateRequestPacket.Read(memory.Span));
                    break;
                case EtervaleOpcodes.QuestRequest:
                    await RequestQuestAsync(QuestRequestPacket.Read(memory.Span));
                    break;
                case EtervaleOpcodes.SkillCast:
                    await CastSkillAsync(SkillCastPacket.Read(memory.Span));
                    break;
                case EtervaleOpcodes.PersonalShopOpen:
                    await OpenPersonalShopAsync(PersonalShopOpenPacket.Read(memory.Span));
                    break;
                case EtervaleOpcodes.PersonalShopClose:
                    await ClosePersonalShopAsync(PersonalShopClosePacket.Read(memory.Span));
                    break;
                case EtervaleOpcodes.PersonalShopView:
                    await ViewPersonalShopAsync(PersonalShopViewPacket.Read(memory.Span));
                    break;
                case EtervaleOpcodes.PersonalShopBuy:
                    await BuyFromPersonalShopAsync(PersonalShopBuyPacket.Read(memory.Span));
                    break;
                case EtervaleOpcodes.PartyInvite:
                    await InviteToPartyAsync(PartyInvitePacket.Read(memory.Span));
                    break;
                case EtervaleOpcodes.PartyAnswer:
                    await AnswerPartyAsync(PartyAnswerPacket.Read(memory.Span));
                    break;
                case EtervaleOpcodes.PartyCommand:
                    await CommandPartyAsync(PartyCommandPacket.Read(memory.Span));
                    break;
                case EtervaleOpcodes.TradeRequest:
                    await RequestTradeAsync(TradeRequestPacket.Read(memory.Span));
                    break;
                case EtervaleOpcodes.TradeAnswer:
                    await AnswerTradeAsync(TradeAnswerPacket.Read(memory.Span));
                    break;
                case EtervaleOpcodes.TradeOffer:
                    await OfferTradeAsync(TradeOfferPacket.Read(memory.Span));
                    break;
                case EtervaleOpcodes.TradeLock or EtervaleOpcodes.TradeCancel:
                    await SignalTradeAsync(TradeSignalPacket.Read(memory.Span));
                    break;
                case EtervaleOpcodes.ContactChange:
                    await ChangeContactAsync(ContactChangePacket.Read(memory.Span));
                    break;
                case EtervaleOpcodes.ContactRefresh:
                    ContactRefreshPacket.Read(memory.Span);
                    RefreshContacts();
                    break;
                case EtervaleOpcodes.LeaveWorld:
                    SendLarge(new LeaveWorldPacket(Saved: await ClosePlayAsync()).Write, LeaveWorldPacket.Size);
                    break;
                default:
                    // A code without a handler is ignored, not answered.
                    break;
            }
        }
        catch (Exception ex) when (ex is InvalidSessionException or InvalidOperationException)
        {
            LogRejected(code, ex.Message);
        }
        catch (CommitInvariantException ex)
        {
            _services.Logger.LogError(ex, "Commit invariant broken for {Code:X8} from {Remote}; closing so the character reloads from storage",
                code, _connection.RemoteEndPoint);
            _connection.Close("state diverged");
        }
        catch (System.Data.Common.DbException ex)
        {
            _services.Logger.LogWarning("Storage failed for {Code:X8} from {Remote}: {Message}", code, _connection.RemoteEndPoint, ex.Message);
            ReplyStorageFailure(code, memory.Span);
        }
    }

    // Persistence errors answer the pending request instead of dropping the connection.
    private void ReplyStorageFailure(uint code, ReadOnlySpan<byte> packet)
    {
        switch (code)
        {
            case EtervaleOpcodes.Login:
                SendLoginResult(LoginFailure.Busy);
                break;
            case EtervaleOpcodes.CharacterCommand:
            {
                var command = CharacterCommandPacket.Read(packet);
                SendCharacterResult(command, ok: false, (int)CreateCharacterFailure.Rejected);
                break;
            }
            case EtervaleOpcodes.EnterWorld:
                SendEntryFailure(EnterWorldFailure.Rejected);
                break;
        }
    }

    private async Task LoginAsync(LoginPacket request)
    {
        if (_token is not null)
        {
            SendLoginResult(LoginFailure.AlreadyLoggedIn);
            return;
        }

        if (request.Protocol < ProtocolVersion.Minimum)
        {
            SendLoginResult(LoginFailure.OutdatedClient);
            return;
        }

        var address = NetworkLimits.AddressOf(_connection.RemoteEndPoint);
        var throttle = _services.LoginThrottle;
        if (throttle.IsBlocked(address, request.Account, Environment.TickCount64))
        {
            LogRejected(EtervaleOpcodes.Login, "login throttled");
            SendLoginResult(LoginFailure.Busy);
            return;
        }

        var outcome = await _services.Auth.LoginAsync(request.Account, request.Password);
        if (!outcome.Ok || !_services.Registry.TryGet(outcome.SessionToken, out var session))
        {
            if (outcome.Failure == LoginFailure.BadCredentials)
            {
                throttle.RecordFailure(address, request.Account, Environment.TickCount64);
            }

            SendLoginResult(outcome.Ok ? LoginFailure.Busy : outcome.Failure);
            return;
        }

        throttle.RecordSuccess(request.Account);
        if (session.GmLevel == 0 && _services.Control.Full(_services.World.PlayerCount))
        {
            // Only game masters may join a full server.
            _services.Registry.Remove(outcome.SessionToken);
            SendLoginResult(LoginFailure.Busy);
            return;
        }

        // The connection is logged in only once its list is read: a list storage refuses answers Busy and leaves the
        // connection free to log in again.
        try
        {
            await SendCharacterListAsync(outcome.SessionToken);
        }
        catch
        {
            _services.Registry.Remove(outcome.SessionToken);
            throw;
        }

        Volatile.Write(ref _token, outcome.SessionToken);
        _session = session;
        SendLoginResult(LoginFailure.None);
    }

    private void SendLoginResult(LoginFailure failure) =>
        SendLarge(new LoginResultPacket(failure).Write, LoginResultPacket.Size);

    private async Task SendCharacterListAsync(string token)
    {
        var characters = await _services.Characters.ListCharactersAsync(token);
        var list = new CharacterListPacket(characters.Take(CharacterListPacket.MaxCharacters).Select(WireMapping.Entry).ToArray());
        SendLarge(list.Write, CharacterListPacket.MaxSize);
    }

    // A successful creation or deletion sends the new character list before the result.
    private async Task ApplyCharacterCommandAsync(CharacterCommandPacket command)
    {
        var token = RequireToken();
        if (command.Action == CharacterAction.Create)
        {
            var created = await _services.Characters.CreateCharacterAsync(token, command.Name, command.Class, command.FaceIndex);
            if (created.Ok)
            {
                await SendCharacterListAsync(token);
            }

            SendCharacterResult(command, created.Ok, (int)created.Failure);
            return;
        }

        var deleted = await _services.Characters.DeleteCharacterAsync(token, command.Name);
        if (deleted.Ok)
        {
            await SendCharacterListAsync(token);
        }

        SendCharacterResult(command, deleted.Ok, 0);
    }

    private void SendCharacterResult(CharacterCommandPacket command, bool ok, int failure) =>
        SendLarge(new CharacterResultPacket(command.Action, ok, failure, command.Name).Write, CharacterResultPacket.MaxSize);

    private async Task EnterWorldAsync(EnterWorldPacket request)
    {
        var token = RequireToken();
        var outcome = await _services.Sessions.EnterWorldAsync(token, request.Name);
        if (!outcome.Ok || _session?.Character is null)
        {
            SendEntryFailure(outcome.Ok ? EnterWorldFailure.Rejected : outcome.Failure);
            return;
        }

        var entry = new WorldEntryPacket(0, outcome.EntityId, outcome.FieldId, outcome.SpawnX, outcome.SpawnZ);
        SendLarge(entry.Write, WorldEntryPacket.Size);
    }

    private void SendEntryFailure(EnterWorldFailure failure) =>
        SendLarge(new WorldEntryPacket((byte)failure, 0, 0, 0, 0).Write, WorldEntryPacket.Size);

    private async Task JoinWorldAsync(JoinWorldPacket request)
    {
        var session = _session;
        if (_player is not null || session?.Character is null || session.Loaded is null || request.EntityId != session.EntityId)
        {
            throw new InvalidOperationException("JoinWorld before a successful EnterWorld.");
        }

        // Read before joining, so the player is in the chat as soon as it is in the world.
        var contacts = await LoadContactsAsync(session);
        if (!session.TryBeginPlay())
        {
            throw new InvalidOperationException("JoinWorld before a successful EnterWorld.");
        }

        var player = WorldPlayerFactory.Create(session);
        var outbox = new WorldOutbox();
        bool joined;
        try
        {
            joined = await _services.World.JoinAsync(session.FieldId, player, outbox.Writer, session);
        }
        catch (WorldBusyException)
        {
            joined = false;
        }

        if (!joined)
        {
            session.EndPlay();
            throw new InvalidOperationException("World join rejected.");
        }

        _player = player;
        _outbox = outbox;
        _encoder = new PresenceEncoder(session.EntityId, _services.WorldData);
        SendLarge(new WorldJoinedPacket(session.EntityId, Environment.TickCount64, (byte)Math.Clamp(session.GmLevel, 0, 255),
            _services.World.GameClockMs).Write, WorldJoinedPacket.Size);
        _pump = PumpAsync(outbox, _encoder);
        JoinChat(session, contacts);
        RefreshContacts();
    }

    private async Task PumpAsync(WorldOutbox outbox, PresenceEncoder encoder)
    {
        try
        {
            // The outbox completes when the session leaves the world or closes; waiting without a token allocates nothing.
            while (await outbox.Reader.WaitToReadAsync())
            {
                // A client that reads slower than its world changes gets fewer snapshots, each joining what it missed,
                // instead of a backlog that grows until the connection closes.
                await _connection.WaitForBacklogAsync(PresenceBacklogBytes, SendStallTimeout);
                if (!outbox.Reader.TryRead(out var snapshot))
                {
                    continue;
                }

                try
                {
                    using (_connection.Batch())
                    {
                        encoder.Encode(snapshot, _countingSink);
                    }
                }
                finally
                {
                    snapshot.ReleaseFrame();
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            // A connection whose presence stopped would play blind: the client reconnects instead.
            _services.Logger.LogError(ex, "Presence failed for {Remote}", _connection.RemoteEndPoint);
            _connection.Close("presence failed");
        }
        finally
        {
            encoder.Close();
        }
    }

    private void ReportMovement(MovementReportPacket report)
    {
        var session = _session;
        var player = _player;
        var outbox = _outbox;
        if (session is null || player is null || outbox is null)
        {
            return;
        }

        var position = report.Position;
        if (report.Field >= 0 && report.Field != session.FieldId)
        {
            Task<bool> transition;
            try
            {
                transition = _services.World.MoveToFieldAsync(session.FieldId, report.Field, player, position, outbox.Writer);
            }
            catch (WorldBusyException)
            {
                Correct(force: true);
                return;
            }

            _ = transition.ContinueWith(task =>
            {
                if (!task.IsCompletedSuccessfully || !task.Result)
                {
                    Correct(force: true);
                }
            }, TaskScheduler.Default);
            return;
        }

        // A saturated queue or quota drops the report; the next one carries the newer position.
        _services.World.TryReportMovement(session.FieldId, session.EntityId, position, report.Facing, report.Moving,
            report.Motion == MotionState.Run, report.ActionTarget, this);
    }

    // On the tick thread: a rejection is answered from the thread pool, where the correction may wait for the encoder.
    void IMovementVerdictSink.OnMovementVerdict(MovementVerdict verdict)
    {
        if (verdict == MovementVerdict.Rejected)
        {
            ThreadPool.UnsafeQueueUserWorkItem(static session => session.Correct(force: false), this, preferLocal: false);
        }
    }

    // FieldWarp back to the authoritative field and position after a rejected report or field change.
    private void Correct(bool force)
    {
        var now = Environment.TickCount64;
        var encoder = _encoder;
        var session = _session;
        if (encoder?.SelfPosition is not { } position || session is null || !force && now - _lastCorrectionMs < CorrectionIntervalMs)
        {
            return;
        }

        _lastCorrectionMs = now;
        SendWarp(session.FieldId, position);
    }

    // A forced FieldWarp: the client puts its player on the floor at the new field and position.
    private void SendWarp(int fieldId, Vector3 position) =>
        SendLarge(new FieldWarpPacket(fieldId, position.X, position.Y, position.Z, Forced: true).Write, FieldWarpPacket.Size);

    private async Task AttackAsync(AttackRequestPacket attack)
    {
        var session = _session;
        if (session is null || _player is null || _encoder is null)
        {
            return;
        }

        var target = attack.Target;
        if (target != 0 && _encoder.KindOf(target) == EntityKind.Npc)
        {
            await InteractNpcAsync(session, target);
            return;
        }

        _ = _services.World.SetAttackTargetAsync(session.FieldId, session.EntityId, target, attack.Sustained, attack.Sequence);
    }

    // SkillCast is answered with SkillCastResult on this connection; the start, impact and statuses follow on the
    // presence timeline. The server ignores the client's rank, cost and timing and validates the cast in the tick.
    private async Task CastSkillAsync(SkillCastPacket packet)
    {
        var session = _session;
        if (session is null || _player is null)
        {
            return;
        }

        var hasPoint = packet.X != 0 || packet.Y != 0 || packet.Z != 0;
        var point = new Vector3(GameUnits.FromFixed(packet.X), GameUnits.FromFixed(packet.Y), GameUnits.FromFixed(packet.Z));
        var request = new SkillCastRequest(packet.Sequence, packet.SkillCode, packet.TargetId, point, hasPoint, packet.Command);
        SkillCastOutcome outcome;
        try
        {
            outcome = await _services.World.CastSkillAsync(session.FieldId, session.EntityId, request);
        }
        catch (WorldBusyException)
        {
            outcome = SkillCastOutcome.Refused(request, SkillCastVerdict.Busy);
        }

        if (!outcome.Accepted)
        {
            LogRejected(EtervaleOpcodes.SkillCast, $"skill {packet.SkillCode:X8}: {outcome.Verdict}");
        }

        var result = new SkillCastResultPacket(outcome.Sequence, outcome.SkillCode, outcome.Verdict, (byte)Math.Clamp(outcome.Rank, 0, 255),
            (uint)Math.Max(0, outcome.CooldownMs), (uint)Math.Max(0, outcome.RemainingMs), outcome.Mana, outcome.Stamina, 0);
        SendLarge(result.Write, SkillCastResultPacket.Size);
    }

    private async Task InteractNpcAsync(PlayerSession session, ulong npc)
    {
        var interaction = await _services.World.InteractNpcAsync(session, npc);
        var services = interaction.Services
            .Select(offer => new NpcServiceEntry((byte)offer.Kind, offer.Available, offer.Value, (ushort)Math.Clamp(offer.PricePercent, 0, ushort.MaxValue)))
            .ToArray();
        var dialog = new NpcDialogPacket(npc, interaction.Ok, interaction.Name, interaction.Message, services, interaction.Dialog);
        SendLarge(dialog.Write, FrameCodec.MaxFrameSize);
    }

    private async Task RequestNpcServiceAsync(NpcServiceRequestPacket request)
    {
        var session = _session;
        var command = new NpcServiceCommand(request.OperationId, request.NpcId, (NpcServiceKind)request.Service,
            (NpcServiceAction)request.Action,
            request.ExpectedRevision, request.ExpectedStorageRevision, request.ItemCode, request.ItemId, request.Anchor, request.Count, request.Amount,
            request.Materials, request.Covered);
        NpcServiceReply reply;
        try
        {
            AdmitStoredOperation();
            reply = session is null || _player is null
                ? new NpcServiceReply(NpcServiceOutcome.Failed(NpcServiceStatus.Rejected, "enter the world first"))
                : await _services.Npc.RequestAsync(RequireToken(), command);
        }
        catch (Exception ex) when (ex is InvalidSessionException or InvalidOperationException or System.Data.Common.DbException)
        {
            reply = new NpcServiceReply(ex is WorldBusyException
                ? NpcServiceOutcome.Failed(NpcServiceStatus.Busy, ex.Message)
                : NpcServiceOutcome.Failed(NpcServiceStatus.StorageFailed, ex.Message));
        }

        NpcWireMapping.SendState(request, reply, packet => SendLarge(packet, FrameCodec.MaxFrameSize));
        if (reply.Arrival is { } arrival)
        {
            SendWarp(arrival.FieldCode, arrival.Position);
        }
    }

    private async Task RequestQuestAsync(QuestRequestPacket request)
    {
        var session = _session;
        QuestOutcome outcome;
        try
        {
            AdmitStoredOperation();
            outcome = session is null || _player is null
                ? QuestOutcome.Failed(QuestStatus.Rejected, "enter the world first")
                : await _services.Quests.RequestAsync(RequireToken(), QuestWireMapping.ToCommand(request));
        }
        catch (Exception ex) when (ex is InvalidSessionException or InvalidOperationException)
        {
            outcome = QuestOutcome.Failed(ex is WorldBusyException ? QuestStatus.Busy : QuestStatus.StorageFailed, ex.Message);
        }

        var result = QuestWireMapping.ToResult(request.OperationId, outcome);
        SendLarge(result.Write, result.Capacity);
    }

    private void DescribeObject(DescribeEntityPacket request)
    {
        // A small request costs a description of up to a few hundred bytes, so requests beyond the bucket are dropped.
        if (!_playerInfoRate.TryTake(Environment.TickCount64))
        {
            LogRejected(EtervaleOpcodes.DescribeEntity, "description rate exceeded");
            return;
        }

        _encoder?.SendRequestedDescription(request.EntityId, _countingSink);
    }

    private async Task ApplyInventoryAsync(InventoryCommandPacket request)
    {
        var command = new InventoryCommand(request.OperationId, request.ExpectedRevision, (InventoryAction)request.Action, request.ItemId,
            request.Destination, request.Count, (EquipmentSlot)request.Slot);
        InventoryOutcome outcome;
        try
        {
            AdmitStoredOperation();
            outcome = await _services.Sessions.ApplyInventoryAsync(RequireToken(), command);
        }
        catch (Exception ex) when (ex is InvalidSessionException or InvalidOperationException or System.Data.Common.DbException)
        {
            outcome = new InventoryOutcome(false, request.ExpectedRevision, ex.Message);
        }

        var result = new OperationResultPacket(EtervaleOpcodes.InventoryResult, request.OperationId, outcome.Ok, outcome.Revision, outcome.Message);
        SendLarge(result.Write, OperationResultPacket.MaxSize);
    }

    // The item use answers with ItemUseResult; a use that moved the player to another field also sends a forced FieldWarp.
    private async Task UseItemAsync(ItemUseCommandPacket request)
    {
        var command = new ItemUseCommand(request.OperationId, request.ExpectedRevision, request.ItemId, request.Argument, request.Text);
        ItemUseReply reply;
        try
        {
            AdmitStoredOperation();
            reply = _session is null || _player is null
                ? new ItemUseReply(ItemUseOutcome.Refused(request.ExpectedRevision, ItemUseStatus.Rejected))
                : await _services.ItemUse.UseAsync(RequireToken(), command);
        }
        catch (WorldBusyException)
        {
            reply = new ItemUseReply(ItemUseOutcome.Refused(request.ExpectedRevision, ItemUseStatus.Busy));
        }
        catch (Exception ex) when (ex is InvalidSessionException or InvalidOperationException)
        {
            reply = new ItemUseReply(ItemUseOutcome.Refused(request.ExpectedRevision, ItemUseStatus.StorageFailed));
        }

        var outcome = reply.Outcome;
        var result = new ItemUseResultPacket(request.OperationId, outcome.Ok, outcome.Revision, (ushort)outcome.Status, outcome.ItemCode, outcome.Message);
        SendLarge(result.Write, ItemUseResultPacket.MaximumSize);
        if (reply.Arrival is { } arrival)
        {
            SendWarp(arrival.FieldCode, arrival.Position);
        }
    }

    // A wing gate trip answers with WingGateResult; a trip that warped sends a forced FieldWarp.
    private async Task TravelWingGateAsync(WingGateRequestPacket request)
    {
        var command = new WingGateCommand(request.OperationId, request.Destination);
        WingGateReply reply;
        try
        {
            AdmitStoredOperation();
            reply = _session is null || _player is null
                ? new WingGateReply(new WingGateOutcome(WingGateStatus.Rejected, command.Destination))
                : await _services.WingGates.TravelAsync(RequireToken(), command);
        }
        catch (WorldBusyException)
        {
            reply = new WingGateReply(new WingGateOutcome(WingGateStatus.Busy, command.Destination));
        }
        catch (Exception ex) when (ex is InvalidSessionException or InvalidOperationException)
        {
            reply = new WingGateReply(new WingGateOutcome(WingGateStatus.StorageFailed, command.Destination));
        }

        var outcome = reply.Outcome;
        var result = new WingGateResultPacket(request.OperationId, (byte)outcome.Status, request.Destination, outcome.Fee);
        SendLarge(result.Write, WingGateResultPacket.Size);
        if (reply.Arrival is { } arrival)
        {
            SendWarp(arrival.FieldCode, arrival.Position);
        }
    }

    // A dead player's restart answers with RestartResult; a restart that moved the player sends a forced FieldWarp.
    private async Task RestartAsync(RestartRequestPacket request)
    {
        var command = new RestartCommand(request.OperationId, (RestartChoice)request.Choice);
        RestartReply reply;
        try
        {
            AdmitStoredOperation();
            reply = _session is null || _player is null
                ? new RestartReply(new RestartOutcome(RestartStatus.Rejected, command.Choice))
                : await _services.Restart.RestartAsync(RequireToken(), command);
        }
        catch (WorldBusyException)
        {
            reply = new RestartReply(new RestartOutcome(RestartStatus.Busy, command.Choice));
        }
        catch (Exception ex) when (ex is InvalidSessionException or InvalidOperationException)
        {
            reply = new RestartReply(new RestartOutcome(RestartStatus.StorageFailed, command.Choice));
        }

        var outcome = reply.Outcome;
        var result = new RestartResultPacket(request.OperationId, (byte)outcome.Status, (byte)outcome.Choice, outcome.ScrollUsed, outcome.ExperienceLost,
            outcome.GoldLost);
        SendLarge(result.Write, RestartResultPacket.Size);
        if (outcome.Ok)
        {
            SendWarp(reply.FieldId, reply.Position);
        }
    }

    private async Task ApplyProgressionAsync(ProgressionCommandPacket request)
    {
        var command = new ProgressionCommand(request.OperationId, request.ExpectedRevision, (ProgressionAction)request.Action,
            (CharacterAttribute)request.Attribute, request.Amount, request.SkillCode, request.NpcId);
        ProgressionOutcome outcome;
        try
        {
            AdmitStoredOperation();
            outcome = await _services.Sessions.ApplyProgressionAsync(RequireToken(), command);
        }
        catch (Exception ex) when (ex is InvalidSessionException or InvalidOperationException)
        {
            outcome = new ProgressionOutcome(false, request.ExpectedRevision, ex.Message);
        }

        var result = new OperationResultPacket(EtervaleOpcodes.ProgressionResult, request.OperationId, outcome.Ok, outcome.Revision, outcome.Message);
        SendLarge(result.Write, OperationResultPacket.MaxSize);
    }

    // Whether the character was stored; false when nothing was playing.
    private async Task<bool> ClosePlayAsync()
    {
        var outbox = _outbox;
        var player = _player;
        var session = _session;
        var pump = _pump;
        if (outbox is null || player is null || session is null)
        {
            return false;
        }

        _outbox = null;
        _player = null;
        _pump = null;
        LeaveChat(session);
        outbox.Writer.TryComplete();
        var leave = await _services.World.LeaveAsync(session.FieldId, session.EntityId, outbox.Writer);
        _services.Chat.Directory.Deliver(leave.Notices);
        session.EndPlay();
        var saved = false;
        try
        {
            saved = leave.Save is { } left
                ? await _services.Sessions.SaveLeftCharacterAsync(_token!, left, CancellationToken.None)
                : await _services.Sessions.SaveCharacterAsync(_token!, CancellationToken.None);
        }
        catch (Exception ex)
        {
            _services.Logger.LogError(ex, "Could not save character on leave.");
        }

        if (pump is not null)
        {
            await pump;
        }

        return saved;
    }

    private void AdmitStoredOperation()
    {
        if (!_storedRate.TryTake(Environment.TickCount64))
        {
            throw new WorldBusyException("too many requests");
        }
    }

    private string RequireToken() => _token ?? throw new InvalidOperationException("Log in first.");

    private void LogRejected(uint code, string reason)
    {
        if (_services.RejectedPackets.TryEnter(Environment.TickCount64, out var suppressed))
        {
            _services.Logger.LogWarning("Rejected {Code:X8} from {Remote}: {Reason} ({Suppressed} similar rejections not logged)",
                code, _connection.RemoteEndPoint, reason, suppressed);
        }
    }

    internal delegate int PacketWrite(Span<byte> destination);

    private void SendLarge(PacketWrite write, int capacity)
    {
        lock (_sendGate)
        {
            if (capacity > _scratch.Length)
            {
                throw new ArgumentOutOfRangeException(nameof(capacity));
            }

            SendSpan(_scratch.AsSpan(0, write(_scratch)));
        }
    }

    private void SendSpan(ReadOnlySpan<byte> packet)
    {
        Interlocked.Increment(ref _packetsSent);
        _connection.Send(packet);
    }
}
