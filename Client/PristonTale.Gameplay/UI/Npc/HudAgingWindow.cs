using Microsoft.Xna.Framework;
using PristonTale.Gameplay.UI.Inventory;
using PristonTale.Gameplay.UI.Rendering;
using PristonTale.Simulation.Characters;
using PristonTale.Simulation.Items.Aging;
using PristonTale.Simulation.World.Npcs;
using PristonTale.Gameplay.Localization;

namespace PristonTale.Gameplay.UI.Npc;

// The aging window: an ageable item, the sheltoms its level asks (each slot shows the one it wants) and a stone. Taking
// the item back clears the window, since the slots follow its level.
public sealed class HudAgingWindow : HudWorkbench
{
    public void Show(int pricePercent = 100) => Open(pricePercent);

    public override NpcServiceKind Service => NpcServiceKind.ItemAging;

    public override NpcServiceAction WorkAction => NpcServiceAction.Age;

    public override string ResultText(int result) => (AgingResult)result switch
    {
        AgingResult.Destroyed => ClientText.Get("Aging.Failed"),
        AgingResult.DownOne => ClientText.Get("Aging.DownOne"),
        AgingResult.DownTwo => ClientText.Get("Aging.DownTwo"),
        AgingResult.Double => ClientText.Get("Aging.UpTwo"),
        _ => ClientText.Get("Aging.UpOne")
    };

    public override bool Succeeded(int result) => (AgingResult)result >= AgingResult.Success;

    public static bool Ageable(OwnedItemView item) =>
        item?.Details is { } details && AgingRules.Ageable(item.CatalogKey, item.Kind, details.UniqueItem, details.MixSheltom, details.AgingGoal > 0,
            details.UpgradeLevel);

    public override bool Ready(OwnerState state)
    {
        if (MainItem(state) is not { Details: { } details })
        {
            return false;
        }

        var required = Required(details.UpgradeLevel);
        for (var slot = 0; slot < required.Count; slot++)
        {
            if (required[slot] != 0 && Slots[slot] == Guid.Empty)
            {
                return false;
            }
        }

        return true;
    }

    public override long Price(OwnerState state) =>
        MainItem(state) is { Details: { } details } ? AgingRules.Price(details.UpgradeLevel, details.Roll.Price, PricePercent) : 0;

    protected override string PlaceItem(OwnedItemView item, Point origin, Point pointer, OwnerState state)
    {
        var key = item.CatalogKey;
        if (AgingRules.StoneOf(key) != AgingStone.None)
        {
            StoneId = item.InstanceId;
            return "";
        }

        var ordinal = AgingRules.SheltomOrdinal(key);
        if (ordinal > 0)
        {
            return PlaceSheltom(item.InstanceId, ordinal, HudCraftLayout.SheltomAt(origin, pointer), state) ? "" : ClientText.Get("Aging.CannotAge");
        }

        if (!Ageable(item))
        {
            return ClientText.Get("Aging.CannotAge");
        }

        Clear();
        ItemId = item.InstanceId;
        return "";
    }

    protected override void DrawPanel(HudPainter painter, Point origin, OwnerState state)
    {
        var title = HudStoreLayout.Title(origin);
        painter.Sprite(HudCraftArt.AgingTitle, title.X, title.Y);
        var panel = HudCraftLayout.Panel(origin);
        painter.Sprite(HudCraftArt.Panel, panel.X, panel.Y);
        var plate = HudCraftLayout.PricePlate(origin);
        painter.Sprite(HudNpcArt.Gold, plate.X, plate.Y);
        if (MainItem(state)?.Details is { } details)
        {
            var required = Required(details.UpgradeLevel);
            for (var slot = 0; slot < required.Count; slot++)
            {
                var cell = HudCraftLayout.Sheltom(origin, slot);
                painter.Sprite(HudCraftArt.SheltomHint(required[slot]), cell.X, cell.Y);
            }
        }
    }

    protected override void DrawPlacement(HudPainter painter, Point origin, OwnedItemView incoming, Point pointer, OwnerState state)
    {
        var key = incoming.CatalogKey;
        if (AgingRules.StoneOf(key) != AgingStone.None)
        {
            var stone = HudCraftLayout.Stone(origin);
            if (stone.Contains(pointer))
            {
                painter.Fill(stone, HudPlacementColors.Fits);
            }

            return;
        }

        var ordinal = AgingRules.SheltomOrdinal(key);
        if (ordinal > 0)
        {
            var slot = HudCraftLayout.SheltomAt(origin, pointer);
            if (slot >= 0)
            {
                var required = MainItem(state)?.Details is { } details ? Required(details.UpgradeLevel) : [];
                var fits = slot < required.Count && required[slot] == ordinal && Slots[slot] == Guid.Empty;
                painter.Fill(HudCraftLayout.Sheltom(origin, slot), fits ? HudPlacementColors.Fits : HudPlacementColors.Blocked);
            }

            return;
        }

        var box = HudCraftLayout.ItemBox(origin);
        if (box.Contains(pointer))
        {
            painter.Fill(box, Ageable(incoming) ? HudPlacementColors.Fits : HudPlacementColors.Blocked);
        }
    }

    protected override void DrawStartInfo(HudPainter painter, Point info) => painter.Sprite(HudCraftArt.AgingStartInfo, info.X, info.Y);

    protected override bool ShowsStart(OwnerState state) => Ready(state);

    protected override bool ShowsPrice(OwnerState state) => MainItem(state) is not null;

    protected override void MainItemLeft() => Clear();

    private bool PlaceSheltom(Guid id, int ordinal, int slot, OwnerState state)
    {
        if (MainItem(state)?.Details is not { } details)
        {
            return false;
        }

        var required = Required(details.UpgradeLevel);
        bool Free(int index) => index >= 0 && index < required.Count && required[index] == ordinal && Slots[index] == Guid.Empty;
        if (!Free(slot))
        {
            slot = Enumerable.Range(0, required.Count).FirstOrDefault(Free, -1);
        }

        if (slot < 0)
        {
            return false;
        }

        Slots[slot] = id;
        return true;
    }

    private static IReadOnlyList<int> Required(int level) => level < AgingRules.MaxLevel ? AgingRules.SheltomsFor(level) : [];
}
