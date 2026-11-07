using PristonTale.Simulation.World;

namespace PristonTale.Simulation.World.Fields;

public static class Ice1Field
{
    public static readonly FieldDef Definition = new("ice1", "Ice 1", FieldState.Ice, FieldBackdrops.Snow, "Field\\Ice\\ice1.smd",
        [
            "Field\\Ice\\ice_ani_01.smd",
            "Field\\Ice\\ice_bip_leewood01.smd",
            "Field\\Ice\\ice_bip_leewood03.smd",
            "Field\\Ice\\ice_bip_leewood04.smd",
            "Field\\Ice\\ice_bip_leaf01.smd",
            "Field\\Ice\\ice_bip_leaf02.smd",
            "Field\\Ice\\ice_bip_leaf03.smd",
            "Field\\Ice\\ice_bip_leaf04.smd",
            "Field\\Ice\\ice_bip_leaf05.smd",
            "Field\\Ice\\ice_bip_leaf06.smd",
            "Field\\Ice\\ice_bip_leaf07.smd"
        ])
    {
        FieldCode = 31,
        Center = new FieldPoint(32313, 28529),
        Gates = [new FieldGate("ice2", 33729, 38029, 0)],
        BackMusicCode = "BGM_CODE_ICE",
        ServerCode = "PLAY_SERVER_CODE_EXTEND"
    };
}

public static class Ice2Field
{
    public static readonly FieldDef Definition = new(
        "ice2",
        "Ice 2",
        FieldState.Ice,
        FieldBackdrops.Snow,
        "Field\\Ice\\ice_2.smd",
        [])
    {
        FieldCode = 35,
        Center = new FieldPoint(36264, 40182),
        Gates = [new FieldGate("ice1", 35364, 39518, 0)],
        WarpGates = [new FieldWarpGate(37981, 50790, 1216, 64, 32, "Boss", 33000, 50036, 1512, 0)],
        BackMusicCode = "BGM_CODE_ICE",
        ServerCode = "PLAY_SERVER_CODE_EXTEND"
    };
}

public static class IceUraField
{
    public static readonly FieldDef Definition = new(
        "ice_ura",
        "Ice Ura",
        FieldState.Ice,
        FieldBackdrops.Snow,
        "Field\\Ice\\ice_ura.smd",
        [
            "Field\\Ice\\ice_ani_01.smd"
        ])
    {
        FieldCode = FieldCodes.InnerIce,
        SightCells = 1,
        WarpGates = [new FieldWarpGate(30407, 22232, 1349, 64, 32, "ice_ura", 30610, 22164, 1304, 2)],
        Center = new FieldPoint(32621, 23865),
        Gates = [new FieldGate("ice1", 31848, 27225, 0)],
        BackMusicCode = "BGM_CODE_DESERT",
        ServerCode = "PLAY_SERVER_CODE_EXTEND"
    };
}
