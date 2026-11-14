using System.Numerics;
using PristonTale.Simulation.Characters;
using PristonTale.Simulation.Skills;
using PristonTale.Simulation.Items;
using PristonTale.Simulation.Items.Usable;
using PristonTale.Simulation.PersonalShops;
using PristonTale.Simulation.World.Ai;
using PristonTale.Simulation.Combat;
using PristonTale.Simulation.Social;
using PristonTale.Simulation.World;
using PristonTale.Simulation.World.Motion;
using PristonTale.Contracts.Motion;
using PristonTale.Contracts.World;
using PristonTale.Simulation.Physics;

using PristonTale.Simulation.Modifiers;

namespace PristonTale.Application.World;

public enum MovementVerdict
{
    Accepted,
    Ignored,
    Rejected
}

public enum PlayerActionState
{
    Idle = 0,

    Swing = 1,

    Flinch = 2,

    Eat = 3,

    Cast = 4,

    Yahoo = 5,
}

public sealed class WorldPlayer
{
    public float AttackRange => PlayerAttackReach.For(_character);

    // A game master may dash (Shift held while running); its movement budget allows that speed.
    public int GameMasterLevel { get; init; }

    private readonly PlayerCharacter _character;
    private readonly CostumeCatalog _costumes;
    private bool _inVillage;

    // The client owns locomotion and reports it with MovementReport. Reports spend a distance budget that refills at
    // 1.35 x run speed and holds at most two seconds of it plus 32 units, so many small reports in one tick cannot add
    // up to more than one long report could.
    private const float ReportSpeedTolerance = 1.35f;
    private const float ReportDistanceSlack = 32f;
    private const float FirstReportSeconds = 0.5f;
    private const float MaxBudgetSeconds = 2f;
    private const float BusyCorrectionDistance = 192f;
    private const long StaleMotionMs = 1500;
    // How far above the server's height a floor still counts as the one the player stands on where a report starts; falls
    // of any height are accepted.
    private const float ReportClimb = 64f;
    // The most each 8-unit step of a report may climb: the client steps up at most GroundTraversal.StepUp (10 units)
    // every 4 units, so 20, with room for the two sides sampling the ground at different points; ReportClimb bounds the
    // whole report, this each step, so no report scales a cliff without a wall face.
    private const float StepClimb = 24f;
    // Every report is walked in pieces no longer than this, each following the client's wall slide from the floor the
    // last piece stood on, so the rising ground of a ramp is not taken for a wall. A piece may end this far from its
    // point on the reported path (a corner cut between reports); the server then keeps the slid point on its side of
    // the wall.
    private const float WallCheckDistance = 48f;
    private const float CornerCutTolerance = 8f;
    private long _lastReportMs = -1;
    private float _moveBudget;
    private long _budgetMs = -1;

    private ulong _attackTargetId;
    private bool _attackSustained;

    // A swing hits only when elapsed time crosses its authored event offset.
    // The running action's clip; Action says which action it belongs to.
    private ActionTimeline _timeline;
    private ulong _swingTargetId;

    private PotionDefinition? _pendingPotion;
    private bool _potionCheers;

    public bool HasPendingPotion => _pendingPotion is not null || Action == PlayerActionState.Eat;

    // A queued cheer starts once the motion in progress ends (FieldInstance gives it the clip's length).
    private bool _yahooPending;
    private long _yahooKeyUntilMs = long.MinValue;

    public bool HasPendingYahoo => _yahooPending;

    // Whether the cheer begun last heals the player's clan's monsters (a cheer the player asked for); one the world
    // plays for it does not.
    public bool YahooHeals { get; private set; }

    // Quest work waiting for the next upkeep, notices and dialogs for the owner, and duel bookkeeping.
    public PlayerQuestState QuestState { get; } = new();

    internal long AgingCountedAtMs { get; set; } = -1;
    internal float AgingWornMs { get; set; }
    internal long AgingCheckAtMs { get; set; }

    // A potion that cheers (the tiger capsule) ends its eating motion at the event frame and cheers instead.
    public void QueuePotion(PotionDefinition potion, bool cheers = false)
    {
        if (!IsDead && !HasPendingPotion)
        {
            _pendingPotion = potion;
            _potionCheers = cheers;
        }
    }

    public void QueueYahoo(bool heals)
    {
        if (!IsDead)
        {
            _yahooPending = true;
            _yahooQueuedHeals = heals;
        }
    }

    private bool _yahooQueuedHeals;

    // A yahoo word in the chat (key false) opens the Space window and cheers; Space (key true) cheers inside the window
    // for its stamina. Either one cheers only from standing, walking or running.
    public bool AskYahoo(bool key, long nowMs)
    {
        if (!key)
        {
            _yahooKeyUntilMs = nowMs + YahooRules.KeyWindowMs;
        }
        else if (nowMs >= _yahooKeyUntilMs)
        {
            return false;
        }

        if (IsDead || IsBusy || _yahooPending || MoveState is not (MotionState.Stand or MotionState.Walk or MotionState.Run))
        {
            return false;
        }

        if (key)
        {
            var cost = YahooRules.KeyStaminaCost(_character.MaxStamina);
            if (_character.Stamina <= cost)
            {
                return false;
            }

            _character.Stamina -= cost;
        }

        QueueYahoo(heals: true);
        return true;
    }

    public bool BeginPendingYahoo(float durationMs)
    {
        if (IsDead || IsBusy || !_yahooPending)
        {
            return false;
        }

        _yahooPending = false;
        YahooHeals = _yahooQueuedHeals;
        Action = PlayerActionState.Yahoo;
        MoveState = MotionState.Yahoo;
        _timeline = ActionTimeline.Start(durationMs);
        return true;
    }

    public bool BeginPendingPotion(float durationMs, float eventMs)
    {
        if (IsDead || IsBusy || _pendingPotion is null)
        {
            return false;
        }

        Action = PlayerActionState.Eat;
        MoveState = MotionState.Eat;
        _timeline = ActionTimeline.Start(durationMs, Math.Clamp(eventMs, 0f, MathF.Max(1f, durationMs)));
        return true;
    }

    public PotionResource TickPotion(float dtMs, ISimRng random)
    {
        if (Action != PlayerActionState.Eat || IsDead || _pendingPotion is null)
        {
            return PotionResource.None;
        }

        _timeline.Advance(dtMs);
        var restored = PotionResource.None;
        if (!_timeline.EventFired && _timeline.ElapsedMs >= _timeline.EventMs)
        {
            _timeline.FireEvent();
            restored = _pendingPotion.Restore(_character, random);
            if (_potionCheers)
            {
                _timeline.End();
                QueueYahoo(heals: true);
            }
        }

        if (_timeline.Finished)
        {
            _pendingPotion = null;
            StandIdle();
        }

        return restored;
    }

    // The length of the damage motion: the class fallback until the field sets the body's authored clip (FieldInstance.Add).
    public float FlinchMs { get; set; }

    public PlayerSkillCooldowns Cooldowns { get; } = new();
    public RegenerationState Regeneration { get; } = new();
    // A skill motion owns the avatar like a swing; FieldSkills fires its hits from the elapsed time while it casts.
    public float CastElapsedMs => _timeline.ElapsedMs;
    public bool CastCompleted { get; private set; }
    public bool IsCasting => Action == PlayerActionState.Cast;

    // Casts of each skill this session; every fifth adds proficiency to the skill's saved use counter.
    public Dictionary<uint, int> SkillCasts { get; } = new();

    // The monster this player last attacked or cast at (its familiar and passive summon fight it).
    public ulong CombatTargetId { get; set; }

    // A released charge ends the cast motion earlier.
    public void ShortenCast(float durationMs)
    {
        if (IsCasting)
        {
            _timeline.Shorten(durationMs);
        }
    }

    public void BeginCast(Vector3 facingTarget, float durationMs)
    {
        if (facingTarget != Position)
        {
            Facing = Planar.Heading(Position, facingTarget);
        }

        Action = PlayerActionState.Cast;
        MoveState = MotionState.Skill;
        CastCompleted = false;
        _timeline = ActionTimeline.Start(durationMs);
        ClearAttackTarget();
    }

    public RegenerationPosture Posture => IsDead ? RegenerationPosture.Acting : MoveState switch
    {
        MotionState.Stand => RegenerationPosture.Standing,
        MotionState.Walk => RegenerationPosture.Walking,
        MotionState.Run => RegenerationPosture.Running,
        _ => RegenerationPosture.Acting
    };

    public WorldPlayer(ulong entityId, Vector3 position, PlayerCharacter character, bool inVillage, PlayerAppearanceView appearance, CostumeCatalog? costumes = null)
    {
        if (!WorldPosition.IsValid(position))
        {
            throw new ArgumentOutOfRangeException(nameof(position));
        }

        EntityId = entityId;
        Position = position;
        _character = character;
        _costumes = costumes ?? CostumeCatalog.Empty;
        FlinchMs = MotionTiming.DurationMs(PlayerMotionFallback.GetClip(character.Class.Class, MotionCode.Damage).Frames, MotionCodes.DamageAndEatRate);
        _inVillage = inVillage;
        _appearance = appearance;
        _cueLevel = character.Level;
        _cueSkillRevision = character.Skills.Revision;
        _cueRanks = TrainedRanks();
    }

    private int _cueLevel;
    private long _cueSkillRevision;
    private int _cueRanks;

    // Whether the player gained a level or trained a skill rank since the last call, for the effects everyone near sees.
    public void TakeProgressCues(out bool leveledUp, out bool learned)
    {
        leveledUp = _character.Level > _cueLevel;
        _cueLevel = _character.Level;
        learned = false;
        if (_character.Skills.Revision != _cueSkillRevision)
        {
            _cueSkillRevision = _character.Skills.Revision;
            var ranks = TrainedRanks();
            learned = ranks > _cueRanks;
            _cueRanks = ranks;
        }
    }

    private int TrainedRanks()
    {
        var total = 0;
        foreach (var rank in _character.Skills.Ranks.Values)
        {
            total += rank;
        }

        return total;
    }

    public ulong EntityId { get; }
    public Vector3 Position { get; private set; }
    public float Facing { get; private set; }
    public MotionState MoveState { get; private set; } = MotionState.Stand;

    public ulong AttackTargetId => _attackTargetId;
    public ulong AttackSequence { get; private set; }
    private ulong _swingAttackSequence;

    public bool AcceptAttackSequence(ulong sequence)
    {
        if (sequence != 0 && sequence < AttackSequence)
        {
            return false;
        }

        AttackSequence = sequence == 0 ? AttackSequence + 1 : sequence;
        return true;
    }

    public bool AttackSustained => _attackSustained;

    public bool IsDead { get; private set; }

    public PlayerActionState Action { get; private set; }

    public bool IsBusy => Action != PlayerActionState.Idle;

    public bool InVillage => _inVillage;

    // The party the player belongs to and the request to join one it has not answered (WorldServer.Parties).
    public Party? Party { get; internal set; }
    public PartyRequest? PartyRequest { get; internal set; }

    public bool SharesPartyWith(WorldPlayer other) => Party is { } party && ReferenceEquals(party, other.Party);

    // The trade the player is in and the request to trade it has not answered (WorldServer.Trades).
    public Trade? Trade { get; internal set; }
    public TradeRequest? TradeRequest { get; internal set; }

    // An offered bag item changes only through the exchange.
    public bool Offers(Guid itemId) => Trade is { } trade && trade.OfferOf(trade.SideOf(EntityId)).Holds(itemId);

    // The personal shop the player sells from; while it is open the seller stays where it stands and acts on nothing else.
    public PersonalShop? Shop { get; private set; }

    public void OpenShop(PersonalShop shop) => Shop = shop;

    public void CloseShop() => Shop = null;

    // Drops what the seller no longer holds; a shop with nothing left closes.
    public void PruneShop()
    {
        Shop?.Prune(_character.Inventory);
        if (Shop is { IsEmpty: true })
        {
            Shop = null;
        }
    }

    private PlayerAppearanceView _appearance;
    // Built on every read from the equipment and premium state: read InVillage for the flag alone.
    public PlayerAppearanceView Appearance => _appearance with
    {
        WeaponKey = _character.GetEquipped(EquipmentSlot.Weapon)?.Definition.CatalogKey ?? "",
        ArmorKey = _character.GetEquipped(EquipmentSlot.Armor)?.Definition.CatalogKey ?? "",
        CostumeKey = _character.ActiveCostumeKey(_costumes),
        OffHandKey = _character.GetEquipped(EquipmentSlot.OffHand)?.Definition.CatalogKey ?? "",
        WeaponGlow = HeldItemGlow.For(_character.GetEquipped(EquipmentSlot.Weapon)),
        OffHandGlow = HeldItemGlow.For(_character.GetEquipped(EquipmentSlot.OffHand)),
        InVillage = _inVillage,
        Tier = _character.Tier,
        HelperPet = HelpPets.Active(_character) is { } pet ? HelpPets.KindOf(pet) : 0,
        Phoenix = _character.Premium.IsActive(PremiumEffectKind.PhoenixPet),
        BigHead = _character.Premium.BigHeadMode,
        ShopSign = Shop?.Sign ?? "",
        ClanId = Clan?.ClanId ?? 0,
        ClanName = Clan?.Name ?? "",
        ClanMark = Clan?.MarkVersion ?? 0
    };

    // The clan the player shows over its head (ClanAppService keeps it), null for none.
    public Simulation.Social.Clans.ClanTag? Clan { get; set; }

    // A hair tint changes the head every viewer draws.
    public void ChangeHair(int hairIndex)
    {
        var current = _appearance;
        _appearance = new PlayerAppearanceView(current.Name, current.Class, current.Tier, current.FaceIndex, hairIndex, current.ModelName, current.ModelName2);
    }

    // Damage is ignored while the guard runs (arrivals, revives, invulnerability items).
    public void Guard(float durationMs)
    {
        if (durationMs > GuardMsLeft)
        {
            GuardMsLeft = durationMs;
            _guardChanged = true;
        }
    }

    public int Level => _character.Level;
    public int Life => _character.Life;
    public int MaxLife => _character.MaxLife;

    public PlayerCharacter Character => _character;

    // WorldServer serializes this with the tick. Height stays authoritative; client terrain samples are not trusted.
    public MovementVerdict ReportMovement(Vector3 position, float facing, bool moving, bool running, ulong actionTarget, long nowMs,
        FieldTerrain? terrain = null)
    {
        if (!WorldPosition.IsValid(position) || !float.IsFinite(facing) || IsDead)
        {
            return IsDead ? MovementVerdict.Ignored : MovementVerdict.Rejected;
        }

        float dx = position.X - Position.X;
        float dz = position.Z - Position.Z;
        float distance = MathF.Sqrt(dx * dx + dz * dz);
        if (IsBusy || Shop is not null || _character.Effects.Has(Simulation.Modifiers.ControlFlags.Stun))
        {
            // Swings, flinches, potions, cheers, stuns and an open shop own the avatar; small drift is tolerated, large drift
            // is corrected.
            return distance > BusyCorrectionDistance ? MovementVerdict.Rejected : MovementVerdict.Ignored;
        }

        if (distance > RefillMoveBudget(nowMs))
        {
            return MovementVerdict.Rejected;
        }

        // The classic server only flagged players found off the floor; Etervale rejects the report instead.
        // A player off a ledge falls as the client shows it (Falling) from the last accepted report, so the walls below are
        // checked at the height the player is falling past rather than at the floor it will land on.
        terrain ??= FieldTerrain.None;
        var pieces = Math.Max(1, (int)MathF.Ceiling(distance / WallCheckDistance));
        var fallSeconds = _lastReportMs < 0 ? float.PositiveInfinity : Math.Max(0L, nowMs - _lastReportMs) / 1000f / pieces;
        var reached = Position;
        for (var piece = 1; piece <= pieces; piece++)
        {
            var along = piece / (float)pieces;
            var target = new Vector3(Position.X + (dx * along), reached.Y, Position.Z + (dz * along));
            var slid = terrain.Slide(reached, target);
            float missX = slid.X - target.X;
            float missZ = slid.Z - target.Z;
            if ((missX * missX) + (missZ * missZ) > CornerCutTolerance * CornerCutTolerance
                || !TryFollowFloor(terrain, reached, slid, fallSeconds, piece == pieces, out reached))
            {
                return MovementVerdict.Rejected;
            }
        }

        _moveBudget -= distance;

        if (moving && actionTarget == 0 && _attackTargetId != 0)
        {
            // A move without an action target is a ground click, which drops the attack lock.
            ClearAttackTarget();
        }

        _lastReportMs = nowMs;
        Position = reached;
        Facing = facing;

        // A character over the weight limit only walks, whatever the client reports.
        var runs = running && !CarriedWeight.IsOverweight(_character);
        MoveState = !moving ? MotionState.Stand : runs ? MotionState.Run : MotionState.Walk;
        return MovementVerdict.Accepted;
    }

    // Walks one piece in steps of the snap distance, as the client follows the ground each frame: a floor within the
    // snap distance below is stood on, a deeper one is fallen toward for the piece's share of the time. A falling body
    // may pass over ground with no floor (water, a gap between ledges), but the report must end over a floor.
    private static bool TryFollowFloor(FieldTerrain terrain, Vector3 from, Vector3 to, float seconds, bool last, out Vector3 reached)
    {
        reached = from;
        var length = new Vector2(to.X - from.X, to.Z - from.Z).Length();
        var steps = Math.Max(1, (int)MathF.Ceiling(length / Falling.SnapDistance));
        var stepSeconds = seconds / steps;
        var height = from.Y;
        var airborne = !terrain.TryFloorNear(from, ReportClimb, out var start) || Falling.IsAirborne(from.Y, start);
        for (var step = 1; step <= steps; step++)
        {
            var point = Vector3.Lerp(from, to, step / (float)steps) with { Y = height };
            if (!terrain.TryFloorNear(point, StepClimb, out var floor))
            {
                if (!airborne || (last && step == steps) || !float.IsFinite(stepSeconds))
                {
                    return false;
                }

                height -= Falling.UnitsPerSecond * stepSeconds;
                reached = point with { Y = height };
                continue;
            }

            airborne = Falling.IsAirborne(height, floor);
            height = airborne ? Falling.Step(height, floor, stepSeconds) : floor;
            reached = point with { Y = height };
        }

        return true;
    }

    private float RefillMoveBudget(long nowMs)
    {
        var moveSpeed = _character.Derived.MoveSpeed;
        float rate = (GameMasterLevel > 0 ? Locomotion.GameMasterDashUnitsPerSecond
                : CarriedWeight.IsOverweight(_character) ? Locomotion.WalkUnitsPerSecond(moveSpeed, true) : Locomotion.RunUnitsPerSecond(moveSpeed))
            * ReportSpeedTolerance;
        if (_budgetMs < 0)
        {
            _moveBudget = rate * FirstReportSeconds + ReportDistanceSlack;
        }
        else
        {
            float refill = rate * Math.Max(0L, nowMs - _budgetMs) / 1000f;
            _moveBudget = MathF.Min(rate * MaxBudgetSeconds + ReportDistanceSlack, _moveBudget + refill);
        }

        _budgetMs = nowMs;
        return _moveBudget;
    }

    // A client that stops reporting while moving is shown standing, like the original's 1.5 s motion cut-off.
    public void ExpireStaleMotion(long nowMs)
    {
        if (!IsBusy && !IsDead && MoveState is MotionState.Walk or MotionState.Run
            && _lastReportMs >= 0 && nowMs - _lastReportMs > StaleMotionMs)
        {
            MoveState = MotionState.Stand;
        }
    }

    public void SetAttackTarget(ulong targetEntityId, bool sustained)
    {
        _attackTargetId = targetEntityId;
        _attackSustained = sustained;
    }

    public void ClearAttackTarget()
    {
        _attackTargetId = 0;
        _attackSustained = false;
    }

    public void Teleport(Vector3 position)
    {
        if (!WorldPosition.IsValid(position))
        {
            throw new ArgumentOutOfRangeException(nameof(position));
        }

        Position = position;
        ClearAttackTarget();
        _lastReportMs = -1;
        _budgetMs = -1;
        // A paid potion that has not restored yet is drunk again where the player arrives; one that already restored is done.
        if (Action == PlayerActionState.Eat && _timeline.EventFired)
        {
            _pendingPotion = null;
        }

        _yahooPending = false;
        StandIdle();
    }

    public void EnterField(Vector3 position, bool inVillage)
    {
        Teleport(position);
        _inVillage = inVillage;
    }

    public void CrossGate(bool inVillage)
    {
        // A chase target belongs to the previous field.
        ClearAttackTarget();

        _inVillage = inVillage;
    }

    public float DistanceSqTo(Vector3 worldPos) => Simulation.Physics.Planar.DistanceSquared(worldPos, Position);

    public bool InAttackRange(Vector3 worldPos)
    {
        float range = AttackRange;
        return DistanceSqTo(worldPos) <= range * range && MathF.Abs(worldPos.Y - Position.Y) <= MonsterAttackReach.VerticalEntryLimit;
    }

    // Whether a swing may start now: alive, idle, not stunned and no personal shop open.
    public bool MaySwing => !IsDead && !IsBusy && !_character.Effects.Has(ControlFlags.Stun) && Shop is null;

    public void BeginSwing(ulong targetId, Vector3 targetPos, float durationMs, float hitAtMs)
    {
        Facing = Planar.Heading(Position, targetPos);
        CombatTargetId = targetId;
        StartSwing(targetId, durationMs, hitAtMs);
    }

    // A swing at nothing, the way the player faces: its hit strikes no one, and the monster last attacked stays the one
    // the player's summons and familiar fight.
    public void BeginEmptySwing(float durationMs, float hitAtMs) => StartSwing(0, durationMs, hitAtMs);

    private void StartSwing(ulong targetId, float durationMs, float hitAtMs)
    {
        Action = PlayerActionState.Swing;
        MoveState = MotionState.Attack;
        _timeline = ActionTimeline.Start(durationMs, hitAtMs);
        _swingTargetId = targetId;
        _swingAttackSequence = AttackSequence;
    }

    public bool TickAction(float dtMs, out ulong hitTargetId)
    {
        hitTargetId = 0;

        switch (Action)
        {
            case PlayerActionState.Flinch or PlayerActionState.Yahoo:
                _timeline.Advance(dtMs);
                if (_timeline.Finished)
                {
                    StandIdle();
                }

                return false;

            case PlayerActionState.Cast:
                _timeline.Advance(dtMs);
                if (_timeline.Finished)
                {
                    CastCompleted = true;
                    StandIdle();
                }

                return false;

            case PlayerActionState.Swing:
                var before = _timeline.Advance(dtMs);
                var hit = !_timeline.EventFired && _timeline.EventMs > before && _timeline.EventMs <= _timeline.ElapsedMs;
                // Malformed offsets past the clip still land once at completion.
                if (_timeline.Finished && !_timeline.EventFired)
                {
                    hit = true;
                }

                if (hit)
                {
                    _timeline.FireEvent();
                    hitTargetId = _swingTargetId;
                }

                if (_timeline.Finished)
                {
                    StandIdle();
                    if (!_attackSustained && _swingAttackSequence == AttackSequence)
                    {
                        ClearAttackTarget();
                    }
                }

                return hit;

            default:
                return false;
        }
    }

    private void StandIdle()
    {
        Action = PlayerActionState.Idle;
        MoveState = MotionState.Stand;
    }

    public void ApplyDamage(int amount)
    {
        if (amount <= 0 || IsDead || GuardMsLeft > 0f)
        {
            return;
        }

        int life = _character.Life - amount;
        if (life <= 0)
        {
            _character.Life = 0;
            IsDead = true;
            Shop = null;
            _pendingPotion = null;
            _yahooPending = false;
            Action = PlayerActionState.Idle;
            _attackTargetId = 0;
            _attackSustained = false;
            DeadMs = 0f;
            MoveState = MotionState.Dead;
        }
        else
        {
            _character.Life = life;
            if (Action == PlayerActionState.Idle && amount > 1)
            {
                Action = PlayerActionState.Flinch;
                _timeline = ActionTimeline.Start(FlinchMs);
                MoveState = MotionState.Damage;
            }
        }
    }

    public void Heal(int amount)
    {
        if (amount <= 0)
        {
            return;
        }
        _character.Life = Math.Min(MaxLife, _character.Life + amount);
    }

    // Real-clock milliseconds before which billing crystals and soul stones refuse (every crystal use restarts it).
    public long CrystalDelayUntilMs { get; set; }

    // Milliseconds since the player died.
    public float DeadMs { get; private set; }

    // A dead player stays down until it chooses where to stand up (RestartTransactions) or a revive reaches it.
    public void TickDead(float dtMs)
    {
        if (IsDead)
        {
            DeadMs += dtMs;
        }
    }

    // Stands a dead player up where the world placed it (EnterField first), with this much life, guarded for a while.
    // Mana and stamina stay as they were, as the original's restart left them.
    public void Restart(int life, float guardMs)
    {
        Revive(life, guardMs);
        _lastReportMs = -1;
        _budgetMs = -1;
    }

    public float GuardMsLeft { get; private set; }

    public void TickGuard(float dtMs)
    {
        if (GuardMsLeft > 0f)
        {
            GuardMsLeft = MathF.Max(0f, GuardMsLeft - dtMs);
            _guardChanged |= GuardMsLeft == 0f;
        }
    }

    // Ends a running guard at once (its item left behind, a game master's kill).
    public void EndGuard()
    {
        _guardChanged |= GuardMsLeft > 0f;
        GuardMsLeft = 0f;
    }

    private bool _guardChanged;

    // True once after the guard was granted, ran out or was cut short, for the field to publish it (PlayerGrace.StatusCode).
    public bool TakeGuardChange()
    {
        var changed = _guardChanged;
        _guardChanged = false;
        return changed;
    }

    // Brings a dead player back where it fell with this much life (a revive skill), guarded for a while.
    public void Revive(int life, float guardMs)
    {
        if (!IsDead)
        {
            return;
        }

        _character.Life = Math.Clamp(life, 1, MaxLife);
        IsDead = false;
        _attackTargetId = 0;
        MoveState = MotionState.Stand;
        Action = PlayerActionState.Idle;
        GuardMsLeft = MathF.Max(0f, guardMs);
        _guardChanged |= GuardMsLeft > 0f;
    }

    public EntityView ToView() => new(
        EntityId, EntityKind.Player, isSelf: false, Position, Facing, MoveState,
        Level, Life, MaxLife, Appearance,
        moveSpeed: _character.Derived.MoveSpeed,
        attackRate: _character.AttackRate,
        attackTargetId: AttackTargetId, attackSequence: AttackSequence)
    {
        Hidden = Hidden
    };

    // A game master who hid is left out of every other player's view and out of monsters' sight.
    public bool Hidden { get; set; }

    // A game master's kill: the player dies at once, whatever guard is running.
    public bool Kill()
    {
        if (IsDead)
        {
            return false;
        }

        EndGuard();
        ApplyDamage(Math.Max(1, _character.Life));
        return IsDead;
    }
}
