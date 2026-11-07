using PristonTale.Application.Chat;

namespace PristonTale.Application.Clans;

// What other services read of the clans besides an online player's clan: whether they are loaded (until then no clan is
// known), a clan's name (null when there is none) and its id by name in any case (0 when there is none).
public interface IClanDirectory : IClanRoster
{
    bool Available { get; }

    string? NameOf(long clanId);

    // A clan's chief's name, null when there is no such clan.
    string? ChiefNameOf(long clanId);

    long IdOf(string name);

    // An online player's rank in its clan, null without one.
    Simulation.Social.Clans.ClanRank? RankOf(ulong entityId);
}
