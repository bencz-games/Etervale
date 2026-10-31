using System.Threading.Channels;
using PristonTale.Application.Services;

namespace PristonTale.Application.GameLogs;

public readonly record struct GameLogStats(long Written, long Failures, long Dropped);

// The background writer of a log's rows, so a log never holds the game or a player's commands. Rows wait in a bounded
// queue (Capacity); one writer stores up to batchSize at a time and retries a refused batch whole (RetryBackoff), so a
// writer that stores row by row takes batches of one. A full queue drops new rows and counts them. On stop, what is left
// is tried once, a batch at a time, and what storage refuses is counted as dropped.
public sealed class LogRowQueue<T>(Func<IReadOnlyList<T>, Task> write, int batchSize, TimeSpan? firstBackoff = null)
{
    public const int Capacity = 65536;

    private readonly Channel<T> _rows = Channel.CreateBounded<T>(
        new BoundedChannelOptions(Capacity) { FullMode = BoundedChannelFullMode.Wait, SingleReader = true });
    private long _written;
    private long _failures;
    private long _dropped;

    public Action<Exception>? Failed { get; init; }

    public GameLogStats Stats => new(Interlocked.Read(ref _written), Interlocked.Read(ref _failures), Interlocked.Read(ref _dropped));

    public void Add(T row)
    {
        if (!_rows.Writer.TryWrite(row))
        {
            Interlocked.Increment(ref _dropped);
        }
    }

    public async Task RunAsync(CancellationToken stop)
    {
        var backoff = new RetryBackoff(firstBackoff);
        var batch = new List<T>(batchSize);
        while (!stop.IsCancellationRequested)
        {
            if (batch.Count == 0)
            {
                try
                {
                    await _rows.Reader.WaitToReadAsync(stop);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }

            Fill(batch);
            if (await WriteAsync(batch))
            {
                batch.Clear();
                backoff.Reset();
                continue;
            }

            try
            {
                await Task.Delay(backoff.Next(), stop);
            }
            catch (OperationCanceledException)
            {
            }
        }

        _rows.Writer.TryComplete();
        do
        {
            Fill(batch);
            if (batch.Count > 0 && !await WriteAsync(batch))
            {
                Interlocked.Add(ref _dropped, batch.Count);
            }

            batch.Clear();
        }
        while (_rows.Reader.TryPeek(out _));
    }

    private void Fill(List<T> batch)
    {
        while (batch.Count < batchSize && _rows.Reader.TryRead(out var row))
        {
            batch.Add(row);
        }
    }

    private async Task<bool> WriteAsync(List<T> batch)
    {
        try
        {
            await write(batch);
            Interlocked.Add(ref _written, batch.Count);
            return true;
        }
        catch (Exception exception)
        {
            Interlocked.Increment(ref _failures);
            Failed?.Invoke(exception);
            return false;
        }
    }
}
