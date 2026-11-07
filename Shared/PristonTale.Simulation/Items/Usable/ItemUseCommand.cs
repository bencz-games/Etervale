using PristonTale.Simulation.Characters;
using PristonTale.Contracts.Texts;

namespace PristonTale.Simulation.Items.Usable;

// One use of an owned item, built from the inventory revision the client saw. Argument names a travel destination;
// Text is the line a firecracker shows.
public sealed record ItemUseCommand(Guid OperationId, long ExpectedRevision, Guid ItemId, int Argument = 0, string Text = "");

// Wire values of the use result; append only.
public enum ItemUseStatus : ushort
{
    Used = 0,
    NotUsable = 1,
    Stale = 2,
    Busy = 3,
    AlreadyRunning = 4,
    SameAttribute = 5,
    NotHere = 6,
    LevelTooLow = 7,
    NothingToChange = 8,
    Rejected = 9,
    StorageFailed = 10,
    Dead = 11,
    Delay = 12,
    Conflict = 13,
    ClanOnly = 14,
    NoSpace = 15,
    OverWeight = 16,
    MaleCannotUse = 17,
    FemaleCannotUse = 18,
    NotEnoughGold = 19,
    CrystalRefused = 20,
    SummonLimit = 21,
    RegisteredClanOnly = 22,
    UnionCoreOwn = 23,
    UnionCoreAway = 24,
    UnionCoreWrongClan = 25,
    ClanPetOwned = 26,
    ClanPetsNearby = 27,
    MercenaryLimit = 28
}

public sealed record ItemUseOutcome(bool Ok, long Revision, ItemUseStatus Status, uint ItemCode, GameText Message)
{
    public static ItemUseOutcome Refused(long revision, ItemUseStatus status, uint itemCode = 0) =>
        new(false, revision, status, itemCode, ItemUseMessages.For(status));
}

public sealed record AppliedItemUse(ItemUseCommand Command, long Revision, ItemUseOutcome Outcome) : IAppliedOperation
{
    Guid IAppliedOperation.OperationId => Command?.OperationId ?? Guid.Empty;
}

