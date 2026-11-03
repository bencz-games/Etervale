using PristonTale.Simulation.World;

namespace PristonTale.Simulation.Items.Usable;

// What using an item from the inventory does.
public enum UsableItemAction : byte
{
    PremiumTimers,
    ResetAttributes,
    ResetSkills,
    ResetAttributesAndSkills,
    ScrollEffect,
    TeleportCore,
    HairTint,
    BigHead,
    Firecracker,
    SkillMastery,
    AgingMastery,
    GravityScroll,
    MobileShop,
    ReturnToTown,
    ForceOrb,
    MonsterCrystal,
    CastleItem,
    Restoration,
    Food,
    GiftBox,
    SoccerHead,
    Chocolate,
    PuzzleBag,
    UnionCore
}

// The three scroll effects; a scroll refuses while another scroll of the same effect, or the opposite offensive or
// defensive scroll, runs.
public enum ScrollEffectKind : byte
{
    Invulnerability = 1,
    Critical = 2,
    Evasion = 3
}

public sealed record PremiumGrant(PremiumEffectKind Kind, int Value, long DurationSeconds);

// Argument: the attribute of a single-attribute stone (1..5), the hair model, the class tier of a skill master, the
// equipment group of an aging master, the field of a return core (-1: none), the scroll effect, the castle item, the
// gift box grade or the Virtual Life level of a chocolate.
public sealed record UsableItemRule(UsableItemAction Action, int Argument = 0)
{
    public IReadOnlyList<PremiumGrant> Grants { get; init; } = [];
    public PremiumConflict Conflict { get; init; }
    public int MinimumLevel { get; init; }
    public bool ForbiddenInVillage { get; init; }
}

public static class UsableItemCatalog
{
    public const long Hour = 3600;
    public const long Day = 24 * Hour;

    private static readonly Dictionary<string, UsableItemRule> Rules = Build();

    public static bool TryGet(string catalogKey, out UsableItemRule rule)
    {
        rule = null;
        return !string.IsNullOrEmpty(catalogKey) && Rules.TryGetValue(catalogKey, out rule);
    }

    public static bool IsUsable(string catalogKey) => TryGet(catalogKey, out _);

    public static IReadOnlyCollection<string> Keys => Rules.Keys;

    private static Dictionary<string, UsableItemRule> Build()
    {
        var rules = new Dictionary<string, UsableItemRule>(StringComparer.OrdinalIgnoreCase)
        {
            ["BI101"] = new(UsableItemAction.ResetAttributes),
            ["BI102"] = new(UsableItemAction.ResetSkills),
            ["BI103"] = new(UsableItemAction.ResetAttributesAndSkills),
            ["BI319"] = new(UsableItemAction.ResetAttributes),
            ["BI320"] = new(UsableItemAction.ResetSkills),
            ["BI105"] = new(UsableItemAction.ScrollEffect, (int)ScrollEffectKind.Invulnerability) { ForbiddenInVillage = true },
            ["BI106"] = new(UsableItemAction.ScrollEffect, (int)ScrollEffectKind.Critical) { ForbiddenInVillage = true },
            ["BI107"] = new(UsableItemAction.ScrollEffect, (int)ScrollEffectKind.Evasion) { ForbiddenInVillage = true },
            ["BI108"] = new(UsableItemAction.TeleportCore),
            ["BI109"] = new(UsableItemAction.BigHead),
            ["BI123"] = new(UsableItemAction.Firecracker),
            ["BI136"] = new(UsableItemAction.AgingMastery, 0),
            ["BI137"] = new(UsableItemAction.AgingMastery, 1),
            ["BI138"] = new(UsableItemAction.AgingMastery, 2),
            ["BI162"] = new(UsableItemAction.AgingMastery, 3),
            ["BI163"] = new(UsableItemAction.AgingMastery, 4),
            ["BI164"] = new(UsableItemAction.AgingMastery, 5),
            ["BI139"] = new(UsableItemAction.SkillMastery, 1),
            ["BI140"] = new(UsableItemAction.SkillMastery, 2),
            ["BI141"] = new(UsableItemAction.SkillMastery, 3),
            ["BI142"] = new(UsableItemAction.MobileShop),
            ["BI184"] = new(UsableItemAction.GravityScroll),
            ["EC101"] = new(UsableItemAction.ReturnToTown, FieldCodes.Ricarten),
            ["EC102"] = new(UsableItemAction.ReturnToTown, FieldCodes.Navisko),
            ["EC104"] = new(UsableItemAction.ReturnToTown, FieldCodes.Pillai),
            ["EC103"] = new(UsableItemAction.ReturnToTown, -1),
            ["EC105"] = new(UsableItemAction.UnionCore),
            ["BC105"] = new(UsableItemAction.Restoration),
            ["SP140"] = new(UsableItemAction.SoccerHead),
            ["SP134"] = new(UsableItemAction.GiftBox, GiftBoxes.TigerCapsule)
        };

        foreach (var crystal in MonsterCrystals.All)
        {
            rules[crystal.CatalogKey] = new UsableItemRule(UsableItemAction.MonsterCrystal);
        }

        foreach (var castle in CastleItems.All)
        {
            rules[castle.CatalogKey] = new UsableItemRule(UsableItemAction.CastleItem, castle.Index);
        }

        foreach (var key in EventFoods.Keys)
        {
            rules[key] = new UsableItemRule(UsableItemAction.Food);
        }

        for (var grade = 1; grade <= 6; grade++)
        {
            rules[$"SP1{grade + 4:00}"] = new UsableItemRule(UsableItemAction.GiftBox, grade);
        }

        foreach (var chocolate in Chocolates.All)
        {
            rules[chocolate.CatalogKey] = new UsableItemRule(UsableItemAction.Chocolate, chocolate.Level);
        }

        foreach (var piece in Puzzles.Pieces)
        {
            rules[piece] = new UsableItemRule(UsableItemAction.PuzzleBag);
        }

        // A sealed piece reaches its owner as the lucky bag.
        rules[Puzzles.LuckyBagIcon] = new UsableItemRule(UsableItemAction.PuzzleBag);

        foreach (var (key, _) in ForceOrbs.All)
        {
            rules[key] = new UsableItemRule(UsableItemAction.ForceOrb) { ForbiddenInVillage = true };
        }

        for (var attribute = 1; attribute <= 5; attribute++)
        {
            rules[$"BI14{4 + attribute}"] = new UsableItemRule(UsableItemAction.ResetAttributes, attribute);
        }

        var hairKeys = new[] { "BI116", "BI117", "BI118", "BI213", "BI214", "BI215", "BI216", "BI217", "BI218", "BI219", "BI220", "BI221", "BI222" };
        for (var hair = 0; hair < hairKeys.Length; hair++)
        {
            rules[hairKeys[hair]] = new UsableItemRule(UsableItemAction.HairTint, hair);
        }

        Single(rules, "BI112", PremiumEffectKind.ThirdEyes, 50, Day);
        Single(rules, "BI114", PremiumEffectKind.ThirdEyes, 50, 7 * Day);
        Single(rules, "BI178", PremiumEffectKind.ThirdEyes, 50, Hour);
        Single(rules, "BI113", PremiumEffectKind.ExperienceUp, 30, Day);
        Single(rules, "BI115", PremiumEffectKind.ExperienceUp, 30, 7 * Day);
        Single(rules, "BI124", PremiumEffectKind.ExperienceUp, 50, Day);
        Single(rules, "BI125", PremiumEffectKind.ExperienceUp, 50, 7 * Day);
        Single(rules, "BI179", PremiumEffectKind.ExperienceUp, 50, Hour);
        Single(rules, "BI143", PremiumEffectKind.ExperienceUp, 100, Day);
        Single(rules, "BI144", PremiumEffectKind.ExperienceUp, 100, 7 * Day);
        Single(rules, "BI150", PremiumEffectKind.ExperienceUp, 100, 30 * Day);
        Single(rules, "BI180", PremiumEffectKind.ExperienceUp, 100, Hour);
        Single(rules, "BI119", PremiumEffectKind.VampiricCuspid, 0, 3 * Hour);
        Single(rules, "BI120", PremiumEffectKind.VampiricCuspid, 0, Day);
        Single(rules, "BI181", PremiumEffectKind.VampiricCuspid, 0, Hour);
        Single(rules, "BI186", PremiumEffectKind.VampiricCuspidEx, 0, Hour);
        Single(rules, "BI187", PremiumEffectKind.VampiricCuspidEx, 0, 3 * Hour);
        Single(rules, "BI188", PremiumEffectKind.VampiricCuspidEx, 0, Day);
        Single(rules, "BI121", PremiumEffectKind.ManaRecharging, 0, 3 * Hour);
        Single(rules, "BI122", PremiumEffectKind.ManaRecharging, 0, Day);
        Single(rules, "BI182", PremiumEffectKind.ManaRecharging, 0, Hour);
        Single(rules, "BI128", PremiumEffectKind.ManaReduce, 30, Day);
        Single(rules, "BI129", PremiumEffectKind.ManaReduce, 30, 7 * Day);
        Single(rules, "BI183", PremiumEffectKind.ManaReduce, 30, Hour);
        Single(rules, "BI258", PremiumEffectKind.ManaReduce, 30, 3 * Hour);
        Single(rules, "BI191", PremiumEffectKind.StaminaReduce, 30, Day);
        Single(rules, "BI192", PremiumEffectKind.StaminaReduce, 30, 7 * Day);
        Single(rules, "BI238", PremiumEffectKind.StaminaReduce, 30, Hour);
        Single(rules, "BI259", PremiumEffectKind.StaminaReduce, 30, 3 * Hour);
        Single(rules, "BI132", PremiumEffectKind.PhoenixPet, 0, Day);
        Single(rules, "BI133", PremiumEffectKind.PhoenixPet, 0, 7 * Day);
        Single(rules, "BI134", PremiumEffectKind.PhoenixPet, 0, 3 * Hour);
        Single(rules, "BI151", PremiumEffectKind.PhoenixPet, 0, 30 * Day);
        Single(rules, "BI177", PremiumEffectKind.PhoenixPet, 0, Hour);

        Booster(rules, PremiumEffectKind.LifeBooster, "BC121", "BC122", "BC123");
        Booster(rules, PremiumEffectKind.ManaBooster, "BC124", "BC125", "BC126");
        Booster(rules, PremiumEffectKind.StaminaBooster, "BC127", "BC128", "BC129");
        rules["BC130"] = new UsableItemRule(UsableItemAction.PremiumTimers) { Grants = [new PremiumGrant(PremiumEffectKind.SkillDelay, 0, 300)] };
        rules["BC131"] = new UsableItemRule(UsableItemAction.PremiumTimers) { Grants = [new PremiumGrant(PremiumEffectKind.SkillDelay, 0, 3 * Hour)] };
        rules["BC132"] = new UsableItemRule(UsableItemAction.PremiumTimers) { Grants = [new PremiumGrant(PremiumEffectKind.SkillDelay, 0, Day)] };

        Pets(rules, 30 * Day, "BI152", "BI153", "BI154", "BI155");
        Pets(rules, 7 * Day, "BI165", "BI166", "BI167", "BI168");
        Pets(rules, Day, "BI169", "BI170", "BI171", "BI172");

        // Packages run Third Eyes and an EXP potion together at the package's own percentages.
        Package(rules, 20, 10, ("BI201", 3 * Hour), ("BI202", Day), ("BI203", 7 * Day), ("BI204", 30 * Day), ("BI126", Day), ("BI127", 7 * Day));
        Package(rules, 30, 20, ("BI205", 3 * Hour), ("BI206", Day), ("BI207", 7 * Day), ("BI208", 30 * Day), ("BI130", Day), ("BI131", 7 * Day));
        Package(rules, 40, 30, ("BI209", 3 * Hour), ("BI210", Day), ("BI211", 7 * Day), ("BI212", 30 * Day), ("BI135", 7 * Day));
        Package(rules, 50, 40, ("BI223", 3 * Hour), ("BI224", Day), ("BI225", 7 * Day), ("BI226", 30 * Day), ("BI245", 3 * Hour), ("BI246", Day),
            ("BI247", 7 * Day));
        return rules;
    }

    private static void Single(Dictionary<string, UsableItemRule> rules, string key, PremiumEffectKind kind, int value, long seconds)
    {
        rules[key] = new UsableItemRule(UsableItemAction.PremiumTimers) { Grants = [new PremiumGrant(kind, value, seconds)] };
    }

    // Boosters raise the maximum by 15% for 1 h, 3 h or 1 day; refused in towns; each kind runs once.
    private static void Booster(Dictionary<string, UsableItemRule> rules, PremiumEffectKind kind, string hour, string threeHours, string day)
    {
        rules[hour] = new UsableItemRule(UsableItemAction.PremiumTimers) { Grants = [new PremiumGrant(kind, PremiumRules.BoosterPercent, Hour)], ForbiddenInVillage = true };
        rules[threeHours] = new UsableItemRule(UsableItemAction.PremiumTimers)
        {
            Grants = [new PremiumGrant(kind, PremiumRules.BoosterPercent, 3 * Hour)],
            ForbiddenInVillage = true
        };
        rules[day] = new UsableItemRule(UsableItemAction.PremiumTimers) { Grants = [new PremiumGrant(kind, PremiumRules.BoosterPercent, Day)], ForbiddenInVillage = true };
    }

    private static void Pets(Dictionary<string, UsableItemRule> rules, long seconds, params string[] keys)
    {
        PremiumEffectKind[] pets = [PremiumEffectKind.HelpPetTerry, PremiumEffectKind.HelpPetNepsys, PremiumEffectKind.HelpPetIo, PremiumEffectKind.HelpPetMute];
        for (var index = 0; index < keys.Length; index++)
        {
            rules[keys[index]] = new UsableItemRule(UsableItemAction.PremiumTimers)
            {
                Grants = [new PremiumGrant(pets[index], 0, seconds)],
                Conflict = PremiumConflict.HelperPet,
                MinimumLevel = 10
            };
        }
    }

    private static void Package(Dictionary<string, UsableItemRule> rules, int dropPercent, int experiencePercent, params (string Key, long Seconds)[] items)
    {
        foreach (var (key, seconds) in items)
        {
            rules[key] = new UsableItemRule(UsableItemAction.PremiumTimers)
            {
                Grants = [new PremiumGrant(PremiumEffectKind.ThirdEyes, dropPercent, seconds), new PremiumGrant(PremiumEffectKind.ExperienceUp, experiencePercent, seconds)],
                Conflict = PremiumConflict.ThirdEyesPackage
            };
        }
    }

    // Might of Awell adds its value to the carrying capacity while it runs; each Gravity Scroll adds 50 for good.
    public const int GravityScrollWeight = 50;
    public const int MaxGravityScrolls = 5;
}
