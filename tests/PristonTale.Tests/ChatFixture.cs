using System.Numerics;
using PristonTale.Application.Chat;
using PristonTale.Application.GameMasters;
using PristonTale.Application.Sessions;
using PristonTale.Application.World;
using PristonTale.Persistence.Abstractions.Accounts;
using PristonTale.Persistence.Abstractions.Characters;
using PristonTale.Persistence.Abstractions.GameMasters;
using PristonTale.Persistence.Abstractions.Warehouses;
using PristonTale.Simulation.Characters;
using PristonTale.Simulation.Combat;
using PristonTale.Simulation.Items;
using PristonTale.Simulation.World;
using PristonTale.Simulation.World.Fields;
using PristonTale.Simulation.World.Loot;
using PristonTale.Simulation.World.Monsters;
using PristonTale.Contracts.Characters;

namespace PristonTale.Tests;

// A world of one field (code 3) with chat and game-master commands, players placed by hand, and links that record what
// each player's connection received.
internal sealed class ChatFixture
{
    public const int Field = 3;
    private readonly CharacterClassRegistry _classes = new();
    private readonly SessionRegistry _sessions = new();
    private uint _tick;
    private long _accounts;

    public ChatFixture(GameMasterOptions? options = null, IPartyRoster? parties = null, IClanRoster? clans = null,
        FieldTerrain? terrain = null, IReadOnlyList<FieldPoint>? startPoints = null, FieldState state = FieldState.Forest, TimeProvider? clock = null,
        IGmAudit? audit = null)
    {
        var empty = WorldData.Empty;
        Data = new WorldDataHolder
        {
            Current = new WorldData(new ItemCatalog([Axe, Potion]), new MonsterCatalog([Hopy]), empty.Npcs,
                new Dictionary<int, FieldSpawnData> { [Field] = FieldSpawnData.Empty }, empty.Motions)
        };
        var catalog = Catalog = new WorldCatalog([new FieldDef("fixture", "Fixture Field", state, new FieldBackdrop("", "", ""), "", [])
        {
            FieldCode = Field,
            StartPoints = startPoints ?? []
        }]);
        var terrains = new FieldTerrainCatalog();
        if (terrain is not null)
        {
            terrains.Publish(new Dictionary<int, FieldTerrain> { [Field] = terrain });
        }

        World = new WorldServer(Data, catalog, clock, terrain: terrains, log: Log);
        Directory = new ChatDirectory();
        Server = new FakeServer();
        GameMasters = new GameMasterCommands(Directory, World, Data, options, Server, Audit, audit ?? Audit)
        {
            Accounts = Accounts,
            Saves = new ImmediateSaves(Characters),
            Warehouses = Warehouses,
            NowMs = () => NowMs
        };
        Chat = new ChatAppService(Directory, World, GameMasters, parties, clans) { NowMs = () => NowMs };
    }

    public static readonly MonsterDefinition Hopy = new(1, "Hopy", MonsterFaction.Enemy, MonsterClass.Normal, MonsterBrood.Normal,
        new MonsterCombatStats(4, 30, new MonsterRange(1, 2), 6, 30, 0, 10, 0, 50, ResistanceBlock.Zero), "")
    {
        ExpReward = 10,
        Drops = new DropTable([DropEntry.GoldSlot(100, 8, 8)], 1, false)
    };

    // A weapon with ranges and a class specialization, for item creation.
    public static readonly ItemDefinition Axe = new(0x0101_0100, "Stone Axe", ItemKind.Axe, EquipmentSlot.Weapon, GridWidth: 2, GridHeight: 3)
    {
        CatalogKey = "WA101",
        Price = 100,
        Weight = 10,
        Requirements = new CharacterAttributes(40, 0, 0, 20, 0),
        Template = new ItemTemplate
        {
            Durability = new ItemRange(60, 80),
            DamageMin = new ItemRange(2, 6),
            DamageMax = new ItemRange(8, 14),
            AttackRating = new ItemRange(10, 30),
            SpecJob = (uint)JobBitCode.Fighter,
            RandomSpecJobs = [(uint)JobBitCode.Mechanician],
            Spec = new ItemSpecTemplate { Defence = new ItemRange(1, 9), LevelAttackRating = new ItemRange(0, 5) }
        }
    };

    public static readonly ItemDefinition Potion = new(0x0405_0100, "Mini Life Potion", ItemKind.Potion, MaxStack: 100) { CatalogKey = "PL101", Weight = 1 };

    public ISessionRegistry Sessions => _sessions;
    public WorldCatalog Catalog { get; }
    public WorldDataHolder Data { get; }
    public WorldServer World { get; }
    public ChatDirectory Directory { get; }
    public FakeServer Server { get; }
    public MemoryGameMasters Audit { get; } = new();
    public MemoryAccounts Accounts { get; } = new();
    public MemoryCharacterBook Characters { get; } = new();
    public RecordingLog Log { get; } = new();
    public MemoryWarehouses Warehouses { get; } = new();
    public GameMasterCommands GameMasters { get; }
    public ChatAppService Chat { get; }
    public long NowMs { get; set; } = 1_000_000;

    public async Task<Player> JoinAsync(string name, float x, float z, int gmLevel = 0, PlayerClass playerClass = PlayerClass.Fighter, int level = 1)
    {
        var session = _sessions.Create(++_accounts, name.ToLowerInvariant() + "-account");
        session.GmLevel = gmLevel;
        Assert.True(session.TryBeginEntry(), "fixture entry");
        var record = CharacterRecordFactory.NewStarter(_classes, session.AccountId, name, 0, playerClass, Field) with { Id = (long)session.EntityId };
        session.Character = _classes.CreatePlayer(playerClass, name, level);
        session.Loaded = record;
        session.CharacterName = name;
        session.FieldId = Field;
        session.SpawnPosition = new Vector3(x, 0, z);
        Assert.True(session.CompleteEntry(true) && session.TryBeginPlay(), "fixture play");
        Characters.Records[name] = record;
        var outbox = new WorldOutbox();
        Assert.True(await RunAsync(World.JoinAsync(Field, WorldPlayerFactory.Create(session), outbox.Writer, session)), "fixture join");
        var link = new FakeLink();
        var member = new ChatMember(session, name, link, NowMs, session.Character.Money);
        Directory.Join(member);
        return new Player(session, member, link, outbox);
    }

    public async Task SayAsync(Player player, string text) => await RunAsync(Chat.SayAsync(player.Session.EntityId, text));

    public Task RunAsync(Task task) => WorldPump.RunAsync(World, task, () => ++_tick);

    public Task<T> RunAsync<T>(Task<T> task) => WorldPump.RunAsync(World, task, () => ++_tick);

    public void Tick(int ticks)
    {
        for (var step = 0; step < ticks; step++)
        {
            World.Tick(0.05f, ++_tick);
        }
    }

    // The latest snapshot a player's connection received.
    public static WorldSnapshot? Latest(Player player)
    {
        WorldSnapshot? latest = null;
        while (player.Outbox.Reader.TryRead(out var snapshot))
        {
            latest = snapshot;
        }

        return latest;
    }

    public sealed record Player(PlayerSession Session, ChatMember Member, FakeLink Link, WorldOutbox Outbox)
    {
        public ulong Id => Session.EntityId;
    }
}

internal sealed class FakeLink : IChatLink
{
    public List<ChatLine> Lines { get; } = new();
    public List<(int Field, Vector3 Position)> Warps { get; } = new();
    public string? Disconnected { get; private set; }
    public int CloseRequests { get; private set; }
    public string RemoteAddress => "203.0.113.7";
    public long PacketsReceived => 12;
    public long PacketsSent => 34;

    public void Deliver(ChatLine line)
    {
        lock (Lines)
        {
            Lines.Add(line);
        }
    }

    public void Warp(int fieldId, Vector3 position) => Warps.Add((fieldId, position));

    public void RequestClose() => CloseRequests++;

    public void Disconnect(string reason) => Disconnected = reason;

    public IReadOnlyList<string> Texts()
    {
        lock (Lines)
        {
            return Lines.Select(line => line.Text).ToArray();
        }
    }

    public string Last => Texts().LastOrDefault() ?? "";

    public void Clear()
    {
        lock (Lines)
        {
            Lines.Clear();
        }
    }
}

internal sealed class FakeServer : IServerOperations
{
    public int OpenConnections => 5;
    public int UserLimit { get; set; }

    public string ServerName { get; set; } = "";
    public int Shutdowns { get; private set; }
    public List<BlockedLogin> Blocked { get; } = [new BlockedLogin("guesser", 240_000)];

    public bool BeginShutdown()
    {
        Shutdowns++;
        return Shutdowns == 1;
    }

    public IReadOnlyList<BlockedLogin> BlockedLogins() => Blocked.ToArray();

    public int ClearBlockedLogins()
    {
        var count = Blocked.Count;
        Blocked.Clear();
        return count;
    }

    public bool ReleaseBlockedLogin(string account) => Blocked.RemoveAll(entry => entry.Account == account) > 0;
}

internal sealed class MemoryGameMasters : IGameMasterRepository, IGmAudit
{
    public List<GmAuditEntry> Entries { get; } = new();
    public Dictionary<string, int> Levels { get; } = new(StringComparer.OrdinalIgnoreCase);
    public List<(string Character, string Account)> Moves { get; } = new();

    public Task<bool> SetGmLevelAsync(string userId, int level, CancellationToken ct = default)
    {
        Levels[userId] = level;
        return Task.FromResult(true);
    }

    public Task AppendAuditAsync(GmAuditEntry entry, CancellationToken ct = default)
    {
        Append(entry);
        return Task.CompletedTask;
    }

    public void Append(GmAuditEntry entry)
    {
        lock (Entries)
        {
            Entries.Add(entry);
        }
    }

    public Task<IReadOnlyList<GmAuditRecord>> RecentAuditAsync(int limit, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<GmAuditRecord>>([]);

    public Task<CharacterTransfer> MoveCharacterAsync(string characterName, string targetUserId, int maxSlots, CancellationToken ct = default)
    {
        Moves.Add((characterName, targetUserId));
        return Task.FromResult(targetUserId == "nobody" ? CharacterTransfer.NoAccount : CharacterTransfer.Moved);
    }
}

internal sealed class MemoryAccounts : IAccountRepository
{
    public Task<AccountRecord?> FindByUserIdAsync(string userId, CancellationToken ct = default) =>
        Task.FromResult<AccountRecord?>(userId switch
        {
            "banker" => new AccountRecord(77, "banker", "", false, false),
            "spoiled" => new AccountRecord(78, "spoiled", "", false, false),
            _ => null
        });

    public Task<AccountRecord> CreateAsync(string userId, string passwordHash, CancellationToken ct = default) => throw new NotSupportedException();

    public Task UpdateLastLoginAsync(long accountId, CancellationToken ct = default) => Task.CompletedTask;
}

internal sealed class MemoryWarehouses : IWarehouseRepository
{
    public Task<WarehouseRecord?> FindAsync(long accountId, CancellationToken ct = default) =>
        Task.FromResult<WarehouseRecord?>(accountId switch
        {
            77 => new WarehouseRecord(77, 4500, 1, new JsonText("[]")),
            78 => new WarehouseRecord(78, 0, 1, new JsonText("{\"version\": 99}")),
            _ => null
        });

    public Task<bool> SaveWithCharacterAsync(CharacterRecord character, WarehouseRecord warehouse, long expectedRevision, CancellationToken ct = default) =>
        throw new NotSupportedException();
}

// Character rows by name; a game master's gift writes through UpdateAsync.
internal sealed class MemoryCharacterBook : ICharacterRepository
{
    public Dictionary<string, CharacterRecord> Records { get; } = new(StringComparer.OrdinalIgnoreCase);
    public int Updates { get; private set; }

    public Task<CharacterRecord?> FindByNameForAccountAsync(long accountId, string name, CancellationToken ct = default) =>
        Task.FromResult(Records.GetValueOrDefault(name));

    // Runs before a write of one character.
    public Func<CharacterRecord, Task>? BeforeUpdate { get; set; }

    public async Task<bool> UpdateAsync(CharacterRecord record, CancellationToken ct = default)
    {
        if (BeforeUpdate is not null)
        {
            await BeforeUpdate(record);
        }

        Updates++;
        Records[record.Name] = record;
        return true;
    }

    public async Task<int> SaveAllAsync(IReadOnlyList<CharacterRecord> records, CancellationToken ct = default)
    {
        foreach (var record in records)
        {
            await UpdateAsync(record, ct);
        }

        return records.Count;
    }

    // Runs before a write of several characters; false refuses it with nothing written.
    public Func<IReadOnlyList<CharacterRecord>, Task<bool>>? BeforeUpdateAll { get; set; }

    public async Task<bool> UpdateAllAsync(IReadOnlyList<CharacterRecord> records, CancellationToken ct = default)
    {
        if (BeforeUpdateAll is not null && !await BeforeUpdateAll(records))
        {
            return false;
        }

        Updates++;
        foreach (var record in records)
        {
            Records[record.Name] = record;
        }

        return true;
    }

    public Task<IReadOnlyList<CharacterRecord>> ListByAccountAsync(long accountId, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<CharacterRecord>>(Records.Values.Where(record => record.AccountId == accountId).ToArray());

    public Task<bool> NameExistsAsync(string name, CancellationToken ct = default) => Task.FromResult(Records.ContainsKey(name));

    public Task<IReadOnlyList<short>> UsedSlotsAsync(long accountId, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<short>>([]);

    public Task<CharacterRecord> CreateAsync(CharacterRecord record, CancellationToken ct = default) => throw new NotSupportedException();

    public Task<bool> DeleteByNameAsync(long accountId, string name, CancellationToken ct = default) => throw new NotSupportedException();
}
