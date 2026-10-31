using Microsoft.Xna.Framework;
using PristonTale.Gameplay.UI.Rendering;
using PristonTale.Simulation.Characters;
using PristonTale.Simulation.Items;
using PristonTale.Simulation.Items.Usable;

namespace PristonTale.Gameplay.UI.Inventory;

// The original inventory window and its text, with the original equipment covers and selection colors.
public sealed class HudInventoryPanel
{
    private readonly HudItemCounts _counts = new();
    // Whether the bag holds a puzzle piece or lucky bag (the board shows) and the puzzle is finished, per revision.
    private (bool Shown, bool Complete) _puzzle;
    private long _puzzleRevision = long.MinValue;
    private long _money = -1;
    private string _gold = "";
    private (int Carried, int Max) _weightValue = (-1, -1);
    private string _weight = "";
    private OwnerState _state;
    private OwnedItemView _hovered;
    // An item is on the cursor (from the bag or the warehouse), so _hovered is only described.
    private bool _carrying;
    // The item on the cursor or just set down (HudInventoryController.Held) and where it is drawn.
    private OwnedItemView _held;
    private Point _heldAt;
    // What the window colours on the belt boxes, drawn over the bottom bar by DrawBelt: the hovered potion, the landing box.
    private Rectangle _beltSelection;
    private (Rectangle Bounds, Color Color) _beltPlacement;

    // The HUD clock that sweeps the finished puzzle's scan lines.
    public double ClockMs { get; set; }

    // An extra tooltip line for the hovered item, such as what an open shop pays for it.
    public Func<OwnedItemView, string> Note { get; set; }

    public Func<Guid, bool> Withheld { get; set; }

    // stored: the warehouse item on the cursor, whose landing the window colours too.
    public void Draw(HudPainter painter, Point origin, OwnerState state, HudInventoryController controller, HudItemIcons icons,
        Point pointer, bool interactive, OwnedItemView stored = null)
    {
        _hovered = null;
        _carrying = false;
        _held = null;
        _beltSelection = Rectangle.Empty;
        _beltPlacement = default;
        painter.Sprite(HudArt.Inventory, origin.X, origin.Y + 400);
        if (state is null)
        {
            return;
        }

        Refresh(state);
        _state = state;
        var boxShown = controller.QuestBoxShown;
        if (boxShown)
        {
            for (var box = 0; box < QuestItemBox.Boxes; box++)
            {
                var cell = HudInventoryLayout.QuestBoxCell(origin, box);
                painter.Sprite(HudArt.QuestBoxTile, cell.X, cell.Y);
            }
        }
        else
        {
            DrawPuzzleBoard(painter, origin);
        }

        var items = state.Items;
        for (var index = 0; index < items.Count; index++)
        {
            var item = items[index];
            if (!HudInventoryLayout.Shows(item, boxShown))
            {
                continue;
            }

            if (item.Equipped)
            {
                var slot = HudInventoryLayout.Equipment(origin, item.Slot);
                painter.Sprite(HudInventoryLayout.Cover(item.Slot), slot.X, slot.Y);
            }

            if (!Usable(item, state))
            {
                // Items the character cannot use, in the bag or worn (a worn one then adds nothing).
                painter.Fill(HudInventoryLayout.Bounds(origin, item), HudPlacementColors.Blocked);
            }
        }

        var shadow = HudInventoryLayout.TwoHandedShadow(items);
        var offHand = HudInventoryLayout.Equipment(origin, (int)EquipmentSlot.OffHand);
        if (shadow is not null)
        {
            painter.Sprite(HudInventoryLayout.Cover((int)EquipmentSlot.OffHand), offHand.X, offHand.Y);
            if (!Usable(shadow, state))
            {
                painter.Fill(offHand, HudPlacementColors.Blocked);
            }
        }

        var dragged = OwnedItemView.Find(state.Items, controller.Dragging);
        var hovered = interactive && stored is null ? controller.Hovered : null;
        if (hovered?.BeltBox >= 0)
        {
            _beltSelection = HudInventoryLayout.Belt(origin, hovered.BeltBox);
        }
        else if (hovered is not null)
        {
            var onShadow = hovered.InstanceId == shadow?.InstanceId && offHand.Contains(pointer);
            painter.Fill(onShadow ? offHand : HudInventoryLayout.Bounds(origin, hovered), HudPlacementColors.Selection);
        }

        // A box item goes back to its box, and a bag item the box keeps goes into it, wherever they are set down: no landing
        // to colour.
        if ((dragged ?? stored) is { QuestBox: < 0 } carried && interactive && !controller.GoesToQuestBox(carried))
        {
            var placement = dragged is null
                ? HudInventoryPlacement.OfStored(origin, state.Items, carried, pointer)
                : HudInventoryPlacement.Of(origin, state.Items, carried, pointer, Usable(carried, state));
            if (placement.Belt >= 0)
            {
                _beltPlacement = (placement.Highlight, HudPlacementColors.Of(placement.Kind));
            }
            else
            {
                HudPlacementColors.Draw(painter, placement);
            }
        }

        for (var index = 0; index < items.Count; index++)
        {
            var item = items[index];
            // The carried item is on the cursor only: its place stays empty until it is set down, as in the original, and
            // a thrown or picked up item's until the owner state shows where it went.
            if (item.BeltBox >= 0 || !HudInventoryLayout.Shows(item, boxShown) || controller.Hides(item))
            {
                continue;
            }

            var tint = Withheld?.Invoke(item.InstanceId) == true ? HudPlacementColors.Carried : Color.White;
            _counts.Draw(painter, icons, item, HudInventoryLayout.Bounds(origin, item), tint);
            if (item.InstanceId == shadow?.InstanceId)
            {
                _counts.Draw(painter, icons, item, offHand, tint);
            }
        }

        DrawButtons(painter, origin, state, boxShown, pointer, interactive);
        var money = HudInventoryLayout.Money(origin);
        painter.TextCentered(_gold, money.Left, money.Right, money.Y);
        var weight = HudInventoryLayout.Weight(origin);
        painter.TextCentered(_weight, weight.Left, weight.Right, weight.Y);
        _carrying = interactive && (dragged ?? stored) is not null;
        _hovered = _carrying ? CarriedOver(origin, state, controller, dragged, pointer) : hovered;
        _held = OwnedItemView.Find(state.Items, controller.Held);
        _heldAt = controller.HeldAt(pointer);
    }

    // While a puzzle piece or lucky bag is carried, the board backdrop covers the last four columns' top four rows; a
    // finished puzzle glows yellow under two orange lines that sweep the board one pixel every three 70 Hz frames.
    private void DrawPuzzleBoard(HudPainter painter, Point origin)
    {
        if (!_puzzle.Shown)
        {
            return;
        }

        var board = HudInventoryLayout.Cell(origin, Puzzles.BoardColumn, Puzzles.BoardSize, Puzzles.BoardSize);
        painter.Sprite(HudArt.PuzzleBoard, board.X, board.Y);
        if (!_puzzle.Complete)
        {
            return;
        }

        painter.Fill(board, Color.FromNonPremultiplied(255, 255, 0, 30));
        var travel = board.Height - 6;
        var step = (int)(ClockMs / (3000d / 70d)) % (travel * 2);
        var offset = step < travel ? step : travel * 2 - step;
        var line = Color.FromNonPremultiplied(255, 123, 0, 180);
        painter.Fill(new Rectangle(board.X, board.Y + offset, board.Width, 2), line);
        painter.Fill(new Rectangle(board.X, board.Bottom - 2 - offset, board.Width, 2), line);
    }

    // The window closed with an item on the cursor: the item stays at the pointer, drawn by DrawOverlay, as the original
    // keeps the mouse item.
    public void TrackHeld(OwnerState state, HudInventoryController controller, Point pointer)
    {
        _hovered = null;
        _held = state is null ? null : OwnedItemView.Find(state.Items, controller.Dragging);
        _heldAt = pointer;
    }

    // The item under a carried one: the window takes no hover then (HudInventoryController.Hovered), but the original
    // still shows the item box of what the pointer is over.
    internal static OwnedItemView CarriedOver(Point origin, OwnerState state, HudInventoryController controller, OwnedItemView dragged, Point pointer)
    {
        var under = HudInventoryLayout.ItemAt(origin, state.Items, pointer, controller.QuestBoxShown);
        return under is null || under.InstanceId == dragged?.InstanceId || controller.Hides(under) ? null : under;
    }

    // Drawn after the bottom bar so the held item and the item box stay on top, as the original draws items after the
    // interface; the item box goes over the carried item, as there.
    public void DrawOverlay(HudPainter painter, HudLayout layout, HudItemIcons icons, Point pointer)
    {
        if (_held is not null)
        {
            _counts.Draw(painter, icons, _held, HudGridPlacement.Frame(_held, _heldAt), Color.White);
        }

        if (_hovered is not null)
        {
            DrawTooltip(painter, layout, _hovered, pointer);
        }
    }

    // Requirements are checked against the owner's current level and attributes, as the original re-checks them on every
    // stat change; items without details count as usable.
    public static bool Usable(OwnedItemView item, OwnerState state) =>
        item.Details is null || state is null || item.Details.Usable(state.Level,
            new CharacterAttributes(state.Strength, state.Spirit, state.Talent, state.Agility, state.Health));

    // The belt's potions over the bottom bar (drawn after it, the window open or not), with the window's colours on its
    // boxes; origin is the window's origin when fully open (HudInventoryLayout.Origin).
    public void DrawBelt(HudPainter painter, Point origin, OwnerState state, HudInventoryController controller, HudItemIcons icons)
    {
        if (state is null)
        {
            return;
        }

        Refresh(state);
        if (_beltSelection != Rectangle.Empty)
        {
            painter.Fill(_beltSelection, HudPlacementColors.Selection);
        }

        if (_beltPlacement.Color != Color.Transparent)
        {
            painter.Fill(_beltPlacement.Bounds, _beltPlacement.Color);
        }

        foreach (var item in state.Items)
        {
            if (item.BeltBox >= 0 && !controller.Hides(item))
            {
                _counts.Draw(painter, icons, item, HudInventoryLayout.Bounds(origin, item), Color.White);
            }
        }

        // Set again by the next Draw of the window; a closed window colours nothing.
        _beltSelection = Rectangle.Empty;
        _beltPlacement = default;
    }

    // The original lights the shown page's and the held pair's arrow and the open special item box's button; the button
    // under the pointer lights too and shows its label above it, and the close button shows its exit sign.
    private static void DrawButtons(HudPainter painter, Point origin, OwnerState state, bool boxShown, Point pointer, bool interactive)
    {
        var questBox = HudInventoryLayout.QuestBoxButton(origin);
        if (!boxShown)
        {
            painter.Sprite(HudArt.QuestBoxButton, questBox.X, questBox.Y);
        }

        DrawButton(painter, questBox, HudArt.QuestBoxButtonOpen, boxShown, boxShown ? HudArt.QuestBoxCloseHint : HudArt.QuestBoxHint, 0, pointer, interactive);
        for (var side = 0; side < PlayerCharacter.Sides; side++)
        {
            var art = side == 0 ? HudArt.SideLeft : HudArt.SideRight;
            DrawButton(painter, HudInventoryLayout.PageButton(origin, side), art, side == state.BagPage, HudArt.PageHints[side], 0, pointer, interactive);
            DrawButton(painter, HudInventoryLayout.PairButton(origin, side), art, side == state.WeaponSet, HudArt.PairHints[side], 0, pointer, interactive);
        }

        DrawButton(painter, HudInventoryLayout.ShopButton(origin), PersonalShops.HudPersonalShopArt.InventoryButton, false,
            PersonalShops.HudPersonalShopArt.InventoryInfo, -39, pointer, interactive);
        DrawButton(painter, HudInventoryLayout.Close(origin), Npc.HudNpcArt.Exit, false, HudArt.CloseHint, -8, pointer, interactive);
    }

    private static void DrawButton(HudPainter painter, Rectangle button, string art, bool lit, string hint, int hintShift, Point pointer, bool interactive)
    {
        var hovered = interactive && button.Contains(pointer);
        if (lit || hovered)
        {
            painter.Sprite(art, button.X, button.Y);
        }

        if (hovered)
        {
            painter.Sprite(hint, button.X + hintShift, button.Y - HudInventoryLayout.HintRise);
        }
    }

    private void DrawTooltip(HudPainter painter, HudLayout layout, OwnedItemView item, Point pointer)
    {
        var usable = item.InBag && UsableItemCatalog.IsUsable(item.CatalogKey);
        // A click with an item carried sets it down, so no action is offered for the item under it.
        var action = _carrying ? ""
            : item.Equipped ? "Right-click: unequip"
            : item.QuestBox >= 0 ? "Click: carry"
            : item.Kind == ItemKind.Potion ? "Right-click: drink one"
            : usable ? "Right-click: use"
            : "Click: carry  Shift-click: split  Right-click: equip";
        var named = _counts.Named(item);
        var note = Note?.Invoke(item) ?? "";
        if (usable && note.Length == 0)
        {
            HudUsableTooltip.Draw(painter, layout, pointer, named.Name, HudUsableTooltip.Lines(item.CatalogKey), action);
            return;
        }

        HudItemDetailTooltip.Draw(painter, layout, pointer, HudItemDetailTooltip.Lines(named, _state, note, action),
            HudItemDetailTooltip.Gauge(item, _state));
    }

    private void Refresh(OwnerState state)
    {
        _counts.Refresh(state.InventoryRevision, state.Items);
        if (_puzzleRevision != state.InventoryRevision)
        {
            _puzzleRevision = state.InventoryRevision;
            var bag = state.Items.Where(static item => item.InBag).ToList();
            var shown = bag.Any(static item => Puzzles.IsPiece(item.CatalogKey) || item.CatalogKey.Equals(Puzzles.LuckyBagIcon, StringComparison.OrdinalIgnoreCase));
            _puzzle = (shown, shown && Puzzles.Complete(bag.Select(static item => (item.CatalogKey, item.Anchor)), HudInventoryLayout.Columns));
        }

        if (_money != state.Money)
        {
            _money = state.Money;
            _gold = _money.ToString("N0");
        }

        if (_weightValue != (state.CarriedWeight, state.MaxWeight))
        {
            _weightValue = (state.CarriedWeight, state.MaxWeight);
            _weight = $"{state.CarriedWeight}/{state.MaxWeight}";
        }
    }
}
