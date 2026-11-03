namespace PristonTale.Simulation.World.Fields;

public static class Dungeon1Field
{
    public static readonly FieldDef Definition = new(
        "dun-1",
        "Dungeon 1",
        FieldState.Dungeon,
        FieldBackdrops.Ruin2,
        "Field\\dungeon\\dun-1.smd",
        ["Field\\dungeon\\dun-1-wheel.smd"])
    {
        FieldCode = FieldCodes.Dungeon1,
        TravelLevel = 40,
        Center = new FieldPoint(-15384, -24310),
        WarpGates =
        [
            new FieldWarpGate(-15388, -24073, 100, 64, 32, "ruin-1", 16649, 15238, 501, 0, 0),
            new FieldWarpGate(-15305, -28824, 1, 64, 32, "dun-2", -6027, -26881, 99, 50, 0)
        ],
        Ambients =
        [
            new FieldAmbient(new FieldVector(-15385, 100, -24949), 128, 0)
        ],
        BackMusicCode = "BGM_CODE_DUNGEON",
        ServerCode = "PLAY_SERVER_CODE_EXTEND"
    };
}

public static class Dungeon2Field
{
    public static readonly FieldDef Definition =
        new("dun-2", "Dungeon 2", FieldState.Dungeon, FieldBackdrops.Ruin2, "Field\\dungeon\\dun-2.smd", [])
        {
            FieldCode = 14,
            TravelLevel = 50,
            Center = new FieldPoint(-6108, -26880),
            WarpGates =
            [
                new FieldWarpGate(-5908, -26878, 136, 64, 32, "dun-1", -15314, -28718, 58, 0, 0),
                new FieldWarpGate(-8019, -25768, 21, 64, 32, "dun-3", 1810, -28802, 0, 60, 0),
                new FieldWarpGate(-3918, -27988, 21, 64, 32, "dun-3", 1810, -28802, 0, 60, 0)
            ],
            BackMusicCode = "BGM_CODE_DUNGEON",
            ServerCode = "PLAY_SERVER_CODE_EXTEND"
        };
}

public static class Dungeon3Field
{
    public static readonly FieldDef Definition =
        new("dun-3", "Dungeon 3", FieldState.Dungeon, FieldBackdrops.Ruin2, "Field\\dungeon\\dun-3.smd", [])
        {
            FieldCode = 15,
            TravelLevel = 60,
            Center = new FieldPoint(1827, -28586),
            WarpGates =
            [
                new FieldWarpGate(1810, -28924, 0, 64, 32, "dun-2", -8176, -25775, 77, 0, 0) { MoreExits = [new FieldVector(-3738, 73, -27990)] }
            ],
            BackMusicCode = "BGM_CODE_DUNGEON",
            ServerCode = "PLAY_SERVER_CODE_EXTEND"
        };
}

public static class Dungeon4Field
{
    public static readonly FieldDef Definition = new(
        "dun-4",
        "Dungeon 4",
        FieldState.Dungeon,
        FieldBackdrops.Ruin2,
        "Field\\dungeon\\dun-4.smd",
        [
            "Field\\dungeon\\dun-4-ani-01.smd",
            "Field\\dungeon\\dun-4-ani-02.smd",
            "Field\\dungeon\\dun-4-ani-03.smd",
            "Field\\dungeon\\dun-4-ani-06.smd",
            "Field\\dungeon\\dun-4-ani-07.smd",
            "Field\\dungeon\\dun-4-ani-08.smd",
            "Field\\dungeon\\dun-4-ani-09.smd",
            "Field\\dungeon\\dun-4-ani-14.smd",
            "Field\\dungeon\\dun-4-ani-15.smd",
            "Field\\dungeon\\dun-4-ani-16.smd",
            "Field\\dungeon\\dun-4-ani-17.smd",
            "Field\\dungeon\\dun-4-ani-18.smd",
            "Field\\dungeon\\dun-4-ani-19.smd",
            "Field\\dungeon\\dun-4-ani-20.smd",
            "Field\\dungeon\\dun-4-ani-21.smd",
            "Field\\dungeon\\dun-4-ani-22.smd",
            "Field\\dungeon\\dun-4-ani-23.smd",
            "Field\\dungeon\\dun-4-ani-24.smd",
            "Field\\dungeon\\dun-4-ani-25.smd",
            "Field\\dungeon\\dun-4-ani-26.smd",
            "Field\\dungeon\\dun-4-ani-27.smd",
            "Field\\dungeon\\dun-4-ani-28.smd",
            "Field\\dungeon\\dun-4-ani-29.smd",
            "Field\\dungeon\\dun-4-ani-30.smd",
            "Field\\dungeon\\dun-4-ani-31.smd",
            "Field\\dungeon\\dun-4-ani-32.smd",
            "Field\\dungeon\\dun-4-ani-33.smd",
            "Field\\dungeon\\dun-4-ani-34.smd",
            "Field\\dungeon\\dun-4-ani-35.smd",
            "Field\\dungeon\\dun-4-ani-36.smd",
            "Field\\dungeon\\dun-4-ani-37.smd"
        ])
    {
        FieldCode = 22,
        TravelLevel = 70,
        Center = new FieldPoint(-11108, -41681),
        WarpGates =
        [
            new FieldWarpGate(-10860, -41666, 398, 64, 32, "De-3", 35872, -2016, 804, 0, 0),
            new FieldWarpGate(-12089, -40380, 95, 64, 32, "dun-5", -2527, -37196, 727, 75, 0)
            {
                MoreExits = [new FieldVector(-3669, 727, -36444), new FieldVector(-4795, 727, -37198)]
            }
        ],
        Ambients =
        [
            new FieldAmbient(new FieldVector(-10857, 343, -42634), 10, 24),
            new FieldAmbient(new FieldVector(-11299, 689, -44077), 10, 24),
            new FieldAmbient(new FieldVector(-12995, 689, -43856), 10, 24),
            new FieldAmbient(new FieldVector(-11698, 343, -43240), 10, 24),
            new FieldAmbient(new FieldVector(-13472, 343, -42494), 10, 24),
            new FieldAmbient(new FieldVector(-13447, 343, -40892), 10, 24),
            new FieldAmbient(new FieldVector(-9956, 343, -40637), 10, 24),
            new FieldAmbient(new FieldVector(-9957, 443, -43232), 10, 24),
            new FieldAmbient(new FieldVector(-14113, 443, -42715), 10, 24),
            new FieldAmbient(new FieldVector(-14245, 343, -40555), 10, 24),
            new FieldAmbient(new FieldVector(-12071, 339, -39600), 10, 25),
            new FieldAmbient(new FieldVector(-10236, 443, -44024), 0, 23),
            new FieldAmbient(new FieldVector(-12637, 339, -38716), 0, 23),
            new FieldAmbient(new FieldVector(-12231, 94, -41732), 10, 27)
        ],
        BackMusicCode = "BGM_CODE_DUNGEON",
        ServerCode = "PLAY_SERVER_CODE_EXTEND"
    };
}

public static class Dungeon5Field
{
    public static readonly FieldDef Definition = new(
        "dun-5",
        "Dungeon 5",
        FieldState.Dungeon,
        FieldBackdrops.Ruin2,
        "Field\\dungeon\\dun-5.smd",
        [
            "Field\\dungeon\\dun-5-ani-01.smd",
            "Field\\dungeon\\dun-5-ani-02.smd",
            "Field\\dungeon\\dun-5-ani-03.smd",
            "Field\\dungeon\\dun-5-ani-04.smd",
            "Field\\dungeon\\dun-5-ani-05.smd",
            "Field\\dungeon\\dun-5-ani-06.smd",
            "Field\\dungeon\\dun-5-ani-07.smd",
            "Field\\dungeon\\dun-5-ani-08.smd",
            "Field\\dungeon\\dun-5-ani-09.smd",
            "Field\\dungeon\\dun-5-ani-10.smd",
            "Field\\dungeon\\dun-5-ani-11.smd",
            "Field\\dungeon\\dun-5-ani-12.smd",
            "Field\\dungeon\\dun-5-ani-13.smd",
            "Field\\dungeon\\dun-5-ani-14.smd",
            "Field\\dungeon\\dun-5-ani-15.smd",
            "Field\\dungeon\\dun-5-ani-16.smd",
            "Field\\dungeon\\dun-5-ani-17.smd",
            "Field\\dungeon\\dun-5-ani-18.smd",
            "Field\\dungeon\\dun-5-ani-19.smd",
            "Field\\dungeon\\dun-5-ani-20.smd",
            "Field\\dungeon\\dun-5-ani-21.smd",
            "Field\\dungeon\\dun-5-ani-22.smd"
        ])
    {
        FieldCode = 23,
        TravelLevel = 75,
        Center = new FieldPoint(-3675, -36597),
        WarpGates =
        [
            new FieldWarpGate(-3665, -36334, 762, 64, 32, "dun-4", -12073, -40701, 95, 0, 0),
            new FieldWarpGate(-4895, -37132, 762, 64, 32, "dun-4", -12073, -40701, 95, 0, 0),
            new FieldWarpGate(-2441, -37137, 762, 64, 32, "dun-4", -12073, -40701, 95, 0, 0),
            new FieldWarpGate(-3207, -43830, 553, 128, 32, "dun-6", -3650, -45312, 116, 80, 0) { MoreExits = [new FieldVector(-3668, 3, -50022)] },
            new FieldWarpGate(-4025, -43821, 553, 128, 32, "dun-6", -3650, -45312, 116, 80, 0) { MoreExits = [new FieldVector(-3668, 3, -50022)] }
        ],
        Ambients =
        [
            new FieldAmbient(new FieldVector(-3660, 730, -36915), 40, 23),
            new FieldAmbient(new FieldVector(-2166, 732, -36125), 0, 24),
            new FieldAmbient(new FieldVector(-5150, 732, -36123), 0, 24),
            new FieldAmbient(new FieldVector(-6175, 762, -41850), 0, 24),
            new FieldAmbient(new FieldVector(-1239, 762, -41850), 0, 24),
            new FieldAmbient(new FieldVector(-1251, 794, -38207), 0, 24),
            new FieldAmbient(new FieldVector(-2424, 732, -38229), 0, 7),
            new FieldAmbient(new FieldVector(-3611, 885, -39747), 0, 7),
            new FieldAmbient(new FieldVector(-4798, 732, -38212), 0, 7)
        ],
        BackMusicCode = "BGM_CODE_DUNGEON",
        ServerCode = "PLAY_SERVER_CODE_EXTEND",
        Lighting = new()
        {
            Ambient = FieldColor.Grey(0.15f),
            GlowIntensity = 0.30f,
            GlowScale = 0.85f
        },
    };
}
