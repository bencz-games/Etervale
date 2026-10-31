using Dapper;
using Microsoft.Extensions.DependencyInjection;
using PristonTale.Application.GameMasters;
using PristonTale.Application.GameLogs;
using PristonTale.Persistence.Abstractions;
using PristonTale.Persistence.Abstractions.GameLogs;
using PristonTale.Persistence.Sqlite;

namespace PristonTale.Tests;

internal static class GameLogTests
{
    public static IEnumerable<(string, Func<Task>)> Cases()
    {
        yield return ("the SQLite game logs keep transfers, currency and server events, and pruning removes only the old rows", RepositoryAsync);
        yield return ("the log writer stores transfers in batches, retries a refused batch, drops past its capacity and writes the rest on stop", QueueAsync);
        yield return ("the currency ledger hands each sink's gold to the log once, and takes back a take the log could not store", CurrencyAsync);
        yield return ("the log service records the start under the configured server name once the host started, and the currency and stop at stop",
            ServiceAsync);
        yield return ("a game master's command returns while the audit's storage is locked; the row is stored once it frees, never twice", AuditAsync);
    }

    private static async Task AuditAsync()
    {
        var store = new LockedAudit();
        var audit = new GmAuditQueue(store, TimeSpan.FromMilliseconds(5));
        using var stop = new CancellationTokenSource();
        var writing = audit.RunAsync(stop.Token);
        var fixture = new ChatFixture(audit: audit);
        var gm = await fixture.JoinAsync("Keeper", 0, 0, GameMasterLevel.Guide);
        var command = fixture.SayAsync(gm, "/hide");
        var first = await Task.WhenAny(command, Task.Delay(TimeSpan.FromSeconds(5)));
        Assert.True(first == command && gm.Link.Last == "Hide OK!", "the command waited for the locked audit storage");
        await fixture.SayAsync(gm, "/show");
        Assert.True(store.Rows.Count == 0, "a row was stored while the storage was locked");
        store.Fail = true;
        store.Unlock();
        for (var wait = 0; audit.Stats.Failures == 0 && wait < 500; wait++)
        {
            await Task.Delay(2);
        }

        store.Fail = false;
        for (var wait = 0; store.Rows.Count < 2 && wait < 500; wait++)
        {
            await Task.Delay(2);
        }

        stop.Cancel();
        await writing;
        Assert.True(store.Rows.Select(row => row.Command).SequenceEqual(["/hide", "/show"]) && audit.Stats is { Written: 2, Dropped: 0 },
            $"the audit stored {string.Join(", ", store.Rows.Select(row => row.Command))} ({audit.Stats})");
    }

    // An audit storage that holds every write until unlocked, then refuses while Fail is set.
    private sealed class LockedAudit : PristonTale.Persistence.Abstractions.GameMasters.IGameMasterRepository
    {
        private readonly TaskCompletionSource _unlocked = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public List<PristonTale.Persistence.Abstractions.GameMasters.GmAuditEntry> Rows { get; } = new();
        public volatile bool Fail;

        public void Unlock() => _unlocked.TrySetResult();

        public async Task AppendAuditAsync(PristonTale.Persistence.Abstractions.GameMasters.GmAuditEntry entry, CancellationToken ct = default)
        {
            await _unlocked.Task;
            if (Fail)
            {
                throw new InvalidOperationException("database is locked");
            }

            lock (Rows)
            {
                Rows.Add(entry);
            }
        }

        public Task<bool> SetGmLevelAsync(string userId, int level, CancellationToken ct = default) => Task.FromResult(true);

        public Task<IReadOnlyList<PristonTale.Persistence.Abstractions.GameMasters.GmAuditRecord>> RecentAuditAsync(int limit, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<PristonTale.Persistence.Abstractions.GameMasters.GmAuditRecord>>([]);

        public Task<PristonTale.Persistence.Abstractions.GameMasters.CharacterTransfer> MoveCharacterAsync(string characterName, string targetUserId, int maxSlots,
            CancellationToken ct = default) => Task.FromResult(PristonTale.Persistence.Abstractions.GameMasters.CharacterTransfer.NoAccount);
    }

    private static ItemTransferRecord Transfer(DateTime at, long amount) =>
        new(at, ItemTransferKind.Trade, Guid.NewGuid().ToString(), 1, 2, null, 3, "WA101", Guid.NewGuid().ToString(), amount);

    private static async Task RepositoryAsync()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"priston-logs-{Guid.NewGuid():N}");
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddPristonSqlite(Path.Combine(directory, "logs.db"));
        try
        {
            await using var provider = services.BuildServiceProvider();
            await provider.GetRequiredService<IDatabaseMigrator>().MigrateAsync();
            var logs = provider.GetRequiredService<IGameLogRepository>();
            var old = DateTime.UtcNow.AddDays(-200);
            var now = DateTime.UtcNow;
            await logs.AppendItemTransfersAsync([Transfer(old, 1), Transfer(now, 2), Transfer(now, 3)]);
            await logs.AppendCurrencyAsync([new CurrencySinkRecord(old, (short)CurrencySink.Shop, 10), new CurrencySinkRecord(now, (short)CurrencySink.Aging, 20)]);
            await logs.AppendServerEventAsync(new ServerEventRecord(now, ServerEventKind.Started, "Etervale", ""));
            Assert.True(await logs.PruneAsync(DateTime.UtcNow.AddDays(-180)) == 2, "pruning did not remove exactly the two old rows");
            await using var connection = await provider.GetRequiredService<IDbConnectionFactory>().OpenAsync();
            Assert.True(await connection.ExecuteScalarAsync<long>("SELECT SUM(amount) FROM item_transfers") == 5
                && await connection.ExecuteScalarAsync<long>("SELECT gold FROM currency_sinks") == 20
                && await connection.ExecuteScalarAsync<int>("SELECT kind FROM server_events") == (int)ServerEventKind.Started,
                "the recent log rows were not kept as written");
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            Directory.Delete(directory, recursive: true);
        }
    }

    private static async Task QueueAsync()
    {
        var store = new MemoryGameLogs { Refusals = 1 };
        var queue = new GameLogQueue(store, TimeSpan.FromMilliseconds(10));
        var now = DateTime.UtcNow;
        queue.ItemTransfers(Enumerable.Range(0, GameLogQueue.Capacity + 1).Select(index => Transfer(now, index)).ToList());
        Assert.True(queue.Stats.Dropped == 1, "a full queue did not drop and count the overflow");
        using var stop = new CancellationTokenSource();
        var running = queue.RunAsync(stop.Token);
        for (var wait = 0; queue.Stats.Written < GameLogQueue.Capacity && wait < 1000; wait++)
        {
            await Task.Delay(10);
        }

        Assert.True(queue.Stats is { Written: GameLogQueue.Capacity, Failures: 1 } && store.Rows.Count == GameLogQueue.Capacity,
            $"the refused batch was not retried until every row was stored: {queue.Stats}");
        queue.ItemTransfers([Transfer(now, 7)]);
        await stop.CancelAsync();
        await running.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(store.Rows.Count == GameLogQueue.Capacity + 1, "the rows left at stop were not written");
    }

    private static Task CurrencyAsync()
    {
        var ledger = new CurrencyLedger();
        ledger.Record(CurrencySink.Shop, 100);
        ledger.Record(CurrencySink.Teleports, 40);
        var taken = ledger.TakeUnrecorded();
        Assert.True(taken[(int)CurrencySink.Shop] == 100 && taken[(int)CurrencySink.Teleports] == 40 && ledger.TakeUnrecorded().All(static gold => gold == 0),
            "a take did not hand each sink's gold once");
        ledger.Restore(taken);
        ledger.Record(CurrencySink.Shop, 5);
        Assert.True(ledger.TakeUnrecorded()[(int)CurrencySink.Shop] == 105 && ledger[CurrencySink.Shop] == 105 && ledger.Total == 145,
            "a take put back was lost, or the report totals changed");
        return Task.CompletedTask;
    }

    private static async Task ServiceAsync()
    {
        var logs = new MemoryGameLogs();
        var lifetime = new ManualLifetime();
        var world = new PristonTale.Application.World.WorldServer(new PristonTale.Application.World.WorldDataHolder());
        var control = new PristonTale.Server.Net.GameServerControl(new PristonTale.Application.Chat.ChatDirectory(), lifetime,
            Microsoft.Extensions.Logging.Abstractions.NullLogger<PristonTale.Server.Net.GameServerControl>.Instance);
        var service = new PristonTale.Server.Hosting.GameLogService(new GameLogQueue(logs), new GmAuditQueue(new MemoryGameMasters()), logs, world, control,
            new PristonTale.Server.Hosting.GameLogOptions(1, 0), lifetime,
            Microsoft.Extensions.Logging.Abstractions.NullLogger<PristonTale.Server.Hosting.GameLogService>.Instance);
        await service.StartAsync(CancellationToken.None);
        control.ServerName = "Configured";
        Assert.True(logs.Events.Count == 0, "the start was logged before the host started");
        lifetime.Start();
        for (var wait = 0; logs.Events.Count == 0 && wait < 500; wait++)
        {
            await Task.Delay(10);
        }

        world.Currency.Record(CurrencySink.Teleports, 30);
        await service.StopAsync(CancellationToken.None);
        Assert.True(logs.Events is [{ Kind: ServerEventKind.Started, ServerName: "Configured" }, { Kind: ServerEventKind.Stopped }]
            && logs.Currency.Single() is { Gold: 30 } sink && sink.Sink == (short)CurrencySink.Teleports,
            "the start, the last currency or the stop was not logged");
    }

    private sealed class ManualLifetime : Microsoft.Extensions.Hosting.IHostApplicationLifetime
    {
        private readonly CancellationTokenSource _started = new();

        public CancellationToken ApplicationStarted => _started.Token;
        public CancellationToken ApplicationStopping => CancellationToken.None;
        public CancellationToken ApplicationStopped => CancellationToken.None;

        public void Start() => _started.Cancel();

        public void StopApplication()
        {
        }
    }

    // The game logs in memory; Refusals item transfer batches throw before any is stored.
    private sealed class MemoryGameLogs : IGameLogRepository
    {
        public int Refusals;
        public List<ItemTransferRecord> Rows { get; } = [];
        public List<CurrencySinkRecord> Currency { get; } = [];
        public List<ServerEventRecord> Events { get; } = [];

        public Task AppendItemTransfersAsync(IReadOnlyList<ItemTransferRecord> transfers, CancellationToken ct = default)
        {
            if (Interlocked.Decrement(ref Refusals) >= 0)
            {
                throw new InvalidOperationException("storage refused the batch");
            }

            Rows.AddRange(transfers);
            return Task.CompletedTask;
        }

        public Task AppendCurrencyAsync(IReadOnlyList<CurrencySinkRecord> sinks, CancellationToken ct = default)
        {
            Currency.AddRange(sinks);
            return Task.CompletedTask;
        }

        public Task AppendServerEventAsync(ServerEventRecord serverEvent, CancellationToken ct = default)
        {
            Events.Add(serverEvent);
            return Task.CompletedTask;
        }

        public Task<int> PruneAsync(DateTime before, CancellationToken ct = default) => Task.FromResult(0);
    }
}
