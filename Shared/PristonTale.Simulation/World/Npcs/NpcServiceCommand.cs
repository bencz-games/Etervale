using PristonTale.Simulation.Characters;
using PristonTale.Simulation.Items;
using PristonTale.Contracts.Texts;

namespace PristonTale.Simulation.World.Npcs;

// Persisted in the character's operation history and carried on the wire; append values only.
public enum NpcServiceAction : byte
{
    Open = 0,
    Close = 1,
    Refresh = 2,
    Buy = 10,
    Sell = 11,
    Repair = 12,
    RepairAll = 13,
    Deposit = 20,
    Withdraw = 21,
    Move = 22,
    DepositGold = 23,
    WithdrawGold = 24,
    Teleport = 30,
    // The puzzle collector takes a finished puzzle for a reward.
    Exchange = 40,

    Age = 50,

    // The mixing NPC: Mix works ItemId with sheltoms (or reconstructs it with seals); Refine turns sheltoms into force orbs.
    Mix = 51,
    Refine = 52
}

public enum NpcServiceStatus : ushort
{
    Ok = 0,
    NotAvailable = 1,
    OutOfReach = 2,
    Rejected = 3,
    Stale = 4,
    NotEnoughGold = 5,
    NoSpace = 6,
    GoldLimit = 7,
    Busy = 8,
    StorageFailed = 9,
    Conflict = 10,
    NotOpen = 11,
    OverWeight = 12,
    WrongPuzzle = 13,
    WrongMaterials = 14
}

// ItemCode names a stock entry to buy; ItemId an owned item. Anchor is a destination cell (-1 = first free) or, for a
// teleport, the destination index. Amount is gold for the gold actions. Materials are the owned items a work on ItemId
// uses up (one unit per entry). Covered is the one item a warehouse item set down lies over, exchanged with it.
public sealed record NpcServiceCommand(
    Guid OperationId,
    ulong NpcEntityId,
    NpcServiceKind Service,
    NpcServiceAction Action,
    long ExpectedRevision = 0,
    long ExpectedStorageRevision = 0,
    uint ItemCode = 0,
    Guid ItemId = default,
    int Anchor = -1,
    int Count = 1,
    long Amount = 0,
    Guid[] Materials = null,
    Guid Covered = default)
{
    public const int MaxMaterials = 16;

    // Value equality over the materials too, so a replayed operation id with other materials is a conflict.
    public bool Equals(NpcServiceCommand other) =>
        other is not null && OperationId == other.OperationId && NpcEntityId == other.NpcEntityId && Service == other.Service
        && Action == other.Action && ExpectedRevision == other.ExpectedRevision && ExpectedStorageRevision == other.ExpectedStorageRevision
        && ItemCode == other.ItemCode && ItemId == other.ItemId && Anchor == other.Anchor && Count == other.Count && Amount == other.Amount
        && Covered == other.Covered && (Materials ?? []).AsSpan().SequenceEqual(other.Materials ?? []);

    public override int GetHashCode() => HashCode.Combine(OperationId, NpcEntityId, Service, Action, ItemId, Anchor, Amount, Materials?.Length ?? 0);
}

public static class NpcServiceActions
{
    // Durable actions change the character or its storage and are written before they take effect.
    public static bool IsDurable(NpcServiceAction action) => action is not (NpcServiceAction.Open or NpcServiceAction.Close or NpcServiceAction.Refresh);
}

public sealed record AppliedServiceOperation(NpcServiceCommand Command, long Revision) : IAppliedOperation
{
    Guid IAppliedOperation.OperationId => Command?.OperationId ?? Guid.Empty;
}

public sealed record NpcServiceOutcome(bool Ok, NpcServiceStatus Status, long Revision, long StorageRevision, GameText Message)
{
    public static NpcServiceOutcome Failed(NpcServiceStatus status, GameText message, long revision = 0, long storageRevision = 0) =>
        new(false, status, revision, storageRevision, message);
}

public sealed record ShopOffer(uint Code, int Tab, int Ordinal, string Name, ItemKind Kind, long Price, int Width, int Height, int MaxStack,
    string CatalogKey = "");

public sealed record ShopQuote(Guid ItemId, long SellPrice, long RepairCost, int Durability, int MaxDurability);

public sealed record ShopQuotes(long InventoryRevision, IReadOnlyList<ShopQuote> Items);

public sealed record WarehouseView(long Revision, long Money, int Weight, int MaxWeight, int Columns, int Rows, IReadOnlyList<OwnedItemView> Items);

public sealed record TeleportOffer(int Index, int FieldCode, string Name, long Price, int MinimumLevel);

// The reply to one service request: the outcome plus the state the service shows after it.
public sealed record NpcServiceReply(NpcServiceOutcome Outcome)
{
    public IReadOnlyList<ShopOffer> Catalog { get; init; }
    public ShopQuotes Quotes { get; init; }
    public WarehouseView Warehouse { get; init; }
    public IReadOnlyList<TeleportOffer> Destinations { get; init; }
    public TeleportArrival Arrival { get; init; }
    public ItemWorkResult Work { get; init; }
}

// Result is AgingResult for the aging NPC; ItemId is empty when the item was lost.
public sealed record ItemWorkResult(byte Result, Guid ItemId, uint Code);

public sealed record TeleportArrival(int FieldCode, System.Numerics.Vector3 Position);
