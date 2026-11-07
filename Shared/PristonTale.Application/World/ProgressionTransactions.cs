using PristonTale.Application.Sessions;
using PristonTale.Persistence.Abstractions.Characters;
using PristonTale.Simulation.Characters;
using PristonTale.Simulation.Progression;
using PristonTale.Simulation.World.Npcs;
using PristonTale.Contracts.Texts;

namespace PristonTale.Application.World;

internal static class ProgressionTransactions
{
    public static PreparedProgressionOperation Prepare(
        PlayerSession session, WorldPlayer player, FieldInstance field, ProgressionCommand command)
    {
        var original = player.Character;
        if (original.ProgressionOperations.Find(command.OperationId) is { } operation)
        {
            var replay = operation.Command == command;
            return new PreparedProgressionOperation(original, null, null,
                new ProgressionOutcome(replay, operation.Revision, GameText.Of(replay ? GameTextId.AlreadyApplied : GameTextId.OperationConflict)));
        }

        if (player.IsDead || command.Action == ProgressionAction.LearnSkill
            && (!field.TryGetNpc(command.NpcEntityId, out var npc) || !npc.Definition.Has(NpcServiceKind.SkillMaster) || !NpcServiceTransactions.CanReach(player, npc)
                || !Simulation.Quests.QuestClasses.IsHomeTown(player.Character, field.FieldId)))
        {
            return new PreparedProgressionOperation(original, null, null,
                new ProgressionOutcome(false, original.ProgressionRevision, GameText.Of(GameTextId.SkillMasterAway)));
        }

        var candidate = original.CloneForInventoryOperation();
        if (!candidate.ApplyProgression(command, field.CastleTax))
        {
            return new PreparedProgressionOperation(original, null, null,
                new ProgressionOutcome(false, original.ProgressionRevision, GameText.Of(GameTextId.ProgressionRefused)));
        }

        var snapshot = CharacterRecordFactory.Capture(session.Loaded!, candidate, field.FieldId, player.Position);
        return new PreparedProgressionOperation(original, candidate, snapshot,
            new ProgressionOutcome(true, candidate.ProgressionRevision, GameText.None), field.CastleTax);
    }
}

// Tax: the castle's tax the candidate was charged, which the commit charges the live character again.
public sealed record PreparedProgressionOperation(
    PlayerCharacter Original,
    PlayerCharacter? Candidate,
    CharacterSaveSnapshot? Snapshot,
    ProgressionOutcome Outcome,
    int Tax = 0) : PreparedCharacterOperation<ProgressionOutcome>(Original, Candidate, Snapshot, Outcome);
