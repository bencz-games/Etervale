using System.Numerics;
using PristonTale.Application.Sessions;
using PristonTale.Simulation.Items.Usable;
using PristonTale.Simulation.World;
using PristonTale.Simulation.World.Npcs;
using PristonTale.Simulation.Characters;
using PristonTale.Contracts.Texts;

namespace PristonTale.Application.World.ItemUses;

// Items that move the player or reach a town from afar. None works in a room; Bellatra and the quest arena refuse the
// return cores and the mobile shop, which towns and the castle refuse as well. A destination is its field's first start
// point, settled on the floor on arrival.
internal static class TravelRules
{
    public static readonly int[] ReturnBlockedFields = [FieldCodes.Bellatra, FieldCodes.QuestArena];

    // Where a Union Core does not reach its bound member.
    public static readonly int[] UnionCoreBlockedFields = [FieldCodes.Castle, FieldCodes.QuestArena, FieldCodes.Bellatra];
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
        use.Player.Guard(PlayerGrace.ArrivalMs);
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

// A Union Core gathers a registered clan, never in a room. Its first use binds it to its user, who keeps it to hand to a
// member. A bound core takes another member of the same clan to the bound member and is spent: the member must be in the
// world, outside a room, the castle, the quest arena and Bellatra, in a field the user's level may enter, and the user
// outside the quest arena.
internal sealed class UnionCoreUse : ItemUseAction
{
    public override bool Consumes => false;

    public override ItemUseStatus Check(in ItemUseRequest use)
    {
        var player = use.Context.Player;
        if (use.Here?.State == FieldState.Room)
        {
            return ItemUseStatus.NotHere;
        }

        if (player.Clan is not { } clan)
        {
            return ItemUseStatus.ClanOnly;
        }

        if (!clan.Registered)
        {
            return ItemUseStatus.RegisteredClanOnly;
        }

        var link = use.Item.Link;
        if (link.Length == 0)
        {
            return ItemUseStatus.Used;
        }

        if (string.Equals(link, use.Character.Name, StringComparison.OrdinalIgnoreCase))
        {
            return ItemUseStatus.UnionCoreOwn;
        }

        if (use.Context.Field.FieldId == FieldCodes.QuestArena)
        {
            return ItemUseStatus.NotHere;
        }

        if (use.Context.PlayerNamed(link) is not { } member || TravelRules.UnionCoreBlockedFields.Contains(member.FieldId)
            || !use.Context.World.TryGetFieldByCode(member.FieldId, out var there) || there.State == FieldState.Room)
        {
            return ItemUseStatus.UnionCoreAway;
        }

        if (member.Player.Clan?.ClanId != clan.ClanId)
        {
            return ItemUseStatus.UnionCoreWrongClan;
        }

        return use.Character.Level < there.LimitLevel ? ItemUseStatus.LevelTooLow : ItemUseStatus.Used;
    }

    public override TeleportArrival? Arrival(in ItemUseRequest use) =>
        use.Item.Link.Length > 0 && use.Context.PlayerNamed(use.Item.Link) is { } member
            ? new TeleportArrival(member.FieldId, member.Player.Position)
            : null;

    // An unbound core takes its user's name; a bound one is spent.
    public override ItemUseExtra Extend(in ItemUseRequest use, PlayerCharacter candidate)
    {
        var anchor = candidate.Inventory.Find(use.Item.InstanceId);
        if (anchor < 0)
        {
            return new ItemUseExtra(ItemUseStatus.NotUsable);
        }

        if (use.Item.Link.Length > 0)
        {
            return candidate.Inventory.ConsumeAt(anchor) ? ItemUseExtra.None : new ItemUseExtra(ItemUseStatus.NotUsable);
        }

        var bound = candidate.Inventory.TakeAt(anchor);
        bound.Link = use.Character.Name;
        return candidate.Inventory.TryPlace(bound, anchor)
            ? new ItemUseExtra(ItemUseStatus.Used, Message: GameText.Of(GameTextId.UnionCoreBound))
            : new ItemUseExtra(ItemUseStatus.NotUsable);
    }
}
