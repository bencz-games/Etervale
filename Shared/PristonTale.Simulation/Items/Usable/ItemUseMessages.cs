using PristonTale.Contracts.Texts;

namespace PristonTale.Simulation.Items.Usable;

// The line the player reads for each use result.
public static class ItemUseMessages
{
    public static GameText For(ItemUseStatus status) => status switch
    {
        ItemUseStatus.SummonLimit => GameText.Of(GameTextId.SummonLimit, MonsterCrystals.PetSlots),
        _ => GameText.Of(Id(status))
    };

    private static GameTextId Id(ItemUseStatus status) => status switch
    {
        ItemUseStatus.Used => GameTextId.ItemUsed,
        ItemUseStatus.NotUsable => GameTextId.ItemNotUsable,
        ItemUseStatus.Stale => GameTextId.InventoryChanged,
        ItemUseStatus.Busy => GameTextId.ActionPending,
        ItemUseStatus.AlreadyRunning => GameTextId.ItemAlreadyRunning,
        ItemUseStatus.SameAttribute => GameTextId.SameAttributeRunning,
        ItemUseStatus.NotHere => GameTextId.ItemNotHere,
        ItemUseStatus.LevelTooLow => GameTextId.LevelTooLow,
        ItemUseStatus.NothingToChange => GameTextId.NothingToChange,
        ItemUseStatus.Dead => GameTextId.DeadCannotUse,
        ItemUseStatus.Delay => GameTextId.UseDelay,
        ItemUseStatus.Conflict => GameTextId.OperationConflict,
        ItemUseStatus.StorageFailed => GameTextId.CharacterUnchanged,
        ItemUseStatus.ClanOnly => GameTextId.ClanOnly,
        ItemUseStatus.NoSpace => GameTextId.NoSpace,
        ItemUseStatus.OverWeight => GameTextId.OverWeight,
        ItemUseStatus.MaleCannotUse => GameTextId.MaleCannotUse,
        ItemUseStatus.FemaleCannotUse => GameTextId.FemaleCannotUse,
        ItemUseStatus.NotEnoughGold => GameTextId.NotEnoughGold,
        ItemUseStatus.CrystalRefused => GameTextId.CrystalRefused,
        _ => GameTextId.ItemNotUsed
    };
}
