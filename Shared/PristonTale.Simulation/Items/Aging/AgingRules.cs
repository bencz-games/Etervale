using PristonTale.Simulation.World.Ai;

namespace PristonTale.Simulation.Items.Aging;

// The stone is kept because some stones keep the level requirement when the level comes.
public sealed record AgingMaturity(int Count, int Goal, AgingStone Stone);

// Percentages of the original price, failure chances and double chances.
public sealed record AgingSettings(int PricePercent = 100, int FailurePercent = 100, int DoublePercent = 100)
{
    public static readonly AgingSettings Default = new();
}

public enum AgingStone : byte
{
    None = 0,
    AgingStone = 1,
    CopperOre = 2,
    ElderCopperOre = 3,
    SuperAgingStone = 4,
    SuperiorCopperOre = 5
}

// A fall of one or two levels matures one level back.
public enum AgingResult : byte
{
    Destroyed = 0,
    DownOne = 1,
    DownTwo = 2,
    Success = 3,
    Double = 4
}

// How an ageable item family ages: what its uses count toward the next level and against which goal table, whether each
// level wears its durability, and what each level adds (taken back on a fall): damage, attack rating, a critical or block
// point on odd levels, mana, absorption per step and defence by percent. A game master ages directly only the families
// marked so.
internal sealed record AgingFamily(AgingCounter Counter, int[] Goal, bool Wears, bool Damage = false, int AttackRating = 0, bool Critical = false,
    int Mana = 0, bool Block = false, float AbsorbStep = 0f, int DefencePercent = 0, bool GameMasterAging = true);

// The stats an aged item shows as raised by its aging.
[Flags]
public enum AgedStats
{
    None = 0,
    Damage = 1,
    Critical = 2,
    Defence = 4,
    AttackRating = 8,
    Absorb = 16,
    Block = 32,
    Integrity = 64,
    Mana = 128
}

public enum AgingCounter : byte
{
    None = 0,
    Attack = 1,
    Critical = 2,
    Block = 3,
    Hit = 4,
    Time = 5
}

public static class AgingRules
{
    public const int MaxLevel = 20;

    // Attacks, blocks and blows count only against a foe within this many levels.
    public const int CountLevelSpread = 10;

    // Costumes and timed items of ageable families that the NPC refuses.
    private static readonly HashSet<string> Refused = new(StringComparer.OrdinalIgnoreCase)
    {
        "DA131", "DA132", "DA133", "DA134", "DA135", "DA136", "DA137", "DA138", "DA139", "DA140", "DA141", "DA142", "DA143", "DA144", "DA145",
        "DA146", "DA148", "DA154", "DA155",
        "DA231", "DA232", "DA233", "DA234", "DA235", "DA236", "DA237", "DA238", "DA239", "DA240", "DA241", "DA242", "DA243", "DA244", "DA245",
        "DA246", "DA248", "DA254", "DA255",
        "OA231", "OA232", "OA233", "OA234", "DB131", "DB132", "DB133", "DB134", "QT107", "QT108"
    };

    // Laid out as the window shows them, in four rows of three.
    private static readonly int[][] Sheltoms =
    [
        [3, 3, 4, 4, 5], [3, 3, 4, 4, 5, 5], [3, 3, 4, 4, 5, 5, 6], [3, 3, 4, 4, 5, 5, 6, 6], [3, 3, 4, 4, 5, 5, 6, 6, 7],
        [3, 3, 4, 4, 5, 5, 6, 6, 7, 7], [3, 3, 4, 4, 5, 5, 6, 6, 7, 7, 8], [3, 3, 4, 4, 5, 5, 6, 6, 7, 7, 8, 8],
        [4, 4, 5, 5, 6, 6, 7, 7, 8, 8, 9], [4, 4, 5, 5, 6, 6, 7, 7, 8, 8, 9, 9],
        [5, 5, 6, 6, 7, 7, 8, 8, 9, 9, 10], [5, 5, 6, 6, 7, 7, 8, 8, 9, 9, 10, 10], [6, 6, 7, 7, 8, 8, 9, 9, 10, 10, 11],
        [6, 6, 7, 7, 8, 8, 9, 9, 10, 10, 11, 11], [7, 7, 8, 8, 9, 9, 10, 10, 11, 11, 12], [7, 7, 8, 8, 9, 9, 10, 10, 11, 11, 12, 12],
        [8, 8, 9, 9, 10, 10, 11, 11, 12, 12, 13], [8, 8, 9, 9, 10, 10, 11, 11, 12, 12, 13, 13], [9, 9, 10, 10, 11, 11, 12, 12, 13, 13, 14],
        [9, 9, 10, 10, 11, 11, 12, 12, 13, 13, 14, 14]
    ];

    // Chance in percent (against 0..100) that the aging of each level fails.
    private static readonly int[] FailPlain = [0, 0, 0, 0, 0, 5, 10, 15, 20, 30, 35, 40, 45, 50, 55, 65, 75, 85, 95, 95];
    // The aging stone and the super aging stone share their failure chances.
    private static readonly int[] FailAgingStone = [0, 0, 0, 0, 0, 3, 5, 8, 11, 18, 21, 25, 29, 33, 38, 48, 60, 74, 90, 90];
    private static readonly int[] FailSuperiorOre = [0, 0, 0, 0, 0, 3, 7, 11, 14, 22, 26, 31, 35, 40, 45, 55, 67, 79, 93, 93];

    // Chance in percent that a level that did not fail is gained at once (the aging stone doubles it).
    private static readonly int[] DoubleChance = [30, 25, 20, 10, 10, 10, 10, 5, 5, 5, 5, 4, 4, 3, 3, 2, 2, 2, 0, 0];

    // A failure at each level: below the first threshold the copy falls one level, below the second two, else it is lost.
    private static readonly (int One, int Two)[] FailureSplit =
    [
        (101, 101), (101, 101), (101, 101), (101, 101), (101, 101), (101, 101), (90, 101), (80, 101), (70, 85), (60, 80), (50, 70), (40, 60),
        (30, 50), (30, 40), (20, 30), (10, 20), (10, 20), (10, 20), (10, 20), (10, 20), (10, 20)
    ];

    private static readonly int[] AttackGoal =
        [100, 130, 169, 219, 284, 369, 479, 622, 808, 1049, 1362, 1769, 2297, 2983, 3874, 5031, 6534, 8486, 11021, 14313, 14313];
    private static readonly int[] CriticalGoal =
        [12, 16, 21, 27, 35, 45, 58, 75, 97, 126, 164, 213, 277, 360, 468, 608, 790, 1026, 1332, 1730, 1730];
    private static readonly int[] BlockGoal =
        [15, 19, 25, 32, 42, 55, 71, 92, 119, 155, 201, 261, 339, 440, 571, 742, 964, 1252, 1626, 2112, 2112];
    private static readonly int[] HitGoal =
        [45, 58, 75, 97, 126, 164, 213, 277, 360, 468, 608, 790, 1026, 1332, 1730, 2247, 2918, 3790, 4922, 6392, 6392];

    // Bows and javelins count critical hits toward the attack table's goal, as in the original.
    private static readonly Dictionary<string, AgingFamily> Families = Build();
    private static readonly Dictionary<string, AgingFamily>.AlternateLookup<ReadOnlySpan<char>> FamiliesByPrefix =
        Families.GetAlternateLookup<ReadOnlySpan<char>>();
    private const int FamilyLength = 3;

    // The ring that ages counts twenty minutes.
    private const string AgeableRing = "OR201";
    private const int RingGoal = 60 * 20;

    private static Dictionary<string, AgingFamily> Build()
    {
        var axe = new AgingFamily(AgingCounter.Attack, AttackGoal, Wears: true, Damage: true, AttackRating: 10);
        var claw = new AgingFamily(AgingCounter.Critical, CriticalGoal, Wears: false, Damage: true, AttackRating: 5, Critical: true);
        var robe = new AgingFamily(AgingCounter.Hit, HitGoal, Wears: true, AbsorbStep: 0.5f, DefencePercent: 10);
        var armor = new AgingFamily(AgingCounter.Hit, HitGoal, Wears: true, AbsorbStep: 0.5f, DefencePercent: 5);
        return new Dictionary<string, AgingFamily>(StringComparer.OrdinalIgnoreCase)
        {
            ["WA1"] = axe,
            ["WH1"] = axe,
            ["WC1"] = claw,
            ["WP1"] = claw,
            ["WS2"] = claw,
            ["WM1"] = new(AgingCounter.Attack, AttackGoal, Wears: false, Damage: true, AttackRating: 10, Mana: 10),
            ["WS1"] = new(AgingCounter.Critical, AttackGoal, Wears: true, Damage: true, Critical: true),
            ["WT1"] = new(AgingCounter.Critical, AttackGoal, Wears: false, Damage: true, Critical: true),
            ["DS1"] = new(AgingCounter.Block, BlockGoal, Wears: true, Block: true, AbsorbStep: 0.2f),
            ["OM1"] = robe with { GameMasterAging = false },
            ["DB1"] = robe,
            ["DG1"] = robe,
            ["DA1"] = armor,
            ["DA2"] = armor,
            ["OA2"] = new(AgingCounter.Hit, HitGoal, Wears: true, AttackRating: 5, DefencePercent: 10, GameMasterAging: false)
        };
    }

    // Read on every counted attack, block and second of a worn maturing item, so it allocates no substring.
    private static AgingFamily FamilyOf(ItemInstance item)
    {
        var key = item.Definition.CatalogKey;
        return key is { Length: >= FamilyLength } && FamiliesByPrefix.TryGetValue(key.AsSpan(0, FamilyLength), out var family) ? family : null;
    }

    // What a level of the family changes, as the item tooltip marks it. Bows and javelins mark their added mana too,
    // though their aging adds none, as the original tooltip does.
    public static AgedStats RaisedBy(string key)
    {
        if (!Families.TryGetValue(ItemCodes.FamilyOf(key), out var family))
        {
            return AgedStats.None;
        }

        var stats = AgedStats.None;
        stats |= family.Damage ? AgedStats.Damage : 0;
        stats |= family.Critical ? AgedStats.Critical : 0;
        stats |= family.DefencePercent > 0 ? AgedStats.Defence : 0;
        stats |= family.AttackRating > 0 ? AgedStats.AttackRating : 0;
        stats |= family.AbsorbStep > 0 ? AgedStats.Absorb : 0;
        stats |= family.Block ? AgedStats.Block : 0;
        stats |= family.Wears ? AgedStats.Integrity : 0;
        stats |= family.Mana > 0 || ItemCodes.FamilyOf(key) is "WS1" or "WT1" ? AgedStats.Mana : 0;
        return stats;
    }

    private static bool IsRing(string key) => string.Equals(key, AgeableRing, StringComparison.OrdinalIgnoreCase);


    public static bool Ageable(ItemInstance item) =>
        Ageable(item.Definition.CatalogKey, item.Definition.Kind, item.Definition.UniqueItem, item.MixSheltom, item.Maturity is not null, item.UpgradeLevel);

    public static bool Ageable(string key, ItemKind kind, int unique, int mixSheltom, bool maturing, int level) =>
        (Families.ContainsKey(ItemCodes.FamilyOf(key)) || IsRing(key))
        && !Refused.Contains(key) && unique == 0 && kind != ItemKind.Quest && mixSheltom == 0 && !maturing && level < MaxLevel;

    public static IReadOnlyList<int> SheltomsFor(int level) => Sheltoms[Math.Clamp(level, 0, Sheltoms.Length - 1)];

    // The magic sheltoms do not age.
    public static int SheltomOrdinal(string key) =>
        ItemCodes.FamilyOf(key) == "OS1" && int.TryParse(key.AsSpan(3), out var ordinal) && ordinal is >= 1 and <= 14 ? ordinal : 0;

    public static AgingStone StoneOf(string key) => key.ToUpperInvariant() switch
    {
        "BI110" => AgingStone.AgingStone,
        "BI111" => AgingStone.CopperOre,
        "BI160" => AgingStone.ElderCopperOre,
        "BI161" => AgingStone.SuperAgingStone,
        "BI185" => AgingStone.SuperiorCopperOre,
        _ => AgingStone.None
    };

    public static long Price(ItemInstance item, AgingSettings settings = null) => Price(item.UpgradeLevel, item.Roll.Price, settings?.PricePercent ?? 100);

    public static long Price(int level, long price, int percent = 100) => (level + 1) * price / 2 * Math.Max(0, percent) / 100;

    public static bool SheltomsMatch(int level, IEnumerable<int> ordinals)
    {
        var wanted = SheltomsFor(level).Order().ToArray();
        var given = ordinals.Order().ToArray();
        return wanted.SequenceEqual(given);
    }

    // Three draws of 0..100 (destroy, down, plus); the settings scale the failure and double chances.
    public static AgingResult Roll(int level, AgingStone stone, ISimRng rng, AgingSettings settings = null)
    {
        settings ??= AgingSettings.Default;
        var destroy = rng.NextInclusive(0, 100);
        var down = rng.NextInclusive(0, 100);
        var plus = rng.NextInclusive(0, 100);
        var index = Math.Clamp(level, 0, FailPlain.Length - 1);
        var fail = stone switch
        {
            AgingStone.AgingStone or AgingStone.SuperAgingStone => FailAgingStone[index],
            AgingStone.SuperiorCopperOre => FailSuperiorOre[index],
            _ => FailPlain[index]
        };
        fail = Math.Clamp(fail * settings.FailurePercent / 100, 0, 101);

        var result = AgingResult.Success;
        if (fail > destroy)
        {
            // A failure where the level has no split (up to +5) leaves the aging a success, as the original does.
            var split = FailureSplit[Math.Clamp(level, 0, FailureSplit.Length - 1)];
            if (split.One <= 100)
            {
                result = down < split.One ? AgingResult.DownOne : down < split.Two ? AgingResult.DownTwo : AgingResult.Destroyed;
            }
        }
        else if (stone is not (AgingStone.SuperAgingStone or AgingStone.SuperiorCopperOre) && level < 10
                 && DoubleChance[index] * (stone == AgingStone.AgingStone ? 2 : 1) * settings.DoublePercent / 100 > plus)
        {
            result = AgingResult.Double;
        }

        if (result == AgingResult.Destroyed && stone is AgingStone.CopperOre or AgingStone.ElderCopperOre or AgingStone.SuperAgingStone
                or AgingStone.SuperiorCopperOre)
        {
            result = rng.RollPercent() < 81 ? AgingResult.DownOne : AgingResult.DownTwo;
        }

        return result;
    }

    // A fall starts the next maturity half way.
    public static void Apply(ItemInstance item, AgingResult result, AgingStone stone)
    {
        if (result == AgingResult.Destroyed)
        {
            return;
        }

        if (result == AgingResult.Double)
        {
            LevelUp(item, stone);
        }
        else if (result is AgingResult.DownOne or AgingResult.DownTwo)
        {
            LevelDown(item, (int)result + 1);
        }

        var goal = GoalFor(item);
        item.Maturity = new AgingMaturity(result <= AgingResult.DownTwo ? goal / 2 : 0, goal, stone);
    }

    public static AgingCounter CounterOf(ItemInstance item) =>
        FamilyOf(item)?.Counter ?? (IsRing(item.Definition.CatalogKey) ? AgingCounter.Time : AgingCounter.None);

    public static bool Matured(ItemInstance item) => item.Maturity is { Goal: > 0 } maturity && maturity.Count >= maturity.Goal;

    // Stops at the goal; the level comes with Mature, in a stored change of the character.
    public static bool Tally(ItemInstance item, int amount = 1)
    {
        if (item.Maturity is not { Goal: > 0 } maturity || maturity.Count >= maturity.Goal || amount <= 0)
        {
            return false;
        }

        var count = (int)Math.Min(maturity.Goal, (long)maturity.Count + amount);
        item.Maturity = maturity with { Count = count };
        return count >= maturity.Goal;
    }

    public static bool Mature(ItemInstance item)
    {
        if (!Matured(item))
        {
            return false;
        }

        LevelUp(item, item.Maturity.Stone);
        item.Maturity = null;
        return true;
    }

    public static void AgeTo(ItemInstance item, int level)
    {
        if (FamilyOf(item) is not { GameMasterAging: true })
        {
            return;
        }

        while (item.UpgradeLevel < Math.Min(level, MaxLevel))
        {
            LevelUp(item, AgingStone.None);
        }
    }

    public static bool Count(ItemInstance item, int amount = 1)
    {
        if (item.Maturity is not { } maturity)
        {
            return false;
        }

        var count = (int)Math.Min(maturity.Goal, (long)maturity.Count + amount);
        if (maturity.Goal > 0 && count >= maturity.Goal)
        {
            LevelUp(item, maturity.Stone);
            item.Maturity = null;
            return true;
        }

        item.Maturity = maturity with { Count = count };
        return false;
    }

    private static int GoalFor(ItemInstance item)
    {
        var level = Math.Clamp(item.UpgradeLevel, 0, AttackGoal.Length - 1);
        return IsRing(item.Definition.CatalogKey) ? RingGoal : (FamilyOf(item)?.Goal ?? HitGoal)[level];
    }

    // The level requirement rises on even levels unless the item asks level 100, 102 or 105 or the aging used the elder
    // copper ore or a super stone.
    public static void LevelUp(ItemInstance item, AgingStone stone)
    {
        var level = item.UpgradeLevel;
        var roll = item.Roll;
        var odd = level % 2 == 1;
        int Damage() => 1 + (level >= 9 ? 1 : 0) + (level >= 19 ? 1 : 0);
        float Absorb(float step) => step * (1 + (level >= 9 ? 1 : 0) + (level >= 19 ? 1 : 0));
        var family = FamilyOf(item);
        var wear = family?.Wears ?? false;
        if (family is not null)
        {
            var damage = family.Damage ? Damage() : 0;
            roll = roll with
            {
                DamageMin = roll.DamageMin + damage,
                DamageMax = roll.DamageMax + damage,
                AttackRating = roll.AttackRating + family.AttackRating,
                CriticalBonus = roll.CriticalBonus + (family.Critical && odd ? 1 : 0),
                AddMana = roll.AddMana + family.Mana,
                Block = roll.Block + (family.Block && odd ? 1 : 0),
                Absorb = roll.Absorb + Absorb(family.AbsorbStep),
                Defence = roll.Defence + (family.DefencePercent > 0 ? (int)(roll.Defence / (100f / family.DefencePercent)) : 0)
            };
        }

        if (wear)
        {
            item.DurabilityMax = Math.Max(0, item.DurabilityMax - (item.DurabilityMax / 100 + 1));
            item.DurabilityCurrent = Math.Min(item.DurabilityCurrent, item.DurabilityMax);
        }

        item.UpgradeLevel = level + 1;
        var fixedLevel = roll.RequiredLevel is 100 or 102 or 105 || stone is AgingStone.ElderCopperOre or AgingStone.SuperAgingStone
            or AgingStone.SuperiorCopperOre;
        if (!fixedLevel && item.UpgradeLevel % 2 == 0)
        {
            roll = roll with { RequiredLevel = roll.RequiredLevel + 1 };
        }

        item.Roll = roll;
    }

    // Falls `steps` levels (2 or 3: the aging that follows matures one back).
    public static void LevelDown(ItemInstance item, int steps)
    {
        var old = item.UpgradeLevel;
        var level = Math.Max(0, old - steps);
        item.UpgradeLevel = level;
        var odd = level % 2 == 1;
        var roll = item.Roll;
        var damage = old < 10 ? steps : steps switch
        {
            1 => 2,
            2 => old == 10 ? 3 : 4,
            _ => old == 10 ? 4 : old == 11 ? 5 : 6
        };
        float Absorb(float step)
        {
            var taken = step * steps;
            var above = old - 9;
            return above <= 0 ? taken : taken + (above == 1 ? step : step * steps);
        }

        int Critical() => 1 + (steps > 2 && odd ? 1 : 0);
        int Defence(int percent)
        {
            var value = roll.Defence;
            for (var index = 0; index < steps; index++)
            {
                value = (int)(100f * value / (100f + percent));
            }

            return value;
        }

        var family = FamilyOf(item);
        var wear = family?.Wears ?? false;
        if (family is not null)
        {
            var lost = family.Damage ? damage : 0;
            roll = roll with
            {
                DamageMin = roll.DamageMin - lost,
                DamageMax = roll.DamageMax - lost,
                AttackRating = roll.AttackRating - family.AttackRating * steps,
                CriticalBonus = roll.CriticalBonus - (family.Critical ? Critical() : 0),
                AddMana = roll.AddMana - family.Mana * steps,
                Block = roll.Block - (family.Block ? Critical() : 0),
                Absorb = roll.Absorb - Absorb(family.AbsorbStep),
                Defence = family.DefencePercent > 0 ? Defence(family.DefencePercent) : roll.Defence
            };
        }

        if (wear)
        {
            item.DurabilityMax += (item.DurabilityMax / 100 + 1) * steps;
        }

        var required = roll.RequiredLevel;
        if (required < 100)
        {
            required--;
        }

        if (steps > 2 && odd)
        {
            required--;
        }

        item.Roll = roll with { RequiredLevel = Math.Max(1, required) };
    }
}
