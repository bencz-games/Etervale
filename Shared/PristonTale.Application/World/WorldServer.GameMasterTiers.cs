using PristonTale.Application.Sessions;
using PristonTale.Persistence.Abstractions.Characters;
using PristonTale.Simulation.Characters;
using PristonTale.Simulation.Progression;

namespace PristonTale.Application.World;

public sealed record GmTierUnlockOutcome(bool Success, int Tier, int Level, int SkillsAdded, string Message);

public sealed partial class WorldServer
{
    // The save gate is acquired by the GM command before this call. Prepare on the world
    // tick using a clone, so the live character remains unchanged until commit. The
    // captured candidate is enqueued to normal character saving after the commit.
    public Task<PreparedCharacterOperation<GmTierUnlockOutcome>?> PrepareGmTierAsync(PlayerSession session, int? requestedTier)
    {
        return EnqueueAsync<PreparedCharacterOperation<GmTierUnlockOutcome>?>(session.EntityId, () =>
        {
            if (session.Loaded is null || !TryGetPlaying(session, out var connection))
            {
                return null;
            }

            var original = connection.Player.Character;
            PreparedCharacterOperation<GmTierUnlockOutcome> Refuse(string message) =>
                new(original, null, null, new GmTierUnlockOutcome(false, original.Tier + 1, original.Level, 0, message));

            var tier = requestedTier ?? GmTierProgression.ForLevel(original.Level);
            if (tier < original.Tier || tier > PlayerCharacter.LastTier)
            {
                return Refuse($"Cannot lower Tier {original.Tier + 1}; choose {original.Tier + 1}..{GmTierProgression.LastDisplayTier}.");
            }

            // An explicit tier raises the level to the end of its bracket if needed. An omitted
            // tier follows the existing level and never changes experience.
            var targetLevel = requestedTier.HasValue
                ? Math.Max(original.Level, GmTierProgression.HighestLevel(tier))
                : original.Level;
            var candidate = original.CloneForInventoryOperation();
            var added = candidate.UnlockTierForGameMaster(tier, targetLevel);
            if (original.Tier == tier && original.Level == candidate.Level && added == 0)
            {
                return Refuse($"Already Tier {tier + 1} (level {original.Level}); no additional skills available.");
            }

            var snapshot = CharacterRecordFactory.Capture(session.Loaded, candidate, connection.FieldId, connection.Player.Position);
            var outcome = new GmTierUnlockOutcome(true, tier + 1, candidate.Level, added, $"Tier {tier + 1}; level {candidate.Level}; {added} skills learned (rank 1). Saved.");
            return new PreparedCharacterOperation<GmTierUnlockOutcome>(original, candidate, snapshot, outcome);
        });
    }

    public Task CommitGmTierAsync(PlayerSession session, PreparedCharacterOperation<GmTierUnlockOutcome> operation)
    {
        return EnqueueAsync(NoOwner, () =>
        {
            if (!ReferenceEquals(session.Character, operation.Original) || operation.Candidate is null)
            {
                throw new CommitInvariantException("Character changed during GM tier unlock.");
            }

            operation.Original.UnlockTierForGameMaster(operation.Candidate.Tier, operation.Candidate.Level);
            return true;
        }, waitForCapacity: true);
    }
}
