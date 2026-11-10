using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Content;
using Microsoft.Xna.Framework.Graphics;
using PristonTale.Client.Net.Presence;
using PristonTale.Effects;
using PristonTale.Effects.Vfx;
using PristonTale.Engine3D.Diagnostics;
using PristonTale.Engine3D.Rendering.Lighting;
using PristonTale.Protocol.Packets;
using PristonTale.Simulation.Physics;
using PristonTale.Simulation.Skills;
using PristonTale.Contracts.Combat;
using PristonTale.Contracts.Skills;
using PristonTale.Contracts.Units;
using PristonTale.Engine3D;

namespace PristonTale.Gameplay.Effects;

// What the skill presentation needs from the scene: entity positions and bones (client world space), the listener and
// the motion playback of casters.
public interface ISkillSceneHost : IVfxAnchors
{
    ulong SelfId { get; }
    Vector3 ListenerPosition { get; }

    // Plays the skill's motion on the caster; loop counts the repeats of weapon-clip skills. For a skill clip rate is
    // the playback rate; for a weapon clip it multiplies the caster's attack rate.
    void PlaySkillMotion(ulong entityId, SkillMotionPlan plan, float durationMs, float rate, int loop);

    // The caster's attack playback rate (1 when unknown); weapon-clip skills speed it up.
    float AttackRateOf(ulong entityId);

    // A released charge: the caster's skill clip jumps to the frame and plays on at the rate.
    void SeekSkillMotion(ulong entityId, int framesFromStart, float rate) { }

    // How the entity's actors look this frame (tints, translucency, shield swaps, afterimages); Normal once a look ends.
    void ApplyLook(ulong entityId, in EntityLook look) { }

    // How far behind the server's timeline another entity is drawn, so its skill events wait as long as its motion does.
    double PlaybackDelayMsOf(ulong entityId) => World.Remote.RemoteSnapshotBuffer.BaseDelayMs;

    // A skill's impact shakes the local player's camera by this factor (SkillRule.CameraShake).
    void ShakeCamera(int factor) { }
}

// Turns the server's skill timeline into motions, effects, sounds, lights, floating numbers and the buff and
// cooldown state the HUD shows. Events of other entities wait for their caster's playback delay, so their effects line
// up with the interpolated motion; the local player's events play at once.
public sealed class SkillPresentation : IVfxServices, IDisposable
{
    public const string ReviveEffect = "vfx.priestess.resurrection.rise";
    // The pillar of light of a player standing up: entering the world, a restart or any revival.
    public const string RestartEffect = "vfx.player.restart";
    private const float ChainLinkSeconds = 0.08f;
    // Game-thread time per frame spent registering effect definitions until all are ready.
    private static readonly TimeSpan CompileBudget = TimeSpan.FromMilliseconds(2);

    private readonly ISkillSceneHost _host;
    private readonly EffectLibrary _effectLibrary;
    private readonly PristonTransientLights _lights;
    private readonly SkillSoundBank _sounds;
    private readonly IClientLog _log;
    private readonly List<Pending> _pending = new(32);
    private readonly Dictionary<ulong, VfxHandle> _projectiles = new();
    private readonly List<(ulong CasterId, VfxHandle Effect)> _castEffects = new();
    // Impacts each caster's current cast has had (per-hit sounds and effects).
    private readonly Dictionary<ulong, int> _impacts = new();
    private readonly Random _soundRandom = new();
    private bool _effectSoundsLoaded;

    public SkillPresentation(GraphicsDevice device, ContentManager content, EffectLibrary library, PristonTransientLights lights, ISkillSceneHost host,
        SkillSoundBank sounds, IClientLog log)
    {
        _host = host;
        _effectLibrary = library;
        _lights = lights;
        _sounds = sounds;
        _log = log;
        Library = library.Vfx.Library;
        System = new VfxSystem(host, this, Library);
        Renderer = new VfxRenderer(device, content);
        Statuses.Added += OnStatusAdded;
        Statuses.Removed += OnStatusRemoved;
        sounds.Preload(SkillRuleCatalog.All.SelectMany(rule => new[] { rule.CastSound, rule.ImpactSound, rule.CompanionSound }.Concat(rule.ImpactSounds).Where(static sound => sound != SkillRule.Silent)));
    }

    public VfxLibrary Library { get; }
    public VfxSystem System { get; }
    public VfxRenderer Renderer { get; }
    public StatusBook Statuses { get; } = new();
    public SkillCooldownBook Cooldowns { get; } = new();
    public FloatingCombatText Text { get; } = new();
    public ActorLookBook Looks { get; } = new();

    public event Action<SkillCastResultInfo> CastResolved;

    public void Observe(PresenceUpdate update, double nowMs)
    {
        if (update.CastResult is { } result)
        {
            Cooldowns.Apply(result, nowMs);
            CastResolved?.Invoke(result);
        }

        if (update.Cooldowns is { } cooldowns)
        {
            Cooldowns.Apply(cooldowns, nowMs);
        }

        foreach (var skillEvent in update.SkillEvents)
        {
            var due = nowMs + (skillEvent.CasterId == _host.SelfId ? 0d : _host.PlaybackDelayMsOf(skillEvent.CasterId));
            _pending.Add(new Pending(due, PendingKind.Event, skillEvent, 0));
        }

        // Statuses of an entity that left the view are forgotten; the server sends the running ones when it returns.
        foreach (var removed in update.Removed)
        {
            Statuses.Forget(removed);
        }

        foreach (var status in update.StatusEffects)
        {
            if (status.Op != StatusEffectOp.Tick)
            {
                Statuses.Apply(status, nowMs);
            }
        }

        foreach (var combat in update.Events)
        {
            ObserveCombat(combat);
        }
    }

    public void Update(float seconds, double nowMs)
    {
        if (!_effectLibrary.Vfx.Ready && _effectLibrary.Vfx.Compile(CompileBudget) || _effectLibrary.Vfx.Ready && !_effectSoundsLoaded)
        {
            _effectSoundsLoaded = true;
            _sounds.Preload(Library.Names.SelectMany(name => Library.TryGet(name, out var effect) ? effect.Definition.Sounds.SelectMany(sound => sound.AllPaths) : []));
        }

        _sounds.Update();
        for (var index = 0; index < _pending.Count; index++)
        {
            var pending = _pending[index];
            if (pending.AtMs > nowMs)
            {
                continue;
            }

            _pending.RemoveAt(index--);
            Run(pending, nowMs);
        }

        KeepAuras(nowMs);
        Looks.Update(seconds, nowMs, Statuses, _host.SelfId);
        foreach (var (entity, look) in Looks.Looks)
        {
            _host.ApplyLook(entity, look);
        }

        System.Update(seconds);
        Text.Update(seconds);
    }

    public void Draw(Matrix view, Matrix projection, Vector3 cameraPosition, Texture2D sceneDepth) =>
        Renderer.Draw(System, view, projection, cameraPosition, sceneDepth);

    public void Clear()
    {
        _pending.Clear();
        _projectiles.Clear();
        _castEffects.Clear();
        _impacts.Clear();
        Looks.Clear();
        Statuses.Clear();
        System.Clear();
    }

    // Starts a named effect at a point with a full orientation (scripts carried by other effects, monster skills).
    public VfxHandle SpawnAt(string name, Vector3 point, Matrix orientation)
    {
        if (!Library.TryGet(name, out var effect))
        {
            return default;
        }

        var handle = System.Spawn(effect, new VfxSpawn(0, 0, point, 0f));
        handle.SetOrientation(orientation);
        return handle;
    }

    // Spawns a named effect directly (probes, previews).
    public VfxHandle Spawn(string name, ulong casterId, ulong targetId, Vector3 point, float facing = 0f)
    {
        return Library.TryGet(name, out var effect) ? System.Spawn(effect, new VfxSpawn(casterId, targetId, point, facing)) : default;
    }

    void IVfxServices.AddLight(Vector3 position, Color color, int power, int decay) => _lights?.Add(position, color, power, decay);

    void IVfxServices.PlaySound(string path, Vector3 position, float volume) => _sounds.Play(path, position, _host.ListenerPosition, volume);

    private void Run(in Pending pending, double nowMs)
    {
        var skillEvent = pending.Event;
        if (!SkillRuleCatalog.TryGet(skillEvent.SkillCode, out var rule))
        {
            return;
        }

        switch (pending.Kind)
        {
            case PendingKind.Event:
                switch (skillEvent.Phase)
                {
                    case SkillEventPhase.Start:
                        Start(skillEvent, rule, nowMs);
                        break;
                    case SkillEventPhase.Impact:
                        Impact(skillEvent, rule);
                        break;
                    case SkillEventPhase.Cancel:
                        Cancel(skillEvent.CasterId);
                        break;
                    case SkillEventPhase.Release:
                        Release(skillEvent, rule, nowMs);
                        break;
                }

                break;
            case PendingKind.Loop:
                var loops = Math.Max(1, rule.At(rule.Loops, skillEvent.Rank));
                _host.PlaySkillMotion(skillEvent.CasterId, SkillFormulas.Plan(rule, skillEvent.Rank), skillEvent.DurationMs / loops, MotionRate(rule, skillEvent),
                    pending.Loop);
                break;
            case PendingKind.Launch:
                Launch(skillEvent, rule);
                break;
        }
    }

    private void Start(SkillEventInfo skillEvent, SkillRule rule, double nowMs)
    {
        var plan = SkillFormulas.Plan(rule, skillEvent.Rank);
        var loops = plan.Motion == SkillMotion.Attack ? Math.Max(1, rule.At(rule.Loops, skillEvent.Rank)) : 1;
        var rate = MotionRate(rule, skillEvent);
        _host.PlaySkillMotion(skillEvent.CasterId, plan, skillEvent.DurationMs / loops, rate, 0);
        for (var loop = 1; loop < loops; loop++)
        {
            _pending.Add(new Pending(nowMs + skillEvent.DurationMs * loop / loops, PendingKind.Loop, skillEvent, loop));
        }

        _impacts[skillEvent.CasterId] = 0;
        Looks.Cast(skillEvent, rule, nowMs);
        var position = Position(skillEvent.CasterId, skillEvent.Position);
        var facing = -skillEvent.Facing;
        if (rule.CastEffect.Length > 0 && Spawn(rule.CastEffect, skillEvent.CasterId, skillEvent.TargetId, position, facing) is { Spawned: true } cast)
        {
            _castEffects.Add((skillEvent.CasterId, cast));
        }

        _sounds.Play(rule.CastSound, position, _host.ListenerPosition);
        if (rule.Projectile && rule.ProjectileEffect.Length > 0)
        {
            _pending.Add(new Pending(nowMs + skillEvent.HitAtMs, PendingKind.Launch, skillEvent, 0));
        }
    }

    private void Launch(SkillEventInfo skillEvent, SkillRule rule)
    {
        if (!_host.TryGetPosition(skillEvent.CasterId, out var from))
        {
            return;
        }

        var to = Position(skillEvent.TargetId, skillEvent.Position);
        var planar = new Vector2(to.X - from.X, to.Z - from.Z).Length();
        var seconds = SkillProjectile.FlightMs(planar) / 1000f;
        if (Spawn(rule.ProjectileEffect, skillEvent.CasterId, skillEvent.TargetId, from, -skillEvent.Facing) is { Spawned: true } projectile)
        {
            projectile.Travel(from, to, MathF.Max(0.05f, seconds), rule.ProjectileArc);
            _projectiles[skillEvent.CasterId] = projectile;
        }
    }

    // The charge ended early: the clip jumps to the full frame and plays the rest in the time the server left.
    private void Release(SkillEventInfo skillEvent, SkillRule rule, double nowMs)
    {
        if (rule.Charge is not { } charge)
        {
            return;
        }

        var plan = SkillFormulas.Plan(rule, skillEvent.Rank);
        var remainingFrames = Math.Max(1, plan.Frames - charge.FullFrame);
        var rate = skillEvent.DurationMs > 0f
            ? Math.Clamp(remainingFrames * GameUnits.FrameMs / skillEvent.DurationMs, 0.1f, 8f)
            : charge.ReleaseSpeed / SkillFormulas.FrameStepAtRateOne;
        _host.SeekSkillMotion(skillEvent.CasterId, charge.FullFrame, rate);
        if (rule.Projectile && rule.ProjectileEffect.Length > 0)
        {
            _pending.Add(new Pending(nowMs + skillEvent.HitAtMs, PendingKind.Launch, skillEvent, 0));
        }
    }

    // A familiar's strike or a shield's counter: the strike effect from the caster to the struck target.
    private void Strike(SkillEventInfo skillEvent, SkillRule rule)
    {
        var point = Position(skillEvent.TargetId, skillEvent.Position);
        if (rule.CompanionEffect.Length > 0 && _host.TryGetPosition(skillEvent.CasterId, out var from))
        {
            // The strike flies from above the caster (the familiar, the orbs or the shield) to the struck target.
            from.Y += 50f;
            var flight = new Vector2(point.X - from.X, point.Z - from.Z).Length() / SkillProjectile.UnitsPerSecond;
            if (Spawn(rule.CompanionEffect, skillEvent.CasterId, skillEvent.TargetId, from, -skillEvent.Facing) is { } strike)
            {
                strike.Travel(from, point + new Vector3(0f, 30f, 0f), MathF.Max(0.08f, flight), 12f);
            }
        }

        _sounds.Play(rule.CompanionSound, point, _host.ListenerPosition);
    }

    // The cast's resolution mode (an element picked per cast) may bring its own impact effect.
    internal static string ImpactEffectOf(SkillRule rule, SkillEventInfo skillEvent, int impact)
    {
        if (rule.ImpactEffects.Length > 0 && rule.ImpactEffects[Math.Min(impact, rule.ImpactEffects.Length - 1)] is { Length: > 0 } perHit)
        {
            return perHit == SkillRule.Silent ? "" : perHit;
        }

        var mode = skillEvent.Flags.Mode();
        if ((skillEvent.Flags & SkillEventFlags.Charged) != 0 && rule.Charge?.Full is { ImpactEffect.Length: > 0 } full)
        {
            return full.ImpactEffect;
        }

        return mode > 0 && mode < rule.Modes.Length && rule.Modes[mode].ImpactEffect.Length > 0 ? rule.Modes[mode].ImpactEffect
            : rule.Modes.Length > 0 && rule.Modes[0].ImpactEffect.Length > 0 && mode == 0 ? rule.Modes[0].ImpactEffect : rule.ImpactEffect;
    }

    private void Impact(SkillEventInfo skillEvent, SkillRule rule)
    {
        if ((skillEvent.Flags & SkillEventFlags.Struck) != 0)
        {
            if (rule.TargetEffect.Length > 0)
            {
                Spawn(rule.TargetEffect, skillEvent.CasterId, skillEvent.TargetId, Position(skillEvent.TargetId, skillEvent.Position), -skillEvent.Facing);
            }

            return;
        }

        if ((skillEvent.Flags & SkillEventFlags.Defended) != 0)
        {
            if (rule.DefenseEffect.Length > 0)
            {
                Spawn(rule.DefenseEffect, skillEvent.CasterId, skillEvent.CasterId, Position(skillEvent.CasterId, skillEvent.Position), -skillEvent.Facing);
            }

            return;
        }

        if ((skillEvent.Flags & (SkillEventFlags.Companion | SkillEventFlags.Counter)) != 0 && rule.CompanionEffect.Length > 0)
        {
            Strike(skillEvent, rule);
            return;
        }

        if ((skillEvent.Flags & (SkillEventFlags.Chain | SkillEventFlags.Companion | SkillEventFlags.Ally)) != 0)
        {
            SecondaryImpact(skillEvent, rule);
            return;
        }

        if (_projectiles.Remove(skillEvent.CasterId, out var projectile))
        {
            projectile.Stop();
        }

        var targetsEntity = rule.Kind is SkillKind.WeaponAttack or SkillKind.MagicBolt or SkillKind.Heal
            || rule.Kind == SkillKind.Area && rule.AreaCenter == SkillAreaCenter.Target && skillEvent.TargetId != 0;
        var point = rule.Kind == SkillKind.Area && rule.AreaCenter == SkillAreaCenter.Caster
            ? Position(skillEvent.CasterId, skillEvent.Position)
            : Position(skillEvent.TargetId, skillEvent.Position);
        var impact = _impacts.GetValueOrDefault(skillEvent.CasterId);
        _impacts[skillEvent.CasterId] = impact + 1;
        Looks.Impact(skillEvent, rule);
        var impactEffect = ImpactEffectOf(rule, skillEvent, impact);
        if (impactEffect.Length > 0 && (skillEvent.TargetCount > 0 || !targetsEntity && !rule.ImpactNeedsTarget))
        {
            Spawn(impactEffect, skillEvent.CasterId, skillEvent.TargetId != 0 ? skillEvent.TargetId : skillEvent.CasterId, point, -skillEvent.Facing);
        }

        _sounds.Play(ImpactSoundOf(rule, skillEvent, impact), point, _host.ListenerPosition);
        if (rule.CameraShake)
        {
            var distance = Planar.Distance(Position(skillEvent.CasterId, skillEvent.Position).ToNumerics(), _host.ListenerPosition.ToNumerics());
            if (distance < SkillRule.CameraShakeReach)
            {
                _host.ShakeCamera((int)((SkillRule.CameraShakeReach - distance) / 15f));
            }
        }
    }

    // The impact's sound: the rule's per-hit list when it has one (landed hits only), else its single impact sound.
    private string ImpactSoundOf(SkillRule rule, SkillEventInfo skillEvent, int impact) =>
        ImpactSoundFor(rule, impact, skillEvent.TargetCount, skillEvent.Rank, _soundRandom);

    internal static string ImpactSoundFor(SkillRule rule, int impact, int targetCount, int rank, Random random)
    {
        var sounds = rule.ImpactSounds;
        if (sounds.Length == 0)
        {
            return rule.ImpactSound;
        }

        if (targetCount <= 0)
        {
            return "";
        }

        var sound = rule.ImpactSoundOrder switch
        {
            SkillSoundOrder.Random => sounds[random.Next(sounds.Length)],
            SkillSoundOrder.Final => impact + 1 >= SkillFormulas.HitCount(rule, rank) ? sounds[0] : "",
            _ => sounds[impact % sounds.Length]
        };
        return sound == SkillRule.Silent ? "" : sound;
    }

    // A chain link (an arc from the previous link to its target), a companion's strike or a support effect on an ally.
    private void SecondaryImpact(SkillEventInfo skillEvent, SkillRule rule)
    {
        if (rule.ImpactEffect.Length == 0)
        {
            return;
        }

        var target = Position(skillEvent.TargetId, skillEvent.Position);
        if ((skillEvent.Flags & SkillEventFlags.Chain) != 0)
        {
            var from = WorldSpace.Mirror(skillEvent.Position);
            if (Spawn(rule.ImpactEffect, skillEvent.CasterId, skillEvent.TargetId, from, -skillEvent.Facing) is { } link)
            {
                link.Travel(from, target, ChainLinkSeconds);
            }

            _sounds.Play(ImpactSoundFor(rule, 0, 1, skillEvent.Rank, _soundRandom), target, _host.ListenerPosition);
            return;
        }

        Spawn(ElementVariant(rule, rule.ImpactEffect, skillEvent.CasterId), skillEvent.CasterId, skillEvent.TargetId, target, -skillEvent.Facing);
        if ((skillEvent.Flags & SkillEventFlags.Companion) != 0)
        {
            _sounds.Play(ImpactSoundFor(rule, 0, 1, skillEvent.Rank, _soundRandom), target, _host.ListenerPosition);
        }
    }

    private void Cancel(ulong casterId)
    {
        Looks.Cancel(casterId);
        if (_projectiles.Remove(casterId, out var projectile))
        {
            projectile.Stop();
        }

        for (var index = _castEffects.Count - 1; index >= 0; index--)
        {
            var (caster, effect) = _castEffects[index];
            if (!effect.Alive)
            {
                _castEffects.RemoveAt(index);
            }
            else if (caster == casterId)
            {
                effect.Stop();
                _castEffects.RemoveAt(index);
            }
        }
    }

    private void ObserveCombat(CombatEventInfo combat)
    {
        var self = _host.SelfId;
        if (combat.Kind == CombatEventKind.Evade)
        {
            if (combat.TargetId == self && TryAbove(combat.TargetId, out var evaded))
            {
                Text.Add(evaded, 0, FloatingTextKind.Evade);
            }

            return;
        }

        if (combat.Kind == CombatEventKind.Revive)
        {
            if (_host.TryGetPosition(combat.TargetId, out var risen))
            {
                Spawn(RestartEffect, combat.TargetId, combat.TargetId, risen);
                if (combat.AttackerId != combat.TargetId)
                {
                    Spawn(ReviveEffect, combat.AttackerId, combat.TargetId, risen);
                }
            }

            return;
        }

        if (combat.Kind == CombatEventKind.Heal)
        {
            if (combat.Damage > 0 && TryAbove(combat.TargetId, out var healed))
            {
                Text.Add(healed, combat.Damage, FloatingTextKind.Heal);
            }

            return;
        }

        if (combat.Kind != CombatEventKind.Hit || combat.Damage <= 0 || combat.AttackerId != self && combat.TargetId != self)
        {
            return;
        }

        if (!TryAbove(combat.TargetId, out var position))
        {
            return;
        }

        var kind = combat.TargetId == self ? FloatingTextKind.DamageTaken : combat.Critical ? FloatingTextKind.Critical : FloatingTextKind.DamageDealt;
        Text.Add(position, combat.Damage, kind);
    }

    private bool TryAbove(ulong entityId, out Vector3 position)
    {
        if (!_host.TryGetPosition(entityId, out position))
        {
            return false;
        }

        position.Y += 62f;
        return true;
    }

    private void OnStatusAdded(ulong entityId, StatusEntry entry)
    {
        entry.Aura = SpawnAura(entityId, entry);
    }

    private void OnStatusRemoved(ulong entityId, StatusEntry entry)
    {
        entry.Aura.Stop();
        entry.Aura = default;
    }

    // Aura effects are finite; one that ran out while its status is still active starts again. A status that arrived before
    // its entity was shown (a viewer meeting a buffed player) starts its aura once the entity is there.
    private void KeepAuras(double nowMs)
    {
        foreach (var (entity, entry) in Statuses.All())
        {
            if (!entry.Aura.Alive && entry.RemainingMs(nowMs) > 500d)
            {
                entry.Aura = SpawnAura(entity, entry);
            }
        }
    }

    private VfxHandle SpawnAura(ulong entityId, StatusEntry entry)
    {
        if (!SkillRuleCatalog.TryGet(entry.Code, out var rule) || rule.BuffEffect.Length == 0 || !_host.TryGetPosition(entityId, out var position))
        {
            return default;
        }

        return Spawn(Variant(rule, rule.BuffEffect, entry.Value), entityId, entityId, position);
    }

    // The effect a running status of the entity names (pick: SkillRule.HitEffect or ShotEffect) in the element the status
    // drew; empty when none names one.
    public string StatusEffect(ulong entityId, Func<SkillRule, string> pick)
    {
        foreach (var entry in Statuses.For(entityId))
        {
            if (SkillRuleCatalog.TryGet(entry.Code, out var rule) && pick(rule) is { Length: > 0 } name)
            {
                return Variant(rule, name, entry.Value);
            }
        }

        return "";
    }

    // A familiar's strike in the element its status drew.
    private string ElementVariant(SkillRule rule, string name, ulong casterId)
    {
        foreach (var entry in Statuses.For(casterId))
        {
            if (entry.Code == rule.Code)
            {
                return Variant(rule, name, entry.Value);
            }
        }

        return name;
    }

    private readonly Dictionary<(string Name, int Element), string> _variants = new();

    // Effects that draw an element (enchants, companions) report it as the status value; their effects have a variant per
    // element ("<effect>.ice") when one is defined, resolved once per name and element.
    private string Variant(SkillRule rule, string name, int value)
    {
        var element = (Simulation.Combat.Element)value;
        if ((rule.Buff?.Enchant?.Choices ?? rule.Buff?.Companion?.Choices) is not { Length: > 1 } || !Enum.IsDefined(element))
        {
            return name;
        }

        if (!_variants.TryGetValue((name, value), out var resolved))
        {
            var variant = name + "." + element.ToString().ToLowerInvariant();
            resolved = Library.TryGet(variant, out _) ? variant : name;
            _variants[(name, value)] = resolved;
        }

        return resolved;
    }

    private Vector3 Position(ulong entityId, Vector3 fallbackTransport)
    {
        if (entityId != 0 && _host.TryGetPosition(entityId, out var position))
        {
            return position;
        }

        return WorldSpace.Mirror(fallbackTransport);
    }

    // Skill clips: the playback rate that makes the clip last the duration the server scheduled. Weapon clips: the
    // speed-up the server applied on top of the caster's attack rate (SkillFormulas.WeaponClipSpeedup).
    private float MotionRate(SkillRule rule, SkillEventInfo skillEvent)
    {
        var plan = SkillFormulas.Plan(rule, skillEvent.Rank);
        if (plan.Motion == SkillMotion.Skill && plan.Frames > 0 && skillEvent.DurationMs > 0f)
        {
            return Math.Clamp(plan.Frames * GameUnits.FrameMs / skillEvent.DurationMs, 0.1f, 8f);
        }

        return SkillFormulas.WeaponClipSpeedup(rule, skillEvent.Rank, _host.AttackRateOf(skillEvent.CasterId));
    }

    public void Dispose()
    {
        Renderer.Dispose();
        _sounds.Dispose();
    }

    private enum PendingKind : byte
    {
        Event,
        Loop,
        Launch
    }

    private readonly record struct Pending(double AtMs, PendingKind Kind, SkillEventInfo Event, int Loop);
}
