using PristonTale.Simulation.World.Bellatra;
using PristonTale.Simulation.World.Monsters;

namespace PristonTale.Application.World;

// Bellatra's arena field: the rounds' monsters it opens in their rooms, and the score its entrants' kills give.
public sealed partial class FieldInstance
{
    // The world's game, set on the arena field only.
    internal BellatraGame? Bellatra { get; set; }

    internal int BellatraScoreDivisor { get; set; } = BellatraRules.DefaultScoreDivisor;

    // The round bosses entrants killed since the world last told them, with their score.
    internal List<(ulong KillerId, long Score)> BellatraBossKills { get; } = [];

    // The round's monsters by the catalog, resolved once a round (null for a name it lacks).
    internal MonsterDefinition?[] ResolveBellatraMonsters(BellatraRound round, out MonsterDefinition? boss)
    {
        boss = round.Boss != "" && _catalog.TryFind(round.Boss, out var found) ? found : null;
        var monsters = new MonsterDefinition?[round.Monsters.Length];
        for (var index = 0; index < monsters.Length; index++)
        {
            monsters[index] = _catalog.TryFind(round.Monsters[index].Name, out var monster) ? monster : null;
        }

        return monsters;
    }

    // One monster a pulse in each room of the round with no more than the round's cap standing; a room's boss once the
    // room has opened more than the cap less BellatraRound.BossMargin.
    internal void SpawnBellatraRound(BellatraGame game, BellatraRound round)
    {
        var (first, end) = BellatraArena.RoomsOf(game.Round);
        for (var room = first; room < end; room++)
        {
            var standing = 0;
            foreach (var monster in _monsters.Values)
            {
                standing += monster.BellatraRoom == room && monster.IsAlive ? 1 : 0;
            }

            if (standing > round.MaxMonsters)
            {
                continue;
            }

            var boss = game.Boss is not null && !game.BossOpened[room] && game.Opened[room] > round.MaxMonsters - BellatraRound.BossMargin;
            if ((boss ? game.Boss : game.Monsters[round.Pick(_rng.NextExclusive(0, 100))]) is not { } definition)
            {
                continue;
            }

            game.BossOpened[room] |= boss;
            game.Opened[room]++;
            var opened = ((Simulation.World.Spawning.ISpawnSink)this).Spawn(definition, BellatraArena.Rooms[room].RandomPoint(_rng), -1,
                Simulation.World.Spawning.FieldSpawner.RandomFacing(_rng));
            opened.BellatraRoom = room;
            opened.BellatraBoss = boss;
        }
    }

    internal void KillBellatraMonsters()
    {
        foreach (var monster in _monsters.Values)
        {
            if (monster.BellatraRoom >= 0 && monster.IsAlive)
            {
                KillOutright(monster);
            }
        }
    }

    // An entrant's kill of a round's monster while a round is played: the score to the entrant and its team, a round
    // boss's told to the killer.
    internal void ScoreBellatra(MonsterInstance killed, ulong killerId)
    {
        if (Bellatra is not { Round: > 0 } game || killed.BellatraRoom < 0 || !_players.TryGetValue(killerId, out var killer)
            || !game.Entrants.TryGetValue(killer.Character.Name, out var entrant))
        {
            return;
        }

        var score = BellatraRules.Score((long)Math.Min(killed.Def.ExpReward, long.MaxValue), BellatraScoreDivisor, killed.BellatraBoss, game.Round);
        entrant.Score += score;
        game.TeamScores[entrant.Team] += score;
        game.Revision++;
        if (killed.BellatraBoss)
        {
            BellatraBossKills.Add((killerId, score));
        }
    }
}
