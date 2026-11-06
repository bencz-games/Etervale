using PristonTale.Application.Saves;
using PristonTale.Application.Sessions;
using PristonTale.Application.World;
using PristonTale.Simulation.Quests;

namespace PristonTale.Application.Services;

// Quest requests and the quest upkeep outside the tick: the world prepares on the tick and commits, and the character is
// stored in the background (DurableOperations). A request that changes nothing answers without a save.
public sealed class QuestAppService(ISessionRegistry sessions, ICharacterSaves saves, WorldServer world)
{
    // How long the upkeep waits for the save gate before trying again later.
    private static readonly TimeSpan UpkeepGateWait = TimeSpan.FromSeconds(2);

    private readonly DurableOperations _durable = new(saves);

    public async Task<QuestOutcome> RequestAsync(string sessionToken, QuestCommand command, CancellationToken ct = default)
    {
        using var lease = await _durable.AdmitAsync(sessions, sessionToken, ct);
        return await lease.RunAsync(
            session => world.PrepareQuestAsync(session, command),
            world.CommitQuestAsync,
            prepared => prepared.Outcome,
            failure => QuestOutcome.Failed(failure == DurableFailure.Busy ? QuestStatus.Busy : QuestStatus.Rejected, DurableFailures.Text(failure)));
    }

    // Applies the quest work the tick queued for a player (quest drops, weapon growth, expired items) and queues the
    // character for storage. Returns false when the work is still pending: the save gate stayed busy, and the field
    // signals it again.
    public async Task<bool> RunUpkeepAsync(PlayerSession session, CancellationToken ct = default)
    {
        using var lease = await _durable.AdmitAsync(session, UpkeepGateWait, ct);
        if (lease.Failure is not null)
        {
            await world.DeferQuestUpkeepAsync(session);
            return false;
        }

        return await lease.RunAsync(
            world.PrepareQuestUpkeepAsync,
            world.CommitQuestAsync,
            _ => true,
            failure => failure == DurableFailure.NotPlaying);
    }
}
