using PristonTale.Persistence.Abstractions.Characters;
using PristonTale.Simulation.Characters;
using PristonTale.Simulation.Quests;

namespace PristonTale.Application.World;

// A validated quest request or quest upkeep. The commit applies the candidate's journal and items, the progression
// deltas and any travel or duel the quest opened.
public sealed record PreparedQuestOperation(
    PlayerCharacter Original,
    PlayerCharacter? Candidate,
    CharacterSaveSnapshot? Snapshot,
    QuestOutcome Outcome)
    : PreparedCharacterOperation<QuestOutcome>(Original, Candidate, Snapshot, Outcome)
{
    public short Tier { get; init; }
    public int StatPoints { get; init; }
    public int SkillPoints { get; init; }
    public int EliteSkillPoints { get; init; }
    public long MoneyDelta { get; init; }

    // Experience the quest pays, awarded at the commit as a kill's is and stored with the next save.
    public ulong Experience { get; init; }
    public bool ResetSkills { get; init; }

    // Upkeep only: the pending drops it took, returned to the player when storage fails.
    public PendingQuestDrop[] Drops { get; init; } = [];
    public bool Upkeep { get; init; }
}
