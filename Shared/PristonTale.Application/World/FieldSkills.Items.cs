using PristonTale.Simulation.Modifiers;

namespace PristonTale.Application.World;

// Timed effects of used items (scrolls, force orbs) and of pets (a slowed monster). Item uses commit in the command phase; their effects start with the
// next field step so the status events reach that step's snapshot.
internal sealed partial class FieldSkills
{
    private readonly List<(ulong Entity, EffectSpec Spec, float DurationMs, int Rank)> _pendingItemEffects = new();

    private readonly List<(ulong Entity, uint Code)> _pendingItemEnds = new();

    public void QueueItemEffect(ulong entity, EffectSpec spec, float durationMs, int rank = 1) => _pendingItemEffects.Add((entity, spec, durationMs, rank));

    // Ends an item's effect on a player with the next step (a castle item replaced or left behind). An add of that effect
    // still queued is dropped, since ends are applied before adds.
    public void EndItemEffect(WorldPlayer player, uint code)
    {
        for (var index = _pendingItemEffects.Count - 1; index >= 0; index--)
        {
            if (_pendingItemEffects[index].Entity == player.EntityId && _pendingItemEffects[index].Spec.Code == code)
            {
                _pendingItemEffects.RemoveAt(index);
            }
        }

        _pendingItemEnds.Add((player.EntityId, code));
    }

    private bool HasPendingItemEffect(WorldPlayer player, uint code)
    {
        foreach (var (entity, spec, _, _) in _pendingItemEffects)
        {
            if (entity == player.EntityId && spec.Code == code)
            {
                return true;
            }
        }

        return false;
    }

    private void StartItemEffects()
    {
        foreach (var (entity, code) in _pendingItemEnds)
        {
            if (_host.TryGetPlayer(entity, out var holder))
            {
                RemoveEffect(holder.Character.Effects, entity, code);
            }
        }

        _pendingItemEnds.Clear();
        foreach (var (entity, spec, durationMs, rank) in _pendingItemEffects)
        {
            var effects = LiveEffects(entity);
            if (effects?.Apply(spec, entity, durationMs, entity) is { } change)
            {
                _statusEvents.Add(StatusView(change, rank));
            }
        }

        _pendingItemEffects.Clear();
    }
}
