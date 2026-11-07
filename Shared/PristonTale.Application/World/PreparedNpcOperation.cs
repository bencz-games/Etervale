using PristonTale.Persistence.Abstractions.Characters;
using PristonTale.Simulation.Characters;
using PristonTale.Simulation.World.Npcs;

namespace PristonTale.Application.World;

// A validated NPC service request. The warehouse, when set, is stored with the snapshot; the commit applies the
// candidate, the gold change and the arrival. Tax: the castle's tax the gold change was charged with. BellatraTeam: the
// team a Bellatra ticket enters (none: -1).
public sealed record PreparedNpcOperation(
    PlayerCharacter Original,
    PlayerCharacter? Candidate,
    CharacterSaveSnapshot? Snapshot,
    NpcServiceReply Reply,
    long MoneyDelta = 0,
    WarehouseStorage? Warehouse = null,
    long ExpectedWarehouseRevision = 0,
    int Tax = 0,
    int BellatraTeam = -1)
    : PreparedOperation(Original, Candidate, Snapshot);
