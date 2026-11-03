using System.Text.Json;
using PristonTale.Application.Saves;
using PristonTale.Application.Sessions;
using PristonTale.Application.World;
using PristonTale.Persistence.Abstractions.Characters;
using PristonTale.Persistence.Abstractions.Warehouses;
using PristonTale.Simulation.World.Npcs;

namespace PristonTale.Application.Services;

// Typed NPC services outside the tick: the world validates and prepares, then commits (DurableOperations). A change to the
// account warehouse is stored with the character before its commit; any other change is stored in the background.
public sealed class NpcAppService(
    ISessionRegistry sessions,
    ICharacterSaves saves,
    IWarehouseRepository warehouses,
    WorldServer world,
    WorldDataHolder worldData)
{
    private readonly DurableOperations _durable = new(saves);

    public async Task<NpcServiceReply> RequestAsync(string sessionToken, NpcServiceCommand command, CancellationToken ct = default)
    {
        using var lease = await _durable.AdmitAsync(sessions, sessionToken, ct);
        if (lease.Failure is null && command.Service == NpcServiceKind.Warehouse && command.Action == NpcServiceAction.Open
            && await OpenWarehouseAsync(lease.Session, command, ct) is { } opened)
        {
            return opened;
        }

        return await lease.RunAsync(
            session => world.PrepareNpcServiceAsync(session, command),
            world.CommitNpcServiceAsync,
            prepared => prepared.Reply,
            failure => Failed(failure switch
            {
                DurableFailure.Busy => NpcServiceStatus.Busy,
                DurableFailure.StorageFailed => NpcServiceStatus.StorageFailed,
                DurableFailure.Conflict => NpcServiceStatus.Conflict,
                _ => NpcServiceStatus.Rejected
            }, DurableFailures.Message(failure)),
            StoreAsync,
            storeFirst: static prepared => prepared.Warehouse is not null);
    }

    // A change to the warehouse is stored with the character. A warehouse stored elsewhere since this session read it is
    // a conflict, and the session drops its copy.
    private async Task<DurableFailure?> StoreAsync(PlayerSession session, PreparedNpcOperation prepared)
    {
        if (await warehouses.SaveWithCharacterAsync(session.Stamp(prepared.Snapshot!), WarehouseStateCodec.Capture(session.AccountId, prepared.Warehouse!),
                prepared.ExpectedWarehouseRevision, CancellationToken.None))
        {
            return null;
        }

        session.Warehouse = null;
        return DurableFailure.Conflict;
    }

    // The warehouse is read from storage on every open, after the NPC and its reach are confirmed.
    private async Task<NpcServiceReply?> OpenWarehouseAsync(PlayerSession session, NpcServiceCommand command, CancellationToken ct)
    {
        var check = await world.CheckNpcServiceAsync(session, command);
        if (!check.Ok)
        {
            return new NpcServiceReply(check);
        }

        try
        {
            var record = await warehouses.FindAsync(session.AccountId, ct);
            session.Warehouse = WarehouseStateCodec.Restore(record, worldData.Current.Items);
            return null;
        }
        catch (Exception exception) when (exception is InvalidDataException or JsonException or ArgumentException)
        {
            session.Warehouse = null;
            return Failed(NpcServiceStatus.StorageFailed, "the saved warehouse cannot be read");
        }
    }

    private static NpcServiceReply Failed(NpcServiceStatus status, string message) => new(NpcServiceOutcome.Failed(status, message));
}
