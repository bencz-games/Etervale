using Microsoft.Xna.Framework;
using PristonTale.Gameplay.Localization;
using PristonTale.Gameplay.UI.Inventory;
using PristonTale.Gameplay.UI.Rendering;
using PristonTale.Simulation.Characters;
using PristonTale.Simulation.Items.Aging;
using PristonTale.Simulation.Items.Smelting;
using PristonTale.Simulation.World.Npcs;

namespace PristonTale.Gameplay.UI.Npc;

// The smelting window: three ores or crystals of one colour in the left panel's three boxes and the sheltom their colour
// asks for in the right panel's box, whose hint shows once a piece is placed.
public sealed class HudSmeltingWindow : HudWorkbench
{
    private static readonly Point[] PieceCells = [new(80, 159), new(55, 190), new(105, 190)];
    private static readonly Point SheltomCell = new(240, 173);

    public override NpcServiceKind Service => NpcServiceKind.Smelting;

    public override NpcServiceAction WorkAction => NpcServiceAction.Smelt;

    public void Show() => Open(100);

    public override string ResultText(int result) =>
        (SmeltResult)result == SmeltResult.Smelted ? ClientText.Get("Smelt.Done") : ClientText.Get("Smelt.Failed");

    public override bool Succeeded(int result) => (SmeltResult)result == SmeltResult.Smelted;

    public override bool Ready(OwnerState state) =>
        Piece(state) is { } piece && Array.IndexOf(Slots, Guid.Empty, 0, SmeltingWork.Pieces) < 0
        && OwnedItemView.Find(state.Items, StoneId) is { } sheltom && AgingRules.SheltomOrdinal(sheltom.CatalogKey) == SmeltingWork.SheltomFor(piece);

    public override long Price(OwnerState state) => Piece(state) is { } piece ? SmeltingWork.Fee(piece) : 0;

    protected override Rectangle SlotBounds(Point origin, int slot) =>
        slot < PieceCells.Length ? HudCraftLayout.Cell(origin, PieceCells[slot]) : Rectangle.Empty;

    protected override int SlotAt(Point origin, Point pointer) => HudCraftLayout.CellAt(origin, PieceCells, pointer);

    protected override Rectangle StoneBounds(Point origin) => HudCraftLayout.Cell(origin, SheltomCell);

    protected override string PlaceItem(OwnedItemView item, Point origin, Point pointer, OwnerState state)
    {
        var key = item.CatalogKey;
        if (SmeltingWork.SheltomFor(key) > 0)
        {
            if (Piece(state) is { } placed && !placed.Equals(key, StringComparison.OrdinalIgnoreCase))
            {
                return ClientText.Get("Smelt.NotSame");
            }

            var slot = SlotAt(origin, pointer);
            if (slot < 0 || Slots[slot] != Guid.Empty)
            {
                slot = Array.IndexOf(Slots, Guid.Empty, 0, SmeltingWork.Pieces);
            }

            if (slot < 0)
            {
                return ClientText.Get("Smelt.CannotSmelt");
            }

            Slots[slot] = item.InstanceId;
            return "";
        }

        if (AgingRules.SheltomOrdinal(key) is > 0 and var ordinal && Piece(state) is { } piece && ordinal == SmeltingWork.SheltomFor(piece))
        {
            StoneId = item.InstanceId;
            return "";
        }

        return ClientText.Get("Smelt.CannotSmelt");
    }

    protected override void DrawPanel(HudPainter painter, Point origin, OwnerState state)
    {
        var title = HudStoreLayout.Title(origin);
        painter.Sprite(HudCraftArt.SmeltingTitle, title.X, title.Y);
        var panel = HudCraftLayout.Panel(origin);
        painter.Sprite(HudCraftArt.SmeltingPanel, panel.X, panel.Y);
        if (StoneId == Guid.Empty && Piece(state) is { } piece)
        {
            var hint = StoneBounds(origin);
            painter.Sprite(HudCraftArt.SheltomHint(SmeltingWork.SheltomFor(piece)), hint.X, hint.Y);
        }

        var plate = HudCraftLayout.PricePlate(origin);
        painter.Sprite(HudNpcArt.Gold, plate.X, plate.Y);
    }

    protected override void DrawPlacement(HudPainter painter, Point origin, OwnedItemView incoming, Point pointer, OwnerState state)
    {
        var key = incoming.CatalogKey;
        var slot = SlotAt(origin, pointer);
        if (slot >= 0)
        {
            var fits = Slots[slot] == Guid.Empty && SmeltingWork.SheltomFor(key) > 0
                && (Piece(state) is not { } placed || placed.Equals(key, StringComparison.OrdinalIgnoreCase));
            painter.Fill(SlotBounds(origin, slot), fits ? HudPlacementColors.Fits : HudPlacementColors.Blocked);
            return;
        }

        var stone = StoneBounds(origin);
        if (stone.Contains(pointer))
        {
            var fits = StoneId == Guid.Empty && Piece(state) is { } piece && AgingRules.SheltomOrdinal(key) == SmeltingWork.SheltomFor(piece);
            painter.Fill(stone, fits ? HudPlacementColors.Fits : HudPlacementColors.Blocked);
        }
    }

    protected override void DrawStartInfo(HudPainter painter, Point info) => painter.Sprite(HudCraftArt.SmeltingStartInfo, info.X, info.Y);

    protected override bool ShowsStart(OwnerState state) => StartHovered && Ready(state);

    protected override bool ShowsPrice(OwnerState state) => Piece(state) is not null;

    protected override void MainItemLeft() => ItemId = Guid.Empty;

    // The catalog key of the ore or crystal placed first; null with none placed.
    private string Piece(OwnerState state)
    {
        for (var slot = 0; slot < SmeltingWork.Pieces; slot++)
        {
            if (OwnedItemView.Find(state?.Items, Slots[slot]) is { } piece)
            {
                return piece.CatalogKey;
            }
        }

        return null;
    }

}
