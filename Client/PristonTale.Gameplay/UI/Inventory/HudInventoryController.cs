using PristonTale.Gameplay.World.Remote;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using PristonTale.Gameplay.Screens.Framework;
using PristonTale.Simulation.Characters;
using PristonTale.Simulation.Items;
using PristonTale.Simulation.Items.Usable;
using PristonTale.Gameplay.Localization;
using PristonTale.Contracts.Texts;

namespace PristonTale.Gameplay.UI.Inventory;

public sealed class HudInventoryController(ScreenContext context, Func<InventoryCommand, Task<InventoryOutcome>> applyInventory)
{
    private readonly ScreenContext _context = context;
    private readonly Func<InventoryCommand, Task<InventoryOutcome>> _applyInventory = applyInventory;
    private OwnerState _state;
    private Point _origin;
    private MouseState _previousMouse;
    // The item on the cursor (it keeps its place on the server until it is set down). _carryOnSuccess: what the pending
    // command hands to the cursor when it succeeds (the item an exchange picks up, an equipped item taken off).
    // _carryAfter: an item carried once the owner state reaches the inventory revision _carryAt and shows it in the bag
    // (the state travels apart from the replies).
    private Guid _dragging;
    private Guid _carryOnSuccess;
    private Guid _carryAfter;
    private long _carryAt;
    // The item set down, where, and the revision whose owner state shows it placed (unknown until the reply).
    private Guid _settling;
    private Point _settleAt;
    private long _settleRevision;
    // The item thrown whole to the ground, hidden from its place until the owner state shows it gone (the revision unknown
    // until the reply); a refused throw shows it again.
    private Guid _throwing;
    private long _throwRevision;
    // A ground item picked up with the window open comes to the cursor, as the original puts it on the mouse: the pickup
    // asks for it in the bag as a stack of its own, and the bag item of its code that the pickup's owner state adds (not
    // among _pickupBefore, the items when it left) is carried; until then it shows nowhere. Code 0: none awaited.
    private uint _pickupCode;
    private long _pickupRevision;
    private readonly HashSet<Guid> _pickupBefore = [];
    private bool _visible;
    // The item under the pointer this frame (the window open), for Shift and a belt key.
    private OwnedItemView _hovered;
    // A weapon pair asked for while the player swings or casts: held once that motion ends (the player stops, or the next
    // swing or cast begins), as the original swaps at the end of the motion in progress.
    private int _pairWanted = -1;
    private long _pairWantedDuring;
    private bool _pendingOperation;
    private volatile InventoryOutcome _completedOperation;

    public string Message { get; private set; } = "";

    // Whether the last completed operation succeeded (its message is a confirmation rather than a refusal).
    public bool LastOk { get; private set; }
    public int Completions { get; private set; }
    public Guid Dragging => _dragging;

    // The item on the cursor, or the one just set down until the owner state shows where it went (its place on the server
    // until then is the old one): drawn at the pointer, or where it was set down, and not in its old place.
    public Guid Held => _dragging != Guid.Empty ? _dragging : _settling;

    public Point HeldAt(Point pointer) => _dragging != Guid.Empty ? pointer : _settleAt;

    // The item thrown to the ground and not yet gone from the owner state: drawn nowhere.
    public Guid Thrown => _throwing;

    // Whether the window leaves an owned item out of its place: carried, set down elsewhere, thrown, or picked up for
    // the cursor.
    public bool Hides(OwnedItemView item) => item.InstanceId == Held || item.InstanceId == _throwing || AwaitedPickup(item);

    private bool AwaitedPickup(OwnedItemView item) => _pickupCode != 0 && item.InBag && item.Code == _pickupCode && !_pickupBefore.Contains(item.InstanceId);

    // Called as a ground item's pickup leaves: true when it should come to the cursor (the window open, nothing carried).
    public bool ExpectPickup(uint itemCode)
    {
        if (!_visible || itemCode == 0 || _dragging != Guid.Empty || _pickupCode != 0 || _state is null)
        {
            return false;
        }

        _pickupBefore.Clear();
        foreach (var item in _state.Items)
        {
            _pickupBefore.Add(item.InstanceId);
        }

        _pickupCode = itemCode;
        _pickupRevision = long.MaxValue;
        return true;
    }

    // The awaited pickup's reply: its revision, or nothing to carry when it failed.
    public void PickupAnswered(bool ok, long revision)
    {
        if (_pickupCode == 0)
        {
            return;
        }

        if (ok)
        {
            _pickupRevision = revision;
        }
        else
        {
            _pickupCode = 0;
        }
    }

    // The owned item under the pointer while the window takes input and nothing is carried.
    public OwnedItemView Hovered => _hovered;

    // Set by the HUD each frame: nothing is picked up while the player attacks or casts, as in the original.
    public bool PickupLocked { get; set; }

    // The player's swings and casts begun so far (GameScene.PlayerActionStarts).
    public long ActionStarts { get; set; }

    // Shop and warehouse windows take clicks on owned items and drops outside the inventory while they are open.
    public Func<OwnedItemView, bool> InterceptClick { get; set; }
    public Func<Guid, Point, bool> ExternalDrop { get; set; }

    // An item set down above the window where no shop or warehouse takes it: put on the ground (true when the HUD took it).
    public Func<OwnedItemView, bool> ThrowRequested { get; set; }

    // The coin by the gold, clicked with nothing carried.
    public Action GoldRequested { get; set; }

    // The special item box's button; the HUD opens or closes the box where the original allows it.
    public Action QuestBoxRequested { get; set; }

    // The personal shop button; the HUD opens the player's own shop where the original allows it.
    public Action ShopRequested { get; set; }

    // Whether the special item box shows over the bag (the bag's items hidden), and the bag's item count it closes on
    // when the bag gains an item, as the original closes it to show what was received.
    public bool QuestBoxShown { get; private set; }
    // The costume page is a local UI choice; ownership/which item is worn stay server-authoritative.
    public bool CostumePage { get; private set; }
    public void ToggleCostumePage() => CostumePage = !CostumePage;
    private (long Revision, int Count) _bagCount = (-1, 0);

    // A right click on a carried usable item (premium, travel and effect items).
    public Action<OwnedItemView> UseRequested { get; set; }

    // A right-clicked item a running quest uses (answers true when it took the click).
    public Func<OwnedItemView, bool> QuestItemUse { get; set; }

    // The catalog key of an item lifted from or set into the pack or the equipment, for its sound.
    public Action<string> ItemSound { get; set; }
    private string _pendingSound;

    // A carried item set into the closed special item box (where it was carried): the original rings the drink sound
    // over its own and lights the box glow there.
    public Action<Rectangle> QuestBoxStored { get; set; }
    private static readonly EquipmentSlot[] HandSlots = [EquipmentSlot.Weapon, EquipmentSlot.OffHand];
    private Rectangle? _storedFrame;

    // The warehouse is open with an item on the cursor: the original switches neither the bag page nor the weapon pair.
    public bool SidesLocked { get; set; }
    // The owned items of the last owner state seen (revision -1: none yet), to sound an item the server lays on the
    // character by itself.
    private readonly HashSet<Guid> _owned = [];
    private long _ownedRevision = -1;
    private int _ownedPage;
    private int _ownedWeaponSet;

    // Lets go of the item on the cursor (Esc): it stays where it lies on the server.
    public void CancelDrag()
    {
        _dragging = Guid.Empty;
        _settling = Guid.Empty;
    }

    public void ToggleQuestBox() => QuestBoxShown = !QuestBoxShown;

    public void CloseQuestBox() => QuestBoxShown = false;

    public void CarryAfter(Guid itemId, long revision)
    {
        _carryAfter = itemId;
        _carryAt = revision;
    }

    public void Update(OwnerState state, HudLayout layout, bool visible, KeyboardState keyboard, MouseState mouse)
    {
        _state = state;
        _visible = visible;
        SoundArrival(state);
        _origin = HudInventoryLayout.Origin(layout);
        var completed = _completedOperation;
        if (completed is not null)
        {
            _completedOperation = null;
            _pendingOperation = false;
            // A refusal without a line is the client's own: the server was not reached.
            Message = !completed.Message.IsNone ? ClientText.Format(completed.Message) : completed.Ok ? "" : ClientText.Get("Inventory.OperationFailed");
            LastOk = completed.Ok;
            Completions++;
            if (completed.Ok && _pendingSound is not null)
            {
                ItemSound?.Invoke(_pendingSound);
            }

            if (completed.Ok && _storedFrame is { } stored)
            {
                QuestBoxStored?.Invoke(stored);
            }

            _storedFrame = null;

            if (completed.Ok && _carryOnSuccess != Guid.Empty)
            {
                CarryAfter(_carryOnSuccess, completed.Revision);
            }

            AwaitState(ref _settling, ref _settleRevision, completed);
            AwaitState(ref _throwing, ref _throwRevision, completed);
            _carryOnSuccess = Guid.Empty;
            _pendingSound = null;
        }

        if (_carryAfter != Guid.Empty && state is not null && state.InventoryRevision >= _carryAt)
        {
            if (OwnedItemView.Find(state.Items, _carryAfter) is { InBag: true })
            {
                _dragging = _carryAfter;
            }

            _carryAfter = Guid.Empty;
        }

        Release(ref _settling, _settleRevision, state);
        Release(ref _throwing, _throwRevision, state);
        CarryPickup(state);
        CarryHeld(state);

        if (_pairWanted >= 0 && (!PickupLocked || ActionStarts != _pairWantedDuring) && !_pendingOperation && state is not null)
        {
            if (_pairWanted != state.WeaponSet)
            {
                SendInventory(new InventoryCommand(Guid.NewGuid(), state.InventoryRevision, InventoryAction.SwitchWeaponSet, Guid.Empty, _pairWanted, 0, default));
            }

            _pairWanted = -1;
        }

        // The hover shows while a command is pending too; only the clicks wait for it.
        if (state is not null && state.InventoryRevision != _bagCount.Revision)
        {
            var count = Count(state.Items, HudInventoryLayout.InBag);
            QuestBoxShown &= _bagCount.Revision < 0 || count <= _bagCount.Count;
            _bagCount = (state.InventoryRevision, count);
        }

        _hovered = visible && state is not null && _dragging == Guid.Empty
            ? HudInventoryLayout.ItemAt(_origin, state.Items, layout.ToLogical(mouse.Position), QuestBoxShown)
            : null;
        if (_hovered is { Equipped: true } && CostumePage)
        {
            // Equipment underneath the costume overlay must not receive input.
            _hovered = null;
        }
        if (_hovered is not null && Hides(_hovered))
        {
            _hovered = null;
        }

        if (visible && state is not null && !_pendingOperation)
        {
            var clicked = mouse.LeftButton == ButtonState.Pressed && _previousMouse.LeftButton == ButtonState.Released;
            UpdateInventoryInput(keyboard, mouse, layout.ToLogical(mouse.Position), clicked);
        }
        else
        {
            // The carried item stays on the cursor while the window is closed or under a modal box, as the original keeps the
            // mouse item, and while a command waits (a page switched with it); only a lost owner state lets go of it.
            if (state is null)
            {
                CancelDrag();
            }

            // The belt is on the bottom bar: its potions are drunk with a right click while the window is closed too.
            var pointer = layout.ToLogical(mouse.Position);
            if (state is not null && !_pendingOperation && mouse.RightButton == ButtonState.Pressed && _previousMouse.RightButton == ButtonState.Released
                && HudInventoryLayout.ItemAt(_origin, state.Items, pointer, QuestBoxShown) is { BeltBox: >= 0 } potion)
            {
                Use(potion);
            }
        }

        _previousMouse = mouse;
    }

    // The original's carrying: a click picks an item up (an equipped one is taken off first) and it follows the cursor; the
    // next click sets it down where HudInventoryPlacement says, or takes it out of the bag above the window.
    private void UpdateInventoryInput(KeyboardState keyboard, MouseState mouse, Point pointer, bool clicked)
    {
        // The side buttons take their clicks with an item on the cursor too, which is never set down on them.
        if (clicked)
        {
            if (HudInventoryLayout.CostumeButton(_origin).Contains(pointer))
            {
                ToggleCostumePage();
                return;
            }
            for (var side = 0; side < PlayerCharacter.Sides; side++)
            {
                if (HudInventoryLayout.PageButton(_origin, side).Contains(pointer))
                {
                    ShowBagPage(side);
                    return;
                }

                if (HudInventoryLayout.PairButton(_origin, side).Contains(pointer))
                {
                    HoldWeaponPair(side);
                    return;
                }
            }
        }

        if (_dragging != Guid.Empty)
        {
            var carried = OwnedItemView.Find(_state.Items, _dragging);
            if (carried is null)
            {
                _dragging = Guid.Empty;
            }
            else if (clicked)
            {
                if (CostumePage && HudInventoryLayout.CostumeSlot(_origin).Contains(pointer))
                {
                    if (carried.InBag && carried.Count == 1 && CostumeItemPolicy.IsCosmetic(carried.CatalogKey))
                    {
                        var clearing = _state.CostumeItemId == carried.InstanceId;
                        _dragging = Guid.Empty;
                        SendInventory(new InventoryCommand(Guid.NewGuid(), _state.InventoryRevision,
                            clearing ? InventoryAction.ClearCostume : InventoryAction.WearCostume,
                            clearing ? Guid.Empty : carried.InstanceId, 0, 0, default));
                    }
                    else
                    {
                        Message = "This item cannot be worn as a costume.";
                    }
                    return;
                }
                SetDown(carried, pointer, keyboard.IsKeyDown(Keys.LeftShift) || keyboard.IsKeyDown(Keys.RightShift));
            }

            return;
        }

        if (clicked)
        {
            if (HudInventoryLayout.GoldButton(_origin).Contains(pointer))
            {
                GoldRequested?.Invoke();
                return;
            }

            if (HudInventoryLayout.QuestBoxButton(_origin).Contains(pointer))
            {
                QuestBoxRequested?.Invoke();
                return;
            }

            if (HudInventoryLayout.ShopButton(_origin).Contains(pointer))
            {
                ShopRequested?.Invoke();
                return;
            }
        }

        if (CostumePage && HudInventoryLayout.CostumeSlot(_origin).Contains(pointer))
        {
            var rightClick = mouse.RightButton == ButtonState.Pressed && _previousMouse.RightButton == ButtonState.Released;
            if ((clicked || rightClick) && _state.CostumeItemId != Guid.Empty)
            {
                SendInventory(new InventoryCommand(Guid.NewGuid(), _state.InventoryRevision,
                    InventoryAction.ClearCostume, Guid.Empty, 0, 0, default));
            }
            return;
        }

        var hovered = _hovered;
        if (hovered is null)
        {
            return;
        }

        if (clicked)
        {
            if (InterceptClick?.Invoke(hovered) != true && !PickupLocked)
            {
                PickUp(hovered);
            }

            return;
        }

        if (mouse.RightButton == ButtonState.Pressed && _previousMouse.RightButton == ButtonState.Released)
        {
            Use(hovered);
        }
    }

    private void PickUp(OwnedItemView item)
    {
        if (item.Equipped)
        {
            SendInventory(new InventoryCommand(Guid.NewGuid(), _state.InventoryRevision, InventoryAction.Unequip, item.InstanceId, 0, 0,
                (EquipmentSlot)item.Slot), carryOnSuccess: item.InstanceId);
            return;
        }

        _dragging = item.InstanceId;
        ItemSound?.Invoke(item.CatalogKey);
    }

    private void SetDown(OwnedItemView carried, Point pointer, bool split)
    {
        _settleAt = pointer;
        // A box item goes back to its box wherever it is set down; a box item the full box left in the bag goes to the box
        // once there is room, as the original sets it.
        if (carried.QuestBox >= 0)
        {
            _dragging = Guid.Empty;
            ItemSound?.Invoke(carried.CatalogKey);
            return;
        }

        if (GoesToQuestBox(carried))
        {
            Send(InventoryAction.StoreQuestItem, carried, 0);
            _storedFrame = QuestBoxShown ? null : HudGridPlacement.Frame(carried, pointer);
            return;
        }

        var placement = HudInventoryPlacement.Of(_origin, _state.Items, carried, pointer, HudInventoryPanel.Usable(carried, _state));
        if (placement.Belt >= 0 || carried.BeltBox >= 0)
        {
            SetDownPotion(carried, placement);
            return;
        }

        switch (placement.Kind)
        {
            case HudPlacementKind.Fits or HudPlacementKind.Exchange when placement.Slot is { } slot:
                // Hopy cannot be worn as an ordinary armour by dragging it onto that slot.
                if (CostumeItemPolicy.IsCosmetic(carried.CatalogKey))
                {
                    Message = "Use the costume button and slot to wear Hopy.";
                    break;
                }
                Send(InventoryAction.Equip, carried, 0, 0, slot, placement.Covered);
                break;
            case HudPlacementKind.Fits when placement.Anchor == carried.Anchor:
                _dragging = Guid.Empty;
                ItemSound?.Invoke(carried.CatalogKey);
                break;
            case HudPlacementKind.Fits when split && carried.Count > 1 && carried.InBag:
                Send(InventoryAction.Split, carried, placement.Anchor, carried.Count / 2);
                break;
            case HudPlacementKind.Fits:
                Send(InventoryAction.Move, carried, placement.Anchor);
                break;
            case HudPlacementKind.Merge:
                Send(InventoryAction.Merge, carried, placement.Covered.Anchor);
                break;
            case HudPlacementKind.Exchange:
                Send(InventoryAction.Swap, carried, placement.Anchor, covered: placement.Covered);
                break;
            case HudPlacementKind.Outside when ExternalDrop?.Invoke(carried.InstanceId, pointer) == true:
            case HudPlacementKind.Outside when !QuestBoxShown && ThrowRequested?.Invoke(carried) == true:
                _dragging = Guid.Empty;
                break;
        }
    }

    // The belt's side of setting down: into a box (a bag potion over another kind's box picks that stack up in exchange),
    // or from a box back to free bag cells or onto a bag stack of its kind.
    private void SetDownPotion(OwnedItemView carried, HudPlacement placement)
    {
        if (placement.Belt >= 0 && placement.Belt == carried.BeltBox)
        {
            _dragging = Guid.Empty;
            ItemSound?.Invoke(carried.CatalogKey);
        }
        else if (placement.Belt >= 0 && placement.Kind != HudPlacementKind.Blocked)
        {
            var displaced = placement.Kind == HudPlacementKind.Exchange && carried.InBag ? placement.Covered : null;
            Send(InventoryAction.BeltPut, carried, placement.Belt, covered: displaced);
        }
        else if (placement.Kind == HudPlacementKind.Fits)
        {
            Send(InventoryAction.BeltTake, carried, placement.Anchor);
        }
        else if (placement.Kind == HudPlacementKind.Merge)
        {
            Send(InventoryAction.BeltTake, carried, placement.Covered.Anchor);
        }
    }

    // The bag page (the side buttons, key E): not while a command waits. A carried bag item goes along to the page shown
    // and stays on the cursor. The weapon pair (key W) switches with an item on the cursor too, as the original's does: a
    // carried item is never in a hand slot (taking one off puts it in the bag first). Neither switches while SidesLocked.
    public void ShowBagPage(int page)
    {
        // With the special item box open the page buttons close it instead, as the original does.
        if (QuestBoxShown)
        {
            QuestBoxShown = false;
            return;
        }

        if (!_pendingOperation && !SidesLocked && _state is not null && page != _state.BagPage)
        {
            var carried = OwnedItemView.Find(_state.Items, _dragging) is { InBag: true } item ? item.InstanceId : Guid.Empty;
            SendInventory(new InventoryCommand(Guid.NewGuid(), _state.InventoryRevision, InventoryAction.SwitchBagPage, carried, page, 0, default));
        }
    }

    public void HoldWeaponPair(int pair)
    {
        if (_state is not null && !SidesLocked && pair != _state.WeaponSet)
        {
            _pairWanted = pair;
            _pairWantedDuring = ActionStarts;
        }
    }

    public void ToggleBagPage() => ShowBagPage(1 - (_state?.BagPage ?? 1));

    public void ToggleWeaponPair() => HoldWeaponPair(1 - (_state?.WeaponSet ?? 1));

    // Keys 1 to 3, as in the original: drink the box's potion; with Shift, fill the box from the bag potion under the
    // pointer.
    public void BeltKey(int box, bool shift)
    {
        if (_pendingOperation || _state is null || _dragging != Guid.Empty)
        {
            return;
        }

        if (shift)
        {
            if (_hovered is { InBag: true, Kind: ItemKind.Potion } potion)
            {
                SendInventory(new InventoryCommand(Guid.NewGuid(), _state.InventoryRevision, InventoryAction.BeltPut, potion.InstanceId, box, 0, default));
            }

            return;
        }

        if (OwnedItemView.InBox(_state.Items, box) is { } held)
        {
            Use(held);
        }
    }

    private void Send(InventoryAction action, OwnedItemView carried, int destination, int count = 0, EquipmentSlot slot = default,
        OwnedItemView covered = null)
    {
        _dragging = Guid.Empty;
        _settling = carried.InstanceId;
        _settleRevision = long.MaxValue;
        SendInventory(new InventoryCommand(Guid.NewGuid(), _state.InventoryRevision, action, carried.InstanceId, destination, count, slot),
            covered?.InstanceId ?? Guid.Empty);
    }

    // An item that arrives without a command of this window (a gift, a quest prize, a trade, a pickup) sounds as the
    // original's client sounds every item it sets; one already sounding for its own reply is not repeated
    // (FieldAudioController.PlayItem). The first state after login and a switch of the bag page, which shows the other
    // page's items, sound nothing; a switch of the weapon pair sounds the pair now held, weapon then off-hand, as the
    // original sounds each item it sets in the hands.
    private void SoundArrival(OwnerState state)
    {
        if (state is null || state.InventoryRevision == _ownedRevision)
        {
            return;
        }

        OwnedItemView arrived = null;
        var pairSwitched = _ownedRevision >= 0 && state.WeaponSet != _ownedWeaponSet;
        if (_ownedRevision >= 0 && state.BagPage == _ownedPage && !pairSwitched)
        {
            foreach (var item in state.Items)
            {
                if (!_owned.Contains(item.InstanceId))
                {
                    arrived = item;
                    break;
                }
            }
        }

        _owned.Clear();
        foreach (var item in state.Items)
        {
            _owned.Add(item.InstanceId);
        }

        (_ownedRevision, _ownedPage, _ownedWeaponSet) = (state.InventoryRevision, state.BagPage, state.WeaponSet);
        if (arrived is not null)
        {
            ItemSound?.Invoke(arrived.CatalogKey);
        }

        if (pairSwitched)
        {
            foreach (var slot in HandSlots)
            {
                if (OwnedItemView.InSlot(state.Items, (int)slot) is { } held)
                {
                    ItemSound?.Invoke(held.CatalogKey);
                }
            }
        }
    }

    // An item set down or thrown waits for its own command's reply (the one command waiting since), not a later one's: a
    // success names the revision whose owner state shows it moved, a refusal lets it go back to its place.
    private static void AwaitState(ref Guid item, ref long revision, InventoryOutcome completed)
    {
        if (item != Guid.Empty && revision == long.MaxValue)
        {
            if (completed.Ok)
            {
                revision = completed.Revision;
            }
            else
            {
                item = Guid.Empty;
            }
        }
    }

    private static void Release(ref Guid item, long revision, OwnerState state)
    {
        if (item != Guid.Empty && state is not null && state.InventoryRevision >= revision)
        {
            item = Guid.Empty;
        }
    }

    // The awaited pickup's item to the cursor once its owner state came (none when the bag had no room for it alone and
    // it went where any received item goes).
    private void CarryPickup(OwnerState state)
    {
        if (_pickupCode == 0 || state is null || state.InventoryRevision < _pickupRevision)
        {
            return;
        }

        foreach (var item in state.Items)
        {
            if (_dragging == Guid.Empty && AwaitedPickup(item))
            {
                _dragging = item.InstanceId;
                break;
            }
        }

        _pickupCode = 0;
    }

    // The item in the hand (on neither page; PlayerCharacter.Held) is always on the cursor: after a pickup, a login, or a
    // refused setting down, it comes back as soon as nothing else is carried or awaited.
    private void CarryHeld(OwnerState state)
    {
        if (state is null || _dragging != Guid.Empty || _settling != Guid.Empty || _throwing != Guid.Empty)
        {
            return;
        }

        for (var index = 0; index < state.Items.Count; index++)
        {
            if (state.Items[index].Held)
            {
                _dragging = state.Items[index].InstanceId;
                return;
            }
        }
    }

    // Puts `count` of a bag item, or with no item `count` gold, on the ground where the player stands. An item thrown
    // whole leaves its place at once (Thrown); part of a stack stays where it is.
    public void Drop(OwnedItemView item, int count)
    {
        if (!_pendingOperation && _state is not null)
        {
            if (item is not null && count >= item.Count)
            {
                _throwing = item.InstanceId;
                _throwRevision = long.MaxValue;
            }

            SendInventory(new InventoryCommand(Guid.NewGuid(), _state.InventoryRevision, item is null ? InventoryAction.DropGold : InventoryAction.Drop,
                item?.InstanceId ?? Guid.Empty, 0, count, default));
        }
    }

    // Right click: a usable item is used, a potion drunk, and an item worn or taken off.
    private void Use(OwnedItemView item)
    {
        // Hopy is exclusively cosmetic even if the optional visual catalog is missing.
        if (item.InBag && CostumeCatalog.IsExclusiveCosmeticKey(item.CatalogKey))
        {
            var clearing = _state.CostumeItemId == item.InstanceId;
            SendInventory(new InventoryCommand(Guid.NewGuid(), _state.InventoryRevision,
                clearing ? InventoryAction.ClearCostume : InventoryAction.WearCostume,
                clearing ? Guid.Empty : item.InstanceId, 0, 0, default));
            return;
        }

        if (item.InBag && (EquipmentVisuals.IsCostume(item.CatalogKey)
            || string.Equals(item.CatalogKey, "CA547", StringComparison.OrdinalIgnoreCase)
            || string.Equals(item.CatalogKey, "CA647", StringComparison.OrdinalIgnoreCase)))
        {
            if (!EquipmentVisuals.IsCostume(item.CatalogKey))
            {
                Message = "Costume model is not configured. The original armour was not changed.";
                return;
            }
            var clearing = _state.CostumeItemId == item.InstanceId;
            SendInventory(new InventoryCommand(Guid.NewGuid(), _state.InventoryRevision,
                clearing ? InventoryAction.ClearCostume : InventoryAction.WearCostume,
                clearing ? Guid.Empty : item.InstanceId, 0, 0, default));
            return;
        }

        if (!item.Equipped && QuestItemUse?.Invoke(item) == true)
        {
            return;
        }

        if (item.InBag && UsableItemCatalog.IsUsable(item.CatalogKey))
        {
            UseRequested?.Invoke(item);
            return;
        }

        if (!item.Equipped && item.Kind == ItemKind.Potion)
        {
            SendInventory(new InventoryCommand(Guid.NewGuid(), _state.InventoryRevision, InventoryAction.ConsumePotion, item.InstanceId, 0, 1, default));
            return;
        }

        var slot = item.Equipped ? (EquipmentSlot?)item.Slot : SlotFor(item.Kind);
        if (slot.HasValue)
        {
            SendInventory(new InventoryCommand(Guid.NewGuid(), _state.InventoryRevision,
                item.Equipped ? InventoryAction.Unequip : InventoryAction.Equip, item.InstanceId, 0, 0, slot.Value));
        }
    }

    private void SendInventory(InventoryCommand command, Guid carryOnSuccess = default)
    {
        _pendingOperation = true;
        _carryOnSuccess = carryOnSuccess;
        _pendingSound = command.Action switch
        {
            InventoryAction.ConsumePotion or InventoryAction.SwitchBagPage => null,
            InventoryAction.DropGold => ItemCodes.GoldKey,
            _ => OwnedItemView.Find(_state.Items, command.ItemId)?.CatalogKey
        };
        _ = CompleteInventoryAsync(command);
    }

    private async Task CompleteInventoryAsync(InventoryCommand command)
    {
        try
        {
            _completedOperation = await _applyInventory(command).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            _context.Log.Error("inventory operation failed", exception);
            _completedOperation = new InventoryOutcome(false, 0, GameText.None);
        }
    }

    // A carried bag item the special item box keeps goes there, wherever it is set down, while the box has room.
    public bool GoesToQuestBox(OwnedItemView carried) =>
        carried.InBag && _state is not null && QuestItemBox.Keeps(carried.CatalogKey) && Count(_state.Items, static item => item.QuestBox >= 0) < QuestItemBox.Boxes;

    private static int Count(IReadOnlyList<OwnedItemView> items, Func<OwnedItemView, bool> counts)
    {
        var count = 0;
        for (var index = 0; index < items.Count; index++)
        {
            count += counts(items[index]) ? 1 : 0;
        }

        return count;
    }

    private EquipmentSlot? SlotFor(ItemKind kind) =>
        ItemEquipmentMetadata.DefaultSlot(kind, ItemEquipmentMetadata.SlotOf(kind), slot => OwnedItemView.InSlot(_state.Items, (int)slot) is not null);

}
