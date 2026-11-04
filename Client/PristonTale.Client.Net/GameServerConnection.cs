using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Net.Sockets;
using System.Numerics;
using System.Threading.Channels;
using PristonTale.Client.Net.Online;
using PristonTale.Client.Net.Presence;
using PristonTale.Client.Net.WorldCatalog;
using PristonTale.Protocol.Binary;
using PristonTale.Protocol.Packets;
using PristonTale.Protocol.Transport;
using PristonTale.Simulation.Characters;
using PristonTale.Simulation.Items;
using PristonTale.Simulation.Items.Usable;
using PristonTale.Simulation.Progression;
using PristonTale.Simulation.Quests;
using PristonTale.Simulation.World;
using PristonTale.Simulation.World.Npcs;
using PristonTale.Contracts.Motion;
using PristonTale.Contracts.Skills;
using PristonTale.Contracts.Sessions;
using PristonTale.Contracts.World;
using PristonTale.Contracts.Units;
using PristonTale.Contracts.Characters;

namespace PristonTale.Client.Net;

// Client side of Etervale's protocol over one TCP connection. Login, character and world-entry requests run one at a
// time and complete on their reply opcodes; inventory and progression commands and NPC service requests correlate by
// operation id and NPC clicks by NPC entityId.
public sealed class GameServerConnection : IGameBackend
{
    private static readonly TimeSpan HeartbeatInterval = TimeSpan.FromSeconds(4);
    private static readonly TimeSpan DescriptionRetryInterval = TimeSpan.FromMilliseconds(Online.PresenceDecoder.DescriptionRetryMs);

    private readonly string _host;
    private readonly int _port;
    private readonly ServerCertificatePolicy _certificatePolicy;
    private readonly SemaphoreSlim _connectGate = new(1, 1);
    private readonly SemaphoreSlim _requestGate = new(1, 1);
    private readonly object _sendGate = new();
    private readonly byte[] _scratch = new byte[FrameCodec.MaxFrameSize];
    private readonly CancellationTokenSource _lifetime = new();
    private readonly WorldRenderCatalog _catalog = new();
    private readonly PresenceDecoder _presence;
    private readonly PendingReplies<Guid, OperationResultPacket> _operations = new();
    private readonly PendingReplies<Guid, ItemUseResultPacket> _itemUses = new();
    private readonly PendingReplies<Guid, RestartResultPacket> _restarts = new();
    private readonly PendingReplies<Guid, WingGateResultPacket> _wingGates = new();
    private readonly PendingReplies<ulong, NpcInteraction> _npcDialogs = new();
    private readonly PendingReplies<Guid, NpcServiceReply> _services = new();
    private readonly PendingReplies<Guid, QuestOutcome> _quests = new();
    // The state messages of a service request, collected until its result: they precede it on the same connection.
    private readonly ConcurrentDictionary<Guid, NpcServiceReply> _serviceParts = new();
    private readonly Channel<PresenceUpdate> _presenceUpdates = Channel.CreateBounded<PresenceUpdate>(new BoundedChannelOptions(4096)
    {
        SingleReader = true,
        SingleWriter = true,
        FullMode = BoundedChannelFullMode.Wait
    });
    private FrameConnection? _connection;
    private Task? _run;
    private Task? _heartbeat;
    private Exception? _terminalError;
    private IReadOnlyList<CharacterSummary> _characters = [];
    private PendingLogin? _login;
    private TaskCompletionSource<CharacterResultPacket>? _characterReply;
    private TaskCompletionSource<EnterWorldOutcome>? _enter;
    private TaskCompletionSource<bool>? _joined;
    private TaskCompletionSource<bool>? _left;
    private WorldEntryPacket? _entered;
    private uint _minimumProtocol;

    private string _account = "";
    private bool _disposed;

    public GameServerConnection(string address)
    {
        var uri = TlsTransport.ParseAddress(address);
        _host = uri.Host;
        _port = uri.Port;
        _certificatePolicy = ServerCertificatePolicy.FromAddress(uri);
        _presence = new PresenceDecoder(_catalog, RequestDescription);
        PersonalShops = new PersonalShopRequests(packet => Send(packet));
    }

    internal PersonalShopRequests PersonalShops { get; }

    public string? SessionToken { get; private set; }

    // The player's game master level from WorldJoined (0 for players).
    public int GameMasterLevel { get; private set; }

    // The server's game clock: the WorldJoined value carried forward on the local clock.
    public bool HasGameClock { get; private set; }
    public long GameClockOffsetMs { get; private set; }
    public long GameClockMs => GameClockOffsetMs + Environment.TickCount64;

    public bool RequiresNewConnection => SessionToken is not null || Volatile.Read(ref _terminalError) is not null;

    public long BytesReceived => _connection?.BytesReceived ?? 0;

    public long BytesSent => _connection?.BytesSent ?? 0;

    public async Task<LoginOutcome> LoginAsync(string account, string password, CancellationToken ct = default)
    {
        await ConnectAsync(ct);
        await _requestGate.WaitAsync(ct);
        try
        {
            if (!Fits(account, AccountWire.MaxAccountBytes) || !Fits(password, AccountWire.MaxPasswordBytes))
            {
                return LoginOutcome.Denied(LoginFailure.BadCredentials);
            }

            var pending = new PendingLogin();
            _login = pending;
            _account = account;
            Send(new LoginPacket(ProtocolVersion.Current, account, password));
            var outcome = await pending.Completion.Task.WaitAsync(ct);
            if (outcome.Ok)
            {
                SessionToken = outcome.SessionToken;
            }

            return outcome;
        }
        finally
        {
            _login = null;
            _requestGate.Release();
        }
    }

    public Task<IReadOnlyList<CharacterSummary>> ListCharactersAsync(CancellationToken ct = default)
    {
        ThrowIfDisconnected();
        return Task.FromResult(Volatile.Read(ref _characters));
    }

    public async Task<CreateCharacterOutcome> CreateCharacterAsync(string name, PlayerClass playerClass, int faceIndex, CancellationToken ct = default)
    {
        if (!Fits(name, AccountWire.MaxNameBytes))
        {
            return CreateCharacterOutcome.Failed(CreateCharacterFailure.NameLength);
        }

        var reply = await CharacterRequestAsync(new CharacterCommandPacket(CharacterAction.Create, playerClass, faceIndex, name), ct);
        if (!reply.Ok)
        {
            return CreateCharacterOutcome.Failed((CreateCharacterFailure)reply.Failure);
        }

        var summary = _characters.FirstOrDefault(candidate => string.Equals(candidate.Name, name.Trim(), StringComparison.OrdinalIgnoreCase));
        return summary is null ? CreateCharacterOutcome.Failed(CreateCharacterFailure.Rejected) : CreateCharacterOutcome.Success(summary);
    }

    public async Task<DeleteCharacterOutcome> DeleteCharacterAsync(string name, CancellationToken ct = default)
    {
        if (!Fits(name, AccountWire.MaxNameBytes))
        {
            return DeleteCharacterOutcome.Denied("character not found");
        }

        var reply = await CharacterRequestAsync(new CharacterCommandPacket(CharacterAction.Delete, 0, 0, name), ct);
        return reply.Ok ? DeleteCharacterOutcome.Success() : DeleteCharacterOutcome.Denied("character not found");
    }

    private static bool Fits(string text, int maxBytes) => text.Length > 0 && System.Text.Encoding.UTF8.GetByteCount(text) <= maxBytes;

    private void Send<TPacket>(in TPacket packet) where TPacket : IFramePacket
    {
        lock (_sendGate)
        {
            Connection.Send(_scratch.AsSpan(0, packet.Write(_scratch)));
        }
    }

    private async Task<CharacterResultPacket> CharacterRequestAsync(CharacterCommandPacket request, CancellationToken ct)
    {
        await _requestGate.WaitAsync(ct);
        try
        {
            var pending = new TaskCompletionSource<CharacterResultPacket>(TaskCreationOptions.RunContinuationsAsynchronously);
            _characterReply = pending;
            Send(request);
            return await pending.Task.WaitAsync(ct);
        }
        finally
        {
            _characterReply = null;
            _requestGate.Release();
        }
    }

    public async Task<EnterWorldOutcome> EnterWorldAsync(string characterName, CancellationToken ct = default)
    {
        if (!Fits(characterName, WorldEntryWire.MaxNameBytes))
        {
            return EnterWorldOutcome.Denied(EnterWorldFailure.NotFound);
        }

        await _requestGate.WaitAsync(ct);
        try
        {
            var pending = new TaskCompletionSource<EnterWorldOutcome>(TaskCreationOptions.RunContinuationsAsynchronously);
            _enter = pending;
            var request = new EnterWorldPacket(characterName);
            Send(request);

            return await pending.Task.WaitAsync(ct);
        }
        finally
        {
            _enter = null;
            _requestGate.Release();
        }
    }

    public IPlayChannel OpenPlayChannel(CancellationToken ct = default) => new OnlinePlayChannel(this, ct);

    public Task<WorldRenderCatalog> GetWorldCatalogAsync(CancellationToken ct = default) => Task.FromResult(_catalog);

    internal async Task OpenPlayAsync(CancellationToken ct)
    {
        await _requestGate.WaitAsync(ct);
        try
        {
            var entered = _entered ?? throw new InvalidOperationException("Enter the world before opening a play channel.");
            var pending = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            _joined = pending;
            _presence.SelfEntityId = entered.EntityId;
            var join = new JoinWorldPacket(entered.EntityId);
            Send(join);

            await pending.Task.WaitAsync(ct);
        }
        finally
        {
            _joined = null;
            _requestGate.Release();
        }
    }

    internal async Task ClosePlayAsync(CancellationToken ct)
    {
        await _requestGate.WaitAsync(ct);
        try
        {
            var pending = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            _left = pending;
            Send(new LeaveWorldPacket(Saved: false));

            await pending.Task.WaitAsync(ct);
            _entered = null;
        }
        finally
        {
            _left = null;
            _requestGate.Release();
        }
    }

    internal async Task<InventoryOutcome> ApplyInventoryAsync(InventoryCommand command, CancellationToken ct)
    {
        var packet = new InventoryCommandPacket(command.OperationId, command.ExpectedRevision, (int)command.Action, command.ItemId, command.Destination,
            command.Count, (int)command.Slot);
        var result = await OperationAsync(command.OperationId, packet, ct);
        return new InventoryOutcome(result.Ok, result.Revision, result.Message);
    }

    internal async Task<ItemUseOutcome> UseItemAsync(ItemUseCommand command, CancellationToken ct)
    {
        ThrowIfDisconnected();
        var packet = new ItemUseCommandPacket(command.OperationId, command.ExpectedRevision, command.ItemId, command.Argument, command.Text);
        var result = await _itemUses.RequestAsync(command.OperationId, () => Send(packet), ct);
        return new ItemUseOutcome(result.Ok, result.Revision, (ItemUseStatus)result.Status, result.ItemCode, result.Message);
    }

    // A dead player's restart; the new place arrives as a forced FieldWarp and the presence stream.
    internal async Task<RestartOutcome> RestartAsync(RestartChoice choice, CancellationToken ct)
    {
        ThrowIfDisconnected();
        var packet = new RestartRequestPacket(Guid.NewGuid(), (byte)choice);
        var result = await _restarts.RequestAsync(packet.OperationId, () => Send(packet), ct);
        return new RestartOutcome((RestartStatus)result.Status, (RestartChoice)result.Choice, result.ExperienceLost, result.GoldLost, result.ScrollUsed);
    }

    // A trip through the wing warp gates; the new place arrives as a forced FieldWarp.
    internal async Task<WingGateOutcome> TravelWingGateAsync(int destination, CancellationToken ct)
    {
        ThrowIfDisconnected();
        var packet = new WingGateRequestPacket(Guid.NewGuid(), (byte)destination);
        var result = await _wingGates.RequestAsync(packet.OperationId, () => Send(packet), ct);
        return new WingGateOutcome((WingGateStatus)result.Status, result.Destination, result.Fee);
    }

    internal async Task<ProgressionOutcome> ApplyProgressionAsync(ProgressionCommand command, CancellationToken ct)
    {
        var packet = new ProgressionCommandPacket(command.OperationId, command.ExpectedRevision, (int)command.Action, (int)command.Attribute,
            command.Amount, command.SkillCode, command.NpcEntityId);
        var result = await OperationAsync(command.OperationId, packet, ct);
        return new ProgressionOutcome(result.Ok, result.Revision, result.Message);
    }

    private Task<OperationResultPacket> OperationAsync<TPacket>(Guid operationId, TPacket packet, CancellationToken ct) where TPacket : IFramePacket
    {
        ThrowIfDisconnected();
        return _operations.RequestAsync(operationId, () => Send(packet), ct);
    }

    internal async Task<NpcInteraction> InteractNpcAsync(ulong entityId, CancellationToken ct)
    {
        ThrowIfDisconnected();
        if (_npcDialogs.IsWaiting(entityId))
        {
            return NpcInteraction.Rejected("already talking to this NPC");
        }

        return await _npcDialogs.RequestAsync(entityId, () => SendAttackRequest(entityId, sustained: false, 0), ct);
    }

    internal async Task<NpcServiceReply> RequestNpcServiceAsync(NpcServiceCommand command, CancellationToken ct)
    {
        ThrowIfDisconnected();
        var packet = new NpcServiceRequestPacket(command.OperationId, command.NpcEntityId, (byte)command.Service, (byte)command.Action,
            command.ExpectedRevision, command.ExpectedStorageRevision, command.ItemCode, command.ItemId, command.Anchor, command.Count, command.Amount,
            command.Materials, command.Covered);
        try
        {
            return await _services.RequestAsync(command.OperationId, () => Send(packet), ct);
        }
        finally
        {
            _serviceParts.TryRemove(command.OperationId, out _);
        }
    }

    internal Task<QuestOutcome> RequestQuestAsync(QuestCommand command, CancellationToken ct)
    {
        ThrowIfDisconnected();
        var packet = QuestWireReader.ToPacket(command);
        return _quests.RequestAsync(command.OperationId, () => Send(packet), ct);
    }

    internal void SendMovement(Vector3 position, float facing, bool moving, bool running, ulong actionTarget, int fieldCode)
    {
        var motion = !moving ? MotionState.Stand : running ? MotionState.Run : MotionState.Walk;
        var report = new MovementReportPacket(motion, fieldCode, position, facing, actionTarget);
        Send(report);
    }

    // The clicked target (0 stops attacking) and the request sequence; the server resolves every swing.
    internal void SendAttackRequest(ulong target, bool sustained, ulong sequence)
    {
        var attack = new AttackRequestPacket(target, sustained, sequence);
        Send(attack);
    }

    internal void SendSkillCast(uint sequence, uint skillCode, ulong target, Vector3? point, SkillCastCommand command = SkillCastCommand.Cast)
    {
        var aimed = point ?? Vector3.Zero;
        var cast = new SkillCastPacket(sequence, skillCode, target, GameUnits.ToFixed(aimed.X), GameUnits.ToFixed(aimed.Y), GameUnits.ToFixed(aimed.Z),
            0, (uint)Environment.TickCount, command);
        Send(cast);
    }

    // A typed line: party lines or everything else (spoken lines, whispers, commands); the server adds the speaker's name.
    internal void SendChat(string text, bool party)
    {
        var input = new ChatSayPacket(party, text);
        Send(input);
    }

    internal void SendYahooKey()
    {
        Send(new YahooKeyPacket());
    }

    internal void SendDismissCrystalPets(uint statusCode)
    {
        Send(new DismissCrystalPetsPacket(statusCode));
    }

    // Party, trade and contact requests, answered by what the presence stream shows.
    internal void SendSocial(IFramePacket packet) => Send(packet);

    internal IAsyncEnumerable<PresenceUpdate> ReadPresenceAsync(CancellationToken ct) => _presenceUpdates.Reader.ReadAllAsync(ct);

    internal void Recycle(PresenceUpdate update) => _presence.Recycle(update);

    private FrameConnection Connection
    {
        get
        {
            ThrowIfDisconnected();
            return _connection ?? throw new InvalidOperationException("Log in before sending a request.");
        }
    }

    private async Task ConnectAsync(CancellationToken ct)
    {
        if (_connection is not null)
        {
            return;
        }

        await _connectGate.WaitAsync(ct);
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_connection is not null)
            {
                return;
            }

            var connection = await TlsTransport.ConnectAsync(_host, _port, _certificatePolicy, ct);
            _connection = connection;
            _run = RunAsync(connection);
            _heartbeat = HeartbeatAsync(connection);
        }
        finally
        {
            _connectGate.Release();
        }
    }

    private async Task RunAsync(FrameConnection connection)
    {
        Exception? error = null;
        try
        {
            await connection.RunAsync(HandleAsync, _lifetime.Token);
        }
        catch (Exception ex)
        {
            error = ex;
        }
        finally
        {
            error ??= new IOException("Server closed the connection: " + (connection.CloseReason ?? "unknown"));
            Volatile.Write(ref _terminalError, error);
            _login?.Completion.TrySetException(error);
            _characterReply?.TrySetException(error);
            _enter?.TrySetException(error);
            _joined?.TrySetException(error);
            _left?.TrySetException(error);
            _operations.FailAll(error);
            _itemUses.FailAll(error);
            _restarts.FailAll(error);
            _wingGates.FailAll(error);
            _npcDialogs.FailAll(error);
            _services.FailAll(error);
            _quests.FailAll(error);
            PersonalShops.FailAll(error);

            _presenceUpdates.Writer.TryComplete(error);
        }
    }

    private async Task HeartbeatAsync(FrameConnection connection)
    {
        using var timer = new PeriodicTimer(DescriptionRetryInterval);
        var counter = 0u;
        var elapsed = TimeSpan.Zero;
        try
        {
            while (await timer.WaitForNextTickAsync(connection.Closed))
            {
                _presence.RetryDescriptions();
                elapsed += DescriptionRetryInterval;
                if (elapsed < HeartbeatInterval)
                {
                    continue;
                }

                elapsed = TimeSpan.Zero;
                Send(new HeartbeatPacket(++counter, Environment.TickCount64));
            }
        }
        catch (Exception ex) when (ex is OperationCanceledException or IOException or InvalidOperationException)
        {
        }
    }

    private ValueTask HandleAsync(ReadOnlyMemory<byte> memory)
    {
        var packet = memory.Span;
        if (_presence.TryHandle(packet, out var update))
        {
            return Publish(update);
        }

        var code = BinaryPrimitives.ReadUInt32LittleEndian(packet[4..]);
        switch (code)
        {
            case EtervaleOpcodes.ServerHello:
            {
                var hello = ServerHelloPacket.Read(packet);
                _minimumProtocol = hello.MinimumProtocol;
                break;
            }
            case EtervaleOpcodes.LoginResult:
                OnLoginResult(LoginResultPacket.Read(packet));
                break;
            case EtervaleOpcodes.CharacterList:
                OnCharacterList(CharacterListPacket.Read(packet));
                break;
            case EtervaleOpcodes.CharacterResult:
                _characterReply?.TrySetResult(CharacterResultPacket.Read(packet));
                break;
            case EtervaleOpcodes.Heartbeat:
                HeartbeatPacket.Read(packet);
                break;
            case EtervaleOpcodes.WorldEntry:
                OnWorldEntry(WorldEntryPacket.Read(packet));
                break;
            case EtervaleOpcodes.WorldJoined:
                var joined = WorldJoinedPacket.Read(packet);
                GameMasterLevel = joined.GameMasterLevel;
                GameClockOffsetMs = joined.GameClockMs - Environment.TickCount64;
                HasGameClock = true;
                _joined?.TrySetResult(true);
                break;
            case EtervaleOpcodes.LeaveWorld:
                _left?.TrySetResult(LeaveWorldPacket.Read(packet).Saved);
                break;
            case EtervaleOpcodes.InventoryResult:
            case EtervaleOpcodes.ProgressionResult:
            {
                var result = OperationResultPacket.Read(packet);
                _operations.Complete(result.OperationId, result);

                break;
            }
            case EtervaleOpcodes.RestartResult:
            {
                var restart = RestartResultPacket.Read(packet);
                _restarts.Complete(restart.OperationId, restart);

                break;
            }
            case EtervaleOpcodes.WingGateResult:
            {
                var wingGate = WingGateResultPacket.Read(packet);
                _wingGates.Complete(wingGate.OperationId, wingGate);

                break;
            }
            case EtervaleOpcodes.ItemUseResult:
            {
                var result = ItemUseResultPacket.Read(packet);
                _itemUses.Complete(result.OperationId, result);

                break;
            }
            case EtervaleOpcodes.NpcResult:
            {
                var dialog = NpcDialogPacket.Read(packet);
                if (_npcDialogs.IsWaiting(dialog.NpcId))
                {
                    var offers = dialog.Services
                        .Select(entry => new NpcServiceOffer((NpcServiceKind)entry.Kind, entry.Value, entry.Available, entry.PricePercent)).ToArray();
                    _npcDialogs.Complete(dialog.NpcId, new NpcInteraction(dialog.Ok, dialog.NpcId, dialog.Name, offers, dialog.Lines, dialog.Message));
                }

                break;
            }
            case EtervaleOpcodes.ShopCatalog:
            case EtervaleOpcodes.ShopQuotes:
            case EtervaleOpcodes.Warehouse:
            case EtervaleOpcodes.TeleportList:
            case EtervaleOpcodes.ItemWork:
            case EtervaleOpcodes.NpcServiceResult:
                OnServiceMessage(code, packet);
                break;
            case EtervaleOpcodes.QuestResult:
            {
                var result = QuestResultPacket.Read(packet);
                if (_quests.IsWaiting(result.OperationId))
                {
                    _quests.Complete(result.OperationId, QuestWireReader.ToOutcome(result));
                }

                break;
            }
            default:
                PersonalShops.Handle(code, packet);
                break;
        }

        return ValueTask.CompletedTask;
    }

    private void AddServicePart(Guid operationId, Func<NpcServiceReply, NpcServiceReply> add) =>
        _serviceParts.AddOrUpdate(operationId, _ => add(new NpcServiceReply(NpcServiceOutcome.Failed(NpcServiceStatus.Rejected, ""))), (_, reply) => add(reply));

    private void OnServiceMessage(uint code, ReadOnlySpan<byte> packet)
    {
        switch (code)
        {
            case EtervaleOpcodes.ShopCatalog:
            {
                var catalog = ShopCatalogPacket.Read(packet);
                if (_services.IsWaiting(catalog.OperationId))
                {
                    var offers = catalog.Items.Select(entry => new ShopOffer(entry.Code, entry.Tab, entry.Ordinal, entry.Name, (ItemKind)entry.Kind,
                        entry.Price, entry.Width, entry.Height, entry.MaxStack, ItemCodes.KeyOf(entry.Code))).ToArray();
                    AddServicePart(catalog.OperationId, reply => reply with { Catalog = offers });
                }

                break;
            }
            case EtervaleOpcodes.ShopQuotes:
            {
                var quotes = ShopQuotesPacket.Read(packet);
                if (_services.IsWaiting(quotes.OperationId))
                {
                    var shopQuotes = new ShopQuotes(quotes.InventoryRevision, quotes.Items
                        .Select(entry => new ShopQuote(entry.ItemId, entry.SellPrice, entry.RepairCost, entry.Durability, entry.MaxDurability)).ToArray());
                    AddServicePart(quotes.OperationId, reply => reply with { Quotes = shopQuotes });
                }

                break;
            }
            case EtervaleOpcodes.Warehouse:
            {
                var warehouse = WarehousePacket.Read(packet);
                if (_services.IsWaiting(warehouse.OperationId))
                {
                    var items = warehouse.Items.Select(StoreItems.View).ToArray();
                    var view = new WarehouseView(warehouse.Revision, warehouse.Money, warehouse.Weight, warehouse.MaxWeight, warehouse.Columns,
                        warehouse.Rows, items);
                    AddServicePart(warehouse.OperationId, reply => reply with { Warehouse = view });
                }

                break;
            }
            case EtervaleOpcodes.TeleportList:
            {
                var list = TeleportListPacket.Read(packet);
                if (_services.IsWaiting(list.OperationId))
                {
                    var destinations = list.Destinations
                        .Select(entry => new TeleportOffer(entry.Index, entry.FieldCode, entry.Name, entry.Price, entry.MinimumLevel)).ToArray();
                    AddServicePart(list.OperationId, reply => reply with { Destinations = destinations });
                }

                break;
            }
            case EtervaleOpcodes.ItemWork:
            {
                var work = ItemWorkPacket.Read(packet);
                if (_services.IsWaiting(work.OperationId))
                {
                    var result = new ItemWorkResult(work.Result, work.ItemId, work.Code);
                    AddServicePart(work.OperationId, reply => reply with { Work = result });
                }

                break;
            }
            case EtervaleOpcodes.NpcServiceResult:
            {
                var result = NpcServiceResultPacket.Read(packet);
                if (_services.IsWaiting(result.OperationId))
                {
                    var outcome = new NpcServiceOutcome(result.Ok, (NpcServiceStatus)result.Status, result.Revision, result.StorageRevision, result.Message);
                    var reply = _serviceParts.TryGetValue(result.OperationId, out var collected) ? collected with { Outcome = outcome } : new NpcServiceReply(outcome);
                    _services.Complete(result.OperationId, reply);
                }

                break;
            }
        }
    }

    // A successful login sends the character list first. A client older than the server's minimum cannot play.
    private void OnLoginResult(LoginResultPacket result)
    {
        if (_login is not { } login)
        {
            return;
        }

        if (result.Status != LoginFailure.None)
        {
            login.Completion.TrySetResult(LoginOutcome.Denied(result.Status));
            return;
        }

        login.Completion.TrySetResult(ProtocolVersion.Current < _minimumProtocol
            ? LoginOutcome.Denied(LoginFailure.OutdatedClient)
            : LoginOutcome.Success(_account, $"welcome, {_account}"));
    }

    private void OnCharacterList(CharacterListPacket list)
    {
        var summaries = list.Characters.Select(entry => new CharacterSummary(entry.Name, entry.Class, entry.Level, entry.FaceIndex, entry.Tier,
            entry.HairIndex, ItemCodes.KeyOf(entry.ArmorCode), ItemCodes.KeyOf(entry.WeaponCode), ItemCodes.KeyOf(entry.OffHandCode), entry.InVillage)).ToArray();
        Volatile.Write(ref _characters, summaries);
    }

    private void OnWorldEntry(WorldEntryPacket entry)
    {
        if (!entry.Entered)
        {
            _enter?.TrySetResult(EnterWorldOutcome.Denied((EnterWorldFailure)entry.Status));
            return;
        }

        _entered = entry;
        _enter?.TrySetResult(EnterWorldOutcome.Success(entry.Field, entry.EntityId, entry.X, entry.Z, $"entered field {entry.Field}"));
    }

    // A full queue (the game thread stalled on a field load or a debugger) holds the receive loop instead of closing the
    // connection: TCP pushes back and the server's send backlog decides.
    private ValueTask Publish(PresenceUpdate? update) =>
        update is null || _presenceUpdates.Writer.TryWrite(update) ? ValueTask.CompletedTask : _presenceUpdates.Writer.WriteAsync(update, _lifetime.Token);

    private void RequestDescription(ulong entityId)
    {
        Send(new DescribeEntityPacket(entityId));
    }

    private void ThrowIfDisconnected()
    {
        if (Volatile.Read(ref _terminalError) is { } error)
        {
            throw new IOException("TCP connection is closed.", error);
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        await _lifetime.CancelAsync();
        _connection?.Close("client disposed");
        if (_run is not null)
        {
            await _run;
        }

        if (_heartbeat is not null)
        {
            await _heartbeat;
        }

        if (_connection is not null)
        {
            await _connection.DisposeAsync();
        }

        _lifetime.Dispose();
        _connectGate.Dispose();
        _requestGate.Dispose();
    }

    private sealed class PendingLogin
    {
        public TaskCompletionSource<LoginOutcome> Completion { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }


}
