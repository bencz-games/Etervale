using System.Diagnostics.CodeAnalysis;
using System.Numerics;
using PristonTale.Simulation.Items;
using PristonTale.Simulation.World;
using PristonTale.Simulation.World.Ai;
using PristonTale.Simulation.World.Monsters;
using PristonTale.Simulation.World.Spawning;

namespace PristonTale.Application.World;

// What game-master commands read and change in one field. Called on the command phase only.
public sealed partial class FieldInstance
{
    internal ISimRng Rng => _rng;

    internal FieldSpawner Spawner => _spawner;

    internal bool TryFindMonsterDefinition(string name, [MaybeNullWhen(false)] out MonsterDefinition definition) => _catalog.TryFind(name, out definition);

    internal bool TryGetMonster(ulong entityId, [MaybeNullWhen(false)] out MonsterInstance monster) => _monsters.TryGetValue(entityId, out monster);

    // Living wild monsters of the field (summons excluded).
    internal int LiveMonsterCount => _monsters.Values.Count(static monster => monster.IsAlive && !monster.IsSummon);

    // Living wild monsters that came from the field's spawn points.
    internal int SpawnPointMonsterCount => _monsters.Values.Count(static monster => monster.IsAlive && !monster.IsSummon && monster.SpawnPointIndex >= 0);

    internal int MonsterEntryCount => _monsters.Count;

    // The name of a monster, NPC or player of this field.
    internal bool TryGetEntityName(ulong entityId, [MaybeNullWhen(false)] out string name)
    {
        if (_monsters.TryGetValue(entityId, out var monster))
        {
            name = monster.Def.Name;
            return true;
        }

        if (_npcs.TryGetValue(entityId, out var npc))
        {
            name = npc.Definition.Label;
            return true;
        }

        if (_players.TryGetValue(entityId, out var player))
        {
            name = player.Character.Name;
            return true;
        }

        name = null;
        return false;
    }

    // A monster a game master called at a position, outside the spawn points; it drops nothing unless drops is set.
    internal MonsterInstance SpawnForGameMaster(MonsterDefinition definition, Vector3 position, bool drops)
    {
        var monster = ((ISpawnSink)this).Spawn(definition, position, -1, _rng.NextUnit() * MathF.Tau);
        monster.DropsDisabled = !drops;
        _spawner.SeedIdleDespawn(monster);
        return monster;
    }

    internal MonsterInstance CallGameMasterPet(WorldPlayer owner, MonsterDefinition definition, float lifetimeMs, Vector3? position = null) =>
        _skills.CallGameMasterPet(owner, definition, lifetimeMs, position is { } at ? Terrain.PlaceOnFloor(at) : null);

    // An item copy or gold a game master made, dropped on the floor under the position.
    internal bool DropForGameMaster(ItemInstance? item, int gold, Vector3 position, ulong ownerId) =>
        GroundLoot.PlaceCreated(item, gold, Terrain.SettleNear(position, FieldTerrain.DropTolerance), ownerId, NowMs);
}
