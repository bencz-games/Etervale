using PristonTale.Simulation.Combat;
using PristonTale.Simulation.Items.Usable;

namespace PristonTale.Simulation.World.Loot;

// Server event rates for the event items: per 10000 drop rolls, 0 for off. Rice cakes (Mandoo) take no rate, only on
// or off.
public sealed record ItemEventRates
{
    public static readonly ItemEventRates None = new();

    public bool Mandoo { get; init; }
    public int ValentineDay { get; init; }
    public int Watermelon { get; init; }
    public int Valentine { get; init; }
    public int CandyDays { get; init; }
    public int WhiteDay { get; init; }
    public int Puzzle { get; init; }
    public int GiftBox { get; init; }

    public bool Any => Mandoo || ValentineDay > 0 || Watermelon > 0 || Valentine > 0 || CandyDays > 0 || WhiteDay > 0 || Puzzle > 0 || GiftBox > 0;
}

// Event items replace a monster's main drop roll. The events are tried in a fixed order and the first that hits gives the
// item; most refuse a player far from the monster's level (more than 15 levels, or 8 below a level-20 monster). Last, a
// kill in the action field gives its event item at a fixed rate, with no event running and at any level.
public static class ItemEventDrops
{
    public const string ActionFieldItem = "GF102";
    public const int ActionFieldRate = 150;

    public readonly record struct EventDrop(string CatalogKey, bool Sealed = false);

    public static EventDrop? Roll(CombatRng rng, ItemEventRates rates, int playerLevel, int monsterLevel, bool actionField = false)
    {
        var gap = Math.Abs(playerLevel - monsterLevel);
        var far = gap > 15 || monsterLevel < 20 && gap > 8;
        if (rates.Mandoo)
        {
            var roll = rng.RollPercent();
            if (roll < 4)
            {
                return new EventDrop("SP101");
            }

            if (roll < 6)
            {
                return new EventDrop("SP102");
            }
        }

        if (Hits(rng, rates.ValentineDay, far))
        {
            return new EventDrop(rng.Range(0, 9) < 7 ? "CH101" : "CH102");
        }

        if (Hits(rng, rates.Watermelon, far))
        {
            return new EventDrop("SP115");
        }

        if (Hits(rng, rates.Valentine, gap > 15))
        {
            return new EventDrop("SP135");
        }

        if (Hits(rng, rates.CandyDays, gap > 15))
        {
            return new EventDrop("SP136");
        }

        if (Hits(rng, rates.WhiteDay, far))
        {
            return new EventDrop(rng.Range(0, 9) < 7 ? "CH103" : "CH104");
        }

        if (rates.Puzzle > 0 && rng.RollPerMyriad() < rates.Puzzle && !far)
        {
            return new EventDrop(Puzzles.DropPiece(rng.RollPerMyriad()), Sealed: true);
        }

        if (Hits(rng, rates.GiftBox, far))
        {
            return new EventDrop(GiftBoxes.BoxForMonsterLevel(monsterLevel));
        }

        if (actionField && rng.RollPerMyriad() < ActionFieldRate)
        {
            return new EventDrop(ActionFieldItem);
        }

        return null;
    }

    // A rate per 10000; a player too far from the monster's level never hits.
    private static bool Hits(CombatRng rng, int rate, bool far)
    {
        if (rate <= 0)
        {
            return false;
        }

        var roll = rng.RollPerMyriad();
        return !far && roll < rate;
    }
}
