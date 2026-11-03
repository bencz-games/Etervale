using Microsoft.Xna.Framework;
using PristonTale.Gameplay.UI.Inventory;
using PristonTale.Simulation.Items;

namespace PristonTale.Gameplay.UI.Npc;

// The original shop and warehouse window in 800x600 design coordinates: a 344x264 frame at (0, 40) that slides in from
// the left, the warehouse's 9x9 grid of 22-pixel cells (the shop lays its stock on it too), the parchment with tabs and
// arrows, two gold bars and four round buttons.
public static class HudStoreLayout
{
    public const int Columns = StoreGrid.Columns;
    public const int Rows = StoreGrid.Rows;
    public const int SlideDistance = 384;

    // HudSlide offsets run 0..256 while the window travels its full width.
    public static Point Origin(int slideOffset) => new(-slideOffset * SlideDistance / 256, 0);

    public static Rectangle Window(Point origin) => new(origin.X, origin.Y + 40, 344, 264);

    public static Rectangle Grid(Point origin) => new(origin.X + 21, origin.Y + 86, Columns * HudGridPlacement.CellSize, Rows * HudGridPlacement.CellSize);

    public static Rectangle Cell(Point origin, int anchor, int width = 1, int height = 1) =>
        HudGridPlacement.Cell(Grid(origin), Columns, anchor, width, height);

    // A store page's occupied cells (ItemGrid rows), Columns wide and rows high: the store's, or the trade's shorter one.
    public static uint[] NewGrid(int rows = Rows) => new uint[rows];

    // The first free place for an item, down each column from the left as the shops lay items; -1 when none.
    public static int FindSpace(uint[] occupied, int width, int height) =>
        ItemGrid.FirstFit(occupied, Columns, Columns * occupied.Length, width, height);

    public static void Occupy(uint[] occupied, int anchor, int width, int height) => ItemGrid.Occupy(occupied, Columns, anchor, width, height);

    public static Point Title(Point origin) => new(origin.X + 225, origin.Y + 47);
    // A personal shop's picture, where the NPC shop shows its tabs.
    public static Point Picture(Point origin) => new(origin.X + 238, origin.Y + 90);
    public static Point WarehouseTitle(Point origin) => new(origin.X + 243, origin.Y + 100);
    public static Rectangle TabWeapons(Point origin) => new(origin.X + 239, origin.Y + 87, 41, 45);
    public static Rectangle TabArmor(Point origin) => new(origin.X + 280, origin.Y + 87, 41, 45);
    public static Point TabGoods(Point origin) => new(origin.X + 232, origin.Y + 93);
    public static Rectangle LeftArrow(Point origin) => new(origin.X + 260, origin.Y + 132, 20, 20);
    public static Rectangle RightArrow(Point origin) => new(origin.X + 282, origin.Y + 132, 20, 20);
    public static Rectangle RepairAll(Point origin) => new(origin.X + 269, origin.Y + 228, 28, 28);
    public static Rectangle Repair(Point origin) => new(origin.X + 238, origin.Y + 228, 28, 28);
    public static Rectangle Sell(Point origin) => new(origin.X + 238, origin.Y + 259, 28, 28);
    public static Rectangle Buy(Point origin) => new(origin.X + 269, origin.Y + 259, 28, 28);
    public static Rectangle Exit(Point origin) => new(origin.X + 304, origin.Y + 264, 20, 20);

    // Upper bar: the price under the pointer (shop) or the stored gold (warehouse). Lower bar: carried gold or weight.
    public static Rectangle UpperBar(Point origin) => new(origin.X + 231, origin.Y + 166, 99, 20);
    public static Rectangle LowerBar(Point origin) => new(origin.X + 231, origin.Y + 196, 99, 20);
    public static Point GoldPlate(Point origin) => new(origin.X + 231, origin.Y + 193);

    // The coin at the left of the warehouse's upper bar: a click asks how much stored gold to take out.
    public static Rectangle WarehouseGold(Point origin) => new(origin.X + 231, origin.Y + 163, 20, 20);
    public static Point WeightIcon(Point origin) => new(origin.X + 231, origin.Y + 191);
}
