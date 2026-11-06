using System.Numerics;
using PristonTale.Application.World;
using PristonTale.Simulation.World.Monsters;
using PristonTale.Client.Net.Presence;
using PristonTale.Protocol.Packets;
using PristonTale.Server.Net;
using PristonTale.Simulation.Characters;
using PristonTale.Simulation.Items;
using PristonTale.Simulation.Modifiers;
using PristonTale.Contracts.Motion;
using PristonTale.Contracts.World;
using PristonTale.Contracts.Combat;
using PristonTale.Contracts.Skills;
using PristonTale.Contracts.Characters;

namespace PristonTale.Tests;

// The shared presence pipeline: frames reused between ticks, entries encoded once for every receiver, field-wide
// descriptor revisions, the crowd cap, per-tick event segments, the grid and the running-status book.
internal static class PresencePipelineTests
{
    public static IEnumerable<(string, Func<Task>)> Cases()
    {
        yield return ("presence frames are reused only when no reader pins them and never while an outbox can still hand them out", FramesAsync);
        yield return ("a description encoded once for all receivers is byte for byte what one receiver would encode", SharedWireAsync);
        yield return ("a new large frame describing one entity takes one pooled buffer, not one for every entity", SharedWireBuffersAsync);
        yield return ("a receiver reading another language's names never shares the frame's descriptions", LanguageDescriptionsAsync);
        yield return ("events routed by the entities a receiver knows are the events a full scan finds, in the field's order", RoutedEventsAsync);
        yield return ("presence entries carry handles, 64-bit ids and sequences, 32-bit life, only the values that changed and world-wide positions at an eighth of a unit, whole or as a change",
            EntriesAsync);
        yield return ("descriptor revisions are field-wide and rise only when what a description shows changes", RevisionsAsync);
        yield return ("a receiver in a crowd shows its nearest players up to their cap, monsters to the rest, and never loses its NPCs, target, attackers, summons or party",
            CrowdCapAsync);
        yield return ("events of ticks a slow reader fell behind on keep their own tick on the timeline", SegmentsAsync);
        yield return ("a receiver gets descriptions pushed ahead of spawns, positions as changes that never drift, and handles given again only after their removal",
            HandlesAsync);
        yield return ("a shown entity's new max life and description refresh it, and a lost pushed description is asked for", RefreshAsync);
        yield return ("the entity grid finds, counts and relates entities on both sides of zero", GridAsync);
        yield return ("running statuses are kept per field, carried across fields on the new clock and pruned when they end", StatusBookAsync);
        yield return ("a receiver whose events were lost gets the statuses running on every entity it shows again", LostEventsAsync);
    }

    private static Task FramesAsync()
    {
        var frames = new FieldPresenceFrames();
        var first = frames.Rent(3);
        var generation = first.Generation;
        frames.Published(first);
        Assert.True(first.TryPin(generation, 7), "a published frame refused a reader");
        var second = frames.Rent(3);
        Assert.True(!ReferenceEquals(first, second), "the latest published frame was reused");
        frames.Published(second);
        var third = frames.Rent(4);
        Assert.True(!ReferenceEquals(third, first) && third.Views.Length == 4, "a pinned frame was reused");
        frames.Published(third);
        first.Release(7);
        var reused = frames.Rent(3);
        Assert.True(ReferenceEquals(reused, first) && first.Generation == generation + 1, "an unpinned old frame was not reused");
        Assert.True(!first.TryPin(generation, 9), "a reader pinned a reused frame under its old generation");
        frames.Published(reused);
        Assert.True(first.TryPin(first.Generation, 9), "the reused frame refused its new readers");
        first.Release(9);
        return Task.CompletedTask;
    }

    private static Task RoutedEventsAsync()
    {
        static EntityView At(ulong id, float x) =>
            new(id, id == 1 ? EntityKind.Player : EntityKind.Monster, false, new Vector3(x, 0, 0), 0f, MotionState.Stand, 1, 100, 100, default);
        EntityView[] views = [At(1, 0), At(10, 20), At(11, 40), At(12, 60), At(98, 50_000), At(99, 50_020)];
        // Far fights the receiver never sees, with its own monsters' events among them.
        var combat = Enumerable.Range(0, 40).Select(index => new CombatEventView(99, 98, CombatEventKind.Hit, damage: index)).ToList();
        combat.Insert(5, new CombatEventView(10, 1, CombatEventKind.Hit, damage: 1000));
        combat.Insert(20, new CombatEventView(11, 12, CombatEventKind.Hit, damage: 2000));
        combat.Add(new CombatEventView(1, 1, CombatEventKind.Heal, damage: 3000));
        var events = combat.ToArray();
        StatusEventView[] statuses = [new(98, 99, 7, StatusEffectOp.Add, 1, 0, 0, false, false, 1000, 1000, 0), new(12, 11, 8, StatusEffectOp.Add, 1, 0, 0, false, false, 1000, 1000, 0)];
        var frame = new PresenceFrame(views.Length);
        views.CopyTo(frame.Views, 0);
        frame.Grid.Rebuild(frame.Views);
        frame.Routes.Rebuild(events, [], statuses, []);
        var routed = new WireRoundTrip(1).PushMerged(new WorldSnapshot(5, frame.Views, events, 3) { Grid = frame.Grid, Frame = frame, StatusEvents = statuses });
        var scanned = new WireRoundTrip(1).PushMerged(new WorldSnapshot(5, views, events, 3) { StatusEvents = statuses });
        Assert.True(routed.Events.Select(item => item.Damage).SequenceEqual([1000, 2000, 3000]), $"routed {string.Join(",", routed.Events.Select(item => item.Damage))}");
        Assert.True(scanned.Events.Select(item => item.Damage).SequenceEqual([1000, 2000, 3000]), $"scanned {string.Join(",", scanned.Events.Select(item => item.Damage))}");
        Assert.True(routed.StatusEffects.Select(item => item.StatusCode).SequenceEqual([8u]) && scanned.StatusEffects.Select(item => item.StatusCode).SequenceEqual([8u]),
            "a status of an unseen entity was routed, or a seen one was lost");
        return Task.CompletedTask;
    }

    private static Task LanguageDescriptionsAsync()
    {
        var english = new WorldDataHolder { Current = new WorldData(WorldData.Empty.Items, new MonsterCatalog([ChatFixture.Hopy]), WorldData.Empty.Npcs,
            WorldData.Empty.FieldSpawns, WorldData.Empty.Motions) };
        var localized = new WorldDataLanguagesHolder();
        localized.Set("en", english.Current);
        localized.Set("pt-BR", new WorldData(WorldData.Empty.Items, new MonsterCatalog([ChatFixture.Hopy with { Name = "Hopi" }]), WorldData.Empty.Npcs,
            WorldData.Empty.FieldSpawns, WorldData.Empty.Motions));
        EntityView[] views =
        [
            new(1, EntityKind.Player, false, Vector3.Zero, 0f, MotionState.Stand, 1, 100, 100, default),
            new(10, EntityKind.Monster, false, new Vector3(30, 0, 0), 0f, MotionState.Stand, 4, 50, 50, default, templateId: 1) { Revision = 1 }
        ];
        var frame = new PresenceFrame(views.Length);
        views.CopyTo(frame.Views, 0);
        frame.Grid.Rebuild(frame.Views);
        frame.Routes.Rebuild([], [], [], []);
        var snapshot = new WorldSnapshot(5, frame.Views, [], 3) { Grid = frame.Grid, Frame = frame };
        string Seen(string language) => new WireRoundTrip(1, english, localized, language).PushMerged(snapshot).Spawns.Single(spawn => spawn.EntityId == 10).Name;
        var portuguese = Seen("pt-BR");
        var englishName = Seen("en");
        Assert.True(portuguese == "Hopi" && englishName == "Hopy", $"pt-BR saw {portuguese} and English {englishName}");
        return Task.CompletedTask;
    }

    // A field that cannot reuse its frames (encoders holding them all) makes a new frame each tick.
    private static Task SharedWireBuffersAsync()
    {
        const int crowd = 200;
        const int maxFrameBytes = 16 * 1024;
        var view = new EntityView(42, EntityKind.Monster, false, Vector3.Zero, 0f, MotionState.Stand, 30, 77, 120, default, templateId: 9) { Revision = 3 };
        var first = SharedPresenceWire.Create(crowd).Description(7, view, WorldData.Empty).ToArray();
        var before = GC.GetAllocatedBytesForCurrentThread();
        var wire = SharedPresenceWire.Create(crowd);
        var again = wire.Description(7, view, WorldData.Empty);
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.True(again.SequenceEqual(first) && allocated < maxFrameBytes, $"a new frame of {crowd} describing one entity allocated {allocated} bytes");
        wire.Reset(crowd);
        return Task.CompletedTask;
    }

    private static Task SharedWireAsync()
    {
        var view = new EntityView(42, EntityKind.Monster, false, new Vector3(-1234.5f, 17f, 999.25f), 1.1f, MotionState.Walk, 30, 77, 120, default,
            templateId: 9) { Revision = 3 };
        var wire = SharedPresenceWire.Create(4);
        var description = new byte[EntityDescriptionPacket.MaximumSize];
        var described = PresenceEncoder.DescriptionPacket(view, view.Revision, WorldData.Empty).Write(description);
        Assert.True(wire.Description(1, view, WorldData.Empty).SequenceEqual(description.AsSpan(0, described)), "the shared description differs");
        Assert.True(wire.Description(1, view, WorldData.Empty).SequenceEqual(description.AsSpan(0, described)), "a second receiver got other bytes");
        wire.Reset(4);
        var renamed = view with { Revision = 4 };
        described = PresenceEncoder.DescriptionPacket(renamed, renamed.Revision, WorldData.Empty).Write(description);
        Assert.True(wire.Description(1, renamed, WorldData.Empty).SequenceEqual(description.AsSpan(0, described)), "a reset frame kept the previous tick's description");
        return Task.CompletedTask;
    }

    private static Task EntriesAsync()
    {
        var spawn = new PresenceEntry(300, MotionState.Skill, PresencePoint.From(new Vector3(-999_999.875f, 1234.5f, 999_999.875f)),
            PresenceEntry.ToFacing(4.71f), true, ulong.MaxValue - 1, PresenceFields.All, 100_000, 2_000_000, 0x4000_0001, ulong.MaxValue - 3, 255,
            0x3020 | PresenceWire.NpcTemplate, -1);
        var step = new PresenceEntry(300, MotionState.Run, new PresencePoint(81, -24, 60), PresenceEntry.ToFacing(0.5f), Absolute: false);
        var hit = new PresenceEntry(300, MotionState.Damage, default, 0, Absolute: false, Fields: PresenceFields.Life, Life: 99_000);
        var buffer = new byte[PresenceWire.HeaderSize + PresenceWire.MaxEntrySize * 3];
        var writer = new PresenceEntriesWriter(buffer, 77, 50);
        var sizes = new List<int>();
        foreach (var entry in new[] { spawn, step, hit })
        {
            sizes.Add(PresenceWire.Write(writer.Reserve(PresenceWire.MaxEntrySize), entry));
            writer.Commit(sizes[^1]);
        }

        var message = writer.Finish();
        var read = new List<PresenceEntry>();
        PresenceEntriesReader.Read(message, (in PresenceEntry value) => read.Add(value));
        Assert.True(PresenceEntriesReader.Clock(message) == new PresenceClockPacket(77, 50) && read is [var back, var moved, var struck]
            && back == spawn && moved == step && struck == hit && sizes[0] <= PresenceWire.MaxEntrySize,
            $"the presence message did not round-trip: {string.Join(", ", read)}");
        Assert.True(sizes[1] == PresenceWire.MotionSize(300, step.Position, false) && sizes[1] == 8 && sizes[2] == 2 + 1 + 3 + 1 + 1 + 3,
            $"a step or a hit took more bytes than its change: {sizes[1]}, {sizes[2]}");
        Assert.True(Vector3.Distance(PresencePoint.From(new Vector3(-999_999.875f, 1234.5f, 999_999.875f)).ToWorld(), new Vector3(-999_999.875f, 1234.5f, 999_999.875f)) <= 0.0625f
            && MathF.Abs(read[0].FacingRadians - 4.71f) < MathF.PI / 256f, "positions or facing lost more than the wire's precision");
        Assert.True(PresenceWire.PrefersAbsolute(new PresencePoint(1 << 20, 1 << 20, 0)) && !PresenceWire.PrefersAbsolute(new PresencePoint(500, 3, -500)),
            "a large change was not sent whole, or a small one was");
        foreach (var value in new ulong[] { 0, 127, 128, 16_383, 16_384, uint.MaxValue, ulong.MaxValue })
        {
            var bytes = new byte[VarInt.MaxBytes64];
            var size = VarInt.Write(bytes, value);
            var offset = 0;
            Assert.True(size == VarInt.Size(value) && VarInt.Read(bytes, ref offset) == value && offset == size, $"varint {value} did not round-trip");
        }

        Assert.True(VarInt.UnZigZag(VarInt.ZigZag(-1)) == -1 && VarInt.UnZigZag(VarInt.ZigZag(int.MinValue)) == int.MinValue, "zigzag lost the sign");

        var buffer2 = new byte[EntityDescriptionPacket.MaximumSize];
        var player = new EntityDescriptionPacket(7, 4, EntityKind.Player, 285, 3, 1.25f, "Ário Ñandú", "char/tmABCD/b001.ini",
            new DescribedPlayer(PlayerClass.Mechanician, 3, 1, 4, "char/tmABCD/tmh-b01.inf", 0x01020304, 0x02030405, 0x03040506, true, true, 5, 2));
        var length2 = player.Write(buffer2);
        var readPlayer = EntityDescriptionPacket.Read(buffer2.AsSpan(0, length2));
        Assert.True(readPlayer == player with { Player = readPlayer.Player } && readPlayer.Player == player.Player, "a player description did not round-trip");
        var summon = new EntityDescriptionPacket(0x4000_0002, 9, EntityKind.Monster, 70, 3, 1f, "Metal Golem", "char/monster/Metalgolem/metalgolem.inx",
            Template: new DescribedTemplate(0x3020, 1f, 0x3020, -8, 200, 4, uint.MaxValue + 5UL, 1.5f, 0, 1, -1));
        length2 = summon.Write(buffer2);
        var readSummon = EntityDescriptionPacket.Read(buffer2.AsSpan(0, length2));
        Assert.True(readSummon.Template == summon.Template && readSummon.Name == summon.Name && readSummon.Level == 70 && length2 <= EntityDescriptionPacket.MaximumSize,
            "a summon description did not round-trip");
        return Task.CompletedTask;
    }

    private static Task RevisionsAsync()
    {
        var field = new FieldInstance(3);
        var classes = new CharacterClassRegistry();
        var character = classes.CreatePlayer(PlayerClass.Fighter, "Revised", 10);
        var player = new WorldPlayer(1, Vector3.Zero, character, false, new PlayerAppearanceView("Revised", PlayerClass.Fighter, 0, 0, 0, "", ""));
        field.Add(player);
        field.Add(new WorldPlayer(2, new Vector3(50, 0, 0), classes.CreatePlayer(PlayerClass.Fighter, "Other", 10), false,
            new PlayerAppearanceView("Other", PlayerClass.Fighter, 0, 0, 0, "", "")));
        var first = field.BuildViews();
        Assert.True(first.All(view => view.Revision == 1), "new entities do not start at revision 1");
        player.ReportMovement(new Vector3(20, 0, 0), 0.5f, true, false, 0, 1000);
        var moved = field.BuildViews();
        Assert.True(moved.All(view => view.Revision == 1), "a move changed a description revision");
        character.Equip(EquipmentSlot.Weapon, SkillScenario.Weapon(ItemKind.Axe, "WA101"));
        var equipped = field.BuildViews();
        Assert.True(equipped.Single(view => view.EntityId == 1).Revision == 2 && equipped.Single(view => view.EntityId == 2).Revision == 1,
            "equipment did not raise only its owner's revision");
        return Task.CompletedTask;
    }

    private static Task CrowdCapAsync()
    {
        static EntityView Player(ulong id, Vector3 at, ulong target = 0) => new(id, EntityKind.Player, false, at, 0f, MotionState.Stand, 1, 100, 100,
            new PlayerAppearanceView("P" + id, PlayerClass.Mechanician, 0, 0, 0, "", ""), attackTargetId: target) { Revision = 1 };
        var views = new List<EntityView> { Player(1, Vector3.Zero, target: 900) };
        for (ulong id = 2; id < 402; id++)
        {
            views.Add(Player(id, new Vector3(id % 20 * 8f, 0f, id / 20 * 8f)));
        }

        // Beyond the crowd but in view: an NPC, the receiver's target, a monster attacking it and its summon.
        views.Add(new EntityView(900, EntityKind.Monster, false, new Vector3(700, 0, 0), 0f, MotionState.Stand, 1, 10, 10, default, templateId: 5)
            { Revision = 1 });
        views.Add(new EntityView(901, EntityKind.Monster, false, new Vector3(0, 0, 700), 0f, MotionState.Attack, 1, 10, 10, default, templateId: 5,
            attackTargetId: 1) { Revision = 1 });
        views.Add(new EntityView(902, EntityKind.Monster, false, new Vector3(-700, 0, 0), 0f, MotionState.Stand, 1, 10, 10, default, templateId: 6)
            { OwnerId = 1, Revision = 1 });
        views.Add(new EntityView(903, EntityKind.Npc, false, new Vector3(0, 0, -700), 0f, MotionState.Stand, 1, 1, 1, default, templateId: 7)
            { Revision = 1 });
        views.Add(Player(904, new Vector3(650, 0, 650)));
        // A crowd of monsters behind the players, nearer than the essential ones.
        for (ulong id = 1000; id < 1300; id++)
        {
            views.Add(new EntityView(id, EntityKind.Monster, false, new Vector3(-(float)(id % 20) * 8f - 10f, 0f, (id - 1000) / 20 * 8f), 0f,
                MotionState.Stand, 1, 10, 10, default, templateId: 5) { Revision = 1 });
        }

        var party = new PartyView([new PartyMemberView(1, "P1", 1, 100, 100, 0, 0), new PartyMemberView(904, "P904", 1, 100, 100, 650, 650)]);
        var wire = new WireRoundTrip(1);
        var update = wire.PushMerged(new WorldSnapshot(1, views.ToArray(), fieldId: 3) { Party = party });
        var spawned = update.Spawns.Select(spawn => spawn.EntityId).ToHashSet();
        var players = spawned.Count(id => id is > 1 and < 1000 and not (>= 900 and < 904));
        Assert.True(spawned.Count == PresenceEncoder.MaxVisible + 1 && players == PresenceEncoder.MaxVisiblePlayers,
            $"the crowd showed {spawned.Count} entities, {players} of them players");
        Assert.True(new ulong[] { 900, 901, 902, 903, 904 }.All(spawned.Contains), "an essential entity or a party member was dropped for the crowd");
        Assert.True(spawned.Contains(2) && !spawned.Contains(401) && spawned.Contains(1000), "the crowd did not keep its nearest members");
        var again = wire.PushMerged(new WorldSnapshot(2, views.ToArray(), fieldId: 3) { Party = party });
        Assert.True(again.Spawns.Count == 0 && again.Removed.Count == 0, "an unchanged crowd churned its visible set");
        return Task.CompletedTask;
    }

    private static Task HandlesAsync()
    {
        static EntityView Monster(ulong id, Vector3 at) =>
            new(id, EntityKind.Monster, false, at, 0.3f, MotionState.Walk, 1, 10, 10, default, templateId: 5) { Revision = 1 };
        var self = new EntityView(1, EntityKind.Player, false, Vector3.Zero, 0f, MotionState.Stand, 1, 100, 100,
            new PlayerAppearanceView("Self", PlayerClass.Mechanician, 0, 0, 0, "", "")) { Revision = 1 };
        var wire = new WireRoundTrip(1);
        var first = wire.PushMerged(new WorldSnapshot(1, [self, Monster(10, new Vector3(100, 0, 0)), Monster(11, new Vector3(0, 0, 100))], fieldId: 3));
        Assert.True(first.Spawns.Count == 3 && wire.DescriptionRequests == 0, $"spawns waited for descriptions they had to ask: {wire.DescriptionRequests}");

        // A walk of uneven steps arrives as changes, and the client's sum never drifts from the server's position.
        var at = new Vector3(100, 0, 0);
        PresenceUpdate update = first;
        for (uint tick = 2; tick < 120; tick++)
        {
            at += new Vector3(1.37f, 0.11f, -0.93f);
            update = wire.PushMerged(new WorldSnapshot(tick, [self, Monster(10, at), Monster(11, new Vector3(0, 0, 100))], fieldId: 3));
        }

        var walked = update.States.Concat(wire.PushMerged(new WorldSnapshot(200, [self, Monster(10, at), Monster(11, new Vector3(0, 0, 100))], fieldId: 3)).States)
            .LastOrDefault(state => state.EntityId == 10);
        // Rounding to an eighth of a unit leaves at most a sixteenth on each axis.
        Assert.True(walked.EntityId == 10 && Vector3.Distance(walked.Position, at) <= MathF.Sqrt(3f) / 16f, $"the walked position drifted: {walked.Position} against {at}");

        wire.PushMerged(new WorldSnapshot(203, [self, Monster(10, at), Monster(11, new Vector3(0, 0, 50))], fieldId: 3));
        var handleOf11 = wire.LastEntries.Single().Handle;

        // 11 leaves and 12 arrives at once: 12 takes a new handle; the freed one is given again only on a later encode.
        var swap = wire.PushMerged(new WorldSnapshot(204, [self, Monster(10, at), Monster(12, new Vector3(-100, 0, 0))], fieldId: 3));
        Assert.True(wire.LastEntries.Single(entry => entry.Spawn).Handle != handleOf11, "a handle was given again before its removal went out");
        var later = wire.PushMerged(new WorldSnapshot(205, [self, Monster(10, at), Monster(12, new Vector3(-100, 0, 0)), Monster(13, new Vector3(0, 0, -100))],
            fieldId: 3));
        Assert.True(wire.LastEntries.Single(entry => entry.Spawn) is { EntityId: 13 } reused && reused.Handle == handleOf11, "the freed handle was not given again");
        var moved = wire.PushMerged(new WorldSnapshot(215, [self, Monster(10, at), Monster(12, new Vector3(-90, 0, 0)), Monster(13, new Vector3(0, 0, -90))],
            fieldId: 3));
        Assert.True(swap.Removed is [11] && swap.Spawns is [{ EntityId: 12 }] && later.Spawns is [{ EntityId: 13 }]
            && moved.States.Single(state => state.EntityId == 12).Position == new Vector3(-90, 0, 0)
            && moved.States.Single(state => state.EntityId == 13).Position == new Vector3(0, 0, -90),
            "a handle given again moved another entity");
        Assert.True(wire.DescriptionRequests == 0, "a monster of a template the client holds was described again on request");
        return Task.CompletedTask;
    }

    private static Task RefreshAsync()
    {
        static EntityView Player(ulong id, int maxLife, byte revision, Vector3 at) => new(id, EntityKind.Player, false, at, 0f, MotionState.Stand, 1,
            maxLife, maxLife, new PlayerAppearanceView("P" + id, PlayerClass.Mechanician, 0, 0, 0, "", "")) { Revision = revision };
        var wire = new WireRoundTrip(1) { DropPushedDescriptions = 1 };
        var first = wire.PushMerged(new WorldSnapshot(1, [Player(1, 100, 1, Vector3.Zero), Player(2, 100, 1, new Vector3(50, 0, 0))], fieldId: 3));
        Assert.True(first.Spawns.Count == 2 && wire.DescriptionRequests == 1, $"a lost pushed description was not asked for: {wire.DescriptionRequests}");
        var grown = wire.PushMerged(new WorldSnapshot(2, [Player(1, 100, 1, Vector3.Zero), Player(2, 180, 2, new Vector3(50, 0, 0))], fieldId: 3));
        Assert.True(grown.Spawns is [{ EntityId: 2, MaxLife: 180 }] && wire.DescriptionRequests == 1,
            "a shown player's new max life and description did not refresh it");
        return Task.CompletedTask;
    }

    private static Task SegmentsAsync()
    {
        var self = new EntityView(1, EntityKind.Player, false, Vector3.Zero, 0f, MotionState.Stand, 1, 100, 100,
            new PlayerAppearanceView("Self", PlayerClass.Mechanician, 0, 0, 0, "", ""));
        var outbox = new WorldOutbox();
        CombatEventView Hit(int damage) => new(1, 1, CombatEventKind.Hit, damage: damage);
        Assert.True(outbox.Writer.TryWrite(new WorldSnapshot(10, [self], [Hit(5)], 3)), "first tick refused");
        Assert.True(outbox.Writer.TryWrite(new WorldSnapshot(11, [self], [], 3)), "second tick refused");
        Assert.True(outbox.Writer.TryWrite(new WorldSnapshot(12, [self], [Hit(7)], 3)), "third tick refused");
        Assert.True(outbox.Reader.TryRead(out var snapshot), "the outbox delivered nothing");
        Assert.True(snapshot.Events.Length == 2 && snapshot.EventSegments is { Length: 2 } segments && segments[0].Tick == 10 && segments[1].Tick == 12,
            "the joined events lost their ticks");
        var updates = new WireRoundTrip(1).Push(snapshot);
        var timed = updates.Where(update => update.Events.Count > 0).SelectMany(update => update.Events.Select(value => (update.ServerTimeMs ?? -1d, value.Damage))).ToList();
        Assert.True(timed.SequenceEqual([(500d, 5), (600d, 7)]), $"events were placed at {string.Join(",", timed)}");

        var single = new WorldOutbox();
        Assert.True(single.Writer.TryWrite(new WorldSnapshot(20, [self], [Hit(3)], 3)) && single.Writer.TryWrite(new WorldSnapshot(21, [self], [], 3)), "writes refused");
        Assert.True(single.Reader.TryRead(out var late) && late.EventSegments is [{ Tick: 20 }], "a lone older batch lost its tick");
        return Task.CompletedTask;
    }

    private static Task GridAsync()
    {
        static EntityView At(ulong id, float x, float z, ulong target = 0, ulong owner = 0, EntityKind kind = EntityKind.Monster) =>
            new(id, kind, false, new Vector3(x, 0, z), 0f, MotionState.Stand, 1, 1, 1, default, attackTargetId: target) { OwnerId = owner };
        var grid = EntityGrid.Build([At(1, -10, -10), At(2, 10, 10, target: 1), At(3, -5000, 0, owner: 1), At(4, 0, 300, kind: EntityKind.Npc), At(5, 63, -1)]);
        var found = new List<int>();
        grid.Query(Vector3.Zero, 70f, found);
        Assert.True(found.Order().SequenceEqual([0, 1, 4]), $"the grid found {string.Join(",", found)} around the origin");
        Assert.True(grid.CountInSquare(Vector3.Zero, 70f) == 3, "the square count missed an entity across zero");
        var related = new List<int>();
        grid.Related(1, related);
        Assert.True(related.Order().SequenceEqual([1, 2]), "the attacker and the summon of entity 1 were not related to it");
        Assert.True(grid.Npcs.SequenceEqual([3]), "the NPC index is wrong");
        return Task.CompletedTask;
    }

    private static Task LostEventsAsync()
    {
        static EntityView At(ulong id, float x) =>
            new(id, id == 1 ? EntityKind.Player : EntityKind.Monster, false, new Vector3(x, 0, 0), 0f, MotionState.Stand, 1, 100, 100, default);
        EntityView[] views = [At(1, 0), At(10, 20), At(11, 40)];
        var receiver = new WireRoundTrip(1);
        receiver.PushMerged(new WorldSnapshot(1, views, [], 3));
        var book = new FieldStatusBook();
        book.Apply([new StatusEventView(10, 9, 7, StatusEffectOp.Add, 1, 0, 0, false, false, 10000, 10000, 0)], 1000);
        var running = new WorldSnapshot(2, views, [], 3) { ActiveStatuses = book.Snapshot, FieldNowMs = 4000 };
        Assert.True(receiver.PushMerged(running).StatusEffects.Count == 0, "statuses were sent again with no event lost");
        var resent = receiver.PushMerged(new WorldSnapshot(3, views, [], 3) { ActiveStatuses = book.Snapshot, FieldNowMs = 4000, EventsLost = true });
        Assert.True(resent.StatusEffects is [{ StatusCode: 7 }], "the status running on a shown entity was not sent again after its events were lost");
        return Task.CompletedTask;
    }

    private static Task StatusBookAsync()
    {
        var book = new FieldStatusBook();
        StatusEventView Status(ulong entity, StatusEffectOp op, int remaining, bool permanent = false) =>
            new(entity, 9, 0x01010003, op, 1, 1, 0, false, permanent, 10000, remaining, 5);
        book.Apply([Status(1, StatusEffectOp.Add, 10000), Status(2, StatusEffectOp.Add, 4000)], 1000);
        Assert.True(book.Snapshot.For(1).Single().At(4000).RemainingMs == 7000, "a running status did not count down");
        book.Apply([Status(1, StatusEffectOp.Tick, 0)], 2000);
        Assert.True(book.Snapshot.For(1).Length == 1, "a periodic tick changed the book");
        var carried = book.Take(1);
        Assert.True(book.Snapshot.For(1).IsEmpty && carried.Length == 1, "a leaving entity kept its statuses in the field");
        var other = new FieldStatusBook();
        other.Carry(1, carried, 5000, 200);
        Assert.True(other.Snapshot.For(1).Single().At(700).RemainingMs == 5500, "a carried status was not rebased onto the new field clock");
        book.Apply([Status(2, StatusEffectOp.Remove, 0)], 3000);
        Assert.True(book.Snapshot.For(2).IsEmpty, "a removed status stayed");
        book.Apply([Status(3, StatusEffectOp.Add, 1000)], 3000);
        book.Prune(9000, id => id == 3);
        Assert.True(book.Snapshot.For(3).IsEmpty, "an ended status survived pruning");
        return Task.CompletedTask;
    }
}
