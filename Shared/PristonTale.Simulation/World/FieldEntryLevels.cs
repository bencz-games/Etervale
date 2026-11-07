namespace PristonTale.Simulation.World;

// The level each field opens at, by field code: the walking check, the warp gates (WorldCatalog gives a gate its
// destination's level), the NPC and wing teleports and the teleport core read this one table. A field it does not name is
// open. Up to Ancient Weapons the levels are the original client's; the later fields' follow their monsters' levels,
// stretched to the level cap of 180. Office's Closed refuses every entry a level is checked for, but a character placed
// there by a game master stays.
public static class FieldEntryLevels
{
    public const int Closed = 1000;

    private static readonly int[] Levels =
    [
        0, 0, 0, 0, 0, 0, 0, 0, 0, 0,                   // 0-9
        0, 55, 55, 40, 50, 60, Closed, 0, 0, 0,         // 10-19
        0, 0, 70, 75, 55, 55, 65, 80, 85, 90,           // 20-29
        0, 90, 0, 0, 70, 90, 90, 95, 100, 0,            // 30-39
        100, 100, 80, 102, 105, 0, 108, 0, 110, 115,    // 40-49
        124, 129, 133, 143                              // 50-53
    ];

    public static int Of(int fieldCode) => fieldCode >= 0 && fieldCode < Levels.Length ? Levels[fieldCode] : 0;
}
