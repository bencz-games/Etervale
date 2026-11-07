using Microsoft.Xna.Framework;
using PristonTale.Gameplay.UI.Inventory;

namespace PristonTale.Gameplay.UI.Npc;

public static class HudCraftLayout
{
    public const int SheltomRows = 4;
    public const int SheltomColumns = 3;
    public const int SheltomSlots = SheltomRows * SheltomColumns;

    public static Point Panel(Point origin) => new(origin.X + 18, origin.Y + 85);
    public static Rectangle ItemBox(Point origin) => new(origin.X + 58, origin.Y + 143, 3 * HudGridPlacement.CellSize, 4 * HudGridPlacement.CellSize);

    public static Rectangle Sheltom(Point origin, int slot) => HudGridPlacement.Cell(SheltomGrid(origin), SheltomColumns, slot);

    public static Rectangle SheltomGrid(Point origin) => new(origin.X + 218, origin.Y + 143, SheltomColumns * HudGridPlacement.CellSize, SheltomRows * HudGridPlacement.CellSize);

    public static int SheltomAt(Point origin, Point pointer) => HudGridPlacement.CellAt(SheltomGrid(origin), SheltomColumns, pointer);

    public static Rectangle Stone(Point origin) => new(origin.X + 159, origin.Y + 218, HudGridPlacement.CellSize, HudGridPlacement.CellSize);
    public static Rectangle Start(Point origin) => new(origin.X + 158, origin.Y + 172, 26, 26);
    public static Point StartInfo(Point origin) => new(origin.X + 131, origin.Y + 145);
    public static Point PricePlate(Point origin) => new(origin.X + 200, origin.Y + 264);
    public static Rectangle PriceText(Point origin) => new(origin.X + 200, origin.Y + 273, 75, 14);

    // One item cell of a window's own boxes, its top-left this far from the window's origin.
    public static Rectangle Cell(Point origin, Point cell) =>
        new(origin.X + cell.X, origin.Y + cell.Y, HudGridPlacement.CellSize, HudGridPlacement.CellSize);

    // The index of the cell under the pointer, or -1.
    public static int CellAt(Point origin, Point[] cells, Point pointer)
    {
        for (var index = 0; index < cells.Length; index++)
        {
            if (Cell(origin, cells[index]).Contains(pointer))
            {
                return index;
            }
        }

        return -1;
    }
}
