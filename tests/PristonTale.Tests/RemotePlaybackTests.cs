using Microsoft.Xna.Framework;
using PristonTale.Client.Net.Online;
using PristonTale.Client.Net.Presence;
using PristonTale.Client.Net.WorldCatalog;
using PristonTale.Engine3D.Formats;
using PristonTale.Engine3D.Rendering;
using PristonTale.Gameplay.UI;
using PristonTale.Gameplay.UI.Rendering;
using PristonTale.Gameplay.UI.Widgets;
using PristonTale.Gameplay.World;
using PristonTale.Gameplay.World.Remote;
using PristonTale.Protocol.Packets;
using PristonTale.Simulation.Characters;
using PristonTale.Simulation.World.Monsters;
using PristonTale.Contracts.Motion;
using PristonTale.Contracts.World;
using PristonTale.Contracts.Combat;
using PristonTale.Contracts.Characters;

namespace PristonTale.Tests;

internal static class RemotePlaybackTests
{
    public static IEnumerable<(string, Func<Task>)> Cases()
    {
        yield return ("remote snapshots interpolate position, server Y and speed on the server timeline", InterpolationAsync);
        yield return ("remote facing interpolates through the short arc across plus and minus pi", FacingWrapAsync);
        yield return ("remote samples snap when the authoritative error exceeds 256 units", SnapAsync);
        yield return ("remote extrapolation is capped at 250 ms and returns to the last sample", ExtrapolationAsync);
        yield return ("an entity that rested between samples waits at its place and walks off at its pace", RestAsync);
        yield return ("far-ring cadence raises the playback delay and the near ring restores it", CadenceAsync);
        yield return ("remote motion states become due when the render time reaches their sample", StateTimelineAsync);
        yield return ("remote clock follows early packets quickly, late packets slowly and resynchronises", ClockAsync);
        yield return ("remote Walk/Run without ground motion shows Stand and clip rate follows ground speed", LocomotionAsync);
        yield return ("remote player walk rate keeps the original frame step at nominal speed", PlayerRateAsync);
        yield return ("a remote monster killed mid-swing, mid-flinch or between samples shows its death clip", DeathAsync);
        yield return ("a model whose death clip is on its own sub model shows that model while it dies, as the mushroom does", AlternateModelAsync);
        yield return ("target frame follows the trace, keeps it after ground clicks and closes like the original", TargetSelectionAsync);
        yield return ("target HP bar is modern by default and classic only after a full-life attack", TargetHealthBarAsync);
        yield return ("presence clock extension round-trips and stamps decoded samples", PresenceClockAsync);
    }

    private static RemoteSample Sample(double time, float x, MotionState state = MotionState.Walk, float y = 0f, float facing = 0f) =>
        new(time, new Vector3(x, y, 0f), facing, state, 10);

    private static Task InterpolationAsync()
    {
        var buffer = new RemoteSnapshotBuffer(Sample(0, 0));
        Assert.True(!buffer.Add(Sample(200, 14, y: 10), Vector3.Zero), "a 14-unit step was treated as a snap");
        var pose = buffer.Sample(250);
        Assert.True(pose.Kind == RemotePoseKind.Interpolated, "render time between samples did not interpolate");
        Assert.Near(7f, pose.Position.X);
        Assert.Near(5f, pose.Position.Y);
        Assert.Near(70f, pose.PlanarSpeed, 0.01f);
        Assert.True(buffer.RenderTimeMs == 100, "playback delay is not the 150 ms base");
        return Task.CompletedTask;
    }

    private static Task FacingWrapAsync()
    {
        var buffer = new RemoteSnapshotBuffer(Sample(0, 0, facing: 3f));
        buffer.Add(Sample(200, 0, facing: -3f), Vector3.Zero);
        var facing = buffer.Sample(250).Facing;
        Assert.True(MathF.Abs(MathF.Abs(facing) - MathF.PI) < 1e-3f, $"facing crossed through zero instead of pi: {facing}");
        return Task.CompletedTask;
    }

    private static Task SnapAsync()
    {
        var buffer = new RemoteSnapshotBuffer(Sample(0, 0));
        Assert.True(buffer.Add(Sample(200, 300), Vector3.Zero), "a 300-unit jump was interpolated");
        Assert.True(buffer.Count == 1, "snap kept the previous history");
        Assert.Near(300f, buffer.Sample(250).Position.X);
        Assert.True(!buffer.Add(Sample(400, 310), new Vector3(300, 0, 0)), "a small step after a snap snapped again");
        Assert.True(buffer.Add(Sample(600, 320), new Vector3(0, 0, 0)), "a rendered error above 256 units did not snap");
        return Task.CompletedTask;
    }

    private static Task ExtrapolationAsync()
    {
        var buffer = new RemoteSnapshotBuffer(Sample(0, 0));
        buffer.Add(Sample(200, 14), Vector3.Zero);
        var lead = buffer.Sample(450);
        Assert.True(lead.Kind == RemotePoseKind.Extrapolated, "a walking entity without fresh samples froze");
        Assert.Near(21f, lead.Position.X, 0.001f);
        Assert.Near(70f, lead.PlanarSpeed, 0.01f);
        Assert.Near(31.5f, buffer.Sample(600).Position.X, 0.001f);
        var returning = buffer.Sample(650);
        Assert.Near(28f, returning.Position.X, 0.001f);
        Assert.True(returning.PlanarSpeed == 0f, "returning lead reported ground speed");
        var held = buffer.Sample(850);
        Assert.True(held.Kind == RemotePoseKind.Held && held.Position.X == 14f, "extrapolation stayed ahead of the last sample");

        var idle = new RemoteSnapshotBuffer(Sample(0, 0, MotionState.Stand));
        idle.Add(Sample(200, 14, MotionState.Stand), Vector3.Zero);
        Assert.True(idle.Sample(450).Position.X == 14f, "a standing sample was extrapolated");
        return Task.CompletedTask;
    }

    private static Task RestAsync()
    {
        // Standing since 0 (the server sends nothing meanwhile), then one tick of a 70 u/s walk reported at 3000.
        var buffer = new RemoteSnapshotBuffer(Sample(0, 0, MotionState.Stand));
        buffer.Add(Sample(3000, 3.5f), Vector3.Zero);
        var waiting = buffer.Sample(2900 + RemoteSnapshotBuffer.BaseDelayMs);
        Assert.True(waiting.Position.X == 0f && waiting.PlanarSpeed == 0f, $"the entity crept across its rest: {waiting.Position.X}");
        Assert.True(!buffer.TryTakeDueState(out _), "the rest replayed the state before it");
        var leaving = buffer.Sample(2975 + RemoteSnapshotBuffer.BaseDelayMs);
        Assert.Near(1.75f, leaving.Position.X, 0.001f);
        Assert.Near(70f, leaving.PlanarSpeed, 0.01f);
        var ahead = buffer.Sample(3100 + RemoteSnapshotBuffer.BaseDelayMs);
        Assert.True(ahead.Kind == RemotePoseKind.Extrapolated, "the walk stopped at its first sample");
        Assert.Near(10.5f, ahead.Position.X, 0.001f);
        Assert.True(buffer.TryTakeDueState(out var walk) && walk.MoveState == MotionState.Walk && !buffer.TryTakeDueState(out _),
            "the walk was not the one state due after the rest");
        return Task.CompletedTask;
    }

    private static Task CadenceAsync()
    {
        var buffer = new RemoteSnapshotBuffer(Sample(0, 0));
        for (var index = 1; index <= 4; index++)
        {
            buffer.Add(Sample(index * 500, index * 35), Vector3.Zero);
        }

        Assert.True(buffer.TargetDelayMs == 450, $"2 Hz cadence chose delay {buffer.TargetDelayMs}");
        buffer.Advance(2000);
        Assert.True(buffer.DelayMs == 450, "delay did not grow to the far-ring cadence");
        var pose = buffer.Sample(2400);
        Assert.True(pose.Kind == RemotePoseKind.Interpolated, "far-ring entity stopped between packets");
        Assert.Near(136.5f, pose.Position.X, 0.01f);

        for (var index = 1; index <= 4; index++)
        {
            buffer.Add(Sample(2000 + index * 200, 140 + index * 14), Vector3.Zero);
        }

        buffer.Advance(10000);
        Assert.True(buffer.DelayMs == RemoteSnapshotBuffer.BaseDelayMs, "near-ring cadence did not restore the base delay");
        return Task.CompletedTask;
    }

    private static Task StateTimelineAsync()
    {
        var buffer = new RemoteSnapshotBuffer(Sample(0, 0, MotionState.Stand));
        buffer.Add(Sample(1000, 0, MotionState.Attack), Vector3.Zero);
        buffer.Sample(1100);
        Assert.True(!buffer.TryTakeDueState(out _), "attack state played before its sample time");
        buffer.Sample(1150);
        Assert.True(buffer.TryTakeDueState(out var due) && due.MoveState == MotionState.Attack, "attack state never became due");
        Assert.True(!buffer.TryTakeDueState(out _), "a state was delivered twice");
        return Task.CompletedTask;
    }

    private static Task ClockAsync()
    {
        var clock = new RemoteServerClock();
        Assert.True(clock.Stamp(null) == 0 && !clock.Synchronised, "unsynchronised clock did not use local time");
        clock.Observe(5000);
        Assert.True(clock.NowMs == 5000, "first server time did not synchronise");
        clock.Advance(100);
        Assert.True(clock.NowMs == 5100, "clock did not advance with local time");
        clock.Observe(5200);
        Assert.True(clock.NowMs == 5125, $"early packet moved the clock to {clock.NowMs}");
        clock.Observe(5000);
        Assert.True(Math.Abs(clock.NowMs - 5122.5) < 1e-9, $"late packet moved the clock to {clock.NowMs}");
        clock.Observe(9000);
        Assert.True(clock.NowMs == 9000, "a jump above one second did not resynchronise");
        Assert.True(clock.Stamp(42) == 42 && clock.Stamp(null) == clock.NowMs, "sample stamps ignored the server time");
        return Task.CompletedTask;
    }

    private static Task LocomotionAsync()
    {
        var scene = new RecordingScene();
        var monster = new RemoteModelEntity(scene, ActorHandle.None, 1f, 0, Vector3.Zero, 0f, MotionState.Stand, 10);
        var now = 1000d;
        void Run(double milliseconds)
        {
            for (var elapsed = 0d; elapsed < milliseconds; elapsed += 50)
            {
                now += 50;
                monster.Update(0.05f, now);
            }
        }

        monster.SetTarget(now, Vector3.Zero, 0f, MotionState.Walk, 10);
        Run(400);
        Assert.True(monster.Motion == MotionCode.Walk && scene.LastMotion == MotionCode.Stand,
            "a Walk state without ground motion was shown walking");

        var x = 0f;
        void Move(int samples, float step, MotionState state)
        {
            for (var index = 0; index < samples; index++)
            {
                x += step;
                monster.SetTarget(now, new Vector3(x, 0, 0), 0f, state, 10);
                Run(200);
            }
        }

        Move(6, 14, MotionState.Walk);
        Assert.True(scene.LastMotion == MotionCode.Walk, "ground motion did not resume the walk clip");
        Assert.Near(1f, scene.LastRate, 0.02f);
        Move(6, 7, MotionState.Walk);
        Assert.Near(0.5f, scene.LastRate, 0.02f);
        Move(6, 28, MotionState.Run);
        Assert.True(scene.LastMotion == MotionCode.Run, "run state was not shown");
        Assert.Near(1f, scene.LastRate, 0.02f);
        monster.SetTarget(now, new Vector3(x, 0, 0), 0f, MotionState.Attack, 10);
        Run(400);
        Assert.True(scene.LastMotion == MotionCode.Attack, "server attack state lost to the locomotion safeguard");
        return Task.CompletedTask;
    }

    private static Task DeathAsync()
    {
        foreach (var (name, states, swing) in new (string, MotionState[], bool)[]
        {
            ("mid-swing", [MotionState.Attack, MotionState.Dead], true),
            ("mid-skill", [MotionState.Skill, MotionState.Dead], true),
            ("mid-flinch", [MotionState.Damage, MotionState.Dead], false),
            ("walking", [MotionState.Walk, MotionState.Dead], false),
            ("one frame", [MotionState.Damage, MotionState.Dead], false)
        })
        {
            var scene = new RecordingScene();
            var monster = new RemoteModelEntity(scene, ActorHandle.None, 1f, 0, Vector3.Zero, 0f, MotionState.Stand, 10);
            var now = 1000d;
            var step = name == "one frame" ? 0d : 50d;
            foreach (var state in states)
            {
                monster.SetTarget(now, Vector3.Zero, 0f, state, state == MotionState.Dead ? 0 : 5);
                if (swing && state != MotionState.Dead)
                {
                    monster.ApplyCombatEvent(new CombatEventInfo(1, 2, CombatEventKind.SwingBegin, 800f, 300f, 0, false, false) { Clip = 3 }, now);
                }

                now += step;
            }

            monster.ApplyCombatEvent(new CombatEventInfo(2, 1, CombatEventKind.Hit, 0f, 0f, 9, false, true), now);
            for (var tick = 0; tick < 20; tick++)
            {
                now += 50;
                monster.Update(0.05f, now);
            }

            Assert.True(monster.Motion == MotionCode.Dead && scene.LastMotion == MotionCode.Dead, $"death clip not shown {name}: {scene.LastMotion}");
        }

        return Task.CompletedTask;
    }

    private static Task PlayerRateAsync()
    {
        var scene = new RecordingScene();
        var player = new RemoteEntity(scene, new PlayerActorHandles(ActorHandle.None, ActorHandle.None), 0, Vector3.Zero, 0f, MotionState.Walk, 10);
        player.SetRates(1, 1f);
        var speed = Locomotion.WalkUnitsPerSecond(1, inVillage: false);
        var now = 0d;
        for (var index = 1; index <= 8; index++)
        {
            player.SetTarget(index * 200, new Vector3(speed * index * 0.2f, 0, 0), 0f, MotionState.Walk, 10);
            for (var frame = 0; frame < 4; frame++)
            {
                now += 50;
                player.Update(0.05f, now);
            }
        }

        Assert.True(scene.LastMotion == MotionCode.Walk, "walking player was not shown walking");
        Assert.Near(AnimationRate.MoveRate(Locomotion.BaseTranslation(1)), scene.LastRate, 0.01f);
        return Task.CompletedTask;
    }

    private static Task TargetSelectionAsync()
    {
        var live = new Dictionary<ulong, TargetLiveness> { [2] = TargetLiveness.Alive, [3] = TargetLiveness.Alive, [4] = TargetLiveness.Alive };
        TargetLiveness Query(ulong id) => live.TryGetValue(id, out var state) ? state : TargetLiveness.Missing;
        var selection = new TargetSelection();
        selection.Trace(2);
        selection.Update(0, Query);
        Assert.True(selection.FrameId == 2, "clicked monster did not open the frame");
        selection.Hover(3);
        selection.Update(10, Query);
        Assert.True(selection.FrameId == 2, "hover replaced a traced target");
        selection.Hover(0);
        selection.ClearTrace();
        selection.Update(20, Query);
        Assert.True(selection.FrameId == 2, "a ground click closed the frame");
        selection.Hover(3);
        selection.Update(30, Query);
        Assert.True(selection.FrameId == 3, "hover did not replace the frame without a trace");
        selection.Hover(0);
        selection.Update(40, Query);
        Assert.True(selection.FrameId == 3, "leaving the hovered entity closed the frame");
        selection.Trace(4);
        selection.Update(50, Query);
        Assert.True(selection.FrameId == 4, "clicked NPC did not open the frame");

        live[4] = TargetLiveness.Dead;
        selection.Update(100, Query);
        Assert.True(selection.FrameId == 4 && selection.TracedId == 0, "death closed the frame at once or kept the trace");
        selection.Update(100 + 1142, Query);
        Assert.True(selection.FrameId == 4, "frame closed before the 0x50-frame death delay");
        selection.Update(100 + 1143, Query);
        Assert.True(selection.FrameId == 0, "dead target kept the frame after its death delay");

        selection.Trace(2);
        selection.Update(1300, Query);
        live.Remove(2);
        selection.Update(1310, Query);
        Assert.True(selection.FrameId == 0 && selection.TracedId == 0, "removed target kept the frame");
        live[3] = TargetLiveness.Dead;
        selection.Hover(3);
        selection.Update(1320, Query);
        Assert.True(selection.FrameId == 0, "hovering a corpse opened the frame");
        selection.Trace(3);
        selection.Clear();
        Assert.True(selection.FrameId == 0 && selection.TracedId == 0 && selection.HoveredId == 0, "clear kept selection state");
        return Task.CompletedTask;
    }

    private static Task TargetHealthBarAsync()
    {
        var combat = new HudCombat();
        var appearance = new PlayerAppearanceInfo("Self", PlayerClass.Fighter, 0, 0, 0, "", "", 1);
        combat.Observe(new PresenceUpdate(1,
        [
            new(1, EntityKind.Player, true, default, 0, MotionState.Stand, 64, 64, appearance, 0),
            new(2, EntityKind.Monster, false, default, 0, MotionState.Stand, 50, 50, null, 1, Name: "Hopy", Level: 3),
            new(3, EntityKind.Monster, false, default, 0, MotionState.Stand, 20, 50, null, 1),
            new(4, EntityKind.Npc, false, default, 0, MotionState.Stand, 100, 100, null, 7)
        ], [], [],
        [
            new(1, 2, CombatEventKind.SwingBegin, 800, 400, 0, false, false),
            new(1, 3, CombatEventKind.SwingBegin, 800, 400, 0, false, false)
        ]));
        Assert.True(combat.ShowsHealthBar(2, classic: false) && combat.ShowsHealthBar(3, classic: false), "modern mode hid a monster bar");
        Assert.True(combat.ShowsHealthBar(2, classic: true), "classic mode hid the bar after a full-life attack");
        Assert.True(!combat.ShowsHealthBar(3, classic: true), "classic mode showed a bar for a monster someone else engaged");
        Assert.True(!combat.ShowsHealthBar(4, classic: false), "an NPC showed a life bar although the server sends its life");
        Assert.True(HudCombat.HealthPercent(1, 1000) == 1 && HudCombat.HealthPercent(0, 10) == 0, "percentage hid a living target or showed a dead one");
        Assert.True(HudCombat.HealthPercent(500, 1000) == 50 && HudCombat.HealthPercent(1000, 1000) == 100, "percentage formula drifted");
        foreach (var size in new[] { new Point(800, 600), new Point(1920, 1080), new Point(3840, 2160) })
        {
            var layout = HudLayout.Create(size.X, size.Y);
            var frame = HudCombat.TargetFrame(layout);
            var viewport = new Rectangle(0, 0, layout.Width, layout.Height);
            Assert.True(frame.Box.Contains(frame.Portrait) && viewport.Contains(frame.HealthBar), "target frame regions escaped their bounds");
            Assert.True(frame.StatsY >= frame.HealthBar.Bottom && frame.NameY < frame.Box.Bottom, "target frame lines overlap the bar");
            Assert.True(frame.Box.Contains(frame.Brood) && Math.Abs(frame.Box.Right - 8 - frame.Brood.Right) <= 1 && frame.Brood.Y == frame.Box.Y + 8,
                "the race icon left the frame's top right corner");
        }

        // The portrait camera: level with the head bone plus the correction and three units, 100 units plus that height away.
        var world = Matrix.CreateTranslation(100f, 5f, 0f);
        var portrait = new Rectangle(0, 0, 102, 102);
        var camera = HudTargetPortrait.Camera(new PristonPortraitAnchor(new Vector3(0f, 40f, 2f), true, 50f, default, world), world,
            new HudPortraitSubject(2, 8, 25), portrait, 600);
        Assert.True(Vector3.Distance(camera.Target, new Vector3(100f, 56f, 2f)) < 1e-3f
            && Vector3.Distance(camera.Position, new Vector3(100f, 56f, 175f)) < 1e-3f, "the portrait camera left the head");
        var focal = 300f / MathF.Tan(HudTargetPortrait.ScreenFieldOfView * 0.5f);
        Assert.True(MathF.Abs(camera.FieldOfView - 2f * MathF.Atan(51f / (focal * HudTargetPortrait.CloseUp))) < 1e-5f && camera.AspectRatio == 1f,
            "the portrait projection is not the screen's actor projection cropped to the frame and narrowed to the close-up");
        var boneless = HudTargetPortrait.Camera(new PristonPortraitAnchor(Vector3.Zero, false, 24f, default, Matrix.Identity), Matrix.Identity,
            new HudPortraitSubject(2, 0, 0), portrait, 600);
        Assert.True(MathF.Abs(boneless.Target.Y - 17f) < 1e-3f && MathF.Abs(boneless.Position.Z - 114f) < 1e-3f,
            "a model without a head bone was not framed ten units below its authored height");
        var scaled = HudTargetPortrait.Camera(new PristonPortraitAnchor(new Vector3(0f, 40f, 0f), true, 50f, default, Matrix.Identity),
            Matrix.CreateScale(2f), new HudPortraitSubject(2, 0, 0), portrait, 600);
        Assert.True(MathF.Abs(scaled.Position.Z - 280f) < 1e-3f && MathF.Abs(scaled.FieldOfView - camera.FieldOfView) < 1e-6f,
            "a scaled model was not framed like its natural size");
        Assert.True(combat.TryGetPortraitSubject(2, out var subject) && subject.EntityId == 2 && !combat.TryGetPortraitSubject(99, out _),
            "the portrait subject did not follow the roster");
        Assert.True(HudCombat.BroodIcon(MonsterBrood.Undead) == HudArt.BroodUndead && HudCombat.BroodIcon(MonsterBrood.User) is null,
            "the race icons drifted");
        var npcs = new List<Vector3>();
        combat.CollectNpcPositions(npcs);
        Assert.True(npcs.Count == 1, "the minimap NPC list did not hold the NPC alone");

        return Task.CompletedTask;
    }

    private static Task PresenceClockAsync()
    {
        var buffer = new byte[PresenceClockPacket.Size];
        Assert.True(new PresenceClockPacket(1234, 50).Write(buffer) == PresenceClockPacket.Size, "presence clock size drifted");
        var read = PresenceClockPacket.Read(buffer);
        Assert.True(read.ServerTick == 1234 && read.TickIntervalMs == 50 && read.ServerTimeMs == 61700, "presence clock fields did not round-trip");
        var rejected = (byte[])buffer.Clone();
        rejected[8] = 0;
        rejected[9] = 0;
        var threw = false;
        try
        {
            PresenceClockPacket.Read(rejected);
        }
        catch (InvalidDataException)
        {
            threw = true;
        }

        Assert.True(threw, "presence clock version 0 was accepted");

        var decoder = new PresenceDecoder(new WorldRenderCatalog(), static _ => { });
        Assert.True(decoder.TryHandle(buffer, out var none) && none is null, "presence clock was not consumed by the decoder");
        Assert.True(decoder.ServerTimeMs == 61700, "decoder ignored the presence clock");
        return Task.CompletedTask;
    }

    private static Task AlternateModelAsync()
    {
        var root = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "assets");
        var mushroom = Path.Combine(root, "char", "monster", "Monmushroom", "Monmushroom.inx");
        if (!PristonTale.Assets.AssetFiles.Exists(mushroom))
        {
            return Task.CompletedTask;
        }

        var reader = new PristonInxReader();
        var main = reader.Read(mushroom);
        var alternatePath = AlternateModel.Path(root, main);
        Assert.True(alternatePath is not null, "the mushroom's sub model was not found");
        var alternate = reader.Read(alternatePath!);
        Assert.True(AlternateModel.Alternates(main, alternate) && AlternateModel.Shows(main, alternate, MotionCode.Dead)
            && !AlternateModel.Shows(main, alternate, MotionCode.Stand) && !AlternateModel.Shows(main, alternate, MotionCode.Attack),
            "only the death plays on the mushroom's sub model");

        var scene = new AlternateScene();
        var body = new ActorHandle(1, 1);
        var death = new ActorHandle(2, 1);
        var monster = new RemoteModelEntity(scene, body, 1f, 0, Vector3.Zero, 0f, MotionState.Stand, 10, modelInfo: main, alternate: death,
            alternateInfo: alternate);
        Assert.True(monster.Actor == body && !scene.Hidden.ContainsKey(body), "a living mushroom shows its body");
        var now = 1000d;
        monster.SetTarget(now, Vector3.Zero, 0f, MotionState.Dead, 0);
        for (var step = 0; step < 20; step++)
        {
            now += 50;
            monster.Update(0.05f, now);
        }

        Assert.True(monster.Actor == death && scene.Hidden[body] && !scene.Hidden[death] && scene.Motions[death] == MotionCode.Dead,
            "a dying mushroom shows its death model playing the death clip");
        monster.Dispose();
        Assert.True(scene.Removed.SetEquals([body, death]), "both models leave with the monster");
        return Task.CompletedTask;
    }

    private sealed class AlternateScene : IActorScene
    {
        public Dictionary<ActorHandle, bool> Hidden { get; } = [];
        public Dictionary<ActorHandle, MotionCode> Motions { get; } = [];
        public HashSet<ActorHandle> Removed { get; } = [];
        public ActorHandle AddActor(PristonActorInstance instance) => ActorHandle.None;
        public void SetActorWorld(ActorHandle handle, Matrix world)
        {
        }

        public void SetActorMotion(ActorHandle handle, MotionCode motion) => Motions[handle] = motion;
        public void SetActorMotionIndex(ActorHandle handle, int motionIndex)
        {
        }

        public void RestartActorMotion(ActorHandle handle, MotionCode motion) => Motions[handle] = motion;
        public void SetActorPlaybackRate(ActorHandle handle, float rateFactor)
        {
        }

        public void SetActorHidden(ActorHandle handle, bool hidden) => Hidden[handle] = hidden;
        public void RemoveActor(ActorHandle handle) => Removed.Add(handle);
    }

    private sealed class RecordingScene : IActorScene
    {
        public MotionCode LastMotion { get; private set; } = MotionCode.Stand;
        public float LastRate { get; private set; } = 1f;
        public ActorHandle AddActor(PristonActorInstance instance) => ActorHandle.None;
        public void SetActorWorld(ActorHandle handle, Matrix world)
        {
        }

        public void SetActorMotion(ActorHandle handle, MotionCode motion) => LastMotion = motion;
        public void SetActorMotionIndex(ActorHandle handle, int motionIndex)
        {
        }

        public void RestartActorMotion(ActorHandle handle, MotionCode motion) => LastMotion = motion;
        public void SetActorPlaybackRate(ActorHandle handle, float rateFactor) => LastRate = rateFactor;
        public void RemoveActor(ActorHandle handle)
        {
        }
    }
}
