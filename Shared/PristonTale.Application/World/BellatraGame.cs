using System.Numerics;
using PristonTale.Simulation.World.Bellatra;
using PristonTale.Simulation.World.Monsters;

namespace PristonTale.Application.World;

// One Bellatra game, owned by the world: changed by commands and the serial part of the tick, and by the Bellatra
// field's step (scores) while no other part runs. Next is the round that starts next (0 before the first; past the last
// the game finishes); the round being played is Next while it is 1 to BellatraRounds.Count. Entrants and moves name
// characters, which a player keeps across a relog.
internal sealed class BellatraGame
{
    internal sealed class Entrant(int team)
    {
        public int Team { get; } = team;
        public long Score { get; set; }
    }

    // A character told to leave a room for another or for the village once its banner has shown.
    internal readonly record struct Move(string Name, int FieldCode, Vector3 Position, long DueMs);

    public Dictionary<string, Entrant> Entrants { get; } = new(StringComparer.OrdinalIgnoreCase);
    public long[] TeamScores { get; } = new long[BellatraArena.Teams];
    public int[] TeamLevels { get; } = new int[BellatraArena.Teams];
    public int[] Opened { get; } = new int[BellatraArena.Rooms.Length];
    public bool[] BossOpened { get; } = new bool[BellatraArena.Rooms.Length];
    public List<Move> Moves { get; } = [];
    public int Table { get; set; } = 1;
    public int Next { get; set; }
    public long DelayUntilMs { get; set; }
    public long NextPulseMs { get; set; }
    public long OutAtMs { get; set; }

    // The game clock's hour the last tick saw (none yet: -1).
    public int LastHour { get; set; } = -1;

    // The round's monsters as the arena's catalog holds them (null for a name it lacks), and its boss.
    public MonsterDefinition?[] Monsters { get; set; } = [];
    public MonsterDefinition? Boss { get; set; }

    public int Round => Next is >= 1 and <= BellatraRounds.Count ? Next : 0;

    public BellatraRound? Playing => Round > 0 ? BellatraRounds.Tables[Table][Round - 1] : null;

    // The hour the next step of the game waits for: the next round's start, or the end after the last.
    public int NextHour => Next < BellatraRounds.Count ? BellatraRounds.Tables[Table][Next].StartHour : BellatraRounds.EndHour;

    // Tickets sell only between games.
    public bool BetweenGames => Next == 0 && OutAtMs == 0;

    public void Reset()
    {
        Entrants.Clear();
        Array.Clear(TeamScores);
        Array.Clear(TeamLevels);
        Next = 0;
        OutAtMs = 0;
    }
}
