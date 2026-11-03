using PristonTale.Simulation.Characters;
using PristonTale.Simulation.Items;
using PristonTale.Simulation.Items.Usable;
using PristonTale.Simulation.Modifiers;
using PristonTale.Simulation.Progression;
using PristonTale.Simulation.Quests;
using PristonTale.Simulation.Skills;
using PristonTale.Simulation.PersonalShops;
using PristonTale.Simulation.World.Npcs;

namespace PristonTale.Simulation.Characters;

public sealed partial class PlayerCharacter
{
    private readonly Dictionary<EquipmentSlot, ItemInstance> _equipment = new();

    public PlayerCharacter(CharacterClass characterClass, string name, int level = 1, CharacterAttributes attributes = null)
    {
        Class = characterClass;
        Name = name;
        Level = Math.Max(1, level);
        Attributes = attributes ?? Definition.AttributesAtLevel(Level);
        Skills = new LearnedSkills(characterClass.Skills);
        Skills.Changed += Recompute;
        Premium.Changed += Recompute;
        Recompute();
        Life = MaxLife;
        Mana = MaxMana;
        Stamina = MaxStamina;
        Class.OnSpawn(this);
        Inventory.Changed += OnInventoryChanged;
    }

    public CharacterClass Class { get; }
    public CharacterClassDef Definition => Class.Definition;
    public string Name { get; }
    public int Level { get; private set; }
    public ulong Experience { get; private set; }
    public long Money { get; private set; }
    public int StatPoints { get; private set; }
    public int SkillPoints { get; private set; }
    public int EliteSkillPoints { get; private set; }
    public short Tier { get; private set; }

    // The class's tiers run from 0 to LastTier (each rank-up moves one up). Below the last, experience stops at the start
    // of CappedLevel: an award that would pass it is discarded whole.
    public const int LastTier = 3;
    private const int CappedLevel = 100;

    public ulong ExperienceLimit => Tier < LastTier ? ExperienceTable.ForLevel(CappedLevel) : ExperienceTable.Ceiling;

    public int StarterGiftsReceived { get; private set; } = StarterGifts.Count;

    public void RestoreStarterGifts(int received) =>
        StarterGiftsReceived = received is >= 0 and <= StarterGifts.Count
            ? received
            : throw new ArgumentOutOfRangeException(nameof(received), "A character received 0 to StarterGifts.Count starter gifts.");

    // The beginner's pet (HelpPets): its kind (0 before one is drawn) and whether the player hid it.
    public int HelpPet { get; private set; }
    public bool HelpPetHidden { get; private set; }

    public void RestoreHelpPet(int kind, bool hidden)
    {
        HelpPet = kind is >= 0 and <= HelpPets.Kinds ? kind : throw new ArgumentOutOfRangeException(nameof(kind), "A help pet is 0 to HelpPets.Kinds.");
        HelpPetHidden = hidden;
    }

    // A character below HelpPets.LeaveLevel without a pet draws one.
    public void MeetHelpPet(World.Ai.ISimRng rng)
    {
        if (HelpPet == 0 && Level < HelpPets.LeaveLevel)
        {
            HelpPet = rng.NextInclusive(1, HelpPets.Kinds);
        }
    }

    // Shows or hides the beginner's pet while it is there; false otherwise.
    public bool ToggleHelpPet()
    {
        if (HelpPet == 0 || Level >= HelpPets.LeaveLevel)
        {
            return false;
        }

        HelpPetHidden = !HelpPetHidden;
        return true;
    }

    // Quest progress: the running quest, finished quests and the logs whose bits pay lasting rewards.
    public QuestJournal Quests { get; private set; } = new();

    public void RestoreProgress(ulong experience, long money, int statPoints = 0, short tier = 0, int skillPoints = 0, int eliteSkillPoints = 0)
    {
        if (money < 0 || statPoints < 0 || skillPoints < 0 || eliteSkillPoints < 0 || tier < 0 || tier > LastTier)
        {
            throw new ArgumentOutOfRangeException(nameof(experience));
        }

        Experience = experience;
        Money = money;
        StatPoints = statPoints;
        Tier = tier;
        SkillPoints = skillPoints;
        EliteSkillPoints = eliteSkillPoints;
    }

    public int AwardExperience(ulong amount)
    {
        if (amount == 0)
        {
            return 0;
        }

        // Experience saturates below the unsigned maximum (no wrap); an award past ExperienceLimit is discarded whole.
        const ulong ceiling = ExperienceTable.Ceiling;
        if (Experience >= ceiling)
        {
            return 0;
        }

        var next = amount > ceiling - Experience ? ceiling : Experience + amount;
        if (next > ExperienceLimit)
        {
            return 0;
        }

        Experience = next;
        var previousLevel = Level;
        var targetLevel = ExperienceTable.LevelFor(Experience);
        while (Level < targetLevel)
        {
            LevelUp();
        }

        return Level - previousLevel;
    }

    // Takes experience away (the death penalties), never below the start of the current level; returns what was taken.
    public ulong LoseExperience(ulong amount)
    {
        var floor = ExperienceTable.ForLevel(Math.Clamp(Level, 1, ExperienceTable.MaximumLevel));
        if (amount == 0 || Experience <= floor)
        {
            return 0;
        }

        var taken = Math.Min(amount, Experience - floor);
        Experience -= taken;
        return taken;
    }

    // intoBag: the item goes into the bag as a stack of its own (InventoryCommand.PickupToBag) for the client to carry, or
    // to the hand (Held) when the page shown has no room, as the original puts it on the mouse; unless the special item
    // box keeps it or the hand is full, when it is received as any item.
    public bool ReceiveGroundLoot(ItemInstance item, int gold, bool intoBag = false)
    {
        if (InventoryRevision == long.MaxValue)
        {
            return false;
        }

        if (item is not null)
        {
            return gold == 0 && Items.CarriedWeight.CanTake(this, Items.CarriedWeight.Of(item))
                && (intoBag && !QuestItemBox.Keeps(item.Definition.Code) && (Inventory.TryPlaceFirstFree(item) || TryHold(item)) || TryReceive(item));
        }

        if (gold <= 0 || !TryChangeMoney(gold))
        {
            return false;
        }

        OnInventoryChanged();
        return true;
    }

    public bool CanChangeMoney(long delta) => delta != long.MinValue && (delta >= 0 || Money >= -delta) && (delta <= 0 || Money <= long.MaxValue - delta);

    public bool TryChangeMoney(long delta)
    {
        if (!CanChangeMoney(delta))
        {
            return false;
        }

        Money += delta;
        return true;
    }

    public CharacterAttributes Attributes { get; private set; }
    public Inventory Inventory { get; private set; } = new();
    public long InventoryRevision { get; private set; }
    public OperationHistory<AppliedInventoryOperation> InventoryOperations { get; } = new();
    public OperationHistory<AppliedServiceOperation> ServiceOperations { get; } = new();
    public OperationHistory<AppliedShopPurchase> ShopPurchases { get; } = new();
    public long ProgressionRevision { get; private set; }
    public OperationHistory<AppliedProgressionOperation> ProgressionOperations { get; } = new();
    public LearnedSkills Skills { get; }
    public IReadOnlyDictionary<EquipmentSlot, ItemInstance> Equipment => _equipment;

    // Timed and permanent modifiers from every source; the derived stats follow its revision.
    public EffectContainer Effects { get; } = new();

    public PremiumTimers Premium { get; } = new();

    public OperationHistory<AppliedItemUse> ItemUses { get; } = new();

    private DerivedStats _derived;
    private long _derivedEffectsRevision = -1;
    private bool _derivedStale;

    public DerivedStats Derived
    {
        get
        {
            if (_derivedEffectsRevision != Effects.Revision || _derivedStale)
            {
                Recompute();
            }

            return _derived;
        }
    }

    private EquipmentTotals _equipmentTotals = EquipmentTotals.None;

    // What the usable equipped items add; reading it brings the derived stats up to date first.
    public EquipmentTotals EquipmentTotals
    {
        get
        {
            _ = Derived;
            return _equipmentTotals;
        }
    }

    public int CarriedWeight => Derived.CarriedWeight;

    // The attack animation rate: the attack speed and the attack-speed buffs.
    public float AttackRate => AnimationRate.AttackRate(Derived.AttackSpeed, Effects.Totals.Flat(StatKind.AttackMotionSpeed));

    public int Life { get; set; }
    public int Mana { get; set; }
    public int Stamina { get; set; }
    public int MaxLife => Derived.MaxLife;
    public int MaxMana => Derived.MaxMana;
    public int MaxStamina => Derived.MaxStamina;

    public event Action<PlayerCharacter, int> LeveledUp;
    public event Action<PlayerCharacter, EquipmentSlot, ItemInstance> Equipped;

    public void LevelUp()
    {
        if (Level >= ExperienceTable.MaximumLevel)
        {
            return;
        }

        Level++;
        StatPoints += 5 + QuestRewards.LevelUpBonusStatPoints(Level, Quests.LevelLog);
        if (Level >= 10 && Level % 2 == 0)
        {
            SkillPoints++;
        }

        if (Level >= 60 && Level % 2 == 0)
        {
            EliteSkillPoints++;
        }
        Recompute();
        // The original restores HP/MP/SP to full on level-up.
        Life = MaxLife;
        Mana = MaxMana;
        Stamina = MaxStamina;
        Class.OnLevelUp(this);
        LeveledUp?.Invoke(this, Level);
    }

    public ItemInstance GetEquipped(EquipmentSlot slot) =>
        _equipment.GetValueOrDefault(slot);

    public void Equip(EquipmentSlot slot, ItemInstance item)
    {
        if (item is null)
        {
            _equipment.Remove(slot);
        }
        else
        {
            if (!CanHold(slot, item))
            {
                throw new ArgumentException("Item equipment slot is invalid.", nameof(item));
            }

            _equipment[slot] = item;
        }
        Recompute();
        // Equipping can lower a max (e.g. swapping out a +life item); keep pools within bounds.
        Life = Math.Min(Life, MaxLife);
        Mana = Math.Min(Mana, MaxMana);
        Stamina = Math.Min(Stamina, MaxStamina);
        Equipped?.Invoke(this, slot, item);
        InventoryRevision++;
    }

    // A worn item must fit the slot, be usable by the character and leave the hands consistent: a two-handed weapon
    // holds the off hand too.
    public bool CanEquip(EquipmentSlot slot, ItemInstance item) => CanHold(slot, item) && ItemUsability.CanUse(this, item);

    // Slot, usability and identity checks without the hand rules, which equipping from the bag resolves by swapping.
    public bool CanWear(EquipmentSlot slot, ItemInstance item) => Fits(slot, item) && ItemUsability.CanUse(this, item);

    // Whether the slot can hold the item at all, usable or not: a saved item whose requirements are no longer met stays
    // worn without adding anything.
    public bool CanHold(EquipmentSlot slot, ItemInstance item) =>
        Fits(slot, item) && PairAllows(slot, item, GetEquipped(EquipmentSlot.Weapon), GetEquipped(EquipmentSlot.OffHand));

    // The hand rule of a weapon and off-hand pair, held or stored: a two-handed weapon leaves the off hand empty.
    private static bool PairAllows(EquipmentSlot slot, ItemInstance item, ItemInstance weapon, ItemInstance offHand) => slot switch
    {
        EquipmentSlot.Weapon => !item.Definition.TwoHanded || offHand is null,
        EquipmentSlot.OffHand => weapon?.Definition.TwoHanded != true,
        _ => true
    };

    private bool Fits(EquipmentSlot slot, ItemInstance item)
    {
        if (item is null || item.Count != 1 || !Enum.IsDefined(slot))
        {
            return false;
        }

        if (!ItemEquipmentMetadata.Fits(item.Definition.Kind, item.Definition.EquipSlot, slot))
        {
            return false;
        }

        foreach (var equipped in _equipment)
        {
            if (equipped.Key != slot && equipped.Value.InstanceId == item.InstanceId)
            {
                return false;
            }
        }

        return true;
    }

    // A received item (TryReceive) goes on the first empty slot that may hold it, as the original's client wears what it
    // receives; false when none may.
    private bool TryWearReceived(ItemInstance item)
    {
        for (var slot = (EquipmentSlot)0; (int)slot < EquipmentSlots.Count; slot++)
        {
            if (GetEquipped(slot) is null && CanEquip(slot, item))
            {
                Equip(slot, item);
                return true;
            }
        }

        return false;
    }

    // The item replaced in the slot takes the bag cell; a two-handed weapon also sends the off-hand item to the bag, and
    // an off-hand item sends a held two-handed weapon there.
    public bool TryEquipFromInventory(int anchor, EquipmentSlot slot)
    {
        if (anchor < 0 || anchor >= Inventory.Capacity || !CanWear(slot, Inventory[anchor]))
        {
            return false;
        }

        var item = Inventory[anchor];
        EquipmentSlot? otherHand = null;
        if (slot == EquipmentSlot.Weapon && item.Definition.TwoHanded)
        {
            otherHand = EquipmentSlot.OffHand;
        }
        else if (slot == EquipmentSlot.OffHand && GetEquipped(EquipmentSlot.Weapon)?.Definition.TwoHanded == true)
        {
            otherHand = EquipmentSlot.Weapon;
        }

        var displaced = otherHand.HasValue ? GetEquipped(otherHand.Value) : null;
        if (displaced is not null)
        {
            if (!Inventory.TryAdd(displaced))
            {
                return false;
            }

            Equip(otherHand.Value, null);
        }

        if (!Inventory.TryExchange(Inventory.Find(item.InstanceId), GetEquipped(slot)))
        {
            if (displaced is not null)
            {
                Inventory.TakeAt(Inventory.Find(displaced.InstanceId));
                Equip(otherHand.Value, displaced);
            }

            return false;
        }

        Equip(slot, item);
        return true;
    }

    // An equipped item cannot be taken off while the character carries more than the weight limit.
    public bool TryUnequip(EquipmentSlot slot)
    {
        var item = GetEquipped(slot);
        if (item is null || Items.CarriedWeight.IsOverweight(this) || !Inventory.TryAdd(item))
        {
            return false;
        }

        Equip(slot, null);
        return true;
    }

    public bool TryAllocateAttribute(CharacterAttribute attribute, int amount)
    {
        if (amount <= 0 || amount > StatPoints || !Enum.IsDefined(attribute))
        {
            return false;
        }

        var delta = attribute switch
        {
            CharacterAttribute.Strength => new CharacterAttributes(amount, 0, 0, 0, 0),
            CharacterAttribute.Spirit => new CharacterAttributes(0, amount, 0, 0, 0),
            CharacterAttribute.Talent => new CharacterAttributes(0, 0, amount, 0, 0),
            CharacterAttribute.Agility => new CharacterAttributes(0, 0, 0, amount, 0),
            CharacterAttribute.Health => new CharacterAttributes(0, 0, 0, 0, amount),
            _ => CharacterAttributes.Empty
        };
        if ((long)Attributes.Strength + delta.Strength > int.MaxValue || (long)Attributes.Spirit + delta.Spirit > int.MaxValue
            || (long)Attributes.Talent + delta.Talent > int.MaxValue || (long)Attributes.Agility + delta.Agility > int.MaxValue
            || (long)Attributes.Health + delta.Health > int.MaxValue)
        {
            return false;
        }

        Attributes = Attributes.Add(delta);
        StatPoints -= amount;
        Recompute();
        return true;
    }

    // Sets the attributes and the unallocated points of a stat reset; the total of both must not change.
    public void RedistributeAttributes(CharacterAttributes attributes, int statPoints)
    {
        if (attributes is null || statPoints < 0 || Sum(attributes) + statPoints != Sum(Attributes) + StatPoints)
        {
            throw new ArgumentException("A stat reset must keep the total of attributes and points.");
        }

        Attributes = attributes;
        StatPoints = statPoints;
        Recompute();
    }

    private static long Sum(CharacterAttributes a) => (long)a.Strength + a.Spirit + a.Talent + a.Agility + a.Health;

    // Item kinds in the main and the off hand (bit per kind), for effects bound to a weapon or a shield.
    private static readonly EquipmentSlot[] HandSlots = [EquipmentSlot.Weapon, EquipmentSlot.OffHand];

    private uint HeldKinds()
    {
        var held = 0u;
        foreach (var slot in HandSlots)
        {
            if (GetEquipped(slot) is { } item && Enum.IsDefined(item.Definition.Kind))
            {
                held |= 1u << (int)item.Definition.Kind;
            }
        }

        return held;
    }

    // The single derived-stats path: attributes and level, usable equipment with its class specialization, carried
    // weight, passive skills, then every active modifier.
    private void Recompute()
    {
        PassiveSkillEffects.Sync(this);
        QuestEffects.Sync(this);
        PremiumEffects.Sync(this);
        Effects.SetHeld(HeldKinds());
        _derivedStale = false;
        var equipped = Items.EquipmentTotals.Sum(this);
        var armorPercent = Effects.Totals.Flat(StatKind.ArmorDefence);
        if (armorPercent != 0 && GetEquipped(EquipmentSlot.Armor) is { } armor && armor.Roll.SpecializedFor(Definition.JobBitCode))
        {
            // The class armor's own defence, specialization included, grows by the percent before defence feeds absorption.
            var armorDefence = armor.Roll.Defence + armor.Roll.Spec.Defence;
            equipped = equipped with { Defence = equipped.Defence + armorDefence * armorPercent / 100 };
        }

        _equipmentTotals = equipped;
        var stats = DerivedStats.Compute(Attributes, Level, Definition.FunctionProfile, equipped, Items.CarriedWeight.Of(this));
        _derivedEffectsRevision = Effects.Revision;
        _derived = ModifierRules.Apply(PassiveSkills.Apply(stats, this), Effects.Totals);
        _derived = _derived with { MaxWeight = _derived.MaxWeight + PremiumRules.WeightBonus(Premium) };
        Life = Math.Min(Life, _derived.MaxLife);
        Mana = Math.Min(Mana, _derived.MaxMana);
        Stamina = Math.Min(Stamina, _derived.MaxStamina);
    }

    private void OnInventoryChanged()
    {
        InventoryRevision++;
        _derivedStale = true;
    }

    public void RestoreInventoryHistory(long revision, IEnumerable<AppliedInventoryOperation> operations)
    {
        if (revision < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(revision));
        }

        InventoryOperations.Restore(operations, operation => operation.Revision >= 1 && operation.Revision <= revision, "inventory operation");
        InventoryRevision = revision;
    }

    public void RecordInventoryOperation(InventoryCommand command)
    {
        InventoryOperations.Record(new AppliedInventoryOperation(command, InventoryRevision));
    }

    public void RestoreServiceHistory(IEnumerable<AppliedServiceOperation> operations)
    {
        ServiceOperations.Restore(operations, operation => operation.Revision >= 0 && operation.Revision <= InventoryRevision, "NPC service operation");
    }

    public void RecordServiceOperation(NpcServiceCommand command)
    {
        ServiceOperations.Record(new AppliedServiceOperation(command, InventoryRevision));
    }

    public void RestorePurchaseHistory(IEnumerable<AppliedShopPurchase> purchases)
    {
        ShopPurchases.Restore(purchases, purchase => purchase.Revision >= 0 && purchase.Revision <= InventoryRevision, "personal shop purchase");
    }

    public void RecordShopPurchase(PersonalShopPurchase purchase)
    {
        ShopPurchases.Record(new AppliedShopPurchase(purchase, InventoryRevision));
    }

    // Aging counts change no stat and so leave the inventory revision alone.
    public long AgingRevision { get; private set; }

    public bool TallyAging(Items.Aging.AgingCounter counter, int amount = 1)
    {
        var reached = false;
        foreach (var item in _equipment.Values)
        {
            var before = item.Maturity;
            if (before is not null && Items.Aging.AgingRules.CounterOf(item) == counter)
            {
                reached |= Items.Aging.AgingRules.Tally(item, amount);
                if (!ReferenceEquals(before, item.Maturity))
                {
                    AgingRevision++;
                }
            }
        }

        return reached;
    }

    public bool HasMaturedItem => OwnedItems().Any(Items.Aging.AgingRules.Matured);

    public bool MatureAgedItems()
    {
        var matured = false;
        foreach (var item in OwnedItems())
        {
            matured |= Items.Aging.AgingRules.Mature(item);
        }

        if (matured)
        {
            OnInventoryChanged();
        }

        return matured;
    }

    public ItemInstance FindOwnedItem(Guid instanceId) => OwnedItems().FirstOrDefault(item => item.InstanceId == instanceId);

    public IEnumerable<ItemInstance> OwnedItems()
    {
        foreach (var item in Inventory.Items())
        {
            yield return item;
        }

        foreach (var equipped in _equipment.Values)
        {
            yield return equipped;
        }

        foreach (var (_, potion) in Belt.Occupied())
        {
            yield return potion;
        }

        foreach (var (_, kept) in QuestBox.Occupied())
        {
            yield return kept;
        }

        foreach (var hidden in HiddenItems())
        {
            yield return hidden;
        }
    }

    public bool RestoreDurability(Guid instanceId)
    {
        var item = FindOwnedItem(instanceId);
        if (item is null || item.DurabilityMax <= 0 || item.DurabilityCurrent >= item.DurabilityMax)
        {
            return false;
        }

        item.DurabilityCurrent = item.DurabilityMax;
        Recompute();
        OnInventoryChanged();
        return true;
    }

    // Marks a change of the carried state that moves no item, such as gold handed to storage.
    public void TouchInventory()
    {
        OnInventoryChanged();
    }

    public PlayerCharacter CloneForInventoryOperation()
    {
        var clone = new PlayerCharacter(Class, Name, Level, Attributes);
        clone.RestoreProgress(Experience, Money, StatPoints, Tier, SkillPoints, EliteSkillPoints);
        clone.Inventory.CopyFrom(Inventory);
        foreach (var equipped in _equipment)
        {
            clone.Equip(equipped.Key, equipped.Value.Copy(1, preserveId: true));
        }

        clone.Belt.CopyFrom(Belt);
        clone.QuestBox.CopyFrom(QuestBox);
        clone.CopySides(this);

        foreach (var skill in Skills.Ranks)
        {
            clone.Skills.Restore(skill.Key, skill.Value, Skills.UsesOf(skill.Key));
        }

        clone.RestoreInventoryHistory(InventoryRevision, InventoryOperations);
        clone.RestoreServiceHistory(ServiceOperations);
        clone.RestorePurchaseHistory(ShopPurchases);
        clone.RestoreProgressionHistory(ProgressionRevision, ProgressionOperations);
        clone.Quests = Quests.Clone();
        clone.RestoreStarterGifts(StarterGiftsReceived);
        clone.RestoreHelpPet(HelpPet, HelpPetHidden);
        clone.Premium.CopyFrom(Premium);
        clone.ItemUses.CopyFrom(ItemUses);
        clone.Recompute();
        clone.Life = Life;
        clone.Mana = Mana;
        clone.Stamina = Stamina;
        return clone;
    }

    public bool ApplyProgression(ProgressionCommand command)
    {
        if (command.OperationId == Guid.Empty || command.ExpectedRevision != ProgressionRevision || ProgressionRevision == long.MaxValue)
        {
            return false;
        }

        switch (command.Action)
        {
            case ProgressionAction.AllocateAttribute:
                if (command.SkillCode != 0 || command.NpcEntityId != 0 || !TryAllocateAttribute(command.Attribute, command.Amount))
                {
                    return false;
                }

                break;
            case ProgressionAction.LearnSkill:
                if (command.Amount != 0 || command.Attribute != CharacterAttribute.Strength || command.NpcEntityId == 0
                    || !SkillLearning.TryQuote(this, command.SkillCode, out var price, out var finalTier))
                {
                    return false;
                }

                Money -= price;
                if (finalTier)
                {
                    EliteSkillPoints--;
                }
                else
                {
                    SkillPoints--;
                }

                Skills.Increase(command.SkillCode);
                break;
            default:
                return false;
        }

        ProgressionRevision++;
        ProgressionOperations.Record(new AppliedProgressionOperation(command, ProgressionRevision));
        return true;
    }

    public void RestoreProgressionHistory(long revision, IEnumerable<AppliedProgressionOperation> operations)
    {
        if (revision < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(revision));
        }

        ProgressionOperations.Restore(operations, operation => operation.Revision >= 1 && operation.Revision <= revision, "progression operation");
        ProgressionRevision = revision;
    }

    public void RestoreQuests(QuestJournal journal)
    {
        Quests = journal ?? throw new ArgumentNullException(nameof(journal));
        Recompute();
    }

    // Quest changes made outside a stored operation (kill counts, timers) on the live character.
    public void RefreshQuestEffects() => Recompute();

    // Quest rewards that change progression: the class tier, and stat and skill points added on top of whatever the
    // tick changed meanwhile.
    public void ApplyQuestProgress(short tier, int statPoints, int skillPoints, int eliteSkillPoints)
    {
        if (tier < Tier || tier > LastTier || StatPoints + (long)statPoints < 0 || SkillPoints + (long)skillPoints < 0 || EliteSkillPoints + (long)eliteSkillPoints < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(tier));
        }

        Tier = tier;
        StatPoints += statPoints;
        SkillPoints += skillPoints;
        EliteSkillPoints += eliteSkillPoints;
    }

    // The free redistribution a rank-up offers: every learned rank returns to its pool (the first twelve window skills to
    // the skill points, the final tier's to its own points) and the proficiency is forgotten.
    public void ResetLearnedSkills()
    {
        var skills = Class.Skills;
        for (var index = 0; index < skills.Count; index++)
        {
            var rank = Skills.RankOf(skills[index].Code);
            if (SkillLearning.UsesFinalTierPoints(index))
            {
                EliteSkillPoints += rank;
            }
            else
            {
                SkillPoints += rank;
            }
        }

        Skills.Clear();
    }

    // Commits a stored quest operation: the candidate's quest journal (keeping kills counted meanwhile), its items and
    // the progression and gold deltas computed when it was prepared.
    public void CommitQuestOperation(PlayerCharacter candidate, short tier, int statPoints, int skillPoints, int eliteSkillPoints, long moneyDelta,
        bool resetSkills)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        if (!CanChangeMoney(moneyDelta))
        {
            throw new InvalidOperationException("Quest gold change no longer fits.");
        }

        CommitInventoryOperation(candidate);
        if (resetSkills)
        {
            // The refunded points are part of the prepared deltas; the save gate kept the ranks unchanged since then.
            Skills.Clear();
        }

        ApplyQuestProgress(tier, statPoints, skillPoints, eliteSkillPoints);
        TryChangeMoney(moneyDelta);
        Quests.AdoptCommitted(candidate.Quests);
        Recompute();
        Life = Math.Min(Life, MaxLife);
        Mana = Math.Min(Mana, MaxMana);
        Stamina = Math.Min(Stamina, MaxStamina);
    }

    public void CommitInventoryOperation(PlayerCharacter candidate)
    {
        Inventory.Changed -= OnInventoryChanged;
        candidate.Inventory.Changed -= candidate.OnInventoryChanged;
        Inventory = candidate.Inventory;
        Inventory.Changed += OnInventoryChanged;
        _equipment.Clear();
        foreach (var equipped in candidate._equipment)
        {
            _equipment.Add(equipped.Key, equipped.Value);
        }

        Belt.CopyFrom(candidate.Belt);
        QuestBox.CopyFrom(candidate.QuestBox);
        HiddenBag = candidate.HiddenBag;
        (_storedWeapon, _storedOffHand) = (candidate._storedWeapon, candidate._storedOffHand);
        (BagPage, WeaponSet) = (candidate.BagPage, candidate.WeaponSet);
        Held = candidate.Held;

        RestoreInventoryHistory(candidate.InventoryRevision, candidate.InventoryOperations);
        RestoreServiceHistory(candidate.ServiceOperations);
        RestorePurchaseHistory(candidate.ShopPurchases);
        Recompute();
        Life = Math.Min(Life, MaxLife);
        Mana = Math.Min(Mana, MaxMana);
        Stamina = Math.Min(Stamina, MaxStamina);
    }
}
