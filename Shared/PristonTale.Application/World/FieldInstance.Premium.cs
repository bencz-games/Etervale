using System.Diagnostics.CodeAnalysis;
using PristonTale.Simulation.Combat;
using PristonTale.Simulation.Modifiers;
using PristonTale.Simulation.World.Monsters;

namespace PristonTale.Application.World;

// The field side of the premium timers: pet strikes go through the field's damage path and drop their kill's loot.
public sealed partial class FieldInstance : IFieldPremiumHost
{
    CombatRng IFieldPremiumHost.Rng => _rng;

    bool IFieldPremiumHost.TryGetMonster(ulong entityId, [MaybeNullWhen(false)] out MonsterInstance monster) => _monsters.TryGetValue(entityId, out monster);

    bool IFieldPremiumHost.TryGetPlayer(ulong entityId, [MaybeNullWhen(false)] out WorldPlayer player) => _players.TryGetValue(entityId, out player);

    void IFieldPremiumHost.PetStrike(WorldPlayer owner, MonsterInstance monster, int damage)
    {
        if (_combat.ApplyPlayerDamage(owner, monster, damage, false, NowMs, skill: true) is { } reward)
        {
            PlaceLoot(reward, NowMs);
        }
    }

    void IFieldPremiumHost.QueueMonsterEffect(ulong monsterId, EffectSpec spec, float durationMs) => _skills.QueueItemEffect(monsterId, spec, durationMs);

    void IFieldPremiumHost.AddItemEvent(in ItemEffectView effect) => ItemEvents.Add(effect);
}
