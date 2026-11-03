namespace PristonTale.Simulation.Items.Usable;

// One reward of a finished puzzle: the item, its weight out of 10000 and how many come.
public readonly record struct PuzzleReward(string CatalogKey, int Weight, int Count = 1);

// The puzzle event. Monsters drop puzzle pieces sealed as lucky bags; a right click with 1000 gold reveals the piece.
// The eight pieces of one set laid out in their cells of the inventory's last four columns complete the puzzle, which
// the puzzle collector exchanges for one reward. The first set (PZ101-108) counts one per piece, the second (PZ201-208)
// two; a full set is 8 or 16 with no piece twice.
public static class Puzzles
{
    public const int RevealCost = 1000;
    public const int ExchangeWeight = 120;
    public const string LuckyBagName = "Lucky Bag";
    public const string LuckyBagIcon = "PZ100";

    // The puzzle board: the 4 x 4 cells from column 8 of the inventory.
    public const int BoardColumn = 8;
    public const int BoardSize = 4;

    // The NPC event quest number of the puzzle collector.
    public const int CollectorEvent = 2;

    public static IReadOnlyList<string> Pieces { get; } =
        ["PZ101", "PZ102", "PZ103", "PZ104", "PZ105", "PZ106", "PZ107", "PZ108", "PZ201", "PZ202", "PZ203", "PZ204", "PZ205", "PZ206", "PZ207", "PZ208"];

    public const int PiecesPerSet = 8;

    // Each piece's top-left cell on the board (column, row), pieces 1 to 8.
    private static readonly (int Column, int Row)[] Cells = [(8, 0), (10, 0), (8, 1), (9, 1), (9, 2), (11, 1), (8, 3), (10, 3)];

    // Drop weights of pieces 1 to 8 in each set (out of 10000 over both sets).
    private static readonly int[] DropWeights = [743, 723, 704, 650, 650, 650, 380, 500];

    public static IReadOnlyList<PuzzleReward> Rewards { get; } =
    [
        new("BI123", 2500), new("PM104", 2500, 10), new("OS106", 1590), new("GP109", 1500), new("OR228", 700), new("OA133", 700), new("GP121", 500),
        new("OS109", 10)
    ];

    // When a reward cannot be made the collector hands out thirty Mystic Mana Potions instead.
    public static PuzzleReward Fallback { get; } = new("PM104", 0, 30);

    public static bool IsPiece(string catalogKey) => Pieces.Contains(catalogKey, StringComparer.OrdinalIgnoreCase);

    // 1..8 of a piece key, and its set (1 or 2), or 0.
    public static int Number(string catalogKey) => IsPiece(catalogKey) ? catalogKey[4] - '0' : 0;

    public static int Set(string catalogKey) => IsPiece(catalogKey) ? catalogKey[2] - '0' : 0;

    public static (int Column, int Row) Cell(int number) => Cells[number - 1];

    // A piece belongs at the anchor of its cell.
    public static bool InPlace(string catalogKey, int anchor, int inventoryWidth)
    {
        var number = Number(catalogKey);
        if (number == 0)
        {
            return false;
        }

        var (column, row) = Cell(number);
        return anchor == row * inventoryWidth + column;
    }

    // The anchors of a finished board: the eight revealed pieces of one set, each in its board cell, from the keys and
    // anchors of the carried items; empty when no set is finished.
    public static IReadOnlyList<int> Board(IEnumerable<(string CatalogKey, int Anchor)> items, int inventoryWidth)
    {
        var placed = items.Where(item => InPlace(item.CatalogKey, item.Anchor, inventoryWidth)).ToList();
        for (var set = 1; set <= 2; set++)
        {
            var anchors = placed.Where(item => Set(item.CatalogKey) == set).Select(static item => item.Anchor).Distinct().ToList();
            if (anchors.Count == PiecesPerSet)
            {
                return anchors;
            }
        }

        return [];
    }

    public static bool Complete(IEnumerable<(string CatalogKey, int Anchor)> items, int inventoryWidth) => Board(items, inventoryWidth).Count == PiecesPerSet;

    // The value of a set of exchanged pieces, or 0 when a piece repeats or is not a piece.
    public static int Value(IEnumerable<string> catalogKeys)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var value = 0;
        foreach (var key in catalogKeys)
        {
            if (!IsPiece(key) || !seen.Add(key))
            {
                return 0;
            }

            value += Set(key);
        }

        return value;
    }

    public static bool IsComplete(int value) => value is 8 or 16;

    // The reward for a roll in 0..10000 inclusive; a roll on a boundary takes the earlier reward.
    public static PuzzleReward Draw(int roll)
    {
        var sum = 0;
        foreach (var reward in Rewards)
        {
            sum += reward.Weight;
            if (roll <= sum)
            {
                return reward;
            }
        }

        return Rewards[^1];
    }

    // The piece a puzzle drop gives for a roll in 0..9999.
    public static string DropPiece(int roll)
    {
        var sum = 0;
        for (var set = 1; set <= 2; set++)
        {
            for (var number = 1; number <= 8; number++)
            {
                sum += DropWeights[number - 1];
                if (roll < sum)
                {
                    return $"PZ{set}0{number}";
                }
            }
        }

        return "PZ208";
    }
}
