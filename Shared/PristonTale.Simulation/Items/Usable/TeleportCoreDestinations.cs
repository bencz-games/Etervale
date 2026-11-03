namespace PristonTale.Simulation.Items.Usable;

public sealed record TeleportCoreDestination(int Index, int FieldCode, int RequiredLevel, string Name);

// The fields a Teleport Core reaches, shown ten per page, each with the level a character needs.
public static class TeleportCoreDestinations
{
    public const int PageSize = 10;

    public static IReadOnlyList<TeleportCoreDestination> All { get; } =
    [
        new(0, 20, 0, "Path of the Wind"),
        new(1, 19, 0, "Valley of Tranquility"),
        new(2, 17, 0, "Forest of Spirits"),
        new(3, 0, 0, "Acacia Forest"),
        new(4, 2, 0, "Garden of Freedom"),
        new(5, 4, 0, "Refuge of the Ancients"),
        new(6, 5, 0, "Castle of the Lost"),
        new(7, 7, 0, "Cursed Land"),
        new(8, 8, 0, "Forgotten Land"),
        new(9, 10, 0, "Oasis"),
        new(10, 11, 55, "Ancient Battlefield"),
        new(11, 12, 55, "Forbidden Land"),
        new(12, 25, 55, "Beehive Cave"),
        new(13, 24, 55, "Mushroom Cave"),
        new(14, 26, 65, "Dark Sanctuary"),
        new(15, 13, 40, "Ancient Prison 1F"),
        new(16, 14, 50, "Ancient Prison 2F"),
        new(17, 15, 60, "Ancient Prison 3F"),
        new(18, 22, 70, "Cursed Temple 1F"),
        new(19, 23, 75, "Cursed Temple 2F"),
        new(20, 42, 80, "Cursed Temple 3F"),
        new(21, 34, 70, "Lake of Greed"),
        new(22, 27, 80, "Railway of Chaos"),
        new(23, 28, 85, "Heart of Perum"),
        new(24, 29, 90, "Eura Village"),
        new(25, 31, 90, "Gallubia Valley"),
        new(26, 35, 90, "Frozen Sanctuary"),
        new(27, 36, 90, "Kelvezu's Lair"),
        new(28, 37, 95, "Lost Island"),
        new(29, 38, 100, "Lost Temple"),
        new(30, 40, 100, "Endless Tower 1F"),
        new(31, 41, 100, "Endless Tower 2F"),
        new(32, 43, 102, "Endless Tower 3F"),
        new(33, 44, 105, "Ice Mine 1F"),
        new(34, 46, 108, "Secret Laboratory"),
        new(35, 48, 110, "Ancient Weapons"),
        new(36, 49, 112, "Sea of Abyss")
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
