using PristonTale.Contracts.Characters;

namespace PristonTale.Simulation.Characters;

internal static class CharacterHeadModels
{
    // A head file is Prefix + its variant number (two digits, unless Numbers spells them) + Suffix.
    internal readonly record struct Pattern(string Prefix, string Suffix, int VariantCount = CharacterVisualSet.HairPotionCount,
        string[] Numbers = null)
    {
        public string Number(int variant) => Numbers is { } numbers ? numbers[variant] : (variant + 1).ToString("D2", System.Globalization.CultureInfo.InvariantCulture);
    }

    // The fifth tier's Priestess heads 6 to 9 carry three digits.
    private static readonly string[] PriestessFifthNumbers = ["01", "02", "03", "04", "05", "006", "007", "008", "009", "10", "11", "12", "13"];

    private static readonly Dictionary<PlayerClass, Pattern[]> ByClass = new()
    {
        [PlayerClass.Mechanician] =
        [
            new("tmh-a", ""),
            new("tmh-a", "a"),
            new("tmh_a", "b"),
            new("tmh-a", "c"),
            new("Tmh-A", "d")
        ],
        [PlayerClass.Fighter] =
        [
            new("tmh-b", ""),
            new("tmh-b", "a"),
            new("tmh_b", "b"),
            new("tmh-b", "c"),
            new("Tmh-b", "d")
        ],
        [PlayerClass.Pikeman] =
        [
            new("tmh-c", ""),
            new("tmh-c", "a"),
            new("tmh_c", "b"),
            new("tmhC-", "c"),
            new("Tmh-c", "d")
        ],
        [PlayerClass.Archer] =
        [
            new("tfh-D", ""),
            new("tfh-D", "a"),
            new("tfh_D", "b"),
            new("tfh-D", "c"),
            new("Tfh-D", "d")
        ],
        [PlayerClass.Knight] =
        [
            new("Mmh-A", ""),
            new("Mmh-A", "a"),
            new("Mmh_A", "b"),
            new("Mmh-A", "c"),
            new("Mmh-A", "d")
        ],
        [PlayerClass.Atalanta] =
        [
            new("Mfh-B", ""),
            new("Mfh-B", "a"),
            new("Mfh_B", "b"),
            new("Mfh-B", "c"),
            new("Mfh-B", "d")
        ],
        [PlayerClass.Priestess] =
        [
            new("Mfh-C", ""),
            new("Mfh-C", "a"),
            new("Mfh_C", "b"),
            new("Mfh-C", "c"),
            new("Mfh-C", "d", Numbers: PriestessFifthNumbers)
        ],
        [PlayerClass.Magician] =
        [
            new("Mmh-D", ""),
            new("Mmh-D", "a"),
            new("Mmh_D", "b"),
            new("Mmh-D", "c"),
            new("Mmh-D", "d")
        ],
        [PlayerClass.Assassin] =
        [
            new("tfh-e", ""),
            new("tfh-e", "a", CharacterFace.FaceCount),
            new("tfh-e", "b", CharacterFace.FaceCount),
            new("tfh-e", "c", CharacterFace.FaceCount),
            new("tfh-e", "d", CharacterFace.FaceCount)
        ],
        [PlayerClass.Shaman] =
        [
            new("Mmh-E", ""),
            new("Mmh-E", "a", CharacterFace.FaceCount),
            new("Mmh-E", "b", CharacterFace.FaceCount),
            new("Mmh-E", "c", CharacterFace.FaceCount),
            new("Mmh-E", "d", CharacterFace.FaceCount)
        ],
        [PlayerClass.MartialArtist] =
        [
            new("tfh-f", ""),
            new("tfh-f", "a", CharacterFace.FaceCount),
            new("tfh-f", "b", CharacterFace.FaceCount),
            new("tfh-f", "c", CharacterFace.FaceCount),
            new("tfh-f", "d", CharacterFace.FaceCount)
        ]
    };

    internal static Pattern Get(PlayerClass playerClass, int tier)
    {
        if (!ByClass.TryGetValue(playerClass, out var tiers))
        {
            throw new ArgumentOutOfRangeException(nameof(playerClass));
        }

        return tiers[tier];
    }
}
