using PristonTale.Simulation.Items;

namespace PristonTale.Simulation.Characters;

// The original's two sides (its E and W keys): a second bag page, swapped whole with the shown one, and a second weapon
// and off-hand pair, swapped with the held one. What is hidden is still carried: it weighs and it is saved.
public sealed partial class PlayerCharacter
{
    public const int Sides = 2;
    private ItemInstance _storedWeapon;
    private ItemInstance _storedOffHand;

    // The page not shown (Inventory is the shown one), and which page (0 A, 1 B) is shown.
    public Inventory HiddenBag { get; private set; } = new();

    public int BagPage { get; private set; }

    // The pair not held, and which pair (0 A, 1 B) is held.
    public ItemInstance StoredWeapon => _storedWeapon;
    public ItemInstance StoredOffHand => _storedOffHand;
    public int WeaponSet { get; private set; }

    // An item on the cursor (carried, by its id; empty for none) goes along to the page shown: it is laid on that page's
    // first free cells, as the cursor keeps it there until it is set down. No room there refuses the switch.
    public bool TrySwitchBagPage(int page, Guid carried = default)
    {
        if (page is < 0 or >= Sides || page == BagPage)
        {
            return false;
        }

        if (carried != Guid.Empty)
        {
            var anchor = Inventory.Find(carried);
            if (anchor < 0)
            {
                return false;
            }

            var definition = Inventory[anchor].Definition;
            if (!HiddenBag.HasRoom(definition.GridWidth, definition.GridHeight))
            {
                return false;
            }

            HiddenBag.TryPlaceFirstFree(Inventory.TakeAt(anchor));
        }

        Inventory.Changed -= OnInventoryChanged;
        (Inventory, HiddenBag) = (HiddenBag, Inventory);
        Inventory.Changed += OnInventoryChanged;
        BagPage = page;
        OnInventoryChanged();
        return true;
    }

    // The held pair is stored and the stored one held; a pair equipped together holds together again.
    public bool TrySwitchWeaponSet(int set)
    {
        if (set is < 0 or >= Sides || set == WeaponSet)
        {
            return false;
        }

        var weapon = GetEquipped(EquipmentSlot.Weapon);
        var offHand = GetEquipped(EquipmentSlot.OffHand);
        Equip(EquipmentSlot.Weapon, null);
        Equip(EquipmentSlot.OffHand, null);
        Equip(EquipmentSlot.Weapon, _storedWeapon);
        Equip(EquipmentSlot.OffHand, _storedOffHand);
        (_storedWeapon, _storedOffHand) = (weapon, offHand);
        WeaponSet = set;
        OnInventoryChanged();
        return true;
    }

    // As saved: the shown page and held pair, then the hidden page's items and the stored pair.
    public void RestoreSides(int page, int set)
    {
        if (page is < 0 or >= Sides || set is < 0 or >= Sides)
        {
            throw new ArgumentOutOfRangeException(nameof(page));
        }

        BagPage = page;
        WeaponSet = set;
    }

    // A saved item of the stored pair, held by the same rules as the held pair.
    public bool RestoreStored(EquipmentSlot slot, ItemInstance item)
    {
        var empty = slot switch
        {
            EquipmentSlot.Weapon => _storedWeapon is null,
            EquipmentSlot.OffHand => _storedOffHand is null,
            _ => false
        };
        if (!empty || !Fits(slot, item) || !PairAllows(slot, item, _storedWeapon, _storedOffHand))
        {
            return false;
        }

        if (slot == EquipmentSlot.Weapon)
        {
            _storedWeapon = item;
        }
        else
        {
            _storedOffHand = item;
        }

        return true;
    }

    private void CopySides(PlayerCharacter source)
    {
        HiddenBag.CopyFrom(source.HiddenBag);
        _storedWeapon = source._storedWeapon?.Copy(1, preserveId: true);
        _storedOffHand = source._storedOffHand?.Copy(1, preserveId: true);
        BagPage = source.BagPage;
        WeaponSet = source.WeaponSet;
        Held = source.Held?.Copy(source.Held.Count, preserveId: true);
    }

    // Takes up to amount units of the code from the stored weapon pair and the hand, the owned items the bag pages, the
    // box and the equipment leave out; the units taken.
    public int TakeStoredOrHeld(uint code, int amount)
    {
        var taken = 0;
        if (taken < amount && _storedWeapon?.Definition.Code == code)
        {
            _storedWeapon = null;
            taken++;
        }

        if (taken < amount && _storedOffHand?.Definition.Code == code)
        {
            _storedOffHand = null;
            taken++;
        }

        if (taken < amount && Held?.Definition.Code == code)
        {
            var take = Math.Min(Held.Count, amount - taken);
            Held.Count -= take;
            Held = Held.Count > 0 ? Held : null;
            taken += take;
        }

        if (taken > 0)
        {
            OnInventoryChanged();
        }

        return taken;
    }

    // Takes the stored weapon, stored off-hand or held item with this id.
    public bool TakeStoredOrHeld(Guid instanceId)
    {
        if (_storedWeapon?.InstanceId == instanceId)
        {
            _storedWeapon = null;
        }
        else if (_storedOffHand?.InstanceId == instanceId)
        {
            _storedOffHand = null;
        }
        else if (Held?.InstanceId == instanceId)
        {
            Held = null;
        }
        else
        {
            return false;
        }

        OnInventoryChanged();
        return true;
    }

    private IEnumerable<ItemInstance> HiddenItems()
    {
        foreach (var item in HiddenBag.Items())
        {
            yield return item;
        }

        if (_storedWeapon is not null)
        {
            yield return _storedWeapon;
        }

        if (_storedOffHand is not null)
        {
            yield return _storedOffHand;
        }

        if (Held is not null)
        {
            yield return Held;
        }
    }
}
