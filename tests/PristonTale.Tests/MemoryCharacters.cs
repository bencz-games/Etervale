using PristonTale.Persistence.Abstractions.Characters;

namespace PristonTale.Tests;

internal sealed class MemoryCharacters(CharacterRecord initial) : ICharacterRepository
{
    public CharacterRecord Record { get; private set; } = initial;
    public Func<CharacterRecord, Task<bool>>? BeforeUpdate { get; set; }
    public int UpdateCount { get; private set; }

    // Records the next reads return before Record: what storage held before a write that lands meanwhile.
    public Queue<CharacterRecord> EarlierReads { get; } = new();

    public Task<CharacterRecord?> FindByNameForAccountAsync(long accountId, string name, CancellationToken ct = default)
    {
        var read = EarlierReads.TryDequeue(out var earlier) ? earlier : Record;
        return Task.FromResult<CharacterRecord?>(read.AccountId == accountId && read.Name == name ? read : null);
    }

    public async Task<bool> UpdateAsync(CharacterRecord record, CancellationToken ct = default)
    {
        UpdateCount++;
        if (BeforeUpdate is not null && !await BeforeUpdate(record) || record.SaveSeq < Record.SaveSeq)
        {
            return false;
        }

        Record = record;
        return true;
    }

    // A refused write throws, as storage that cannot take a batch does; while BeforeUpdate refuses the stored record,
    // storage is unreachable and even an empty write throws.
    public async Task<int> SaveAllAsync(IReadOnlyList<CharacterRecord> records, CancellationToken ct = default)
    {
        if (records.Count == 0 && BeforeUpdate is not null && !await BeforeUpdate(Record))
        {
            throw new InvalidOperationException("storage unreachable");
        }

        var written = 0;
        foreach (var record in records)
        {
            if (BeforeUpdate is not null && !await BeforeUpdate(record))
            {
                throw new InvalidOperationException("storage refused the batch");
            }

            if (record.SaveSeq >= Record.SaveSeq)
            {
                Record = record;
                written++;
            }
        }

        UpdateCount++;
        return written;
    }

    // One character's rows only: a trade between two characters needs MemoryCharacterBook.
    public Task<bool> UpdateAllAsync(IReadOnlyList<CharacterRecord> records, CancellationToken ct = default) =>
        records.Count == 1 ? UpdateAsync(records[0], ct) : throw new NotSupportedException();

    public Task<IReadOnlyList<CharacterRecord>> ListByAccountAsync(long accountId, CancellationToken ct = default)
    {
        return Task.FromResult<IReadOnlyList<CharacterRecord>>([Record]);
    }

    public Task<bool> NameExistsAsync(string name, CancellationToken ct = default) => Task.FromResult(Record.Name == name);
    public Task<IReadOnlyList<short>> UsedSlotsAsync(long accountId, CancellationToken ct = default)
    {
        return Task.FromResult<IReadOnlyList<short>>([Record.Slot]);
    }

    public Task<CharacterRecord> CreateAsync(CharacterRecord record, CancellationToken ct = default) => throw new NotSupportedException();
    public bool Deleted { get; private set; }

    public Task<bool> DeleteByNameAsync(long accountId, string name, CancellationToken ct = default)
    {
        Deleted = !Deleted && Record.AccountId == accountId && Record.Name == name;
        return Task.FromResult(Deleted);
    }
}
