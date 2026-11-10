namespace PristonTale.Gameplay.Audio;

// The events' sounds (wav/Sod): Bellatra's round calls, screen turn, seal pickup, and a team going on, out and the end,
// whose fanfare also ends a siege of Bless Castle.
public enum EventCue
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

public static class EventSounds
{
    public static readonly (EventCue Cue, string Path)[] Files =
    [
        (EventCue.Round1, "wav/Sod/round1.wav"), (EventCue.Round2, "wav/Sod/round2.wav"), (EventCue.Round3, "wav/Sod/round3.wav"),
        (EventCue.Round4, "wav/Sod/round4.wav"), (EventCue.Round5, "wav/Sod/round5.wav"), (EventCue.Round6, "wav/Sod/round6.wav"),
        (EventCue.Round7, "wav/Sod/round7.wav"), (EventCue.Round8, "wav/Sod/round8.wav"), (EventCue.Turn, "wav/Sod/turn.wav"),
        (EventCue.Item, "wav/Sod/SOD-item.wav"), (EventCue.StageComplete, "wav/Sod/BGM/Stage_Complete.wav"),
        (EventCue.StageFail, "wav/Sod/BGM/Stage_Fail.wav"), (EventCue.Ending, "wav/Sod/BGM/Ending.wav")
    ];

    public static float Volume(EventCue cue) => cue == EventCue.Item ? FieldAudioController.SoftVolume : 1f;
}
