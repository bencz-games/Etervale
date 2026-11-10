using System.Diagnostics.CodeAnalysis;
using System.Numerics;
using PristonTale.Simulation.Characters;
using PristonTale.Simulation.Combat;
using PristonTale.Simulation.Modifiers;
using PristonTale.Simulation.Skills;
using PristonTale.Simulation.World;
using PristonTale.Simulation.World.Ai;
using PristonTale.Simulation.World.Monsters;
using PristonTale.Contracts.Skills;
using PristonTale.Simulation.Physics;

namespace PristonTale.Application.World;

internal interface IFieldSkillHost
{
    long NowMs { get; }
    FieldTerrain Terrain { get; }
    bool TryGetPlayer(ulong entityId, [MaybeNullWhen(false)] out WorldPlayer player);
    bool TryGetMonster(ulong entityId, [MaybeNullWhen(false)] out MonsterInstance monster);
    // The field's own collections: walked every tick, their struct enumerators box nothing.
    Dictionary<ulong, MonsterInstance>.ValueCollection Monsters { get; }
    Dictionary<ulong, WorldPlayer>.ValueCollection Players { get; }
    (float DurationMs, float HitAtMs) PlayerSwingTiming(WorldPlayer player);
    void EmitCombatEvent(in CombatEventView combatEvent);
    KillPayout? ApplyPlayerDamage(WorldPlayer player, MonsterInstance monster, int damage, bool crit, bool forceFlinch = false, ulong sourceEntityId = 0);
    MonsterAiTuning TuningFor(MonsterDefinition definition);
    bool TryFindMonsterDefinition(string name, [MaybeNullWhen(false)] out MonsterDefinition definition);
    MonsterInstance AddSummon(MonsterDefinition definition, Vector3 position, float facing);
    bool PlayersFight { get; }
    bool MayFight(WorldPlayer attacker, WorldPlayer target);
    void HurtPlayer(WorldPlayer attacker, WorldPlayer target, int damage, bool crit);
    bool MonsterStandsAt(Vector3 point);
}

// Server authority for player skills on one field: cast validation, the skill timeline (hits at the motion's event
// times, held charges), damage, heals, support and modifier effects, familiars, counter strikes, charms, revives and
// summons, plus the timers of every effect and the natural regeneration. It runs inside the field tick and only touches
// memory; the outcomes leave as snapshot events. The mechanics live in the partial files named after them.
internal sealed partial class FieldSkills
{
    // Distance a cast may exceed its range by: the client walks into range, the server allows one report of lag.
    public const float RangeSlack = 24f;
    public const float HeightTolerance = 100f;

    private readonly IFieldSkillHost _host;
    private readonly CombatRng _rng;
    private readonly Dictionary<ulong, ActiveCast> _casts = new();
    private readonly List<ulong> _finished = new();
    private readonly List<PendingImpact> _impacts = new();
    private readonly List<EffectChange> _changes = new();
    private readonly List<MonsterInstance?> _targets = new();
    private readonly List<SkillEventView> _skillEvents = new();
    // Casts are validated by commands between ticks; their start events join the next tick's snapshot.
    private readonly List<SkillEventView> _pendingStarts = new();
    private readonly List<StatusEventView> _statusEvents = new();
    private readonly List<KillPayout> _payouts = new();
    private readonly SummonBrain _summonBrain;

    public FieldSkills(IFieldSkillHost host, CombatRng rng)
    {
        _host = host;
        _rng = rng;
        _summonBrain = new SummonBrain(rng);
    }

    public IReadOnlyList<KillPayout> Payouts => _payouts;

    public void BeginTick()
    {
        _skillEvents.Clear();
        _skillEvents.AddRange(_pendingStarts);
        _pendingStarts.Clear();
        _statusEvents.Clear();
        _payouts.Clear();
        StartItemEffects();
    }

    public SkillEventView[] SnapshotSkillEvents() => _skillEvents.Count == 0 ? [] : _skillEvents.ToArray();

    public IReadOnlyList<StatusEventView> StatusEvents => _statusEvents;

    public StatusEventView[] SnapshotStatusEvents() => _statusEvents.Count == 0 ? [] : _statusEvents.ToArray();

    public SkillCastOutcome Cast(WorldPlayer player, in SkillCastRequest request)
    {
        if (player.IsDead)
        {
            return SkillCastOutcome.Refused(request, SkillCastVerdict.Dead);
        }

        if (request.Command != SkillCastCommand.Cast)
        {
            return Command(player, request);
        }

        if (player.IsBusy)
        {
            return SkillCastOutcome.Refused(request, SkillCastVerdict.Busy);
        }

        if (player.InVillage)
        {
            return SkillCastOutcome.Refused(request, SkillCastVerdict.InVillage);
        }

        var character = player.Character;
        var skill = FindSkill(character, request.SkillCode);
        // A summon whose monster the imported catalog lacks would spend its costs and call nothing.
        if (skill is null || !SkillRuleCatalog.TryGet(request.SkillCode, out var rule)
            || rule.Summon is { } summon && !_host.TryFindMonsterDefinition(summon.Monster, out _))
        {
            return SkillCastOutcome.Refused(request, SkillCastVerdict.NotCastable);
        }

        var verdict = SkillFormulas.CheckCaster(character, skill, rule, out var rank);
        if (verdict != SkillCastVerdict.Accepted)
        {
            return SkillCastOutcome.Refused(request, verdict);
        }

        var nowMs = _host.NowMs;
        var remaining = player.Cooldowns.RemainingMs(skill.Code, nowMs);
        if (remaining > 0)
        {
            return SkillCastOutcome.Refused(request, SkillCastVerdict.Cooldown, remaining);
        }

        verdict = ResolveTarget(player, rule, rank, request, out var targetId, out var aim);
        if (verdict != SkillCastVerdict.Accepted)
        {
            return SkillCastOutcome.Refused(request, verdict);
        }

        var timing = SkillFormulas.Timing(rule, rank, character.AttackRate, _host.PlayerSwingTiming(player));
        var charge = rule.Charge is null ? null : SkillFormulas.ChargeTiming(rule, rank);
        if (charge is not null)
        {
            timing = new SkillTiming(charge.FullDurationMs, charge.FullHitsAtMs);
        }

        RemoveReplaced(player, rule);
        character.Mana -= SkillFormulas.ManaCost(skill, rank, character.Effects.Totals);
        character.Stamina -= SkillFormulas.StaminaCost(skill, rank, character.Premium);
        var previousUses = character.Skills.UsesOf(skill.Code);
        var casts = player.SkillCasts.GetValueOrDefault(skill.Code) + 1;
        player.SkillCasts[skill.Code] = casts;
        character.Skills.RecordUses(skill.Code, SkillFormulas.UsesAfterCast(previousUses, casts, WindowIndex(character, skill.Code), rank));
        var cooldown = Simulation.Items.Usable.PremiumRules.SkillCooldownMs(SkillFormulas.CooldownMs(rule, rank, character.Attributes.Talent, (int)character.Derived.MagicMastery, previousUses), character.Premium);
        player.Cooldowns.Start(skill.Code, cooldown, nowMs);
        player.BeginCast(aim, timing.DurationMs);
        if (targetId != player.EntityId && _host.TryGetMonster(targetId, out _))
        {
            player.CombatTargetId = targetId;
        }

        var mode = rule.Modes.Length > 0 ? _rng.NextExclusive(0, rule.Modes.Length) : -1;
        var cast = new ActiveCast(rule, rank, targetId, aim, timing.HitsAtMs, request.Sequence, mode, charge);
        _casts[player.EntityId] = cast;

        var flags = SkillEventFlags.None.WithMode(mode);
        if (rule.Projectile)
        {
            flags |= SkillEventFlags.Projectile;
        }

        if (rule.Kind == SkillKind.Area)
        {
            flags |= SkillEventFlags.Area;
        }

        if (targetId == player.EntityId)
        {
            flags |= SkillEventFlags.SelfCast;
        }

        _pendingStarts.Add(new SkillEventView(player.EntityId, targetId, rule.Code, SkillEventPhase.Start, rank, flags, timing.DurationMs,
            timing.HitsAtMs.Length > 0 ? timing.HitsAtMs[0] : timing.DurationMs, aim, player.Facing, 0, AreaRadius(cast.Rule, rank, player, aim)));
        return new SkillCastOutcome(request.Sequence, rule.Code, SkillCastVerdict.Accepted, rank, cooldown, cooldown, character.Mana, character.Stamina);
    }

    // Fires the hits that came due, lands projectiles, steps familiars, summons, every effect timer and regeneration.
    public void Step(float dtMs)
    {
        var nowMs = _host.NowMs;
        _finished.Clear();
        foreach (var (casterId, cast) in _casts)
        {
            // A cast that ended without completing its motion (death, warp) is cancelled with its remaining hits.
            if (!_host.TryGetPlayer(casterId, out var caster) || caster.IsDead || !caster.IsCasting && !caster.CastCompleted)
            {
                _finished.Add(casterId);
                CancelCast(casterId, cast);
                continue;
            }

            var elapsed = caster.IsCasting ? caster.CastElapsedMs : float.MaxValue;
            if (cast.Charge is not null && !cast.Released && elapsed >= cast.Charge.FullMs)
            {
                // Held through the whole window: the charge is full.
                cast.Released = true;
                cast.Full = true;
            }

            while (cast.NextHit < cast.HitsAtMs.Length && cast.HitsAtMs[cast.NextHit] <= elapsed)
            {
                Launch(caster, cast, cast.NextHit++, nowMs);
            }

            if (cast.NextHit >= cast.HitsAtMs.Length || !caster.IsCasting)
            {
                while (cast.NextHit < cast.HitsAtMs.Length)
                {
                    Launch(caster, cast, cast.NextHit++, nowMs);
                }

                _finished.Add(casterId);
            }
        }

        foreach (var casterId in _finished)
        {
            _casts.Remove(casterId);
        }

        for (var index = _impacts.Count - 1; index >= 0; index--)
        {
            var impact = _impacts[index];
            if (impact.AtMs > nowMs)
            {
                continue;
            }

            _impacts.RemoveAt(index);
            if (_host.TryGetPlayer(impact.CasterId, out var caster) && !caster.IsDead)
            {
                Resolve(caster, impact.Cast, impact.Hit, nowMs);
            }
        }

        StepCompanions(nowMs);
        StepPulses(nowMs);
        StepEffects(dtMs);
    }

    public void Forget(ulong playerId)
    {
        _casts.Remove(playerId);
        _impacts.RemoveAll(impact => impact.CasterId == playerId);
        DismissSummon(playerId);
        ForgetSupport(playerId);
    }

    private SkillCastOutcome Command(WorldPlayer player, in SkillCastRequest request)
    {
        return request.Command switch
        {
            SkillCastCommand.Release => Release(player, request),
            SkillCastCommand.SummonPassive or SkillCastCommand.SummonAggressive => SetSummonMode(player, request),
            _ => SkillCastOutcome.Refused(request, SkillCastVerdict.Rejected)
        };
    }

    private void CancelCast(ulong casterId, ActiveCast cast)
    {
        _skillEvents.Add(new SkillEventView(casterId, cast.TargetId, cast.Rule.Code, SkillEventPhase.Cancel, cast.Rank, SkillEventFlags.None,
            0f, 0f, cast.Aim, 0f, 0, 0f));
    }

    private void Launch(WorldPlayer caster, ActiveCast cast, int hit, long nowMs)
    {
        if (!cast.Rule.Projectile)
        {
            Resolve(caster, cast, hit, nowMs);
            return;
        }

        var end = CurrentAim(cast);
        var distance = Planar.Distance(caster.Position, end);
        var flightMs = (long)SkillProjectile.FlightMs(distance);
        if (flightMs <= 0)
        {
            Resolve(caster, cast, hit, nowMs);
            return;
        }

        _impacts.Add(new PendingImpact(caster.EntityId, cast, hit, nowMs + flightMs));
    }

    private Vector3 CurrentAim(ActiveCast cast)
    {
        if (cast.TargetId != 0 && _host.TryGetMonster(cast.TargetId, out var monster))
        {
            return monster.Position;
        }

        if (cast.TargetId != 0 && _host.TryGetPlayer(cast.TargetId, out var player))
        {
            return player.Position;
        }

        return cast.Aim;
    }

    // The rule that resolves a hit: a full charge's, the cast's mode, or the skill's own.
    private static SkillRule Resolution(ActiveCast cast) =>
        cast.Full && cast.Rule.Charge?.Full is { } full ? full : cast.Mode >= 0 ? cast.Rule.Modes[cast.Mode] : cast.Rule;

    private void Resolve(WorldPlayer caster, ActiveCast cast, int hit, long nowMs)
    {
        var rule = Resolution(cast);
        var rank = cast.Rank;
        var aim = CurrentAim(cast);
        var targetCount = 0;
        switch (rule.Kind)
        {
            case SkillKind.WeaponAttack:
                targetCount = WeaponHit(caster, cast, rule, hit, nowMs) ? 1 : 0;
                if (rule.FollowArea is { } follow && hit + 1 >= rule.FollowAreaFromHit)
                {
                    targetCount += AreaHit(caster, cast, follow, aim);
                }

                break;
            case SkillKind.MagicBolt:
                targetCount = MagicHit(caster, cast, rule) ? 1 : 0;
                break;
            case SkillKind.Area:
                targetCount = AreaHit(caster, cast, rule, aim);
                if (rule.PartyEcho)
                {
                    targetCount += PartyEcho(caster, cast, rule);
                }

                break;
            case SkillKind.SelfBuff:
                targetCount = ApplySupport(caster, cast, rule, nowMs);
                break;
            case SkillKind.Debuff:
                if (_host.TryGetMonster(cast.TargetId, out var monster) && monster.IsAlive && !monster.IsSummon && Takes(rule.Buff, monster))
                {
                    ApplyEffect(monster.Effects, monster.EntityId, caster.EntityId, cast.Rule.Code, rule.Buff, rank, ResistanceOf(rule.Buff, monster),
                        caster.Character);
                    targetCount = 1;
                }

                break;
            case SkillKind.Heal:
                targetCount = rule.Allies is null ? Heal(caster, cast) ? 1 : 0 : HealAllies(caster, cast);
                break;
            case SkillKind.LifeToMana:
                targetCount = LifeToMana(caster, rule, rank) ? 1 : 0;
                break;
            case SkillKind.Resurrect:
                targetCount = Resurrect(caster, cast) ? 1 : 0;
                break;
            case SkillKind.Charm:
                targetCount = Charm(caster, cast) ? 1 : 0;
                break;
            case SkillKind.Summon:
                targetCount = CallSummon(caster, cast.Rule, rank) ? 1 : 0;
                break;
        }

        if (rule.CasterBuff is not null && (rule.Kind != SkillKind.Area || targetCount > 0) && hit == 0)
        {
            ApplyEffect(caster.Character.Effects, caster.EntityId, caster.EntityId, cast.Rule.Code, rule.CasterBuff, rank, 0, caster.Character);
        }

        var flags = rule.Kind == SkillKind.Area ? SkillEventFlags.Area : SkillEventFlags.None;
        flags |= SkillEventFlags.None.WithMode(cast.Mode);
        if (cast.Full)
        {
            flags |= SkillEventFlags.Charged;
        }

        _skillEvents.Add(new SkillEventView(caster.EntityId, cast.TargetId, cast.Rule.Code, SkillEventPhase.Impact, rank, flags, 0f, 0f, aim,
            caster.Facing, targetCount, AreaRadius(rule, rank, caster, aim)));
    }

    private bool Heal(WorldPlayer caster, ActiveCast cast)
    {
        var target = caster;
        if (cast.TargetId != 0 && cast.TargetId != caster.EntityId && (!_host.TryGetPlayer(cast.TargetId, out target) || target.IsDead))
        {
            return false;
        }

        HealPlayer(caster, target, SkillFormulas.RollHeal(caster.Character, cast.Rule, cast.Rank, _rng));
        return true;
    }

    private bool LifeToMana(WorldPlayer caster, SkillRule rule, int rank)
    {
        var character = caster.Character;
        var percent = rule.At(rule.LifeCost, rank);
        var cost = character.MaxLife * percent / 100;
        if (character.Life - cost <= 0)
        {
            return false;
        }

        character.Life -= cost;
        character.Mana = Math.Min(character.MaxMana, character.Mana + character.MaxMana * percent / 100);
        return true;
    }

    private void Pay(WorldPlayer caster, MonsterInstance monster, int damage, bool crit, bool forceFlinch = false, ulong source = 0)
    {
        if (_host.ApplyPlayerDamage(caster, monster, damage, crit, forceFlinch, source) is { } payout)
        {
            _payouts.Add(payout);
        }
    }

    private SkillCastVerdict ResolveTarget(WorldPlayer player, SkillRule rule, int rank, in SkillCastRequest request, out ulong targetId, out Vector3 aim)
    {
        targetId = 0;
        aim = player.Position;
        if (rule.RequiresSkill != 0 && player.Character.Skills.RankOf(rule.RequiresSkill) == 0)
        {
            return SkillCastVerdict.NotLearned;
        }

        if (rule.RequiresClassArmor && !WearsClassArmor(player.Character))
        {
            return SkillCastVerdict.WeaponNotAllowed;
        }

        if (rule.Allies is { RequiresParty: true } && player.Party is null)
        {
            return SkillCastVerdict.InvalidTarget;
        }

        switch (rule.Target)
        {
            case SkillTarget.None when rule.Resurrect is { } resurrect:
                if (FindDeadAlly(player, resurrect.Radius) is not { } fallen)
                {
                    return SkillCastVerdict.InvalidTarget;
                }

                targetId = fallen.EntityId;
                aim = fallen.Position;
                return SkillCastVerdict.Accepted;
            case SkillTarget.Self:
            case SkillTarget.None:
                targetId = player.EntityId;
                if (request.HasPoint && WorldPosition.IsValid(request.Point))
                {
                    aim = new Vector3(request.Point.X, player.Position.Y, request.Point.Z);
                }

                return SkillCastVerdict.Accepted;
            case SkillTarget.Enemy when _host.TryGetPlayer(request.TargetId, out var foe):
                if (!SkillFormulas.ReachesPlayers(rule) || !_host.MayFight(player, foe))
                {
                    return SkillCastVerdict.InvalidTarget;
                }

                if (!InRange(player, foe.Position, SkillFormulas.Range(rule, rank, player.Character)))
                {
                    return SkillCastVerdict.OutOfRange;
                }

                targetId = foe.EntityId;
                aim = foe.Position;
                return SkillCastVerdict.Accepted;
            case SkillTarget.Enemy:
                if (!_host.TryGetMonster(request.TargetId, out var monster) || !Strikeable(player, monster)
                    || rule.Charm is { } charm && !SkillFormulas.MatchesBrood(charm.Broods, monster.Def)
                    || rule.RefusesBosses && (monster.Def.IsBoss || monster.CastleRole is Simulation.Social.Castle.CastleObject.Door or Simulation.Social.Castle.CastleObject.Valhalla
                        or Simulation.Social.Castle.CastleObject.Crystal))
                {
                    return SkillCastVerdict.InvalidTarget;
                }

                if (!InRange(player, monster.Position, SkillFormulas.Range(rule, rank, player.Character)))
                {
                    return SkillCastVerdict.OutOfRange;
                }

                targetId = monster.EntityId;
                aim = monster.Position;
                return SkillCastVerdict.Accepted;
            case SkillTarget.Ally:
                if (request.TargetId == 0 || request.TargetId == player.EntityId)
                {
                    targetId = player.EntityId;
                    return SupportBlocked(player, rule) ? SkillCastVerdict.AlreadyActive : SkillCastVerdict.Accepted;
                }

                if (!_host.TryGetPlayer(request.TargetId, out var ally) || ally.IsDead || rule.Allies is { Party: true, AnyClicked: false } && !player.SharesPartyWith(ally)
                    || rule.AllyClasses.Length > 0 && !rule.AllyClasses.Contains(ally.Character.Class.Class))
                {
                    return SkillCastVerdict.InvalidTarget;
                }

                if (!InRange(player, ally.Position, SkillFormulas.Range(rule, rank, player.Character)))
                {
                    return SkillCastVerdict.OutOfRange;
                }

                targetId = ally.EntityId;
                aim = ally.Position;
                return SupportBlocked(ally, rule) ? SkillCastVerdict.AlreadyActive : SkillCastVerdict.Accepted;
            default:
                return SkillCastVerdict.NotCastable;
        }
    }

    // Body armor made for the caster's class only (its specialization lists no other class).
    private static bool WearsClassArmor(PlayerCharacter character) =>
        character.GetEquipped(Simulation.Characters.EquipmentSlot.Armor) is { } armor && armor.JobSpecMask == (uint)character.Definition.JobBitCode;

    private static bool InRange(WorldPlayer player, Vector3 target, float range)
    {
        var reach = range + RangeSlack;
        return player.DistanceSqTo(target) <= reach * reach && MathF.Abs(target.Y - player.Position.Y) <= HeightTolerance;
    }

    private static int WindowIndex(PlayerCharacter character, uint code)
    {
        var skills = character.Class.Skills;
        for (var index = 0; index < skills.Count; index++)
        {
            if (skills[index].Code == code)
            {
                return index;
            }
        }

        return 0;
    }

    private static Skill? FindSkill(PlayerCharacter character, uint code)
    {
        foreach (var skill in character.Class.Skills)
        {
            if (skill.Code == code && code != 0)
            {
                return skill;
            }
        }

        return null;
    }

    private sealed class ActiveCast(SkillRule rule, int rank, ulong targetId, Vector3 aim, float[] hitsAtMs, uint sequence, int mode, SkillChargeTiming? charge)
    {
        public SkillRule Rule { get; } = rule;
        public int Rank { get; } = rank;
        public ulong TargetId { get; } = targetId;
        public Vector3 Aim { get; } = aim;
        public float[] HitsAtMs { get; } = hitsAtMs;
        public uint Sequence { get; } = sequence;
        public int Mode { get; } = mode;
        public SkillChargeTiming? Charge { get; } = charge;
        public int NextHit { get; set; }
        public bool Missed { get; set; }
        public bool Released { get; set; }
        public bool Full { get; set; }
    }

    private readonly record struct PendingImpact(ulong CasterId, ActiveCast Cast, int Hit, long AtMs);
}
