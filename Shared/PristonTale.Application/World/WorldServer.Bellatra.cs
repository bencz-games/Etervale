using System.Globalization;
using System.Threading.Channels;
using PristonTale.Application.Sessions;
using PristonTale.Contracts.Social;
using PristonTale.Contracts.Texts;
using PristonTale.Simulation.Combat;
using PristonTale.Simulation.World;
using PristonTale.Simulation.World.Bellatra;
using PristonTale.Simulation.World.Npcs;

namespace PristonTale.Application.World;

// Gold the world owes a player for Bellatra, with the line that tells it; paid off the tick (BellatraPrizes). The score
// counts as points for the entrant's registered clan (none: 0) on the server's local day it was won (BellatraDays), and
// Tax is the gold kept from the score for the operating clan.
public sealed record BellatraPrize(PlayerSession Session, long Gold, GameText Text, long ClanId, long Points, long Tax, int Day);

// Bellatra's game (BellatraGame), stepped on the tick after the fields: rounds as the game clock reaches their hours
// (each hour reached counts, even when the clock jumps), monsters into the rooms of the round being played, the teams
// that go on, the prizes and the return to the village. A day whose game has no entrant at round 1 plays nothing.
public sealed partial class WorldServer
{
    private readonly BellatraGame _bellatra = new();
    private readonly CombatRng _bellatraRng = new(0xBE11A);
    private readonly Channel<BellatraPrize> _bellatraPrizes = Channel.CreateUnbounded<BellatraPrize>(new UnboundedChannelOptions
    {
        SingleReader = true,
        AllowSynchronousContinuations = false
    });

    public ChannelReader<BellatraPrize> BellatraPrizes => _bellatraPrizes.Reader;

    private int GameHour => (int)WorldClock.HoursAt(GameClockMs);

    private bool BellatraOpen => BellatraRules.IsOpen(_options.Bellatra.Mode, _clock.GetLocalNow().Hour);

    private BellatraBooth Booth() => new(BellatraOpen, GameHour, _bellatra);

    // Tick only: a ticket's commit enters its team (two tickets prepared together may pass the team's levels by one).
    private void EnterBellatra(PlayerSession session, int team)
    {
        if (session.Character is not { } character || team is < 0 or >= BellatraArena.Teams)
        {
            return;
        }

        _bellatra.Entrants[character.Name] = new BellatraGame.Entrant(team, character.Class.Class);
        _bellatra.TeamLevels[team] += character.Level;
    }

    private void StepBellatra(long nowMs)
    {
        var game = _bellatra;
        StepBellatraMoves(nowMs);
        DeliverBellatraNotices();
        var hour = GameHour;
        // A jump of the clock reaches several hours at once; each reached starts its round in turn.
        var started = 0;
        while (game.LastHour >= 0 && started <= BellatraRounds.Count && BellatraRules.HourReached(game.LastHour, hour, game.NextHour) && StartBellatraRound(nowMs))
        {
            started++;
        }

        game.LastHour = hour;
        if (game.Next == 0)
        {
            if (game.OutAtMs != 0 && nowMs >= game.OutAtMs)
            {
                foreach (var name in game.Entrants.Keys)
                {
                    if (InBellatra(name, out var connection))
                    {
                        SendToBellatraVillage(connection, nowMs, 0);
                    }
                }

                game.Reset();
            }

            return;
        }

        if (game.Playing is { } round && nowMs >= game.DelayUntilMs && nowMs >= game.NextPulseMs
            && _fields.TryGetValue(BellatraArena.Field, out var arena))
        {
            game.NextPulseMs = nowMs + BellatraRules.PulseMs;
            arena.SpawnBellatraRound(game, round);
        }
    }

    // Every round's monster still standing falls and entrants away from the arena lose their place; the round after
    // starts, skipping the final when no player reached its need, or the game finishes after the last or with nobody
    // left. Before rounds 4 and 7 the best teams with somebody left cheer and go on to their rooms, the others are paid
    // and leave. False when the day's game has no entrant to start with.
    private bool StartBellatraRound(long nowMs)
    {
        var game = _bellatra;
        if (game.Next == 0 && (!game.BetweenGames || game.Entrants.Count == 0))
        {
            return false;
        }

        var arena = GetField(BellatraArena.Field);
        Span<bool> present = stackalloc bool[BellatraArena.Teams];
        foreach (var (name, entrant) in game.Entrants.ToList())
        {
            if (InBellatra(name, out _))
            {
                present[entrant.Team] = true;
                entrant.Outcome = BellatraOutcome.Playing;
            }
            else
            {
                game.Entrants.Remove(name);
            }
        }

        if (game.Next < BellatraRounds.Count && BellatraRounds.Tables[game.Table][game.Next].SkipBelow is var need && need > 0
            && !game.Entrants.Values.Any(entrant => entrant.Score >= need))
        {
            game.Next++;
        }

        arena.KillBellatraMonsters(potions: game.Next >= BellatraRounds.Count);
        if (game.Next >= BellatraRounds.Count || game.Entrants.Count == 0)
        {
            FinishBellatra(nowMs);
            return true;
        }

        var round = game.Next + 1;
        var next = BellatraRounds.Tables[game.Table][game.Next];
        Array.Clear(game.Opened);
        Array.Clear(game.BossOpened);
        game.Monsters = arena.ResolveBellatraMonsters(next, out var boss);
        game.Boss = boss;
        game.DelayUntilMs = nowMs + next.DelaySeconds * 1000L;
        if (BellatraArena.GoOn(round) < BellatraArena.Teams)
        {
            var ranks = BellatraRules.Rank(game.TeamScores, present);
            (game.StageTeams, game.GoingOn) = (0, 0);
            for (var rank = 0; rank < ranks.Length; rank++)
            {
                if (present[ranks[rank]])
                {
                    game.StageTeams |= 1 << ranks[rank];
                    game.GoingOn |= rank < BellatraArena.GoOn(round) ? 1 << ranks[rank] : 0;
                }
            }

            game.Revision++;
            foreach (var (name, entrant) in game.Entrants.ToList())
            {
                var rank = Array.IndexOf(ranks, entrant.Team);
                InBellatra(name, out var connection);
                if (rank < BellatraArena.GoOn(round))
                {
                    entrant.Outcome = BellatraOutcome.GoesOn;
                    connection.Player.QueueYahoo(heals: false);
                    game.Moves.Add(new BellatraGame.Move(name, BellatraArena.Field, BellatraArena.Rooms[BellatraArena.AdvanceRoom(rank, round)].Centre,
                        nowMs + BellatraRules.BannerMs));
                }
                else
                {
                    PayBellatra(connection, entrant, GameText.Of(GameTextId.BellatraOut, (rank + 1).ToString(CultureInfo.InvariantCulture),
                        BellatraRules.Prize(entrant.Score, _options.Bellatra.Tax).ToString(CultureInfo.InvariantCulture)));
                    entrant.Outcome = BellatraOutcome.Out;
                    game.Leaving[name] = game.ViewOf(entrant) with { Outcome = BellatraOutcome.Out };
                    game.Entrants.Remove(name);
                    SendToBellatraVillage(connection, nowMs, BellatraRules.BannerMs);
                }
            }
        }

        game.Next++;
        game.Revision++;
        return true;
    }

    // Every entrant still in the arena is paid its score less the tax and cheers; all go to the village OutSeconds later.
    private void FinishBellatra(long nowMs)
    {
        var game = _bellatra;
        game.Final = new BellatraResult(true, BellatraResultLines(-1));
        foreach (var (name, entrant) in game.Entrants)
        {
            if (InBellatra(name, out var connection))
            {
                PayBellatra(connection, entrant, GameText.Of(GameTextId.BellatraCleared, BellatraRules.Prize(entrant.Score, _options.Bellatra.Tax)));
                connection.Player.QueueYahoo(heals: false);
            }

            entrant.Outcome = BellatraOutcome.Cleared;
        }

        game.Revision++;
        game.Next = 0;
        game.OutAtMs = nowMs + BellatraRules.OutSeconds * 1000L;
        game.Table = _bellatraRng.NextExclusive(0, BellatraRounds.Tables.Length);
    }

    private void PayBellatra(Connection connection, BellatraGame.Entrant entrant, GameText text)
    {
        if (connection.Session is { } session)
        {
            _bellatraPrizes.Writer.TryWrite(new BellatraPrize(session, BellatraRules.Prize(entrant.Score, _options.Bellatra.Tax), text,
                connection.Player.Clan is { Registered: true } clan ? clan.ClanId : 0, entrant.Score, BellatraRules.TaxOf(entrant.Score, _options.Bellatra.Tax),
                BellatraDays.Of(_clock.GetLocalNow())));
        }
    }

    // The village's centre, as the original sends the failed and the finished.
    private void SendToBellatraVillage(Connection connection, long nowMs, int afterMs)
    {
        var centre = _catalog.TryGetFieldByCode(BellatraArena.Village, out var village) ? village.Center : FieldPoint.Zero;
        _bellatra.Moves.Add(new BellatraGame.Move(connection.Player.Character.Name, BellatraArena.Village, new System.Numerics.Vector3(centre.X, 0f, centre.Z),
            nowMs + afterMs));
    }

    // A move waits while its player lies dead, and is dropped once the player left the arena (or, to another room, the
    // game).
    private void StepBellatraMoves(long nowMs)
    {
        var moves = _bellatra.Moves;
        for (var index = moves.Count - 1; index >= 0; index--)
        {
            var move = moves[index];
            if (nowMs < move.DueMs)
            {
                continue;
            }

            var found = TryGetBellatraConnection(move.Name, out var connection);
            if (found && connection.Player.IsDead && connection.FieldId == BellatraArena.Field)
            {
                continue;
            }

            moves.RemoveAt(index);
            if (found && connection.Session is { } session && connection.FieldId == BellatraArena.Field
                && (move.FieldCode != BellatraArena.Field || _bellatra.Entrants.ContainsKey(move.Name)))
            {
                Arrive(session, new TeleportArrival(move.FieldCode, move.Position));
            }
        }
    }

    private void DeliverBellatraNotices()
    {
        if (!_fields.TryGetValue(BellatraArena.Field, out var arena))
        {
            return;
        }

        foreach (var (killerId, score) in arena.BellatraBossKills)
        {
            Notify(killerId, ChatKind.Whisper, GameText.Of(GameTextId.BellatraBossDown, score));
        }

        arena.BellatraBossKills.Clear();
        foreach (var (picker, team, seal) in arena.BellatraSeals)
        {
            foreach (var (name, entrant) in _bellatra.Entrants)
            {
                if (entrant.Team == team && InBellatra(name, out var member))
                {
                    var notice = seal == BellatraSeal.Points
                        ? GameText.Of(GameTextId.BellatraPoints, picker, BellatraRules.PointsBonus.ToString(CultureInfo.InvariantCulture))
                        : GameText.Of(BellatraRules.NoticeOf(seal), picker);
                    Notify(member.Player.EntityId, ChatKind.System, notice);
                }
            }
        }

        arena.BellatraSeals.Clear();
    }

    // The board of a player in the arena: its own while it plays, its last one after it left the game until it leaves the
    // arena; none elsewhere.
    private BellatraView? BellatraViewOf(Connection connection)
    {
        if (connection.FieldId != BellatraArena.Field)
        {
            return null;
        }

        var name = connection.Player.Character.Name;
        return _bellatra.Entrants.TryGetValue(name, out var entrant) ? _bellatra.ViewOf(entrant) : _bellatra.Leaving.GetValueOrDefault(name);
    }

    // The result window of a player asking for its own game (team 0): the game's final result while its entrant waits
    // after the end, otherwise every team's live result; a team booth's (team 1 to 4) is that team's live result.
    public Task<BellatraResult> BellatraResultAsync(ulong entityId, int team) =>
        EnqueueAsync(entityId, () => team == 0 && _bellatra.Final is { } final && _connections.TryGetValue(entityId, out var connection)
            && _bellatra.Entrants.ContainsKey(connection.Player.Character.Name)
            ? final
            : new BellatraResult(false, BellatraResultLines(BellatraRules.TeamOfBooth(team))));

    // The entrants in the arena of a team (-1 every team), best score first.
    private BellatraResultLine[] BellatraResultLines(int team)
    {
        var lines = new List<BellatraResultLine>();
        foreach (var (name, entrant) in _bellatra.Entrants)
        {
            if ((team < 0 || entrant.Team == team) && InBellatra(name, out var connection))
            {
                lines.Add(new BellatraResultLine(connection.Player.Character.Name, entrant.Class, entrant.Score, entrant.Kills));
            }
        }

        return [.. lines.OrderByDescending(static line => line.Score).Take(BellatraLimits.ResultLines)];
    }

    // Whether the entrant plays in the arena now.
    private bool InBellatra(string name, out Connection connection) =>
        TryGetBellatraConnection(name, out connection) && connection.FieldId == BellatraArena.Field && BellatraArena.InArea(connection.Player.Position);

    private bool TryGetBellatraConnection(string name, out Connection connection)
    {
        foreach (var candidate in _connections.Values)
        {
            if (candidate.Player.Character.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
            {
                connection = candidate;
                return true;
            }
        }

        connection = null!;
        return false;
    }

    // A Bellatra prize into the player's gold: Outcome is the gold paid, 0 when it would pass the gold limit.
    public Task<PreparedCharacterOperation<long>?> PrepareBellatraPrizeAsync(PlayerSession session, long gold) =>
        EnqueueAsync<PreparedCharacterOperation<long>?>(NoOwner, () =>
        {
            if (session.Loaded is null || !TryGetPlaying(session, out var connection))
            {
                return null;
            }

            var paid = false;
            var (candidate, snapshot) = Receive(session, connection, null, candidate => paid = gold > 0 && candidate.TryChangeMoney(gold));
            return paid
                ? new PreparedCharacterOperation<long>(connection.Player.Character, candidate, snapshot, gold)
                : new PreparedCharacterOperation<long>(connection.Player.Character, null, null, 0);
        }, waitForCapacity: true);

    public Task<bool> CommitBellatraPrizeAsync(PlayerSession session, PreparedCharacterOperation<long> operation) =>
        EnqueueAsync(NoOwner, () =>
        {
            CheckReceived(session, operation, "a Bellatra prize");
            if (!operation.Original.TryChangeMoney(operation.Outcome))
            {
                throw new CommitInvariantException("The gold changed during a Bellatra prize's commit.");
            }

            return true;
        }, waitForCapacity: true);
}
