using Microsoft.Xna.Framework;
using PristonTale.Simulation.Characters;

namespace PristonTale.Gameplay.UI.Inventory;

// The original's item grids of 22-pixel cells (the bag, the warehouse, the workbench slots) and where a carried item
// lands on one: the item is drawn centred on the pointer and lands with its top-left rounded to the nearest cell. Free
// cells take it; over one item it is exchanged with that item (a potion over its own kind joins it), over two it is
// refused.
public static class HudGridPlacement
{
    public const int CellSize = Engine3D.IO.ItemIconFiles.CellPixels;
    public const int HalfCell = CellSize / 2;

    public static Rectangle Frame(OwnedItemView item, Point center) => Frame(item.Width, item.Height, center);

    // An item of this many cells centred on a point (the pointer, a box's centre).
    public static Rectangle Frame(int width, int height, Point center) =>
        new(center.X - width * CellSize / 2, center.Y - height * CellSize / 2, width * CellSize, height * CellSize);

    public static Rectangle Cell(Rectangle grid, int columns, int anchor, int width = 1, int height = 1) =>
        new(grid.X + anchor % columns * CellSize, grid.Y + anchor / columns * CellSize, width * CellSize, height * CellSize);

    public static int CellAt(Rectangle grid, int columns, Point pointer) =>
        grid.Contains(pointer) ? (pointer.Y - grid.Y) / CellSize * columns + (pointer.X - grid.X) / CellSize : -1;

    // The item whose cells contain the pointer, of those `lies` puts on this grid.
    public static OwnedItemView ItemAt(Rectangle grid, int columns, IReadOnlyList<OwnedItemView> items, Func<OwnedItemView, bool> lies, Point pointer)
    {
        for (var index = 0; index < items.Count; index++)
        {
            var item = items[index];
            if (lies(item) && Cell(grid, columns, item.Anchor, item.Width, item.Height).Contains(pointer))
            {
                return item;
            }
        }

        return null;
    }

    // lies: which of the items lie on this grid, by anchor. Null when the rounded item would not lie wholly on the grid.
    public static HudPlacement? Of(Rectangle grid, int columns, int rows, IReadOnlyList<OwnedItemView> items, Func<OwnedItemView, bool> lies,
        OwnedItemView carried, Point pointer)
    {
        var frame = Frame(carried, pointer);
        var column = HudLayout.FloorDivide(frame.X + HalfCell - grid.X, CellSize);
        var row = HudLayout.FloorDivide(frame.Y + HalfCell - grid.Y, CellSize);
        if (column < 0 || row < 0 || column + carried.Width > columns || row + carried.Height > rows)
        {
            return null;
        }

        var anchor = row * columns + column;
        var target = Cell(grid, columns, anchor, carried.Width, carried.Height);
        OwnedItemView covered = null;
        for (var index = 0; index < items.Count; index++)
        {
            var item = items[index];
            if (!lies(item) || item.InstanceId == carried.InstanceId || !target.Intersects(Cell(grid, columns, item.Anchor, item.Width, item.Height)))
            {
                continue;
            }

            if (covered is not null)
            {
                return new HudPlacement(HudPlacementKind.Blocked, target, anchor);
            }

            covered = item;
        }

        if (covered is null)
        {
            return new HudPlacement(HudPlacementKind.Fits, target, anchor);
        }

        return new HudPlacement(covered.Joins(carried) ? HudPlacementKind.Merge : HudPlacementKind.Exchange, Cell(grid, columns, covered.Anchor, covered.Width, covered.Height),
            anchor, Covered: covered);
    }

}
