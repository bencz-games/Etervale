using PristonTale.Simulation.World;
namespace PristonTale.Simulation.World.Fields;

public static class BossField
{
    public static readonly FieldDef Definition = new(
        "Boss", "Boss", FieldState.Dungeon, FieldBackdrops.Ruin2,
        "Field\\Boss\\Boss.smd", [])
    {
        FieldCode = FieldCodes.Boss,
        Ambience = FieldAmbience.DarkSanctuary,
        Center = new FieldPoint(33000, 50036),
        WarpGates =
        [
            new FieldWarpGate(33012, 49926, 1533, 64, 32, "ice2", 37971, 50460, 1209, 0),
            new FieldWarpGate(33015, 53075, 1335, 64, 32, "HeartOfFire", 27174, -67848, 1408, 0)
            {
                MoreExits = [new FieldVector(22430, 1537, -74461), new FieldVector(30380, 1512, -78001)]
            }
        ],
        BackMusicCode = "BGM_CODE_DUNGEON",
        ServerCode = "PLAY_SERVER_CODE_EXTEND"
    };
}

// Below Kelvezu's Lair.
public static class HeartOfFireField
{
    public static readonly FieldDef Definition = new(
        "HeartOfFire", "Heart of Fire", FieldState.Dungeon, FieldBackdrops.Rain,
        "Field\\HeartOfFire\\HeartOfFire.smd", [])
    {
        FieldCode = 51,
        Ambience = FieldAmbience.DarkSanctuary,
        Center = new FieldPoint(28700, -74000),
        WarpGates =
        [
            new FieldWarpGate(27894, -75602, 1695, 64, 32, "Boss", 32953, 50125, 1500, 0)
        ],
        BackMusicCode = "BGM_CODE_DUNGEON",
        ServerCode = "PLAY_SERVER_CODE_EXTEND"
    };
}
