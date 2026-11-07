namespace PristonTale.Simulation.World.Fields;

public static class NaviskoField
{
    public static readonly FieldDef Definition = new(
        "village-1",
        "Navisko",
        FieldState.Village,
        FieldBackdrops.Desert,
        "Field\\forest\\village-1.smd",
        [
            "Field\\forest\\v2-ani01.smd"
        ])
    {
        FieldCode = 9,
        WarpGates = [new FieldWarpGate(21936, -1833, 855, 64, 32, "village-1", 21826, -1712, 825, 2)],
        FieldEvent = FieldEventKind.Nightmare,
        Center = new FieldPoint(22214, -1182),
        StartPoints = [new FieldPoint(22214, -1182)],
        Gates =
        [
            new FieldGate("De-3", 27110, -479, 0),
            new FieldGate("sod-1", 21840, 1062, 0)
        ],
        BackMusicCode = "BGM_CODE_TOWN1",
        ServerCode = "PLAY_SERVER_CODE_MAIN"
    };
}
