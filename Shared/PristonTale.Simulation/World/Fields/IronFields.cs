namespace PristonTale.Simulation.World.Fields;

public static class Iron1Field
{
    public static readonly FieldDef Definition = new(
        "iron-1",
        "Iron 1",
        FieldState.Iron,
        FieldBackdrops.Iron1,
        "Field\\Iron\\iron-1.smd",
        [
            "Field\\Iron\\iron-ani01.smd",
            "Field\\Iron\\iron-ani02.smd",
            "Field\\Iron\\iron-ani03.smd",
            "Field\\Iron\\i1-ani01_Bip.smd",
            "Field\\Iron\\i1-ani02_Bip.smd",
            "Field\\Iron\\i1-ani03_Bip.smd",
            "Field\\Iron\\i1-ani04_Bip.smd",
            "Field\\Iron\\i1-ani05_Bip.smd",
            "Field\\Iron\\i1-ani06_Bip.smd",
            "Field\\Iron\\i1-ani07_Bip.smd",
            "Field\\Iron\\i1-ani08_Bip.smd",
            "Field\\Iron\\i1-ani09_Bip.smd",
            "Field\\Iron\\i1-ani10_Bip.smd",
            "Field\\Iron\\i1-ani11_Bip.smd"
        ])
    {
        FieldCode = 27,
        Center = new FieldPoint(47617, 13626),
        StartPoints =
        [
            new FieldPoint(46905, 13478),
            new FieldPoint(47357, 20845),
            new FieldPoint(46741, 21349)
        ],
        Gates = [new FieldGate("iron-2", 45316, 21407, 0)],
        BackMusicCode = "BGM_CODE_DESERT",
        ServerCode = "PLAY_SERVER_CODE_EXTEND"
    };
}

public static class Iron2Field
{
    public static readonly FieldDef Definition = new(
        "iron-2",
        "Iron 2",
        FieldState.Iron,
        FieldBackdrops.Iron2,
        "Field\\Iron\\iron-2.smd",
        [
            "Field\\Iron\\iron2-ani01.smd",
            "Field\\Iron\\iron2-ani02.smd",
            "Field\\Iron\\iron2-ani03.smd",
            "Field\\Iron\\iron2-ani04.smd",
            "Field\\Iron\\iron2-ani05.smd",
            "Field\\Iron\\iron2-ani06.smd",
            "Field\\Iron\\i2-bip01.smd",
            "Field\\Iron\\i2-bip02.smd",
            "Field\\Iron\\i2-bip03.smd",
            "Field\\Iron\\i2-bip04_ani.smd",
            "Field\\Iron\\i2-bip05_ani.smd",
            "Field\\Iron\\i2-bip06_ani.smd",
            "Field\\Iron\\i2-bip07_ani.smd",
            "Field\\Iron\\i2-bip08_ani.smd",
            "Field\\Iron\\i2-bip09.smd",
            "Field\\Iron\\i2-bip10.smd",
            "Field\\Iron\\i2-bip11_ani.smd",
            "Field\\Iron\\i2-bip12_ani.smd"
        ])
    {
        FieldCode = 28,
        Center = new FieldPoint(33938, 24229),
        StartPoints = [new FieldPoint(35149, 24326), new FieldPoint(44893, 21380)],
        Gates = [new FieldGate("ice_ura", 33618, 24011, 0)],
        BackMusicCode = "BGM_CODE_DESERT",
        ServerCode = "PLAY_SERVER_CODE_EXTEND"
    };
}
