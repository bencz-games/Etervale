// The two-handed list is item data; edit entries, not structure.
using PristonTale.Simulation.Characters;

namespace PristonTale.Simulation.Items;

// Equipment slot and kind of every wearable family by its three-character catalog prefix, or by its first two characters
// when every family of that group is worn the same way (all body armours and robes are "DA"). Rings fit either ring slot
// and two-handed weapons also hold the off hand.
public static class ItemEquipmentMetadata
{
    private static readonly Dictionary<string, (ItemKind Kind, EquipmentSlot Slot)> Families = new(StringComparer.OrdinalIgnoreCase)
    {
        ["WA1"] = (ItemKind.Axe, EquipmentSlot.Weapon),
        ["WC1"] = (ItemKind.Claw, EquipmentSlot.Weapon),
        ["WH1"] = (ItemKind.Hammer, EquipmentSlot.Weapon),
        ["WM1"] = (ItemKind.Staff, EquipmentSlot.Weapon),
        ["WP1"] = (ItemKind.Pole, EquipmentSlot.Weapon),
        ["WS1"] = (ItemKind.Bow, EquipmentSlot.Weapon),
        ["WS2"] = (ItemKind.Sword, EquipmentSlot.Weapon),
        ["WT1"] = (ItemKind.ThrowingWeapon, EquipmentSlot.Weapon),
        ["WD1"] = (ItemKind.Dagger, EquipmentSlot.Weapon),
        ["WN1"] = (ItemKind.Phantom, EquipmentSlot.Weapon),
        ["WV1"] = (ItemKind.Vambrace, EquipmentSlot.Weapon),
        ["DA"] = (ItemKind.Armor, EquipmentSlot.Armor),
        ["DB"] = (ItemKind.Boots, EquipmentSlot.Boots),
        ["DG"] = (ItemKind.Gloves, EquipmentSlot.Gloves),
        ["DS"] = (ItemKind.Shield, EquipmentSlot.OffHand),
        ["OM1"] = (ItemKind.Orb, EquipmentSlot.OffHand),
        ["OA1"] = (ItemKind.Amulet, EquipmentSlot.Amulet),
        ["OA2"] = (ItemKind.Bracelet, EquipmentSlot.Bracelet),
        ["OR1"] = (ItemKind.Ring, EquipmentSlot.RingLeft),
        ["OR2"] = (ItemKind.Ring, EquipmentSlot.RingLeft),
        ["OS1"] = (ItemKind.Sheltom, EquipmentSlot.Sheltom)
    };

    private static readonly HashSet<string> TwoHandedKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "WA105", "WA108", "WA109", "WA110", "WA112", "WA113", "WA115", "WA116", "WA117", "WA119", "WA120", "WA121",
        "WA122", "WA123", "WA124", "WA126", "WA127", "WA128", "WA129", "WA130", "WA131", "WA132", "WA151", "WA152",
        "WA153",
        "WH106", "WH107", "WH110", "WH111", "WH114", "WH115", "WH116", "WH118", "WH119", "WH121", "WH122", "WH123",
        "WH124", "WH125", "WH127", "WH128", "WH129", "WH130", "WH131", "WH132", "WH133", "WH152", "WH153",
        "WM104", "WM105", "WM108", "WM109", "WM110", "WM112", "WM114", "WM116", "WM118", "WM119", "WM121", "WM123",
        "WM124", "WM125", "WM127", "WM128", "WM129", "WM130", "WM131", "WM132", "WM133", "WM153",
        "WP104", "WP105", "WP106", "WP107", "WP108", "WP109", "WP110", "WP111", "WP112", "WP113", "WP114", "WP115",
        "WP116", "WP117", "WP118", "WP119", "WP120", "WP121", "WP122", "WP123", "WP124", "WP125", "WP126", "WP127",
        "WP128", "WP129", "WP130", "WP131", "WP132", "WP151", "WP152", "WP153",
        "WS101", "WS104", "WS105", "WS106", "WS107", "WS108", "WS110", "WS111", "WS112", "WS113", "WS114", "WS115",
        "WS116", "WS117", "WS118", "WS119", "WS120", "WS121", "WS122", "WS123", "WS124", "WS125", "WS126", "WS127",
        "WS128", "WS129", "WS130", "WS131", "WS132", "WS133", "WS151", "WS152", "WS153",
        "WS205", "WS210", "WS213", "WS215", "WS216", "WS218", "WS219", "WS221", "WS223", "WS224", "WS225", "WS226",
        "WS227", "WS229", "WS230", "WS231", "WS232", "WS233", "WS234", "WS235", "WS253", "WS254"
    };

    public static bool IsTwoHanded(string catalogKey) => TwoHandedKeys.Contains(catalogKey);

    private static readonly Dictionary<string, (ItemKind Kind, EquipmentSlot Slot)>.AlternateLookup<ReadOnlySpan<char>> FamilySpans =
        Families.GetAlternateLookup<ReadOnlySpan<char>>();

    public static bool IsShield(string catalogKey) => KindOf(catalogKey) == ItemKind.Shield;

    // The kind of a catalog key's family (its three-letter family, else its two-letter one); null for none. It allocates
    // nothing, so frame code may call it.
    public static ItemKind? KindOf(string catalogKey) => TryGet(catalogKey, out var kind, out _) ? kind : null;

    public static bool TryGet(string catalogKey, out ItemKind kind, out EquipmentSlot slot)
    {
        kind = default;
        slot = default;
        if (catalogKey is null || catalogKey.Length < 3
            || !FamilySpans.TryGetValue(catalogKey.AsSpan(0, 3), out var family) && !FamilySpans.TryGetValue(catalogKey.AsSpan(0, 2), out family))
        {
            return false;
        }

        (kind, slot) = family;
        return true;
    }

    public static ItemDefinition Apply(ItemDefinition definition)
    {
        if (!TryGet(definition.CatalogKey, out var kind, out var slot))
        {
            return definition;
        }

        return definition with { Kind = kind, EquipSlot = slot, TwoHanded = TwoHandedKeys.Contains(definition.CatalogKey) };
    }

    // The slot an item goes to when it is equipped without naming one: the left ring slot first, then the right.
    public static EquipmentSlot? DefaultSlot(ItemKind kind, EquipmentSlot? slot, Func<EquipmentSlot, bool> occupied)
    {
        if (slot is not { } target)
        {
            return null;
        }

        if (kind == ItemKind.Ring)
        {
            return occupied(EquipmentSlot.RingLeft) && !occupied(EquipmentSlot.RingRight) ? EquipmentSlot.RingRight : EquipmentSlot.RingLeft;
        }

        return target;
    }

    // Whether an item goes in the slot: its own slot, or either ring slot for a ring.
    public static bool Fits(ItemKind kind, EquipmentSlot? itemSlot, EquipmentSlot slot) =>
        itemSlot == slot || kind == ItemKind.Ring && slot is EquipmentSlot.RingLeft or EquipmentSlot.RingRight;

    // The same for a view that knows only the item's kind.
    public static bool Fits(ItemKind kind, EquipmentSlot slot) => Fits(kind, SlotOf(kind), slot);

    // The slot of an item kind, for views that know only the kind.
    public static EquipmentSlot? SlotOf(ItemKind kind) => kind switch
    {
        ItemKind.Sword or ItemKind.Axe or ItemKind.Hammer or ItemKind.Claw or ItemKind.Pole or ItemKind.Bow or ItemKind.ThrowingWeapon
            or ItemKind.Staff or ItemKind.Dagger or ItemKind.Phantom or ItemKind.Vambrace => EquipmentSlot.Weapon,
        ItemKind.Armor => EquipmentSlot.Armor,
        ItemKind.Shield or ItemKind.Orb => EquipmentSlot.OffHand,
        ItemKind.Gloves => EquipmentSlot.Gloves,
        ItemKind.Boots => EquipmentSlot.Boots,
        ItemKind.Bracelet => EquipmentSlot.Bracelet,
        ItemKind.Amulet => EquipmentSlot.Amulet,
        ItemKind.Ring => EquipmentSlot.RingLeft,
        ItemKind.Sheltom => EquipmentSlot.Sheltom,
        _ => null
    };
}
