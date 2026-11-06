using Microsoft.Xna.Framework;
using PristonTale.Client.Net.Presence;
using PristonTale.Contracts.Social;
using PristonTale.Gameplay.UI.Trades;
using PristonTale.Gameplay.UI.Widgets;
using PristonTale.Simulation.Characters;
using PristonTale.Simulation.Trades;
using PristonTale.Gameplay.Localization;

namespace PristonTale.Gameplay.UI;

// The trade: its window on the store frame, which opens when the server opens the trade (closing the other service
// windows and opening the bag, as the original does), the request box, and the bag clicks that offer items while the window
// shows. Closing the window cancels the trade.
public sealed partial class GameplayHud
{
    private readonly HudTradeWindow _trade = new();
    private readonly HudRequestBox _tradeRequest = new(HudTradeArt.Request, TradeLimits.RequestMs);

    // Sends the trade requests; set by the screen.
    public ITradeClient Trades { get; set; }

    // The trade part of each presence update. As in the original, a request is not shown while a service window is open.
    public void ObserveTrade(PresenceUpdate update)
    {
        var nowMs = _clock.Elapsed.TotalMilliseconds;
        if (update.Trade is { } info)
        {
            if (!info.Open)
            {
                _trade.Close();
            }
            else
            {
                if (!_trade.IsOpen)
                {
                    _partyWindow.Friends.Met(info.PartnerName);
                    CloseServices();
                    _inventory.CloseQuestBox();
                    OpenOnly(_inventorySlide);
                    _trade.Show();
                }

                _trade.Apply(info, _state?.Items, nowMs);
            }
        }

        if (update.TradeRequest is { } request && !ServiceWindowOpen(blockingOnly: false))
        {
            _tradeRequest.Show(request.FromId, request.FromName, null, nowMs);
        }
    }

    private void SetUpTrade()
    {
        _sprites.Request(HudTradeArt.All);
        _trade.Hidden = () => Trades?.CancelTradeAsync();
    }

    private void SendOffer() => Trades?.OfferTradeAsync(_trade.Offer());

    // While the window shows, a click on a bag item offers it (until the player locks) and never lifts it.
    private bool InterceptTradeClick(OwnedItemView item)
    {
        if (!_trade.FullyOpen || !item.InBag)
        {
            return false;
        }

        if (_trade.Info.OwnLocked || _trade.Holds(item.InstanceId))
        {
            return true;
        }

        if (!TradeRules.IsTradable(item.Kind, item.CatalogKey))
        {
            _notice.Show(TradeMessages.Of(TradeStatus.NotTradable));
        }
        else if (_trade.TryOffer(item, _state?.Items))
        {
            SendOffer();
        }
        else
        {
            _notice.Show(TradeMessages.Of(TradeStatus.TooMany));
        }

        return true;
    }

    private bool ClickTrade(Point pointer)
    {
        var from = _tradeRequest.FromId;
        if (_tradeRequest.Click(pointer) is { } answer)
        {
            Trades?.AnswerTradeAsync(from, answer);
            return true;
        }

        switch (_trade.Click(pointer))
        {
            case HudTradeAction.None:
                return false;
            case HudTradeAction.Close:
                _trade.Hide();
                break;
            case HudTradeAction.Lock when _trade.CanLock(_clock.Elapsed.TotalMilliseconds):
                Trades?.LockTradeAsync();
                break;
            case HudTradeAction.Money when _state is not null:
                _countBox.Open(ClientText.Get("Count.HowMuchGold"), ClientText.Get("Trade.Gold"), _state.Money, count =>
                {
                    _trade.SetMoney(count);
                    SendOffer();
                }, zeroAccepted: true);
                break;
            case HudTradeAction.TakeBack:
                _trade.TakeBack(_trade.Selected, _state?.Items);
                SendOffer();
                break;
        }

        return true;
    }

    // The original's question for the gold a trade offers.
}
