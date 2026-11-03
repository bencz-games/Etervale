using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using PristonTale.Gameplay.UI.Inventory;
using PristonTale.Gameplay.UI.Rendering;
using PristonTale.Gameplay.UI.Widgets;
using PristonTale.Simulation.Characters;
using PristonTale.Simulation.World.Npcs;

namespace PristonTale.Gameplay.UI.Npc;

public enum HudWorkActionKind
{
    None,
    Consumed,
    Exit,
    Start
}

// The frame the aging and mixing windows share: it slides in beside the bag with a main item box, a stone box and the
// sheltom slots. Items dropped from the bag are placed by the window's rules and taken back with a right click; a
// placed item that leaves the bag leaves the window. The start button runs the work once the window is ready.
public abstract class HudWorkbench : IHudServiceWindow
{
    private OwnedItemView _described;
    private OwnerState _describedState;
    private List<(string Text, Color Color)> _description;
    private AgingGauge? _describedGauge;


    private readonly HudSlide _slide = new();
    private OwnedItemView _hovered;
    private bool _exitHovered;

    protected Guid[] Slots { get; } = new Guid[HudCraftLayout.SheltomSlots];
    protected Guid ItemId { get; set; }
    protected Guid StoneId { get; set; }
    protected int PricePercent { get; private set; } = 100;
    protected bool StartHovered { get; private set; }

    public bool IsOpen => _slide.Open;
    public bool Visible => _slide.Visible;
    public bool FullyOpen => _slide.FullyOpen;
    public bool BlocksWorld => true;
    public Point Origin => HudStoreLayout.Origin(_slide.Offset);
    public Guid Item => ItemId;

    public Guid[] Materials => Slots.Where(static id => id != Guid.Empty).Append(StoneId).Where(static id => id != Guid.Empty).ToArray();

    public abstract bool Ready(OwnerState state);

    public abstract long Price(OwnerState state);

    // The NPC service and the action the start button asks for.
    public abstract NpcServiceKind Service { get; }

    public abstract NpcServiceAction WorkAction { get; }

    // The work starts (the request goes out); a window keeps what its result line needs.
    public virtual void Started(OwnerState state)
    {
    }

    // The line the player reads for a finished work's result code, and whether the work succeeded.
    public abstract string ResultText(int result);

    public abstract bool Succeeded(int result);

    public void Hide()
    {
        _slide.Open = false;
        Clear();
    }

    public void Clear()
    {
        ItemId = Guid.Empty;
        StoneId = Guid.Empty;
        Array.Clear(Slots);
        _hovered = null;
    }

    public void Advance(double seconds) => _slide.Advance(seconds);

    public bool Contains(Point pointer) => _slide.Visible && HudStoreLayout.Window(Origin).Contains(pointer);

    public bool Holds(Guid id) => id != Guid.Empty && (id == ItemId || id == StoneId || Array.IndexOf(Slots, id) >= 0);

    public void Sync(OwnerState state)
    {
        if (state is null)
        {
            return;
        }

        bool Owned(Guid id) => id == Guid.Empty || OwnedItemView.Find(state.Items, id) is { InBag: true };
        if (!Owned(ItemId))
        {
            MainItemLeft();
        }

        if (!Owned(StoneId))
        {
            StoneId = Guid.Empty;
        }

        for (var slot = 0; slot < Slots.Length; slot++)
        {
            if (!Owned(Slots[slot]))
            {
                Slots[slot] = Guid.Empty;
            }
        }
    }

    // An inventory item dropped on the window; null when the drop missed the window, else the refusal text ("" when placed).
    public string Place(OwnedItemView item, Point pointer, OwnerState state)
    {
        var origin = Origin;
        if (!FullyOpen || item is null || item.Slot >= 0 || !HudStoreLayout.Window(origin).Contains(pointer))
        {
            return null;
        }

        return Holds(item.InstanceId) ? "" : PlaceItem(item, origin, pointer, state);
    }

    public HudWorkActionKind Update(MouseState mouse, MouseState previous, Point pointer, OwnerState state, bool blocked)
    {
        var origin = Origin;
        StartHovered = HudCraftLayout.Start(origin).Contains(pointer);
        _exitHovered = HudStoreLayout.Exit(origin).Contains(pointer);
        if (!FullyOpen || blocked)
        {
            return HudWorkActionKind.None;
        }

        var pressed = mouse.LeftButton == ButtonState.Pressed && previous.LeftButton == ButtonState.Released;
        var rightClicked = mouse.RightButton == ButtonState.Pressed && previous.RightButton == ButtonState.Released;
        if (rightClicked && PlacedAt(pointer) is { } placed)
        {
            Remove(placed);
            return HudWorkActionKind.Consumed;
        }

        if (!pressed)
        {
            return HudWorkActionKind.None;
        }

        if (_exitHovered)
        {
            return HudWorkActionKind.Exit;
        }

        if (StartHovered && Ready(state))
        {
            return HudWorkActionKind.Start;
        }

        return Contains(pointer) ? HudWorkActionKind.Consumed : HudWorkActionKind.None;
    }

    public void Draw(HudPainter painter, HudItemIcons icons, Point pointer, OwnerState state, OwnedItemView incoming, bool interactive)
    {
        var origin = Origin;
        _hovered = null;
        painter.Sprite(HudNpcArt.Window, origin.X, origin.Y + 40);
        DrawPanel(painter, origin, state);
        if (incoming is not null && interactive)
        {
            DrawPlacement(painter, origin, incoming, pointer, state);
        }

        var items = state?.Items ?? [];
        for (var index = 0; index < items.Count; index++)
        {
            var item = items[index];
            if (PlacedBounds(item, origin) is not { } bounds)
            {
                continue;
            }

            if (interactive && bounds.Contains(pointer))
            {
                _hovered = item;
            }

            icons.Draw(painter, item.CatalogKey, bounds);
        }

        if (ShowsStart(state))
        {
            var start = HudCraftLayout.Start(origin);
            painter.Sprite(HudCraftArt.StartButton, start.X, start.Y);
        }

        if (_exitHovered && interactive)
        {
            var exit = HudStoreLayout.Exit(origin);
            painter.Sprite(HudNpcArt.Exit, exit.X, exit.Y);
        }

        if (ShowsPrice(state))
        {
            var price = HudCraftLayout.PriceText(origin);
            painter.TextCentered(_price.Of(Price(state)), price.Left, price.Right, price.Y);
        }
    }

    public void DrawOverlay(HudPainter painter, HudLayout layout, Point pointer, OwnerState state)
    {
        if (StartHovered && Ready(state))
        {
            DrawStartInfo(painter, HudCraftLayout.StartInfo(Origin));
        }
        else if (_hovered is not null)
        {
            // The box's lines are made again only when the hovered item or the owner state change.
            if (!ReferenceEquals(_hovered, _described) || !ReferenceEquals(state, _describedState))
            {
                (_described, _describedState) = (_hovered, state);
                _description = HudItemDetailTooltip.Lines(_hovered, state, "", "Right-click: take back");
                _describedGauge = HudItemDetailTooltip.Gauge(_hovered, state);
            }

            HudItemDetailTooltip.Draw(painter, layout, pointer, _description, _describedGauge);
        }
    }

    void IHudServiceWindow.Draw(in HudServiceFrame frame) =>
        Draw(frame.Painter, frame.Icons, frame.Pointer, frame.State, frame.Incoming, FullyOpen && !frame.SettingsOpen);

    void IHudServiceWindow.DrawOverlay(in HudServiceFrame frame) => DrawOverlay(frame.Painter, frame.Layout, frame.Pointer, frame.State);

    protected void Open(int pricePercent)
    {
        Clear();
        PricePercent = Math.Max(0, pricePercent);
        _slide.Open = true;
    }

    // An item the window does not hold yet, dropped on it: "" when placed, else the refusal text.
    protected abstract string PlaceItem(OwnedItemView item, Point origin, Point pointer, OwnerState state);

    // The title, the panel and the price plate (and whatever the panel shows of the main item).
    protected abstract void DrawPanel(HudPainter painter, Point origin, OwnerState state);

    // How the box under the pointer takes the item being dragged.
    protected abstract void DrawPlacement(HudPainter painter, Point origin, OwnedItemView incoming, Point pointer, OwnerState state);

    protected abstract void DrawStartInfo(HudPainter painter, Point info);

    protected abstract bool ShowsStart(OwnerState state);

    protected abstract bool ShowsPrice(OwnerState state);

    // The main item left the bag or was taken back.
    protected abstract void MainItemLeft();

    protected OwnedItemView MainItem(OwnerState state) =>
        OwnedItemView.Find(state?.Items, ItemId);

    private void Remove(Guid id)
    {
        if (id == ItemId)
        {
            MainItemLeft();
        }
        else if (id == StoneId)
        {
            StoneId = Guid.Empty;
        }
        else if (Array.IndexOf(Slots, id) is >= 0 and var slot)
        {
            Slots[slot] = Guid.Empty;
        }
    }

    private Guid? PlacedAt(Point pointer)
    {
        var origin = Origin;
        if (StoneId != Guid.Empty && HudCraftLayout.Stone(origin).Contains(pointer))
        {
            return StoneId;
        }

        var slot = HudCraftLayout.SheltomAt(origin, pointer);
        if (slot >= 0 && Slots[slot] != Guid.Empty)
        {
            return Slots[slot];
        }

        return ItemId != Guid.Empty && HudCraftLayout.ItemBox(origin).Contains(pointer) ? ItemId : null;
    }

    private readonly HudNumberText _price = new();

    // Where an owned item placed on the bench is drawn; null for one not on it.
    private Rectangle? PlacedBounds(OwnedItemView item, Point origin)
    {
        if (item.InstanceId == ItemId)
        {
            return HudGridPlacement.Frame(item, HudCraftLayout.ItemBox(origin).Center);
        }

        if (item.InstanceId == StoneId)
        {
            return HudCraftLayout.Stone(origin);
        }

        return Array.IndexOf(Slots, item.InstanceId) is >= 0 and var slot ? HudCraftLayout.Sheltom(origin, slot) : null;
    }
}
