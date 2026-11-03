namespace PristonTale.Simulation.World.Fields;

public static class Mine1Field
{
    public static readonly FieldDef Definition = new(
        "mine-1", "Mine 1", FieldState.Dungeon, FieldBackdrops.Ruin2,
        "Field\\Mine\\mine-1.smd", [])
    {
        FieldCode = 44,
        TravelLevel = 105,
        Center = new FieldPoint(18006, -34740),
        WarpGates =
        [
            new FieldWarpGate(17944, -34641, 246, 64, 32, "ice1", 35352, 31928, 829, 105, 0)
        ],
        BackMusicCode = "BGM_CODE_DUNGEON",
        ServerCode = "PLAY_SERVER_CODE_EXTEND"
    };
}
