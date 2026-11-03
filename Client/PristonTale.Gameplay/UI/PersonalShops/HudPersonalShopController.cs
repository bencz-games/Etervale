using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using PristonTale.Engine3D.Diagnostics;
using PristonTale.Gameplay.UI.Rendering;
using PristonTale.Gameplay.UI.Widgets;
using PristonTale.Simulation.Characters;
using PristonTale.Simulation.PersonalShops;
using PristonTale.Simulation.World.Npcs;

namespace PristonTale.Gameplay.UI.PersonalShops;

// The personal shop on the HUD, as the original runs it. The inventory's shop button opens the player's own listing in a
// town: while it does not sell, a click on a bag item asks its price per unit and lays it out (a listed item keeps its
// place in the bag), a click on a listed item asks to take it back, and the start button asks for the sign and opens the
// shop. While it sells nothing in the bag moves and the player stands still; the window may close, and a button at the
// left reopens it. Another player's shop opens when its seller is reached: a click on an offer asks how many (a stack) or
// whether to buy it.
public sealed class HudPersonalShopController(IClientLog log)
{
    private const string NoRoom = "The shop has no room for this item";
    private const string NothingToSell = "No more items to sell";
    // How long an opened shop waits for its sign (the player's description) before it counts as closed again: 300 of the
    // game's ticks.
    private const double SignWaitMs = 300 * 1000d / PristonTale.Contracts.Units.GameUnits.TicksPerSecond;
    private static readonly Rectangle ReopenBounds = new(10, 220, 70, 70);

    private readonly HudRequest<PersonalShopReply> _request = new(log, "Personal shop request failed");
    private readonly HudSignBox _sign = new();
    private PersonalShopOffer _confirm;
    private OwnerState _state;
    private long _followed = -1;
    private PersonalShopAction _sent;
    private ulong _sentTo;
    private double _nowMs;
    private double _signWaitUntilMs;

    public HudPersonalShopWindow Window { get; } = new();
    public IPersonalShopClient Client { get; set; }
    public Action<string> Notify { get; set; } = static _ => { };

    // The count box: two lines, the largest number, what to do with the number accepted.
    public Action<string, string, long, Action<int>> AskCount { get; set; } = static (_, _, _, _) => { };

    // The player's own sign hangs over it (set by the HUD every frame).
    public bool SignShown { get; set; }

    // The player's own shop sells: its sign hangs, or an opened shop waits for it.
    public bool Selling => SignShown || _nowMs < _signWaitUntilMs;

    public bool ModalOpen => _sign.IsOpen || _confirm is not null;
    public bool SignOpen => _sign.IsOpen;
    private bool ReopenShown => Selling && !Window.IsOpen;

    public bool Holds(Guid itemId) => Window.Holds(itemId);

    public bool Captures(Point pointer) => ModalOpen || ReopenShown && ReopenBounds.Contains(pointer);

    public void OpenOwn(bool inTown)
    {
        if (!inTown && !Selling)
        {
            Notify(PersonalShopMessages.Of(PersonalShopStatus.NotInTown));
            return;
        }

        Window.ShowOwn();
    }

    public void View(ulong sellerId)
    {
        if (!Selling && Client is { } client)
        {
            Send(PersonalShopAction.View, sellerId, () => client.ViewPersonalShopAsync(sellerId));
        }
    }

    public void Update(OwnerState state, double nowMs)
    {
        _state = state;
        _nowMs = nowMs;
        if (SignShown)
        {
            _signWaitUntilMs = 0;
        }

        if (state is not null && state.InventoryRevision != _followed)
        {
            _followed = state.InventoryRevision;
            Window.Follow(state.Items);
        }

        if (_request.TryTake(out var reply))
        {
            Apply(reply);
        }

        Window.Selling = Selling;
    }

    public void Type(ReadOnlySpan<char> typed, Func<Keys, bool> pressed, Func<char, bool> canDraw, Func<string, int> measure) =>
        _sign.Type(typed, pressed, canDraw, measure);

    // A click on a bag item while the player sells (nothing moves) or its own listing shows (it is the listing's).
    public bool InterceptBagClick(OwnedItemView item)
    {
        if (Selling)
        {
            return true;
        }

        if (!Window.IsOpen || Window.Buying)
        {
            return false;
        }

        if (Window.Holds(item.InstanceId) || !item.InBag || _state is not { } state)
        {
            return true;
        }

        if (!PersonalShopRules.IsListable(item.Kind, item.CatalogKey))
        {
            Notify(PersonalShopMessages.Of(PersonalShopStatus.NotListable));
            return true;
        }

        var room = PersonalShopRules.GoldRoom(state.Level, state.Tier, state.Money, Window.Total);
        var highest = Math.Min(PersonalShopRules.MaxPrice, room / Math.Max(1, item.Count));
        if (highest < 1)
        {
            Notify(PersonalShopMessages.Of(PersonalShopStatus.GoldLimit));
            return true;
        }

        var (first, second) = item.Count > 1 ? ("Selling price per unit", "of the item?") : ("Do you wanna register", "your item money?");
        AskCount(first, second, highest, price =>
        {
            if (!Window.TryList(item, price))
            {
                Notify(NoRoom);
            }
        });
        return true;
    }

    public bool Click(Point pointer)
    {
        if (_sign.IsOpen)
        {
            _sign.Click(pointer);
            return true;
        }

        if (_confirm is { } confirm)
        {
            if (HudMessageBox.Accept.Contains(pointer))
            {
                _confirm = null;
                if (Window.Buying)
                {
                    Buy(confirm, 1);
                }
                else
                {
                    Window.Unlist(confirm.Item.InstanceId);
                }
            }
            else if (HudMessageBox.Reject.Contains(pointer))
            {
                _confirm = null;
            }

            return true;
        }

        if (ReopenShown && ReopenBounds.Contains(pointer))
        {
            Window.ShowOwn();
            return true;
        }

        switch (Window.Click(pointer))
        {
            case HudPersonalShopAction.None:
                return false;
            case HudPersonalShopAction.Exit:
                Window.Hide();
                break;
            case HudPersonalShopAction.Start when !Selling:
                Start();
                break;
            case HudPersonalShopAction.Stop when Selling && Client is { } client:
                Send(PersonalShopAction.Close, 0, client.ClosePersonalShopAsync);
                break;
            case HudPersonalShopAction.Unlist when Window.Selected is { } offer:
                _confirm = offer;
                break;
            case HudPersonalShopAction.Buy when Window.Selected is { } offer:
                Ask(offer);
                break;
        }

        return true;
    }

    // Escape closes the question first, then the window; false when nothing was open.
    public bool Close()
    {
        if (_sign.IsOpen || _confirm is not null)
        {
            _sign.Close();
            _confirm = null;
            return true;
        }

        if (!Window.IsOpen)
        {
            return false;
        }

        Window.Hide();
        return true;
    }

    public void Draw(HudPainter painter)
    {
        if (ReopenShown)
        {
            painter.Stretch(HudPersonalShopArt.Reopen, ReopenBounds);
        }
    }

    public void DrawModal(HudPainter painter, Point pointer)
    {
        if (_sign.IsOpen)
        {
            _sign.Draw(painter, pointer);
        }
        else if (_confirm is { } confirm)
        {
            HudMessageBox.Draw(painter, "Do you wanna", Window.Buying ? "buy the item?" : "cancel the Register?", pointer, confirm.Item.Name);
        }
    }

    private void Start()
    {
        if (Window.Offers.Count == 0)
        {
            Notify(NothingToSell);
            return;
        }

        _sign.Open(sign =>
        {
            if (Client is { } client)
            {
                var entries = Window.Entries();
                Send(PersonalShopAction.Open, 0, () => client.OpenPersonalShopAsync(sign, entries));
            }
        });
    }

    // A stack asks how many, up to what the gold pays for; one item asks whether to buy it.
    private void Ask(PersonalShopOffer offer)
    {
        var affordable = offer.UnitPrice > 0 ? (_state?.Money ?? 0) / offer.UnitPrice : 0;
        if (affordable < 1)
        {
            Notify(PersonalShopMessages.Of(PersonalShopStatus.NotEnoughGold));
            return;
        }

        if (offer.Item.Count > 1)
        {
            AskCount("How many items", "do you wanna buy?", Math.Min(offer.Item.Count, affordable), count => Buy(offer, count));
            return;
        }

        _confirm = offer;
    }

    private void Buy(PersonalShopOffer offer, int count)
    {
        if (Client is { } client && _state is { } state)
        {
            var purchase = new PersonalShopPurchase(Guid.NewGuid(), Window.SellerId, offer.Item.InstanceId, offer.UnitPrice, count, state.InventoryRevision);
            Send(PersonalShopAction.Buy, purchase.SellerId, () => client.BuyFromPersonalShopAsync(purchase));
        }
    }

    private void Send(PersonalShopAction action, ulong sellerId, Func<Task<PersonalShopReply>> send)
    {
        if (_request.Pending)
        {
            Notify(PersonalShopMessages.Of(PersonalShopStatus.Busy));
            return;
        }

        (_sent, _sentTo) = (action, sellerId);
        _request.Start(send, _ => new PersonalShopReply(new PersonalShopOutcome(false, PersonalShopStatus.Rejected, 0, HudRequest.ConnectionError)));
    }

    // A view shows the shop unless the player's own listing took the window meanwhile; a purchase refreshes the shop it was
    // made in while it still shows, or closes it. An opened shop sells until its sign shows; a refusal says why.
    private void Apply(PersonalShopReply reply)
    {
        var shown = Window.IsOpen && Window.Buying && Window.SellerId == _sentTo;
        if (_sent == PersonalShopAction.View && reply.Listing is { } listing && (!Window.IsOpen || Window.Buying))
        {
            Window.Show(listing);
        }
        else if (_sent == PersonalShopAction.Buy && shown)
        {
            if (reply.Listing is { } current)
            {
                Window.Show(current);
            }
            else if (reply.Outcome.Ok || reply.Outcome.Status is PersonalShopStatus.Closed or PersonalShopStatus.OutOfReach)
            {
                Window.Hide();
            }
        }
        else if (_sent == PersonalShopAction.Open && reply.Outcome.Ok)
        {
            _signWaitUntilMs = _nowMs + SignWaitMs;
        }
        else if (_sent == PersonalShopAction.Close && reply.Outcome.Ok)
        {
            _signWaitUntilMs = 0;
        }

        if (!reply.Outcome.Ok)
        {
            Notify(reply.Outcome.Message);
        }
    }
}
