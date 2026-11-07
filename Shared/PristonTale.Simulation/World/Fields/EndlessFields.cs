namespace PristonTale.Simulation.World.Fields;

public static class Dungeon7Field
{
    public static readonly FieldDef Definition = new(
        "dun-7", "Dungeon 7", FieldState.Dungeon, FieldBackdrops.Ruin2,
        "Field\\endless\\dun-7.smd", [])
    {
        FieldCode = FieldCodes.EndlessTower1,
        Ambience = FieldAmbience.Dungeon,
        Center = new FieldPoint(14255, -39099),
        WarpGates =
        [
            new FieldWarpGate(14242, -40988, 259, 64, 32, "dun-8", 5255, -37897, 86, 0),
            new FieldWarpGate(14246, -38912, 220, 64, 32, "Losttemple", -12272, 11299, 509, 0)
        ],
        BackMusicCode = "BGM_CODE_DUNGEON",
        ServerCode = "PLAY_SERVER_CODE_EXTEND"
    };
}

public static class Dungeon8Field
{
    public static readonly FieldDef Definition = new(
        "dun-8", "Dungeon 8", FieldState.Dungeon, FieldBackdrops.Ruin2,
        "Field\\endless\\dun-8.smd", [])
    {
        FieldCode = 41,
        Ambience = FieldAmbience.Dungeon,
        Center = new FieldPoint(5255, -37897),
        WarpGates =
        [
            new FieldWarpGate(5253, -37708, 69, 64, 32, "dun-7", 14242, -41199, 220, 0),
            new FieldWarpGate(5254, -41500, 139, 64, 32, "dun-9", 4896, -42220, 202, 0) { MoreExits = [new FieldVector(6408, 202, -42220)] }
        ],
        BackMusicCode = "BGM_CODE_DUNGEON",
        ServerCode = "PLAY_SERVER_CODE_EXTEND"
    };
}

public static class Dungeon9Field
{
    public static readonly FieldDef Definition = new(
        "dun-9", "Dungeon 9", FieldState.Dungeon, FieldBackdrops.Ruin2,
        "Field\\endless\\dun-9.smd", [])
    {
        FieldCode = 43,
        Ambience = FieldAmbience.Dungeon,
        Center = new FieldPoint(4896, -42330),
        StartPoints = [new FieldPoint(4896, -42330), new FieldPoint(6418, -42330)],
        WarpGates =
        [
            new FieldWarpGate(4896, -42090, 230, 64, 32, "dun-8", 5254, -41361, 139, 0),
            new FieldWarpGate(6408, -42090, 230, 64, 32, "dun-8", 5254, -41361, 139, 0)
        ],
        BackMusicCode = "BGM_CODE_DUNGEON",
        ServerCode = "PLAY_SERVER_CODE_EXTEND"
    };
}

public static class Dungeon6Field
{
    public static readonly FieldDef Definition = new(
        "dun-6", "Dungeon 6", FieldState.Dungeon, FieldBackdrops.Ruin2,
        "Field\\dungeon\\Dun-6a.smd",
        [
            "Field\\dungeon\\Dun-6a-ani-001.smd",
            "Field\\dungeon\\Dun-6a-ani-002.smd",
            "Field\\dungeon\\Dun-6a-ani-003.smd",
            "Field\\dungeon\\Dun-6a-ani-004.smd",
            "Field\\dungeon\\Dun-6a-ani-005.smd",
            "Field\\dungeon\\Dun-6a-ani-006.smd",
            "Field\\dungeon\\Dun-6a-ani-007.smd",
            "Field\\dungeon\\Dun-6a-ani-008.smd",
            "Field\\dungeon\\Dun-6a-ani-009.smd",
            "Field\\dungeon\\Dun-6a-ani-010.smd",
            "Field\\dungeon\\Dun-6a-ani-011.smd",
            "Field\\dungeon\\Dun-6a-ani-012.smd",
            "Field\\dungeon\\Dun-6a-ani-013.smd",
            "Field\\dungeon\\Dun-6a-ani-014.smd",
            "Field\\dungeon\\Dun-6a-ani-015.smd",
            "Field\\dungeon\\Dun-6a-ani-016.smd",
            "Field\\dungeon\\Dun-6a-ani-017.smd",
            "Field\\dungeon\\Dun-6a-ani-018.smd",
            "Field\\dungeon\\Dun-6a-ani-019.smd"
        ])
    {
        FieldCode = 42,
        Ambience = FieldAmbience.Dungeon,
        Center = new FieldPoint(-3660, -45200),
        WarpGates =
        [
            new FieldWarpGate(-3655, -50603, 3, 64, 32, "dun-5", -2851, -43792, 642, 0) { MoreExits = [new FieldVector(-4422, 642, -43801)] }
        ],
        Ambients =
        [
            new FieldAmbient(new FieldVector(-3648, 3, -50013), 0, 7),
            new FieldAmbient(new FieldVector(-4349, 454, -47777), 0, 7),
            new FieldAmbient(new FieldVector(-5130, 454, -47587), 0, 7),
            new FieldAmbient(new FieldVector(-2183, 172, -47864), 0, 7),
            new FieldAmbient(new FieldVector(-1785, 167, -48077), 0, 7),
            new FieldAmbient(new FieldVector(-3725, 116, -46758), 0, 7),
            new FieldAmbient(new FieldVector(-3632, 3, -47999), 0, 7),
            new FieldAmbient(new FieldVector(-627, 229, -45987), 0, 7),
            new FieldAmbient(new FieldVector(-1896, 116, -45010), 0, 7),
            new FieldAmbient(new FieldVector(-5077, 116, -45555), 0, 7),
            new FieldAmbient(new FieldVector(-5289, 116, -46422), 0, 7)
        ],
        BackMusicCode = "BGM_CODE_DUNGEON",
        ServerCode = "PLAY_SERVER_CODE_EXTEND"
    };
}
