using PristonTale.Persistence.Abstractions.Characters;

namespace PristonTale.Persistence.Abstractions.Castle;

// The castle row: the owner's towers (one digit per slot, none before the owner sets them), mercenaries of the three
// kinds, the clan skill chosen and the one in force (CastleClanSkill), the soul fountain's potions and the treasury.
public sealed record CastleRecord(long OwnerClanId, int Tax, int NextTax, string Towers, int MercenaryA, int MercenaryB, int MercenaryC, int ClanSkill,
    int SkillInForce, int FountainPotions, long Treasury)
{
    public static CastleRecord Empty { get; } = new(0, 0, 0, "", 0, 0, 0, 0, 0, 0, 0);
}

// The kinds of clan money movements: Bellatra's income for its operating clan and its chief's redemption of it, the
// castle's tax and its chief's withdrawal.
public enum TreasuryKind : short
{
    Bellatra = 0,
    Tax = 1,
    Withdrawal = 2,
    BellatraRedemption = 3
}

// A movement of the owner clan's treasury: an amount in (positive) or out (negative).
public sealed record TreasuryMove(long ClanId, TreasuryKind Kind, long Amount, long AtSeconds);

public interface ICastleRepository
{
    // The castle, or Empty before the first save.
    Task<CastleRecord> LoadAsync(CancellationToken ct = default);

    Task SaveAsync(CastleRecord castle, CancellationToken ct = default);

    // The castle with the character that paid or received gold (its UpdateSql) and a treasury movement, in one
    // transaction; false when the character's capture is older than the stored one.
    Task<bool> SaveWithAsync(CastleRecord castle, CharacterRecord? character, TreasuryMove? move, CancellationToken ct = default);
}
