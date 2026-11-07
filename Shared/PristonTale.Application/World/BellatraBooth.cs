using PristonTale.Contracts.Texts;
using PristonTale.Simulation.Characters;
using PristonTale.Simulation.World;
using PristonTale.Simulation.World.Bellatra;
using PristonTale.Simulation.World.Npcs;

namespace PristonTale.Application.World;

// A Bellatra team booth as the world stands when a request reaches it.
internal sealed class BellatraBooth(bool open, int gameHour, BellatraGame game)
{
    public NpcServiceStatus Refusal(PlayerCharacter character, int team)
    {
        if (game.Entrants.ContainsKey(character.Name))
        {
            return NpcServiceStatus.BellatraEntered;
        }

        if (open && !game.BetweenGames)
        {
            return NpcServiceStatus.BellatraNoTickets;
        }

        return BellatraRules.Sells(open, character.Level, gameHour, game.TeamLevels[team]) switch
        {
            BellatraEntry.Ok => NpcServiceStatus.Ok,
            BellatraEntry.Closed => NpcServiceStatus.BellatraClosed,
            BellatraEntry.LevelTooLow => NpcServiceStatus.BellatraLevel,
            BellatraEntry.TeamFull => NpcServiceStatus.BellatraTeamFull,
            _ => NpcServiceStatus.BellatraNoTickets
        };
    }

    // The booth's answer on opening: why it refuses, or the ticket's price with the ticket offered as the one destination
    // its player buys (the destination's index is not read).
    public GameText Answer(PlayerCharacter character, int team, WorldCatalog world, out TeleportOffer? ticket)
    {
        ticket = null;
        if (Refusal(character, team) is var refusal && refusal != NpcServiceStatus.Ok)
        {
            return NpcServiceTransactions.Describe(refusal);
        }

        ticket = new TeleportOffer(0, BellatraArena.Field, world.TryGetFieldByCode(BellatraArena.Field, out var arena) ? arena.Name : "",
            BellatraRules.Fee(character.Level), BellatraRules.MinLevel);
        return GameText.Of(GameTextId.BellatraTicket, BellatraRules.Fee(character.Level));
    }

    // The candidate pays the ticket and goes to its team's room.
    public NpcServiceStatus Admit(PlayerCharacter candidate, int team, out TeleportArrival? arrival)
    {
        arrival = null;
        var refusal = Refusal(candidate, team);
        if (refusal != NpcServiceStatus.Ok)
        {
            return refusal;
        }

        if (!candidate.TryChangeMoney(-BellatraRules.Fee(candidate.Level)))
        {
            return NpcServiceStatus.NotEnoughGold;
        }

        arrival = new TeleportArrival(BellatraArena.Field, BellatraArena.Rooms[team].Centre);
        return NpcServiceStatus.Ok;
    }
}
