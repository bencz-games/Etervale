using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using PristonTale.Client.Net.Presence;
using PristonTale.Engine3D.Formats;
using PristonTale.Engine3D.Rendering;
using PristonTale.Gameplay.Effects;
using PristonTale.Gameplay.World.Remote;
using PristonTale.Simulation.Skills;
using NumVector3 = System.Numerics.Vector3;
using PristonTale.Contracts.Motion;
using PristonTale.Contracts.World;
using PristonTale.Contracts.Skills;
using PristonTale.Engine3D;
using PristonTale.Gameplay.UI.Widgets;

namespace PristonTale.Gameplay.World;

// Skill casting and presentation of the scene: the mouse sockets cast the selected skills the way the original does
// (right or left click on a monster, on a player or anywhere for self skills), walking into range first; the server's
// skill timeline drives motions and effects through SkillPresentation.
public sealed partial class GameScene : ISkillSceneHost
{
    // The refusals a cast meets, from the server or the client's own checks.
    private const string NotReadyText = "The skill is not ready yet.";
    private const string NoManaText = "Not enough mana.";
    private const string NoStaminaText = "Not enough stamina.";

    // A cast the server has not answered yet keeps the avatar still for at most this long.
    private const double CastAnswerTimeoutMs = 1500d;
    private const float RangeMargin = 8f;

    private SkillPresentation _skillFx;
    private ActorLookPresenter _looks;
    private readonly CostumeWings _wings = new();
    private SkillSoundBank _skillSounds;
    private double _skillClockMs;
    private uint _castSequence;
    private double _castLockUntilMs;
    private PendingCast? _pendingCast;
    // A held charge of the local player: the skill and whether its mouse button is the right one.
    private uint _chargeSkill;
    private bool _chargeRight;
    private readonly Dictionary<uint, bool> _summonAggressive = new();

    public SkillPresentation Skills => _skillFx;

    public ulong SelfEntityId => _remote?.SelfEntityId ?? 0;

    // The skills bound to the left and right mouse sockets; 0 or the basic attack id leaves the socket to attacks.
    public uint LeftSkill { get; set; }
    public uint RightSkill { get; set; }

    // Raised when the client refuses a cast before sending it (not ready, no mana); the HUD shows the reason.
    public event Action<string> SkillNotice;

    public bool SelfCasting => _skillClockMs < _castLockUntilMs;

    ulong ISkillSceneHost.SelfId => _remote?.SelfEntityId ?? 0;

    Vector3 ISkillSceneHost.ListenerPosition => _playerBody?.Position ?? Vector3.Zero;

    private void CreateSkillPresentation()
    {
        if (_skillFx is not null || _effectLibrary is null || _renderer is null)
        {
            return;
        }

        _skillSounds = new SkillSoundBank(_assetRoot ?? "", _log);
        _skillFx = new SkillPresentation(_graphicsDevice, _content, _effectLibrary, _renderer.TransientLights, this, _skillSounds, _log);
        _skillFx.CastResolved += OnCastResolved;
        CreateMonsterPresentation();
        CreateCombatSounds();
        _looks = new ActorLookPresenter(_actorScene, _actorLoader, _assetRoot ?? "");
    }

    void ISkillSceneHost.ApplyLook(ulong entityId, in EntityLook look)
    {
        if (_looks is null || _remote is null)
        {
            return;
        }

        if (entityId == _remote.SelfEntityId)
        {
            var handles = _equipmentPresentation?.Handles ?? PlayerActors;
            _looks.Apply(entityId, handles, _equipmentPresentation?.HoldsShield == true, look);
            return;
        }

        _ = _remote.TryGetLookHandles(entityId, out var remote, out var shield);
        _looks.Apply(entityId, remote, shield, look);
    }

    private void ObserveSkills(PresenceUpdate update) => _skillFx?.Observe(update, _skillClockMs);

    private void UpdateSkills(GameTime gameTime)
    {
        _skillFx?.Update((float)gameTime.ElapsedGameTime.TotalSeconds, _skillClockMs);
        UpdateCostumeWings();
        UpdateHeldItems((float)gameTime.ElapsedGameTime.TotalSeconds);
        UpdateTerrain((float)gameTime.ElapsedGameTime.TotalSeconds);
    }

    private void UpdateCostumeWings()
    {
        if (_skillFx is null || _remote is null)
        {
            return;
        }

        if (_playerBody is not null && _equipmentPresentation is not null)
        {
            var village = _activeField?.Definition.State == Simulation.World.FieldState.Village;
            _wings.Wear(_skillFx, _remote.SelfEntityId, _equipmentPresentation.ArmorKey, !village && !_playerController.IsDead, _playerBody.Position,
                PlayerYaw);
        }

        _remote.ForEachWearer(_wearRemote ??= WearRemote);
        _wings.EndFrame();
    }

    private Action<ulong, string, bool, float> _wearRemote;

    private void WearRemote(ulong entityId, string armorKey, bool shown, float yaw)
    {
        if (_remote.TryGetDrawnPosition(entityId, out var position))
        {
            _wings.Wear(_skillFx, entityId, armorKey, shown, position, yaw);
        }
    }

    private void DrawSkillEffects()
    {
        if (_skillFx is not null && _camera is not null)
        {
            QueueBlobShadows();
            QueueFootprints();
            _shots.Draw(_skillFx.Renderer, _skillFx.Library);

            _skillFx.Draw(_camera.View, _camera.Projection, _camera.Position, _renderer.SceneLinearDepth);
        }
    }

    public void DrawCombatText(SpriteBatch batch, SpriteFont font)
    {
        if (_skillFx is not null && _camera is not null)
        {
            _skillFx.Text.Draw(batch, font, _graphicsDevice.Viewport, _camera.View, _camera.Projection);
        }
    }

    private void OnCastResolved(SkillCastResultInfo result)
    {
        if (result.Accepted && _chargeSkill == 0 && SkillRuleCatalog.TryGet(result.SkillCode, out var rule) && rule.Charge is not null
            && result.CooldownMs > 0)
        {
            _chargeSkill = result.SkillCode;
            _chargeRight = RightSkill == result.SkillCode;
        }

        if (result.Accepted && result.CooldownMs > 0 && SkillRuleCatalog.TryGet(result.SkillCode, out var called) && called.Summon is not null)
        {
            ResetSummonMode(result.SkillCode);
        }

        if (!result.Accepted)
        {
            _castLockUntilMs = 0d;
            SkillNotice?.Invoke(result.Status switch
            {
                SkillCastVerdict.Cooldown => NotReadyText,
                SkillCastVerdict.NotEnoughMana => NoManaText,
                SkillCastVerdict.NotEnoughStamina => NoStaminaText,
                SkillCastVerdict.OutOfRange => "The target is out of range.",
                SkillCastVerdict.WeaponNotAllowed => "This skill needs another weapon.",
                SkillCastVerdict.InVillage => "Skills cannot be used in town.",
                SkillCastVerdict.AlreadyActive => "That effect is still active.",
                SkillCastVerdict.InvalidTarget => "There is no valid target for this skill.",
                _ => "The skill cannot be used now."
            });
        }
    }

    // A click on the scene with a skill in the clicked socket. Returns true when the click was taken by the skill.
    private bool TryCastSkillClick(MouseState mouse, bool rightButton)
    {
        var code = rightButton ? RightSkill : LeftSkill;
        if (code is 0 or HudSkillBindings.BasicAttackId || _player is null || _remote is null || !SkillRuleCatalog.TryGet(code, out var rule))
        {
            return false;
        }

        ulong target = 0;
        var kind = EntityKind.Monster;
        if (TryCursorRay(mouse, out var ray) && _remote.TryPick(ray, _remote.SelfEntityId, out var picked, out var pickedKind))
        {
            target = picked;
            kind = pickedKind;
        }

        switch (rule.Target)
        {
            case SkillTarget.Self:
            case SkillTarget.None:
                return RequestCast(code, rule, 0, null);
            case SkillTarget.Enemy:
                if (target == 0 || kind != EntityKind.Monster)
                {
                    return false;
                }

                _targetSelection.Trace(target);
                return RequestCast(code, rule, target, null);
            case SkillTarget.Ally:
                return RequestCast(code, rule, target != 0 && kind == EntityKind.Player ? target : 0, null);
            default:
                return false;
        }
    }

    private bool RequestCast(uint code, SkillRule rule, ulong target, NumVector3? point)
    {
        var rank = Rank(code);
        var skill = FindSkill(code);
        if (rank <= 0 || skill is null)
        {
            SkillNotice?.Invoke("Learn this skill at a skill master first.");
            return true;
        }

        if (!_skillFx.Cooldowns.Ready(code, _skillClockMs))
        {
            SkillNotice?.Invoke(NotReadyText);
            return true;
        }

        _lootInteraction?.Cancel();
        ClearAttackTarget();
        _pendingCast = new PendingCast(code, rule, rank, target);
        UpdatePendingCast();
        return true;
    }

    // Walks toward the pending target and casts once it is in range, like the original's approach before a skill.
    private void UpdatePendingCast()
    {
        if (_pendingCast is not { } pending || _playerBody is null || SelfCasting || _playerController.LocomotionFrozen || SelfStunned)
        {
            return;
        }

        if (pending.TargetId == 0 || pending.TargetId == _remote.SelfEntityId)
        {
            Send(pending, 0);
            return;
        }

        if (!_remote.TryGetTarget(pending.TargetId, out var targetPosition, out var dead) || dead)
        {
            _pendingCast = null;
            return;
        }

        var range = CastRange(pending.Rule, pending.Rank);
        var planar = new Vector2(targetPosition.X, targetPosition.Z);
        var offset = planar - _playerBody.PlanarPosition;
        if (offset.Length() <= MathF.Max(range - RangeMargin, 10f))
        {
            _hasMoveTarget = false;
            FaceToward(planar);
            Send(pending, pending.TargetId);
            return;
        }

        // The approach runs every frame: only a new destination (or a target that moved away from it) is reported.
        if (!_hasMoveTarget || Vector2.DistanceSquared(_moveTarget, planar) > ApproachReportDistance * ApproachReportDistance)
        {
            _reports.Request();
        }

        _moveTarget = planar;
        _hasMoveTarget = true;
    }

    private void Send(PendingCast pending, ulong target)
    {
        _pendingCast = null;
        if (_playChannel is null)
        {
            return;
        }

        // Before the lock, which keeps StopPredictedMovement from standing: a refused cast never starts a clip that would
        // end the walk.
        StopPredictedMovement();
        _castLockUntilMs = _skillClockMs + CastAnswerTimeoutMs;
        _hasMoveTarget = false;
        ReportMovementNow();
        var planar = _playerBody.PlanarPosition;
        var point = WorldSpace.Mirror(new NumVector3(planar.X, _playerBody.Height, planar.Y));
        SendUpstream(_playChannel.SendSkillCastAsync(++_castSequence, pending.Code, target, point, _presenceCts.Token), "skill cast");
    }

    private void CancelPendingCast() => _pendingCast = null;

    // The distance the avatar walks into before casting, as the server measures it.
    private float CastRange(SkillRule rule, int rank) => SkillFormulas.Range(rule, rank, Owner?.AttackRange ?? PristonTale.Simulation.Combat.MeleeReach.PlayerBaseRange);

    // Releasing the mouse button of a held charge tells the server; holding through the window charges fully on its own.
    private void UpdateCharge(MouseState mouse)
    {
        if (_chargeSkill == 0)
        {
            return;
        }

        var held = _chargeRight ? mouse.RightButton == ButtonState.Pressed : mouse.LeftButton == ButtonState.Pressed;
        if (held && SelfCasting)
        {
            return;
        }

        var code = _chargeSkill;
        _chargeSkill = 0;
        if (SelfCasting && _playChannel is not null)
        {
            SendUpstream(_playChannel.SendSkillCommandAsync(++_castSequence, code, SkillCastCommand.Release, _presenceCts.Token),
                "skill release");
        }
    }

    // A double click on a summon's buff icon switches it between fighting its owner's target and picking its own.
    public void ToggleSummonMode(uint skillCode)
    {
        if (_playChannel is null || !SkillRuleCatalog.TryGet(skillCode, out var rule) || rule.Summon is null)
        {
            return;
        }

        var aggressive = !_summonAggressive.GetValueOrDefault(skillCode, rule.Summon.Aggressive);
        _summonAggressive[skillCode] = aggressive;
        var command = aggressive ? SkillCastCommand.SummonAggressive : SkillCastCommand.SummonPassive;
        SendUpstream(_playChannel.SendSkillCommandAsync(++_castSequence, skillCode, command, _presenceCts.Token), "summon mode");
        SkillNotice?.Invoke(aggressive ? $"{rule.Name}: attacks any enemy nearby." : $"{rule.Name}: attacks only your target.");
    }

    // A confirmed right click on a crystal pet's icon dismisses the pet of that status.
    public void DismissCrystalPets(uint statusCode)
    {
        if (_playChannel is null)
        {
            return;
        }

        SendUpstream(_playChannel.SendDismissCrystalPetsAsync(statusCode, _presenceCts.Token), "crystal pet dismissal");
        SkillNotice?.Invoke("Pet has been dismissed");
    }

    // True when the summon skill's monster picks its own enemies, false when it fights only the owner's target; null
    // for skills that summon nothing.
    public bool? SummonAutomatic(uint skillCode) =>
        SkillRuleCatalog.TryGet(skillCode, out var rule) && rule.Summon is not null
            ? _summonAggressive.GetValueOrDefault(skillCode, rule.Summon.Aggressive)
            : null;

    // The summon of a new cast starts in its skill's own mode.
    private void ResetSummonMode(uint skillCode) => _summonAggressive.Remove(skillCode);
    private int Rank(uint code)
    {
        if (Owner is null)
        {
            return 0;
        }

        foreach (var skill in Owner.Skills)
        {
            if (skill.Code == code)
            {
                return skill.Rank;
            }
        }

        return 0;
    }

    private Skill FindSkill(uint code)
    {
        foreach (var skill in _player.Class.Skills)
        {
            if (skill.Code == code && code != 0)
            {
                return skill;
            }
        }

        return null;
    }

    bool PristonTale.Effects.Vfx.IVfxAnchors.TryGetPosition(ulong entityId, out Vector3 position) => TryGetDrawnPosition(entityId, out position);

    bool PristonTale.Effects.Vfx.IVfxAnchors.TryGetBone(ulong entityId, string bone, out Vector3 position)
    {
        var found = ((PristonTale.Effects.Vfx.IVfxAnchors)this).TryGetBoneWorld(entityId, bone, out var world);
        position = world.Translation;
        return found;
    }

    bool PristonTale.Effects.Vfx.IVfxAnchors.TryGetBoneWorld(ulong entityId, string bone, out Matrix world)
    {
        world = Matrix.Identity;
        if (_renderer is null || _remote is null)
        {
            return false;
        }

        ActorHandle actor;
        if (entityId == _remote.SelfEntityId)
        {
            actor = _playerBodyActor;
        }
        else if (!_remote.TryGetBodyActor(entityId, out actor))
        {
            return false;
        }

        return _renderer.TryGetActorBoneWorld(actor, bone, out world);
    }

    void ISkillSceneHost.SeekSkillMotion(ulong entityId, int framesFromStart, float rate)
    {
        if (_remote is null)
        {
            return;
        }

        if (entityId != _remote.SelfEntityId)
        {
            _remote.SeekSkillMotion(entityId, framesFromStart, rate);
            return;
        }

        if (_renderer is null || !_playerBodyActor.IsValid)
        {
            return;
        }

        PlayerActors.SeekMotion(_actorScene, framesFromStart);
        PlayerActors.SetPlaybackRate(_actorScene, rate);
    }

    // The original's skill shake swings the view every third frame.
    void ISkillSceneHost.ShakeCamera(int factor) => _camera?.Wave(factor, 2);

    double ISkillSceneHost.PlaybackDelayMsOf(ulong entityId) =>
        _remote?.PlaybackDelayMsOf(entityId) ?? World.Remote.RemoteSnapshotBuffer.BaseDelayMs;

    float ISkillSceneHost.AttackRateOf(ulong entityId) =>
        entityId == _remote?.SelfEntityId ? PlayerRateFor(MotionCode.Attack) : _remote?.AttackRateOf(entityId) ?? 1f;

    void ISkillSceneHost.PlaySkillMotion(ulong entityId, SkillMotionPlan plan, float durationMs, float rate, int loop)
    {
        if (_remote is null)
        {
            return;
        }

        if (entityId != _remote.SelfEntityId)
        {
            _remote.PlaySkillMotion(entityId, plan.MotionIndex, rate, plan.Motion == SkillMotion.Attack);
            return;
        }

        if (_renderer is null || !_playerBodyActor.IsValid || _playerController.IsDead)
        {
            return;
        }

        _castLockUntilMs = Math.Max(_castLockUntilMs, _skillClockMs + durationMs);
        _hasMoveTarget = false;
        if (plan.Motion == SkillMotion.Skill)
        {
            _ = _playerController.Retrigger(MotionCode.Skill);
            var clip = _renderer.ResolveSkillMotion(_playerBodyActor, plan.MotionIndex);
            PlayerActors.SetMotionIndex(_actorScene, clip);
            PlayerActors.SetPlaybackRate(_actorScene, rate);
            return;
        }

        ApplyPlayerCommand(_playerController.Retrigger(MotionCode.Attack));
        PlayerActors.SetPlaybackRate(_actorScene, PlayerRateFor(MotionCode.Attack) * rate);
    }

    // The skill clip returns to stand by itself; the controller follows when the cast lock ends, or a cast with no walk
    // after it would keep the motions-from-0x100 rules (no floor following, no falls) until the next step.
    private void EndSkillMotion()
    {
        if (_playerController.Current == MotionCode.Skill && !SelfCasting)
        {
            ApplyPlayerCommand(_playerController.ApplyAuthoritative(MotionCode.Stand));
        }
    }

    private readonly record struct PendingCast(uint Code, SkillRule Rule, int Rank, ulong TargetId);
}
