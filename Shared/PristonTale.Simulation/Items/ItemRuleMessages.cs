namespace PristonTale.Simulation.Items;

// The texts a player sees when an item rule refuses an action, as the original client words them.
public static class ItemRuleMessages
{
    public const string OverWeight = "You have exceeded the inventory weight limit";
    public const string CannotUse = "Item cannot be used yet";
    public const string CannotDrop = "This item cannot be dropped";
    public const string PotionNotStored = "Potions cannot be stored";
    public const string NotEnoughGold = "Not enough gold";
    public const string NoSpace = "Not enough space in inventory";
    public const string GoldLimit = "Exceeds maximum amount of gold in possession";
    public const string TradeStorageFailed = "storage rejected the trade; nothing changed";
    public const string StaleInventory = "invalid operation or stale inventory revision";
}
