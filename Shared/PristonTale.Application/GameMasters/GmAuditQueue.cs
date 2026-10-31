using PristonTale.Application.GameLogs;
using PristonTale.Persistence.Abstractions.GameMasters;

namespace PristonTale.Application.GameMasters;

// Where game-master commands record what they did; it must return at once, so a slow or locked database never holds the
// game master's next command.
public interface IGmAudit
{
    void Append(GmAuditEntry entry);
}

// The audit, written in the background one row at a time (the repository stores row by row, so a retried batch never
// stores a row twice).
public sealed class GmAuditQueue : IGmAudit
{
    private const int RowsPerWrite = 1;

    private readonly LogRowQueue<GmAuditEntry> _entries;

    public GmAuditQueue(IGameMasterRepository repository, TimeSpan? firstBackoff = null, Action<Exception>? failed = null)
    {
        _entries = new LogRowQueue<GmAuditEntry>(batch => repository.AppendAuditAsync(batch[0], CancellationToken.None), RowsPerWrite, firstBackoff)
        {
            Failed = failed
        };
    }

    public GameLogStats Stats => _entries.Stats;

    public void Append(GmAuditEntry entry) => _entries.Add(entry);

    public Task RunAsync(CancellationToken stop) => _entries.RunAsync(stop);
}
