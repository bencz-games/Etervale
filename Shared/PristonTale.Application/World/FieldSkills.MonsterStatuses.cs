using PristonTale.Simulation.Modifiers;
using PristonTale.Simulation.World.Monsters;

namespace PristonTale.Application.World;

// Timed effects monster skills and blows put on players and monsters: applied at once on the tick, reported like every
// other status, ended by the shared timers, death and the status rules of the effect engine.
internal sealed partial class FieldSkills
{
    // The effects of a living player, else of a living monster; null for neither.
    private EffectContainer? LiveEffects(ulong entity) =>
        _host.TryGetPlayer(entity, out var player) && !player.IsDead ? player.Character.Effects
            : _host.TryGetMonster(entity, out var monster) && monster.IsAlive ? monster.Effects : null;

    public bool ApplyMonsterStatus(ulong entity, EffectSpec spec, float durationMs, ulong source)
    {
        var effects = LiveEffects(entity);
        if (effects?.Apply(spec, source, durationMs, entity) is not { } change)
        {
            return false;
        }

        _statusEvents.Add(StatusView(change, 1));
        return true;
    }

    // Ends every stun on a monster (another monster's skill wakes it).
    public void EndStuns(MonsterInstance monster)
    {
        var effects = monster.Effects;
        for (var index = effects.Count - 1; index >= 0; index--)
        {
            var effect = effects.Effects[index];
            if ((effect.Spec.Control & ControlFlags.Stun) != 0)
            {
                RemoveEffect(effects, monster.EntityId, effect.Code);
            }
        }
    }
}
