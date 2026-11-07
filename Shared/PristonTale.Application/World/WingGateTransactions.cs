using PristonTale.Application.Sessions;
using PristonTale.Persistence.Abstractions.Characters;
using PristonTale.Simulation.Characters;
using PristonTale.Simulation.World;
using PristonTale.Simulation.World.Npcs;
using PristonTale.Contracts.World;

namespace PristonTale.Application.World;

public sealed record PreparedWingGateOperation(
    PlayerCharacter Original,
    PlayerCharacter? Candidate,
    CharacterSaveSnapshot? Snapshot,
    WingGateOutcome Outcome,
    TeleportArrival? Arrival = null)
    : PreparedCharacterOperation<WingGateOutcome>(Original, Candidate, Snapshot, Outcome);

// Runs on the world tick. A living player standing on a warp stone travels to a stone its best carried wing reaches
// (WingGates) when its level meets the destination field's; the candidate pays the fee and the snapshot places the
// character at the destination stone's exit, so the trip is stored before the world moves the player.
internal static class WingGateTransactions
{
    public static PreparedWingGateOperation Prepare(PlayerSession session, WorldPlayer player, FieldInstance field, WorldCatalog world,
        WingGateCommand command)
    {
        var original = player.Character;
        if (player.IsDead || session.Loaded is null || !WingGates.TryGet(command.Destination, out var destination)
            || !world.TryGetFieldByCode(destination.FieldCode, out var target))
        {
            return Final(original, command, WingGateStatus.Rejected);
        }

        if (!world.TryGetFieldByCode(field.FieldId, out var here) || !WingGates.AtStone(here, player.Position))
        {
            return Final(original, command, WingGateStatus.NotAtStone);
        }

        var wing = WingGates.BestWing(Carried(original));
        if (destination.Index >= WingGates.Reach(wing))
        {
            return Final(original, command, WingGateStatus.NotReached);
        }

        if (original.Level < WingGates.RequiredLevel(destination, target.LimitLevel))
        {
            return Final(original, command, WingGateStatus.LevelTooLow);
        }

        var fee = WingGates.Fee(wing, destination, field.FieldId);
        var candidate = original.CloneForInventoryOperation();
        if (!candidate.TryChangeMoney(-fee))
        {
            return Final(original, command, WingGateStatus.NotEnoughGold, fee);
        }

        var arrival = new TeleportArrival(target.FieldCode, WingGates.Exit(target));
        var snapshot = CharacterRecordFactory.Capture(session.Loaded, candidate, arrival.FieldCode, arrival.Position);
        return new PreparedWingGateOperation(original, candidate, snapshot, new WingGateOutcome(WingGateStatus.Warped, command.Destination, fee),
            arrival);
    }

    // The shown bag page and the special item box, where wings are kept.
    private static IEnumerable<uint> Carried(PlayerCharacter character) =>
        character.Inventory.Items().Concat(character.QuestBox.Occupied().Select(static box => box.Item)).Select(static item => item.Definition.Code);

    private static PreparedWingGateOperation Final(PlayerCharacter original, WingGateCommand command, WingGateStatus status, long fee = 0) =>
        new(original, null, null, new WingGateOutcome(status, command.Destination, fee));
}
