using Microsoft.Xna.Framework;
using PristonTale.Contracts.Social;
using PristonTale.Gameplay.UI.Inventory;
using PristonTale.Gameplay.UI.Npc;
using PristonTale.Gameplay.UI.Rendering;
using PristonTale.Gameplay.UI.Widgets;
using PristonTale.Simulation.Characters;
using PristonTale.Simulation.PersonalShops;
using PristonTale.Simulation.World.Npcs;

namespace PristonTale.Gameplay.UI.PersonalShops;

public enum HudPersonalShopAction
{
    None,
    Consumed,
    Exit,
    Start,
    Stop,
    Unlist,
    Buy
}

// The original personal shop on the store window: the player's own listing (laid down each column from the left, with
// the start and stop buttons, what it all sells for and the gold still within what the player may carry) or another
// player's shop (its offers, the price under the pointer and the player's gold). A click on an offer buys it, or takes an
// item back off the player's own listing while it does not sell.
public sealed class HudPersonalShopWindow : IHudServiceWindow
{
    private readonly HudSlide _slide = new();
    private readonly List<PersonalShopOffer> _offers = [];
    private readonly List<OwnedItemView> _views = [];
    private readonly HudItemCounts _counts = new();
    private readonly HudNumberText _upper = new();
    private readonly HudNumberText _lower = new();
    private long _revision;
    private PersonalShopOffer _hovered;
    private Button? _hoveredButton;

    private enum Button
    {
        Start,
        Stop,
        Buy,
        Exit
    }

    private static readonly Button[] Buttons = Enum.GetValues<Button>();
    private PersonalShopOffer _described;
    private List<(string Text, Color Color)> _description;

    // Another player's shop; otherwise the player's own listing.
    public bool Buying { get; private set; }
    public ulong SellerId { get; private set; }

    // The player's own shop is selling: its listing holds.
    public bool Selling { get; set; }

    public IReadOnlyList<PersonalShopOffer> Offers => _offers;
    public PersonalShopOffer Selected { get; private set; }
    public bool IsOpen => _slide.Open;
    public bool Visible => _slide.Visible;
    public bool FullyOpen => _slide.FullyOpen;
    public bool BlocksWorld => true;
    public Point Origin => HudStoreLayout.Origin(_slide.Offset);

    public long Total
    {
        get
        {
            long total = 0;
            foreach (var offer in _offers)
            {
                total += offer.UnitPrice * offer.Item.Count;
            }

            return total;
        }
    }

    public void ShowOwn()
    {
        if (Buying)
        {
            Replace([]);
        }

        Buying = false;
        SellerId = 0;
        _slide.Open = true;
    }

    public void Show(PersonalShopListing listing)
    {
        Buying = true;
        SellerId = listing.SellerId;
        Replace(listing.Offers);
        _slide.Open = true;
    }

    // An own listing that does not sell is forgotten with the window, as the original's is.
    public void Hide()
    {
        _slide.Open = false;
        _hovered = null;
        if (!Buying && !Selling)
        {
            Replace([]);
        }
    }

    public void Advance(double seconds) => _slide.Advance(seconds);

    public bool Contains(Point pointer) => _slide.Visible && HudStoreLayout.Window(Origin).Contains(pointer);

    // An item of the player's listing, which the bag keeps from being moved.
    public bool Holds(Guid itemId) => !Buying && Find(itemId) >= 0;

    public IReadOnlyList<PersonalShopEntry> Entries() => _offers.Select(static offer => new PersonalShopEntry(offer.Item.InstanceId, offer.UnitPrice, offer.Item.Anchor)).ToArray();

    // Lays a bag item on the player's listing at the first free place; false when the listing is full or has no room.
    public bool TryList(OwnedItemView item, long unitPrice)
    {
        if (Buying || _offers.Count >= PersonalShopLimits.MaxEntries || Find(item.InstanceId) >= 0)
        {
            return false;
        }

        var occupied = HudStoreLayout.NewGrid();
        foreach (var offer in _offers)
        {
            HudStoreLayout.Occupy(occupied, offer.Item.Anchor, offer.Item.Width, offer.Item.Height);
        }

        var anchor = HudStoreLayout.FindSpace(occupied, item.Width, item.Height);
        if (anchor < 0)
        {
            return false;
        }

        _offers.Add(new PersonalShopOffer(item with { Anchor = anchor }, unitPrice));
        Changed();
        return true;
    }

    public void Unlist(Guid itemId)
    {
        var index = Find(itemId);
        if (index >= 0)
        {
            _offers.RemoveAt(index);
            Changed();
        }
    }

    // The player's listing follows its bag: an item that left it leaves the listing, a stack shows what remains of it.
    public void Follow(IReadOnlyList<OwnedItemView> owned)
    {
        if (Buying)
        {
            return;
        }

        var changed = false;
        for (var index = _offers.Count - 1; index >= 0; index--)
        {
            var offer = _offers[index];
            var item = OwnedItemView.Find(owned, offer.Item.InstanceId);
            if (item is not { InBag: true })
            {
                _offers.RemoveAt(index);
                changed = true;
            }
            else if (item.Count != offer.Item.Count || !ReferenceEquals(item.Details, offer.Item.Details))
            {
                _offers[index] = offer with { Item = item with { Anchor = offer.Item.Anchor } };
                changed = true;
            }
        }

        if (changed)
        {
            Changed();
        }
    }

    public HudPersonalShopAction Click(Point pointer)
    {
        Selected = null;
        if (!Contains(pointer))
        {
            return HudPersonalShopAction.None;
        }

        switch (ButtonAt(pointer))
        {
            case Button.Exit:
                return HudPersonalShopAction.Exit;
            case Button.Start:
                return HudPersonalShopAction.Start;
            case Button.Stop:
                return HudPersonalShopAction.Stop;
        }

        if (OfferAt(pointer) is { } offer)
        {
            Selected = offer;
            return Buying ? HudPersonalShopAction.Buy : Selling ? HudPersonalShopAction.Consumed : HudPersonalShopAction.Unlist;
        }

        return HudPersonalShopAction.Consumed;
    }

    void IHudServiceWindow.Draw(in HudServiceFrame frame) => Draw(frame.Painter, frame.Icons, frame.State, frame.Pointer, FullyOpen && !frame.SettingsOpen);

    void IHudServiceWindow.DrawOverlay(in HudServiceFrame frame) => DrawOverlay(frame.Painter, frame.Layout, frame.State, frame.Pointer);

    public void Draw(HudPainter painter, HudItemIcons icons, OwnerState state, Point pointer, bool interactive)
    {
        var origin = Origin;
        _hovered = interactive ? OfferAt(pointer) : null;
        _hoveredButton = interactive ? ButtonAt(pointer) : null;
        painter.Sprite(HudNpcArt.Window, origin.X, origin.Y + 40);
        var title = HudStoreLayout.Title(origin);
        painter.Sprite(HudPersonalShopArt.Title, title.X, title.Y);
        var picture = HudStoreLayout.Picture(origin);
        painter.Sprite(HudPersonalShopArt.Picture, picture.X, picture.Y);
        DrawButtons(painter, origin);
        foreach (var view in _views)
        {
            var bounds = HudStoreLayout.Cell(origin, view.Anchor, view.Width, view.Height);
            if (ReferenceEquals(view, _hovered?.Item))
            {
                painter.Fill(bounds, HudPlacementColors.Selection);
            }

            _counts.Draw(painter, icons, view, bounds, Color.White);
        }

        var plate = HudStoreLayout.GoldPlate(origin);
        painter.Sprite(HudNpcArt.Gold, plate.X, plate.Y);
        var money = state?.Money ?? 0;
        var upper = HudStoreLayout.UpperBar(origin);
        var lower = HudStoreLayout.LowerBar(origin);
        if (Buying)
        {
            painter.TextCentered(_upper.Of(_hovered?.UnitPrice ?? 0, _hovered is not null), upper.Left, upper.Right, upper.Y + 3);
            painter.TextCentered(_lower.Of(money), lower.Left, lower.Right, lower.Y + 3);
            return;
        }

        var total = Total;
        var room = state is null ? 0 : PersonalShopRules.GoldRoom(state.Level, state.Tier, money, total);
        painter.TextCentered(_upper.Of(total), upper.Left, upper.Right, upper.Y + 3);
        painter.TextCentered(_lower.Of(room), lower.Left, lower.Right, lower.Y + 3);
    }

    public void DrawOverlay(HudPainter painter, HudLayout layout, OwnerState state, Point pointer)
    {
        if (_hoveredButton is { } button && Info(button) is { } info)
        {
            var bounds = Bounds(button, Origin);
            painter.Sprite(info, bounds.X - (Buying ? 26 : 34), bounds.Y - 27);
            return;
        }

        if (_hovered is not { } offer || state is null)
        {
            return;
        }

        if (!ReferenceEquals(offer, _described))
        {
            var price = offer.Item.Count > 1 ? $"Selling price per unit: {offer.UnitPrice:N0}" : $"The Price: {offer.UnitPrice:N0}";
            var hint = Buying ? "Click: buy" : Selling ? "On Sale" : "Click: take back";
            (_described, _description) = (offer, HudItemDetailTooltip.Lines(_counts.Named(offer.Item), state, price, hint));
        }

        HudItemDetailTooltip.Draw(painter, layout, pointer, _description);
    }

    private void DrawButtons(HudPainter painter, Point origin)
    {
        if (Buying)
        {
            var buy = Bounds(Button.Buy, origin);
            painter.Sprite(_hoveredButton == Button.Buy || _hovered is not null ? HudNpcArt.Buy : HudNpcArt.BuyIdle, buy.X, buy.Y);
        }
        else
        {
            var start = Bounds(Button.Start, origin);
            var stop = Bounds(Button.Stop, origin);
            painter.Sprite(Selling || _hoveredButton == Button.Start ? HudPersonalShopArt.Start : HudPersonalShopArt.StartIdle, start.X, start.Y);
            painter.Sprite(_hoveredButton == Button.Stop ? HudPersonalShopArt.Stop : HudPersonalShopArt.StopIdle, stop.X, stop.Y);
        }

        if (_hoveredButton == Button.Exit)
        {
            var exit = HudStoreLayout.Exit(origin);
            painter.Sprite(HudNpcArt.Exit, exit.X, exit.Y);
        }
    }

    private Button? ButtonAt(Point pointer)
    {
        var origin = Origin;
        foreach (var button in Buttons)
        {
            if ((button == Button.Buy) == Buying || button == Button.Exit)
            {
                if (Bounds(button, origin).Contains(pointer))
                {
                    return button;
                }
            }
        }

        return null;
    }

    // The original puts the start button where the NPC shop repairs and the stop button where it repairs everything.
    private static Rectangle Bounds(Button button, Point origin) => button switch
    {
        Button.Start => HudStoreLayout.Repair(origin),
        Button.Stop => HudStoreLayout.RepairAll(origin),
        Button.Buy => HudStoreLayout.Buy(origin),
        _ => HudStoreLayout.Exit(origin)
    };

    private string Info(Button button) => button switch
    {
        Button.Start => HudPersonalShopArt.StartInfo,
        Button.Stop => HudPersonalShopArt.StopInfo,
        Button.Buy => HudNpcArt.BuyInfo,
        _ => null
    };

    private PersonalShopOffer OfferAt(Point pointer)
    {
        if (!_slide.FullyOpen)
        {
            return null;
        }

        var origin = Origin;
        foreach (var offer in _offers)
        {
            if (HudStoreLayout.Cell(origin, offer.Item.Anchor, offer.Item.Width, offer.Item.Height).Contains(pointer))
            {
                return offer;
            }
        }

        return null;
    }

    private int Find(Guid itemId)
    {
        for (var index = 0; index < _offers.Count; index++)
        {
            if (_offers[index].Item.InstanceId == itemId)
            {
                return index;
            }
        }

        return -1;
    }

    private void Replace(IReadOnlyList<PersonalShopOffer> offers)
    {
        _offers.Clear();
        _offers.AddRange(offers);
        Changed();
    }

    private void Changed()
    {
        _views.Clear();
        foreach (var offer in _offers)
        {
            _views.Add(offer.Item);
        }

        _counts.Refresh(++_revision, _views);
        _hovered = null;
    }
}
