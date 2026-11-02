namespace PristonTale.Simulation.World.Fields;

public static class GreedyField
{
    public static readonly FieldDef Definition = new(
        "Greedy", "Greedy", FieldState.Ice, FieldBackdrops.Greedy,
        "Field\\Greedy\\Greedy.smd",
        [
            "Field\\Greedy\\vane1_ani.smd",
            "Field\\Greedy\\vane2_ani.smd",
            "Field\\Greedy\\vane3_ani.smd",
            "Field\\Greedy\\vane4_ani.smd",
            "Field\\Greedy\\ani_tail.smd",
            "Field\\Greedy\\ship_ani.smd",
            "Field\\Greedy\\ship1_ani.smd",
            "Field\\Greedy\\ship2_ani.smd",
            "Field\\Greedy\\ship3_ani.smd",
            "Field\\Greedy\\flower_animi01.smd",
            "Field\\Greedy\\flower_animi02.smd",
            "Field\\Greedy\\flower_animi03.smd",
            "Field\\Greedy\\flower_animi04.smd",
            "Field\\Greedy\\flower_animi05.smd",
            "Field\\Greedy\\flower_animi06.smd",
            "Field\\Greedy\\flower_animi07.smd",
            "Field\\Greedy\\flower_animi08.smd",
            "Field\\Greedy\\flower_animi09.smd",
            "Field\\Greedy\\flower_animi10.smd",
            "Field\\Greedy\\flower_ani00.smd",
            "Field\\Greedy\\flower_ani01.smd",
            "Field\\Greedy\\flower_ani02.smd",
            "Field\\Greedy\\flower_ani03.smd",
            "Field\\Greedy\\flower_ani04.smd",
            "Field\\Greedy\\flower_ani05.smd",
            "Field\\Greedy\\flower_ani06.smd",
            "Field\\Greedy\\flower_ani07.smd",
            "Field\\Greedy\\flower_ani08.smd",
            "Field\\Greedy\\flower_ani09.smd",
            "Field\\Greedy\\door.smd"
        ])
    {
        FieldCode = 34,
        LimitLevel = 70,
        Center = new FieldPoint(13930, 23206),
        BackMusicCode = "BGM_CODE_DESERT",
        ServerCode = "PLAY_SERVER_CODE_MAIN"
    };
}
