using Microsoft.Xna.Framework;
using PristonTale.Gameplay.UI.Inventory;
using PristonTale.Gameplay.UI.Rendering;
using PristonTale.Gameplay.UI.Widgets;
using PristonTale.Simulation.Characters;
using PristonTale.Simulation.World.Npcs;

namespace PristonTale.Gameplay.UI.Npc;

public enum HudShopMode
{
    Buy,
    Sell,
    Repair
}

public enum HudShopAction
{
    None,
    Consumed,
    Exit,
    Buy,
    RepairAll
}

// The NPC shop in the original window: weapon and armour tabs (or the goods title), pages of stock packed into the grid,
// and the buy, sell, repair and repair-all buttons. Buying is a click on the stock; selling and repairing are clicks on
// owned items while that mode is active.
public sealed class HudShopWindow : IHudServiceWindow
{
    private static readonly HudShopButton[] Buttons = Enum.GetValues<HudShopButton>();
    // The hovered offer's price line, made once per offer.
    private object _pricedOffer;
    private string _priceText = "";
    private const int GoodsTab = 2;
    private readonly HudSlide _slide = new();
    private readonly List<List<Placed>>[] _pages = [[], [], []];
    private readonly HudNumberText _price = new();
    private readonly HudNumberText _money = new();
    private ShopOffer _hovered;
    private HudShopButton? _hoveredButton;

    private sealed record Placed(ShopOffer Offer, int Anchor);

    private enum HudShopButton
    {
        Buy,
        Sell,
        Repair,
        RepairAll,
        Exit
    }

    public HudShopMode Mode { get; set; }
    public int Tab { get; private set; }
    public int Page { get; private set; }
    public bool IsOpen => _slide.Open;
    public bool Visible => _slide.Visible;
    public bool FullyOpen => _slide.FullyOpen;
    public Point Origin => HudStoreLayout.Origin(_slide.Offset);
    public int PageCount => _pages[Tab].Count;
    public ShopOffer Selected { get; private set; }

    public IEnumerable<string> IconKeys => _pages.SelectMany(tab => tab).SelectMany(page => page).Select(placed => placed.Offer.CatalogKey);

    public void Show(IReadOnlyList<ShopOffer> catalog)
    {
        for (var tab = 0; tab < _pages.Length; tab++)
        {
            _pages[tab] = Pack(catalog.Where(offer => offer.Tab == tab).OrderBy(offer => offer.Ordinal));
        }

        Tab = Enumerable.Range(0, _pages.Length).FirstOrDefault(tab => _pages[tab].Count > 0);
        Page = 0;
        Mode = HudShopMode.Buy;
        _slide.Open = true;
    }

    public void Hide()
    {
        _slide.Open = false;
        _hovered = null;
    }

    public void Advance(double seconds) => _slide.Advance(seconds);

    public bool BlocksWorld => false;

    void IHudServiceWindow.Draw(in HudServiceFrame frame) => Draw(frame.Painter, frame.Icons, frame.State, frame.Pointer, FullyOpen && !frame.SettingsOpen);

    void IHudServiceWindow.DrawOverlay(in HudServiceFrame frame) => DrawOverlay(frame.Painter, frame.Layout, frame.Pointer);

    public bool Contains(Point pointer) => _slide.Visible && HudStoreLayout.Window(Origin).Contains(pointer);

    public HudShopAction Click(Point pointer)
    {
        Selected = null;
        if (!Contains(pointer))
        {
            return HudShopAction.None;
        }

        var origin = Origin;
        if (HudStoreLayout.Exit(origin).Contains(pointer))
        {
            return HudShopAction.Exit;
        }

        if (Tab != GoodsTab && HudStoreLayout.TabWeapons(origin).Contains(pointer))
        {
            SelectTab(0);
        }
        else if (Tab != GoodsTab && HudStoreLayout.TabArmor(origin).Contains(pointer))
        {
            SelectTab(1);
        }
        else if (HudStoreLayout.LeftArrow(origin).Contains(pointer) && PageCount > 1)
        {
            Page = (Page + PageCount - 1) % PageCount;
        }
        else if (HudStoreLayout.RightArrow(origin).Contains(pointer) && PageCount > 1)
        {
            Page = (Page + 1) % PageCount;
        }
        else if (HudStoreLayout.Buy(origin).Contains(pointer))
        {
            Mode = HudShopMode.Buy;
        }
        else if (HudStoreLayout.Sell(origin).Contains(pointer))
        {
            Mode = Mode == HudShopMode.Sell ? HudShopMode.Buy : HudShopMode.Sell;
        }
        else if (Tab != GoodsTab && HudStoreLayout.Repair(origin).Contains(pointer))
        {
            Mode = Mode == HudShopMode.Repair ? HudShopMode.Buy : HudShopMode.Repair;
        }
        else if (Tab != GoodsTab && HudStoreLayout.RepairAll(origin).Contains(pointer))
        {
            return HudShopAction.RepairAll;
        }
        else if (OfferAt(pointer) is { } offer)
        {
            Selected = offer;
            return HudShopAction.Buy;
        }

        return HudShopAction.Consumed;
    }

    public void Draw(HudPainter painter, HudItemIcons icons, OwnerState state, Point pointer, bool interactive)
    {
        var origin = Origin;
        _hovered = interactive ? OfferAt(pointer) : null;
        _hoveredButton = interactive ? ButtonAt(pointer) : null;
        painter.Sprite(HudNpcArt.Window, origin.X, origin.Y + 40);
        DrawTabs(painter, origin);
        DrawButtons(painter, origin);
        if (PageCount > 1)
        {
            painter.Sprite(HudNpcArt.LeftArrow, HudStoreLayout.LeftArrow(origin).X, HudStoreLayout.LeftArrow(origin).Y);
            painter.Sprite(HudNpcArt.RightArrow, HudStoreLayout.RightArrow(origin).X, HudStoreLayout.RightArrow(origin).Y);
            painter.TextCentered($"{Page + 1}/{PageCount}", origin.X + 228, origin.Y + 330, origin.Y + 154);
        }

        if (Page < PageCount)
        {
            foreach (var placed in _pages[Tab][Page])
            {
                var bounds = HudStoreLayout.Cell(origin, placed.Anchor, placed.Offer.Width, placed.Offer.Height);
                if (ReferenceEquals(placed.Offer, _hovered))
                {
                    painter.Fill(bounds, HudPlacementColors.Selection);
                }

                icons.Draw(painter, placed.Offer.CatalogKey, bounds);
            }
        }

        painter.Sprite(HudNpcArt.Gold, HudStoreLayout.GoldPlate(origin).X, HudStoreLayout.GoldPlate(origin).Y);
        var upper = HudStoreLayout.UpperBar(origin);
        painter.TextCentered(_price.Of(_hovered?.Price ?? 0, _hovered is not null), upper.Left, upper.Right, upper.Y + 3);
        var lower = HudStoreLayout.LowerBar(origin);
        painter.TextCentered(_money.Of(state?.Money ?? 0), lower.Left, lower.Right, lower.Y + 3);
    }

    public void DrawOverlay(HudPainter painter, HudLayout layout, Point pointer)
    {
        if (_hoveredButton is { } button && Info(button) is { } info)
        {
            var bounds = Bounds(button, Origin);
            painter.Sprite(info, bounds.X - 26, bounds.Y - 27);
            return;
        }

        if (_hovered is null)
        {
            return;
        }

        var offer = _hovered;
        if (!ReferenceEquals(offer, _pricedOffer))
        {
            (_pricedOffer, _priceText) = (offer, $"Price: {offer.Price:N0} gold");
        }

        const string action = "Click: buy";
        HudItemTooltip.Draw(painter, layout, pointer, offer.Name, _priceText, action);
    }

    private void DrawTabs(HudPainter painter, Point origin)
    {
        var title = HudStoreLayout.Title(origin);
        if (Tab == GoodsTab)
        {
            painter.Sprite(HudNpcArt.TabGoods, HudStoreLayout.TabGoods(origin).X, HudStoreLayout.TabGoods(origin).Y);
            painter.Sprite(HudNpcArt.TitleGoods, title.X, title.Y);
            return;
        }

        var weapons = HudStoreLayout.TabWeapons(origin);
        var armor = HudStoreLayout.TabArmor(origin);
        painter.Sprite(Tab == 0 ? HudNpcArt.TabWeapons : HudNpcArt.TabWeaponsIdle, weapons.X, weapons.Y);
        painter.Sprite(Tab == 1 ? HudNpcArt.TabArmor : HudNpcArt.TabArmorIdle, armor.X, armor.Y);
        painter.Sprite(HudNpcArt.TitleEquipment, title.X, title.Y);
    }

    private void DrawButtons(HudPainter painter, Point origin)
    {
        if (Tab != GoodsTab)
        {
            DrawButton(painter, origin, HudShopButton.RepairAll, HudNpcArt.RepairAll, HudNpcArt.RepairAllIdle, false);
            DrawButton(painter, origin, HudShopButton.Repair, HudNpcArt.Repair, HudNpcArt.RepairIdle, Mode == HudShopMode.Repair);
        }

        DrawButton(painter, origin, HudShopButton.Sell, HudNpcArt.Sell, HudNpcArt.SellIdle, Mode == HudShopMode.Sell);
        DrawButton(painter, origin, HudShopButton.Buy, HudNpcArt.Buy, HudNpcArt.BuyIdle, Mode == HudShopMode.Buy);
        if (_hoveredButton == HudShopButton.Exit)
        {
            var exit = HudStoreLayout.Exit(origin);
            painter.Sprite(HudNpcArt.Exit, exit.X, exit.Y);
        }
    }

    private void DrawButton(HudPainter painter, Point origin, HudShopButton button, string active, string idle, bool selected)
    {
        var bounds = Bounds(button, origin);
        painter.Sprite(selected || _hoveredButton == button ? active : idle, bounds.X, bounds.Y);
    }

    private HudShopButton? ButtonAt(Point pointer)
    {
        var origin = Origin;
        foreach (var button in Buttons)
        {
            if ((Tab != GoodsTab || button is not (HudShopButton.Repair or HudShopButton.RepairAll)) && Bounds(button, origin).Contains(pointer))
            {
                return button;
            }
        }

        return null;
    }

    private static Rectangle Bounds(HudShopButton button, Point origin) => button switch
    {
        HudShopButton.Buy => HudStoreLayout.Buy(origin),
        HudShopButton.Sell => HudStoreLayout.Sell(origin),
        HudShopButton.Repair => HudStoreLayout.Repair(origin),
        HudShopButton.RepairAll => HudStoreLayout.RepairAll(origin),
        _ => HudStoreLayout.Exit(origin)
    };

    private static string Info(HudShopButton button) => button switch
    {
        HudShopButton.Buy => HudNpcArt.BuyInfo,
        HudShopButton.Sell => HudNpcArt.SellInfo,
        HudShopButton.Repair => HudNpcArt.RepairInfo,
        HudShopButton.RepairAll => HudNpcArt.RepairAllInfo,
        _ => null
    };

    private ShopOffer OfferAt(Point pointer)
    {
        if (!_slide.FullyOpen || Page >= PageCount)
        {
            return null;
        }

        var origin = Origin;
        foreach (var placed in _pages[Tab][Page])
        {
            if (HudStoreLayout.Cell(origin, placed.Anchor, placed.Offer.Width, placed.Offer.Height).Contains(pointer))
            {
                return placed.Offer;
            }
        }

        return null;
    }

    private void SelectTab(int tab)
    {
        if (_pages[tab].Count > 0)
        {
            Tab = tab;
            Page = 0;
        }
    }

    // Stock fills pages of the grid in authored order, each item at the first free place down the columns from the left (the
    // original shop's order); an item starts a new page when the current one has no room for it.
    private static List<List<Placed>> Pack(IEnumerable<ShopOffer> offers)
    {
        var pages = new List<List<Placed>>();
        var occupied = new List<uint[]>();
        foreach (var offer in offers)
        {
            if (offer.Width > HudStoreLayout.Columns || offer.Height > HudStoreLayout.Rows || offer.Width < 1 || offer.Height < 1)
            {
                continue;
            }

            var page = pages.Count - 1;
            var anchor = page >= 0 ? HudStoreLayout.FindSpace(occupied[page], offer.Width, offer.Height) : -1;
            if (anchor < 0)
            {
                pages.Add([]);
                occupied.Add(HudStoreLayout.NewGrid());
                page = pages.Count - 1;
                anchor = HudStoreLayout.FindSpace(occupied[page], offer.Width, offer.Height);
            }

            HudStoreLayout.Occupy(occupied[page], anchor, offer.Width, offer.Height);
            pages[page].Add(new Placed(offer, anchor));
        }

        return pages;
    }
}
