using System.Numerics;
using System.Threading.Channels;
using PristonTale.Application.Sessions;
using PristonTale.Simulation.Combat;
using PristonTale.Simulation.Quests;
using PristonTale.Simulation.World;

namespace PristonTale.Application.World;

public sealed partial class WorldServer
{
    // Touched only by the tick thread.
    private readonly CombatRng _questRng = new(0x5155);

    // Sessions with quest work for the upkeep worker; written from the field phase, read off the tick.
    private readonly Channel<PlayerSession> _questUpkeep = SessionRequests();

    public ChannelReader<PlayerSession> QuestUpkeepRequests => _questUpkeep.Reader;

    private void ConfigureQuests(FieldInstance field)
    {
        field.QuestClock = () => UnixNowMs;
        field.QuestUpkeepRequested = player =>
        {
            // Read-only lookup during the field phase: the connection map changes only in the command phase.
            if (_connections.TryGetValue(player.EntityId, out var connection) && connection.Session is { } session)
            {
                _questUpkeep.Writer.TryWrite(session);
            }
            else
            {
                player.QuestState.UpkeepSignaled = false;
            }
        };
    }

    public Task<PreparedQuestOperation?> PrepareQuestAsync(PlayerSession session, QuestCommand command)
    {
        return EnqueueAsync<PreparedQuestOperation?>(session.EntityId, () =>
        {
            if (session.Loaded is null || !TryGetPlaying(session, out var connection) || !_fields.TryGetValue(connection.FieldId, out var field))
            {
                return null;
            }

            return QuestTransactions.Prepare(session, connection.Player, field, command, _worldData.Current.Items, _catalog, _questRng,
                UnixNowMs);
        });
    }

    public Task<bool> CommitQuestAsync(PlayerSession session, PreparedQuestOperation operation)
    {
        return EnqueueAsync(NoOwner, () =>
        {
            // SaveGate remains held through commit, including when the connection leaves during storage I/O.
            if (!ReferenceEquals(session.Character, operation.Original) || operation.Candidate is null)
            {
                throw new CommitInvariantException("Character ownership changed during quest commit.");
            }

            operation.Original.CommitQuestOperation(operation.Candidate, operation.Tier, operation.StatPoints, operation.SkillPoints,
                operation.EliteSkillPoints, operation.MoneyDelta, operation.ResetSkills);
            if (operation.Experience > 0)
            {
                operation.Original.AwardExperience(operation.Experience);
            }
            if (!TryGetOwnConnection(session, out var connection))
            {
                return true;
            }

            var state = connection.Player.QuestState;
            if (operation.Upkeep)
            {
                state.Post(operation.Outcome.Notices);
                state.UpkeepDue = false;
                state.UpkeepSignaled = false;
            }

            if (operation.Outcome.Travel is { } travel)
            {
                Travel(connection, travel);
            }

            if (operation.Outcome.Duel is { } duel && _connections.TryGetValue(session.EntityId, out var arrived)
                && arrived.FieldId == QuestArena.FieldCode)
            {
                var arena = GetField(QuestArena.FieldCode);
                arena.OpenDuel(arrived.Player, duel, arena.NowMs);
            }

            return true;
        }, waitForCapacity: true);
    }

    public Task<PreparedQuestOperation?> PrepareQuestUpkeepAsync(PlayerSession session)
    {
        return EnqueueAsync<PreparedQuestOperation?>(NoOwner, () =>
        {
            if (session.Loaded is null || !TryGetPlaying(session, out var connection) || !_fields.TryGetValue(connection.FieldId, out var field))
            {
                return null;
            }

            var prepared = QuestTransactions.PrepareUpkeep(session, connection.Player, field, _worldData.Current.Items, _questRng, UnixNowMs);
            if (prepared.Candidate is null)
            {
                connection.Player.QuestState.UpkeepDue = false;
                connection.Player.QuestState.UpkeepSignaled = false;
            }

            return prepared;
        }, waitForCapacity: true);
    }

    // The upkeep could not run now (the save gate stayed busy): the field signals it again on its next check.
    public Task<bool> DeferQuestUpkeepAsync(PlayerSession session)
    {
        return EnqueueAsync(NoOwner, () =>
        {
            if (TryGetOwnConnection(session, out var connection))
            {
                connection.Player.QuestState.UpkeepSignaled = false;
            }

            return true;
        }, waitForCapacity: true);
    }

    // Field changes the quests asked for during the field phase (home after falling in the arena).
    private void ApplyQuestTravels()
    {
        List<Connection>? travelling = null;
        foreach (var connection in _connections.Values)
        {
            if (connection.Player.QuestState.PendingTravel is not null)
            {
                (travelling ??= []).Add(connection);
            }
        }

        if (travelling is null)
        {
            return;
        }

        foreach (var connection in travelling)
        {
            var travel = connection.Player.QuestState.PendingTravel!;
            connection.Player.QuestState.PendingTravel = null;
            Travel(connection, travel);
        }
    }

    // Moves a player to the quest's destination; the presence encoder confirms the new field to the client.
    private void Travel(Connection connection, QuestTravel travel)
    {
        if (!_catalog.TryGetFieldByCode(travel.FieldCode, out var destination))
        {
            return;
        }

        var position = GetField(travel.FieldCode).Terrain.PlaceOnFloor(QuestTransactions.TravelPoint(destination, travel));
        var inVillage = destination.State == FieldState.Village;
        Relocate(connection, travel.FieldCode, position, inVillage);
        connection.Player.EnterField(position, inVillage);
    }

    // The quest arena is left on entry: a character saved there starts in its home town.
    public static bool StartsAway(int fieldCode) => fieldCode == QuestArena.FieldCode;

    internal static Vector3 HomeStart(WorldCatalog catalog, int homeField)
    {
        return catalog.TryGetFieldByCode(homeField, out var home)
            ? QuestTransactions.TravelPoint(home, new QuestTravel(QuestTravelKind.Home, homeField, 0, 0))
            : Vector3.Zero;
    }
}
