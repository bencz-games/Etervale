using System.Collections.Immutable;
using System.Numerics;
using PristonTale.Simulation.Social.Castle;

namespace PristonTale.Application.Castle;

// The defenders a siege opens with: the owner clan (0 for none), its crystal towers' elements and its mercenaries of each
// kind (CastleSiegeRules.Defenders turns an ownerless castle's into its own).
public sealed record CastleSiegeOrder(long OwnerClanId, ImmutableArray<CastleTower> Towers, ImmutableArray<int> Mercenaries);

// The siege as the castle field last published it (at most once a second while it changes): whether the door still
// stands, the crystal towers and mercenaries of each kind still standing, whether the Tower of Valhalla fell and to which
// clan (the board's leader), and the best clans' damage.
public sealed record CastleSiegeView(bool Running, bool DoorDown, int Crystals, ImmutableArray<int> Mercenaries, bool ValhallaFallen, long Winner,
    ImmutableArray<CastleClanScore> Top, long TotalDamage)
{
    public static CastleSiegeView None { get; } = new(false, false, 0, [], false, 0, [], 0);
}

// A player the siege's end sent back to the castle's arrival area.
public readonly record struct CastleArrival(ulong EntityId, int FieldId, Vector3 Position);

// What Bless Castle asks of the world: know the owner clan, the clan skill and the tax in force (its members' way
// through the wing warp gates, CastleClanSkills, the tax on fees) and hand over the tax gathered, open the siege in the
// castle field (now, or when the field opens), close it (the defenders leave), end it (players outside the owner clan
// go back to the arrival area), and weaken every standing defender to one life (a game master's easy siege).
public interface ICastleWorld
{
    CastleSiegeView Siege { get; }

    void SetOwner(long clanId, CastleClanSkill skill, int tax);

    // The castle's tax gathered from the fees players paid since the last call.
    long TakeTaxGold();

    Task OpenSiegeAsync(CastleSiegeOrder order);

    // ownerClanId: the clan holding the castle as the siege closes, whose members in the castle cheer while it is over.
    Task CloseSiegeAsync(long ownerClanId);

    Task<IReadOnlyList<CastleArrival>> EndSiegeAsync(long ownerClanId);

    Task<bool> WeakenSiegeAsync();

    // The gold the owner's chief pays at the castle's butler, or receives from the treasury (a negative fee).
    Task<World.PreparedNpcPayment?> PrepareOwnerPaymentAsync(Sessions.PlayerSession session, ulong npcEntityId, long fee);

    Task<bool> CommitOwnerPaymentAsync(Sessions.PlayerSession session, World.PreparedNpcPayment payment);

    // The soul fountain's next recipient: a playing member of the owner clan at the fountain in the castle field, the
    // first whose entity id follows after, else the first (none when nobody stands there).
    Task<Sessions.PlayerSession?> FountainRecipientAsync(long ownerClanId, ulong after);

    // One fountain potion into the recipient's bag; Outcome false when it does not fit (nothing is written).
    Task<World.PreparedCharacterOperation<bool>?> PrepareFountainPotionAsync(Sessions.PlayerSession session);

    Task<bool> CommitFountainPotionAsync(Sessions.PlayerSession session, World.PreparedCharacterOperation<bool> operation);
}
