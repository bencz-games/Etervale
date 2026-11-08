using PristonTale.Client.Net.Presence;
using PristonTale.Contracts.Social;
using PristonTale.Contracts.Texts;
using PristonTale.Gameplay.Audio;
using PristonTale.Gameplay.UI.Bellatra;

namespace PristonTale.Tests;

internal static class BellatraShowTests
{
    private const int TrackBytes = 1488;
    private const int EffectBytes = 488;
    private const int Tracks = 50;

    public static IEnumerable<(string, Func<Task>)> Cases()
    {
        yield return ("Bellatra's effect table is read by its layout, a track steps at 70 frames a second to a held key, a loop or its end, an effect "
            + "runs once at a time, and a table of another size, an effect naming a track or a layer out of it are refused", TableAsync);
        yield return ("Bellatra's show: a ticket's arrival shows the loading screen, a round's call three seconds after its start with round 1's "
            + "music, coming back to a game in play only plays its music, a team going on hears its fanfare, the screen closes at five seconds and opens "
            + "at nine for round 4's call and music, a team out hears its fall and its screen opens only when it leaves, the end its ending, and a "
            + "room's seal quakes", ShowAsync);
    }

    private static Task TableAsync()
    {
        var data = new byte[Tracks * TrackBytes + 40 * EffectBytes];
        Track(data, 0, (1, 10, 255), (1, -2, 255));
        Track(data, 1, (1, 10, 255));
        Effect(data, (int)BellatraEffect.Round, 0);
        Effect(data, (int)BellatraEffect.Start, 1);
        Track(data, 2, (3, 4, 60), (-1, -1, 9));
        Key(data, 2, 0, offset: (12, -34), growth: (56, 78), angle: 360);
        Effect(data, (int)BellatraEffect.Stage, Tracks + 3);
        Effect(data, (int)BellatraEffect.Complete, 2, layer: 2, origin: (400, 300), size: (90, 32));
        Effect(data, (int)BellatraEffect.Fail, 2, layer: 3);
        var table = BellatraEffectTable.Read(data);
        Assert.True(table.Track(0) is { Kind: BellatraImageKind.Texture, Keys: [{ Delay: 10, Alpha: 255 }, { Delay: BellatraKey.Hold }] }
            && table.Track(2).Keys is [{ Image: 3, Delay: 4, Alpha: 60, Offset.X: 12, Offset.Y: -34, Growth.X: 56, Growth.Y: 78, Angle: 360 }, { Delay: BellatraKey.Loop }]
            && table.Layers(BellatraEffect.Round) is [{ Track: 0, Layer: 0 }]
            && table.Layers(BellatraEffect.Complete) is [{ Track: 2, Layer: 2, Origin.X: 400, Origin.Y: 300, Size.X: 90, Size.Y: 32 }]
            && table.Layers(BellatraEffect.Stage).Count == 0 && table.Layers(BellatraEffect.Fail).Count == 0,
            "the table's tracks or effects were not read by their layout, or an effect naming a track or a layer out of it was kept");

        var effects = new BellatraEffects(table);
        Assert.True(effects.Start(BellatraEffect.Round) && !effects.Start(BellatraEffect.Round) && effects.Start(BellatraEffect.Start)
            && effects.Start(BellatraEffect.Complete) && !effects.Start(BellatraEffect.Stage), "an effect started twice, or one without layers started");
        const double frame = Contracts.Units.GameUnits.TickMs / 1000.0;
        effects.Advance(10.5 * frame);
        Assert.True(!effects.IsHolding(BellatraEffect.Round) && effects.IsRunning(BellatraEffect.Start), "a key of ten frames ended before its eleventh");
        effects.Advance(frame);
        Assert.True(effects.IsHolding(BellatraEffect.Round) && !effects.IsRunning(BellatraEffect.Start),
            "a track did not reach its held key at its eleventh frame, or outlived its last");
        effects.Advance(5);
        Assert.True(effects.IsHolding(BellatraEffect.Round) && effects.IsRunning(BellatraEffect.Complete) && !effects.IsHolding(BellatraEffect.Complete),
            "a held key let its track go, or a looping track ended or held");
        effects.Stop(BellatraEffect.Round);
        Assert.True(!effects.IsRunning(BellatraEffect.Round) && effects.IsRunning(BellatraEffect.Complete), "a stopped effect still ran, or stopped another");
        Assert.Throws<InvalidDataException>(() => BellatraEffectTable.Read(data.AsSpan(1)), "a table of another size was read");
        return Task.CompletedTask;
    }

    private static Task ShowAsync()
    {
        var data = new byte[Tracks * TrackBytes + 40 * EffectBytes];
        Track(data, 0, (0, -2, 255));
        Effect(data, (int)BellatraEffect.CloseScreen, 0);
        Effect(data, (int)BellatraEffect.Loading, 0);
        var stage = new Stage();
        var show = new HudBellatraShow(BellatraEffectTable.Read(data)) { Stage = stage };

        show.Observe(BellatraBoardInfo.Hidden, Board(0, BellatraOutcome.Playing));
        Assert.True(show.Showing && stage.Cues.Count == 0 && stage.Music.Count == 0, "a ticket's arrival did not show the loading screen");
        show.Update(3.1);
        Assert.True(!show.Showing, "the loading screen outlived its three seconds");
        show.Observe(Board(0, BellatraOutcome.Playing), Board(1, BellatraOutcome.Playing));
        show.Update(2.9);
        Assert.True(stage.Cues.Count == 0, "a round's call came before three seconds");
        show.Update(0.2);
        Assert.True(stage.Take() is [EventCue.Round1] && stage.Music is ["BGM_CODE_SOD1"], "round 1's call or music did not come");
        show.Observe(Board(1, BellatraOutcome.Playing), BellatraBoardInfo.Hidden);
        show.Observe(BellatraBoardInfo.Hidden, Board(3, BellatraOutcome.Playing));
        show.Update(5);
        Assert.True(stage.Cues.Count == 0 && stage.Music[^1] == "BGM_CODE_SOD1" && !show.Showing, "coming back to a game in play replayed its round");

        show.Observe(Board(3, BellatraOutcome.Playing), Board(4, BellatraOutcome.GoesOn));
        Assert.True(stage.Take() is [EventCue.StageComplete] && stage.Music[^1] == "", "a team going on did not hear its fanfare over silence");
        show.Update(5.1);
        Assert.True(stage.Take() is [EventCue.Turn] && show.Showing, "the screen did not close at five seconds");
        show.Update(4);
        Assert.True(stage.Take() is [EventCue.Turn], "the screen did not open at nine seconds");
        show.Update(1);
        Assert.True(stage.Take() is [EventCue.Round4] && stage.Music[^1] == "BGM_CODE_SOD2", "round 4's call or music did not follow the opening");

        show.Observe(GameTextId.BellatraQuake);
        show.Observe(GameTextId.BellatraPoints);
        show.Observe(GameTextId.BellatraCleared);
        Assert.True(stage.Take() is [EventCue.Item, EventCue.Item] && stage.Quakes == 1, "a room's seal did not quake, or another seal or line did");

        show.Observe(Board(4, BellatraOutcome.GoesOn), Board(6, BellatraOutcome.Out));
        show.Update(6.1);
        Assert.True(stage.Take() is [EventCue.StageFail, EventCue.Turn], "a team out did not hear its fall, or the screen did not close at six seconds");
        show.Update(5);
        Assert.True(stage.Cues.Count == 0, "the screen of a team out opened before it left the arena");
        show.Observe(Board(6, BellatraOutcome.Out), BellatraBoardInfo.Hidden);
        Assert.True(stage.Take() is [EventCue.Turn], "leaving the arena did not open the closed screen");
        show.Update(10);
        Assert.True(stage.Cues.Count == 0, "what was still to come played after leaving the arena");

        show.Observe(BellatraBoardInfo.Hidden, Board(8, BellatraOutcome.Playing));
        show.Observe(Board(8, BellatraOutcome.Playing), Board(0, BellatraOutcome.Cleared));
        Assert.True(stage.Take() is [EventCue.Ending] && stage.Music[^1] == "", "the end did not play its ending over silence");
        return Task.CompletedTask;
    }

    private static BellatraBoardInfo Board(int round, BellatraOutcome outcome) => new(true, round, 1, 1_000, [500, 1_000, 200, 0], outcome);

    // A track's keys (kind 1, a texture): image, delay and alpha each.
    private static void Track(byte[] data, int index, params (int Image, int Delay, byte Alpha)[] keys)
    {
        var track = data.AsSpan(index * TrackBytes, TrackBytes);
        BitConverter.TryWriteBytes(track, 1);
        BitConverter.TryWriteBytes(track[4..], keys.Length);
        for (var key = 0; key < keys.Length; key++)
        {
            BitConverter.TryWriteBytes(track[(8 + key * 4)..], keys[key].Image);
            BitConverter.TryWriteBytes(track[(168 + key * 4)..], keys[key].Delay);
            track[1448 + key] = keys[key].Alpha;
        }
    }

    // A key's offset, growth and angle (z).
    private static void Key(byte[] data, int index, int key, (int X, int Y) offset, (int X, int Y) growth, int angle)
    {
        var track = data.AsSpan(index * TrackBytes, TrackBytes);
        BitConverter.TryWriteBytes(track[(328 + key * 8)..], offset.X);
        BitConverter.TryWriteBytes(track[(332 + key * 8)..], offset.Y);
        BitConverter.TryWriteBytes(track[(648 + key * 8)..], growth.X);
        BitConverter.TryWriteBytes(track[(652 + key * 8)..], growth.Y);
        BitConverter.TryWriteBytes(track[(968 + key * 12 + 8)..], angle);
    }

    // An effect of one layer on a track.
    private static void Effect(byte[] data, int index, int track, int layer = 0, (int X, int Y) origin = default, (int X, int Y) size = default)
    {
        var effect = data.AsSpan(Tracks * TrackBytes + index * EffectBytes, EffectBytes);
        BitConverter.TryWriteBytes(effect[4..], 1);
        BitConverter.TryWriteBytes(effect[8..], track);
        BitConverter.TryWriteBytes(effect[88..], layer);
        BitConverter.TryWriteBytes(effect[168..], origin.X);
        BitConverter.TryWriteBytes(effect[172..], origin.Y);
        BitConverter.TryWriteBytes(effect[328..], size.X);
        BitConverter.TryWriteBytes(effect[332..], size.Y);
    }

    private sealed class Stage : IBellatraStage
    {
        public List<EventCue> Cues { get; } = [];
        public List<string> Music { get; } = [];
        public int Quakes { get; private set; }

        public void Play(EventCue cue) => Cues.Add(cue);

        void IBellatraStage.Music(string code) => Music.Add(code);

        public void Quake() => Quakes++;

        public EventCue[] Take()
        {
            EventCue[] taken = [.. Cues];
            Cues.Clear();
            return taken;
        }
    }
}
