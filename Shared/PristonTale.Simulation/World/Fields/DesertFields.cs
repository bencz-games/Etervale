namespace PristonTale.Simulation.World.Fields;

public static class Desert1Field
{
    public static readonly FieldDef Definition = new("De-1", "Desert 1", FieldState.Desert, FieldBackdrops.Desert, "Field\\desert\\De-1.smd", [])
    {
        FieldCode = FieldCodes.Desert1,
        Center = new FieldPoint(15005, -1421),
        StartPoints = [new FieldPoint(13306, 2000), new FieldPoint(13083, -2249)],
        Gates =
        [
            new FieldGate("De-2", 13466, -5953, 0),
            new FieldGate("village-1", 20041, -892, 0)
        ],
        BackMusicCode = "BGM_CODE_DESERT",
        ServerCode = "PLAY_SERVER_CODE_MAIN"
    };
}

public static class Desert2Field
{
    public static readonly FieldDef Definition = new("De-2", "Desert 2", FieldState.Desert, FieldBackdrops.Desert, "Field\\desert\\De-2.smd", [])
    {
        FieldCode = FieldCodes.Desert2,
        Center = new FieldPoint(15887, -11161),
        StartPoints = [new FieldPoint(11859, -11257), new FieldPoint(16169, -12768)],
        BackMusicCode = "BGM_CODE_DESERT",
        ServerCode = "PLAY_SERVER_CODE_MAIN"
    };
}

public static class Desert3Field
{
    public static readonly FieldDef Definition = new("De-3", "Desert 3", FieldState.Desert, FieldBackdrops.Desert, "Field\\desert\\De-3.smd", [])
    {
        FieldCode = 11,
        Center = new FieldPoint(34758, -1323),
        StartPoints = [new FieldPoint(34758, -1323), new FieldPoint(29424, 322)],
        Gates = [new FieldGate("De-4", 34372, 4277, 0)],
        WarpGates =
        [
            new FieldWarpGate(29559, 2499, 828, 64, 32, "Slab", -11975, -62457, 209, 0),
            new FieldWarpGate(36128, -2162, 804, 64, 32, "dun-4", -11088, -41570, 343, 0)
        ],
        BackMusicCode = "BGM_CODE_DESERT",
        ServerCode = "PLAY_SERVER_CODE_EXTEND"
    };
}

public static class Desert4Field
{
    public static readonly FieldDef Definition = new(
        "De-4",
        "Desert 4",
        FieldState.Desert,
        FieldBackdrops.Desert,
        "Field\\desert\\De-4.smd",
        ["Field\\desert\\d4-ani01.smd"])
    {
        FieldCode = FieldCodes.Desert4,
        WarpGates = [new FieldWarpGate(33979, 6080, 969, 64, 32, "De-4", 34100, 6214, 940, 2)],
        Center = new FieldPoint(43988, 12053),
        StartPoints = [new FieldPoint(33740, 6491), new FieldPoint(40691, 11056)],
        Gates = [new FieldGate("iron-1", 44545, 13063, 0)],
        BackMusicCode = "BGM_CODE_DESERT",
        ServerCode = "PLAY_SERVER_CODE_EXTEND"
    };
}
