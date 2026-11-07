using PristonTale.Simulation.World;

namespace PristonTale.Simulation.World.Fields;

public static class PillaiField
{
    public static readonly FieldDef Definition = new(
        "pilai",
        "Pillai",
        FieldState.Village,
        FieldBackdrops.Fall,
        "Field\\forever-fall\\pilai.smd",
        [
            "Field\\forever-fall\\piani-01.smd",
            "Field\\forever-fall\\piani-02.smd",
            "Field\\forever-fall\\piani-03.smd",
            "Field\\forever-fall\\piani-04.smd",
            "Field\\forever-fall\\piani-05.smd",
            "Field\\forever-fall\\piani-06.smd",
            "Field\\forever-fall\\piani-07.smd",
            "Field\\forever-fall\\piani-08.smd",
            "Field\\forever-fall\\piani-09.smd",
            "Field\\forever-fall\\piani-10.smd",
            "Field\\forever-fall\\piani-11.smd",
            "Field\\forever-fall\\piani-12.smd",
            "Field\\forever-fall\\piani-13.smd",
            "Field\\forever-fall\\piani-14.smd",
            "Field\\forever-fall\\piani-15.smd",
            "Field\\forever-fall\\piani-16.smd",
            "Field\\forever-fall\\piani-17.smd",
            "Field\\forever-fall\\piani-18.smd",
            "Field\\forever-fall\\piani-19.smd",
            "Field\\forever-fall\\piani-20.smd",
            "Field\\forever-fall\\piani-21.smd",
            "Field\\forever-fall\\piani-22.smd",
            "Field\\forever-fall\\piani-23.smd",
            "Field\\forever-fall\\piani-24.smd",
            "Field\\forever-fall\\piani-25.smd",
            "Field\\forever-fall\\piani-26.smd",
            "Field\\forever-fall\\piani-27.smd",
            "Field\\forever-fall\\piani-28.smd",
            "Field\\forever-fall\\piani-29.smd",
            "Field\\forever-fall\\piani-30.smd",
            "Field\\forever-fall\\piani-31.smd",
            "Field\\forever-fall\\piani-32.smd",
            "Field\\forever-fall\\piani-33.smd",
            "Field\\forever-fall\\piani-34.smd",
            "Field\\forever-fall\\piani-35.smd",
            "Field\\forever-fall\\piani-36.smd",
            "Field\\forever-fall\\piani-37.smd",
            "Field\\forever-fall\\piani-38.smd",
            "Field\\forever-fall\\piani-39.smd",
            "Field\\forever-fall\\piani-40.smd",
            "Field\\forever-fall\\piani-42.smd",
            "Field\\forever-fall\\piani-43.smd"
        ])
    {
        FieldCode = FieldCodes.Pillai,
        Center = new FieldPoint(2981, 75486),
        StartPoints = [new FieldPoint(2287, 74131), new FieldPoint(3547, 75500)],
        WarpGates =
        [
            new FieldWarpGate(2000, 72907, 474, 64, 32, "forever-fall-01", 1958, 70922, 536, 1),
            new FieldWarpGate(2245, 78259, 745, 64, 32, "pilai", 2252, 78041, 754, 2)
        ],
        Ambients =
        [
            new FieldAmbient(new FieldVector(3557, 613, 75494), 20, 9),
            new FieldAmbient(new FieldVector(2256, 745, 78247), 50, 6),
            new FieldAmbient(new FieldVector(2066, 463, 73782), 0, 5),
            new FieldAmbient(new FieldVector(3032, 554, 73674), 10, 4),
            new FieldAmbient(new FieldVector(2246, 554, 76696), 0, 14),
            new FieldAmbient(new FieldVector(2531, 544, 74955), 30, 12),
            new FieldAmbient(new FieldVector(2034, 544, 74921), 30, 12),
            new FieldAmbient(new FieldVector(934, 542, 73886), 0, 17),
            new FieldAmbient(new FieldVector(3851, 492, 74639), 0, 17),
            new FieldAmbient(new FieldVector(3326, 666, 76387), 0, 17)
        ],
        BackMusicCode = "BGM_CODE_FILAI",
        ServerCode = "PLAY_SERVER_CODE_MAIN"
    };
}
