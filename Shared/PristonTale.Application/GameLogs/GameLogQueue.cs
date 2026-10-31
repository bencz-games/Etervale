using PristonTale.Persistence.Abstractions.GameLogs;

namespace PristonTale.Application.GameLogs;

// Where the world reports the items and gold that changed hands (on the tick, so it must not block).
public interface IGameLog
{
    void ItemTransfers(IReadOnlyList<ItemTransferRecord> transfers);
}

// The item transfers, written in the background in batches of TransferBatch rows per transaction.
public sealed class GameLogQueue : IGameLog
{
    public const int Capacity = LogRowQueue<ItemTransferRecord>.Capacity;
    private const int TransferBatch = 500;

    private readonly LogRowQueue<ItemTransferRecord> _transfers;

    public GameLogQueue(IGameLogRepository logs, TimeSpan? firstBackoff = null, Action<Exception>? failed = null)
    {
        _transfers = new LogRowQueue<ItemTransferRecord>(batch => logs.AppendItemTransfersAsync(batch, CancellationToken.None), TransferBatch,
            firstBackoff) { Failed = failed };
    }

    public GameLogStats Stats => _transfers.Stats;

    public void ItemTransfers(IReadOnlyList<ItemTransferRecord> transfers)
    {
        foreach (var transfer in transfers)
        {
            _transfers.Add(transfer);
        }
    }

    public Task RunAsync(CancellationToken stop) => _transfers.RunAsync(stop);
}
