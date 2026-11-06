using System.Diagnostics;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using PristonTale.Gameplay.Screens.Framework;
using PristonTale.Gameplay.UI.Chat;
using PristonTale.Gameplay.UI.Inventory;
using PristonTale.Gameplay.UI.Npc;
using PristonTale.Gameplay.UI.Quests;
using PristonTale.Gameplay.UI.Rendering;
using PristonTale.Gameplay.UI.Travel;
using PristonTale.Gameplay.UI.Widgets;
using PristonTale.Simulation.Characters;
using PristonTale.Simulation.Items;
using PristonTale.Simulation.Progression;
using PristonTale.Simulation.Quests;
using PristonTale.Simulation.World.Npcs;
using PristonTale.Contracts.Characters;

namespace PristonTale.Gameplay.UI;

// NPC services: opening their windows, the shop, warehouse and workbench actions, item drops from the bag and the icons they need.
public sealed partial class GameplayHud
{
    private const double TeleportPrefetchDelaySeconds = 0.25;

    private void UpdateTeleportPrefetch(Point pointer)
    {
        var destination = _npcOpen ? _npc.DestinationAt(pointer) : null;
        if (destination is null)
        {
            _hoveredTeleportFieldCode = -1;
            _teleportFieldPrepared = false;
            return;
        }

        if (destination.FieldCode != _hoveredTeleportFieldCode)
        {
            _hoveredTeleportFieldCode = destination.FieldCode;
            _hoveredTeleportSince = _clock.Elapsed.TotalSeconds;
            _teleportFieldPrepared = false;
        }

        if (!_teleportFieldPrepared && _clock.Elapsed.TotalSeconds - _hoveredTeleportSince >= TeleportPrefetchDelaySeconds)
        {
            _prepareField?.Invoke(destination.FieldCode);
            _teleportFieldPrepared = true;
        }
    }

    // A service the server opened shows its window; the NPC dialog gives way to it, as the original opens services directly.
    private void ApplyServices(OwnerState state)
    {
        if (_services.Opened != _serviceOpened)
        {
            _serviceOpened = _services.Opened;
            OpenService();
        }

        if (_services.Open != NpcServiceKind.Shop && _shop.IsOpen)
        {
            _shop.Hide();
        }

        if (_services.Open != NpcServiceKind.Warehouse && _warehouse.IsOpen)
        {
            _warehouse.Hide();
        }

        if (_services.Open != NpcServiceKind.ItemAging && _aging.IsOpen)
        {
            _aging.Hide();
        }

        _aging.Sync(state);
        if (_services.Open != NpcServiceKind.ItemMix && _mix.IsOpen)
        {
            _mix.Hide();
        }

        _mix.Sync(state);

        if (_warehouse.IsOpen && _services.Warehouse is { } view && !ReferenceEquals(view, _warehouse.View))
        {
            _warehouse.Refresh(view);
            RequestIcons(_state);
        }

        if (!_services.Pending)
        {
            _services.RefreshQuotes(state);
        }
    }

    // The icon cache keeps only the last requested set, so every request names everything on screen: the pack, the open
    // shop's stock and the open warehouse.
    private void RequestIcons(OwnerState state)
    {
        RememberKeys(state?.Items);
        RememberKeys(_warehouse.View?.Items);
        var keys = state?.Items.Select(static item => item.CatalogKey) ?? [];
        if (_shop.IsOpen)
        {
            keys = keys.Concat(_shop.IconKeys);
        }

        if (_warehouse.IsOpen && _warehouse.View is { } view)
        {
            keys = keys.Concat(view.Items.Select(static item => item.CatalogKey));
        }

        _icons.Request(keys);
    }

    // Keys of the items seen in the pack or the warehouse, so a sold or stored item still has its sound once it is gone.
    private void RememberKeys(IEnumerable<OwnedItemView> items)
    {
        if (_itemKeys.Count > 4096)
        {
            _itemKeys.Clear();
        }

        foreach (var item in items ?? [])
        {
            _itemKeys[item.InstanceId] = item.CatalogKey;
        }
    }

    // A warehouse exchange hands the item it picked up to the cursor, in the bag or on the grid where it now lies.
    private void ServiceSucceeded(NpcServiceCommand command, NpcServiceOutcome outcome)
    {
        ServiceSound(command);
        if (command.Covered == Guid.Empty)
        {
            return;
        }

        if (command.Action == NpcServiceAction.Deposit)
        {
            _inventory.CarryAfter(command.Covered, outcome.Revision);
        }
        else if (command.Action is NpcServiceAction.Move or NpcServiceAction.Withdraw)
        {
            _warehouse.CarryAfter(command.Covered);
        }
    }

    private void ServiceSound(NpcServiceCommand command)
    {
        var key = command.Action == NpcServiceAction.Buy
            ? ItemCodes.TryFormat(command.ItemCode, out var bought) ? bought : null
            : command.Action is NpcServiceAction.Sell or NpcServiceAction.Deposit or NpcServiceAction.Withdraw or NpcServiceAction.Move
                ? _itemKeys.GetValueOrDefault(command.ItemId)
                : command.Action is NpcServiceAction.DepositGold or NpcServiceAction.WithdrawGold ? ItemCodes.GoldKey : null;
        if (key is not null)
        {
            ItemSound?.Invoke(key);
        }
    }

    // A service window closes the special item box, as the original does for every window it opens.
    private void OpenService()
    {
        _inventory.CloseQuestBox();
        switch (_services.Open)
        {
            case NpcServiceKind.Shop:
                _npcOpen = false;
                _shop.Show(_services.Catalog);
                RequestIcons(_state);
                OpenOnly(_inventorySlide);
                break;
            case NpcServiceKind.Warehouse when _services.Warehouse is { } view:
                _npcOpen = false;
                _warehouse.Show(view);
                RequestIcons(_state);
                OpenOnly(_inventorySlide);
                break;
            case NpcServiceKind.Teleporter:
                _npc.ShowDestinations(_services.Destinations);
                break;
            case NpcServiceKind.ItemAging:
                _npcOpen = false;
                _aging.Show(_npc.Current?.Services.FirstOrDefault(static offer => offer.Kind == NpcServiceKind.ItemAging)?.PricePercent ?? 100);
                RequestIcons(_state);
                OpenOnly(_inventorySlide);
                break;
            case NpcServiceKind.ItemMix:
            {
                _npcOpen = false;
                var offer = _npc.Current?.Services.FirstOrDefault(static offer => offer.Kind == NpcServiceKind.ItemMix);
                _mix.Show(offer?.Value == Simulation.Items.Mixing.MixingWork.AlchemyMode, offer?.PricePercent ?? 100);
                RequestIcons(_state);
                OpenOnly(_inventorySlide);
                break;
            }
            case NpcServiceKind.QuestEvent:
                if (_puzzle.Offer(_state, _services.NpcId, _services.NpcName) is { } help)
                {
                    _notice.Show(help);
                }
                else
                {
                    _npcOpen = false;
                    _npc.Dismiss();
                    _inventorySlide.Open = false;
                }

                break;
            case NpcServiceKind.SkillMaster:
                _npcOpen = false;
                _skillMaster = _services.NpcId;
                OpenOnly(_skillSlide);
                break;
        }
    }

    private void CloseServices()
    {
        _services.Close();
        foreach (var window in _serviceWindows)
        {
            window.Hide();
        }
    }

    private void ClickShop(OwnerState state, Point pointer)
    {
        switch (_shop.Click(pointer))
        {
            case HudShopAction.Exit:
                CloseServices();
                break;
            case HudShopAction.Buy when _shop.Selected is { } offer && state is not null:
                BuyFromShop(offer, state);
                break;
            case HudShopAction.RepairAll when state is not null:
                _services.Request(_services.NpcId, _services.NpcName, NpcServiceKind.Shop, NpcServiceAction.RepairAll, state.InventoryRevision);
                break;
        }
    }

    // As in the original, goods sold in stacks (potions) ask how many, up to a stack and what the gold pays for; other
    // items buy one at a click.
    private void BuyFromShop(ShopOffer offer, OwnerState state)
    {
        if (offer.MaxStack <= 1)
        {
            BuyFromShop(offer, 1);
            return;
        }

        var affordable = offer.Price > 0 ? state.Money / offer.Price : offer.MaxStack;
        if (affordable < 1)
        {
            _notice.Show(ItemRuleMessages.NotEnoughGold);
            return;
        }

        _countBox.Open("How many items", "do you wanna buy?", Math.Min(offer.MaxStack, affordable), count => BuyFromShop(offer, count));
    }

    private void BuyFromShop(ShopOffer offer, int count) =>
        _services.Request(_services.NpcId, _services.NpcName, NpcServiceKind.Shop, NpcServiceAction.Buy, _state?.InventoryRevision ?? 0, offer.Code,
            count: count);

    // As in the original, a stack asks how many of it to sell; an item sells whole.
    private void SellToShop(OwnedItemView item)
    {
        if (item.Count <= 1)
        {
            SellToShop(item, 1);
            return;
        }

        _countBox.Open("How many items", "do you wanna sell?", item.Count, count => SellToShop(item, count));
    }

    private void SellToShop(OwnedItemView item, int count) =>
        _services.Request(_services.NpcId, _services.NpcName, NpcServiceKind.Shop, NpcServiceAction.Sell, _state?.InventoryRevision ?? 0,
            itemId: item.InstanceId, count: count);

    private bool InterceptInventoryClick(OwnedItemView item)
    {
        if (InterceptTradeClick(item) || _personalShop.InterceptBagClick(item))
        {
            return true;
        }

        if (_aging.Holds(item.InstanceId) || _mix.Holds(item.InstanceId))
        {
            return true;
        }

        if (!_shop.FullyOpen || _shop.Mode == HudShopMode.Buy || _state is null)
        {
            return false;
        }

        if (_shop.Mode == HudShopMode.Sell && item.Slot >= 0)
        {
            return true;
        }

        if (_shop.Mode == HudShopMode.Sell)
        {
            SellToShop(item);
            return true;
        }

        _services.Request(_services.NpcId, _services.NpcName, NpcServiceKind.Shop, NpcServiceAction.Repair, _state.InventoryRevision, itemId: item.InstanceId);
        return true;
    }

    // An item set down above the inventory with no window to take it goes to the ground, as in the original: not while a
    // shop, the warehouse, a work window or a travel window is open, nor an item the rules keep; a stack asks how many.
    private bool ThrowFromInventory(OwnedItemView item)
    {
        if (ServiceWindowOpen(blockingOnly: false) || _wingGate.IsOpen || _itemUse.Teleport.IsOpen || !ItemDropRules.CanDrop(item.Kind, item.CatalogKey))
        {
            return false;
        }

        if (item.Count == 1)
        {
            _inventory.Drop(item, 1);
            return true;
        }

        _countBox.Open("How many items", "to drop?", item.Count, count => _inventory.Drop(item, count));
        return true;
    }

    // The first line of the count box's gold questions; the second says what the gold is for.
    private const string GoldQuestion = "How much gold";

    // The coin by the gold: with the warehouse open it stores gold there (as much as is carried and the warehouse still
    // holds), otherwise it drops gold on the ground, not while a work window or the wing gate is open, as the original
    // refuses it; each asks how much.
    private void GoldFromInventory()
    {
        if (_state is null)
        {
            return;
        }

        if (_warehouse.FullyOpen && _warehouse.View is { } view)
        {
            _countBox.Open(GoldQuestion, "to store?", Math.Min(_state.Money, WarehouseStorage.MaxMoney - view.Money),
                amount => _services.Request(_services.NpcId, _services.NpcName, NpcServiceKind.Warehouse, NpcServiceAction.DepositGold,
                    _state?.InventoryRevision ?? 0, amount: amount));
            return;
        }

        if (!ServiceWindowOpen(blockingOnly: true) && !_wingGate.IsOpen)
        {
            _countBox.Open(GoldQuestion, "to drop?", _state.Money, amount => _inventory.Drop(null, amount));
        }
    }

    // An inventory item set down on the shop is sold; on the warehouse grid it is stored where HudWarehouseWindow places it.
    private bool DropFromInventory(Guid itemId, Point pointer)
    {
        if (_state is null)
        {
            return false;
        }

        if (_shop.FullyOpen && _shop.Contains(pointer))
        {
            if (OwnedItemView.Find(_state.Items, itemId) is { } sold)
            {
                SellToShop(sold);
            }

            return true;
        }

        var dropped = OwnedItemView.Find(_state.Items, itemId);
        if ((_aging.FullyOpen ? _aging.Place(dropped, pointer, _state) : _mix.FullyOpen ? _mix.Place(dropped, pointer, _state) : null) is { } refusal)
        {
            if (refusal.Length > 0)
            {
                _notice.Show(refusal);
            }

            return true;
        }

        // Over the grid: free cells store it, one stored item is exchanged; a refusal keeps it on the cursor.
        if (_warehouse.SetDownOnGrid(dropped, pointer) is { Kind: not HudWarehouseActionKind.None } action)
        {
            ApplyWarehouseAction(_state, action);
            return action.Kind == HudWarehouseActionKind.Deposit;
        }

        return false;
    }

    private void ApplyWarehouseAction(OwnerState state, HudWarehouseAction action)
    {
        var revision = state?.InventoryRevision ?? 0;
        switch (action.Kind)
        {
            case HudWarehouseActionKind.Exit:
                CloseServices();
                break;
            case HudWarehouseActionKind.Lifted:
                if (_itemKeys.GetValueOrDefault(action.ItemId) is { } key)
                {
                    ItemSound?.Invoke(key);
                }

                break;
            case HudWarehouseActionKind.Deposit or HudWarehouseActionKind.Move or HudWarehouseActionKind.Withdraw:
                var service = action.Kind switch
                {
                    HudWarehouseActionKind.Deposit => NpcServiceAction.Deposit,
                    HudWarehouseActionKind.Move => NpcServiceAction.Move,
                    _ => NpcServiceAction.Withdraw
                };
                _services.Request(_services.NpcId, _services.NpcName, NpcServiceKind.Warehouse, service, revision, itemId: action.ItemId,
                    anchor: action.Anchor, covered: action.Covered);
                break;
            case HudWarehouseActionKind.WithdrawGold when state is not null && _warehouse.View is { } view:
                // As much as is stored and the character may still carry, as the original bounds the count.
                _countBox.Open(GoldQuestion, "to take out?", Math.Min(view.Money, MoneyLimits.CarryLimit(state.Level, state.Tier) - state.Money),
                    amount => _services.Request(_services.NpcId, _services.NpcName, NpcServiceKind.Warehouse, NpcServiceAction.WithdrawGold,
                        _state?.InventoryRevision ?? 0, amount: amount));
                break;
            case HudWarehouseActionKind.Refused:
                _notice.Show(action.Message);
                break;
        }
    }

    private void ApplyWorkAction(HudWorkbench bench, OwnerState state, HudWorkActionKind action)
    {
        switch (action)
        {
            case HudWorkActionKind.Exit:
                CloseServices();
                break;
            case HudWorkActionKind.Start when state is not null:
                if (state.Money < bench.Price(state))
                {
                    _notice.Show(ItemRuleMessages.NotEnoughGold);
                    break;
                }

                bench.Started(state);
                _services.Request(_services.NpcId, _services.NpcName, bench.Service, bench.WorkAction, state.InventoryRevision, itemId: bench.Item,
                    materials: bench.Materials);
                break;
        }
    }

    // The result of the work the bench sent last, once it arrived; a refusal without a result ends the craft as failed.
    private void ShowWorkResult(HudWorkbench bench)
    {
        if (_services.LastCommand is not { } command || command.Action != bench.WorkAction)
        {
            return;
        }

        if (_services.LastWork is not { } work)
        {
            if (!_services.LastOk)
            {
                CraftResult?.Invoke(false);
            }

            return;
        }

        _notice.Show(bench.ResultText(work.Result));
        CraftResult?.Invoke(bench.Succeeded(work.Result));
        CloseServices();
    }

    // Where a warehouse item carried over the inventory window would land in the bag.
    private HudPlacement StoredPlacement(OwnedItemView carried, Point pointer) =>
        _inventorySlide.FullyOpen && _state is not null
            ? HudInventoryPlacement.OfStored(PanelOrigin(Layout(), _inventorySlide), _state.Items, carried, pointer)
            : default;

    private string InventoryNote(OwnedItemView item)
    {
        if (!_shop.FullyOpen || _services.QuoteFor(item.InstanceId) is not { } quote)
        {
            return "";
        }

        if (_shop.Mode == HudShopMode.Repair)
        {
            return quote.RepairCost > 0 ? $"Repair: {quote.RepairCost:N0} gold" : "Not damaged";
        }

        return quote.SellPrice >= 0 ? $"Sells for {quote.SellPrice:N0} gold" : "The shop does not buy this";
    }

    private Task<PristonTale.Simulation.Items.Usable.ItemUseOutcome> UseItemAsync(PristonTale.Simulation.Items.Usable.ItemUseCommand command) =>
        UseItem is { } use ? use(command)
            : Task.FromResult(PristonTale.Simulation.Items.Usable.ItemUseOutcome.Refused(command.ExpectedRevision,
                PristonTale.Simulation.Items.Usable.ItemUseStatus.NotUsable));
}
