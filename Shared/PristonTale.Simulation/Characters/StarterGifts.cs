using PristonTale.Contracts.Characters;
using PristonTale.Simulation.Items;
using PristonTale.Contracts.Texts;

namespace PristonTale.Simulation.Characters;

// The gifts a new character receives one at a time in its first seconds when the server gives them (the original's
// new-user present): its class's weapon, a body armour, an off-hand, then three Hopy crystals. Etervale gives perfect
// copies specialized for the class. The weapon is the first of the class's own family (the family the original's
// low-level presents give it); the armour is a robe for the classes that wear one; the off-hand is a shield or an orb the
// class can specialize, only beside a one-handed weapon, and a class without one skips it. A character created before
// the gifts existed counts as served.
public static class StarterGifts
{
    public const int Count = 6;
    public const int Weapon = 0;
    public const int Armour = 1;
    public const int OffHand = 2;
    public static readonly TimeSpan FirstDelay = TimeSpan.FromSeconds(5);
    public static readonly TimeSpan Interval = TimeSpan.FromSeconds(3);

    private static readonly string[] OffHands = ["DS101", "OM101"];

    public static string WeaponKey(PlayerClass playerClass) => playerClass switch
    {
        PlayerClass.Fighter => "WA101",
        PlayerClass.Mechanician => "WC101",
        PlayerClass.Archer => "WS101",
        PlayerClass.Pikeman => "WP101",
        PlayerClass.Atalanta => "WT101",
        PlayerClass.Knight => "WS201",
        PlayerClass.Magician or PlayerClass.Priestess => "WM101",
        PlayerClass.Assassin => "WD101",
        PlayerClass.Shaman => "WN101",
        PlayerClass.MartialArtist => "WV101",
        _ => throw new ArgumentOutOfRangeException(nameof(playerClass), playerClass, "A class without a starter weapon.")
    };

    // The catalog key of the gift at the index (0 to Count - 1); null when the class has none there.
    public static string ItemKey(ItemCatalog items, PlayerClass playerClass, int index) => index switch
    {
        Weapon => WeaponKey(playerClass),
        Armour => ItemClassRestrictions.WearsRobe(playerClass) ? "DA202" : "DA102",
        OffHand => OffHandKey(items, playerClass),
        _ => "GP101"
    };

    // The index of the next gift the class has from the received count on; Count when none is left.
    public static int Next(ItemCatalog items, PlayerClass playerClass, int received)
    {
        var index = received;
        while (index < Count && ItemKey(items, playerClass, index) is null)
        {
            index++;
        }

        return index;
    }

    // The perfect copy of the gift at the index, specialized for the class where the item takes it; null when the class
    // has no gift there or the catalog lacks it.
    public static ItemInstance Create(ItemCatalog items, PlayerClass playerClass, int index)
    {
        if (ItemKey(items, playerClass, index) is not { } key || !items.TryGet(ItemCodes.Parse(key), out var definition))
        {
            return null;
        }

        var spec = (uint)JobBits.Of(playerClass);
        return ItemRoller.CreatePerfect(definition, 1, ItemRoller.CanTakeSpec(definition, spec) ? spec : 0);
    }

    public static GameText Message(string itemName) => GameText.Of(GameTextId.StarterGift, itemName);

    private static string OffHandKey(ItemCatalog items, PlayerClass playerClass)
    {
        if (!items.TryGet(ItemCodes.Parse(WeaponKey(playerClass)), out var weapon) || weapon.TwoHanded)
        {
            return null;
        }

        var spec = (uint)JobBits.Of(playerClass);
        return OffHands.FirstOrDefault(key => items.TryGet(ItemCodes.Parse(key), out var definition) && ItemRoller.CanTakeSpec(definition, spec));
    }
}
