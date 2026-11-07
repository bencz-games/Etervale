using PristonTale.Simulation.World;

namespace PristonTale.Simulation.World.Fields;

public static class CastleField
{
    public static readonly FieldDef Definition = new(
        "castle",
        "Bless Castle",
        FieldState.Castle,
        FieldBackdrops.Ruin2,
        "Field\\castle\\castle.smd",
        [
            "Field\\castle\\crystal_ani01.smd",
            "Field\\castle\\crystal_ani02.smd",
            "Field\\castle\\crystal_ani03.smd",
            "Field\\castle\\crystal_ani04.smd",
            "Field\\castle\\crystal_ani05.smd",
            "Field\\castle\\crystal_ani06.smd",
            "Field\\castle\\wf_gate_ani.smd"
        ])
    {
        FieldCode = FieldCodes.Castle,
        WarpGates = [new FieldWarpGate(32527, -30693, 774, 64, 32, "castle", 32739, -30474, 711, 2)],
        Center = new FieldPoint(35225, -23847),
        StartPoints =
        [
            new FieldPoint(33920, -23479),
            new FieldPoint(35247, -23355),
            new FieldPoint(36164, -23446)
        ],
        BackMusicCode = "BGM_CODE_SOD3",
        ServerCode = "PLAY_SERVER_CODE_EXTEND"
    };
}
