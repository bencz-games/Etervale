using System.Numerics;
using PristonTale.Simulation.World;
using PristonTale.Simulation.World.Ai;
using PristonTale.Simulation.World.Monsters;
using PristonTale.Simulation.World.Npcs;
using PristonTale.Simulation.World.Spawning;
using PristonTale.Contracts.Motion;

namespace PristonTale.Application.World;

// Night and day, from the server's clock. At night the event creatures of the field (code 100: the wolverines) walk from
// the field's first active spawn point a player stands near (the field's first active point with nobody near) and come
// back there when killed, and every event-creature NPC leaves; at dawn the creatures warp away
// and the NPCs return. A nightmare field spawns its monsters only at night, when its people turn into the night creature
// where they stand; at dawn its monsters die and its people return, the ones with an appearance draw only when the new
// draw picks them.
public sealed partial class FieldInstance
{
    public const int NightCreatureEvent = 100;
    // The night creature of a nightmare field, by model.
    public const string NightmareCreatureModel = "char\\monster\\wolverin\\wolverin_a.inx";

    private bool? _nightApplied;
    private readonly Dictionary<int, ulong> _eventCreatures = new();
    private readonly Dictionary<ulong, WorldNpc> _hiddenNpcs = new();
    private int _appearDraw = -1;
    private MonsterDefinition[]? _nightCreatures;

    // Set by the world before each step.
    internal bool Night { get; set; }

    // The field's event, set by the world when it opens the field.
    internal FieldEventKind Event { get; set; }

    // A siege holds the castle field's spawns.
    internal bool SpawnsNow => (Event != FieldEventKind.Nightmare || Night) && _siege is null;

    private void StepNight()
    {
        if (_nightApplied != Night)
        {
            var first = _nightApplied is null;
            _nightApplied = Night;
            if (Night)
            {
                Nightfall();
            }
            else if (!first)
            {
                Dawn();
            }
        }

        if (Night)
        {
            ReopenEventCreatures();
        }
    }

    private void Nightfall()
    {
        foreach (var npc in _npcs.Values.ToList())
        {
            if (IsEventNpc(npc.Definition))
            {
                HideNpc(npc);
            }
        }

        if (Event == FieldEventKind.Nightmare && NightmareCreature() is { } creature)
        {
            foreach (var npc in _npcs.Values.ToList())
            {
                HideNpc(npc);
                ((ISpawnSink)this).Spawn(creature, npc.Position, -1, npc.Facing).StaysUntilDawn = true;
            }
        }
    }

    private void Dawn()
    {
        foreach (var (_, id) in _eventCreatures)
        {
            if (_monsters.TryGetValue(id, out var creature) && creature.IsAlive)
            {
                creature.VanishOnWarp = true;
                creature.TargetId = 0;
                creature.AiState = MonsterAiState.Idle;
                creature.Motion = MotionState.Warp;
                creature.SpecialMsLeft = TuningFor(creature.Def).WarpDurationMs;
            }
        }

        _eventCreatures.Clear();
        if (Event == FieldEventKind.Nightmare)
        {
            foreach (var monster in _monsters.Values)
            {
                if (monster.IsAlive && !monster.IsSummon && monster.Def.IsHostile)
                {
                    monster.Life = 0;
                    monster.AiState = MonsterAiState.Dead;
                    monster.Motion = MotionState.Dead;
                    _combat.BeginCorpse(monster.EntityId);
                }
            }

            _appearDraw = _rng.Range(0, int.MaxValue - 1);
        }

        foreach (var npc in _hiddenNpcs.Values.ToList())
        {
            if (npc.Definition.AppearsFor(_appearDraw < 0 ? npc.Definition.AppearSlot : _appearDraw))
            {
                _hiddenNpcs.Remove(npc.EntityId);
                _npcs.Add(npc.EntityId, npc);
            }
        }
    }

    // An event creature of this field that is not walking (never opened, or killed and gone) walks again.
    private void ReopenEventCreatures()
    {
        _nightCreatures ??= _catalog.All.Where(c => c.EventCode == NightCreatureEvent && c.EventInfo == FieldId).ToArray();
        foreach (var creature in _nightCreatures)
        {
            if (_eventCreatures.TryGetValue(creature.Code, out var id) && _monsters.ContainsKey(id))
            {
                continue;
            }

            if (EventCreaturePoint() is { } point)
            {
                var opened = ((ISpawnSink)this).Spawn(creature, point, -1, FieldSpawner.RandomFacing(_rng));
                opened.StaysUntilDawn = true;
                _eventCreatures[creature.Code] = opened.EntityId;
            }
        }
    }

    // Where an event creature opens: the first active spawn point, in the field's order, that a player stands near; the
    // first active point when nobody is near (where the creatures stand when night falls on an empty field).
    private Vector3? EventCreaturePoint()
    {
        Vector3? first = null;
        foreach (var point in _spawnPoints)
        {
            if (!point.Active)
            {
                continue;
            }

            var position = FieldSpawner.PointToWorld(point);
            if (((IPlayerProximity)this).AnyPlayerNear(position))
            {
                return position;
            }

            first ??= position;
        }

        return first;
    }

    private MonsterDefinition? NightmareCreature() =>
        _catalog.All.FirstOrDefault(static monster => monster.ModelInxPath.Equals(NightmareCreatureModel, StringComparison.OrdinalIgnoreCase));

    private static bool IsEventNpc(NpcDefinition definition) =>
        definition.Services.Any(static service => service.Kind == NpcServiceKind.EventMonster && service.Value == NightCreatureEvent);

    private void HideNpc(WorldNpc npc)
    {
        if (_npcs.Remove(npc.EntityId))
        {
            _hiddenNpcs[npc.EntityId] = npc;
        }
    }
}
