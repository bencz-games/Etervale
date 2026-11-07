namespace PristonTale.Application.Chat;

// The party a player belongs to, for party chat. MembersOf returns every member including the player, or nothing when
// the player is not in a party.
public interface IPartyRoster
{
    IReadOnlyCollection<ulong> MembersOf(ulong entityId);
}

// The clan a player belongs to, for clan chat; 0 when the player has no clan.
public interface IClanRoster
{
    long ClanOf(ulong entityId);
}

// Used while the server runs no clan system: nobody has a clan, so clan lines are not accepted.
public sealed class NoClans : IClanRoster
{
    public static readonly NoClans Instance = new();

    public long ClanOf(ulong entityId) => 0;
}
