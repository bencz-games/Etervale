using Microsoft.Xna.Framework;
using PristonTale.Gameplay.Localization;
using PristonTale.Gameplay.UI.Inventory;
using PristonTale.Gameplay.UI.Rendering;
using PristonTale.Simulation.Characters;
using PristonTale.Simulation.Items.Mixing;
using PristonTale.Simulation.World.Npcs;

namespace PristonTale.Gameplay.UI.Npc;

// The mixture reset window: a mixed item in the left panel and the Mixture Reset Stone in the right panel's box, which
// takes the stone only once the item is there. The reset is free.
public sealed class HudMixtureResetWindow : HudWorkbench
{
    private static readonly Point StoneCell = new(241, 174);

    public override NpcServiceKind Service => NpcServiceKind.MixtureReset;

    public override NpcServiceAction WorkAction => NpcServiceAction.ResetMix;

    protected override string StartButtonArt => HudCraftArt.ResetButton;

    public void Show() => Open(100);

    public override string ResultText(int result) => result == MixtureReset.Reset ? ClientText.Get("MixReset.Done") : ClientText.Get("Mix.Failed");

    public override bool Succeeded(int result) => result == MixtureReset.Reset;

    public override bool Ready(OwnerState state) => MainItem(state) is not null && OwnedItemView.Find(state.Items, StoneId) is not null;

    public override long Price(OwnerState state) => 0;

    public static bool Resettable(OwnedItemView item) => item?.Details is { MixSheltom: > 0 };

    protected override Rectangle StoneBounds(Point origin) => HudCraftLayout.Cell(origin, StoneCell);

    protected override int SlotAt(Point origin, Point pointer) => -1;

    protected override string PlaceItem(OwnedItemView item, Point origin, Point pointer, OwnerState state)
    {
        if (item.CatalogKey.Equals(MixtureReset.Stone, StringComparison.OrdinalIgnoreCase))
        {
            if (MainItem(state) is null)
            {
                return ClientText.Get("MixReset.ItemFirst");
            }

            StoneId = item.InstanceId;
            return "";
        }

        if (!Resettable(item))
        {
            return ClientText.Get("MixReset.CannotReset");
        }

        ItemId = item.InstanceId;
        return "";
    }

    protected override void DrawPanel(HudPainter painter, Point origin, OwnerState state)
    {
        var title = HudStoreLayout.Title(origin);
        painter.Sprite(HudCraftArt.ResetTitle, title.X, title.Y);
        var panel = HudCraftLayout.Panel(origin);
        painter.Sprite(HudCraftArt.ResetPanel, panel.X, panel.Y);
    }

    protected override void DrawPlacement(HudPainter painter, Point origin, OwnedItemView incoming, Point pointer, OwnerState state)
    {
        var stone = StoneBounds(origin);
        if (stone.Contains(pointer))
        {
            var fits = StoneId == Guid.Empty && MainItem(state) is not null
                && incoming.CatalogKey.Equals(MixtureReset.Stone, StringComparison.OrdinalIgnoreCase);
            painter.Fill(stone, fits ? HudPlacementColors.Fits : HudPlacementColors.Blocked);
            return;
        }

        var box = ItemBounds(origin);
        if (box.Contains(pointer))
        {
            painter.Fill(box, Resettable(incoming) ? HudPlacementColors.Fits : HudPlacementColors.Blocked);
        }
    }

    protected override void DrawStartInfo(HudPainter painter, Point info) => painter.Sprite(HudCraftArt.ResetStartInfo, info.X, info.Y);

    protected override bool ShowsStart(OwnerState state) => Ready(state);

    protected override bool ShowsPrice(OwnerState state) => false;

    // The stone needs the item: taking the item back takes the stone back too.
    protected override void MainItemLeft()
    {
        ItemId = Guid.Empty;
        StoneId = Guid.Empty;
    }
}
