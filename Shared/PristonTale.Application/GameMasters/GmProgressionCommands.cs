using System.Globalization;
using PristonTale.Application.Services;
using PristonTale.Application.World;
using PristonTale.Simulation.Characters;
using PristonTale.Simulation.Progression;

namespace PristonTale.Application.GameMasters;

// An administrator's own development character only. This deliberately does not alter
// another account, award experience, or pretend that the tier-change quest was completed.
internal static class GmProgressionCommands
{
    public static async Task UnlockTier(GmCommandContext context)
    {
        var input = context.Word();
        int? requestedTier = null;
        if (input.Length > 0)
        {
            if (!int.TryParse(input, NumberStyles.None, CultureInfo.InvariantCulture, out var displayTier)
                || displayTier < GmTierProgression.FirstDisplayTier || displayTier > GmTierProgression.LastDisplayTier)
            {
                context.Reply("Usage: /unlocktier [1..5] (without a number, uses the current character level)");
                return;
            }

            requestedTier = displayTier - GmTierProgression.FirstDisplayTier;
        }

        if (context.Rest.Length > 0)
        {
            context.Reply("Usage: /unlocktier [1..5]");
            return;
        }

        if (context.Commands.Saves is null)
        {
            context.Reply("Character saving is not available on this server.");
            return;
        }

        using var lease = await new DurableOperations(context.Commands.Saves)
            .AdmitAsync(context.Issuer.Session, TimeSpan.Zero, CancellationToken.None);
        var outcome = await lease.RunAsync(
            session => context.Commands.World.PrepareGmTierAsync(session, requestedTier),
            context.Commands.World.CommitGmTierAsync,
            static operation => operation.Outcome,
            static failure => new GmTierUnlockOutcome(false, 0, 0, 0, failure switch
            {
                DurableFailure.Busy => "Character is busy; try again.",
                DurableFailure.NoSession => "The game session is no longer available.",
                DurableFailure.NotPlaying => "The character is not in the world.",
                DurableFailure.StorageFailed => "Character saving failed; try again.",
                DurableFailure.Conflict => "Character data changed; try again.",
                _ => "Character operation failed; try again."
            }));
        context.Reply(outcome.Message);
    }
}
