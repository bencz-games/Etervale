namespace PristonTale.Persistence.Abstractions.Castle;

// The castle row: the owner's towers (one digit per slot, none before the owner sets them), mercenaries of the three
// kinds, the clan skill chosen and the one in force (CastleClanSkill), the soul fountain's potions and the treasury.
public sealed record CastleRecord(long OwnerClanId, int Tax, int NextTax, string Towers, int MercenaryA, int MercenaryB, int MercenaryC, int ClanSkill,
    int SkillInForce, int FountainPotions, long Treasury)
{
    public static CastleRecord Empty { get; } = new(0, 0, 0, "", 0, 0, 0, 0, 0, 0, 0);
}

public interface ICastleRepository
{
    // The castle, or Empty before the first save.
    Task<CastleRecord> LoadAsync(CancellationToken ct = default);

    Task SaveAsync(CastleRecord castle, CancellationToken ct = default);
}
