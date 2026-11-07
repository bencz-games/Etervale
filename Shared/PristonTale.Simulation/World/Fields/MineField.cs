namespace PristonTale.Simulation.World.Fields;

public static class Mine1Field
{
    public static readonly FieldDef Definition = new(
        "mine-1", "Mine 1", FieldState.Dungeon, FieldBackdrops.Ruin2,
        "Field\\Mine\\mine-1.smd", [])
    {
        FieldCode = 44,
        Ambience = FieldAmbience.Cave,
        Center = new FieldPoint(18006, -34740),
        WarpGates =
        [
            new FieldWarpGate(17944, -34641, 246, 64, 32, "ice1", 35352, 31928, 829, 0),
            new FieldWarpGate(20396, -36690, 250, 64, 32, "CrystalNest", 35235, -93821, 530, 0)
        ],
        BackMusicCode = "BGM_CODE_DUNGEON",
        ServerCode = "PLAY_SERVER_CODE_EXTEND"
    };
}

// Below the Ice Mine; it has no way out.
public static class CrystalNestField
{
    public static readonly FieldDef Definition = new(
        "CrystalNest", "Crystal Nest", FieldState.Dungeon, FieldBackdrops.Rain,
        "Field\\CrystalNest\\CrystalNest.smd", [])
    {
        FieldCode = 52,
        Ambience = FieldAmbience.IceWind,
        Center = new FieldPoint(35235, -93821),
        StartPoints = [new FieldPoint(35235, -93821)],
        BackMusicCode = "BGM_CODE_DUNGEON",
        ServerCode = "PLAY_SERVER_CODE_EXTEND"
    };
}
