using System.Numerics;
using PristonTale.Simulation.Combat;
using PristonTale.Simulation.Modifiers;
using PristonTale.Simulation.Skills;
using PristonTale.Simulation.World;
using PristonTale.Simulation.World.Ai;
using PristonTale.Simulation.World.Monsters;
using PristonTale.Contracts.Motion;
using PristonTale.Contracts.Skills;

namespace PristonTale.Application.World;

// Summons: an owned monster of the world catalog with the skill's per-rank combat values, or a wild monster a charm
// turned to its caster's side. One per owner (a new call replaces it); it follows the owner, fights the owner's target
// or, when aggressive, the nearest enemy in sight, and ends when it expires, dies, its owner leaves the field or its
// status on the owner ends. The owner carries a status effect with the summon's remaining time (the keep icon); its
// kills and damage credit the owner.
internal sealed partial class FieldSkills
{
    private const float SummonSpawnOffset = 48f;
    private const int CompassPoints = 8;
    private const float SummonRetaliationLockMs = 4000f;
    private const float SummonVerticalSight = 140f;

    private readonly Dictionary<ulong, ulong> _summons = new();
    private readonly Dictionary<ulong, uint> _summonSkills = new();

    private bool CallSummon(WorldPlayer owner, SkillRule rule, int rank)
    {
        var summon = rule.Summon;
        if (summon is null || owner.InVillage || !_host.TryFindMonsterDefinition(summon.Monster, out var definition))
        {
            return false;
        }

        DismissSummon(owner.EntityId);
        var character = owner.Character;
        var combat = definition.Combat;
        var life = (summon.Life.Length == 10 ? SkillFormulas.At(summon.Life, rank) : combat.MaxLife) + character.Attributes.Strength * summon.LifePerStrength;
        var defence = (summon.Defence.Length == 10 ? SkillFormulas.At(summon.Defence, rank) : combat.Defence)
            + character.Attributes.Agility * summon.DefencePerAgility;
        var damage = summon.DamageMin.Length == 10
            ? new MonsterRange(SkillFormulas.At(summon.DamageMin, rank), SkillFormulas.At(summon.DamageMax, rank))
            : combat.AttackDamage;
        var rating = summon.AttackRating.Length == 10 ? SkillFormulas.At(summon.AttackRating, rank) : combat.AttackRatingAccuracy;
        var owned = definition with
        {
            Faction = MonsterFaction.Npc,
            Brood = MonsterBrood.User,
            Combat = combat with { MaxLife = Math.Max(1, life), Defence = defence, AttackDamage = damage, AttackRatingAccuracy = rating },
            Ai = definition.Ai with { MoveSpeed = definition.Ai.MoveSpeed * summon.MoveSpeedPercent / 100 }
        };

        var monster = _host.AddSummon(owned, SpawnPoint(owner), owner.Facing);
        monster.ModelScale = summon.ScaleBase > 0 ? (summon.ScaleBase + summon.ScalePerRank * (rank - 1)) / 256f : 0f;
        Own(owner, monster, rule.Code, rank, summon.LifetimeMs, summon.Aggressive);
        return true;
    }

    // A charmed wild monster becomes its caster's summon for the charm's duration, fighting the caster's target.
    private void AdoptSummon(WorldPlayer owner, MonsterInstance monster, uint code, int rank, float durationMs)
    {
        DismissSummon(owner.EntityId);
        Own(owner, monster, code, rank, durationMs, aggressive: false);
    }

    private void Own(WorldPlayer owner, MonsterInstance monster, uint code, int rank, float lifetimeMs, bool aggressive)
    {
        monster.OwnerId = owner.EntityId;
        monster.SummonSkill = code;
        monster.ExpiresAtMs = _host.NowMs + (long)lifetimeMs;
        monster.Aggressive = aggressive;
        monster.TargetId = 0;
        monster.AiState = MonsterAiState.Idle;
        monster.Motion = MotionState.Stand;
        _summons[owner.EntityId] = monster.EntityId;
        _summonSkills[owner.EntityId] = code;

        // The owner's keep icon shows the summon's remaining time; a charm's own effect already sits on the monster.
        var spec = new EffectSpec(code, EffectSourceKind.PlayerSkill, harmful: false, ReadOnlySpan<StatModifier>.Empty);
        if (owner.Character.Effects.Apply(spec, owner.EntityId, lifetimeMs, owner.EntityId) is { } change)
        {
            _statusEvents.Add(StatusView(change, rank));
        }
    }

    // The heading of one of the eight compass points (any whole number, taken round the compass).
    internal static float CompassAngle(ulong point) => (int)(point % CompassPoints) * MathF.Tau / CompassPoints;

    // One of the eight compass points around the owner whose floor is near the owner's height, preferring one no monster
    // stands on (several pets are not called into each other), else the owner's spot.
    private Vector3 SpawnPoint(WorldPlayer owner)
    {
        var start = _rng.NextExclusive(0, CompassPoints);
        Vector3? crowded = null;
        for (var attempt = 0; attempt < CompassPoints; attempt++)
        {
            var angle = CompassAngle((ulong)(start + attempt));
            var point = new Vector3(owner.Position.X + MathF.Sin(angle) * SummonSpawnOffset, owner.Position.Y,
                owner.Position.Z + MathF.Cos(angle) * SummonSpawnOffset);
            if (_host.Terrain.TryFloorNear(point, FieldTerrain.DropTolerance, out var height) && MathF.Abs(height - owner.Position.Y) < FieldTerrain.DropTolerance
                && !_host.Terrain.CrossesWall(owner.Position, point))
            {
                var floor = point with { Y = height };
                if (!_host.MonsterStandsAt(floor))
                {
                    return floor;
                }

                crowded ??= floor;
            }
        }

        return crowded ?? owner.Position;
    }

    // A summon too far from its owner is placed beside the owner (SpawnPoint), not on the owner's spot where the others
    // would land; false when no floor is there.
    private bool TryRecallBeside(MonsterInstance summon, WorldPlayer owner)
    {
        var point = SpawnPoint(owner);
        if (!_host.Terrain.TryFloorNear(point, FieldTerrain.DropTolerance, out var height))
        {
            return false;
        }

        summon.Position = point with { Y = height };
        summon.TargetId = 0;
        summon.AiState = MonsterAiState.Idle;
        return true;
    }

    // Ends the owner's summon: it dies where it stands and the owner's status ends.
    private void DismissSummon(ulong ownerId)
    {
        if (!_summons.Remove(ownerId, out var summonId))
        {
            return;
        }

        var code = _summonSkills.GetValueOrDefault(ownerId);
        _summonSkills.Remove(ownerId);
        if (_host.TryGetMonster(summonId, out var summon) && summon.IsAlive)
        {
            summon.ApplyDamage(summon.Life);
        }

        if (_host.TryGetPlayer(ownerId, out var owner))
        {
            RemoveEffect(owner.Character.Effects, ownerId, code);
        }
    }

    private SkillCastOutcome SetSummonMode(WorldPlayer owner, in SkillCastRequest request)
    {
        if (!_summons.TryGetValue(owner.EntityId, out var summonId) || _summonSkills.GetValueOrDefault(owner.EntityId) != request.SkillCode
            || !_host.TryGetMonster(summonId, out var summon) || !summon.IsAlive)
        {
            return SkillCastOutcome.Refused(request, SkillCastVerdict.Rejected);
        }

        summon.Aggressive = request.Command == SkillCastCommand.SummonAggressive;
        summon.TargetId = 0;
        return new SkillCastOutcome(request.Sequence, request.SkillCode, SkillCastVerdict.Accepted, owner.Character.Skills.RankOf(request.SkillCode), 0, 0,
            owner.Character.Mana, owner.Character.Stamina);
    }

    // One tick of a summon: its end conditions, the recall to a far owner, the target its orders name and the brain.
    public MonsterAttackEvent? StepSummon(MonsterInstance summon, MonsterAiTuning tuning, float dtMs)
    {
        if (_crystalPets.ContainsKey(summon.EntityId))
        {
            return StepCrystalPet(summon, tuning, dtMs);
        }

        if (_gameMasterPets.Contains(summon.EntityId))
        {
            return StepGameMasterPet(summon, tuning, dtMs);
        }

        var ownerId = summon.OwnerId;
        var current = _summons.TryGetValue(ownerId, out var id) && id == summon.EntityId;
        if (!summon.IsAlive)
        {
            if (current)
            {
                DismissSummon(ownerId);
            }

            return null;
        }

        if (!current || !_host.TryGetPlayer(ownerId, out var owner) || _host.NowMs >= summon.ExpiresAtMs)
        {
            if (current)
            {
                DismissSummon(ownerId);
            }
            else
            {
                summon.ApplyDamage(summon.Life);
            }

            return null;
        }

        if (SummonBrain.NeedsRecall(summon, owner.Position) && !TryRecallBeside(summon, owner))
        {
            DismissSummon(ownerId);
            return null;
        }

        return FollowAndFight(summon, owner, tuning, dtMs);
    }

    // A summon or pet that may go on: it follows its owner and fights the target its orders name.
    private MonsterAttackEvent? FollowAndFight(MonsterInstance summon, WorldPlayer owner, MonsterAiTuning tuning, float dtMs)
    {
        summon.SinceRetargetMs += dtMs;
        var target = SummonTarget(summon, owner, tuning);
        var orders = target is null
            ? new SummonOrders(owner.Position, default, false)
            : new SummonOrders(owner.Position, new PreyCandidate(target.EntityId, target.Position, target.IsAlive), true);
        return _summonBrain.Tick(summon, tuning, orders, dtMs);
    }

    // Aggressive: the current target while it lives, else the nearest enemy in sight on each scan. Passive: what the
    // owner fights.
    private MonsterInstance? SummonTarget(MonsterInstance summon, WorldPlayer owner, MonsterAiTuning tuning)
    {
        if (!summon.Aggressive)
        {
            return _host.TryGetMonster(owner.CombatTargetId, out var fought) && Strikeable(fought) ? fought : null;
        }

        if (summon.TargetId != 0 && _host.TryGetMonster(summon.TargetId, out var held) && Strikeable(held)
            && summon.SinceRetargetMs < MonsterAiTuning.RetargetIntervalMs)
        {
            return held;
        }

        summon.SinceRetargetMs = 0f;
        MonsterInstance? nearest = null;
        var best = (float)tuning.SightSq;
        foreach (var monster in _host.Monsters)
        {
            if (!Strikeable(monster) || MathF.Abs(monster.Position.Y - summon.Position.Y) >= SummonVerticalSight)
            {
                continue;
            }

            var distance = Vector3.DistanceSquared(monster.Position, summon.Position);
            if (distance < best)
            {
                best = distance;
                nearest = monster;
            }
        }

        return nearest;
    }

    // A summon's blow on an enemy (FieldMonsterSkills.BlowOnMonster). The struck monster turns on the summon unless its
    // player target is closer.
    public void ResolveSummonHit(MonsterInstance summon, in MonsterAttackEvent landed)
    {
        if (!summon.IsAlive || !_host.TryGetMonster(landed.TargetEntityId, out var target) || !Strikeable(target)
            || !_host.TryGetPlayer(summon.OwnerId, out var owner)
            || FieldMonsterSkills.BlowOnMonster(_rng, summon, target, landed.IsSkill) is not { } damage)
        {
            return;
        }

        Retaliate(target, summon);
        Pay(owner, target, damage, false, source: summon.EntityId);
        SlowFromWeb(summon, target);
    }

    private void Retaliate(MonsterInstance target, MonsterInstance summon)
    {
        if (target.TargetId != 0 && _host.TryGetPlayer(target.TargetId, out var prey)
            && Vector3.DistanceSquared(prey.Position, target.Position) <= Vector3.DistanceSquared(summon.Position, target.Position))
        {
            return;
        }

        MonsterAiBrain.ForceTarget(target, summon.EntityId, SummonRetaliationLockMs);
    }
}
