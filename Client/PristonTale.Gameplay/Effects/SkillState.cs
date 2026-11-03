using PristonTale.Effects.Vfx;
using PristonTale.Client.Net.Presence;
using PristonTale.Protocol.Packets;
using PristonTale.Simulation.Modifiers;
using PristonTale.Contracts.Skills;

namespace PristonTale.Gameplay.Effects;

// The local player's skill cooldowns as the server reported them, on the client's clock.
public sealed class SkillCooldownBook
{
    private readonly Dictionary<uint, (double EndMs, int DurationMs)> _running = new();

    public void Apply(SkillCastResultInfo result, double nowMs)
    {
        if (result.Accepted && result.CooldownMs > 0)
        {
            _running[result.SkillCode] = (nowMs + result.CooldownMs, result.CooldownMs);
        }
        else if (result.Status == SkillCastVerdict.Cooldown && result.RemainingMs > 0)
        {
            var duration = _running.TryGetValue(result.SkillCode, out var known) ? Math.Max(known.DurationMs, result.RemainingMs) : result.RemainingMs;
            _running[result.SkillCode] = (nowMs + result.RemainingMs, duration);
        }
    }

    // A snapshot replaces the set: skills missing from it are ready.
    public void Apply(SkillCooldownSnapshot snapshot, double nowMs)
    {
        _running.Clear();
        foreach (var cooldown in snapshot.Cooldowns)
        {
            if (cooldown.RemainingMs > 0)
            {
                _running[cooldown.SkillCode] = (nowMs + cooldown.RemainingMs, Math.Max(cooldown.DurationMs, cooldown.RemainingMs));
            }
        }
    }

    public double RemainingMs(uint skillCode, double nowMs) =>
        _running.TryGetValue(skillCode, out var cooldown) ? Math.Max(0d, cooldown.EndMs - nowMs) : 0d;

    // 1 right after a cast, 0 when ready.
    public float Fraction(uint skillCode, double nowMs)
    {
        if (!_running.TryGetValue(skillCode, out var cooldown) || cooldown.DurationMs <= 0)
        {
            return 0f;
        }

        return (float)Math.Clamp((cooldown.EndMs - nowMs) / cooldown.DurationMs, 0d, 1d);
    }

    public bool Ready(uint skillCode, double nowMs) => RemainingMs(skillCode, nowMs) <= 0d;
}

public sealed class StatusEntry
{
    public uint Code { get; init; }
    public ulong SourceId { get; set; }
    public EffectSourceKind SourceKind { get; init; }
    public ControlFlags Control { get; set; }
    public bool Harmful { get; init; }
    public bool Permanent { get; init; }
    public int DurationMs { get; set; }
    public double EndMs { get; set; }
    public int Stacks { get; set; }
    public int Value { get; set; }
    public VfxHandle Aura { get; set; }

    public double RemainingMs(double nowMs) => Permanent ? double.PositiveInfinity : Math.Max(0d, EndMs - nowMs);
}

// Modifier effects on every entity the client sees, from the server's status messages. The client keeps the timers
// running between messages so the buff bar counts down; the server's remove message is authoritative.
public sealed class StatusBook
{
    private readonly Dictionary<ulong, List<StatusEntry>> _byEntity = new();
    private readonly List<StatusEntry> _empty = new();

    public event Action<ulong, StatusEntry> Added;
    public event Action<ulong, StatusEntry> Removed;

    // The concrete list, so frame code walks it without boxing an enumerator; callers only read it.
    public List<StatusEntry> For(ulong entityId) => _byEntity.TryGetValue(entityId, out var list) ? list : _empty;

    public void Apply(StatusEffectInfo status, double nowMs)
    {
        if (!_byEntity.TryGetValue(status.EntityId, out var list))
        {
            list = new List<StatusEntry>(4);
            _byEntity.Add(status.EntityId, list);
        }

        var index = list.FindIndex(entry => entry.Code == status.StatusCode && (!entry.Harmful || entry.SourceId == status.SourceId));
        switch (status.Op)
        {
            case StatusEffectOp.Add:
            case StatusEffectOp.Refresh:
                if (index >= 0)
                {
                    var entry = list[index];
                    entry.DurationMs = status.DurationMs;
                    entry.EndMs = nowMs + status.RemainingMs;
                    entry.Stacks = status.Stacks;
                    entry.Value = status.Value;
                    entry.Control = (ControlFlags)status.Control;
                    return;
                }

                var added = new StatusEntry
                {
                    Code = status.StatusCode,
                    SourceId = status.SourceId,
                    SourceKind = (EffectSourceKind)status.SourceKind,
                    Control = (ControlFlags)status.Control,
                    Harmful = status.Harmful,
                    Permanent = status.Permanent,
                    DurationMs = status.DurationMs,
                    EndMs = nowMs + status.RemainingMs,
                    Stacks = status.Stacks,
                    Value = status.Value
                };
                list.Add(added);
                Added?.Invoke(status.EntityId, added);
                return;
            case StatusEffectOp.Remove:
                if (index >= 0)
                {
                    var removed = list[index];
                    list.RemoveAt(index);
                    Removed?.Invoke(status.EntityId, removed);
                }

                return;
        }
    }

    public void Forget(ulong entityId)
    {
        if (!_byEntity.Remove(entityId, out var list))
        {
            return;
        }

        foreach (var entry in list)
        {
            Removed?.Invoke(entityId, entry);
        }
    }

    public void Clear()
    {
        foreach (var entity in _byEntity.Keys.ToArray())
        {
            Forget(entity);
        }
    }

    // Every status of every entity, walked without allocating.
    public AllStatuses All() => new(_byEntity);

    public readonly struct AllStatuses(Dictionary<ulong, List<StatusEntry>> byEntity)
    {
        public Enumerator GetEnumerator() => new(byEntity.GetEnumerator());

        public struct Enumerator(Dictionary<ulong, List<StatusEntry>>.Enumerator entities)
        {
            private Dictionary<ulong, List<StatusEntry>>.Enumerator _entities = entities;
            private List<StatusEntry> _list;
            private int _index;

            public (ulong EntityId, StatusEntry Entry) Current => (_entities.Current.Key, _list[_index]);

            public bool MoveNext()
            {
                while (_list is null || ++_index >= _list.Count)
                {
                    if (!_entities.MoveNext())
                    {
                        return false;
                    }

                    _list = _entities.Current.Value;
                    _index = -1;
                }

                return true;
            }
        }
    }
}
