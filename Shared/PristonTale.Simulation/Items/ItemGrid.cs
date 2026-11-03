namespace PristonTale.Simulation.Items;

// A grid of item cells held as one bit per cell in each row's word (at most 32 columns), whatever holds the items: the
// bag, a store page, a trade offer. Items fill it down each column from the left, as the original's client fills the
// bag: items received one after another stack under each other before taking the next column.
public static class ItemGrid
{
    // Whether a width x height item fits with its top-left cell at anchor: inside the columns, the rows and the capacity
    // (a last row may be partial), on cells no other item holds.
    public static bool Fits(ReadOnlySpan<uint> rows, int columns, int capacity, int anchor, int width, int height)
    {
        if (anchor < 0 || anchor >= capacity || width < 1 || height < 1)
        {
            return false;
        }

        var x = anchor % columns;
        var y = anchor / columns;
        if (width > columns - x || height > rows.Length - y || (y + height - 1) * columns + x + width > capacity)
        {
            return false;
        }

        var mask = ((1u << width) - 1u) << x;
        for (var row = y; row < y + height; row++)
        {
            if ((rows[row] & mask) != 0)
            {
                return false;
            }
        }

        return true;
    }

    // The first anchor the item fits at, down each column from the left; -1 when none.
    public static int FirstFit(ReadOnlySpan<uint> rows, int columns, int capacity, int width, int height)
    {
        for (var x = 0; x < columns; x++)
        {
            for (var y = 0; y < rows.Length; y++)
            {
                if (Fits(rows, columns, capacity, y * columns + x, width, height))
                {
                    return y * columns + x;
                }
            }
        }

        return -1;
    }

    public static void Occupy(Span<uint> rows, int columns, int anchor, int width, int height)
    {
        var x = anchor % columns;
        var y = anchor / columns;
        var mask = ((1u << width) - 1u) << x;
        for (var row = y; row < y + height && row < rows.Length; row++)
        {
            rows[row] |= mask;
        }
    }
}
