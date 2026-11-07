using PristonTale.Simulation.Characters;
using PristonTale.Simulation.World.Ai;
using PristonTale.Simulation.World.Npcs;

namespace PristonTale.Simulation.Items.Mixing;

// The mixture reset spends a Mixture Reset Stone to undo a mix: the item is made again from its code under the same
// identity, its stats rolled anew, keeping its class specialization (a plain item stays plain). The service is free.
public static class MixtureReset
{
    public const string Stone = "BI189";

    public const byte Reset = 1;

    public static bool Resettable(ItemInstance item) => item is { Mixed: true, Definition.Template: not null };

    public static NpcServiceStatus TryReset(PlayerCharacter candidate, Guid itemId, IReadOnlyList<Guid> materials, ISimRng rng,
        out ItemWorkResult work)
    {
        work = null;
        var inventory = candidate.Inventory;
        var anchor = inventory.Find(itemId);
        if (anchor < 0 || inventory[anchor] is not { } item || !Resettable(item))
        {
            return NpcServiceStatus.Rejected;
        }

        var at = materials is { Count: 1 } && materials[0] != itemId ? inventory.Find(materials[0]) : -1;
        if (at < 0 || !inventory[at].Definition.CatalogKey.Equals(Stone, StringComparison.OrdinalIgnoreCase))
        {
            return NpcServiceStatus.WrongMaterials;
        }

        inventory.ConsumeAt(at);
        var spec = item.Roll.SpecJob;
        var remade = ItemRoller.Create(item.Definition, 1, rng, spec != 0 ? new ItemRollOverrides(spec) : new ItemRollOverrides(Plain: true));
        var reset = new ItemInstance(item.Definition, 1)
        {
            InstanceId = item.InstanceId,
            DurabilityCurrent = remade.DurabilityCurrent,
            DurabilityMax = remade.DurabilityMax,
            Roll = remade.Roll
        };
        anchor = inventory.Find(itemId);
        inventory.TakeAt(anchor);
        if (!inventory.TryPlace(reset, anchor))
        {
            return NpcServiceStatus.NoSpace;
        }

        work = new ItemWorkResult(Reset, reset.InstanceId, item.Definition.Code);
        return NpcServiceStatus.Ok;
    }
}
