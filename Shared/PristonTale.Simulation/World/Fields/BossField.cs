using PristonTale.Simulation.World;
namespace PristonTale.Simulation.World.Fields;

public static class BossField
{
    public static readonly FieldDef Definition = new(
        "Boss", "Boss", FieldState.Dungeon, FieldBackdrops.Ruin2,
        "Field\\Boss\\Boss.smd", [])
    {
        FieldCode = FieldCodes.Boss,
        TravelLevel = 90,
        Center = new FieldPoint(33000, 50036),
        WarpGates =
        [
            new FieldWarpGate(33012, 49926, 1533, 64, 32, "ice2", 37971, 50460, 1209, 0, 0)
        ],
        BackMusicCode = "BGM_CODE_DUNGEON",
        ServerCode = "PLAY_SERVER_CODE_EXTEND"
    };
}
