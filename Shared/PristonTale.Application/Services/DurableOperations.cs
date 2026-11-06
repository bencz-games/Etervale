using PristonTale.Application.Saves;
using PristonTale.Application.Sessions;
using PristonTale.Application.World;
using PristonTale.Contracts.Texts;

namespace PristonTale.Application.Services;

// Why a durable operation did not run, or did not reach its commit.
public enum DurableFailure
{
    NoSession,
    Busy,
    NotPlaying,
    StorageFailed,
    // Storage holds a newer copy of something the operation read (the account warehouse).
    Conflict
}

public static class DurableFailures
{
    // The line a refused durable operation answers with.
    public static GameText Text(DurableFailure failure) => GameText.Of(failure switch
    {
        DurableFailure.NoSession => GameTextId.NoSession,
        DurableFailure.Busy => GameTextId.CharacterBusy,
        DurableFailure.StorageFailed => GameTextId.CharacterUnchanged,
        DurableFailure.Conflict => GameTextId.WarehouseChanged,
        _ => GameTextId.NotPlaying
    });
}

// The path of every character change. The session's save gate admits one change per character at a time (saves share
// it); under it the tick prepares and then commits. An ordinary change is stored in the background after its commit
// (ICharacterSaves); a transfer between characters, or to the ground or the account warehouse, is stored before its
// commit (a store), so no crash can keep an item or gold on both sides.
public sealed class DurableOperations(ICharacterSaves saves)
{
    public Task<DurableLease> AdmitAsync(ISessionRegistry sessions, string sessionToken, CancellationToken ct) =>
        sessions.TryGet(sessionToken, out var session)
            ? AdmitAsync(session, TimeSpan.Zero, ct)
            : Task.FromResult(DurableLease.Refused(DurableFailure.NoSession));

    public async Task<DurableLease> AdmitAsync(PlayerSession session, TimeSpan wait, CancellationToken ct) =>
        await session.SaveGate.WaitAsync(wait, ct) ? new DurableLease(saves, session) : DurableLease.Refused(DurableFailure.Busy);
}

// One admission to a session's save gate, released on Dispose. A refused lease runs nothing and answers its failure.
public sealed class DurableLease : IDisposable
{
    private readonly ICharacterSaves? _saves;
    private PlayerSession? _session;

    internal DurableLease(ICharacterSaves saves, PlayerSession session)
    {
        _saves = saves;
        _session = session;
    }

    private DurableLease(DurableFailure failure) => Failure = failure;

    public DurableFailure? Failure { get; }

    public PlayerSession Session => _session ?? throw new InvalidOperationException("The durable lease was refused or released.");

    internal static DurableLease Refused(DurableFailure failure) => new(failure);

    // Prepares on the tick; a preparation without a snapshot answers at once. Without a store (or when storeFirst says the
    // prepared change does not need one), the commit applies the change and the snapshot is queued for storage under the
    // gate, ahead of any later capture. A store writes before
    // the commit (null when stored) and runs to its end despite cancellation, so retrying the operation id recovers the
    // outcome; Abort runs whenever the store did not take the snapshot, a storage exception included.
    public async Task<TReply> RunAsync<TPrepared, TReply>(
        Func<PlayerSession, Task<TPrepared?>> prepare,
        Func<PlayerSession, TPrepared, Task> commit,
        Func<TPrepared, TReply> reply,
        Func<DurableFailure, TReply> failed,
        Func<PlayerSession, TPrepared, Task<DurableFailure?>>? store = null,
        Func<PlayerSession, TPrepared, Task>? abort = null,
        Func<TPrepared, bool>? storeFirst = null)
        where TPrepared : PreparedOperation
    {
        if (Failure is { } refused)
        {
            return failed(refused);
        }

        var session = Session;
        var prepared = await prepare(session);
        if (prepared is null)
        {
            return failed(DurableFailure.NotPlaying);
        }

        if (prepared.Snapshot is not { } snapshot)
        {
            return reply(prepared);
        }

        if (store is null || storeFirst?.Invoke(prepared) == false)
        {
            await commit(session, prepared);
            _saves!.Enqueue(session, snapshot);
            return reply(prepared);
        }

        DurableFailure? failure = DurableFailure.StorageFailed;
        try
        {
            failure = await store(session, prepared);
        }
        finally
        {
            if (failure is not null && abort is not null)
            {
                await abort(session, prepared);
            }
        }

        if (failure is { } notStored)
        {
            return failed(notStored);
        }

        await commit(session, prepared);
        return reply(prepared);
    }

    public void Dispose() => Interlocked.Exchange(ref _session, null)?.SaveGate.Release();
}
