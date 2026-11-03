namespace PristonTale.Simulation.World.Fields;

public static class Forest3Field
{
    public static readonly FieldDef Definition = new(
        "fore-3",
        "Forest 3",
        FieldState.Forest,
        FieldBackdrops.Forest,
        "Field\\forest\\fore-3.smd",
        [
            "Field\\forest\\3ani-01.smd",
            "Field\\forest\\3ani-02.smd",
            "Field\\forest\\3ani-03.smd",
            "Field\\forest\\3ani-04.smd",
            "Field\\forest\\3ani-05.smd",
            "Field\\forest\\3ani-06.smd",
            "Field\\forest\\3ani-07.smd",
            "Field\\forest\\3ani-08.smd",
            "Field\\forest\\3ani-09.smd",
            "Field\\forest\\3ani-10.smd",
            "Field\\forest\\3ani-11.smd",
            "Field\\forest\\3ani-12.smd",
            "Field\\forest\\3ani-13.smd",
            "Field\\forest\\3ani-14.smd"
        ])
    {
        FieldCode = FieldCodes.Forest3,
        Center = new FieldPoint(-16419, -7054),
        StartPoints =
        [
            new FieldPoint(-10585, -11810),
            new FieldPoint(-13659, -9753)
        ],
        Gates =
        [
            new FieldGate("fore-2", -8508, -10576, 0)
        ],
        WarpGates =
        [
            new FieldWarpGate(-16638, -6737, 267, 64, 32, "Tcave", 119112, 26028, 510, 55, 0)
        ],
        Ambients =
        [
            new FieldAmbient(new FieldVector(-13819, 226, -8950), 100, 15)
        ],
        BackMusicCode = "BGM_CODE_FOREST",
        ServerCode = "PLAY_SERVER_CODE_MAIN"
    };
}
