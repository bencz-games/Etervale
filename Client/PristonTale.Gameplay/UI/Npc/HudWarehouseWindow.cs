using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using PristonTale.Gameplay.UI.Inventory;
using PristonTale.Gameplay.UI.Rendering;
using PristonTale.Gameplay.UI.Widgets;
using PristonTale.Simulation.Characters;
using PristonTale.Simulation.Items;
using PristonTale.Simulation.World.Npcs;
using PristonTale.Gameplay.Localization;
using PristonTale.Contracts.Texts;

namespace PristonTale.Gameplay.UI.Npc;

public enum HudWarehouseActionKind
{
    None,
    Consumed,
    Exit,
    // An item picked up or set back where it was: only its sound.
    Lifted,
    Deposit,
    Move,
    Withdraw,
    WithdrawGold,
    Refused
}

// ItemId at Anchor, exchanged with Covered when there is one. Message: why a set-down was refused.
public readonly record struct HudWarehouseAction(HudWarehouseActionKind Kind, Guid ItemId = default, int Anchor = -1, Guid Covered = default,
    string Message = null);

// The account warehouse in the original window: stored gold in the upper bar (its coin takes gold out), weight in the lower
// one and the 9x9 grid. Items are carried as in the bag: a click picks one up and the next sets it down on the grid or in
// the bag (exchanged with the one item it covers there).
public sealed class HudWarehouseWindow : IHudServiceWindow
{
    private static readonly Func<OwnedItemView, bool> OnGrid = static _ => true;
    private readonly HudSlide _slide = new();
    private readonly HudItemCounts _counts = new();
    private OwnedItemView _hovered;
    private OwnedItemView _carried;
    // The item an exchange leaves on the grid, carried once the reply's view shows it.
    private Guid _carryAfter;
    private string _money = "";
    private string _weight = "";

    public WarehouseView View { get; private set; }
    public bool IsOpen => _slide.Open;
    public bool Visible => _slide.Visible;
    public bool FullyOpen => _slide.FullyOpen;
    public Point Origin => HudStoreLayout.Origin(_slide.Offset);
    public Guid Dragging => _carried?.InstanceId ?? Guid.Empty;
    public OwnedItemView Carried => _carried;

    public void Show(WarehouseView view)
    {
        _carried = null;
        _carryAfter = Guid.Empty;
        Refresh(view);
        _slide.Open = true;
    }

    public void Refresh(WarehouseView view)
    {
        View = view ?? View;
        if (View is null)
        {
            return;
        }

        _carried = OwnedItemView.Find(View.Items, _carryAfter != Guid.Empty ? _carryAfter : Dragging);
        _carryAfter = Guid.Empty;
        _counts.Refresh(View.Revision, View.Items);
        _money = View.Money.ToString("N0");
        _weight = $"{View.Weight}/{View.MaxWeight}";
    }

    public void CarryAfter(Guid itemId) => _carryAfter = itemId;

    public void Hide()
    {
        _slide.Open = false;
        _carried = null;
        _carryAfter = Guid.Empty;
        _hovered = null;
    }

    public void Advance(double seconds) => _slide.Advance(seconds);

    public bool BlocksWorld => true;

    void IHudServiceWindow.Draw(in HudServiceFrame frame) => Draw(frame.Painter, frame.Icons, frame.Pointer, frame.Incoming, FullyOpen && !frame.SettingsOpen);

    void IHudServiceWindow.DrawOverlay(in HudServiceFrame frame) => DrawOverlay(frame.Painter, frame.Layout, frame.Icons, frame.Pointer);

    public bool Contains(Point pointer) => _slide.Visible && HudStoreLayout.Window(Origin).Contains(pointer);

    public HudPlacement Placement(OwnedItemView carried, Point pointer) =>
        FullyOpen && View is not null ? Placement(Origin, View.Items, carried, pointer) : default;

    // Where an item carried over the grid lands; what the warehouse keeps out is refused on the cells it would take,
    // except what it refuses only when set down (WarehouseStorage.RefusedOnSetDown).
    public static HudPlacement Placement(Point origin, IReadOnlyList<OwnedItemView> stored, OwnedItemView carried, Point pointer)
    {
        var grid = HudStoreLayout.Grid(origin);
        if (HudGridPlacement.Of(grid, HudStoreLayout.Columns, HudStoreLayout.Rows, stored, OnGrid, carried, pointer) is not { } placement)
        {
            return default;
        }

        return WarehouseStorage.RefusedOnSetDown(carried.Kind) || WarehouseStorage.CanStore(carried.Kind, carried.CatalogKey)
            ? placement
            : new HudPlacement(HudPlacementKind.Blocked, HudGridPlacement.Cell(grid, HudStoreLayout.Columns, placement.Anchor, carried.Width, carried.Height),
                placement.Anchor);
    }

    // A click setting an item down over the grid, the stored item carried or a bag item: Move or Deposit at its cell
    // (Lifted when a stored item goes back where it was), Refused with the original's message, Consumed when it stays
    // carried; None off the grid.
    public HudWarehouseAction SetDownOnGrid(OwnedItemView carried, Point pointer)
    {
        var placement = Placement(carried, pointer);
        if (placement.Kind == HudPlacementKind.None)
        {
            return default;
        }

        if (WarehouseStorage.RefusedOnSetDown(carried.Kind))
        {
            return new HudWarehouseAction(HudWarehouseActionKind.Refused, Message: ClientText.Get(GameTextId.PotionNotStored));
        }

        if (!placement.SetsDown)
        {
            return new HudWarehouseAction(HudWarehouseActionKind.Consumed);
        }

        var stored = carried == _carried;
        if (stored)
        {
            _carried = null;
        }

        return stored && placement.Kind == HudPlacementKind.Fits && placement.Anchor == carried.Anchor
            ? new HudWarehouseAction(HudWarehouseActionKind.Lifted, carried.InstanceId)
            : new HudWarehouseAction(stored ? HudWarehouseActionKind.Move : HudWarehouseActionKind.Deposit, carried.InstanceId, placement.Anchor,
                placement.Covered?.InstanceId ?? Guid.Empty);
    }

    // bagPlacement: where the carried item would land in the bag (None while the inventory is closed). incoming: the bag
    // carries an item, which the inventory sets down.
    public HudWarehouseAction Update(MouseState mouse, MouseState previous, Point pointer, Func<OwnedItemView, Point, HudPlacement> bagPlacement,
        bool blocked, bool incoming)
    {
        _hovered = null;
        if (!FullyOpen || View is null)
        {
            _carried = null;
            return default;
        }

        if (_carried is null && !incoming && !blocked)
        {
            _hovered = ItemAt(pointer);
        }

        if (blocked || mouse.LeftButton != ButtonState.Pressed || previous.LeftButton != ButtonState.Released)
        {
            return default;
        }

        if (_carried is { } carried)
        {
            return SetDownOnGrid(carried, pointer) is { Kind: not HudWarehouseActionKind.None } action ? action : SetDownInBag(carried, bagPlacement(carried, pointer));
        }

        if (incoming)
        {
            return Contains(pointer) ? new HudWarehouseAction(HudWarehouseActionKind.Consumed) : default;
        }

        if (HudStoreLayout.Exit(Origin).Contains(pointer))
        {
            return new HudWarehouseAction(HudWarehouseActionKind.Exit);
        }

        if (HudStoreLayout.WarehouseGold(Origin).Contains(pointer))
        {
            return new HudWarehouseAction(HudWarehouseActionKind.WithdrawGold);
        }

        if (_hovered is { } item)
        {
            _carried = item;
            _hovered = null;
            return new HudWarehouseAction(HudWarehouseActionKind.Lifted, item.InstanceId);
        }

        return Contains(pointer) ? new HudWarehouseAction(HudWarehouseActionKind.Consumed) : default;
    }

    private HudWarehouseAction SetDownInBag(OwnedItemView carried, HudPlacement placement)
    {
        if (!placement.SetsDown)
        {
            return new HudWarehouseAction(HudWarehouseActionKind.Consumed);
        }

        _carried = null;
        return new HudWarehouseAction(HudWarehouseActionKind.Withdraw, carried.InstanceId, placement.Anchor, placement.Covered?.InstanceId ?? Guid.Empty);
    }

    public void Draw(HudPainter painter, HudItemIcons icons, Point pointer, OwnedItemView incoming, bool interactive)
    {
        var origin = Origin;
        painter.Sprite(HudNpcArt.Window, origin.X, origin.Y + 40);
        var title = HudStoreLayout.WarehouseTitle(origin);
        painter.Sprite(HudNpcArt.WarehouseTitle, title.X, title.Y);
        if (View is null)
        {
            return;
        }

        if (interactive && _hovered is not null)
        {
            painter.Fill(Bounds(_hovered), HudPlacementColors.Selection);
        }

        if ((_carried ?? incoming) is { } moving && interactive)
        {
            HudPlacementColors.Draw(painter, Placement(moving, pointer));
        }

        var items = View.Items;
        for (var index = 0; index < items.Count; index++)
        {
            var item = items[index];
            _counts.Draw(painter, icons, item, Bounds(item), item == _carried ? HudPlacementColors.Carried : Color.White);
        }

        var plate = HudStoreLayout.GoldPlate(origin);
        painter.Sprite(HudNpcArt.Gold, plate.X, plate.Y);
        var weight = HudStoreLayout.WeightIcon(origin);
        painter.Sprite(HudNpcArt.Weight, weight.X, weight.Y);
        var upper = HudStoreLayout.UpperBar(origin);
        painter.TextCentered(_money, upper.Left, upper.Right, upper.Y + 3);
        var lower = HudStoreLayout.LowerBar(origin);
        painter.TextCentered(_weight, lower.Left + 18, lower.Right, lower.Y + 3);
    }

    public void DrawOverlay(HudPainter painter, HudLayout layout, HudItemIcons icons, Point pointer)
    {
        if (_carried is not null)
        {
            _counts.Draw(painter, icons, _carried, HudGridPlacement.Frame(_carried, pointer), Color.White);
        }
        else if (_hovered is not null)
        {
            HudItemTooltip.Draw(painter, layout, pointer, _counts.Named(_hovered).Name, ClientText.Get("Inventory.HintCarry"));
        }
    }

    private Rectangle Bounds(OwnedItemView item) => HudStoreLayout.Cell(Origin, item.Anchor, item.Width, item.Height);

    private OwnedItemView ItemAt(Point pointer) =>
        HudGridPlacement.ItemAt(HudStoreLayout.Grid(Origin), HudStoreLayout.Columns, View.Items, OnGrid, pointer);
}
