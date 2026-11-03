using Microsoft.Xna.Framework;
using PristonTale.Client.Net.Presence;
using PristonTale.Engine3D.Formats;
using PristonTale.Engine3D.Rendering;
using PristonTale.Gameplay.Combat;
using PristonTale.Simulation.Characters;
using PristonTale.Contracts.Motion;
using PristonTale.Contracts.World;
using PristonTale.Contracts.Combat;
using PristonTale.Simulation.World.Ai;

namespace PristonTale.Gameplay.World.Remote;

// The server owns position, height, facing and motion state; a remote entity only plays its samples back through a
// RemoteSnapshotBuffer. The client decides two things for presentation only: Walk/Run playback speed follows the
// interpolated ground speed, and a Walk/Run state with no ground motion is shown as Stand.
public abstract class RemoteEntityBase
{
    // MonsterLocomotion's ordinary walk (one step) and run (two). Learned speeds cover faster templates.
    private const float ModelWalkSpeed = MonsterLocomotion.BaseUnitsPerSecond;
    private const float ModelRunSpeed = MonsterLocomotion.BaseUnitsPerSecond * 2;
    private const float LearnedSpeedLimit = 3f;
    private const float MinLocomotionScale = 0.5f;
    private const float MaxLocomotionScale = 2f;
    private const float StallSpeed = 5f;
    private const double StallDelayMs = 100;
    private const float RateTolerance = 0.01f;
    private const int MaxPendingEvents = 32;

    private readonly RemoteSnapshotBuffer _track;

    // How far behind the server's timeline the entity is drawn (its snapshot buffer's adaptive delay).
    public double PlaybackDelayMs => _track.DelayMs;
    private readonly Queue<(double TimeMs, CombatEventInfo Event)> _events = new();
    private readonly ActorMotionController _controller = new();
    private Vector3 _renderPosition;
    private float _renderFacing;
    private MotionCommand _pendingMotion = MotionCommand.None;
    private bool _pendingMotionStartsSound;
    private MotionCode _shownMotion;
    private float _appliedRate = float.NaN;
    private int _lastLife;
    private int _moveSpeed = 1;
    private float _attackRate = 1f;

    public float AttackRate => _attackRate;
    private float _planarSpeed;
    private float _learnedWalkSpeed;
    private float _learnedRunSpeed;
    private double _stallMs;
    private bool _stalled;
    // A skill started by the server's skill event owns the Skill state: its clip (by motion index) or the weapon clip.
    private int _skillMotionIndex = -1;
    private bool _skillAttackClip;
    private float _skillRate = 1f;

    // The motion-table slot of the clip a monster's swing plays, from the server's swing event (-1: the state's first clip).
    private int _clipIndex = -1;

    private bool SkillOverride => _skillMotionIndex >= 0 || _skillAttackClip;

    protected RemoteEntityBase(EntityKind kind, double timeMs, Vector3 position, float facing, MotionState moveState, int life,
        int soundCode = 0)
    {
        Kind = kind;
        _track = new RemoteSnapshotBuffer(new RemoteSample(timeMs, position, facing, moveState, life));
        _renderPosition = position;
        _renderFacing = facing;
        _ = _controller.ApplyAuthoritative(MotionCodes.Of(moveState));
        _shownMotion = _controller.Current;
        _lastLife = life;
        SoundCode = soundCode;
    }

    public EntityKind Kind { get; }

    // The entity's id (set for players, whose motions play their class's sounds).
    public ulong EntityId { get; init; }

    public int SoundCode { get; }

    // The monster class from its description (1 boss, 200 hammer); 0 for others.
    public int MonsterClass { get; init; }

    public bool InVillage { get; set; }

    // The ground shadow drawn while shadow maps are off: -1 none, 0 small to 3 large (players medium).
    public int ShadowSize { get; init; } = 1;

    public event Action<RemoteEntityBase, MotionCode> MotionStarted;

    public Vector3 RenderPosition => _renderPosition;

    public Vector3 DrawnPosition => _renderPosition;

    public float RenderFacing => _renderFacing;

    public Vector3 TargetPosition => _track.Latest.Position;

    public double RenderTimeMs => _track.RenderTimeMs;

    public float PlanarSpeed => _planarSpeed;

    public MotionCode Motion => _controller.Current;

    public MotionCode ShownMotion => _shownMotion;

    public bool IsDead => _controller.IsDead;

    // Drawn facing left or right only (an action field).
    public bool SideView { get; set; }

    public void SetTarget(double timeMs, Vector3 position, float facing, MotionState moveState, int life)
    {
        _ = _track.Add(new RemoteSample(timeMs, position, facing, moveState, life), _renderPosition);
    }

    public virtual void Update(float dt, double nowMs)
    {
        var elapsedMs = Math.Max(0d, dt * 1000d);
        _track.Advance(elapsedMs);
        var pose = _track.Sample(nowMs);
        ApplyDueTimeline();
        _planarSpeed = pose.PlanarSpeed;
        LearnSpeed(pose);
        UpdateStall(elapsedMs);
        _renderPosition = pose.Position;
        _renderFacing = SideView ? ActionFieldView.Facing(pose.Facing) : pose.Facing;
        ApplyWorld(Matrix.CreateRotationY(_renderFacing) * Matrix.CreateTranslation(_renderPosition));

        if (_pendingMotion.Kind != MotionCommandKind.None)
        {
            var command = _pendingMotion;
            var startsSound = _pendingMotionStartsSound;
            _pendingMotion = MotionCommand.None;
            _pendingMotionStartsSound = false;
            if (command.Motion == MotionCode.Skill && _skillMotionIndex >= 0)
            {
                ApplyMotionIndex(_skillMotionIndex);
            }
            else if (command.Motion is MotionCode.Attack or MotionCode.Skill && _clipIndex >= 0 && !SkillOverride)
            {
                ApplyMotionIndex(_clipIndex);
            }
            else if (command.Motion == MotionCode.Skill && _skillAttackClip)
            {
                ApplyMotionCommand(new MotionCommand(MotionCommandKind.Restart, MotionCode.Attack));
            }
            else
            {
                ApplyMotionCommand(command);
            }

            _shownMotion = command.Motion;
            _appliedRate = float.NaN;
            if (startsSound)
            {
                MotionStarted?.Invoke(this, command.Motion);
            }
        }

        var rate = RemoteRateFor(_shownMotion);
        if (float.IsNaN(_appliedRate) || MathF.Abs(rate - _appliedRate) > RateTolerance)
        {
            _appliedRate = rate;
            ApplyPlaybackRate(rate);
        }
    }

    public abstract int CopyActorHandles(Span<ActorHandle> destination);

    protected abstract void ApplyWorld(Matrix world);

    protected abstract void ApplyMotionCommand(MotionCommand command);

    protected abstract void ApplyMotionIndex(int motionIndex);

    // Starts a skill's motion from the server's skill event: the clip at motionIndex at the given rate, or the weapon's
    // attack clip (attackClip) at the entity's attack rate times rate.
    public void PlaySkill(int motionIndex, float rate, bool attackClip)
    {
        _skillAttackClip = attackClip;
        _skillMotionIndex = attackClip ? -1 : motionIndex;
        _skillRate = attackClip ? _attackRate * rate : rate;
        _ = _controller.ApplyAuthoritative(MotionCode.Skill);
        _pendingMotion = new MotionCommand(MotionCommandKind.Restart, MotionCode.Skill);
        _pendingMotionStartsSound = false;
    }

    protected abstract void ApplyPlaybackRate(float rate);

    public abstract void Dispose();

    public void SetRates(int moveSpeed, float attackRate)
    {
        _moveSpeed = moveSpeed;
        _attackRate = float.IsFinite(attackRate) && attackRate > 0f ? attackRate : 1f;
    }

    // Combat events share the sample timeline, so a swing restarts the clip the state already started at that time.
    public void ApplyCombatEvent(CombatEventInfo combatEvent, double timeMs)
    {
        if (_events.Count >= MaxPendingEvents)
        {
            ApplyCombatEventNow(_events.Dequeue().Event);
        }

        _events.Enqueue((timeMs, combatEvent));
    }

    private void ApplyDueTimeline()
    {
        while (_track.TryTakeDueState(out var sample))
        {
            ApplyDueEvents(sample.TimeMs, inclusive: false);
            ApplySampleState(sample);
        }

        ApplyDueEvents(_track.RenderTimeMs, inclusive: true);
    }

    private void ApplyDueEvents(double limitMs, bool inclusive)
    {
        while (_events.Count > 0)
        {
            var next = _events.Peek();
            if (next.TimeMs > limitMs || !inclusive && next.TimeMs == limitMs)
            {
                return;
            }

            _ = _events.Dequeue();
            ApplyCombatEventNow(next.Event);
        }
    }

    private void ApplySampleState(in RemoteSample sample)
    {
        var motion = MotionCodes.Of(sample.MoveState);
        if (motion == MotionCode.Skill && SkillOverride)
        {
            _lastLife = sample.Life;
            return;
        }

        var command = _controller.ApplyAuthoritative(motion);
        if (command.Kind == MotionCommandKind.None
            && motion == MotionCode.Damage
            && _controller.Current == MotionCode.Damage
            && sample.Life < _lastLife)
        {
            command = _controller.Retrigger(MotionCode.Damage);
        }

        QueueMotion(command, startsSound: true);
        _lastLife = sample.Life;
    }

    private void ApplyCombatEventNow(CombatEventInfo combatEvent)
    {
        if (combatEvent.Kind == CombatEventKind.PotionBegin)
        {
            QueueMotion(_controller.Retrigger(MotionCode.Eat), startsSound: true);
        }
        else if (combatEvent.Kind == CombatEventKind.SwingBegin && Motion is MotionCode.Attack or MotionCode.Skill)
        {
            QueueMotion(_controller.Retrigger(Motion), startsSound: true);
            _clipIndex = combatEvent.Clip;
        }
        else if (combatEvent.Kind == CombatEventKind.Hit && !combatEvent.Killed && Motion == MotionCode.Damage)
        {
            QueueMotion(_controller.Retrigger(MotionCode.Damage), startsSound: true);
        }
    }

    private void UpdateStall(double elapsedMs)
    {
        var locomotion = _controller.Current is MotionCode.Walk or MotionCode.Run;
        _stallMs = locomotion && _planarSpeed < StallSpeed ? _stallMs + elapsedMs : 0d;
        var stalled = locomotion && _stallMs >= StallDelayMs;
        if (stalled == _stalled)
        {
            return;
        }

        _stalled = stalled;
        if (locomotion)
        {
            QueueMotion(new MotionCommand(MotionCommandKind.Set, _controller.Current), startsSound: false);
        }
    }

    private void QueueMotion(MotionCommand command, bool startsSound)
    {
        if (command.Kind == MotionCommandKind.None)
        {
            return;
        }

        if (_stalled && command.Motion is MotionCode.Walk or MotionCode.Run)
        {
            command = new MotionCommand(MotionCommandKind.Set, MotionCode.Stand);
        }

        if (command.Motion != MotionCode.Skill)
        {
            _skillMotionIndex = -1;
            _skillAttackClip = false;
        }

        if (command.Motion is not (MotionCode.Attack or MotionCode.Skill))
        {
            _clipIndex = -1;
        }

        if (command.Kind == MotionCommandKind.Set && command.Motion == _shownMotion)
        {
            _pendingMotion = MotionCommand.None;
            _pendingMotionStartsSound = false;
            return;
        }

        _pendingMotion = command;
        _pendingMotionStartsSound = startsSound;
    }

    private void LearnSpeed(in RemotePose pose)
    {
        if (pose.Kind != RemotePoseKind.Interpolated || !pose.SteadyState || Kind == EntityKind.Player)
        {
            return;
        }

        if (_shownMotion == MotionCode.Walk)
        {
            _learnedWalkSpeed = Math.Max(_learnedWalkSpeed, Math.Min(pose.PlanarSpeed, ModelWalkSpeed * LearnedSpeedLimit));
        }
        else if (_shownMotion == MotionCode.Run)
        {
            _learnedRunSpeed = Math.Max(_learnedRunSpeed, Math.Min(pose.PlanarSpeed, ModelRunSpeed * LearnedSpeedLimit));
        }
    }

    protected float RemoteRateFor(MotionCode motion)
    {
        return motion switch
        {
            MotionCode.Walk or MotionCode.Run => BaseLocomotionRate(motion) * LocomotionScale(motion),
            MotionCode.Skill when SkillOverride => _skillRate,
            MotionCode.Attack or MotionCode.Skill => _attackRate,
            _ => 1f
        };
    }

    // The clip plays at its original rate at the entity's nominal speed and slows or speeds up with the real ground speed.
    private float LocomotionScale(MotionCode motion)
    {
        var reference = ReferenceSpeed(motion);
        return reference > 0f ? Math.Clamp(_planarSpeed / reference, MinLocomotionScale, MaxLocomotionScale) : 1f;
    }

    private float ReferenceSpeed(MotionCode motion)
    {
        var walk = motion == MotionCode.Walk;
        if (Kind == EntityKind.Player)
        {
            return walk ? Locomotion.WalkUnitsPerSecond(_moveSpeed, InVillage) : Locomotion.RunUnitsPerSecond(_moveSpeed);
        }

        return walk ? Math.Max(ModelWalkSpeed, _learnedWalkSpeed) : Math.Max(ModelRunSpeed, _learnedRunSpeed);
    }

    private float BaseLocomotionRate(MotionCode motion)
    {
        if (Kind != EntityKind.Player)
        {
            return 1f;
        }

        return Locomotion.StepRate(_moveSpeed, motion != MotionCode.Walk);
    }
}
