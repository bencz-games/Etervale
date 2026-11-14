using System.Text.Json;
using System.Text.Json.Serialization;
using PristonTale.Simulation.Characters;
using PristonTale.Simulation.Items;
using PristonTale.Simulation.Progression;
using PristonTale.Simulation.World.Npcs;

namespace PristonTale.Persistence.Abstractions.Characters;

public sealed record SavedItem(
    Guid InstanceId,
    uint Code,
    int Count,
    int Anchor,
    EquipmentSlot? Slot,
    int UpgradeLevel,
    int DurabilityCurrent,
    int DurabilityMax,
    uint JobSpecMask = 0,
    ItemStatBlock RolledBonus = default,
    ItemRoll? Roll = null,
    int Unique = 0,
    bool Sealed = false,
    Simulation.Items.Aging.AgingMaturity? Maturity = null,
    int MixSheltom = 0,
    Simulation.Items.Mixing.MixStat MixStats = Simulation.Items.Mixing.MixStat.None,
    int? Belt = null,
    bool Hidden = false,
    int? QuestBox = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] bool Held = false,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Link = null);

// ActiveSide: the bag page shown (inventory) or the weapon pair held (equipment); Hidden items are on the other side.
public sealed record SavedItems(int Version, SavedItem[] Items, long Revision = 0, AppliedInventoryOperation[]? Operations = null,
    AppliedServiceOperation[]? ServiceOperations = null, int ActiveSide = 0, Simulation.PersonalShops.AppliedShopPurchase[]? ShopPurchases = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] Guid CostumeItemId = default);
public sealed record SavedSkill(uint Code, int Rank, int Uses = 0);
public sealed record SavedSkills(int Version, int Points, int ElitePoints, SavedSkill[] Skills,
    long Revision = 0, AppliedProgressionOperation[]? Operations = null);

public static class CharacterStateCodec
{
    internal static readonly JsonSerializerOptions Options = new(CharacterJson.Options)
    {
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        RespectRequiredConstructorParameters = true,
        MaxDepth = 16
    };

    public static void Restore(PlayerCharacter character, CharacterRecord record, ItemCatalog catalog)
    {
        var inventory = ReadItems(record.Inventory, JsonValueKind.Array);
        var equipment = ReadEquipment(record.Equipment);
        var skills = ReadSkills(record.Skills);
        var identities = new HashSet<Guid>();
        // The column holds the unsigned experience's 64-bit pattern (CharacterRecordFactory).
        character.RestoreProgress(unchecked((ulong)record.Exp), record.Money, record.StatPoints, record.Tier, skills.Points, skills.ElitePoints);
        character.RestoreQuests(QuestStateCodec.Read(record.Quests));
        character.RestoreStarterGifts(record.StarterGiftsReceived);
        character.RestoreHelpPet(record.HelpPet, record.HelpPetHidden);

        character.RestoreSides(inventory.ActiveSide, equipment.ActiveSide);
        // Gear restores first: the potion space it gives bounds the belt's stacks.
        foreach (var saved in inventory.Items.Where(static saved => saved.Belt is null && saved.QuestBox is null && !saved.Held))
        {
            var item = RestoreItem(saved, catalog, identities);
            var bag = saved.Hidden ? character.HiddenBag : character.Inventory;
            // Footprints follow the item table; an item saved where its footprint no longer fits moves to the first free place.
            if (saved.Slot.HasValue || !bag.TryPlace(item, saved.Anchor) && !bag.TryPlaceFirstFree(item))
            {
                throw new InvalidDataException("Invalid saved inventory footprint.");
            }
        }

        foreach (var saved in equipment.Items)
        {
            var item = RestoreItem(saved, catalog, identities);
            if (!saved.Slot.HasValue || saved.Anchor != -1)
            {
                throw new InvalidDataException("Invalid saved equipment.");
            }

            // Older builds could equip Hopy as real armour. Preserve that item in a bag
            // rather than failing character login after the new equip rule is enforced.
            if (CostumeCatalog.IsExclusiveCosmeticKey(item.Definition.CatalogKey))
            {
                if (!character.Inventory.TryAdd(item) && !character.HiddenBag.TryAdd(item))
                    throw new InvalidDataException("No room to migrate previously equipped costume.");
                continue;
            }

            if (saved.Hidden)
            {
                if (!character.RestoreStored(saved.Slot.Value, item))
                {
                    throw new InvalidDataException("Invalid saved weapon pair.");
                }

                continue;
            }

            if (character.GetEquipped(saved.Slot.Value) is not null || !character.CanHold(saved.Slot.Value, item))
            {
                throw new InvalidDataException("Invalid saved equipment.");
            }

            character.Equip(saved.Slot.Value, item);
        }

        foreach (var saved in inventory.Items.Where(static saved => saved.QuestBox is not null))
        {
            if (saved.Slot.HasValue || saved.Anchor != -1 || !character.RestoreQuestBox(saved.QuestBox!.Value, RestoreItem(saved, catalog, identities)))
            {
                throw new InvalidDataException("Invalid saved special item box.");
            }
        }

        foreach (var saved in inventory.Items.Where(static saved => saved.Held))
        {
            if (saved.Slot.HasValue || saved.Anchor != -1 || saved.Belt is not null || saved.QuestBox is not null
                || !character.RestoreHeld(RestoreItem(saved, catalog, identities)))
            {
                throw new InvalidDataException("Invalid saved held item.");
            }
        }

        foreach (var saved in inventory.Items.Where(static saved => saved.Belt is not null))
        {
            if (saved.Slot.HasValue || saved.Anchor != -1 || !character.RestoreBelt(saved.Belt!.Value, RestoreItem(saved, catalog, identities)))
            {
                throw new InvalidDataException("Invalid saved potion belt.");
            }
        }

        foreach (var skill in skills.Skills)
        {
            character.Skills.Restore(skill.Code, skill.Rank, skill.Uses);
        }

        character.RestoreInventoryHistory(inventory.Revision, inventory.Operations ?? []);
        character.RestoreServiceHistory(inventory.ServiceOperations ?? []);
        character.RestorePurchaseHistory(inventory.ShopPurchases ?? []);
        character.RestoreProgressionHistory(skills.Revision, skills.Operations ?? []);
        character.RestoreCostume(inventory.CostumeItemId);
    }

    public static SavedItems ReadInventory(JsonText inventory) => ReadItems(inventory, JsonValueKind.Array);

    public static SavedItems CaptureInventory(PlayerCharacter character)
    {
        var items = new List<SavedItem>();
        items.AddRange(character.Inventory.Occupied().Select(static cell => CaptureItem(cell.Item, cell.Anchor, null)));
        items.AddRange(character.HiddenBag.Occupied().Select(static cell => CaptureItem(cell.Item, cell.Anchor, null) with { Hidden = true }));
        items.AddRange(character.Belt.Occupied().Select(static box => CaptureItem(box.Item, -1, null) with { Belt = box.Slot }));
        items.AddRange(character.QuestBox.Occupied().Select(static box => CaptureItem(box.Item, -1, null) with { QuestBox = box.Slot }));
        if (character.Held is { } held)
        {
            items.Add(CaptureItem(held, -1, null) with { Held = true });
        }

        return new SavedItems(1, items.ToArray(), character.InventoryRevision, StoredHistory.Newest(character.InventoryOperations),
            StoredHistory.Newest(character.ServiceOperations), character.BagPage, StoredHistory.Newest(character.ShopPurchases),
            character.ActiveCostumeItemId());
    }

    public static SavedItems CaptureEquipment(PlayerCharacter character)
    {
        var items = new List<SavedItem>(character.Equipment.Count + 2);
        foreach (var equipped in character.Equipment)
        {
            items.Add(CaptureItem(equipped.Value, -1, equipped.Key));
        }

        if (character.StoredWeapon is { } weapon)
        {
            items.Add(CaptureItem(weapon, -1, EquipmentSlot.Weapon) with { Hidden = true });
        }

        if (character.StoredOffHand is { } offHand)
        {
            items.Add(CaptureItem(offHand, -1, EquipmentSlot.OffHand) with { Hidden = true });
        }

        return new SavedItems(1, items.ToArray(), ActiveSide: character.WeaponSet);
    }

    public static SavedItems ReadEquipment(JsonText equipment)
    {
        return ReadItems(equipment, JsonValueKind.Object);
    }

    public static SavedSkills CaptureSkills(PlayerCharacter character)
    {
        var skills = new SavedSkill[character.Skills.Ranks.Count];
        var index = 0;
        foreach (var skill in character.Skills.Ranks)
        {
            skills[index++] = new SavedSkill(skill.Key, skill.Value, character.Skills.UsesOf(skill.Key));
        }

        return new SavedSkills(1, character.SkillPoints, character.EliteSkillPoints, skills,
            character.ProgressionRevision, StoredHistory.Newest(character.ProgressionOperations));
    }

    internal static SavedItem CaptureItem(ItemInstance item, int anchor, EquipmentSlot? slot)
    {
        return new SavedItem(item.InstanceId, item.Definition.Code, item.Count, anchor, slot, item.UpgradeLevel,
            item.DurabilityCurrent, item.DurabilityMax, item.JobSpecMask, default, item.Roll,
            item.Definition.SavedUnique, item.Sealed, item.Maturity, item.MixSheltom, item.MixStats, Link: item.Link.Length > 0 ? item.Link : null);
    }

    internal static ItemInstance RestoreItem(SavedItem saved, ItemCatalog catalog, HashSet<Guid> identities)
    {
        if (saved is null || saved.InstanceId == Guid.Empty || !identities.Add(saved.InstanceId)
            || !catalog.TryGet(saved.Code, saved.Unique, out var definition) || saved.Count < 1 || saved.Count > definition.MaxStack
            || saved.UpgradeLevel < 0 || saved.DurabilityCurrent < 0 || saved.DurabilityMax < saved.DurabilityCurrent)
        {
            throw new InvalidDataException("Invalid or unsupported saved item.");
        }

        var item = new ItemInstance(definition, saved.Count)
        {
            InstanceId = saved.InstanceId,
            UpgradeLevel = saved.UpgradeLevel,
            DurabilityCurrent = saved.DurabilityCurrent,
            DurabilityMax = saved.DurabilityMax,
            Sealed = saved.Sealed,
            Maturity = saved.Maturity,
            MixSheltom = Math.Max(0, saved.MixSheltom),
            MixStats = saved.MixStats,
            Link = saved.Link ?? ""
        };

        // Copies saved before per-copy values existed take the shop form of their item with the saved specialization.
        item.Roll = saved.Roll is { Requirements: not null, Spec: not null } roll ? roll : item.Roll with { SpecJob = saved.JobSpecMask };
        return item;
    }

    // The most items a saved item list holds: every cell of both bag pages, the belt's and the special box's boxes and the
    // hand (CaptureInventory); the equipment's list, slots and the stored pair, stays far below.
    public const int MaxSavedItems = PlayerCharacter.Sides * Inventory.DefaultWidth * Inventory.DefaultHeight + PotionBelt.Boxes + QuestItemBox.Boxes + 1;

    private static SavedItems ReadItems(JsonText json, JsonValueKind blankKind)
    {
        if (IsBlank(json, blankKind))
        {
            return new SavedItems(1, []);
        }

        var state = JsonSerializer.Deserialize<SavedItems>(json.Value, Options);
        if (state is null || state.Version != 1 || state.Items is null || state.Items.Length > MaxSavedItems
            || state.ActiveSide is < 0 or >= PlayerCharacter.Sides)
        {
            throw new InvalidDataException("Unsupported item-state version or size.");
        }

        return state;
    }

    private static SavedSkills ReadSkills(JsonText json)
    {
        if (IsBlank(json, JsonValueKind.Array))
        {
            return new SavedSkills(1, 0, 0, []);
        }

        var state = JsonSerializer.Deserialize<SavedSkills>(json.Value, Options);
        if (state is null || state.Version != 1 || state.Skills is null || state.Skills.Length > 128 || state.Points < 0 || state.ElitePoints < 0)
        {
            throw new InvalidDataException("Unsupported skill-state version or size.");
        }

        return state;
    }

    // Longer owned-state JSON is corrupt: a full bag, belt and warehouse with their histories take a small part of it.
    internal const int MaxStateLength = 4 * 1024 * 1024;

    internal static bool IsBlank(JsonText json, JsonValueKind expected)
    {
        if (string.IsNullOrWhiteSpace(json.Value) || json.Value.Length > MaxStateLength)
        {
            throw new InvalidDataException("Invalid owned-state JSON size.");
        }

        using var document = JsonDocument.Parse(json.Value, new JsonDocumentOptions { MaxDepth = 16 });
        var root = document.RootElement;
        return root.ValueKind == expected && (expected == JsonValueKind.Array
            ? root.GetArrayLength() == 0
            : !root.EnumerateObject().MoveNext());
    }
}
