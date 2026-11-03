using Microsoft.Xna.Framework;
using PristonTale.Client.Net.Presence;
using PristonTale.Contracts.Social;
using PristonTale.Gameplay.UI.Inventory;
using PristonTale.Gameplay.UI.Npc;
using PristonTale.Gameplay.UI.Rendering;
using PristonTale.Gameplay.UI.Widgets;
using PristonTale.Simulation.Characters;
using PristonTale.Simulation.Items;
using PristonTale.Simulation.Trades;

namespace PristonTale.Gameplay.UI.Trades;

public enum HudTradeAction
{
    None,
    Consumed,
    Close,
    Lock,
    Money,
    TakeBack
}

// The original trade window on the store window's frame (slides in from the left): the partner's grid (9x4 cells of 22 px
// at (21, 86)) over the strip at y 174 and the player's grid at (21, 195), the partner's name, gold and weight and its lock
// mark on the right panel, the player's gold (a click asks how much), weight, lock (26x26 at (253, 260); grey for
// TradeLimits.LockDelayMs after the partner changed its offer) and close (305, 264). The player's offer is what it last
// sent until the server's window replaces it; its items keep their bag cells (Holds).
public sealed class HudTradeWindow : IHudServiceWindow
{
    private const int GridHeight = TradeLimits.Rows * HudGridPlacement.CellSize;
    private readonly HudSlide _slide = new();
    private readonly List<TradeEntry> _entries = [];
    private readonly List<OwnedItemView> _own = [];
    private readonly HudItemCounts _ownCounts = new();
    private readonly HudItemCounts _partnerCounts = new();
    private readonly HudNumberText _ownMoney = new();
    private readonly HudNumberText _ownWeight = new();
    private readonly HudNumberText _partnerMoney = new();
    private readonly HudNumberText _partnerWeight = new();
    private long _revision;
    private int _partnerWeightValue;
    private string _partnerName;
    private string _partnerNameShown;
    private OwnedItemView _hovered;
    private OwnedItemView _described;
    private List<(string Text, Color Color)> _description;

    public TradeInfo Info { get; private set; } = TradeInfo.None;
    public long Money { get; private set; }
    public IReadOnlyList<TradeEntry> Entries => _entries;

    // When the lock works again after the partner changed its offer (the HUD clock, ms).
    public double LockReadyMs { get; private set; }

    // The item a TakeBack click picked.
    public Guid Selected { get; private set; }

    // The player closed the window while it trades (exit, Escape, another window): the HUD cancels the trade.
    public Action Hidden { get; set; }

    public bool IsOpen => _slide.Open;
    public bool Visible => _slide.Visible;
    public bool FullyOpen => _slide.FullyOpen;
    public bool BlocksWorld => true;
    public Point Origin => HudStoreLayout.Origin(_slide.Offset);

    public static Rectangle PartnerGrid(Point origin) => new(origin.X + 21, origin.Y + 86, TradeLimits.Columns * HudGridPlacement.CellSize, GridHeight);
    public static Rectangle OwnGrid(Point origin) => new(origin.X + 21, origin.Y + 195, TradeLimits.Columns * HudGridPlacement.CellSize, GridHeight);
    public static Rectangle MoneyButton(Point origin) => new(origin.X + 235, origin.Y + 205, 25, 21);
    public static Rectangle LockButton(Point origin) => new(origin.X + 253, origin.Y + 260, 25, 26);
    public static Rectangle CloseButton(Point origin) => new(origin.X + 305, origin.Y + 264, 20, 20);

    public bool Holds(Guid itemId)
    {
        foreach (var entry in _entries)
        {
            if (entry.ItemId == itemId)
            {
                return true;
            }
        }

        return false;
    }

    public void Show() => _slide.Open = true;

    // The trade ended on the server: the window goes without cancelling anything.
    public void Close()
    {
        _slide.Open = false;
        Info = TradeInfo.None;
        _entries.Clear();
        Money = 0;
        Changed(null);
    }

    public void Hide()
    {
        if (_slide.Open && Info.Open)
        {
            Hidden?.Invoke();
        }

        Close();
    }

    public void Advance(double seconds) => _slide.Advance(seconds);

    public bool Contains(Point pointer) => _slide.Visible && HudStoreLayout.Window(Origin).Contains(pointer);

    // The server's window: the player's offer as it holds it, the partner's; a changed partner offer greys the lock.
    public void Apply(TradeInfo info, IReadOnlyList<OwnedItemView> owned, double nowMs)
    {
        if (!ReferenceEquals(info.PartnerItems, Info.PartnerItems) && (info.PartnerMoney != Info.PartnerMoney || !SameItems(info.PartnerItems, Info.PartnerItems)))
        {
            LockReadyMs = nowMs + TradeLimits.LockDelayMs;
        }

        Info = info;
        _entries.Clear();
        _entries.AddRange(info.Own.Entries);
        Money = info.Own.Money;
        _partnerWeightValue = Weight(info.PartnerItems);
        _partnerCounts.Refresh(++_revision, info.PartnerItems);
        Changed(owned);
    }

    // The bag changed: the offered items show what the bag holds now.
    public void Follow(IReadOnlyList<OwnedItemView> owned) => Changed(owned);

    // Lays a bag item at the first free place of the player's grid; false when the offer is full or has no room.
    public bool TryOffer(OwnedItemView item, IReadOnlyList<OwnedItemView> owned)
    {
        if (_entries.Count >= TradeLimits.MaxItems || Holds(item.InstanceId))
        {
            return false;
        }

        var occupied = HudStoreLayout.NewGrid(TradeLimits.Rows);
        foreach (var view in _own)
        {
            HudStoreLayout.Occupy(occupied, view.Anchor, view.Width, view.Height);
        }

        var cell = HudStoreLayout.FindSpace(occupied, item.Width, item.Height);
        if (cell < 0)
        {
            return false;
        }

        _entries.Add(new TradeEntry(item.InstanceId, cell));
        Changed(owned);
        return true;
    }

    public void TakeBack(Guid itemId, IReadOnlyList<OwnedItemView> owned)
    {
        _entries.RemoveAll(entry => entry.ItemId == itemId);
        Changed(owned);
    }

    public void SetMoney(long money) => Money = Math.Max(0, money);

    public TradeOffer Offer() => new(_entries.ToArray(), Money);

    public bool CanLock(double nowMs) => Info.Open && !Info.OwnLocked && nowMs >= LockReadyMs;

    public HudTradeAction Click(Point pointer)
    {
        Selected = Guid.Empty;
        if (!Contains(pointer) || !_slide.FullyOpen)
        {
            return Contains(pointer) ? HudTradeAction.Consumed : HudTradeAction.None;
        }

        var origin = Origin;
        if (CloseButton(origin).Contains(pointer))
        {
            return HudTradeAction.Close;
        }

        if (Info.OwnLocked)
        {
            return HudTradeAction.Consumed;
        }

        if (LockButton(origin).Contains(pointer))
        {
            return HudTradeAction.Lock;
        }

        if (MoneyButton(origin).Contains(pointer))
        {
            return HudTradeAction.Money;
        }

        if (ItemAt(_own, OwnGrid(origin), pointer) is { } item)
        {
            Selected = item.InstanceId;
            return HudTradeAction.TakeBack;
        }

        return HudTradeAction.Consumed;
    }

    void IHudServiceWindow.Draw(in HudServiceFrame frame) => Draw(frame.Painter, frame.Icons, frame.Pointer, FullyOpen && !frame.SettingsOpen, frame.NowMs);

    void IHudServiceWindow.DrawOverlay(in HudServiceFrame frame) => DrawOverlay(frame.Painter, frame.Layout, frame.State, frame.Pointer, frame.NowMs);

    public void Draw(HudPainter painter, HudItemIcons icons, Point pointer, bool interactive, double nowMs)
    {
        var origin = Origin;
        var partnerGrid = PartnerGrid(origin);
        var ownGrid = OwnGrid(origin);
        _hovered = interactive ? ItemAt(Info.PartnerItems, partnerGrid, pointer) ?? ItemAt(_own, ownGrid, pointer) : null;
        painter.Sprite(HudNpcArt.Window, origin.X, origin.Y + 40);
        painter.Sprite(HudTradeArt.Title, origin.X + 225, origin.Y + 47);
        for (var cell = 0; cell < TradeLimits.Columns; cell++)
        {
            painter.Sprite(HudTradeArt.Strip, origin.X + 21 + cell * HudGridPlacement.CellSize, origin.Y + 174);
        }

        DrawItems(painter, icons, Info.PartnerItems, partnerGrid, _partnerCounts);
        DrawItems(painter, icons, _own, ownGrid, _ownCounts);
        if (!ReferenceEquals(_partnerName, Info.PartnerName))
        {
            (_partnerName, _partnerNameShown) = (Info.PartnerName, painter.Fit(Info.PartnerName, 100));
        }

        painter.TextCentered(_partnerNameShown, origin.X + 229, origin.X + 332, origin.Y + 94);
        if (Info.PartnerMoney > 0)
        {
            painter.TextCentered(_partnerMoney.Of(Info.PartnerMoney, true), origin.X + 251, origin.X + 326, origin.Y + 127);
        }

        if (_partnerWeightValue > 0)
        {
            painter.TextCentered(_partnerWeight.Of(_partnerWeightValue, true), origin.X + 251, origin.X + 326, origin.Y + 152);
        }

        painter.TextCentered(_ownMoney.Of(Money, true), origin.X + 251, origin.X + 326, origin.Y + 215);
        painter.TextCentered(_ownWeight.Of(Weight(_own), true), origin.X + 251, origin.X + 326, origin.Y + 239);
        if (Info.PartnerLocked)
        {
            painter.Sprite(HudTradeArt.Locked, origin.X + 252, origin.Y + 171);
        }

        var lockButton = LockButton(origin);
        if (Info.Open && !Info.OwnLocked && nowMs < LockReadyMs)
        {
            painter.Sprite(HudTradeArt.LockWaiting, lockButton.X, lockButton.Y);
        }
        else if (Info.OwnLocked)
        {
            painter.Sprite(HudTradeArt.Locked, lockButton.X, lockButton.Y);
        }

        if (interactive && CloseButton(origin).Contains(pointer))
        {
            var close = CloseButton(origin);
            painter.Sprite(HudTradeArt.Close, close.X, close.Y);
        }
    }

    public void DrawOverlay(HudPainter painter, HudLayout layout, OwnerState state, Point pointer, double nowMs)
    {
        var origin = Origin;
        if (LockButton(origin).Contains(pointer) && CanLock(nowMs))
        {
            painter.Sprite(HudTradeArt.LockInfo, origin.X + 253, origin.Y + 233);
            return;
        }

        if (CloseButton(origin).Contains(pointer))
        {
            painter.Sprite(HudTradeArt.CloseInfo, origin.X + 278, origin.Y + 237);
            return;
        }

        if (_hovered is not { } item || state is null)
        {
            return;
        }

        if (!ReferenceEquals(item, _described))
        {
            var counts = Holds(item.InstanceId) ? _ownCounts : _partnerCounts;
            (_described, _description) = (item, HudItemDetailTooltip.Lines(counts.Named(item), state, null, null));
        }

        HudItemDetailTooltip.Draw(painter, layout, pointer, _description);
    }

    private void DrawItems(HudPainter painter, HudItemIcons icons, IReadOnlyList<OwnedItemView> items, Rectangle grid, HudItemCounts counts)
    {
        for (var index = 0; index < items.Count; index++)
        {
            var item = items[index];
            var bounds = HudGridPlacement.Cell(grid, TradeLimits.Columns, item.Anchor, item.Width, item.Height);
            if (ReferenceEquals(item, _hovered))
            {
                painter.Fill(bounds, HudPlacementColors.Selection);
            }

            counts.Draw(painter, icons, item, bounds, Color.White);
        }
    }

    private static OwnedItemView ItemAt(IReadOnlyList<OwnedItemView> items, Rectangle grid, Point pointer)
    {
        for (var index = 0; index < items.Count; index++)
        {
            if (HudGridPlacement.Cell(grid, TradeLimits.Columns, items[index].Anchor, items[index].Width, items[index].Height).Contains(pointer))
            {
                return items[index];
            }
        }

        return null;
    }

    private static int Weight(IReadOnlyList<OwnedItemView> items)
    {
        var weight = 0;
        for (var index = 0; index < items.Count; index++)
        {
            weight += CarriedWeight.Of(items[index].Kind, items[index].Details?.Weight ?? 0, Math.Max(1, items[index].Count));
        }

        return weight;
    }

    private static bool SameItems(IReadOnlyList<OwnedItemView> left, IReadOnlyList<OwnedItemView> right)
    {
        if (left.Count != right.Count)
        {
            return false;
        }

        for (var index = 0; index < left.Count; index++)
        {
            if (left[index].InstanceId != right[index].InstanceId || left[index].Count != right[index].Count || left[index].Anchor != right[index].Anchor)
            {
                return false;
            }
        }

        return true;
    }

    // The offered items as the bag holds them, at their trade grid cells; an item that left the bag leaves the offer.
    private void Changed(IReadOnlyList<OwnedItemView> owned)
    {
        _own.Clear();
        if (owned is not null)
        {
            _entries.RemoveAll(entry => OwnedItemView.Find(owned, entry.ItemId) is not { InBag: true });
            foreach (var entry in _entries)
            {
                _own.Add(OwnedItemView.Find(owned, entry.ItemId) with { Anchor = entry.Cell });
            }
        }

        _ownCounts.Refresh(++_revision, _own);
        _hovered = null;
        _described = null;
    }
}
