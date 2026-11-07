namespace PristonTale.Simulation.World.Npcs;

public sealed record TeleportDestination(int Index, int FieldCode, string Name, long Price);

// NPC teleports by route (the teleporter's Value). Route 1 is the town teleporter's list of hunting grounds; route 3 is
// cursed Milter's free way into the Endless Tower's first floor. A destination also requires the level of its field. The
// route to Bless Island (2) leads to event content Etervale does not run yet.
public static class TeleportRoutes
{
    private static readonly IReadOnlyDictionary<int, IReadOnlyList<TeleportDestination>> Routes =
        new Dictionary<int, IReadOnlyList<TeleportDestination>>
        {
            [1] =
            [
                new(1, FieldCodes.Forest3, "Mushroom Cave", 100),
                new(2, FieldCodes.ForeverFall3, "Beehive Cave", 100),
                new(3, FieldCodes.Ruin1, "Cursed Land", 500),
                new(4, FieldCodes.Desert4, "Forbidden Land", 1000)
            ],
            [3] =
            [
                new(1, FieldCodes.EndlessTower1, "Endless Tower 1F", 0)
            ]
        };

    public static bool IsAvailable(int route) => Routes.ContainsKey(route == 0 ? 1 : route);

    public static IReadOnlyList<TeleportDestination> For(int route) =>
        Routes.TryGetValue(route == 0 ? 1 : route, out var destinations) ? destinations : [];

    public static bool TryGet(int route, int index, out TeleportDestination destination)
    {
        foreach (var candidate in For(route))
        {
            if (candidate.Index == index)
            {
                destination = candidate;
                return true;
            }
        }

        destination = null;
        return false;
    }
}

public static class NpcServiceAvailability
{
    // Services Etervale runs; the others answer that they are not available yet.
    public static bool IsAvailable(NpcService service) => service.Kind switch
    {
        NpcServiceKind.Shop or NpcServiceKind.Warehouse or NpcServiceKind.SkillMaster or NpcServiceKind.QuestGiver or NpcServiceKind.QuestScript
            or NpcServiceKind.ItemAging or NpcServiceKind.ItemMix or NpcServiceKind.Smelting or NpcServiceKind.Manufacture
            or NpcServiceKind.MixtureReset => true,
        // The wing exchange (value 0 or 1) runs as a quest.
        NpcServiceKind.WarpGate => service.Value <= 1,
        NpcServiceKind.Teleporter => TeleportRoutes.IsAvailable(service.Value),
        NpcServiceKind.QuestEvent => service.Value == Items.Usable.Puzzles.CollectorEvent,
        _ => false
    };
}
