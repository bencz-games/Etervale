using System.Diagnostics.CodeAnalysis;
using System.Numerics;
using PristonTale.Simulation.Modifiers;
using PristonTale.Simulation.World.Ai;
using PristonTale.Simulation.World.Monsters;
using PristonTale.Simulation.World.Spawning;

namespace PristonTale.Application.World;

// The field side of monster skills: what FieldMonsterSkills reads and changes on this field.
public sealed partial class FieldInstance : IFieldMonsterSkillHost
{
    bool IFieldMonsterSkillHost.TryGetPlayer(ulong entityId, [MaybeNullWhen(false)] out WorldPlayer player) => _players.TryGetValue(entityId, out player);

    bool IFieldMonsterSkillHost.TryGetMonster(ulong entityId, [MaybeNullWhen(false)] out MonsterInstance monster) =>
        _monsters.TryGetValue(entityId, out monster);

    Dictionary<ulong, MonsterInstance>.ValueCollection IFieldMonsterSkillHost.Monsters => _monsters.Values;

    Dictionary<ulong, WorldPlayer>.ValueCollection IFieldMonsterSkillHost.Players => _players.Values;

    IReadOnlyList<PreyCandidate> IFieldMonsterSkillHost.NearbyPlayers(Vector3 origin, float radius) => _prey.Nearby(origin, radius);

    IReadOnlyList<MonsterDefinition> IFieldMonsterSkillHost.MonsterDefinitions => _catalog.All;

    IReadOnlyList<MonsterDefinition> IFieldMonsterSkillHost.MonsterFamily(int soundCode) => _catalog.Family(soundCode);

    MonsterInstance IFieldMonsterSkillHost.SpawnMonster(MonsterDefinition definition, Vector3 position, float facing, int spawnPointIndex) =>
        ((ISpawnSink)this).Spawn(definition, position, spawnPointIndex, facing);

    void IFieldMonsterSkillHost.RemoveMonster(ulong entityId) => RemoveMonster(entityId);

    bool IFieldMonsterSkillHost.ApplyStatus(ulong entity, EffectSpec spec, float durationMs, ulong source) =>
        _skills.ApplyMonsterStatus(entity, spec, durationMs, source);

    void IFieldMonsterSkillHost.EndStuns(MonsterInstance monster) => _skills.EndStuns(monster);
}
