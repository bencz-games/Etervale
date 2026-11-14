using Microsoft.Xna.Framework;
using PristonTale.Simulation.Characters;
using PristonTale.Simulation.Items;

namespace PristonTale.Gameplay.UI.Inventory;

// Original inventory slots, bag grid, arrows and text positions in 800x600 design coordinates of the fully open window.
// Origin is the top-left of the bottom-anchored 800x600 frame; panel animation adds its slide offset to Y.
public static class HudInventoryLayout
{
    public const int Columns = Simulation.Items.Inventory.DefaultWidth;
    public const int Rows = Simulation.Items.Inventory.DefaultHeight;

    // Which owned items lie on the bag grid, for HudGridPlacement.
    public static readonly Func<OwnedItemView, bool> InBag = static item => item.InBag;

    public static Point Origin(HudLayout layout, int slideOffset = 0) => new(0, layout.BottomOffset + slideOffset);

    public static Rectangle Bag(Point origin) => new(origin.X + 23, origin.Y + 436, Columns * HudGridPlacement.CellSize, Rows * HudGridPlacement.CellSize);

    public static Rectangle Cell(Point origin, int anchor, int width = 1, int height = 1) =>
        HudGridPlacement.Cell(Bag(origin), Columns, anchor, width, height);

    // One box of the special item box, QuestBoxSpan cells a side, over the bag.
    public const int QuestBoxSpan = 2;

    public static Rectangle QuestBoxCell(Point origin, int box) =>
        Cell(origin, box / QuestItemBox.Columns * QuestBoxSpan * Columns + box % QuestItemBox.Columns * QuestBoxSpan, QuestBoxSpan, QuestBoxSpan);

    // Whether the window shows an item: the special item box, open, takes the bag's place.
    public static bool Shows(OwnedItemView item, bool questBoxShown) => item.InBag ? !questBoxShown : item.QuestBox < 0 || questBoxShown;

    // The owned item under the pointer: in the bag or the special item box (whichever shows), an equipment slot or a belt
    // box.
    public static OwnedItemView ItemAt(Point origin, IReadOnlyList<OwnedItemView> items, Point pointer, bool questBoxShown)
    {
        for (var index = 0; index < items.Count; index++)
        {
            if (Shows(items[index], questBoxShown) && Bounds(origin, items[index]).Contains(pointer))
            {
                return items[index];
            }
        }

        return TwoHandedShadow(items) is { } weapon && Equipment(origin, (int)EquipmentSlot.OffHand).Contains(pointer) ? weapon : null;
    }

    // A worn two-handed weapon also fills the empty off-hand slot, as the original draws it in both hands; the pointer there
    // is on the weapon.
    public static OwnedItemView TwoHandedShadow(IReadOnlyList<OwnedItemView> items)
    {
        OwnedItemView weapon = null;
        for (var index = 0; index < items.Count; index++)
        {
            if (items[index].Slot == (int)EquipmentSlot.OffHand)
            {
                return null;
            }

            if (items[index].Slot == (int)EquipmentSlot.Weapon)
            {
                weapon = items[index];
            }
        }

        return weapon is not null && ItemEquipmentMetadata.IsTwoHanded(weapon.CatalogKey) ? weapon : null;
    }

    public static Rectangle Equipment(Point origin, int slot)
    {
        var bounds = (EquipmentSlot)slot switch
        {
            EquipmentSlot.Weapon => Edges(574 + 1, 433 + 1, 640, 544),
            EquipmentSlot.OffHand => Edges(722 + 1, 433 + 1, 788, 544),
            EquipmentSlot.Armor => Edges(648 + 1, 433 + 1, 714, 544),
            EquipmentSlot.Amulet => Edges(304 + 1, 438 + 1, 326, 460),
            EquipmentSlot.RingRight => Edges(356 + 1, 438 + 1, 378, 460),
            EquipmentSlot.RingLeft => Edges(330 + 1, 438 + 1, 352, 460),
            EquipmentSlot.Sheltom => Edges(382 + 1, 438 + 1, 404, 460),
            EquipmentSlot.Bracelet => Edges(411 + 1, 438 + 1, 455, 482),
            EquipmentSlot.Gloves => Edges(462 + 1, 438 + 1, 506, 482),
            EquipmentSlot.Boots => Edges(513 + 1, 438 + 1, 557, 482),
            _ => Rectangle.Empty
        };
        return bounds == Rectangle.Empty ? bounds : new Rectangle(bounds.X + origin.X, bounds.Y + origin.Y, bounds.Width, bounds.Height);
    }

    // The potion belt's boxes on the bottom bar, 26 pixels apart; the potion sits centred in its box.
    public static Rectangle Belt(Point origin, int box) => new(origin.X + 495 + 26 * box, origin.Y + 565, 26, 26);

    // Where an owned item is drawn and hit: its bag cells, its equipment slot, or centred in its belt box.
    public static Rectangle Bounds(Point origin, Simulation.Characters.OwnedItemView item)
    {
        if (item.BeltBox >= 0)
        {
            return HudGridPlacement.Frame(item, Belt(origin, item.BeltBox).Center);
        }

        if (item.QuestBox >= 0)
        {
            return HudGridPlacement.Frame(item, QuestBoxCell(origin, item.QuestBox).Center);
        }

        return item.Equipped ? Equipment(origin, item.Slot) : Cell(origin, item.Anchor, item.Width, item.Height);
    }

    // CoverLarge 66x110, CoverMiddle 44x44 and CoverSmall 22x22 are drawn from the slot's top-left behind equipped items.
    public static string Cover(int slot) => (EquipmentSlot)slot switch
    {
        EquipmentSlot.Weapon or EquipmentSlot.OffHand or EquipmentSlot.Armor => Rendering.HudArt.CoverLarge,
        EquipmentSlot.Bracelet or EquipmentSlot.Gloves or EquipmentSlot.Boots => Rendering.HudArt.CoverMiddle,
        _ => Rendering.HudArt.CoverSmall
    };

    // The side buttons: the bag page (A left, B right) under the bag and the weapon pair (A left, B right) by the
    // equipment. The shipped Inven.bmp puts the pair buttons 5 pixels right of and 11 below the original's table, after
    // a third button of its own.
    public static Rectangle PageButton(Point origin, int page) => new(origin.X + 26 + 22 * page, origin.Y + 572, 22, 19);
    public static Rectangle PairButton(Point origin, int pair) => new(origin.X + 524 + 22 * pair, origin.Y + 525, 19, 19);

    // Original Inven.bmp has a third small button immediately before the A/B weapon-pair buttons.
    // This opens the optional costume view, not a new EquipmentSlot.Armor.
    public static Rectangle CostumeButton(Point origin) => new(origin.X + 502, origin.Y + 525, 19, 19);
    public static Rectangle CostumeArea(Point origin) => new(origin.X + 572, origin.Y + 429, 216, 119);
    public static Rectangle CostumeSlot(Point origin) => new(origin.X + 648, origin.Y + 434, 66, 110);

    public static Rectangle Close(Point origin) => new(origin.X + 266, origin.Y + 572, 22, 19);

    // The special item box's button, left of the shop button.
    public static Rectangle QuestBoxButton(Point origin) => new(origin.X + 70, origin.Y + 572, 22, 19);

    // The personal shop button, right of the special item box's.
    public static Rectangle ShopButton(Point origin) => new(origin.X + 98, origin.Y + 572, 22, 19);

    // The coin left of the gold: it drops gold, or stores it while the warehouse is open.
    public static Rectangle GoldButton(Point origin) => new(origin.X + 120, origin.Y + 572, 20, 19);

    // How far above its button a button's label is drawn.
    public const int HintRise = 27;
    public static Rectangle Money(Point origin) => new(origin.X + 147, origin.Y + 574, 110, 18);
    public static Rectangle Weight(Point origin) => new(origin.X + 326, origin.Y + 471, 80, 15);
    public static Rectangle Window(Point origin) => new(origin.X, origin.Y + 400, HudLayout.DesignWidth, 200);

    private static Rectangle Edges(int left, int top, int right, int bottom) => new(left, top, right - left, bottom - top);
}
