using Microsoft.Xna.Framework;
using PristonTale.Client.Net.Presence;
using PristonTale.Effects.Vfx;
using PristonTale.Engine3D.Formats;
using PristonTale.Engine3D.Rendering;
using PristonTale.Gameplay.Effects;
using PristonTale.Protocol.Packets;
using PristonTale.Simulation.Modifiers;
using PristonTale.Simulation.World.Monsters.Skills;
using PristonTale.VfxImport;
using PristonTale.Contracts.Skills;
using PristonTale.Simulation.World.Npcs;

namespace PristonTale.Tests;

// The client side of monster skills: effect families, the triggers that follow a monster's clips, status visuals and
// tints, and the shipped effect data that the names point at.
internal static class MonsterPresentationTests
{
    public static IEnumerable<(string, Func<Task>)> Cases()
    {
        yield return ("monster effect families split by level and size as the original did", FamiliesAsync);
        yield return ("monster clips start their effects at clip start and at each event frame, keyed by the clip", TriggersAsync);
        yield return ("monster auras run while the monster lives and death effects play once", AuraAsync);
        yield return ("corpses darken after their death clip starts and a Figon's body of light fades away", CorpsesAsync);
        yield return ("monster statuses show their effect and tint on the holder until removed", StatusesAsync);
        yield return ("authored monster effects use known families and cover every family the client draws", DataAsync);
    }

    private static Task FamiliesAsync()
    {
        Assert.True(MonsterVisualFamilies.Of(0x1020, 10, 1f) == "imp" && MonsterVisualFamilies.Of(0x1020, 20, 1f) == "mephit", "imp split");
        Assert.True(MonsterVisualFamilies.Of(0x12C0, 75, 1f) == "witch" && MonsterVisualFamilies.Of(0x12C0, 79, 1f) == "succubus"
            && MonsterVisualFamilies.Of(0x12C0, 82, 1f) == "incubus" && MonsterVisualFamilies.Of(0x12C0, 90, 1f) == "incubussummoner", "succubus split");
        Assert.True(MonsterVisualFamilies.Of(0x1440, 60, 1f) == "evilplant" && MonsterVisualFamilies.Of(0x1440, 58, 1f) == "evilmaple", "plant split");
        Assert.True(MonsterVisualFamilies.Of(0x1460, 64, 1f) == "" && MonsterVisualFamilies.Of(0x1460, 69, 1f) == "doomguard"
            && MonsterVisualFamilies.Of(0x1460, 100, 1.6f) == "doomlord", "dark knight split");
        Assert.True(MonsterVisualFamilies.Of(0x10D0, 50, 1f) == "evilsnail" && MonsterVisualFamilies.Of(0x10D0, 70, 1f) == "", "scorpion split");
        return Task.CompletedTask;
    }

    private static PristonMotionInfo Clip(uint state, uint start, uint end, char key, params uint[] events) =>
        new(state, start, end, events, 0, 0, 0, 0, (byte)key, 0);

    private static Task TriggersAsync()
    {
        var sink = new Sink("vfx.monster.kelvezu.attack", "vfx.monster.kelvezu.attack.kP", "vfx.monster.kelvezu.attack.hit",
            "vfx.monster.kelvezu.skill.hit.kI.e2", "vfx.monster.kelvezu.skill.hit", "vfx.monster.kelvezu.skill.hit.e3");
        var scene = new Scene();
        var presentation = new MonsterPresentation(sink, scene);
        var view = new MonsterActorView(7, new ActorHandle(1, 1), 0x1830, 120, 1f, new Vector3(10, 0, 0), 0.5f, false);
        presentation.ObserveSwing(7, 99);

        scene.Motion = Clip(0x100, 10, 40, 'P', 1600, 3200);
        scene.Ticks = 10 * 160;
        presentation.Update([view], 1f / 60f);
        Assert.True(sink.Spawned.SequenceEqual(["vfx.monster.kelvezu.attack.kP"]), "attack start ignored its key");
        Assert.True(sink.Targets.Single() == 99 && sink.Casters.Single() == 7, "effects lost their caster or target");
        scene.Ticks = 10 * 160 + 1700;
        presentation.Update([view], 1f / 60f);
        scene.Ticks = 10 * 160 + 3300;
        presentation.Update([view], 1f / 60f);
        Assert.True(sink.Spawned.Count(name => name == "vfx.monster.kelvezu.attack.hit") == 2, "attack events did not each start the blow effect");

        sink.Spawned.Clear();
        scene.Motion = Clip(0x150, 100, 200, 'I', 800, 1600, 2400, 3200);
        scene.Ticks = 100 * 160;
        presentation.Update([view], 1f / 60f);
        scene.Ticks = 100 * 160 + 4000;
        presentation.Update([view], 1f / 60f);
        Assert.True(sink.Spawned.SequenceEqual(["vfx.monster.kelvezu.skill.hit", "vfx.monster.kelvezu.skill.hit.kI.e2", "vfx.monster.kelvezu.skill.hit.e3",
            "vfx.monster.kelvezu.skill.hit"]), "skill events did not follow the lookup order: " + string.Join(", ", sink.Spawned));

        sink.Spawned.Clear();
        scene.Ticks = 100 * 160;
        presentation.Update([view], 1f / 60f);
        Assert.True(sink.Spawned.Count == 0, "a restarted skill clip without a start effect started one");
        scene.Ticks = 100 * 160 + 900;
        presentation.Update([view], 1f / 60f);
        Assert.True(sink.Spawned.Count == 1, "a restarted clip did not fire its events again");
        return Task.CompletedTask;
    }

    private static Task AuraAsync()
    {
        var sink = new Sink("vfx.monster.figon.aura", "vfx.monster.deadzone.death");
        var scene = new Scene { Motion = Clip(0x40, 0, 10, '\0') };
        var presentation = new MonsterPresentation(sink, scene);
        var figon = new MonsterActorView(1, new ActorHandle(1, 1), 0x1180, 50, 1f, Vector3.Zero, 0f, false);
        presentation.Update([figon], 1f / 60f);
        presentation.Update([figon], 1f / 60f);
        Assert.True(sink.Spawned.SequenceEqual(["vfx.monster.figon.aura"]), "aura did not start once");
        presentation.Update([], 1f / 60f);
        Assert.True(sink.Stopped == 1 && presentation.TrackedCount == 0, "aura outlived its monster");

        var deadzone = new MonsterActorView(2, new ActorHandle(2, 1), 0x1510, 90, 1f, Vector3.Zero, 0f, true);
        scene.Motion = Clip(0x120, 0, 30, '\0');
        presentation.Update([deadzone], 1f / 60f);
        scene.Ticks = 5;
        presentation.Update([deadzone], 1f / 60f);
        Assert.True(sink.Spawned.Count(name => name == "vfx.monster.deadzone.death") == 1, "death effect did not play exactly once");
        return Task.CompletedTask;
    }

    private static Task CorpsesAsync()
    {
        Assert.True(MonsterPresentation.CorpseDim(200f) == 0f && MonsterPresentation.CorpseDim(300f) == 50f / 256f
            && MonsterPresentation.CorpseDim(500f) == 100f / 256f, "a corpse does not darken from 200 ticks to its most at 400");
        Assert.True(MonsterPresentation.CorpseFade(CharacterSoundCodes.Figon, 0f) == 1f && MonsterPresentation.CorpseFade(CharacterSoundCodes.Figon, 64f) == 0.5f
            && MonsterPresentation.CorpseFade(CharacterSoundCodes.Figon, 128f) == 0f && MonsterPresentation.CorpseFade(CharacterSoundCodes.Resolve("DEADZONE"), 1000f) == 1f,
            "a Figon does not fade by two alpha units a tick, or another corpse fades");
        var scene = new Scene { Motion = Clip(0x120, 0, 30, '\0') };
        var presentation = new MonsterPresentation(new Sink(), scene);
        var figon = new MonsterActorView(3, new ActorHandle(3, 1), CharacterSoundCodes.Figon, 50, 1f, Vector3.Zero, 0f, true);
        for (var frame = 0; frame < 140; frame++)
        {
            presentation.Update([figon], 1f / 70f);
        }

        Assert.True(scene.Corpses.TryGetValue(3, out var corpse) && corpse.Fade == 0f, $"a dead Figon's body stayed: {corpse}");
        return Task.CompletedTask;
    }

    private static Task StatusesAsync()
    {
        var sink = new Sink("vfx.monster.status.poison", "vfx.monster.status.bloodyknightstun");
        var scene = new Scene();
        var presentation = new MonsterPresentation(sink, scene);
        var knight = new MonsterActorView(5, new ActorHandle(5, 1), 0x1850, 106, 1f, Vector3.Zero, 0f, false);
        presentation.Update([knight], 1f / 60f);
        sink.Statuses.Apply(Status(1, 5, MonsterStatuses.Stun, StatusEffectOp.Add, ControlFlags.Stun), 0);
        sink.Statuses.Apply(Status(1, 5, MonsterStatuses.Poison, StatusEffectOp.Add), 0);
        sink.Statuses.Apply(Status(1, 5, MonsterStatuses.IceSlow, StatusEffectOp.Add, ControlFlags.Slow), 0);
        Assert.True(sink.Spawned.Contains("vfx.monster.status.bloodyknightstun") && sink.Spawned.Contains("vfx.monster.status.poison"),
            "status effects did not start");
        Assert.True(MonsterPresentation.Stunned(sink.Statuses.For(1)), "stun was not seen on the holder");
        Assert.True(scene.Tints[1].Z > 0.5f && scene.Tints[1].Y > 0.3f, "ice and poison did not tint the holder");
        sink.Statuses.Apply(Status(1, 5, MonsterStatuses.IceSlow, StatusEffectOp.Remove, ControlFlags.Slow), 0);
        sink.Statuses.Apply(Status(1, 5, MonsterStatuses.Poison, StatusEffectOp.Remove), 0);
        Assert.True(scene.Tints[1] == Vector3.Zero && sink.Stopped == 1, "removed statuses kept their tint or effect");
        Assert.True(MonsterPresentation.StatusEffect(MonsterStatuses.Stun, CharacterSoundCodes.Chimera) is null
            && MonsterPresentation.StatusEffect(MonsterStatuses.CurseDefence, CharacterSoundCodes.Mystic) is null
            && MonsterPresentation.StatusEffect(MonsterStatuses.LightningStun, CharacterSoundCodes.CastleCrystalLightning) == "vfx.debuff.stun", "status visual choice");
        return Task.CompletedTask;
    }

    private static StatusEffectInfo Status(ulong entity, ulong source, uint code, StatusEffectOp op, ControlFlags control = ControlFlags.None) =>
        new(entity, source, code, op, 1, (byte)EffectSourceKind.MonsterSkill, (byte)control, StatusEffectFlags.Harmful, 3000, 3000, 0);

    private static Task DataAsync()
    {
        var source = FindAuthored();
        Assert.True(source is not null, "the importer's Authored folder was not found");
        var names = Directory.EnumerateFiles(Path.Combine(source!, "Monsters"), VfxCatalog.Pattern, SearchOption.AllDirectories)
            .SelectMany(file => VfxDefinition.ParseFile(File.ReadAllText(file)))
            .Select(definition => definition.Name)
            .ToArray();
        var families = new HashSet<string>(StringComparer.Ordinal);
        for (var code = 0x1000; code <= 0x6FFF; code++)
        {
            foreach (var (level, scale) in new[] { (10, 1f), (50, 1f), (59, 1f), (60, 1f), (70, 1f), (77, 1f), (85, 1f), (95, 1f), (100, 2f) })
            {
                if (MonsterVisualFamilies.Of(code, level, scale) is { Length: > 0 } family)
                {
                    families.Add(family);
                }
            }
        }

        foreach (var family in families)
        {
            Assert.True(names.Any(name => name.StartsWith($"vfx.monster.{family}.", StringComparison.Ordinal)), $"family {family} has no effect");
        }

        foreach (var name in names.Where(name => !name.StartsWith("vfx.monster.status.", StringComparison.Ordinal)))
        {
            var rest = name["vfx.monster.".Length..];
            var family = rest[..rest.IndexOf('.')];
            Assert.True(families.Contains(family) || family == "revived", $"{name} names an unknown family");
        }

        foreach (var status in new[] { MonsterStatuses.Poison, MonsterStatuses.CurseDefence, MonsterStatuses.CurseAttack })
        {
            Assert.True(names.Contains(MonsterPresentation.StatusEffect(status, 0)), $"status {status:X} has no effect");
        }

        Assert.True(names.Contains("vfx.monster.status.bloodyknightstun"), "the bloody knight stun has no effect");
        return Task.CompletedTask;
    }

    private static string? FindAuthored()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            var candidate = Path.Combine(directory.FullName, "tools", "PristonTale.VfxImport", VfxImporter.AuthoredFolder);
            if (Directory.Exists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }

    private sealed class Sink(params string[] defined) : IMonsterEffectSink
    {
        private readonly HashSet<string> _defined = new(defined, StringComparer.OrdinalIgnoreCase);

        public List<string> Spawned { get; } = new();
        public List<ulong> Casters { get; } = new();
        public List<ulong> Targets { get; } = new();
        public int Stopped { get; private set; }
        public int DefinitionCount => _defined.Count;
        public StatusBook Statuses { get; } = new();

        public bool Defines(string name) => _defined.Contains(name);

        public IMonsterEffect Spawn(string name, ulong casterId, ulong targetId, Vector3 point, float facing)
        {
            Spawned.Add(name);
            Casters.Add(casterId);
            Targets.Add(targetId);
            return new Running(this);
        }

        private sealed class Running(Sink sink) : IMonsterEffect
        {
            public void Stop() => sink.Stopped++;
        }
    }

    private sealed class Scene : IMonsterScene
    {
        public PristonMotionInfo Motion { get; set; } = Clip(0x40, 0, 10, '\0');
        public float Ticks { get; set; }
        public Dictionary<ulong, Vector3> Tints { get; } = new();

        public bool TryGetActorMotion(ActorHandle actor, out PristonMotionInfo motion, out float ticks)
        {
            motion = Motion;
            ticks = Ticks;
            return true;
        }

        public void SetEntityTint(ulong entityId, Vector3 tint) => Tints[entityId] = tint;

        public Dictionary<ulong, (float Fade, float Dim)> Corpses { get; } = new();

        public void SetEntityCorpse(ulong entityId, float fade, float dim) => Corpses[entityId] = (fade, dim);
    }
}
