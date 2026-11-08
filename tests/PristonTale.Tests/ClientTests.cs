using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using PristonTale.Application.World;
using PristonTale.Client.Net;
using PristonTale.Client.Net.Online;
using PristonTale.Client.Net.Presence;
using PristonTale.Client.Net.WorldCatalog;
using PristonTale.Engine3D.Formats;
using PristonTale.Engine3D.Rendering;
using PristonTale.Gameplay.World.Remote;
using PristonTale.Protocol.Binary;
using PristonTale.Protocol.Packets;
using PristonTale.Simulation.Characters;
using PristonTale.Contracts.Motion;
using PristonTale.Contracts.World;
using PristonTale.Contracts.Combat;
using PristonTale.Contracts.Characters;

namespace PristonTale.Tests;

internal static class ClientTests
{
    public static IEnumerable<(string, Func<Task>)> Cases()
    {
        yield return ("original presence packets retain combat events, skill motion and field identity", PresenceAsync);
        yield return ("movement reports go out on change, at 5 Hz moving and 1 Hz standing, never twice within 50 ms", MovementPacingAsync);
        yield return ("crowded presence keeps position updates inside the per-receiver budget without starving walkers", CrowdBudgetAsync);
        yield return ("remote rates and explicit swing events replace per-frame restarts", RemoteMotionAsync);
        yield return ("NPC duplicate Stand clips preserve exact INX indices and weighted selection", NpcStandMotionsAsync);
        yield return ("pending actor preparation does not block presence or resurrect removals", PendingActorAsync);
        yield return ("remote height follows the server sample instead of the highest floor", ServerHeightAsync);
        yield return ("NPC, monster and player collision survives pending assets", CollisionRosterAsync);
        yield return ("an action field draws actors facing left or right, finds the nearest one ahead on the player's line and the player a walk bumps into, never one it walks away from", SideViewAsync);
        yield return ("cursor rays originate at the camera and reject an enclosing NPC pick volume", CursorPickingAsync);
    }

    private static Task MovementPacingAsync()
    {
        var pacer = new PristonTale.Gameplay.World.MovementReportPacer();
        pacer.SetField(3);
        Assert.True(pacer.TryTake(false, false, 0, 3), "the first report did not go out");
        Assert.True(!pacer.TryTake(true, false, 0, 3), "a change inside the 50 ms gap went out at once");
        pacer.Advance(0.05f);
        Assert.True(pacer.TryTake(true, false, 0, 3), "the change did not go out once the gap passed");
        pacer.Advance(0.1f);
        Assert.True(!pacer.TryTake(true, false, 0, 3), "an unchanged walk reported before 200 ms");
        pacer.Advance(0.11f);
        Assert.True(pacer.TryTake(true, false, 0, 3), "an unchanged walk did not report at 200 ms");
        pacer.Advance(0.06f);
        pacer.Request();
        Assert.True(pacer.TryTake(true, false, 0, 3), "a requested report did not go out after the gap");
        pacer.Advance(0.06f);
        Assert.True(pacer.TryTake(false, false, 0, 3), "a stop did not report");
        pacer.Advance(0.9f);
        Assert.True(!pacer.TryTake(false, false, 0, 3), "a standing player reported before a second");
        pacer.Advance(0.11f);
        Assert.True(pacer.TryTake(false, false, 0, 3), "a standing player did not report after a second");

        // A cast reports its position at once, inside the gap; the pacing does not send that state again.
        pacer.Advance(0.02f);
        pacer.Request();
        pacer.Taken(false, false, 0, 3);
        Assert.True(!pacer.TryTake(false, false, 0, 3), "the position reported ahead of a cast went out again");
        pacer.Advance(1.01f);
        Assert.True(pacer.TryTake(false, false, 0, 3), "the pacing did not resume after a cast's report");
        return Task.CompletedTask;
    }

    private static Task PresenceAsync()
    {
        var origin = new System.Numerics.Vector3(120, 35, -400);
        var impact = new CombatEventView(9, 1, CombatEventKind.MonsterSkillImpact,
            sourceSoundCode: 0x18E0, position: origin, facing: 1.2f);
        var entity = new EntityView(2, EntityKind.Monster, false, origin, 1.2f, MotionState.Skill,
            10, 45, 60, default, templateId: 1);
        var self = new EntityView(1, EntityKind.Player, false, origin, 0f, MotionState.Stand, 1, 100, 100,
            new PlayerAppearanceView("Self", PlayerClass.Mechanician, 0, 0, 0, "", ""));
        var snapshot = new WorldSnapshot(8, [self, entity], [impact], 3);
        var update = new WireRoundTrip(1).PushMerged(snapshot);
        Assert.True(update.FieldId == 3 && update.Spawns.Single(spawn => spawn.EntityId == 2).MoveState == MotionState.Skill,
            "TCP presence dropped field or skill motion");
        Assert.True(update.Spawns.Single(spawn => spawn.EntityId == 1).IsSelf, "own avatar was not marked as self");
        Assert.True(update.Events.Single().Position == origin && update.Events.Single().Kind == CombatEventKind.MonsterSkillImpact,
            "TCP presence dropped skill impact data");
        Assert.True(update.Spawns.All(spawn => spawn.ServerTimeMs == 8 * 50), "presence samples lost the server tick stamp");
        return Task.CompletedTask;
    }

    private static Task CrowdBudgetAsync()
    {
        // The largest crowd a receiver shows at once: the receiver, MaxVisiblePlayers other players and monsters to MaxVisible.
        EntityView Walker(int id, float offset) => id <= Server.Net.PresenceEncoder.MaxVisiblePlayers + 1
            ? new((uint)id, EntityKind.Player, false, new System.Numerics.Vector3(id + offset, 0, id % 20 * 10f), 0f, MotionState.Walk, 1, 100, 100,
                new PlayerAppearanceView("W" + id, PlayerClass.Mechanician, 0, 0, 0, "", ""))
            : new((uint)id, EntityKind.Monster, false, new System.Numerics.Vector3(id + offset, 0, id % 20 * 10f), 0f, MotionState.Walk, 1, 100, 100,
                default, templateId: 5);
        var crowd = Server.Net.PresenceEncoder.MaxVisible + 1;
        var wire = new WireRoundTrip(1);
        var spawned = wire.PushMerged(new WorldSnapshot(1, Enumerable.Range(1, crowd).Select(id => Walker(id, 0)).ToArray(), fieldId: 3));
        Assert.True(spawned.Spawns.Count == crowd, "spawns were held back by the movement budget");
        var updated = new HashSet<ulong>();
        for (uint tick = 2; tick < 42; tick++)
        {
            var before = wire.BytesSent;
            var update = wire.PushMerged(new WorldSnapshot(tick, Enumerable.Range(1, crowd).Select(id => Walker(id, tick)).ToArray(), fieldId: 3));
            // Budget for others, plus the receiver's own entry, at most two PLAYDATAGROUP headers and the tick's clock.
            Assert.True(wire.BytesSent - before <= 2048 + 84 + 2 * 16 + PresenceClockPacket.Size, "a crowd exceeded the per-receiver movement budget");
            updated.UnionWith(update.States.Select(state => state.EntityId));
        }

        Assert.True(updated.Count == crowd, "age weighting starved part of the crowd");
        return Task.CompletedTask;
    }

    private static Task RemoteMotionAsync()
    {
        var scene = new FakeScene();
        var remote = new RemoteModelEntity(
            scene,
            ActorHandle.None,
            1,
            0,
            Vector3.Zero,
            0,
            MotionState.Attack,
            100);
        var now = 1000d;
        void Settle()
        {
            for (var frame = 0; frame < 4; frame++)
            {
                now += 50;
                remote.Update(0.05f, now);
            }
        }

        var swing = new CombatEventInfo(2, 1, CombatEventKind.SwingBegin, 800, 400, 0, false, false);
        remote.SetRates(5, 1.75f);
        remote.ApplyCombatEvent(swing, now);
        Settle();
        Assert.True(scene.Restarts == 1, "swing did not restart exactly once");
        Assert.Near(1.75f, scene.LastRate);
        var commands = scene.Commands;
        for (var index = 0; index < 10; index++)
        {
            now += 16;
            remote.Update(0.016f, now);
        }

        Assert.True(scene.Commands == commands, "held Attack emitted per-frame commands");
        remote.ApplyCombatEvent(swing, now);
        Settle();
        Assert.True(scene.Restarts == 2, "second swing event was lost");
        remote.SetTarget(now, Vector3.Zero, 0, MotionState.Skill, 100);
        var cast = new CombatEventInfo(2, 1, CombatEventKind.SwingBegin, 1980, 957, 0, false, false);
        remote.ApplyCombatEvent(cast, now);
        Settle();
        Assert.True(scene.Restarts == 3 && remote.Motion == MotionCode.Skill, "skill entry restarted basic attack");
        remote.ApplyCombatEvent(cast, now);
        Settle();
        Assert.True(scene.Restarts == 4 && remote.Motion == MotionCode.Skill, "repeated skill did not restart its own motion");
        remote.SetTarget(now, Vector3.Zero, 0, MotionState.Walk, 100);
        Settle();
        Settle();
        Assert.True(remote.ShownMotion == MotionCode.Stand, "Walk without ground motion was shown walking");
        Assert.Near(1f, scene.LastRate);
        return Task.CompletedTask;
    }

    private static Task NpcStandMotionsAsync()
    {
        var motions = Enumerable.Range(0, 13)
            .Select(static _ => Motion(0, 0, 0))
            .ToArray();
        motions[10] = Motion((uint)MotionCode.Walk, 11, 41);
        motions[11] = Motion((uint)MotionCode.Stand, 50, 90);
        motions[12] = Motion((uint)MotionCode.Stand, 100, 160);
        var warehouseInfo = ModelInfo("TN-010.inx", motions);
        var cycle = new NpcStandMotionCycle(warehouseInfo, 91);
        var selected = new HashSet<int>();
        for (var index = 0; index < 32; index++)
        {
            var update = cycle.Update(2f, MotionCode.Stand);
            Assert.True(update.MotionChanged, "completed NPC Stand cycle did not select another concrete clip");
            Assert.True(update.MotionIndex is 11 or 12, "NPC idle selected Walk or an empty reserved INX slot");
            selected.Add(update.MotionIndex);
        }

        Assert.True(selected.SetEquals([11, 12]), "warehouse keeper never reached every authored Stand gesture");

        var weightedRates = new int[30];
        weightedRates[PristonFormatConstants.CharacterMotionBase] = 100;
        var weightedInfo = warehouseInfo with
        {
            NpcMotionRates = weightedRates,
            NpcMotionSelection = Enumerable.Repeat(12, PristonFormatConstants.MotionRateSelectionCount).ToArray()
        };
        var weighted = new NpcStandMotionCycle(weightedInfo, 17).Update(0f, MotionCode.Stand);
        Assert.True(weighted.MotionIndex == 12, "expanded Win32 NPC motion-rate table was ignored");

        var scene = new FakeScene();
        var npc = new RemoteModelEntity(
            scene,
            ActorHandle.None,
            1f,
            0,
            Vector3.Zero,
            0f,
            MotionState.Stand,
            1,
            EntityKind.Npc,
            modelInfo: warehouseInfo,
            entityId: 91);
        npc.Update(0f, 0);
        Assert.True(scene.ExactMotionIndices.Count == 1 && scene.ExactMotionIndices[0] is 11 or 12, "remote NPC collapsed its selected Stand entry back to the enum");
        return Task.CompletedTask;
    }

    private static PristonMotionInfo Motion(uint state, uint startFrame, uint endFrame)
    {
        return new PristonMotionInfo(state, startFrame, endFrame, [], 0, 0, 0, 1, 0, 0);
    }

    private static PristonModelInfo ModelInfo(string path, IReadOnlyList<PristonMotionInfo> motions)
    {
        var emptyGroup = new PristonModelGroup(0, []);
        return new PristonModelInfo(path, "fixture.smd", "fixture.smd", "", emptyGroup, emptyGroup, emptyGroup, motions, (uint)motions.Count, "", "", "", [], 0);
    }

    private static Task PendingActorAsync()
    {
        var scene = new FakeScene();
        var composer = new PendingComposer();
        var manager = new RemoteEntityManager(scene, composer, composer, new CharacterClassRegistry(), ".");
        var appearance = new PlayerAppearanceInfo(
            "remote",
            PlayerClass.Fighter,
            0,
            0,
            0,
            "",
            "",
            1);
        var spawn = new EntitySpawnInfo(
            2,
            EntityKind.Player,
            false,
            System.Numerics.Vector3.Zero,
            0,
            MotionState.Stand,
            100,
            100,
            appearance,
            0);
        manager.Apply(new PresenceUpdate(1, [spawn], [], [], []));
        Assert.True(manager.Count == 0 && composer.Composed == 0, "unprepared actor was composed");
        manager.Apply(new PresenceUpdate(2, [], [], [2], []));
        composer.Ready.TrySetResult();
        manager.Update(0.05f);
        Assert.True(manager.Count == 0 && composer.Composed == 0, "removed pending actor reappeared");
        return Task.CompletedTask;
    }

    private static Task SideViewAsync()
    {
        Assert.True(PristonTale.Gameplay.World.ActionFieldView.Facing(0.3f) == MathF.PI / 2f
            && PristonTale.Gameplay.World.ActionFieldView.Facing(-2.5f) == -MathF.PI / 2f
            && PristonTale.Gameplay.World.ActionFieldView.Facing(0f) == MathF.PI / 2f, "a facing did not turn to its side");
        var remote = new RemoteModelEntity(new FakeScene(), ActorHandle.None, 1, 0, Vector3.Zero, 2.8f, MotionState.Stand, 1) { SideView = true };
        remote.Update(0.05f, 50);
        Assert.True(remote.RenderFacing == MathF.PI / 2f, $"a side-view actor faces {remote.RenderFacing}");

        var composer = new PendingComposer();
        composer.Ready.TrySetResult();
        var manager = new RemoteEntityManager(new FakeScene(), composer, composer, new CharacterClassRegistry(), ".");
        var appearance = new PlayerAppearanceInfo("Remote", PlayerClass.Fighter, 0, 0, 0, "", "", 1);
        manager.Apply(new PresenceUpdate(1,
        [
            new(2, EntityKind.Player, false, new System.Numerics.Vector3(60, 0, 10), 0, MotionState.Stand, 100, 100, appearance, 0),
            new(3, EntityKind.Player, false, new System.Numerics.Vector3(30, 0, 40), 0, MotionState.Stand, 100, 100, appearance, 0),
            new(4, EntityKind.Player, false, new System.Numerics.Vector3(-20, 0, 0), 0, MotionState.Stand, 100, 100, appearance, 0)
        ], [], [], []));
        for (var frame = 0; frame < 4; frame++)
        {
            manager.Update(0.05f);
        }

        var origin = Vector3.Zero;
        Assert.True(manager.FindAhead(1, origin, facingRight: true, reach: 100, depth: 32, allies: true) == 2
            && manager.FindAhead(1, origin, facingRight: false, reach: 100, depth: 32, allies: true) == 4
            && manager.FindAhead(1, origin, facingRight: true, reach: 50, depth: 32, allies: true) == 0
            && manager.FindAhead(1, origin, facingRight: true, reach: 100, depth: 32, allies: false) == 0,
            "the search ahead took one off the line, behind, out of reach or of the wrong side");
        var behind = new Vector3(-6, 0, 0);
        Assert.True(manager.FindPlayerBumped(1, origin, new Vector3(-8, 0, 0), reach: 16, height: 16) == 4, "walking into a player did not bump it");
        Assert.True(manager.FindPlayerBumped(1, origin, new Vector3(8, 0, 0), reach: 16, height: 16) == 0, "a player out of reach was bumped");
        Assert.True(manager.FindPlayerBumped(1, new Vector3(-14, 0, 0), behind, reach: 16, height: 16) == 0, "walking away from a close player bumped it");
        Assert.True(manager.FindPlayerBumped(1, new Vector3(-21, 40, 0), new Vector3(-20, 40, 0), reach: 16, height: 16) == 0, "a player at another height was bumped");
        Assert.True(manager.FindPlayerBumped(4, origin, new Vector3(-8, 0, 0), reach: 16, height: 16) == 0, "a walk bumped its own player");
        return Task.CompletedTask;
    }

    private static Task ServerHeightAsync()
    {
        // A monster under a bridge: the server keeps it on the lower floor and the client must not lift it.
        var remote = new RemoteModelEntity(new FakeScene(), ActorHandle.None, 1, 0, new Vector3(0, 7, 0), 0, MotionState.Stand, 1);
        var now = 0d;
        for (var frame = 0; frame < 4; frame++)
        {
            now += 50;
            remote.Update(0.05f, now);
        }

        Assert.True(remote.DrawnPosition.Y == 7, "spawn height was replaced");
        remote.SetTarget(now, new Vector3(0.5f, -40, 0), 0, MotionState.Stand, 1);
        for (var frame = 0; frame < 8; frame++)
        {
            now += 50;
            remote.Update(0.05f, now);
        }

        Assert.True(remote.DrawnPosition.Y == -40 && remote.DrawnPosition.X == 0.5f, "remote height did not follow the server sample");
        return Task.CompletedTask;
    }

    private static Task CollisionRosterAsync()
    {
        var composer = new PendingComposer();
        var manager = new RemoteEntityManager(new FakeScene(), composer, composer, new CharacterClassRegistry(), ".");
        var appearance = new PlayerAppearanceInfo("Remote", PlayerClass.Fighter, 0, 0, 0, "", "", 1);
        var origin = new System.Numerics.Vector3(0, 7, 0);
        manager.Apply(new PresenceUpdate(1,
        [
            new(1, EntityKind.Player, true, origin, 0, MotionState.Stand, 100, 100, appearance, 0),
            new(2, EntityKind.Monster, false, origin + new System.Numerics.Vector3(40, 0, 0), 0, MotionState.Stand, 10, 10, null, 1),
            new(3, EntityKind.Npc, false, origin + new System.Numerics.Vector3(0, 0, 40), 0, MotionState.Stand, 1, 1, null, 2),
            new(4, EntityKind.Player, false, origin + new System.Numerics.Vector3(-25, 0, 0), 0, MotionState.Stand, 100, 100, appearance, 0)
        ], [], [], []));
        var neighbours = new List<PristonTale.Simulation.Physics.EntityNeighbour>();
        manager.CollectNeighbours(1, neighbours);
        Assert.True(manager.Count == 0 && neighbours.Count == 3, "asset preparation disabled authoritative blockers");
        foreach (var displacement in new[] { new System.Numerics.Vector3(30, 0, 0), new(0, 0, 30), new(-15, 0, 0) })
        {
            var accepted = PristonTale.Simulation.Physics.EntityCollision.Resolve(origin, origin + displacement, 16, neighbours);
            Assert.True(accepted == origin, "client predicted movement through a live entity");
        }

        composer.Ready.TrySetResult();
        manager.Update(0.1f);
        manager.CollectNeighbours(1, neighbours);
        Assert.True(neighbours.All(neighbour => neighbour.Position.Y == 7), "presentation leaked into authority collision space");
        manager.Apply(new PresenceUpdate(2, [], [], [2, 3], []));
        manager.CollectNeighbours(1, neighbours);
        Assert.True(neighbours.Count == 1, "removed pending entities left phantom blockers");
        manager.Clear();
        manager.CollectNeighbours(1, neighbours);
        Assert.True(neighbours.Count == 0, "field teardown retained collision state");
        return Task.CompletedTask;
    }

    private static Task CursorPickingAsync()
    {
        var viewport = new Viewport(0, 0, 800, 600);
        var camera = new Vector3(0, 0, 10);
        var view = Matrix.CreateLookAt(camera, Vector3.Zero, Vector3.Up);
        var projection = Matrix.CreatePerspectiveFieldOfView(MathHelper.PiOver2, viewport.AspectRatio, 1f, 100f);
        Assert.True(
            CursorPicking.TryCreateRay(viewport, projection, view, camera, new Point(400, 300), out var centerRay),
            "center cursor did not produce a ray");
        Assert.True(centerRay.Position == camera, "cursor ray started on the near plane instead of the camera");
        Assert.True(CursorPicking.IntersectSphere(centerRay, Vector3.Zero, 1f) is not null, "center cursor missed the visible entity");

        Assert.True(
            CursorPicking.TryCreateRay(viewport, projection, view, camera, new Point(20, 20), out var cornerRay),
            "corner cursor did not produce a ray");
        Assert.True(CursorPicking.IntersectSphere(cornerRay, Vector3.Zero, 1f) is null, "off-entity cursor selected the entity");

        var enclosingRay = new Ray(Vector3.Zero, Vector3.Forward);
        Assert.True(CursorPicking.IntersectSphere(enclosingRay, Vector3.Zero, 40f) is null, "camera inside an NPC volume made every cursor direction a hit");
        return Task.CompletedTask;
    }

    private sealed class PendingComposer : IPlayerActorComposer, IModelActorComposer
    {
        public TaskCompletionSource Ready { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int Composed { get; private set; }
        public Task PrepareAsync(string root, CharacterVisualSet visuals, int face) => Ready.Task;
        public Task PrepareAsync(string root, string model) => Ready.Task;
        public PlayerActorHandles Compose(string root, CharacterVisualSet visuals, int face, Matrix world, MotionCode motion)
        {
            Composed++;
            return new PlayerActorHandles(ActorHandle.None, ActorHandle.None);
        }

        public ComposedModelActor Compose(string root, string model, Matrix world, MotionCode motion)
        {
            Composed++;
            return new ComposedModelActor(ActorHandle.None, null);
        }
    }

    private sealed class FakeScene : IActorScene
    {
        public int Restarts { get; private set; }
        public int Commands { get; private set; }
        public float LastRate { get; private set; }
        public List<int> ExactMotionIndices { get; } = [];
        public ActorHandle AddActor(PristonActorInstance instance) => ActorHandle.None;
        public void SetActorWorld(ActorHandle handle, Matrix world)
        {
        }
        public void SetActorMotion(ActorHandle handle, MotionCode motion) => Commands++;
        public void SetActorMotionIndex(ActorHandle handle, int motionIndex)
        {
            ExactMotionIndices.Add(motionIndex);
            Commands++;
        }
        public void RestartActorMotion(ActorHandle handle, MotionCode motion)
        {
            Restarts++;
            Commands++;
        }

        public void SetActorPlaybackRate(ActorHandle handle, float rateFactor) => LastRate = rateFactor;
        public void RemoveActor(ActorHandle handle)
        {
        }
    }
}
