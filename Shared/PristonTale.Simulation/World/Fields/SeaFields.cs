namespace PristonTale.Simulation.World.Fields;

// Below Lost Island: the Sea of Abyss and its temple.
public static class SeaOfAbyssField
{
    public static readonly FieldDef Definition = new(
        "SeaA", "Sea of Abyss", FieldState.Dungeon, FieldBackdrops.Rain,
        "Field\\SeaA\\SeaA.smd",
        [
            "Field\\SeaA\\sea_ani01.smd",
            "Field\\SeaA\\sea_ani02.smd",
            "Field\\SeaA\\sea_ani03.smd",
            "Field\\SeaA\\sea_ani04.smd",
            "Field\\SeaA\\sea_ani05.smd",
            "Field\\SeaA\\sea_ani06.smd",
            "Field\\SeaA\\sea_ani07.smd",
            "Field\\SeaA\\sea_ani08.smd",
            "Field\\SeaA\\sea_ani09.smd",
            "Field\\SeaA\\sea_ani10.smd"
        ])
    {
        FieldCode = 49,
        Ambience = FieldAmbience.Cave,
        LimitLevel = 112,
        Center = new FieldPoint(-19925, -3858),
        WarpGates =
        [
            new FieldWarpGate(-21970, -5959, 157, 64, 32, "lost", -14250, -1548, 196, 95, 0),
            new FieldWarpGate(-23511, 7571, 859, 64, 32, "Stemple", -29930, 17161, 828, 116, 0),
            new FieldWarpGate(-22392, 7587, 859, 64, 32, "Stemple", -29930, 17161, 828, 116, 0)
        ],
        BackMusicCode = "BGM_CODE_DUNGEON",
        ServerCode = "PLAY_SERVER_CODE_EXTEND"
    };
}

public static class StempleField
{
    public static readonly FieldDef Definition = new(
        "Stemple", "Stemple", FieldState.Dungeon, FieldBackdrops.Rain,
        "Field\\SeaA\\Stemple.smd", [])
    {
        FieldCode = 50,
        Ambience = FieldAmbience.Temple,
        LimitLevel = 116,
        Center = new FieldPoint(-28284, 27144),
        WarpGates =
        [
            new FieldWarpGate(-29805, 16913, 853, 64, 32, "SeaA", -23811, 7571, 859, 0, 0)
            {
                MoreExits = [new FieldVector(-22092, 859, 7587)]
            }
        ],
        BackMusicCode = "BGM_CODE_DUNGEON",
        ServerCode = "PLAY_SERVER_CODE_EXTEND"
    };
}
