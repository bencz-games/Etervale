namespace PristonTale.Simulation.World.Fields;

// The way down from Desert 3: the Secret Laboratory, the Ancient Weapons and the Land of Nurwn.
public static class SlabField
{
    public static readonly FieldDef Definition = new(
        "Slab", "Secret Laboratory", FieldState.Dungeon, FieldBackdrops.Rain,
        "Field\\Slab\\Slab.smd", [])
    {
        FieldCode = 46,
        Ambience = FieldAmbience.Iron,
        Center = new FieldPoint(-11975, -62457),
        WarpGates =
        [
            new FieldWarpGate(-12003, -54164, 374, 64, 32, "ancientW", 12795, -73112, 458, 0),
            new FieldWarpGate(-11973, -62662, 173, 64, 32, "De-3", 29551, 2366, 826, 0)
        ],
        BackMusicCode = "BGM_CODE_DUNGEON",
        ServerCode = "PLAY_SERVER_CODE_EXTEND"
    };
}

public static class AncientWeaponsField
{
    public static readonly FieldDef Definition = new(
        "ancientW", "Ancient Weapons", FieldState.Dungeon, FieldBackdrops.Rain,
        "Field\\AncientW\\ancientW.smd",
        [
            "Field\\AncientW\\RotObj01.smd",
            "Field\\AncientW\\RotObj02.smd",
            "Field\\AncientW\\RotObj03.smd",
            "Field\\AncientW\\RotObj04.smd",
            "Field\\AncientW\\RotObj05.smd",
            "Field\\AncientW\\RotObj06.smd",
            "Field\\AncientW\\RotObj07.smd",
            "Field\\AncientW\\RotObj08.smd",
            "Field\\AncientW\\RotObj09.smd",
            "Field\\AncientW\\RotObj10.smd"
        ])
    {
        FieldCode = 48,
        Ambience = FieldAmbience.Temple,
        Center = new FieldPoint(12795, -73112),
        WarpGates =
        [
            new FieldWarpGate(12796, -74250, 458, 64, 32, "Slab", -11998, -55120, 393, 0),
            new FieldWarpGate(12567, -53797, 794, 64, 32, "LandofNurwn", -11702, -87663, 1411, 0),
            new FieldWarpGate(12695, -53794, 794, 64, 32, "LandofNurwn", -11702, -87664, 1411, 0),
            new FieldWarpGate(12858, -53796, 794, 64, 32, "LandofNurwn", -11702, -87663, 1411, 0),
            new FieldWarpGate(12982, -53801, 794, 64, 32, "LandofNurwn", -11702, -87664, 1411, 0)
        ],
        BackMusicCode = "BGM_CODE_DUNGEON",
        ServerCode = "PLAY_SERVER_CODE_EXTEND"
    };
}

// The Greedy skies are named, but a dungeon draws no sky.
public static class LandOfNurwnField
{
    public static readonly FieldDef Definition = new(
        "LandofNurwn", "Land of Nurwn", FieldState.Dungeon, FieldBackdrops.Greedy,
        "Field\\LandofNurwn\\LandofNurwn.smd", [])
    {
        FieldCode = 53,
        Ambience = FieldAmbience.IceWind,
        Center = new FieldPoint(-11702, -87663),
        StartPoints = [new FieldPoint(-11702, -87663)],
        BackMusicCode = "BGM_CODE_DUNGEON",
        ServerCode = "PLAY_SERVER_CODE_EXTEND"
    };
}
