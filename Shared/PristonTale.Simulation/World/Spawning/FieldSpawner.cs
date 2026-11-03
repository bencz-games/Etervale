using System.Numerics;
using PristonTale.Simulation.World.Ai;
using PristonTale.Simulation.World.Fields;
using PristonTale.Simulation.World.Monsters;

namespace PristonTale.Simulation.World.Spawning;

public sealed class FieldSpawner
{
    private const float GroupScatterRadius = 64f;

    private readonly FieldSpawnData _data;
    private readonly MonsterCatalog _catalog;
    private readonly ISimRng _rng;

    private float _passTimerMs;
    private int _pointCursor;
    private readonly long[] _pointCooldownUntilMs;
    private readonly SpawnCadence _cadence;
    private readonly int _totalWeight;

    public FieldSpawner(FieldSpawnData data, MonsterCatalog catalog, ISimRng rng)
    {
        _data = data ?? throw new ArgumentNullException(nameof(data));
        _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        _rng = rng ?? throw new ArgumentNullException(nameof(rng));

        _pointCooldownUntilMs = new long[data.SpawnPoints.Count];
        if (data.SpawnIntervalMask is int mask)
        {
            _cadence = new SpawnCadence(mask);
        }

        int sum = 0;
        foreach (SpawnTableEntry e in data.SpawnTable)
        {
            if (e.Weight > 0)
            {
                sum += e.Weight;
            }
        }

        _totalWeight = sum;
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(data.SpawnPassIntervalMs);
        _passTimerMs = data.SpawnPassIntervalMs;
    }

    public static Vector3 PointToWorld(SpawnPoint point) => new(point.X, 0f, point.Z);

    // A game master's event: until UntilMs, each regular spawn becomes one of this monster with Permille/1000 chance.
    public SpawnEvent EventMonster { get; set; }

    public int SpawnPointCount => _data.SpawnPoints.Count;

    public int ActiveSpawnPointCount => _data.SpawnPoints.Count(static point => point.Active);

    public int MaxConcurrentMonsters => _data.MaxConcurrentMonsters;

    public int SpawnPassIntervalMs => _data.SpawnPassIntervalMs;

    public void MaintainPopulation(float dtMs, IReadOnlyList<MonsterInstance> live, IPlayerProximity proximity, long nowMs, ISpawnSink sink)
    {
        ArgumentNullException.ThrowIfNull(live);
        ArgumentNullException.ThrowIfNull(proximity);
        ArgumentNullException.ThrowIfNull(sink);

        if (!float.IsFinite(dtMs) || dtMs <= 0f)
        {
            return;
        }

        if (!IsSpawnPassDue(dtMs))
        {
            return;
        }

        if (_data.SpawnTable.Count == 0 || _totalWeight <= 0)
        {
            return;
        }

        int aliveCount = CountAlive(live);
        if (aliveCount >= _data.MaxConcurrentMonsters)
        {
            return;
        }

        MonsterDefinition leaderDef = PickWeightedMonster();
        if (leaderDef is null)
        {
            return;
        }

        int pointIndex = PickSpawnPoint(live, proximity, nowMs);
        if (pointIndex < 0)
        {
            return;
        }

        SpawnPoint point = _data.SpawnPoints[pointIndex];
        Vector3 leaderPos = PointToWorld(point);

        int groupSize = Math.Max(1, _rng.NextInclusive(leaderDef.Ai.GroupSize.Min, leaderDef.Ai.GroupSize.Max));
        if (EventMonster is { } evented && nowMs < evented.UntilMs && evented.Permille > 0 && _rng.NextExclusive(0, 1000) < evented.Permille)
        {
            leaderDef = evented.Definition;
            groupSize = 1;
        }

        groupSize = Math.Min(groupSize, _data.MaxConcurrentMonsters - aliveCount);
        int pointRoom = _data.MaxPerSpawnPoint - CountAliveAtPoint(live, pointIndex);
        groupSize = Math.Min(groupSize, Math.Max(1, pointRoom));

        for (int i = 0; i < groupSize; i++)
        {
            Vector3 pos = i == 0 ? leaderPos : ScatterAround(leaderPos);
            float facing = RandomFacing();
            MonsterInstance spawned = sink.Spawn(leaderDef, pos, pointIndex, facing);
            SeedIdleDespawn(spawned, leaderDef);
        }

        _pointCooldownUntilMs[pointIndex] = nowMs + _data.PerPointCooldownMs;
    }

    public IReadOnlyList<MonsterInstance> MaintainDespawns(float dtMs, IReadOnlyList<MonsterInstance> live, IPlayerProximity proximity)
    {
        ArgumentNullException.ThrowIfNull(live);
        ArgumentNullException.ThrowIfNull(proximity);

        List<MonsterInstance> expired = null;
        for (int i = 0; i < live.Count; i++)
        {
            MonsterInstance m = live[i];
            if (!m.IsAlive || m.StaysUntilDawn)
            {
                continue;
            }

            if (proximity.AnyPlayerNear(m.Position))
            {
                m.IdleDespawnMsLeft = IdleBudgetFor(m.Def);
                continue;
            }

            m.IdleDespawnMsLeft -= dtMs;
            if (m.IdleDespawnMsLeft <= 0f)
            {
                (expired ??= new List<MonsterInstance>()).Add(m);
            }
        }

        return (IReadOnlyList<MonsterInstance>)expired ?? Array.Empty<MonsterInstance>();
    }

    public void SpawnScheduledBosses(int hour, IPlayerProximity proximity, ISpawnSink sink)
    {
        ArgumentNullException.ThrowIfNull(proximity);
        ArgumentNullException.ThrowIfNull(sink);
        ArgumentOutOfRangeException.ThrowIfNegative(hour);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(hour, 23);

        foreach (BossSpawn boss in _data.BossSpawns)
        {
            if (!boss.SpawnHours.Contains(hour)
                || !_catalog.TryGet(boss.BossMonsterCode, out MonsterDefinition masterDef))
            {
                continue;
            }

            int pointIndex = FirstActivePoint(proximity);
            if (pointIndex < 0)
            {
                continue;
            }

            Vector3 anchor = PointToWorld(_data.SpawnPoints[pointIndex]);
            MonsterInstance master = sink.Spawn(masterDef, anchor, pointIndex, RandomFacing());
            SeedIdleDespawn(master, masterDef);

            if (boss.EscortCount > 0 && _catalog.TryGet(boss.EscortMonsterCode, out MonsterDefinition escortDef))
            {
                for (int escortIndex = 0; escortIndex < boss.EscortCount; escortIndex++)
                {
                    MonsterInstance escort = sink.Spawn(escortDef, ScatterAround(anchor), pointIndex, RandomFacing());
                    SeedIdleDespawn(escort, escortDef);
                }
            }
        }
    }

    private bool IsSpawnPassDue(float dtMs)
    {
        if (_cadence is not null)
        {
            return _cadence.Advance(dtMs);
        }

        _passTimerMs -= dtMs;
        if (_passTimerMs > 0f)
        {
            return false;
        }

        _passTimerMs = _data.SpawnPassIntervalMs + _passTimerMs % _data.SpawnPassIntervalMs;
        return true;
    }

    private float IdleBudgetFor(MonsterDefinition def)
        => def.IsBoss ? _data.IdleDespawnMs * (float)_data.BossIdleDespawnFactor : _data.IdleDespawnMs;

    // A monster placed outside the spawn points (a game master's call) leaves after the same idle time as a spawned one.
    public void SeedIdleDespawn(MonsterInstance m) => SeedIdleDespawn(m, m.Def);

    private void SeedIdleDespawn(MonsterInstance m, MonsterDefinition def) => m.IdleDespawnMsLeft = IdleBudgetFor(def);

    private MonsterDefinition PickWeightedMonster()
    {
        // The original bound is inferred; preserve the standard cumulative half-open roll.
        int roll = _rng.NextExclusive(0, _totalWeight);
        int acc = 0;
        foreach (SpawnTableEntry e in _data.SpawnTable)
        {
            if (e.Weight <= 0)
            {
                continue;
            }

            acc += e.Weight;
            if (roll < acc)
            {
                return _catalog.TryGet(e.MonsterCode, out MonsterDefinition def) ? def : null;
            }
        }

        return null;
    }

    private int PickSpawnPoint(IReadOnlyList<MonsterInstance> live, IPlayerProximity proximity, long nowMs)
    {
        int count = _data.SpawnPoints.Count;
        if (count == 0)
        {
            return -1;
        }

        for (int step = 0; step < count; step++)
        {
            int idx = (_pointCursor + step) % count;
            SpawnPoint p = _data.SpawnPoints[idx];
            if (!p.Active)
            {
                continue;
            }

            if (CountAliveAtPoint(live, idx) >= _data.MaxPerSpawnPoint)
            {
                continue;
            }

            if (nowMs < _pointCooldownUntilMs[idx])
            {
                continue;
            }

            if (!proximity.AnyPlayerNear(PointToWorld(p)))
            {
                continue;
            }

            _pointCursor = (idx + 1) % count;
            return idx;
        }

        return -1;
    }

    private int FirstActivePoint(IPlayerProximity proximity)
    {
        int firstActive = -1;
        for (int i = 0; i < _data.SpawnPoints.Count; i++)
        {
            SpawnPoint p = _data.SpawnPoints[i];
            if (!p.Active)
            {
                continue;
            }

            if (firstActive < 0)
            {
                firstActive = i;
            }

            if (proximity.AnyPlayerNear(PointToWorld(p)))
            {
                return i;
            }
        }

        return firstActive;
    }

    private Vector3 ScatterAround(Vector3 center)
    {
        float angle = _rng.NextUnit() * MathF.Tau;
        float radius = _rng.NextUnit() * GroupScatterRadius;
        return new Vector3(center.X + MathF.Cos(angle) * radius, center.Y, center.Z + MathF.Sin(angle) * radius);
    }

    private float RandomFacing() => RandomFacing(_rng);

    // A spawned monster's facing: any direction, in radians.
    public static float RandomFacing(ISimRng rng) => rng.NextUnit() * MathF.Tau;

    private static int CountAlive(IReadOnlyList<MonsterInstance> live)
    {
        int n = 0;
        for (int i = 0; i < live.Count; i++)
        {
            if (live[i].IsAlive)
            {
                n++;
            }
        }

        return n;
    }

    private static int CountAliveAtPoint(IReadOnlyList<MonsterInstance> live, int pointIndex)
    {
        int n = 0;
        for (int i = 0; i < live.Count; i++)
        {
            MonsterInstance m = live[i];
            if (m.IsAlive && m.SpawnPointIndex == pointIndex)
            {
                n++;
            }
        }

        return n;
    }
}

public sealed record SpawnEvent(MonsterDefinition Definition, long UntilMs, int Permille);
