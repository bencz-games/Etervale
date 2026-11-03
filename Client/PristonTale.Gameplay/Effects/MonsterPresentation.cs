using Microsoft.Xna.Framework;
using PristonTale.Effects.Vfx;
using PristonTale.Engine3D.Formats;
using PristonTale.Engine3D.Rendering;
using PristonTale.Simulation.Modifiers;
using PristonTale.Simulation.World.Monsters.Skills;
using PristonTale.Contracts.Units;
using PristonTale.Simulation.World.Npcs;
using PristonTale.Contracts.Motion;

namespace PristonTale.Gameplay.Effects;

// A monster drawn this frame: its body actor, what selects its effects and where it stands (client space; facing as the
// server sends it).
public readonly record struct MonsterActorView(ulong EntityId, ActorHandle Actor, int SoundCode, int Level, float Scale, Vector3 Position, float Facing,
    bool IsDead, bool Hammer = false);

// The effects the monster presentation starts: named effect definitions, running instances it may stop and the statuses
// of every visible entity.
public interface IMonsterEffectSink
{
    int DefinitionCount { get; }
    StatusBook Statuses { get; }
    bool Defines(string name);
    IMonsterEffect Spawn(string name, ulong casterId, ulong targetId, Vector3 point, float facing);
}

public interface IMonsterEffect
{
    void Stop();
}

// The skill presentation's effect system as the monster presentation's sink.
public sealed class SkillEffectSink(SkillPresentation effects) : IMonsterEffectSink
{
    public int DefinitionCount => effects.Library.Count;

    public StatusBook Statuses => effects.Statuses;

    public bool Defines(string name) => effects.Library.TryGet(name, out _);

    public IMonsterEffect Spawn(string name, ulong casterId, ulong targetId, Vector3 point, float facing) =>
        effects.Spawn(name, casterId, targetId, point, facing) is { Spawned: true } instance ? new Running(instance) : null;

    private sealed class Running(VfxHandle instance) : IMonsterEffect
    {
        public void Stop() => instance.Stop();
    }
}

// What the monster presentation reads from the scene and changes on it.
public interface IMonsterScene
{
    bool TryGetActorMotion(ActorHandle actor, out PristonMotionInfo motion, out float ticks);

    // Adds a colour to every actor of an entity (players included); zero clears it.
    void SetEntityTint(ulong entityId, Vector3 tint);

    // A corpse's look on every actor of an entity: fade multiplies its opacity, dim takes light away.
    void SetEntityCorpse(ulong entityId, float fade, float dim);
}

// Plays the visuals of monsters the way the original client did: it follows each monster's animation and starts the
// family's effects when an attack or skill clip starts, when the clip passes its event frames, when the death clip starts
// and while the monster is present; it shows the statuses monster skills put on players and monsters (effects and body
// tints). Effect names: vfx.monster.<family>.<trigger>[.k<key>][.e<event>] (docs/effects.md "Monster effects").
public sealed class MonsterPresentation
{
    // Attack clips fire their effects on their first two event frames, skill clips on all four.
    public const int AttackEvents = 2;
    public const int SkillEvents = 4;

    private const int AttackState = (int)MotionCode.Attack;
    private const int SkillState = (int)MotionCode.Skill;
    private const int DeadState = (int)MotionCode.Dead;
    private const int MinigSound = 0x1030;

    // A corpse darkens from DimStartTicks after its death clip starts, by half a light unit a tick, up to DimMost at
    // DimFullTicks; the bodies of light (Figon, Stygian Lord, Guardian Saint, the fire elemental, Chaos Cara) fade by two
    // alpha units a tick until they are gone. Light and alpha units are 1/256 of a full light and of opaque.
    private const float DimStartTicks = 200f;
    private const float DimPerTick = 0.5f;
    private const float DimFullTicks = 400f;
    private const float DimMost = 100f;
    private const float FadePerTick = 2f;
    private const float Units = 256f;

    private static readonly Vector3 IceTint = new(0f, 0f, 160f / 256f);
    private static readonly Vector3 PoisonTint = new(0f, 100f / 256f, 0f);

    private readonly IMonsterEffectSink _effects;
    private readonly IMonsterScene _scene;
    private readonly Dictionary<ulong, Tracked> _tracked = new();
    private readonly Dictionary<ulong, ulong> _targets = new();
    private readonly Dictionary<(ulong Entity, uint Code, ulong Source), IMonsterEffect> _statusEffects = new();
    private readonly Dictionary<ulong, (int Ice, int Poison)> _tints = new();
    private readonly HashSet<ulong> _seen = new();
    private readonly List<ulong> _gone = new();
    private readonly Dictionary<string, string> _names = new(StringComparer.Ordinal);

    public MonsterPresentation(IMonsterEffectSink effects, IMonsterScene scene)
    {
        _effects = effects;
        _scene = scene;
        effects.Statuses.Added += OnStatusAdded;
        effects.Statuses.Removed += OnStatusRemoved;
    }

    public int TrackedCount => _tracked.Count;

    // The target of a monster's latest swing, from the server's swing event.
    public void ObserveSwing(ulong attackerId, ulong targetId) => _targets[attackerId] = targetId;

    // A monster skill struck someone besides its swing's target: the family's impact effect plays on that victim.
    public void ObserveSkillHit(ulong attackerId, ulong victimId, Vector3 victimPosition)
    {
        if (victimId == Target(attackerId) || !_tracked.TryGetValue(attackerId, out var tracked) || tracked.Family.Length == 0
            || Resolve(tracked.Family, "impact", '\0', 0) is not { } name)
        {
            return;
        }

        _effects.Spawn(name, attackerId, victimId, victimPosition, 0f);
    }

    public void Update(IReadOnlyList<MonsterActorView> monsters, float seconds)
    {
        _seen.Clear();
        foreach (var monster in monsters)
        {
            _seen.Add(monster.EntityId);
            Step(monster, seconds);
        }

        _gone.Clear();
        foreach (var id in _tracked.Keys)
        {
            if (!_seen.Contains(id))
            {
                _gone.Add(id);
            }
        }

        foreach (var id in _gone)
        {
            Forget(id);
        }
    }

    public void Clear()
    {
        foreach (var tracked in _tracked.Values)
        {
            tracked.Aura?.Stop();
        }

        foreach (var instance in _statusEffects.Values)
        {
            instance.Stop();
        }

        _tracked.Clear();
        _targets.Clear();
        _statusEffects.Clear();
        _tints.Clear();
    }

    private void Forget(ulong id)
    {
        if (_tracked.Remove(id, out var tracked))
        {
            tracked.Aura?.Stop();
        }

        _targets.Remove(id);
        _soundById.Remove(id);
    }

    private void Step(in MonsterActorView monster, float seconds)
    {
        if (!_tracked.TryGetValue(monster.EntityId, out var tracked))
        {
            tracked = new Tracked { Family = MonsterVisualFamilies.Of(monster.SoundCode, monster.Level, monster.Scale) };
            _tracked.Add(monster.EntityId, tracked);
        }

        if (tracked.Family.Length == 0)
        {
            return;
        }

        _soundById[monster.EntityId] = monster.SoundCode;
        if (tracked.DeathShown)
        {
            tracked.DeathTicks += seconds * GameUnits.TicksPerSecond;
            var fade = CorpseFade(monster.SoundCode, tracked.DeathTicks);
            var dim = CorpseDim(tracked.DeathTicks);
            if (fade != tracked.Fade || dim != tracked.Dim)
            {
                tracked.Fade = fade;
                tracked.Dim = dim;
                _scene.SetEntityCorpse(monster.EntityId, fade, dim);
            }
        }

        if (tracked.Aura is null && tracked.AuraTried != _effects.DefinitionCount && !monster.IsDead && !tracked.DeathShown)
        {
            // Effects register over several frames after loading; the aura is looked up again whenever more arrived.
            tracked.AuraTried = _effects.DefinitionCount;
            tracked.Aura = Spawn(tracked.Family, "aura", '\0', 0, monster, 0);
        }

        if (!_scene.TryGetActorMotion(monster.Actor, out var motion, out var ticks) || motion is null)
        {
            return;
        }

        if (tracked.Clip.Advance(motion, ticks))
        {
            OnClipStart(tracked, motion, monster);
        }

        var state = (int)motion.State;
        var events = state == SkillState ? SkillEvents : state == AttackState ? AttackEvents : 0;
        for (var index = 0; index < events && index < motion.EventFrames.Count; index++)
        {
            // An event past the clip's end never plays.
            if (motion.EventFrames[index] > (motion.EndFrame - motion.StartFrame) * (uint)GameUnits.FrameTicks)
            {
                continue;
            }

            if (tracked.Clip.Passed(index))
            {
                Spawn(tracked.Family, state == SkillState ? "skill.hit" : "attack.hit", (char)motion.KeyCode, index + 1, monster, Target(monster.EntityId));
            }
        }

        if (monster.SoundCode == MinigSound)
        {
            StepAnger(tracked, monster.EntityId, state, seconds);
        }
    }

    private void OnClipStart(Tracked tracked, PristonMotionInfo motion, in MonsterActorView monster)
    {
        switch ((int)motion.State)
        {
            case AttackState:
                Spawn(tracked.Family, "attack", (char)motion.KeyCode, 0, monster, Target(monster.EntityId));
                break;
            case SkillState:
                Spawn(tracked.Family, "skill", (char)motion.KeyCode, 0, monster, Target(monster.EntityId));
                break;
            case DeadState when !tracked.DeathShown:
                tracked.DeathShown = true;
                tracked.Aura?.Stop();
                tracked.Aura = null;
                Spawn(tracked.Family, "death", '\0', 0, monster, 0);
                break;
        }
    }

    // The light a corpse has lost, as a share of a full light, this many ticks after its death clip started.
    public static float CorpseDim(float ticks) =>
        ticks >= DimFullTicks ? DimMost / Units : ticks > DimStartTicks ? (ticks - DimStartTicks) * DimPerTick / Units : 0f;

    // The opacity left to a corpse: a body of light fades out, every other corpse stays.
    public static float CorpseFade(int soundCode, float ticks) => FadesOnDeath(soundCode) ? Math.Max(0f, 1f - FadePerTick * ticks / Units) : 1f;

    private static bool FadesOnDeath(int soundCode) => soundCode is CharacterSoundCodes.Figon or CharacterSoundCodes.StygianLord
        or CharacterSoundCodes.GuardianSaint or CharacterSoundCodes.FireElemental or CharacterSoundCodes.ChaosCara;

    // A Minig grows red while it fights: +2 per frame in attack or flinch clips, -1 otherwise, at most 100.
    private void StepAnger(Tracked tracked, ulong entityId, int state, float seconds)
    {
        var frames = seconds * GameUnits.TicksPerSecond;
        tracked.Anger = Math.Clamp(tracked.Anger + (state is AttackState or (int)MotionCode.Damage ? 2f : -1f) * frames, 0f, 100f);
        _scene.SetEntityTint(entityId, new Vector3(tracked.Anger / 256f, -tracked.Anger / 512f, 0f));
    }

    private ulong Target(ulong attackerId) => _targets.TryGetValue(attackerId, out var target) ? target : 0;

    private IMonsterEffect Spawn(string family, string trigger, char key, int eventNumber, in MonsterActorView monster, ulong target)
    {
        var name = Resolve(family, trigger, key, eventNumber);
        return name is null ? null : _effects.Spawn(name, monster.EntityId, target, monster.Position, -monster.Facing);
    }

    // Lookup order: <trigger>.k<key>.e<n>, <trigger>.k<key>, <trigger>.e<n>, <trigger>. Asked on every clip start and
    // event frame of every drawn monster, so each combination's answer is kept and its names are built only once; a
    // combination without an effect is asked again once more definitions have registered.
    public string Resolve(string family, string trigger, char key, int eventNumber)
    {
        var combination = (family, trigger, key, eventNumber);
        if (_resolved.TryGetValue(combination, out var known) && (known.Name is not null || known.Definitions == _effects.DefinitionCount))
        {
            return known.Name;
        }

        var name = ResolveNames(family, trigger, key, eventNumber);
        _resolved[combination] = (name, _effects.DefinitionCount);
        return name;
    }

    private readonly Dictionary<(string Family, string Trigger, char Key, int EventNumber), (string Name, int Definitions)> _resolved = new();

    private string ResolveNames(string family, string trigger, char key, int eventNumber)
    {
        var stem = $"vfx.monster.{family}.{trigger}";
        if (key > ' ')
        {
            if (eventNumber > 0 && Defined($"{stem}.k{key}.e{eventNumber}") is { } keyed)
            {
                return keyed;
            }

            if (Defined($"{stem}.k{key}") is { } byKey)
            {
                return byKey;
            }
        }

        if (eventNumber > 0 && Defined($"{stem}.e{eventNumber}") is { } byEvent)
        {
            return byEvent;
        }

        return Defined(stem);
    }

    private string Defined(string name)
    {
        if (_names.TryGetValue(name, out var known))
        {
            return known;
        }

        if (!_effects.Defines(name))
        {
            return null;
        }

        _names[name] = name;
        return name;
    }

    // Status visuals: the effect a status shows on its holder and the body tint of ice and poison.
    public static string StatusEffect(uint code, int sourceSound) => code switch
    {
        MonsterStatuses.Poison => "vfx.monster.status.poison",
        MonsterStatuses.Stun when sourceSound == CharacterSoundCodes.BloodyKnight => "vfx.monster.status.bloodyknightstun",
        MonsterStatuses.LightningStun => "vfx.debuff.stun",
        MonsterStatuses.CurseDefence when sourceSound != CharacterSoundCodes.Mystic => "vfx.monster.status.cursedefence",
        MonsterStatuses.CurseAttack => "vfx.monster.status.curseattack",
        _ => null
    };

    private void OnStatusAdded(ulong entityId, StatusEntry entry)
    {
        if (!MonsterStatuses.IsMonsterStatus(entry.Code))
        {
            return;
        }

        var sourceSound = SoundOf(entry.SourceId);
        if (StatusEffect(entry.Code, sourceSound) is { } name && _effects.Defines(name))
        {
            var key = (entityId, entry.Code, entry.SourceId);
            if (_statusEffects.Remove(key, out var running))
            {
                running.Stop();
            }

            if (_effects.Spawn(name, entityId, entityId, Vector3.Zero, 0f) is { } instance)
            {
                _statusEffects[key] = instance;
            }
        }

        UpdateTint(entityId, entry.Code, 1);
    }

    private void OnStatusRemoved(ulong entityId, StatusEntry entry)
    {
        if (!MonsterStatuses.IsMonsterStatus(entry.Code))
        {
            return;
        }

        if (_statusEffects.Remove((entityId, entry.Code, entry.SourceId), out var instance))
        {
            instance.Stop();
        }

        UpdateTint(entityId, entry.Code, -1);
    }

    private int SoundOf(ulong entityId) => _soundById.TryGetValue(entityId, out var sound) ? sound : 0;

    // The sound family of every monster seen, for statuses whose look depends on the monster that applied them.
    private readonly Dictionary<ulong, int> _soundById = new();

    private void UpdateTint(ulong entityId, uint code, int delta)
    {
        if (code is not (MonsterStatuses.IceSlow or MonsterStatuses.Poison))
        {
            return;
        }

        var (ice, poison) = _tints.GetValueOrDefault(entityId);
        if (code == MonsterStatuses.IceSlow)
        {
            ice = Math.Max(0, ice + delta);
        }
        else
        {
            poison = Math.Max(0, poison + delta);
        }

        if (ice == 0 && poison == 0)
        {
            _tints.Remove(entityId);
        }
        else
        {
            _tints[entityId] = (ice, poison);
        }

        _scene.SetEntityTint(entityId, (ice > 0 ? IceTint : Vector3.Zero) + (poison > 0 ? PoisonTint : Vector3.Zero));
    }

    // Holders of a monster stun cannot move, attack or cast.
    public static bool Stunned(IReadOnlyList<StatusEntry> statuses)
    {
        for (var index = 0; index < statuses.Count; index++)
        {
            if ((statuses[index].Control & ControlFlags.Stun) != 0)
            {
                return true;
            }
        }

        return false;
    }

    private sealed class Tracked
    {
        public string Family = "";
        public ClipEventCursor Clip;
        public IMonsterEffect Aura;
        public int AuraTried = -1;
        public bool DeathShown;
        public float DeathTicks;
        public float Fade = 1f;
        public float Dim;
        public float Anger;
    }
}
