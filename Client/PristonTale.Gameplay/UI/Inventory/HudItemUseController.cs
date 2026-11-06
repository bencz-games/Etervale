using Microsoft.Xna.Framework;
using PristonTale.Engine3D.Diagnostics;
using PristonTale.Gameplay.UI.Rendering;
using PristonTale.Gameplay.UI.Widgets;
using PristonTale.Simulation.Characters;
using PristonTale.Simulation.Items.Usable;
using PristonTale.Gameplay.Localization;
using PristonTale.Contracts.Texts;

namespace PristonTale.Gameplay.UI.Inventory;

// Right-click use of a carried item: most items are sent at once; the Teleport Core first opens its field list and the
// mobile shop asks for confirmation. One use is in flight at a time and carries the inventory revision it was built on.
public sealed class HudItemUseController(Func<ItemUseCommand, Task<ItemUseOutcome>> use, IClientLog log)
{
    private readonly HudRequest<ItemUseOutcome> _request = new(log, "Item use failed");
    private OwnedItemView _confirmItem;
    private TeleportCoreDestination _confirmDestination;
    private Guid _confirmTeleportItem;
    private double _deadSinceMs = -1d;
    private bool _restoreSent;

    public HudTeleportCore Teleport { get; } = new();
    public bool Pending => _request.Pending;
    public bool Confirming => _confirmItem is not null || _confirmDestination is not null;
    public string Message { get; private set; } = "";
    public int Completions { get; private set; }

    // Counts mobile shops opened by a successful use; the HUD opens the shop window when it changes.
    public int ShopsOpened { get; private set; }

    public ItemUseOutcome LastOutcome { get; private set; }

    public bool Captures(Point pointer) => Teleport.Contains(pointer) || Confirming && HudMessageBox.Bounds.Contains(pointer);

    public void Request(OwnedItemView item, OwnerState state)
    {
        if (item is null || state is null || item.Slot >= 0 || Pending || Confirming || !UsableItemCatalog.TryGet(item.CatalogKey, out var rule))
        {
            return;
        }

        switch (rule.Action)
        {
            case UsableItemAction.TeleportCore:
                Teleport.Open(item.InstanceId);
                break;
            case UsableItemAction.MobileShop:
                _confirmItem = item;
                break;
            default:
                Send(new ItemUseCommand(Guid.NewGuid(), state.InventoryRevision, item.InstanceId));
                break;
        }
    }

    // A click while the field list or a confirmation is open; true when the controller took it.
    public bool Click(Point pointer, OwnerState state)
    {
        if (Confirming)
        {
            if (HudMessageBox.Accept.Contains(pointer) && state is not null)
            {
                if (_confirmItem is not null)
                {
                    Send(new ItemUseCommand(Guid.NewGuid(), state.InventoryRevision, _confirmItem.InstanceId));
                }
                else
                {
                    Send(new ItemUseCommand(Guid.NewGuid(), state.InventoryRevision, _confirmTeleportItem, _confirmDestination.Index));
                }

                ClearConfirm();
            }
            else if (HudMessageBox.Reject.Contains(pointer))
            {
                ClearConfirm();
            }

            return true;
        }

        if (!Teleport.Contains(pointer))
        {
            return false;
        }

        if (Teleport.Click(pointer) == HudTeleportAction.Choose)
        {
            _confirmDestination = Teleport.Selected;
            _confirmTeleportItem = Teleport.ItemId;
        }

        return true;
    }

    // A carried Restoration revives its dead owner in a castle field ten seconds after death; the client asks once per
    // death and the server checks the time.
    public void Restore(OwnerState state, bool inCastle, double nowMs)
    {
        if (state is null || state.Life > 0)
        {
            _deadSinceMs = -1d;
            _restoreSent = false;
            return;
        }

        if (_deadSinceMs < 0d)
        {
            _deadSinceMs = nowMs;
        }

        if (!inCastle || Pending || _restoreSent || nowMs - _deadSinceMs < CastleItems.RestorationDelayMs)
        {
            return;
        }

        var restoration = state.Items.FirstOrDefault(static item => item.Slot < 0
            && UsableItemCatalog.TryGet(item.CatalogKey, out var rule) && rule.Action == UsableItemAction.Restoration);
        if (restoration is not null)
        {
            _restoreSent = true;
            Send(new ItemUseCommand(Guid.NewGuid(), state.InventoryRevision, restoration.InstanceId));
        }
    }

    public void Close()
    {
        Teleport.Close();
        ClearConfirm();
    }

    public void Update()
    {
        if (!_request.TryTake(out var outcome))
        {
            return;
        }

        LastOutcome = outcome;
        // A refusal without a line is the client's own: the server was not reached.
        Message = outcome.Message.IsNone ? ClientText.Get("ItemUse.ConnectionError") : ClientText.Format(outcome.Message);
        Completions++;
        if (outcome.Ok && UsableItemCatalog.TryGet(Simulation.Items.ItemCodes.KeyOf(outcome.ItemCode), out var rule)
            && rule.Action == UsableItemAction.MobileShop)
        {
            ShopsOpened++;
        }
    }

    public void Draw(HudPainter painter, Point pointer)
    {
        Teleport.Draw(painter, pointer);
        if (_confirmItem is not null)
        {
            painter.Flush();
            HudMessageBox.Draw(painter, _confirmItem.Name, ClientText.Get("ItemUse.Confirm"), pointer, ClientText.Get("ItemUse.Confirm2"));
        }
        else if (_confirmDestination is not null)
        {
            painter.Flush();
            HudMessageBox.Draw(painter, HudTeleportCore.ConfirmText(_confirmDestination), ClientText.Get("ItemUse.Confirm"), pointer, ClientText.Get("ItemUse.Teleport"));
        }
    }

    private void ClearConfirm()
    {
        _confirmItem = null;
        _confirmDestination = null;
        _confirmTeleportItem = Guid.Empty;
    }

    private void Send(ItemUseCommand command)
    {
        if (use is null)
        {
            return;
        }

        _request.Start(() => use(command), _ => ItemUseOutcome.Refused(command.ExpectedRevision, ItemUseStatus.StorageFailed) with
        {
            Message = GameText.None
        });
    }

}
