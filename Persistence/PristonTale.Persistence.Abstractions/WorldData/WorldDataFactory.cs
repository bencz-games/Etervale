using PristonTale.Simulation.Combat;
using PristonTale.Simulation.World.Fields;
using PristonTale.Simulation.World.Loot;
using PristonTale.Simulation.World.Monsters;
using PristonTale.Simulation.World.Motion;
using PristonTale.Simulation.World.Npcs;
using PristonTale.Simulation.Items;

namespace PristonTale.Persistence.Abstractions.WorldData;

public static class WorldDataFactory
{

    public static ItemDefinition ToItemDefinition(ItemTemplateRecord r)
    {
        // The shop form: the low end of every authored range. Copies made by drops roll from the template.
        var weapon = r.DamageMinHigh != 0 || r.DamageMaxHigh != 0;
        var baseStats = new ItemStatBlock(
            DamageMin: weapon ? r.DamageMinLow : 0,
            DamageMax: weapon ? r.DamageMaxLow : 0,
            Defence: r.DefenceMin,
            AttackRating: r.AttackRatingMin,
            AttackSpeed: r.AttackSpeed,
            Strength: 0, Spirit: 0, Talent: 0, Agility: 0, Health: 0);

        return ItemEquipmentMetadata.Apply(new ItemDefinition(
            ItemCodes.Parse(r.Code),
            r.Name ?? r.Code,
            (ItemKind)r.Kind,
            GridWidth: Math.Max(1, r.GridWidth),
            GridHeight: Math.Max(1, r.GridHeight),
            MaxStack: (ItemKind)r.Kind == ItemKind.Potion ? PotionDefinition.StackSize : Math.Max(1, r.MaxStack))
        {
            CatalogKey = r.Code,
            // Potions and event foods restore their authored ranges; only potions are drunk from the belt.
            Potion = (ItemKind)r.Kind == ItemKind.Potion || r.RestoreLifeMax > 0 || r.RestoreManaMax > 0 || r.RestoreStaminaMax > 0
                ? new PotionDefinition(r.RestoreLifeMin, r.RestoreLifeMax, r.RestoreManaMin, r.RestoreManaMax, r.RestoreStaminaMin, r.RestoreStaminaMax)
                : null,
            RequiredLevel = r.ReqLevel,
            Requirements = new(r.ReqStrength, r.ReqSpirit, r.ReqTalent, r.ReqAgility, r.ReqHealth),
            AllowedJobs = ItemClassRestrictions.AllowedJobs(r.Code),
            SpecializationJobs = (uint)r.SpecClass,
            ShootingRange = r.ShootingRange,
            BaseStats = baseStats,
            MaxDurability = r.DurabilityMin,
            Weight = r.Weight,
            Price = r.Price,
            Resistances = new ResistanceBlock((short)r.ResBionicMin, (short)r.ResEarthMin, (short)r.ResFireMin, (short)r.ResIceMin,
                (short)r.ResLightningMin, (short)r.ResPoisonMin, (short)r.ResWaterMin, (short)r.ResWindMin),
            Critical = r.Critical,
            PotionSpace = r.PotionSpace,
            MagicMastery = r.MagicMasteryMin,
            UniqueItem = r.UniqueItem,
            Template = ToItemTemplate(r),
            Glow = new ItemGlow(Channel(r.GlowR), Channel(r.GlowG), Channel(r.GlowB), Channel(r.GlowA), Math.Clamp(r.GlowBlink, -1, 16), r.GlowScale),
            DisplayEffect = r.DisplayEffect,
            DailyDropLimit = Math.Max(0, r.DailyDropLimit),
            SetCode = r.SetCode ?? 0,
        });
    }

    // Item keys that are not a family and an ordinal (ItemCodes) have no runtime code and stay out of the catalog.
    public static IReadOnlyList<ItemDefinition> ToItemDefinitions(IEnumerable<ItemTemplateRecord> records) => Of(records, ItemVariant.Regular);

    // A named unique (Vega, Rage...) is saved by ItemDefinition.NamedUniqueBase + its variant ordinal.
    public static IReadOnlyList<ItemDefinition> ToNamedUniqueDefinitions(IEnumerable<ItemTemplateRecord> records) =>
        records.Where(static record => record.Variant == (short)ItemVariant.NamedUnique && ItemCodes.TryParse(record.Code, out _))
            .Select(static record => ToItemDefinition(record) with { UniqueVariant = record.VariantOrdinal })
            .ToList();

    // The quest version of an item loads beside the regular catalog.
    public static IReadOnlyList<ItemDefinition> ToQuestVariantDefinitions(IEnumerable<ItemTemplateRecord> records) => Of(records, ItemVariant.Quest);

    private static List<ItemDefinition> Of(IEnumerable<ItemTemplateRecord> records, ItemVariant variant) =>
        records.Where(record => record.Variant == (short)variant && ItemCodes.TryParse(record.Code, out _)).Select(ToItemDefinition).ToList();

    public static IReadOnlyList<ItemSetDefinition> ToItemSets(IEnumerable<ItemSetRecord> records) =>
        records.Select(static r => new ItemSetDefinition(r.SetCode, r.Pieces, r.Life, r.Absorb, r.Block, r.LifeRegen, r.ManaRegen, r.StaminaRegen)).ToList();

    public static ItemTemplate ToItemTemplate(ItemTemplateRecord r)
    {
        // Bonuses arrive by kind, so a life regeneration range (LifeRegenRange) overrides a plain LifeRegen.
        var spec = new ItemSpecTemplate();
        foreach (var bonus in r.SpecBonuses)
        {
            var range = new ItemFloatRange(bonus.MinValue, bonus.MaxValue);
            var whole = new ItemRange((int)bonus.MinValue, (int)bonus.MaxValue);
            spec = (ItemSpecBonusKind)bonus.Bonus switch
            {
                ItemSpecBonusKind.Absorb => spec with { Absorb = range },
                ItemSpecBonusKind.Defence => spec with { Defence = whole },
                ItemSpecBonusKind.MoveSpeed => spec with { MoveSpeed = range },
                ItemSpecBonusKind.Block => spec with { Block = bonus.MinValue },
                ItemSpecBonusKind.AttackSpeed => spec with { AttackSpeed = (int)bonus.MinValue },
                ItemSpecBonusKind.Critical => spec with { Critical = (int)bonus.MinValue },
                ItemSpecBonusKind.ShootingRange => spec with { ShootingRange = (int)bonus.MinValue },
                ItemSpecBonusKind.MagicMastery => spec with { MagicMastery = range },
                ItemSpecBonusKind.LevelMana => spec with { LevelMana = (int)bonus.MinValue },
                ItemSpecBonusKind.LevelLife => spec with { LevelLife = (int)bonus.MinValue },
                ItemSpecBonusKind.LevelAttackRating => spec with { LevelAttackRating = whole },
                ItemSpecBonusKind.LevelDamage => spec with { LevelDamage = (int)bonus.MaxValue },
                ItemSpecBonusKind.ManaRegen => spec with { ManaRegen = range },
                ItemSpecBonusKind.LifeRegen => spec with { LifeRegen = bonus.MinValue, LifeRegenRange = ItemFloatRange.None },
                ItemSpecBonusKind.StaminaRegen => spec with { StaminaRegen = bonus.MinValue },
                ItemSpecBonusKind.LifeRegenRange => spec with { LifeRegen = bonus.MinValue, LifeRegenRange = range },
                _ => spec
            };
        }

        return new ItemTemplate
        {
            Durability = new ItemRange(r.DurabilityMin, r.DurabilityMax),
            DamageMin = new ItemRange(r.DamageMinLow, r.DamageMinHigh),
            DamageMax = new ItemRange(r.DamageMaxLow, r.DamageMaxHigh),
            AttackRating = new ItemRange(r.AttackRatingMin, r.AttackRatingMax),
            Defence = new ItemRange(r.DefenceMin, r.DefenceMax),
            Absorb = new ItemFloatRange(r.AbsorbMin, r.AbsorbMax),
            Block = new ItemFloatRange(r.BlockMin, r.BlockMax),
            MoveSpeed = new ItemFloatRange(r.MoveSpeedMin, r.MoveSpeedMax),
            LifeRegen = new ItemFloatRange(r.LifeRegenMin, r.LifeRegenMax),
            ManaRegen = new ItemFloatRange(r.ManaRegenMin, r.ManaRegenMax),
            StaminaRegen = new ItemFloatRange(r.StaminaRegenMin, r.StaminaRegenMax),
            AddLife = new ItemRange(r.AddLifeMin, r.AddLifeMax),
            AddMana = new ItemRange(r.AddManaMin, r.AddManaMax),
            AddStamina = new ItemRange(r.AddStaminaMin, r.AddStaminaMax),
            MagicMastery = new ItemFloatRange(r.MagicMasteryMin, r.MagicMasteryMax),
            Resistances =
            [
                new(r.ResBionicMin, r.ResBionicMax), new(r.ResEarthMin, r.ResEarthMax), new(r.ResFireMin, r.ResFireMax),
                new(r.ResIceMin, r.ResIceMax), new(r.ResLightningMin, r.ResLightningMax), new(r.ResPoisonMin, r.ResPoisonMax),
                new(r.ResWaterMin, r.ResWaterMax), new(r.ResWindMin, r.ResWindMax)
            ],
            SpecJob = (uint)r.SpecClass,
            RandomSpecJobs = r.RandomSpecClasses.Select(static job => (uint)job).Where(static job => job != 0).ToList(),
            Spec = spec
        };
    }

    // Source configs name authoring .ini files, while shipped model tables use .inx.
    // Keep the persisted value intact and normalize only the runtime definition.
    private static string ToInxModelPath(string modelPath) =>
        string.IsNullOrEmpty(modelPath) ? modelPath : System.IO.Path.ChangeExtension(modelPath, ".inx");

    // The monster code is the record's stable id; event forms take the codes after the highest one.
    public static IReadOnlyList<MonsterDefinition> ToMonsterDefinitions(IReadOnlyList<MonsterRecord> records, IReadOnlyDictionary<string, uint>? itemCodes = null)
    {
        var defs = new List<MonsterDefinition>(records.Count);
        var nextFormCode = 1;
        foreach (var record in records)
        {
            defs.Add(ToMonsterDefinition(record, itemCodes));
            nextFormCode = Math.Max(nextFormCode, record.Id + 1);
        }

        // A monster with an event model gets its event form: the same monster, normal class, wearing the event model (the
        // hammer event turns it into this form).
        var authored = defs.Count;
        for (var i = 0; i < authored; i++)
        {
            if (defs[i].EventModelInxPath.Length == 0)
            {
                continue;
            }

            var formCode = nextFormCode++;
            defs.Add(defs[i] with
            {
                Code = formCode, ModelInxPath = defs[i].EventModelInxPath, Class = MonsterClass.Normal, EventModelInxPath = "", EventFormCode = 0
            });
            defs[i] = defs[i] with { EventFormCode = formCode };
        }

        return defs;
    }

    private static MonsterDefinition ToMonsterDefinition(MonsterRecord r, IReadOnlyDictionary<string, uint>? itemCodes)
    {
        var combat = new MonsterCombatStats(
            Level: r.Level,
            AttackRatingAccuracy: r.Accuracy,
            AttackDamage: new MonsterRange(r.AttackMin, r.AttackMax),
            AttackSpeed: r.AttackSpeed,
            MeleeRange: r.AttackRange,
            Defence: r.Defence,
            Absorption: r.AbsorbPct,
            BlockChance: r.BlockPct,
            MaxLife: r.Hp,
            Resistances: ToMonsterResistances(r))
        {
            DamageStunPercent = r.StunPct,
        };

        // Curse skills deal no damage and have no area; they still carry a rating and a curse code.
        var skill = (r.SkillDamageMin == 0 && r.SkillDamageMax == 0 && r.SkillRange == 0 && r.SkillDistance == 0 && r.SkillRatingPct == 0
                && r.SkillCurseCode == 0)
            ? MonsterSkillStats.None
            : new MonsterSkillStats(
                new MonsterRange(r.SkillDamageMin, r.SkillDamageMax),
                r.SkillRange, r.SkillDistance, r.SkillRatingPct, r.SkillCurseCode);

        var ai = new MonsterAi(
            Iq: r.Iq,
            Nature: (MonsterNature)r.Nature,
            SightRadius: r.Sight,
            MoveSpeed: r.MoveSpeed,
            MoveRange: r.MoveRange,
            GroupSize: new MonsterRange(r.GroupMin, r.GroupMax),
            ActiveHour: ToActiveHour(r.ActiveHour));

        var monsterClass = (MonsterClass)r.Class;
        return new MonsterDefinition(
            r.Id, r.Name ?? r.Key,
            Faction: r.Hostile ? MonsterFaction.Enemy : MonsterFaction.Npc,
            Class: monsterClass,
            Brood: (MonsterBrood)r.Brood,
            Combat: combat,
            ModelInxPath: ToInxModelPath(r.Model))
        {
            Skill = skill,
            Key = r.Key,
            Ai = ai,
            Drops = ToDropTable(r, itemCodes),
            ExpReward = r.Exp >= 0 ? (ulong)r.Exp : throw new InvalidDataException($"Monster '{r.Key}' has negative experience {r.Exp}."),
            SoundCode = r.SoundCode,
            ModelScale = r.ModelScale,
            PortraitOffsetY = r.PortraitOffsetY,
            PortraitOffsetZ = r.PortraitOffsetZ,
            QuestCode = r.QuestCode,
            EventItem = TryResolveDrop(r.EventItem, itemCodes, out var eventItem) ? eventItem : 0,
            PotionCount = r.PotionCount,
            PotionPercent = r.PotionPercent,
            ShadowSizeLevel = r.SizeLevel,
            MagicResistance = r.ResMagic,
            MoveType = r.MoveType,
            SpecialAttackPercent = r.SpecialAttackPct,
            DeathGold = new MonsterRange(Math.Min(r.GoldMin, r.GoldMax), Math.Max(r.GoldMin, r.GoldMax)),
            DeathGoldChance = Math.Clamp(r.GoldPct, 0, 100),
            EventModelInxPath = string.IsNullOrWhiteSpace(r.EventModel) ? "" : ToInxModelPath(r.EventModel),
            EventCode = r.EventCode,
            EventInfo = r.EventInfo,
        };
    }

    private static ResistanceBlock ToMonsterResistances(MonsterRecord r) => new(
        (short)r.ResBionic, (short)r.ResEarth, (short)r.ResFire, (short)r.ResIce,
        (short)r.ResLightning, (short)r.ResPoison, (short)r.ResWater, (short)r.ResWind);

    private static MonsterActiveHour ToActiveHour(int raw) => raw switch
    {
        > 0 => MonsterActiveHour.Day,
        < 0 => MonsterActiveHour.Night,
        _ => MonsterActiveHour.Any,
    };

    // The bonus drop lines a monster rolls: the first ones whose item the catalog knows.
    public const int BonusDropLines = 3;

    // A line of items keeps its whole weight among the items the catalog knows (weight / count each, the remainder to the
    // first ones); a line none of whose items it knows weighs nothing, so its weight is never rolled. Bonus drops have
    // no probability slot.
    private static DropTable ToDropTable(MonsterRecord r, IReadOnlyDictionary<string, uint>? itemCodes)
    {
        if (r.DropLines.Count == 0 && r.BonusDrops.Count == 0 && r.MaxDropsPerKill == 0 && !r.DropsSeenByAll)
        {
            return DropTable.Empty;
        }

        var entries = new List<DropEntry>(r.DropLines.Count + r.BonusDrops.Count);
        foreach (var line in r.DropLines)
        {
            switch ((DropKind)line.Kind)
            {
                case DropKind.Gold:
                    entries.Add(DropEntry.GoldSlot(line.Weight, line.GoldMin, line.GoldMax));
                    break;
                case DropKind.Item:
                    var known = new List<uint>(line.Items.Count);
                    foreach (var item in line.Items)
                    {
                        if (TryResolveDrop(item, itemCodes, out var code))
                        {
                            known.Add(code);
                        }
                    }

                    for (var i = 0; i < known.Count; i++)
                    {
                        var weight = line.Weight / known.Count + (i < line.Weight % known.Count ? 1 : 0);
                        entries.Add(new DropEntry(DropKind.Item, weight, known[i], 0, 0, IsBonus: false));
                    }

                    break;
                default:
                    entries.Add(DropEntry.NothingSlot(line.Weight));
                    break;
            }
        }

        var bonusLines = 0;
        foreach (var bonus in r.BonusDrops)
        {
            if (bonusLines < BonusDropLines && TryResolveDrop(bonus.ItemCode, itemCodes, out var code))
            {
                entries.Add(new DropEntry(DropKind.Item, bonus.Chance, code, 0, 0, IsBonus: true));
                bonusLines++;
            }
        }

        return new DropTable(entries, MaxItemsPerKill: r.MaxDropsPerKill, AllPlayersSeeDrops: r.DropsSeenByAll);
    }

    private static bool TryResolveDrop(string? key, IReadOnlyDictionary<string, uint>? itemCodes, out uint code)
    {
        code = 0;
        return key is not null && itemCodes is not null && itemCodes.TryGetValue(key, out code) && code != 0;
    }

    // The NPC code is the record's stable id.
    public static IReadOnlyList<NpcDefinition> ToNpcDefinitions(IReadOnlyList<NpcRecord> records) => records.Select(ToNpcDefinition).ToList();

    private static NpcDefinition ToNpcDefinition(NpcRecord r)
    {
        var weapons = r.ShopItems.Where(static s => s.Tab == 0).OrderBy(static s => s.Ordinal);
        var armour = r.ShopItems.Where(static s => s.Tab == 1).OrderBy(static s => s.Ordinal);
        var misc = r.ShopItems.Where(static s => s.Tab == 2).OrderBy(static s => s.Ordinal);
        var shop = r.ShopItems.Count == 0
            ? NpcShopStock.Empty
            : new NpcShopStock(
                weapons.Select(static s => s.ItemCode).Where(IsSupportedItemCode).Select(ItemCodes.Parse).ToList(),
                armour.Select(static s => s.ItemCode).Where(IsSupportedItemCode).Select(ItemCodes.Parse).ToList(),
                misc.Select(static s => s.ItemCode).Where(IsSupportedItemCode).Select(ItemCodes.Parse).ToList());

        return new NpcDefinition(r.Id, r.Key, ToInxModelPath(r.Model), ToServices(r, shop))
        {
            DisplayName = r.Name ?? "",
            Shop = shop,
            Dialog = r.Dialog,
            SoundCode = r.SoundCode,
            ModelScale = r.ModelScale,
            MoveRange = r.MoveRange,
            PortraitOffsetY = r.PortraitOffsetY,
            PortraitOffsetZ = r.PortraitOffsetZ,
            Hostile = r.Hostile,
            Level = r.Level,
            ShadowSizeLevel = r.SizeLevel,
            AppearSlot = r.AppearSlot,
            AppearCycle = Math.Max(0, r.AppearCycle),
        };
    }

    private static bool IsSupportedItemCode(string code) => ItemCodes.TryParse(code, out _);

    // A shop whose stock has no runtime item is not offered; unknown service kinds from a newer importer are skipped.
    private static List<NpcService> ToServices(NpcRecord r, NpcShopStock shop)
    {
        var services = r.Services
            .OrderBy(static s => s.Ordinal)
            .Where(static s => Enum.IsDefined((NpcServiceKind)s.Kind))
            .Select(static s => new NpcService((NpcServiceKind)s.Kind, s.Value, s.Value2, s.Text))
            .ToList();
        services.RemoveAll(service => service.Kind == NpcServiceKind.Shop && shop.IsEmpty);
        return services;
    }

    public static FieldSpawnData ToFieldSpawnData(FieldSpawnRecord r)
    {
        var points = r.SpawnPoints
            .Select(static p => new SpawnPoint(p.X, p.Z, Active: p.Active))
            .ToList();
        var table = r.MonsterSpawns
            .Select(static s => new SpawnTableEntry(s.MonsterId, s.Weight))
            .ToList();
        var bosses = r.Bosses
            .Select(static b => new BossSpawn(b.MonsterId, b.EscortMonsterId ?? 0, b.EscortCount, b.Hours))
            .ToList();
        var fixedNpcs = r.Npcs
            .Select(static n => new FixedNpc(n.NpcId, n.X, n.Z, n.Angle, n.Y))
            .ToList();

        return new FieldSpawnData(points, table, bosses, fixedNpcs)
        {
            MaxConcurrentMonsters = r.MaxConcurrentMonsters,
            MaxPerSpawnPoint = r.MaxPerSpawnPoint,
            SpawnIntervalMask = r.SpawnInterval,
            PerPointCooldownMs = r.PerPointCooldownMs,
        };
    }

    public static IReadOnlyDictionary<int, FieldSpawnData> ToFieldSpawnData(IEnumerable<FieldSpawnRecord> records) =>
        records.ToDictionary(static r => r.FieldCode, ToFieldSpawnData);

    private static short Channel(int value) => (short)Math.Clamp(value, -255, 255);

    public static MotionTimingCatalog ToMotionTimings(
        IReadOnlyList<MotionModelRecord> models,
        IReadOnlyList<MotionSlotRecord> slots)
    {
        if (models.Count == 0)
            return MotionTimingCatalog.Empty;

        // One MotionTimingSet per motion table (slots arrive grouped by table, ordered by slot).
        var setByTable = new Dictionary<string, MotionTimingSet>();
        var pending = new List<KeyValuePair<int, MotionClip>>();
        string? currentTable = null;

        void FlushTable()
        {
            if (currentTable != null && pending.Count > 0)
                setByTable[currentTable] = MotionTimingSet.Build(pending);
            pending.Clear();
        }

        foreach (var slot in slots)
        {
            if (slot.MotionTableKey != currentTable)
            {
                FlushTable();
                currentTable = slot.MotionTableKey;
            }

            pending.Add(new KeyValuePair<int, MotionClip>(
                slot.State,
                new MotionClip(
                    slot.StartFrame, slot.EndFrame,
                    unchecked((uint)slot.EventFrame0), unchecked((uint)slot.EventFrame1),
                    unchecked((uint)slot.EventFrame2), unchecked((uint)slot.EventFrame3),
                    slot.Repeat)
                {
                    Slot = slot.Slot,
                    Key = (char)(slot.KeyCode & 0xFF),
                }));
        }

        FlushTable();

        var byModel = new Dictionary<string, MotionTimingSet>(models.Count);
        foreach (var model in models)
        {
            if (setByTable.TryGetValue(model.MotionTableKey, out var set))
                byModel[model.ModelKey] = set;
        }

        return new MotionTimingCatalog(byModel);
    }
}
