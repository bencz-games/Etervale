using PristonTale.Simulation.Items;
namespace PristonTale.Application.GameMasters;

// Where gold left the economy: the totals since the server started, as the game masters' currency report shows them, and
// what the game log has not stored yet (TakeUnrecorded).
public enum CurrencySink
{
    Mixing = 0,
    Aging = 1,
    Shop = 2,
    LifePotions = 3,
    ManaPotions = 4,
    StaminaPotions = 5,
    ForceOrbs = 6,
    Teleports = 7,
    SkillTraining = 8,
    BellatraTickets = 9
}

public sealed class CurrencyLedger
{
    private readonly long[] _totals = new long[Enum.GetValues<CurrencySink>().Length];
    private readonly long[] _unrecorded = new long[Enum.GetValues<CurrencySink>().Length];

    public void Record(CurrencySink sink, long gold)
    {
        if (gold > 0)
        {
            Interlocked.Add(ref _totals[(int)sink], gold);
            Interlocked.Add(ref _unrecorded[(int)sink], gold);
        }
    }

    // The gold each sink took since the last take, by CurrencySink; Restore puts back a take the log could not store.
    public long[] TakeUnrecorded()
    {
        var taken = new long[_unrecorded.Length];
        for (var sink = 0; sink < taken.Length; sink++)
        {
            taken[sink] = Interlocked.Exchange(ref _unrecorded[sink], 0);
        }

        return taken;
    }

    public void Restore(long[] taken)
    {
        for (var sink = 0; sink < taken.Length; sink++)
        {
            Interlocked.Add(ref _unrecorded[sink], taken[sink]);
        }
    }

    public long this[CurrencySink sink] => Interlocked.Read(ref _totals[(int)sink]);

    public long Total
    {
        get
        {
            long total = 0;
            for (var index = 0; index < _totals.Length; index++)
            {
                total += Interlocked.Read(ref _totals[index]);
            }

            return total;
        }
    }

    // A shop purchase counts as potions of its kind when it is a life, mana or stamina potion.
    public static CurrencySink ShopSinkFor(string catalogKey) => PotionDefinition.FamilyOf(catalogKey) switch
    {
        PotionResource.Life => CurrencySink.LifePotions,
        PotionResource.Mana => CurrencySink.ManaPotions,
        PotionResource.Stamina => CurrencySink.StaminaPotions,
        _ => CurrencySink.Shop
    };
}
