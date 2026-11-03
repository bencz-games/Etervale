using PristonTale.Simulation.World;

namespace PristonTale.Simulation.World.Fields;

public static class Sod1Field
{
    public static readonly FieldDef Definition = new(
        "sod-1", "SoD 1", FieldState.Ruin, FieldBackdrops.Ruin2,
        "Field\\Sod\\sod-1.smd",
        [
            "Field\\Sod\\s-ani01_Bip.smd",
            "Field\\Sod\\s-ani02_Bip.smd",
            "Field\\Sod\\s-ani03_Bip.smd",
            "Field\\Sod\\s-ani04_Bip.smd",
            "Field\\Sod\\s-ani05_Bip.smd",
            "Field\\Sod\\s-ani06_Bip.smd",
            "Field\\Sod\\s-ani07_Bip.smd",
            "Field\\Sod\\s-ani08_Bip.smd",
            "Field\\Sod\\s-ani09_Bip.smd",
            "Field\\Sod\\s-ani10_Bip.smd",
            "Field\\Sod\\s-ani11_Bip.smd",
            "Field\\Sod\\s-ani12_Bip.smd",
            "Field\\Sod\\s-ani13_Bip.smd",
            "Field\\Sod\\s-ani14_Bip.smd",
            "Field\\Sod\\s-ani15_Bip.smd",
            "Field\\Sod\\s-ani16_Bip.smd",
            "Field\\Sod\\s-ani17_Bip.smd",
            "Field\\Sod\\s-ani18_Bip.smd"
        ])
    {
        FieldCode = FieldCodes.Bellatra,
        Center = new FieldPoint(22018, 6569),
        BackMusicCode = "BGM_CODE_CUSTOM",
        ServerCode = "PLAY_SERVER_CODE_EXTEND"
    };
}

// The second stage of Bellatra's survival trial.
public static class Sod2Field
{
    public static readonly FieldDef Definition = new(
        "sod-2", "SoD 2", FieldState.Ruin, FieldBackdrops.Ruin2,
        "Field\\Sod\\sod-2.smd", [])
    {
        FieldCode = 45,
        Center = new FieldPoint(21600, 17000),
        BackMusicCode = "BGM_CODE_CUSTOM",
        ServerCode = "PLAY_SERVER_CODE_EXTEND"
    };
}
