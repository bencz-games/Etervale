namespace PristonTale.Simulation.World.Fields;

public static class Forest2Field
{
    public static readonly FieldDef Definition = new(
        "fore-2",
        "Forest 2",
        FieldState.Forest,
        FieldBackdrops.Forest,
        "Field\\forest\\fore-2.smd",
        ["Field\\forest\\2ani-04.smd"])
    {
        FieldCode = 1,
        Center = new FieldPoint(-5427, -9915),
        StartPoints =
        [
            new FieldPoint(-2272, -10314),
            new FieldPoint(-7192, -11175)
        ],
        Gates =
        [
            new FieldGate("fore-1", -292, -9548, 0)
        ],
        WarpGates =
        [
            new FieldWarpGate(-3408, -12447, 251, 64, 32, "fore-2", -3265, -12583, 214, 2)
        ],
        Ambients =
        [
            new FieldAmbient(new FieldVector(-3775, 171, -14447), 160, 13)
        ],
        BackMusicCode = "BGM_CODE_FOREST",
        ServerCode = "PLAY_SERVER_CODE_MAIN"
    };
}
