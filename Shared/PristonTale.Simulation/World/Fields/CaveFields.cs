namespace PristonTale.Simulation.World.Fields;

public static class TcaveField
{
    public static readonly FieldDef Definition = new(
        "Tcave", "Tcave", FieldState.Dungeon, FieldBackdrops.Ruin2,
        "Field\\cave\\Tcave.smd",
        [
            "Field\\cave\\Tcave_ani01.smd",
            "Field\\cave\\Tcave_ani02.smd",
            "Field\\cave\\Tcave_ani03.smd",
            "Field\\cave\\Tcave_ani04.smd",
            "Field\\cave\\Tcave_ani05.smd",
            "Field\\cave\\Tcave_ani06.smd",
            "Field\\cave\\Tcave_ani07.smd",
            "Field\\cave\\Tcave_ani08.smd",
            "Field\\cave\\Tcave_ani09.smd",
            "Field\\cave\\Tcave_ani10.smd",
            "Field\\cave\\Tcave_ani11.smd",
            "Field\\cave\\Tcave_ani12.smd",
            "Field\\cave\\Tcave_ani13.smd",
            "Field\\cave\\Tcave_ani14.smd",
            "Field\\cave\\Tcave_ani15.smd",
            "Field\\cave\\Tcave_ani16.smd"
        ])
    {
        FieldCode = 24,
        TravelLevel = 55,
        Center = new FieldPoint(120126, 26064),
        StartPoints = [new FieldPoint(119319, 26034), new FieldPoint(125606, 24541)],
        WarpGates =
        [
            new FieldWarpGate(125581, 25086, 480, 80, 32, "Dcave", 158627, 20504, 240, 65, 0),
            new FieldWarpGate(118869, 26017, 538, 80, 32, "fore-3", -16490, -6930, 298, 0, 0)
        ],
        BackMusicCode = "BGM_CODE_DUNGEON",
        ServerCode = "PLAY_SERVER_CODE_MAIN"
    };
}

public static class McaveField
{
    public static readonly FieldDef Definition = new(
        "Mcave", "Mcave", FieldState.Dungeon, FieldBackdrops.Ruin2,
        "Field\\cave\\Mcave.smd",
        [
            "Field\\cave\\Mcave_ani01.smd",
            "Field\\cave\\Mcave_ani02.smd",
            "Field\\cave\\Mcave_ani03.smd",
            "Field\\cave\\Mcave_ani04.smd",
            "Field\\cave\\Mcave_ani05.smd",
            "Field\\cave\\Mcave_ani06.smd",
            "Field\\cave\\Mcave_ani07.smd",
            "Field\\cave\\Mcave_ani08.smd",
            "Field\\cave\\Mcave_ani09.smd",
            "Field\\cave\\Mcave_ani10.smd",
            "Field\\cave\\Mcave_ani11.smd",
            "Field\\cave\\Mcave_ani12.smd",
            "Field\\cave\\Mcave_ani13.smd",
            "Field\\cave\\Mcave_ani14.smd",
            "Field\\cave\\Mcave_ani15.smd",
            "Field\\cave\\Mcave_ani16.smd",
            "Field\\cave\\Mcave_ani17.smd"
        ])
    {
        FieldCode = 25,
        TravelLevel = 55,
        Center = new FieldPoint(119966, 35466),
        StartPoints = [new FieldPoint(119320, 35680), new FieldPoint(124380, 33260)],
        WarpGates =
        [
            new FieldWarpGate(124391, 32913, 37, 80, 32, "Dcave", 158543, 19557, 290, 65, 0),
            new FieldWarpGate(118808, 35686, 520, 80, 32, "forever-fall-03", -6056, 43245, 787, 0, 0)
        ],
        BackMusicCode = "BGM_CODE_DUNGEON",
        ServerCode = "PLAY_SERVER_CODE_MAIN"
    };
}

public static class DcaveField
{
    public static readonly FieldDef Definition = new(
        "Dcave", "Dcave", FieldState.Dungeon, FieldBackdrops.Ruin2,
        "Field\\cave\\Dcave.smd",
        [
            "Field\\cave\\Dcave_ani01.smd",
            "Field\\cave\\Dcave_ani02.smd",
            "Field\\cave\\Dcave_ani03.smd",
            "Field\\cave\\Dcave_ani04.smd",
            "Field\\cave\\Dcave_ani05.smd",
            "Field\\cave\\Dcave_ani06.smd",
            "Field\\cave\\Dcave_ani07.smd",
            "Field\\cave\\Dcave_ani08.smd",
            "Field\\cave\\Dcave_ani09.smd",
            "Field\\cave\\Dcave_ani10.smd",
            "Field\\cave\\Dcave_ani11.smd",
            "Field\\cave\\Dcave_ani12.smd",
            "Field\\cave\\Dcave_ani13.smd"
        ])
    {
        FieldCode = 26,
        TravelLevel = 65,
        Center = new FieldPoint(158814, 20070),
        WarpGates =
        [
            new FieldWarpGate(158042, 19525, 330, 120, 32, "Mcave", 124396, 33291, 37, 0, 0),
            new FieldWarpGate(158023, 20453, 240, 120, 32, "Tcave", 125566, 24825, 480, 0, 0)
        ],
        BackMusicCode = "BGM_CODE_DUNGEON",
        ServerCode = "PLAY_SERVER_CODE_EXTEND"
    };
}
