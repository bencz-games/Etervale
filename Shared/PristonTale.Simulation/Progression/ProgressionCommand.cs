using PristonTale.Simulation.Characters;
using PristonTale.Contracts.Texts;

namespace PristonTale.Simulation.Progression;

public enum ProgressionAction
{
    AllocateAttribute,
    LearnSkill
}

public sealed record ProgressionCommand(Guid OperationId, long ExpectedRevision, ProgressionAction Action,
    CharacterAttribute Attribute = CharacterAttribute.Strength, int Amount = 0, uint SkillCode = 0, ulong NpcEntityId = 0);
public sealed record ProgressionOutcome(bool Ok, long Revision, GameText Message);
public sealed record AppliedProgressionOperation(ProgressionCommand Command, long Revision) : IAppliedOperation
{
    Guid IAppliedOperation.OperationId => Command?.OperationId ?? Guid.Empty;
}
