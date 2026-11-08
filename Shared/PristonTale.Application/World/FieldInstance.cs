using PristonTale.Simulation.Modifiers;
using PristonTale.Simulation.World;
using PristonTale.Simulation.Items;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Numerics;
using PristonTale.Simulation.Characters;
using PristonTale.Simulation.Combat;
using PristonTale.Simulation.Physics;
using PristonTale.Simulation.World.Ai;
using PristonTale.Simulation.World.Fields;
using PristonTale.Simulation.World.Monsters;
using PristonTale.Simulation.World.Motion;
using PristonTale.Simulation.World.Spawning;
using PristonTale.Simulation.World.Npcs;
using PristonTale.Simulation.Skills;
using PristonTale.Contracts.Motion;
using PristonTale.Contracts.World;
using PristonTale.Contracts.Combat;
using PristonTale.Contracts.Skills;
using PristonTale.Contracts.Characters;

namespace PristonTale.Application.World;

public sealed partial class FieldInstance : ISpawnSink, IPlayerProximity, IFieldCombatHost, IFieldSkillHost
{
    public const int PlayerNearRadiusSquared = ViewRanges.ConnectSquared;
    public static readonly float PlayerNearRadius = ViewRanges.Connect;

    private readonly Dictionary<ulong, WorldPlayer> _players = new();
    private readonly Dictionary<ulong, MonsterInstance> _monsters = new();
    private readonly Dictionary<ulong, WorldNpc> _npcs = new();

    // Reused scratch (tick thread only) so Step allocates no per-tick garbage on the hot path.
    private readonly List<MonsterInstance> _liveScratch = new();
    private readonly List<MonsterInstance> _expiredScratch = new();
    private readonly List<MonsterInstance> _summonScratch = new();
    private readonly List<EntityNeighbour> _neighbourScratch = new();
    private readonly FieldPreyView _prey = new();
    private readonly MonsterOccupancy _occupancy;
    private readonly SpatialIndex _collisionIndex = new();
    private readonly List<SpatialEntry> _collisionQuery = new();
    private readonly Dictionary<int, MonsterAiTuning> _tuningByCode = new();
    // Null timing entries use the fallback clips and are cached as misses.
    private readonly Dictionary<PlayerClass, MotionTimingSet?> _playerTimingByClass = new();
    // Snapshots copy this tick-owned list before asynchronous publication.
    private readonly List<CombatEventView> _events = new();

    private static readonly CharacterClassRegistry ClassRegistry = new();

    private readonly FieldSpawner _spawner;
    private readonly IReadOnlyList<SpawnPoint> _spawnPoints;
    private readonly MonsterAiBrain _brain;
    private readonly FieldCombat _combat;
    private readonly FieldSkills _skills;
    private readonly FieldMonsterSkills _monsterSkills;
    private readonly EntityIdAllocator _ids;
    private readonly CombatRng _rng;
    private readonly MonsterCatalog _catalog;
    private readonly MotionTimingCatalog _motions;
    private double _nowMs;

    public FieldInstance(int fieldId)
        : this(fieldId, FieldSpawnData.Empty, new MonsterCatalog([]), new EntityIdAllocator(), new CombatRng(fieldId), terrain: null)
    {
    }

    public FieldInstance(
        int fieldId, FieldSpawnData spawnData, MonsterCatalog catalog, EntityIdAllocator ids, CombatRng rng,
        MotionTimingCatalog? motions = null, NpcCatalog? npcs = null, int experienceMultiplier = 1, ItemCatalog? items = null,
        FieldTerrain? terrain = null)
    {
        FieldId = fieldId;
        Terrain = terrain ?? FieldTerrain.None;
        GroundLoot = new FieldGroundLoot(items ?? new ItemCatalog([]), Terrain);
        _ids = ids;
        _rng = rng;
        _catalog = catalog;
        _motions = motions ?? MotionTimingCatalog.Empty;
        _ = PlayerEquipmentTiming.Count;
        _occupancy = new MonsterOccupancy(_neighbourScratch);
        _spawner = new FieldSpawner(spawnData, catalog, rng);
        _spawnPoints = spawnData.SpawnPoints;
        _brain = new MonsterAiBrain(rng);
        _combat = new FieldCombat(this, rng, experienceMultiplier) { ActionField = fieldId == FieldCodes.FallGame };
        _skills = new FieldSkills(this, rng);
        _combat.SkillCombat = _skills;
        _monsterSkills = new FieldMonsterSkills(this, rng) { Combat = _combat };
        _combat.MonsterSkills = _monsterSkills;
        Premium = new FieldPremium(fieldId, this);
        _combat.PlayerHitLanded = Premium.RecordHit;
        _combat.DamageDealt = RecordSiegeDamage;
        _combat.PlayerHurtPlayer = RecordSiegeBlow;
        _combat.HammerLanded = OnHammerLanded;
        foreach (var placement in spawnData.FixedNpcs)
        {
            if (npcs is null || !npcs.TryGet(placement.NpcCode, out var definition))
            {
                throw new InvalidDataException($"Field {fieldId} references missing NPC {placement.NpcCode}.");
            }

            _motions.TryGet(definition.ModelInxPath, out var motionTimings);
            var npc = WorldNpc.Place(_ids.NextMonsterId(), definition, placement, motionTimings);
            _npcs.Add(npc.EntityId, npc);
        }
    }

    public int FieldId { get; }
    public FieldTerrain Terrain { get; }
    internal FieldPremium Premium { get; }
    internal FieldItemEvents ItemEvents { get; } = new();
    internal FieldStatusBook Statuses { get; } = new();
    internal FieldPresenceFrames Frames { get; } = new();
    private readonly FieldDescriptors _descriptors = new();
    public long NowMs => (long)_nowMs;
    public int PlayerCount => _players.Count;
    public int MonsterCount => _monsters.Count;
    public FieldGroundLoot GroundLoot { get; }

    public IReadOnlyCollection<WorldNpc> Npcs => _npcs.Values;
    public bool TryGetNpc(ulong entityId, [MaybeNullWhen(false)] out WorldNpc npc) => _npcs.TryGetValue(entityId, out npc);
    internal Dictionary<ulong, WorldPlayer>.KeyCollection PlayerIds => _players.Keys;

    public void Add(WorldPlayer player)
    {
        _players[player.EntityId] = player;
        player.FlinchMs = PlayerActionTiming(player, MotionCode.Damage, MotionCodes.DamageAndEatRate).DurationMs;
    }
    public void Remove(ulong entityId)
    {
        if (_players.TryGetValue(entityId, out var leaving))
        {
            LeaveCastleItems(leaving);
            _skills.LeaveCrystalPets(leaving);
        }

        // The skills forget the player while it is still here, so its summon's keep icon leaves with the summon.
        _skills.Forget(entityId);
        _players.Remove(entityId, out _);
        Statuses.Take(entityId);
        Premium.Forget(entityId);
    }

    // Validates a cast on the tick thread and starts its motion; the hits follow in Step.
    public SkillCastOutcome CastSkill(WorldPlayer player, in SkillCastRequest request)
    {
        if (!_players.TryGetValue(player.EntityId, out var present) || !ReferenceEquals(present, player))
        {
            return SkillCastOutcome.Refused(request, SkillCastVerdict.Stale);
        }

        // An open personal shop holds its seller (WorldPlayer.Shop).
        return player.Shop is null ? _skills.Cast(player, request) : SkillCastOutcome.Refused(request, SkillCastVerdict.Busy);
    }
    public bool TryGet(ulong entityId, [MaybeNullWhen(false)] out WorldPlayer player) => _players.TryGetValue(entityId, out player);

    public bool SetAttackTarget(ulong attackerEntityId, ulong targetEntityId, bool sustained, ulong sequence = 0)
    {
        if (!_players.TryGetValue(attackerEntityId, out var attacker) || attacker.IsDead || attacker.Shop is not null)
        {
            return false;
        }

        if (!attacker.AcceptAttackSequence(sequence))
        {
            return false;
        }

        if (_players.TryGetValue(targetEntityId, out var foe))
        {
            if (!MayFight(attacker, foe))
            {
                attacker.ClearAttackTarget();
                return false;
            }

            attacker.SetAttackTarget(targetEntityId, sustained);
            return true;
        }

        if (!_monsters.TryGetValue(targetEntityId, out var monster) || !monster.IsAlive || monster.IsSummon
            || monster.Effects.Has(Simulation.Modifiers.ControlFlags.Charmed) || monster.DuelPlayerId != 0 && monster.DuelPlayerId != attackerEntityId
            || FieldCombat.OwnClan(attacker, monster))
        {
            attacker.ClearAttackTarget();
            return false;
        }

        attacker.SetAttackTarget(targetEntityId, sustained);
        return true;
    }

    public ulong ExpFor(ulong playerId) => _combat.ExpFor(playerId);

    // A used item's timed effect on a player of this field; it starts with the next step.
    internal void QueueItemEffect(ulong entityId, EffectSpec spec, float durationMs, int rank = 1) => _skills.QueueItemEffect(entityId, spec, durationMs, rank);

    internal void EndItemEffect(WorldPlayer player, uint code) => _skills.EndItemEffect(player, code);

    // The field's combat resolution, for scenario tests that drive single monster attacks.
    internal FieldCombat Combat => _combat;

    public void Step(float dt, int? scheduledBossHour = null)
    {
        float dtMs = dt * 1000f;
        _nowMs += dtMs;
        long nowMs = (long)_nowMs;
        _events.Clear();
        _skills.BeginTick();
        ItemEvents.BeginTick();
        _prey.Rebuild(_players.Values);
        _preyCurrent = true;
        foreach (var summon in _monsters.Values)
        {
            if (summon.IsSummon)
            {
                // Wild monsters fight a summon only when it hits them; they never pick it by sight.
                _prey.AddTargetOnly(summon.EntityId, summon.Position, summon.IsAlive);
            }
        }

        StepNight();
        SnapshotLiveMonsters();
        if (SpawnsNow)
        {
            _spawner.MaintainPopulation(dtMs, _liveScratch, this, nowMs, this);
        }
        _spawner.MaintainDespawns(dtMs, _liveScratch, this, _expiredScratch);
        for (int i = 0; i < _expiredScratch.Count; i++)
        {
            RemoveMonster(_expiredScratch[i].EntityId);
        }

        // The shared world clock emits one pass for all fields, independent of player entry.
        if (scheduledBossHour is int hour)
        {
            _spawner.SpawnScheduledBosses(hour, this, this);
        }

        _preyCurrent = false;
        SnapshotLiveMonsters();
        RebuildCollisionIndex();
        for (int i = 0; i < _liveScratch.Count; i++)
        {
            StepMonster(_liveScratch[i], dtMs);
        }

        _monsterSkills.ApplyChanges();
        ApplyEventForms();
        StepSummons(dtMs);
        ExpireSoulMonsters();
        StepSiege();

        // Authoritative NPC movement keeps interaction range and presentation aligned.
        foreach (var npc in _npcs.Values)
        {
            StepNpc(npc, dt);
        }

        foreach (var player in _players.Values)
        {
            if (player.IsDead)
            {
                player.TickDead(dtMs);
                _collisionIndex.Put(player.EntityId, player.Position);
                continue;
            }

            if (player.TickAction(dtMs, out ulong hitTargetId))
            {
                if (_combat.ResolvePlayerSwingHit(player, hitTargetId, nowMs) is { } reward)
                {
                    PlaceLoot(reward, nowMs);
                }
            }

            var restored = player.TickPotion(dtMs, _rng);
            if (restored != PotionResource.None)
            {
                EmitCombatEvent(new CombatEventView(player.EntityId, player.EntityId, CombatEventKind.PotionApplied, resources: (int)restored));
            }

            if (player.HasPendingPotion && !player.IsBusy)
            {
                var timing = PlayerPotionTiming(player);
                player.BeginPendingPotion(timing.DurationMs, timing.HitAtMs);
                EmitCombatEvent(new CombatEventView(player.EntityId, player.EntityId, CombatEventKind.PotionBegin,
                    timing.DurationMs, timing.HitAtMs));
            }

            if (player.HasPendingYahoo && !player.IsBusy && player.BeginPendingYahoo(PlayerActionTiming(player, MotionCode.Yahoo, 1f).DurationMs)
                && player.YahooHeals)
            {
                HealClanMonsters(player);
            }

            _combat.StepPlayerAttack(player);
            StepPlayer(player, nowMs);
            player.TakeProgressCues(out var leveledUp, out var learned);
            if (leveledUp)
            {
                EmitCombatEvent(new CombatEventView(player.EntityId, player.EntityId, CombatEventKind.LevelUp));
            }

            if (learned)
            {
                EmitCombatEvent(new CombatEventView(player.EntityId, player.EntityId, CombatEventKind.SkillLearned));
            }
        }

        _skills.Step(dtMs);
        foreach (var reward in _skills.Payouts)
        {
            PlaceLoot(reward, nowMs);
        }

        Premium.Step(_players.Values);

        _combat.TickCorpses(dtMs);
        StepAging(dtMs, nowMs);
        StepQuests(dtMs, nowMs);
        GroundLoot.Step(nowMs);
        Statuses.Apply(_skills.StatusEvents, nowMs);
        Statuses.Prune(nowMs, _isPresent ??= IsPresent);
    }

    private Func<ulong, bool>? _isPresent;

    private bool IsPresent(ulong entityId) => _players.ContainsKey(entityId) || _monsters.ContainsKey(entityId) || _npcs.ContainsKey(entityId);

    // Dead at once, lives in reserve and all, leaving a corpse.
    private void KillOutright(MonsterInstance monster)
    {
        monster.VirtualLives = 0;
        monster.ApplyDamage(monster.Life);
        _combat.BeginCorpse(monster.EntityId);
    }

    private void PlaceLoot(KillPayout reward, long nowMs)
    {
        reward = CreditQuests(reward);
        if (_monsters.TryGetValue(reward.MonsterEntityId, out var killed))
        {
            var dropPosition = Terrain.SettleNear(killed.Position, FieldTerrain.DropTolerance);
            GroundLoot.Place(reward, dropPosition, killed.Def.Drops.AllPlayersSeeDrops, nowMs, _rng);
            if (reward.CreditedPlayerId != 0)
            {
                CallKillSummons(killed, reward.CreditedPlayerId);
                ScoreBellatra(killed, reward.CreditedPlayerId, dropPosition);
            }
        }
    }

    internal SkillEventView[] SnapshotSkillEvents() => _skills.SnapshotSkillEvents();

    internal StatusEventView[] SnapshotStatusEvents() => _skills.SnapshotStatusEvents();

    bool IFieldSkillHost.TryGetPlayer(ulong entityId, [MaybeNullWhen(false)] out WorldPlayer player) => _players.TryGetValue(entityId, out player);

    bool IFieldSkillHost.TryGetMonster(ulong entityId, [MaybeNullWhen(false)] out MonsterInstance monster) => _monsters.TryGetValue(entityId, out monster);

    Dictionary<ulong, MonsterInstance>.ValueCollection IFieldSkillHost.Monsters => _monsters.Values;

    Dictionary<ulong, WorldPlayer>.ValueCollection IFieldSkillHost.Players => _players.Values;

    KillPayout? IFieldSkillHost.ApplyPlayerDamage(WorldPlayer player, MonsterInstance monster, int damage, bool crit, bool forceFlinch, ulong sourceEntityId) =>
        _combat.ApplyPlayerDamage(player, monster, damage, crit, NowMs, forceFlinch, sourceEntityId, skill: true);

    bool IFieldSkillHost.TryFindMonsterDefinition(string name, [MaybeNullWhen(false)] out MonsterDefinition definition) =>
        _catalog.TryFind(name, out definition);

    MonsterInstance IFieldSkillHost.AddSummon(MonsterDefinition definition, Vector3 position, float facing)
    {
        var summon = new MonsterInstance(_ids.NextMonsterId(), definition, Terrain.PlaceOnFloor(position), facing, spawnPointIndex: -1);
        _monsters[summon.EntityId] = summon;
        // Into the collision index at once, so a summon called before the next step is seen standing there.
        _collisionIndex.Put(summon.EntityId, summon.Position);
        return summon;
    }

    // Summons move with the same collision as wild monsters; their brain takes its orders from their owner.
    private void StepSummons(float dtMs)
    {
        _summonScratch.Clear();
        foreach (var monster in _monsters.Values)
        {
            if (monster.IsSummon)
            {
                _summonScratch.Add(monster);
            }
        }

        foreach (var summon in _summonScratch)
        {
            if (!summon.IsAlive)
            {
                _skills.StepSummon(summon, TuningFor(summon.Def), dtMs);
                _combat.BeginCorpse(summon.EntityId);
                continue;
            }

            var before = summon.Position;
            var attack = _skills.StepSummon(summon, TuningFor(summon.Def), dtMs);
            if (summon.SwingJustBegan)
            {
                summon.SwingJustBegan = false;
                var tuning = TuningFor(summon.Def);
                _events.Add(new CombatEventView(summon.EntityId, summon.TargetId, CombatEventKind.SwingBegin,
                    summon.AttackIsSkill ? tuning.SkillDurationMs : tuning.CooldownMs, summon.AttackIsSkill ? tuning.SkillWindupMs : tuning.WindupMs));
            }

            if (summon.Position == before && summon.AiState == MonsterAiState.Idle && !summon.Effects.Has(ControlFlags.Stun))
            {
                StepOutOfCrowd(summon, before, dtMs);
            }

            MoveMonster(summon, before);
            if (!summon.IsAlive)
            {
                _combat.BeginCorpse(summon.EntityId);
            }
            else if (attack is { } landed)
            {
                _skills.ResolveSummonHit(summon, landed);
            }
        }
    }

    private void StepPlayer(WorldPlayer player, long nowMs)
    {
        player.ExpireStaleMotion(nowMs);
        _collisionIndex.Put(player.EntityId, player.Position);
    }

    private void StepMonster(MonsterInstance monster, float dtMs)
    {
        MonsterAiTuning tuning = TuningFor(monster.Def);

        Vector3 before = monster.Position;
        MonsterAttackEvent? attack = _brain.Tick(monster, tuning, DuelPrey(monster) ?? _prey, dtMs);

        _monsterSkills.AfterTick(monster, tuning, before, dtMs);
        MoveMonster(monster, before);

        if (attack is { } landed)
        {
            _combat.ResolveMonsterHit(monster, landed);
        }
    }

    bool IFieldSkillHost.MonsterStandsAt(Vector3 point)
    {
        BuildMonsterNeighbours(0, point, 0f);
        return _occupancy.IsOccupied(point);
    }

    // An idle summon standing inside other monsters walks out of them, away from the bodies it overlaps (a summon on
    // the very spot of another goes the way its id picks). MoveMonster lets an overlapping monster leave, so pets called
    // or recalled together part instead of standing in each other.
    private void StepOutOfCrowd(MonsterInstance summon, Vector3 at, float dtMs)
    {
        // Squared lengths below this give no direction (bodies on the very same spot).
        const float NoDirection = 1e-6f;
        BuildMonsterNeighbours(summon.EntityId, at, 0f);
        var awayX = 0f;
        var awayZ = 0f;
        var overlapping = false;
        foreach (var neighbour in _neighbourScratch)
        {
            if (!neighbour.Overlaps(at))
            {
                continue;
            }

            overlapping = true;
            var dx = at.X - neighbour.Position.X;
            var dz = at.Z - neighbour.Position.Z;
            var squared = (dx * dx) + (dz * dz);
            if (squared > NoDirection)
            {
                var distance = MathF.Sqrt(squared);
                awayX += dx / distance;
                awayZ += dz / distance;
            }
        }

        if (!overlapping)
        {
            return;
        }

        var heading = (awayX * awayX) + (awayZ * awayZ) > NoDirection ? MathF.Atan2(awayX, awayZ) : FieldSkills.CompassAngle(summon.EntityId);
        var step = MonsterLocomotion.UnitsPerSecond(summon.Def, running: false) * dtMs / 1000f;
        summon.Facing = heading;
        summon.Motion = MotionState.Walk;
        summon.Position = new Vector3(at.X + (MathF.Sin(heading) * step), at.Y, at.Z + (MathF.Cos(heading) * step));
    }

    // Re-resolve AI movement against the stage and the other monsters only, sliding along blockers as the client's
    // step check does.
    private void MoveMonster(MonsterInstance monster, Vector3 before)
    {
        Vector3 desired = monster.Position;
        float dx = desired.X - before.X;
        float dz = desired.Z - before.Z;
        float distance = MathF.Sqrt((dx * dx) + (dz * dz));
        bool moved = false;
        bool crowded = false;
        if (distance > 0f)
        {
            BuildMonsterNeighbours(monster.EntityId, before, distance);
            var result = MonsterStepping.Step(Terrain, before, MathF.Atan2(dx, dz), distance, _occupancy, out var accepted);
            crowded = result == MonsterStepResult.Crowded;
            monster.Position = accepted;
            moved = accepted.X != before.X || accepted.Z != before.Z;
            _collisionIndex.Put(monster.EntityId, accepted);
        }
        else if (desired != before)
        {
            monster.Position = before;
        }

        monster.MovedThisTick = moved;
        monster.Crowded = crowded;
    }

    private void StepNpc(WorldNpc npc, float dt)
    {
        var before = npc.Position;
        var desired = npc.IntegrateStep(dt);
        if (desired == before)
        {
            return;
        }

        BuildNeighbours(npc.EntityId, before, desired, EntityCollisionRadii.Npc);
        var accepted = EntityCollision.Resolve(before, desired, EntityCollisionRadii.Npc, _neighbourScratch);
        accepted = Terrain.TryStep(before, accepted, out var grounded) ? grounded : before;
        npc.CommitStep(accepted, accepted == before);
        _collisionIndex.Put(npc.EntityId, accepted);
    }

    public MonsterAiTuning TuningFor(MonsterDefinition def)
    {
        if (!_tuningByCode.TryGetValue(def.Code, out var tuning))
        {
            _motions.TryGet(def.ModelInxPath, out var timing);
            tuning = new MonsterAiTuning(def, timing);
            _tuningByCode[def.Code] = tuning;
        }

        return tuning;
    }

    // An action field's Space with nothing ahead: a player who may swing, holding no attack, swings at nothing (false
    // otherwise).
    internal bool SwingAtNothing(ulong entityId)
    {
        if (!_players.TryGetValue(entityId, out var player) || !player.MaySwing || player.AttackTargetId != 0)
        {
            return false;
        }

        var timing = PlayerSwingTiming(player);
        player.BeginEmptySwing(timing.DurationMs, timing.HitAtMs);
        return true;
    }

    public (float DurationMs, float HitAtMs) PlayerSwingTiming(WorldPlayer player)
    {
        var rate = player.Character.AttackRate;
        return PlayerActionTiming(player, MotionCode.Attack, rate);
    }

    private (float DurationMs, float HitAtMs) PlayerPotionTiming(WorldPlayer player)
    {
        return PlayerActionTiming(player, MotionCode.Eat, MotionCodes.DamageAndEatRate);
    }

    private (float DurationMs, float HitAtMs) PlayerActionTiming(WorldPlayer player, MotionCode state, float rate)
    {
        var playerClass = player.Character.Class.Class;
        var timing = PlayerTimingFor(playerClass);
        var clip = PlayerMotionFallback.GetClip(playerClass, state);
        if (timing is not null && timing.TryGetClip(state, out var authored))
        {
            clip = authored;
        }

        string weaponKey = player.Character.GetEquipped(EquipmentSlot.Weapon)?.Definition.CatalogKey ?? "";
        if (state == MotionCode.Attack && PlayerEquipmentTiming.TryGet(playerClass, weaponKey, out var equipped))
        {
            clip = equipped;
        }

        var duration = MotionTiming.DurationMs(clip.Frames, rate);
        var eventMs = clip.TryGetEventTicks(0, out var ticks)
            ? MotionTiming.EventOffsetMs(ticks, rate)
            : duration;
        return (duration, eventMs);
    }

    public void EmitCombatEvent(in CombatEventView combatEvent) => _events.Add(combatEvent);

    internal CombatEventView[]? SnapshotEvents() => _events.Count == 0 ? null : _events.ToArray();

    private MotionTimingSet? PlayerTimingFor(PlayerClass playerClass)
    {
        if (!_playerTimingByClass.TryGetValue(playerClass, out var set))
        {
            _motions.TryGet(ClassRegistry.Get(playerClass).Definition.DefaultVisuals.Body.InxPath, out set);
            _playerTimingByClass[playerClass] = set;
        }

        return set;
    }

    private void RebuildCollisionIndex()
    {
        _collisionIndex.Clear();
        var order = 0;
        foreach (var player in _players.Values)
        {
            _collisionIndex.Put(player.EntityId, player.Position, order++);
        }

        foreach (var monster in _monsters.Values)
        {
            _collisionIndex.Put(monster.EntityId, monster.Position, order++);
        }

        foreach (var npc in _npcs.Values)
        {
            _collisionIndex.Put(npc.EntityId, npc.Position, order++);
        }
    }

    private void BuildNeighbours(ulong selfId, Vector3 before, Vector3 desired, float selfRadius)
    {
        _neighbourScratch.Clear();
        var bounds = EntityCollision.QueryBounds(before, desired, MathF.Max(selfRadius, EntityCollisionRadii.Monster));
        _collisionIndex.QueryWithin(bounds.Center, bounds.Radius, _collisionQuery);
        foreach (var entry in _collisionQuery)
        {
            if (entry.Id == selfId)
            {
                continue;
            }

            if (_players.TryGetValue(entry.Id, out var player))
            {
                if (player.Life > 0)
                {
                    _neighbourScratch.Add(new EntityNeighbour(player.Position, EntityCollisionRadii.Player));
                }
            }
            else if (_monsters.TryGetValue(entry.Id, out var monster) && monster.IsAlive)
            {
                _neighbourScratch.Add(new EntityNeighbour(monster.Position, EntityCollisionRadii.Monster));
            }
            else if (_npcs.TryGetValue(entry.Id, out var npc))
            {
                _neighbourScratch.Add(new EntityNeighbour(npc.Position, EntityCollisionRadii.Npc));
            }
        }
    }

    // Live monsters around a mover, for the server's monster-only overlap test (players are not blockers).
    private void BuildMonsterNeighbours(ulong selfId, Vector3 origin, float distance)
    {
        _neighbourScratch.Clear();
        _collisionIndex.QueryWithin(origin, distance + EntityCollisionRadii.Monster * 2f, _collisionQuery);
        foreach (var entry in _collisionQuery)
        {
            if (entry.Id != selfId && _monsters.TryGetValue(entry.Id, out var other) && other.IsAlive)
            {
                _neighbourScratch.Add(new EntityNeighbour(other.Position, EntityCollisionRadii.Monster));
            }
        }
    }

    private sealed class MonsterOccupancy(List<EntityNeighbour> neighbours) : IStepOccupancy
    {
        public bool IsOccupied(Vector3 position)
        {
            foreach (var neighbour in neighbours)
            {
                if (neighbour.Overlaps(position))
                {
                    return true;
                }
            }

            return false;
        }
    }

    // Wild monsters only: summons have their own step and do not count toward the field's population.
    private void SnapshotLiveMonsters()
    {
        _liveScratch.Clear();
        foreach (var m in _monsters.Values)
        {
            if (!m.IsSummon)
            {
                _liveScratch.Add(m);
            }
        }
    }

    MonsterInstance ISpawnSink.Spawn(MonsterDefinition definition, Vector3 position, int spawnPointIndex, float facing) =>
        Place(definition, Terrain.PlaceOnFloor(position), spawnPointIndex, facing);

    // A new monster at exactly this position, with its potions or lives.
    private MonsterInstance Place(MonsterDefinition definition, Vector3 position, int spawnPointIndex, float facing)
    {
        ulong id = _ids.NextMonsterId();
        var monster = new MonsterInstance(id, definition, position, facing, spawnPointIndex);
        _monsters[id] = monster;
        _monsterSkills.Prepare(monster);
        return monster;
    }

    bool IFieldCombatHost.TryGetPlayer(ulong entityId, [MaybeNullWhen(false)] out WorldPlayer player)
        => _players.TryGetValue(entityId, out player);

    bool IFieldCombatHost.TryGetMonster(ulong entityId, [MaybeNullWhen(false)] out MonsterInstance monster)
        => _monsters.TryGetValue(entityId, out monster);

    Dictionary<ulong, MonsterInstance>.ValueCollection IFieldCombatHost.Monsters => _monsters.Values;

    IReadOnlyList<PreyCandidate> IFieldCombatHost.NearbyPlayers(Vector3 origin, float radius) => _prey.Nearby(origin, radius);

    void IFieldCombatHost.DespawnMonster(ulong entityId) => _monsters.Remove(entityId, out _);

    // Removes a monster from the field and its combat state.
    private bool RemoveMonster(ulong entityId)
    {
        if (!_monsters.Remove(entityId, out _))
        {
            return false;
        }

        _combat.ForgetMonster(entityId);
        return true;
    }

    // Asked for every monster and spawn point each tick: during the spawner pass the prey index (players at the start of
    // the step, which they keep until the players' own step) answers it without visiting every player.
    private bool _preyCurrent;

    bool IPlayerProximity.AnyPlayerNear(Vector3 worldPosition)
    {
        if (!_preyCurrent)
        {
            foreach (var player in _players.Values)
            {
                if (player.DistanceSqTo(worldPosition) < PlayerNearRadiusSquared)
                {
                    return true;
                }
            }

            return false;
        }

        return _prey.AnyWithin(worldPosition, PlayerNearRadius);
    }

    public int EntityCount => _players.Count + _monsters.Count + _npcs.Count;

    public EntityView[] BuildViews()
    {
        var views = new EntityView[EntityCount];
        FillViews(views);
        return views;
    }

    // Writes every entity's view with its descriptor revision; views must hold exactly EntityCount entries.
    public void FillViews(EntityView[] views)
    {
        _descriptors.BeginTick();
        var index = 0;
        foreach (var player in _players.Values)
        {
            views[index++] = WithRevision(player.ToView());
        }

        foreach (var monster in _monsters.Values)
        {
            views[index++] = WithRevision(ToMonsterView(monster));
        }

        foreach (var npc in _npcs.Values)
        {
            views[index++] = WithRevision(npc.ToView());
        }

        _descriptors.Prune();
    }

    private EntityView WithRevision(in EntityView view) => view with { Revision = _descriptors.RevisionOf(view) };

    private static EntityView ToMonsterView(MonsterInstance m) => new(
        m.EntityId,
        EntityKind.Monster,
        isSelf: false,
        m.Position,
        m.Facing,
        m.PresentedMotion,
        level: m.Def.Combat.Level,
        life: FieldMonsterSkills.ViewLife(m).Life,
        maxLife: FieldMonsterSkills.ViewLife(m).Max,
        appearance: default,
        templateId: (uint)m.Def.Code,
        moveSpeed: m.Def.Ai.MoveSpeed,
        attackRate: 1f) // monsters never scale their attack advance.
    {
        OwnerId = m.OwnerId,
        ModelScale = m.ModelScale,
        DuelPlayerId = m.DuelPlayerId
    };

}
