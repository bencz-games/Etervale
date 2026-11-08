namespace PristonTale.Gameplay.Audio;

// Bellatra's sounds: each round's call, the screen turning, a seal's pickup, and a team going on, out and the end.
public enum BellatraCue
{
    Round1 = 1,
    Round2,
    Round3,
    Round4,
    Round5,
    Round6,
    Round7,
    Round8,
    Turn,
    Item,
    StageComplete,
    StageFail,
    Ending
}

public static class BellatraSounds
{
    public static readonly (BellatraCue Cue, string Path)[] Files =
    [
        (BellatraCue.Round1, "wav/Sod/round1.wav"), (BellatraCue.Round2, "wav/Sod/round2.wav"), (BellatraCue.Round3, "wav/Sod/round3.wav"),
        (BellatraCue.Round4, "wav/Sod/round4.wav"), (BellatraCue.Round5, "wav/Sod/round5.wav"), (BellatraCue.Round6, "wav/Sod/round6.wav"),
        (BellatraCue.Round7, "wav/Sod/round7.wav"), (BellatraCue.Round8, "wav/Sod/round8.wav"), (BellatraCue.Turn, "wav/Sod/turn.wav"),
        (BellatraCue.Item, "wav/Sod/SOD-item.wav"), (BellatraCue.StageComplete, "wav/Sod/BGM/Stage_Complete.wav"),
        (BellatraCue.StageFail, "wav/Sod/BGM/Stage_Fail.wav"), (BellatraCue.Ending, "wav/Sod/BGM/Ending.wav")
    ];

    public static float Volume(BellatraCue cue) => cue == BellatraCue.Item ? FieldAudioController.SoftVolume : 1f;
}
