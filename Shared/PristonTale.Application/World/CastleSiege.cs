using PristonTale.Application.Castle;
using PristonTale.Simulation.Social.Castle;

namespace PristonTale.Application.World;

// The siege the world runs in the castle field, touched only on the tick: its order, the defenders the field placed
// (once, when the field is open), what of them still stands and the clans' damage.
internal sealed class CastleSiege(CastleSiegeOrder order)
{
    public CastleSiegeOrder Order { get; } = order;

    public CastleDamageBoard Board { get; } = new();

    public List<ulong> Defenders { get; } = [];

    public bool Placed { get; set; }

    // A game master's easy siege: the tower keeps one life, not its whole life, while a crystal tower stands.
    public bool Weakened { get; set; }

    public ulong ValhallaId { get; set; }

    public bool DoorDown { get; set; }

    public int Crystals { get; set; }

    public int[] Mercenaries { get; } = new int[CastleSiegeRules.MercenaryKinds];

    public bool ValhallaFallen { get; set; }

    public long Winner { get; set; }

    // Set when what stands changed since the last publication.
    public bool Changed { get; set; }

    public int PublishedVersion { get; set; } = -1;

    public long PublishedSecond { get; set; }
}
