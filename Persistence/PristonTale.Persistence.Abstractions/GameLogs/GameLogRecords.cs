namespace PristonTale.Persistence.Abstractions.GameLogs;

// How an item or gold changed hands (item_transfers.kind; append only).
public enum ItemTransferKind : short
{
    Trade = 0,
    PersonalShop = 1,
    WarehouseDeposit = 2,
    WarehouseWithdrawal = 3,
    GroundThrow = 4,
    GroundPickup = 5,
    GameMasterGift = 6,
    CastleFountain = 7
}

// One item, part of a stack, or an amount of gold (ItemCode null) that moved. A null character is the other end the kind
// names: the account warehouse (AccountId), the ground (FieldId), a game master or Bless Castle's soul fountain.
// TransferId groups the rows of one transfer; a throw and the pickup of what it threw share the ground drop's id.
public sealed record ItemTransferRecord(
    DateTime OccurredAt,
    ItemTransferKind Kind,
    string TransferId,
    long? FromCharacterId,
    long? ToCharacterId,
    long? AccountId,
    int FieldId,
    string? ItemCode,
    string? InstanceId,
    long Amount);

// The gold one sink took out of the economy during the period ending at OccurredAt (Sink: CurrencySink).
public sealed record CurrencySinkRecord(DateTime OccurredAt, short Sink, long Gold);

public enum ServerEventKind : short
{
    Started = 0,
    Stopped = 1
}

public sealed record ServerEventRecord(DateTime OccurredAt, ServerEventKind Kind, string ServerName, string Detail);

// The game logs, written in the background (only inserted, and pruned by age).
public interface IGameLogRepository
{
    Task AppendItemTransfersAsync(IReadOnlyList<ItemTransferRecord> transfers, CancellationToken ct = default);

    Task AppendCurrencyAsync(IReadOnlyList<CurrencySinkRecord> sinks, CancellationToken ct = default);

    Task AppendServerEventAsync(ServerEventRecord serverEvent, CancellationToken ct = default);

    // Deletes every log row older than before; returns the rows deleted.
    Task<int> PruneAsync(DateTime before, CancellationToken ct = default);
}
