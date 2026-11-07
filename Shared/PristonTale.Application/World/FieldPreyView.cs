using System.Collections.Generic;
using System.Numerics;
using PristonTale.Simulation.Physics;
using PristonTale.Simulation.World.Ai;

namespace PristonTale.Application.World;

public sealed class FieldPreyView : IPreyView
{
    private readonly List<PreyCandidate> _candidates = new();
    private readonly Dictionary<ulong, PreyCandidate> _byId = new();
    private readonly SpatialIndex _index = new();
    private readonly List<SpatialEntry> _query = new();
    private readonly List<PreyCandidate> _nearby = new();
    // Hidden game masters: out of monsters' sight, still in the field for the spawner (rarely any).
    private readonly List<Vector3> _hidden = new();

    public IReadOnlyList<PreyCandidate> Nearby(Vector3 position, float radius)
    {
        _index.QueryWithin(position, radius, _query);
        _nearby.Clear();
        foreach (var entry in _query)
        {
            _nearby.Add(_byId[entry.Id]);
        }

        return _nearby;
    }

    public IReadOnlyList<PreyCandidate> Candidates => _candidates;

    // Whether a player, a hidden game master too, stands strictly inside the planar circle (the spawner's proximity test):
    // hiding takes a player out of monsters' sight, not out of the field, so the monsters around one neither leave nor
    // spawn on top of them.
    public bool AnyWithin(Vector3 position, float radius)
    {
        if (_index.AnyWithin(position, radius))
        {
            return true;
        }

        foreach (var hidden in _hidden)
        {
            if (Planar.Within(hidden, position, radius))
            {
                return true;
            }
        }

        return false;
    }

    public bool TryGet(ulong entityId, out PreyCandidate prey) => _byId.TryGetValue(entityId, out prey);

    // An entity monsters can keep as their target but never acquire by sight (a summon that hit them).
    public void AddTargetOnly(ulong entityId, Vector3 position, bool alive) => _byId[entityId] = new PreyCandidate(entityId, position, alive);

    public void Rebuild(Dictionary<ulong, WorldPlayer>.ValueCollection players)
    {
        _candidates.Clear();
        _byId.Clear();
        _index.Clear();
        _hidden.Clear();
        foreach (var p in players)
        {
            // A hidden game master is invisible to monsters as well as to players.
            if (p.Hidden)
            {
                _hidden.Add(p.Position);
                continue;
            }

            var sight = 100 + p.Character.Effects.Totals.Flat(Simulation.Modifiers.StatKind.MonsterSight);
            var candidate = new PreyCandidate(p.EntityId, p.Position, p.Life > 0, Math.Max(0, sight), p.Clan?.ClanId ?? 0);
            _candidates.Add(candidate);
            _index.Put(p.EntityId, p.Position, _candidates.Count - 1);
            _byId[p.EntityId] = candidate;
        }
    }
}
