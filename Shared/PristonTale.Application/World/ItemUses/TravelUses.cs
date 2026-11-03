using System.Numerics;
using PristonTale.Application.Sessions;
using PristonTale.Simulation.Items.Usable;
using PristonTale.Simulation.World;
using PristonTale.Simulation.World.Npcs;

namespace PristonTale.Application.World.ItemUses;

// Items that move the player or reach a town from afar. None works in a room; Bellatra and the quest arena refuse the
// return cores and the mobile shop, which towns and the castle refuse as well. A destination is its field's first start
// point, settled on the floor on arrival.
internal static class TravelRules
{
    public static readonly int[] ReturnBlockedFields = [FieldCodes.Bellatra, FieldCodes.QuestArena];
    public static readonly int[] MobileShopBlockedFields =
        [FieldCodes.Ricarten, FieldCodes.Pillai, FieldCodes.Castle, FieldCodes.Bellatra, FieldCodes.InnerIce];

    public static TeleportArrival ArrivalAt(FieldDef target)
    {
        var start = target.Arrival;
        return new TeleportArrival(target.FieldCode, new Vector3(start.X, 0f, start.Z));
    }
}

// A teleport core takes its user to the destination the command names, when the user's level reaches it.
internal sealed class TeleportCoreUse : ItemUseAction
{
    public override ItemUseStatus Check(in ItemUseRequest use)
    {
        if (use.Here?.State == FieldState.Room || !TeleportCoreDestinations.TryGet(use.Command.Argument, out var destination)
            || !use.Context.World.TryGetFieldByCode(destination.FieldCode, out _))
        {
            return ItemUseStatus.NotHere;
        }

        return use.Character.Level < destination.RequiredLevel ? ItemUseStatus.LevelTooLow : ItemUseStatus.Used;
    }

    public override TeleportArrival? Arrival(in ItemUseRequest use) =>
        TeleportCoreDestinations.TryGet(use.Command.Argument, out var destination) && use.Context.World.TryGetFieldByCode(destination.FieldCode, out var target)
            ? TravelRules.ArrivalAt(target)
            : null;
}

// An ether core returns its user to the rule's town (a core without one plays its return where the player stands) and
// guards the user for ten seconds when the return motion ends.
internal sealed class ReturnToTownUse : ItemUseAction
{
    public const float GuardMs = 10_000f;

    public override ItemUseStatus Check(in ItemUseRequest use)
    {
        if (use.Here is null || use.Here.State == FieldState.Room || TravelRules.ReturnBlockedFields.Contains(use.Context.Field.FieldId))
        {
            return ItemUseStatus.NotHere;
        }

        return use.Rule.Argument < 0 || use.Context.World.TryGetFieldByCode(use.Rule.Argument, out _) ? ItemUseStatus.Used : ItemUseStatus.NotHere;
    }

    public override TeleportArrival? Arrival(in ItemUseRequest use) =>
        use.Rule.Argument >= 0 && use.Context.World.TryGetFieldByCode(use.Rule.Argument, out var town) ? TravelRules.ArrivalAt(town) : null;

    public override ItemUseEffect Apply(in ItemUseInWorld use)
    {
        use.Player.Guard(GuardMs);
        return new ItemUseEffect(ItemEffectKind.Return);
    }
}

// The mobile shop opens a shop that buys and repairs until it is closed (NPC id 0).
internal sealed class MobileShopUse : ItemUseAction
{
    public override ItemUseStatus Check(in ItemUseRequest use) =>
        use.Here is null || use.Here.State == FieldState.Room || TravelRules.MobileShopBlockedFields.Contains(use.Context.Field.FieldId)
            ? ItemUseStatus.NotHere
            : ItemUseStatus.Used;

    public override void Commit(PlayerSession session, UsableItemRule rule) => session.MobileShopOpen = true;
}

// A union core gathers a clan, which Etervale does not run.
internal sealed class UnionCoreUse : ItemUseAction
{
    public override ItemUseStatus Check(in ItemUseRequest use) => ItemUseStatus.ClanOnly;
}
