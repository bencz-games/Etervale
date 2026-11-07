using System.Collections.Immutable;

namespace PristonTale.Simulation.Social.Castle;

public enum CastleOwnerStatus : byte
{
    Ok = 0,
    NotOwner = 1,
    NotChief = 2,
    OutOfReach = 3,
    Busy = 4,
    NotEnoughGold = 5,
    InvalidSetup = 6,
    DuringSiege = 7,
    NotEnoughTreasury = 8,
    TooMuchGold = 9,
    Unavailable = 10
}

// What the chief of the clan holding Bless Castle sets at the castle's butler: the tax rate and the clan skill that take
// effect at the next CastleRules.TaxHour, the element of each crystal tower slot and the mercenaries of each kind for the
// next siege.
public sealed record CastleSetup(int Tax, CastleClanSkill Skill, ImmutableArray<CastleTower> Towers, ImmutableArray<int> Mercenaries)
{
    public static CastleSetup Empty { get; } =
        new(0, CastleClanSkill.None, [.. Enumerable.Repeat(CastleTower.None, CastleSiegeRules.TowerSlots)], [.. Enumerable.Repeat(0, CastleSiegeRules.MercenaryKinds)]);
}

// The owner's purchases, paid from the chief's gold: a crystal tower in an empty slot costs TowerPrice (choosing another
// element for a bought one is free), each mercenary its kind's price. A tower bought stays, and a mercenary hired serves
// until the next siege ends; the setup only grows. The tax runs 0 to MaxTaxRate.
public static class CastleOwnerRules
{
    public const int MaxTaxRate = 10;
    public const long TowerPrice = 500_000;

    // Ricarten militia, Ricarten guard and Bless royal guard, each.
    public static ImmutableArray<long> MercenaryPrices { get; } = [50_000, 100_000, 300_000];

    // What the wanted setup costs over the current one, or InvalidSetup.
    public static CastleOwnerStatus Cost(CastleSetup current, CastleSetup wanted, out long cost)
    {
        cost = 0;
        if (wanted.Tax is < 0 or > MaxTaxRate || !Enum.IsDefined(wanted.Skill) || wanted.Towers.Length != CastleSiegeRules.TowerSlots
            || wanted.Mercenaries.Length != CastleSiegeRules.MercenaryKinds)
        {
            return CastleOwnerStatus.InvalidSetup;
        }

        for (var slot = 0; slot < CastleSiegeRules.TowerSlots; slot++)
        {
            var had = slot < current.Towers.Length ? current.Towers[slot] : CastleTower.None;
            if (!Enum.IsDefined(wanted.Towers[slot]) || had != CastleTower.None && wanted.Towers[slot] == CastleTower.None)
            {
                return CastleOwnerStatus.InvalidSetup;
            }

            cost += had == CastleTower.None && wanted.Towers[slot] != CastleTower.None ? TowerPrice : 0;
        }

        for (var kind = 0; kind < CastleSiegeRules.MercenaryKinds; kind++)
        {
            var had = kind < current.Mercenaries.Length ? current.Mercenaries[kind] : 0;
            if (wanted.Mercenaries[kind] < had || wanted.Mercenaries[kind] > CastleSiegeRules.MercenaryMax)
            {
                return CastleOwnerStatus.InvalidSetup;
            }

            cost += (wanted.Mercenaries[kind] - had) * MercenaryPrices[kind];
        }

        return CastleOwnerStatus.Ok;
    }
}
