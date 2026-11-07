using PristonTale.Simulation.World;

namespace PristonTale.Simulation.Items.Usable;

public sealed record TeleportCoreDestination(int Index, int FieldCode, string Name)
{
    public int RequiredLevel => FieldEntryLevels.Of(FieldCode);
}

// The fields a Teleport Core reaches, shown ten per page, each at its field's entry level.
public static class TeleportCoreDestinations
{
    public const int PageSize = 10;

    public static IReadOnlyList<TeleportCoreDestination> All { get; } =
    [
        new(0, 20, "Path of the Wind"),
        new(1, 19, "Valley of Tranquility"),
        new(2, 17, "Forest of Spirits"),
        new(3, 0, "Acacia Forest"),
        new(4, 2, "Garden of Freedom"),
        new(5, 4, "Refuge of the Ancients"),
        new(6, 5, "Castle of the Lost"),
        new(7, 7, "Cursed Land"),
        new(8, 8, "Forgotten Land"),
        new(9, 10, "Oasis"),
        new(10, 11, "Ancient Battlefield"),
        new(11, 12, "Forbidden Land"),
        new(12, 25, "Beehive Cave"),
        new(13, 24, "Mushroom Cave"),
        new(14, 26, "Dark Sanctuary"),
        new(15, 13, "Ancient Prison 1F"),
        new(16, 14, "Ancient Prison 2F"),
        new(17, 15, "Ancient Prison 3F"),
        new(18, 22, "Cursed Temple 1F"),
        new(19, 23, "Cursed Temple 2F"),
        new(20, 42, "Cursed Temple 3F"),
        new(21, 34, "Lake of Greed"),
        new(22, 27, "Railway of Chaos"),
        new(23, 28, "Heart of Perum"),
        new(24, 29, "Eura Village"),
        new(25, 31, "Gallubia Valley"),
        new(26, 35, "Frozen Sanctuary"),
        new(27, 36, "Kelvezu's Lair"),
        new(28, 37, "Lost Island"),
        new(29, 38, "Lost Temple"),
        new(30, 40, "Endless Tower 1F"),
        new(31, 41, "Endless Tower 2F"),
        new(32, 43, "Endless Tower 3F"),
        new(33, 44, "Ice Mine 1F"),
        new(34, 46, "Secret Laboratory"),
        new(35, 48, "Ancient Weapons"),
        new(36, 49, "Sea of Abyss")
    ];

    public static int PageCount => (All.Count + PageSize - 1) / PageSize;

    public static bool TryGet(int index, out TeleportCoreDestination destination)
    {
        if (index >= 0 && index < All.Count)
        {
            destination = All[index];
            return true;
        }

        destination = null;
        return false;
    }
}
