using System.Numerics;
using System.Threading.Channels;
using Microsoft.Extensions.DependencyInjection;
using PristonTale.Application.Saves;
using PristonTale.Application.Services;
using PristonTale.Application.Sessions;
using PristonTale.Application.World;
using PristonTale.Persistence.Abstractions;
using PristonTale.Persistence.Abstractions.Accounts;
using PristonTale.Persistence.Abstractions.Characters;
using PristonTale.Persistence.Sqlite;
using PristonTale.Simulation.Characters;
using PristonTale.Simulation.Items;
using PristonTale.Simulation.World;
using PristonTale.Contracts.Characters;

namespace PristonTale.Tests;

internal static class CharacterSaveTests
{
    public static IEnumerable<(string, Func<Task>)> Cases()
    {
        yield return ("a character keeps its newest pending capture and storage never takes an older one over a newer", CoalesceAsync);
        yield return ("a batch storage refuses is retried until stored, and a leave save completes only then", RetryAsync);
        yield return ("a stopping server journals what storage refuses, and the next start stores the journal", JournalAsync);
        yield return ("the SQLite repository writes a capture only over an older one", SqliteSequenceAsync);
        yield return ("storage keeps the newest operations of each history, and owned state past 64 KiB loads", AggregateAsync);
        yield return ("the world captures each playing character once per checkpoint period unless its save gate is busy", CheckpointAsync);
        yield return ("a character entering reads its record again once claimed, so a leave save stored meanwhile is the one loaded", ReEntryAsync);
        yield return ("stopping during a long backoff journals at once instead of waiting it out", StopDuringBackoffAsync);
        yield return ("a capture storage refuses while it is reachable is dropped and never holds the other characters", RefusedCaptureAsync);
        yield return ("a transfer stored first is never replaced by an older queued capture of the same character", TransferFirstAsync);
    }

    private static readonly CharacterClassRegistry Classes = new();

    private static (CharacterRecord Record, PlayerCharacter Character, PlayerSession Session) Character(long id = 1)
    {
        var record = CharacterRecordFactory.NewStarter(Classes, 1, $"Saver{id}", 0, PlayerClass.Fighter, 3) with { Id = id };
        var character = Classes.CreatePlayer(PlayerClass.Fighter, record.Name);
        var session = new SessionRegistry().Create(1, "saver");
        session.Loaded = record;
        return (record, character, session);
    }

    private static CharacterSaveSnapshot Capture(CharacterRecord record, PlayerCharacter character, long money)
    {
        character.RestoreProgress(0, money);
        return CharacterRecordFactory.Capture(record, character, 3, Vector3.Zero);
    }

    private static async Task CoalesceAsync()
    {
        var (record, character, session) = Character();
        var repository = new MemoryCharacters(record);
        var queue = new CharacterSaveQueue(repository, new SaveJournal(Path.Combine(Path.GetTempPath(), $"priston-journal-{Guid.NewGuid():N}")));
        queue.Enqueue(session, Capture(record, character, 10));
        queue.Enqueue(session, Capture(record, character, 20));
        var leave = queue.SaveAsync(session, Capture(record, character, 30));
        using var stop = new CancellationTokenSource();
        var running = queue.RunAsync(CancellationToken.None, stop.Token);
        Assert.True(await leave.WaitAsync(TimeSpan.FromSeconds(5)) && repository.Record.Money == 30 && repository.Record.SaveSeq == 3
            && repository.UpdateCount == 1, $"three captures were not stored as the newest, once: {repository.UpdateCount} writes");
        Assert.True(!await repository.UpdateAsync(repository.Record with { Money = 5, SaveSeq = 2 }) && repository.Record.Money == 30,
            "an older capture replaced a newer one");
        await stop.CancelAsync();
        await running.WaitAsync(TimeSpan.FromSeconds(5));
    }

    private static async Task RetryAsync()
    {
        var (record, character, session) = Character();
        var repository = new MemoryCharacters(record);
        var refusing = true;
        repository.BeforeUpdate = _ => Task.FromResult(!Volatile.Read(ref refusing));
        var queue = new CharacterSaveQueue(repository, new SaveJournal(Path.Combine(Path.GetTempPath(), $"priston-journal-{Guid.NewGuid():N}")),
            firstBackoff: TimeSpan.FromMilliseconds(20));
        var leave = queue.SaveAsync(session, Capture(record, character, 40));
        using var stop = new CancellationTokenSource();
        var running = queue.RunAsync(CancellationToken.None, stop.Token);
        await Task.Delay(100);
        Assert.True(!leave.IsCompleted && repository.Record.Money != 40 && queue.Stats.Failures > 0, "a leave save completed before storage took it");
        // A newer capture arriving while the batch fails takes the leave's waiter.
        queue.Enqueue(session, Capture(record, character, 50));
        Volatile.Write(ref refusing, false);
        Assert.True(await leave.WaitAsync(TimeSpan.FromSeconds(10)) && repository.Record.Money == 50 && queue.Stats.Dropped == 0,
            "the refused batch was not retried until stored with the newer capture");
        await stop.CancelAsync();
        await running.WaitAsync(TimeSpan.FromSeconds(5));
    }

    private static async Task JournalAsync()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"priston-journal-{Guid.NewGuid():N}");
        try
        {
            var (record, character, session) = Character();
            var repository = new MemoryCharacters(record) { BeforeUpdate = _ => Task.FromResult(false) };
            var queue = new CharacterSaveQueue(repository, new SaveJournal(directory));
            using var stopping = new CancellationTokenSource();
            await stopping.CancelAsync();
            var leave = queue.SaveAsync(session, Capture(record, character, 70));
            using var stop = new CancellationTokenSource();
            var running = queue.RunAsync(stopping.Token, stop.Token);
            Assert.True(await leave.WaitAsync(TimeSpan.FromSeconds(5)) && new SaveJournal(directory).Files().Count == 1 && repository.Record.Money != 70,
                "the refused leave save was not journaled while stopping");
            await stop.CancelAsync();
            await running.WaitAsync(TimeSpan.FromSeconds(5));
            queue.Enqueue(session, Capture(record, character, 80));
            Assert.True(new SaveJournal(directory).Files().Count == 2, "a capture after the writer stopped was not journaled");

            repository.BeforeUpdate = null;
            await new CharacterSaveQueue(repository, new SaveJournal(directory)).ReplayAsync(CancellationToken.None);
            Assert.True(repository.Record.Money == 80 && repository.Record.SaveSeq == 2 && new SaveJournal(directory).Files().Count == 0,
                "the journal was not stored in order and removed");
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    private static async Task SqliteSequenceAsync()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"priston-save-seq-{Guid.NewGuid():N}");
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddPristonSqlite(Path.Combine(directory, "saves.db"));
        try
        {
            await using var provider = services.BuildServiceProvider();
            await provider.GetRequiredService<IDatabaseMigrator>().MigrateAsync();
            var account = await provider.GetRequiredService<IAccountRepository>().CreateAsync("saver", "hash");
            var characters = provider.GetRequiredService<ICharacterRepository>();
            var created = await characters.CreateAsync(CharacterRecordFactory.NewStarter(Classes, account.Id, "Sequenced", 0, PlayerClass.Fighter, 3));
            Assert.True(created.SaveSeq == 0, "a new character does not start the save sequence at 0");
            Assert.True(await characters.SaveAllAsync([created with { Money = 9, SaveSeq = 5 }]) == 1
                && await characters.SaveAllAsync([created with { Money = 4, SaveSeq = 3 }, created with { Id = created.Id + 99, SaveSeq = 9 }]) == 0
                && !await characters.UpdateAsync(created with { Money = 2, SaveSeq = 4 })
                && await characters.UpdateAsync(created with { Money = 11, SaveSeq = 5 }),
                "an older capture or a missing character was written, or the same capture was refused");
            var stored = await characters.FindByNameForAccountAsync(account.Id, "Sequenced");
            Assert.True(stored is { Money: 11, SaveSeq: 5 }, $"storage holds {stored?.Money} at {stored?.SaveSeq}");
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            Directory.Delete(directory, recursive: true);
        }
    }

    private static Task AggregateAsync()
    {
        var (record, character, _) = Character();
        var stone = new ItemDefinition(1, "Stone", ItemKind.Material);
        for (var page = 0; page < 30; page++)
        {
            var command = new InventoryCommand(Guid.NewGuid(), character.InventoryRevision, InventoryAction.SwitchBagPage, Guid.Empty, (page + 1) % 2, 0, default);
            Assert.True(InventoryCommands.Apply(character, command), "a page switch was refused");
            character.RecordInventoryOperation(command);
        }

        var encoded = CharacterRecordFactory.Capture(record, character, 3, Vector3.Zero).Encode();
        var saved = CharacterJson.Read<SavedItems>(encoded.Inventory)!;
        Assert.True(character.InventoryOperations.Count == 30 && saved.Operations!.Length == 16
            && saved.Operations[^1].Command.OperationId == character.InventoryOperations[^1].Command.OperationId, "storage kept more than the newest 16 operations");
        var restored = Classes.CreatePlayer(PlayerClass.Fighter, record.Name);
        CharacterStateCodec.Restore(restored, encoded, new ItemCatalog([stone]));
        Assert.True(restored.InventoryOperations.Count == 16 && restored.InventoryRevision == character.InventoryRevision, "the stored history did not load");

        var padded = encoded with { Equipment = new JsonText("{" + new string(' ', 70_000) + "}") };
        CharacterStateCodec.Restore(Classes.CreatePlayer(PlayerClass.Fighter, record.Name), padded, new ItemCatalog([stone]));

        // Both bag pages full of single items, the special item box full and an item in the hand all load back.
        var cake = new ItemDefinition(PristonTale.Simulation.Quests.QuestItems.Cake, "Cake", ItemKind.Quest);
        var full = Classes.CreatePlayer(PlayerClass.Fighter, record.Name);
        for (var page = 0; page < PlayerCharacter.Sides; page++)
        {
            while (full.Inventory.TryAdd(new ItemInstance(stone)))
            {
            }

            Assert.True(page == PlayerCharacter.Sides - 1 || full.TrySwitchBagPage(page + 1, Guid.Empty), "the next bag page did not show");
        }

        while (full.QuestBox.TryAdd(new ItemInstance(cake)))
        {
        }

        Assert.True(full.TryHold(new ItemInstance(stone)), "the hand did not take an item");
        var owned = full.OwnedItems().Count();
        var reloaded = Classes.CreatePlayer(PlayerClass.Fighter, record.Name);
        CharacterStateCodec.Restore(reloaded, CharacterRecordFactory.Capture(record, full, 3, Vector3.Zero).Encode(), new ItemCatalog([stone, cake]));
        Assert.True(owned > 150 && reloaded.OwnedItems().Count() == owned, $"a full pack of {owned} items did not load back");
        return Task.CompletedTask;
    }

    private static async Task CheckpointAsync()
    {
        var (record, character, _) = Character();
        var repository = new MemoryCharacters(CharacterRecordFactory.FromLiveSession(record, character, 3, Vector3.Zero));
        var saves = new CountingSaves();
        var holder = new WorldDataHolder();
        var world = new WorldServer(holder, new WorldCatalog(), options: WorldServerOptions.Default with { CheckpointSeconds = 1 }, saves: saves);
        var sessions = new SessionRegistry();
        var session = sessions.Create(1, "owner");
        var service = new SessionAppService(sessions, Classes, repository, saves, new WorldCatalog(), world, holder);
        Assert.True((await service.EnterWorldAsync(session.SessionToken, record.Name)).Ok && session.TryBeginPlay(), "setup entry failed");
        await WorldPump.RunAsync(world, world.JoinAsync(3, WorldPlayerFactory.Create(session), Channel.CreateUnbounded<WorldSnapshot>().Writer, session));
        var before = saves.Count;
        for (uint tick = 1000; tick < 1000 + 2 * WorldServer.TickHz; tick++)
        {
            world.Tick(0.05f, tick);
        }

        Assert.True(saves.Count - before == 2, $"two checkpoint periods captured {saves.Count - before} times");
        await session.SaveGate.WaitAsync();
        for (uint tick = 2000; tick < 2000 + WorldServer.TickHz; tick++)
        {
            world.Tick(0.05f, tick);
        }

        session.SaveGate.Release();
        Assert.True(saves.Count - before == 2, "a character was captured while an operation held its save gate");
    }

    private static async Task ReEntryAsync()
    {
        var (record, character, _) = Character();
        var stored = CharacterRecordFactory.Capture(record, character, 3, Vector3.Zero).Encode() with { Money = 70, SaveSeq = 7 };
        var repository = new MemoryCharacters(stored);
        repository.EarlierReads.Enqueue(stored with { Money = 30, SaveSeq = 3 });
        var sessions = new SessionRegistry();
        var session = sessions.Create(1, "owner");
        var service = new SessionAppService(sessions, Classes, repository, new ImmediateSaves(repository), new WorldCatalog());
        Assert.True((await service.EnterWorldAsync(session.SessionToken, record.Name)).Ok, "entry failed");
        Assert.True(session.Loaded is { SaveSeq: 7 } && session.Character!.Money == 70 && session.NextSaveSeq() == 8,
            "the record read before the claim was loaded");
    }

    private static async Task StopDuringBackoffAsync()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"priston-journal-{Guid.NewGuid():N}");
        try
        {
            var (record, character, session) = Character();
            var repository = new MemoryCharacters(record) { BeforeUpdate = _ => Task.FromResult(false) };
            var queue = new CharacterSaveQueue(repository, new SaveJournal(directory), firstBackoff: TimeSpan.FromMinutes(5));
            var leave = queue.SaveAsync(session, Capture(record, character, 60));
            using var stopping = new CancellationTokenSource();
            using var stop = new CancellationTokenSource();
            var running = queue.RunAsync(stopping.Token, stop.Token);
            await Task.Delay(100);
            await stopping.CancelAsync();
            Assert.True(await leave.WaitAsync(TimeSpan.FromSeconds(2)) && new SaveJournal(directory).Files().Count == 1,
                "the leave save waited out the backoff instead of being journaled");
            await stop.CancelAsync();
            await running.WaitAsync(TimeSpan.FromSeconds(5));
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    private static async Task RefusedCaptureAsync()
    {
        var (first, firstCharacter, firstSession) = Character(1);
        var (second, secondCharacter, secondSession) = Character(2);
        var book = new MemoryCharacterBook();
        book.Records[first.Name] = first;
        book.Records[second.Name] = second;
        book.BeforeUpdate = written => written.Name == second.Name ? throw new InvalidOperationException("unwritable row") : Task.CompletedTask;
        var queue = new CharacterSaveQueue(book, new SaveJournal(Path.Combine(Path.GetTempPath(), $"priston-journal-{Guid.NewGuid():N}")),
            firstBackoff: TimeSpan.FromMilliseconds(10));
        var refused = queue.SaveAsync(secondSession, Capture(second, secondCharacter, 5));
        using var stop = new CancellationTokenSource();
        var running = queue.RunAsync(CancellationToken.None, stop.Token);
        for (var money = 1; !refused.IsCompleted && money < 500; money++)
        {
            queue.Enqueue(firstSession, Capture(first, firstCharacter, money));
            await Task.Delay(10);
        }

        Assert.True(refused.IsCompleted && !await refused && queue.Stats.Dropped == 1, "the unwritable capture was not dropped");
        var last = queue.SaveAsync(firstSession, Capture(first, firstCharacter, 777));
        Assert.True(await last.WaitAsync(TimeSpan.FromSeconds(5)) && book.Records[first.Name].Money == 777, "the other character stopped being stored");
        await stop.CancelAsync();
        await running.WaitAsync(TimeSpan.FromSeconds(5));
    }

    private static async Task TransferFirstAsync()
    {
        var (record, character, session) = Character();
        var repository = new MemoryCharacters(record);
        var queue = new CharacterSaveQueue(repository, new SaveJournal(Path.Combine(Path.GetTempPath(), $"priston-journal-{Guid.NewGuid():N}")));
        queue.Enqueue(session, Capture(record, character, 10));
        Assert.True(await repository.UpdateAsync(session.Stamp(Capture(record, character, 99))), "the transfer was not stored");
        using var stop = new CancellationTokenSource();
        var running = queue.RunAsync(CancellationToken.None, stop.Token);
        for (var wait = 0; queue.Stats.Batches == 0 && wait < 500; wait++)
        {
            await Task.Delay(10);
        }

        Assert.True(queue.Stats.Batches == 1 && repository.Record.Money == 99 && repository.Record.SaveSeq == 2,
            "an older queued capture replaced the transfer");
        await stop.CancelAsync();
        await running.WaitAsync(TimeSpan.FromSeconds(5));
    }

    private sealed class CountingSaves : ICharacterSaves
    {
        public int Count;

        public void Enqueue(PlayerSession session, CharacterSaveSnapshot snapshot) => Count++;

        public Task<bool> SaveAsync(PlayerSession session, CharacterSaveSnapshot snapshot) => Task.FromResult(true);
    }
}
