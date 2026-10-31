using PristonTale.Simulation.World;

namespace PristonTale.Simulation.World.Fields;

public static class RicartenField
{
    public static readonly FieldDef Definition = new(
        "village-2",
        "Ricarten",
        FieldState.Village,
        FieldBackdrops.Forest,
        "Field\\Ricarten\\village-2.smd",
        [
            "Field\\Ricarten\\v-ani01.smd",
            "Field\\Ricarten\\v-ani02.smd",
            "Field\\Ricarten\\v-ani03.smd",
            "Field\\Ricarten\\v-ani04.smd",
            "Field\\Ricarten\\v-ani05.smd",
            "Field\\Ricarten\\v-ani06.smd",
            "Field\\Ricarten\\v-ani07.smd",
            "Field\\Ricarten\\v-ani08.smd",
            "Field\\Ricarten\\v-ani09.smd",
            "Field\\Ricarten\\v-ani10.smd",
            "Field\\Ricarten\\v-ani11.smd",
            "Field\\Ricarten\\v-ani12.smd",
            "Field\\Ricarten\\v-ani13.smd",
            "Field\\Ricarten\\v-ani14.smd"
        ])
    {
        FieldCode = FieldCodes.Ricarten,
        HazeReduction = 0.2f,
        // Ricarten stands in the clouds: a light mist of its own in the mornings.
        MorningMist = 0.8f,
        Center = new FieldPoint(2596, -18738),
        StartPoints =
        [
            new FieldPoint(2592, -18566),
            new FieldPoint(-1047, -16973)
        ],
        WarpGates =
        [
            new FieldWarpGate(
                734,
                -20119,
                312,
                64,
                32,
                "village-2",
                822,
                -19956,
                254,
                0,
                2)
        ],
        Ambients =
        [
            new FieldAmbient(new FieldVector(-1006, 170, -17835), 80, 27),
            new FieldAmbient(new FieldVector(2632, 321, -17285), 80, 27)
        ],
        BackMusicCode = "BGM_CODE_TOWN2",
        ServerCode = "PLAY_SERVER_CODE_MAIN"
    };
}
