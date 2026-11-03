using PristonTale.Simulation.Characters;
using PristonTale.Simulation.Combat;

namespace PristonTale.Simulation.Items;

public enum ItemKind
{
    Sword, Axe, Hammer, Claw, Pole, Bow, ThrowingWeapon, Staff,
    Armor, Shield, Gloves, Boots,
    Amulet, Bracelet, Ring, ForceOrb, Sheltom,
    Potion, Material, Quest, Misc,
    // Keep new values appended: ItemKind is persisted as an integer in world-data rows.
    Dagger, Phantom, Orb, Vambrace
}

public readonly record struct ItemStatBlock(
    int DamageMin,
    int DamageMax,
    int Defence,
    int AttackRating,
    int AttackSpeed,
    int Strength,
    int Spirit,
    int Talent,
    int Agility,
    int Health)
{
    public static readonly ItemStatBlock Zero = default;

    public ItemStatBlock Add(in ItemStatBlock o) => new(
        DamageMin + o.DamageMin,
        DamageMax + o.DamageMax,
        Defence + o.Defence,
        AttackRating + o.AttackRating,
        AttackSpeed + o.AttackSpeed,
        Strength + o.Strength,
        Spirit + o.Spirit,
        Talent + o.Talent,
        Agility + o.Agility,
        Health + o.Health);
}

public sealed record ItemDefinition(
    uint Code,
    string Name,
    ItemKind Kind,
    EquipmentSlot? EquipSlot = null,
    bool TwoHanded = false,
    int GridWidth = 1,
    int GridHeight = 1,
    int MaxStack = 1)
{
    public string CatalogKey { get; init; } = "";
    public PotionDefinition Potion { get; init; }
    public int ShootingRange { get; init; }
    public uint SpecializationJobs { get; init; }
    public int RequiredLevel { get; init; }
    public CharacterAttributes Requirements { get; init; } = CharacterAttributes.Empty;

    public uint AllowedJobs { get; init; }

    // The stats of the item as a shop sells it; dropped copies roll their own values from Template.
    public ItemStatBlock BaseStats { get; init; } = ItemStatBlock.Zero;

    public int MaxDurability { get; init; }
    public int Weight { get; init; }
    public int Price { get; init; }

    public ResistanceBlock Resistances { get; init; } = ResistanceBlock.Zero;

    public int Critical { get; init; }
    public int PotionSpace { get; init; }
    public float MagicMastery { get; init; }

    // 1 marks a unique item; 2 also makes the weapon count as the Mechanician's own weapon.
    public int UniqueItem { get; init; }

    // Authored value ranges; null for definitions built without authored data (every range then collapses to the base).
    public ItemTemplate Template { get; init; }

    // The glow a unique weapon shows in the hand (colour, blink period in ticks, scale pulse); Alpha 0 with no colour shows none.
    public ItemGlow Glow { get; init; } = ItemGlow.None;

    // The weapon's effect setting: a non-zero value shows the class-up light on the weapon while it is held.
    public int DisplayEffect { get; init; }

    // Drops of the item per day across the server; 0 is unlimited.
    public int DailyDropLimit { get; init; }

    // The item set it belongs to (0 none) and that set, attached by the catalog.
    public int SetCode { get; init; }
    public ItemSetDefinition Set { get; init; }

    // A named unique of the code (1, 2...): the variant a saved item names with NamedUniqueBase + this number.
    public int UniqueVariant { get; init; }

    // The unique marker of a code's quest variant (UniqueItem), kept apart from the regular definition.
    public const int QuestUnique = 2;

    // Saved items name a named unique by this base plus its variant number.
    public const int NamedUniqueBase = 1000;

    public int SavedUnique => UniqueVariant > 0 ? NamedUniqueBase + UniqueVariant : UniqueItem == QuestUnique ? QuestUnique : 0;

    public bool IsEquippable => EquipSlot.HasValue;
    public bool IsStackable => MaxStack > 1;

    public bool IsWeapon => ItemEquipmentMetadata.SlotOf(Kind) == EquipmentSlot.Weapon;
}

public sealed class ItemInstance
{
    private ItemRoll _roll;

    public ItemInstance(ItemDefinition definition, int count = 1)
    {
        Definition = definition;
        Count = count;
        _roll = ItemRoller.Lower(definition);
        var durability = ItemRoller.LowerDurability(definition);
        DurabilityMax = durability;
        DurabilityCurrent = durability;
    }

    public Guid InstanceId { get; init; } = Guid.NewGuid();
    public ItemDefinition Definition { get; }

    public ItemInstance Copy(int count, bool preserveId = false) => Copy(count, preserveId ? InstanceId : Guid.NewGuid());

    // Takes `amount` off this stack (less than all of it) into a new stack.
    public ItemInstance Split(int amount)
    {
        if (amount < 1 || amount >= Count)
        {
            throw new ArgumentOutOfRangeException(nameof(amount));
        }

        Count -= amount;
        return Copy(amount);
    }

    public ItemInstance Copy(int count, Guid instanceId)
    {
        return new ItemInstance(Definition, count)
        {
            InstanceId = instanceId,
            UpgradeLevel = UpgradeLevel,
            DurabilityCurrent = DurabilityCurrent,
            DurabilityMax = DurabilityMax,
            Roll = Roll,
            Sealed = Sealed,
            Maturity = Maturity,
            MixSheltom = MixSheltom,
            MixStats = MixStats
        };
    }

    public int Count { get; set; }

    public int UpgradeLevel { get; set; }

    // A dropped puzzle piece stays sealed as a lucky bag until its owner pays to reveal it.
    public bool Sealed { get; set; }

    public Aging.AgingMaturity Maturity { get; set; }

    // 1 Lucidy .. 9 Celesto; 0 never mixed.
    public int MixSheltom { get; set; }

    public Mixing.MixStat MixStats { get; set; }

    public bool Mixed => MixSheltom > 0;

    public int DurabilityCurrent { get; set; }
    public int DurabilityMax { get; set; }

    // Every value decided when the item was made: rolled stats, requirements, price and class specialization.
    public ItemRoll Roll
    {
        get => _roll;
        set => _roll = value ?? throw new ArgumentNullException(nameof(value));
    }

    public uint JobSpecMask => _roll.SpecJob;

    public string DisplayName => UpgradeLevel > 0 ? $"+{UpgradeLevel} {Definition.Name}" : Definition.Name;

    public ItemStatBlock TotalStats =>
        new(_roll.DamageMin, _roll.DamageMax, _roll.Defence, _roll.AttackRating, Definition.BaseStats.AttackSpeed + _roll.AttackSpeedBonus, 0, 0, 0, 0, 0);

    public int Critical => Definition.Critical + _roll.CriticalBonus;

    public float MagicMastery => Definition.MagicMastery + _roll.MagicMasteryBonus;

    public int Price => _roll.Price;
}
