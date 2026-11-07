namespace PristonTale.Simulation.World.Fields;

public static class Ruin4Field
{
    public static readonly FieldDef Definition = new("ruin-4", "Ruin 4", FieldState.Ruin, FieldBackdrops.Ruin1, "Field\\Ruin\\ruin-4.smd", [])
    {
        FieldCode = 4,
        Center = new FieldPoint(1384, -918),
        StartPoints = [new FieldPoint(2578, -5124), new FieldPoint(1873, 3060)],
        Gates = [new FieldGate("ruin-3", 410, 4902, 0)],
        BackMusicCode = "BGM_CODE_DESERT",
        ServerCode = "PLAY_SERVER_CODE_MAIN"
    };
}

public static class Ruin3Field
{
    public static readonly FieldDef Definition = new("ruin-3", "Ruin 3", FieldState.Ruin, FieldBackdrops.Ruin1, "Field\\Ruin\\ruin-3.smd", [])
    {
        FieldCode = 5,
        Center = new FieldPoint(4953, 10922),
        StartPoints = [new FieldPoint(875, 7910), new FieldPoint(1576, 14307)],
        Gates = [new FieldGate("ruin-2", 3051, 16124, 0)],
        BackMusicCode = "BGM_CODE_DESERT",
        ServerCode = "PLAY_SERVER_CODE_MAIN"
    };
}

public static class Ruin2Field
{
    public static readonly FieldDef Definition = new("ruin-2", "Ruin 2", FieldState.Ruin, FieldBackdrops.Ruin2, "Field\\Ruin\\ruin-2.smd", ["Field\\Ruin\\ruin_ani01.smd"])
    {
        FieldCode = 6,
        Center = new FieldPoint(7459, 23984),
        StartPoints = [new FieldPoint(3976, 19645), new FieldPoint(5832, 23751), new FieldPoint(5615, 25117)],
        Gates =
        [
            new FieldGate("ruin-1", 10019, 18031, 0),
            new FieldGate("forever-fall-04", 4470, 27774, 0),
            new FieldGate("Greedy", 12713, 23409, 0)
        ],
        WarpGates = [new FieldWarpGate(4428, 22511, 845, 64, 32, "ruin-2", 4547, 22616, 817, 2)],
        BackMusicCode = "BGM_CODE_DESERT",
        ServerCode = "PLAY_SERVER_CODE_MAIN"
    };
}

public static class Ruin1Field
{
    public static readonly FieldDef Definition = new(
        "ruin-1",
        "Ruin 1",
        FieldState.Ruin,
        FieldBackdrops.Ruin2,
        "Field\\Ruin\\ruin-1.smd",
        [])
    {
        FieldCode = FieldCodes.Ruin1,
        Center = new FieldPoint(16362, 14959),
        StartPoints = [new FieldPoint(12242, 16034), new FieldPoint(12194, 8969)],
        Gates = [new FieldGate("De-1", 13319, 7102, 0)],
        WarpGates = [new FieldWarpGate(16809, 15407, 501, 128, 32, "dun-1", -15395, -24185, 0, 0)],
        BackMusicCode = "BGM_CODE_DESERT",
        ServerCode = "PLAY_SERVER_CODE_MAIN"
    };
}
