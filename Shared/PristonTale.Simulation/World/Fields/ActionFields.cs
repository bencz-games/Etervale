namespace PristonTale.Simulation.World.Fields;

public static class FallGameField
{
    public static readonly FieldDef Definition = new(
        "fall_game", "Fall Game", FieldState.Action, FieldBackdrops.Rain,
        "Field\\Fall_Game\\fall_game.smd",
        [
            "Field\\Fall_Game\\chandelier-ani01.smd",
            "Field\\Fall_Game\\chandelier-ani03.smd",
            "Field\\Fall_Game\\chandelier-ani04.smd",
            "Field\\Fall_Game\\chandelier-ani05.smd",
            "Field\\Fall_Game\\chandelier-ani06.smd",
            "Field\\Fall_Game\\chandelier-ani07.smd",
            "Field\\Fall_Game\\chandelier-ani08.smd",
            "Field\\Fall_Game\\chandelier-ani09.smd",
            "Field\\Fall_Game\\chandelier-ani10.smd",
            "Field\\Fall_Game\\chandelier-ani11.smd",
            "Field\\Fall_Game\\chandelier-ani12.smd",
            "Field\\Fall_Game\\chandelier-ani13.smd",
            "Field\\Fall_Game\\chandelier-ani14.smd",
            "Field\\Fall_Game\\chandelier-ani15.smd",
            "Field\\Fall_Game\\chandelier-ani16.smd",
            "Field\\Fall_Game\\chandelier-ani17.smd",
            "Field\\Fall_Game\\chandelier-ani18.smd",
            "Field\\Fall_Game\\chandelier-ani19.smd"
        ])
    {
        FieldCode = FieldCodes.FallGame,
        Ambience = FieldAmbience.DarkSanctuary,
        Center = new FieldPoint(5800, 36747),
        StartPoints = [new FieldPoint(5800, 36795), new FieldPoint(5830, 36764), new FieldPoint(5800, 36736)],
        ActionCamera = new FieldActionCamera(213140, 36421, 5583, 11483),
        BackMusicCode = "BGM_CODE_DUNGEON",
        ServerCode = "PLAY_SERVER_CODE_MAIN"
    };
}

public static class SwampField
{
    public static readonly FieldDef Definition = new(
        "swamp", "Swamp", FieldState.Action, FieldBackdrops.Rain,
        "Field\\swamp\\swamp.smd", [])
    {
        FieldCode = 47,
        Ambience = FieldAmbience.ForestNight,
        Center = new FieldPoint(4550, 42820),
        ActionCamera = new FieldActionCamera(44800, 42440, 4350, 8550),
        BackMusicCode = "BGM_CODE_DUNGEON",
        ServerCode = "PLAY_SERVER_CODE_MAIN"
    };
}
