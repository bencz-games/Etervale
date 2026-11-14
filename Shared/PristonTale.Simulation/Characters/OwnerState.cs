using PristonTale.Simulation.Items;
using PristonTale.Simulation.Items.Usable;
using PristonTale.Simulation.Progression;

namespace PristonTale.Simulation.Characters;

public sealed record OwnedItemView(
    Guid InstanceId, uint Code, string Name, ItemKind Kind, int Count, int Anchor, int Slot, int Width, int Height, string CatalogKey = "")
{
    // The copy's values for tooltips and equipment rules; null until the owner receives them.
    public ItemDetailView Details { get; init; }

    // A bound Union Core's clan member (ItemInstance.Link).
    public string Link { get; init; } = "";

    // Where it is: the bag (Anchor), an equipment slot (Slot), a potion belt box (Slot from PotionBelt.FirstOwnerSlot), a
    // special item box (Slot from QuestItemBox.FirstOwnerSlot) or the hand (HeldSlot, PlayerCharacter.Held).
    public const int HeldSlot = QuestItemBox.FirstOwnerSlot + QuestItemBox.Boxes;

    public bool InBag => Slot < 0;
    public bool Equipped => Slot is >= 0 and < PotionBelt.FirstOwnerSlot;
    public int BeltBox => Slot is >= PotionBelt.FirstOwnerSlot and < QuestItemBox.FirstOwnerSlot ? Slot - PotionBelt.FirstOwnerSlot : -1;
    public int QuestBox => Slot is >= QuestItemBox.FirstOwnerSlot and < HeldSlot ? Slot - QuestItemBox.FirstOwnerSlot : -1;
    public bool Held => Slot == HeldSlot;

    // What a view shows of an item at a bag or grid anchor or an owner slot; a sealed puzzle piece shows as the lucky bag
    // until it is revealed.
    public static OwnedItemView Of(ItemInstance item, int anchor, int slot)
    {
        var definition = item.Definition;
        return item.Sealed
            ? new OwnedItemView(item.InstanceId, ItemCodes.Parse(Puzzles.LuckyBagIcon), Puzzles.LuckyBagName, definition.Kind, item.Count, anchor, slot,
                definition.GridWidth, definition.GridHeight, Puzzles.LuckyBagIcon)
            : new OwnedItemView(item.InstanceId, definition.Code, item.DisplayName, definition.Kind, item.Count, anchor, slot, definition.GridWidth,
                definition.GridHeight, definition.CatalogKey) { Link = item.Link };
    }

    // Whether a carried potion joins this stack: the same potion. The server joins only compatible stacks
    // (Inventory.TryJoin), which for potions, without durability or rolls, is the same rule.
    public bool Joins(OwnedItemView carried) => carried.Kind == ItemKind.Potion && carried.CatalogKey == CatalogKey;

    // The item of a view with this id, in this owner slot or in this belt box, or null; called every frame, so they do
    // not allocate.
    public static OwnedItemView Find(IReadOnlyList<OwnedItemView> items, Guid id) =>
        id == Guid.Empty ? null : First(items, static (item, id) => item.InstanceId == id, id);

    public static OwnedItemView InSlot(IReadOnlyList<OwnedItemView> items, int slot) => First(items, static (item, slot) => item.Slot == slot, slot);

    public static OwnedItemView InBox(IReadOnlyList<OwnedItemView> items, int box) => InSlot(items, PotionBelt.FirstOwnerSlot + box);

    private static OwnedItemView First<T>(IReadOnlyList<OwnedItemView> items, Func<OwnedItemView, T, bool> matches, T value)
    {
        for (var index = 0; index < (items?.Count ?? 0); index++)
        {
            if (matches(items[index], value))
            {
                return items[index];
            }
        }

        return null;
    }
}
public sealed record OwnedSkillView(uint Code, int Rank, int Uses = 0);

public sealed record AgingProgressView(Guid InstanceId, int Count, int Goal);

public sealed record OwnerState(
    int Level, ulong Experience, ulong LevelStart, ulong NextLevel,
    long Money, int Life, int MaxLife, int Mana, int MaxMana, int Stamina, int MaxStamina,
    int StatPoints, int SkillPoints, int Strength, int Spirit, int Talent, int Agility, int Health,
    int DamageMin, int DamageMax, int Defence, int Absorption, long InventoryRevision,
    IReadOnlyList<OwnedItemView> Items)
{
    public float AttackRange { get; init; } = Combat.MeleeReach.PlayerBaseRange;
    public int AttackRating { get; init; }
    public int MoveSpeed { get; init; }
    public long ProgressionRevision { get; init; }
    public long SkillsRevision { get; init; }
    // The bag page shown and the weapon pair held (0 A, 1 B; PlayerCharacter.BagPage, WeaponSet).
    public int BagPage { get; init; }
    public int WeaponSet { get; init; }
    public Guid CostumeItemId { get; init; }
    public string CostumeKey { get; init; } = "";

    // Class tier 0..3 and the points the final tier's skills spend.
    public int Tier { get; init; }
    public int EliteSkillPoints { get; init; }
    public IReadOnlyList<OwnedSkillView> Skills { get; init; } = Array.Empty<OwnedSkillView>();

    // What the worn items add beyond the status values above.
    public int CarriedWeight { get; init; }
    public int MaxWeight { get; init; }
    public int CriticalChance { get; init; }
    public int BlockChance { get; init; }
    public int ShootingRange { get; init; }
    public int AttackSpeed { get; init; }
    public int PotionSpace { get; init; } = DerivedStats.DefaultPotionSpace;
    public float LifeRegen { get; init; }
    public float ManaRegen { get; init; }
    public float StaminaRegen { get; init; }
    public float MagicMastery { get; init; }
    public Combat.ResistanceBlock Resistances { get; init; }

    public bool Overweight => CarriedWeight > MaxWeight;

    // The running premium timers, their revision and the world clock (Unix seconds) they were captured at.
    public long PremiumRevision { get; init; } = -1;
    public IReadOnlyList<PremiumTimer> PremiumTimers { get; init; } = Array.Empty<PremiumTimer>();
    public long PremiumClockSeconds { get; init; }

    public long AgingRevision { get; init; } = -1;
    public IReadOnlyList<AgingProgressView> Aging { get; init; } = Array.Empty<AgingProgressView>();

    // What every member is made of: the character's values and the revisions of what derives the rest (the inventory and
    // equipment, the effects, the skills, the premium timers, the aging counts). An owner whose stamp holds is unchanged,
    // so the capture the server makes every tick allocates nothing for it.
    private OwnerStamp Stamp { get; init; }

    private static IReadOnlyList<OwnedSkillView> SkillViews(PristonTale.Simulation.Skills.LearnedSkills skills) =>
        Array.AsReadOnly(skills.Ranks.Select(pair => new OwnedSkillView(pair.Key, pair.Value, skills.UsesOf(pair.Key))).ToArray());

    // An unchanged owner returns the previous instance (which the encoder skips); otherwise the state is built in full and
    // compared as a record, the lists reused while their revision holds.
    public static OwnerState Capture(PlayerCharacter character, OwnerState previous = null, long nowSeconds = 0,
        CostumeCatalog costumes = null)
    {
        var attributes = character.Attributes;
        _ = character.Derived;
        var stamp = new OwnerStamp(character.Level, character.Experience, character.Money, character.Life, character.Mana, character.Stamina,
            character.StatPoints, character.SkillPoints, character.EliteSkillPoints, character.Tier, character.BagPage, character.WeaponSet, attributes,
            character.InventoryRevision, character.ProgressionRevision, character.Skills.Revision, character.Premium.Revision, character.AgingRevision,
            character.Effects.Revision);
        if (previous is not null && previous.Stamp == stamp)
        {
            return previous;
        }

        var items = previous is not null && previous.InventoryRevision == character.InventoryRevision ? previous.Items : ProjectItems(character);
        var damage = Combat.WeaponDamage.ComputeForHit(character);
        var current = new OwnerState(character.Level, character.Experience,
            ExperienceTable.ForLevel(Math.Clamp(character.Level, 1, ExperienceTable.MaximumLevel)),
            ExperienceTable.ForLevel(Math.Clamp(character.Level + 1, 1, ExperienceTable.MaximumLevel)),
            character.Money, character.Life, character.MaxLife, character.Mana, character.MaxMana, character.Stamina, character.MaxStamina,
            character.StatPoints, character.SkillPoints, attributes.Strength, attributes.Spirit, attributes.Talent, attributes.Agility, attributes.Health,
            damage.Min, damage.Max, character.Derived.Defence, character.Derived.Absorption, character.InventoryRevision, items)
        {
            AttackRange = Combat.PlayerAttackReach.For(character),
            AttackRating = character.Derived.AttackRating,
            MoveSpeed = character.Derived.MoveSpeed,
            ProgressionRevision = character.ProgressionRevision,
            SkillsRevision = character.Skills.Revision,
            Tier = character.Tier,
            BagPage = character.BagPage,
            WeaponSet = character.WeaponSet,
            CostumeItemId = character.ActiveCostumeItemId(costumes ?? CostumeCatalog.Empty),
            CostumeKey = character.ActiveCostumeKey(costumes ?? CostumeCatalog.Empty),
            EliteSkillPoints = character.EliteSkillPoints,
            Skills = previous is not null && previous.SkillsRevision == character.Skills.Revision ? previous.Skills : SkillViews(character.Skills),
            CarriedWeight = character.Derived.CarriedWeight,
            MaxWeight = character.Derived.MaxWeight,
            CriticalChance = Combat.CombatProfileFactory.CriticalChance(character),
            BlockChance = Combat.CombatProfileFactory.ForPlayerDefender(character).BlockChance,
            ShootingRange = character.Derived.ShootingRange,
            AttackSpeed = character.Derived.AttackSpeed,
            PotionSpace = character.Derived.PotionSpace,
            LifeRegen = character.Derived.LifeRegen,
            ManaRegen = character.Derived.ManaRegen,
            StaminaRegen = character.Derived.StaminaRegen,
            MagicMastery = character.Derived.MagicMastery,
            Resistances = Combat.CombatProfileFactory.Resistances(character),
            PremiumRevision = character.Premium.Revision,
            PremiumTimers = previous is not null && previous.PremiumRevision == character.Premium.Revision ? previous.PremiumTimers
                : Array.AsReadOnly(character.Premium.Timers.ToArray()),
            PremiumClockSeconds = previous is not null && previous.PremiumRevision == character.Premium.Revision ? previous.PremiumClockSeconds : nowSeconds,
            Stamp = stamp,
            AgingRevision = character.AgingRevision,
            Aging = previous is not null && previous.AgingRevision == character.AgingRevision && previous.InventoryRevision == character.InventoryRevision
                ? previous.Aging
                : Array.AsReadOnly(character.Equipment.Values.Where(static item => item.Maturity is not null)
                    .Select(static item => new AgingProgressView(item.InstanceId, item.Maturity.Count, item.Maturity.Goal)).ToArray())
        };
        return current == previous ? previous : current;
    }

    private static IReadOnlyList<OwnedItemView> ProjectItems(PlayerCharacter character)
    {
        var captured = new List<OwnedItemView>();
        foreach (var (anchor, item) in character.Inventory.Occupied())
        {
            captured.Add(Project(item, anchor, -1, character));
        }

        foreach (var pair in character.Equipment)
        {
            captured.Add(Project(pair.Value, -1, (int)pair.Key, character));
        }

        foreach (var (box, potion) in character.Belt.Occupied())
        {
            captured.Add(Project(potion, -1, PotionBelt.FirstOwnerSlot + box, character));
        }

        foreach (var (box, kept) in character.QuestBox.Occupied())
        {
            captured.Add(Project(kept, -1, QuestItemBox.FirstOwnerSlot + box, character));
        }

        if (character.Held is { } held)
        {
            captured.Add(Project(held, -1, OwnedItemView.HeldSlot, character));
        }

        return captured.AsReadOnly();
    }

    private static OwnedItemView Project(ItemInstance item, int anchor, int slot, PlayerCharacter owner) =>
        OwnedItemView.Of(item, anchor, slot) with { Details = ItemDetailView.Of(item, owner) };
}

internal readonly record struct OwnerStamp(int Level, ulong Experience, long Money, int Life, int Mana, int Stamina, int StatPoints, int SkillPoints,
    int EliteSkillPoints, short Tier, int BagPage, int WeaponSet, CharacterAttributes Attributes, long InventoryRevision, long ProgressionRevision,
    long SkillsRevision, long PremiumRevision, long AgingRevision, long EffectsRevision);
