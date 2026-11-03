namespace PristonTale.Simulation.Modifiers;

// The immutable template of a timed or permanent effect. Code identifies it on the wire and for stacking; skills use
// their skill code, items their item code, and other sources their own ranges.
public sealed class EffectSpec
{
    private readonly StatModifier[] _modifiers;
    private readonly PeriodicEffect[] _periodic;

    public EffectSpec(uint code, EffectSourceKind source, bool harmful, ReadOnlySpan<StatModifier> modifiers, ReadOnlySpan<PeriodicEffect> periodic = default,
        ControlFlags control = ControlFlags.None, StackRule stacking = StackRule.Refresh, int maxStacks = 1, uint heldMask = 0)
    {
        foreach (var effect in periodic)
        {
            if (!float.IsFinite(effect.IntervalMs) || effect.IntervalMs <= 0f)
            {
                throw new ArgumentOutOfRangeException(nameof(periodic), "Periodic effects need a positive interval.");
            }
        }

        Code = code;
        Source = source;
        Harmful = harmful;
        _modifiers = modifiers.ToArray();
        _periodic = periodic.ToArray();
        Control = control;
        Stacking = stacking;
        MaxStacks = Math.Max(1, maxStacks);
        HeldMask = heldMask;
    }

    public uint Code { get; }
    public EffectSourceKind Source { get; }
    public bool Harmful { get; }
    public ControlFlags Control { get; }
    public StackRule Stacking { get; }
    public int MaxStacks { get; }
    // Item kinds (bit per kind) one of which the holder must hold for the modifiers to count; 0 always counts.
    public uint HeldMask { get; }
    public ReadOnlySpan<StatModifier> Modifiers => _modifiers;
    public ReadOnlySpan<PeriodicEffect> Periodic => _periodic;

    // The sum of the spec's modifier values, used by KeepStrongest.
    public int Magnitude
    {
        get
        {
            var total = 0;
            foreach (var modifier in _modifiers)
            {
                total += Math.Abs(modifier.Value);
            }

            foreach (var periodic in _periodic)
            {
                total += Math.Abs(periodic.Amount);
            }

            return total;
        }
    }
}

public sealed class ActiveEffect
{
    private readonly float[] _sinceTickMs;

    internal ActiveEffect(EffectSpec spec, ulong sourceEntity, float durationMs)
    {
        Spec = spec;
        SourceEntity = sourceEntity;
        DurationMs = durationMs;
        RemainingMs = durationMs;
        Stacks = 1;
        _sinceTickMs = new float[spec.Periodic.Length];
    }

    public EffectSpec Spec { get; private set; }
    public uint Code => Spec.Code;
    public ulong SourceEntity { get; private set; }
    public int Stacks { get; private set; }

    // Permanent effects (equipped items, auras held by their source) have no timer and are removed explicitly.
    public bool Permanent => float.IsPositiveInfinity(DurationMs);
    public float DurationMs { get; private set; }
    public float RemainingMs { get; private set; }

    internal void Reapply(EffectSpec spec, ulong sourceEntity, float durationMs)
    {
        if (spec.Stacking == StackRule.Stack)
        {
            Stacks = Math.Min(spec.MaxStacks, Stacks + 1);
        }

        Spec = spec;
        SourceEntity = sourceEntity;
        DurationMs = durationMs;
        RemainingMs = durationMs;
        Array.Clear(_sinceTickMs);
    }

    internal void Advance(float dtMs, ulong entity, List<EffectChange> changes)
    {
        if (!Permanent)
        {
            RemainingMs = MathF.Max(0f, RemainingMs - dtMs);
        }

        var periodic = Spec.Periodic;
        for (var index = 0; index < periodic.Length; index++)
        {
            _sinceTickMs[index] += dtMs;
            while (_sinceTickMs[index] >= periodic[index].IntervalMs)
            {
                _sinceTickMs[index] -= periodic[index].IntervalMs;
                changes.Add(new EffectChange(entity, this, EffectChangeKind.Ticked, index));
            }
        }
    }
}

public enum EffectChangeKind : byte
{
    Added = 1,
    Refreshed = 2,
    Removed = 3,
    // One periodic entry of the effect fired; PeriodicIndex selects it.
    Ticked = 4
}

public readonly record struct EffectChange(ulong Entity, ActiveEffect Effect, EffectChangeKind Kind, int PeriodicIndex = -1)
{
    public PeriodicEffect Periodic => Effect.Spec.Periodic[PeriodicIndex];
}

// All modifier effects on one entity, from every source. Only the tick that owns the entity mutates it. Totals are
// cached per revision, so stat reads between changes cost nothing.
public sealed class EffectContainer
{
    private readonly List<ActiveEffect> _effects = new(4);
    private readonly ModifierTotals _totals = new();
    private long _totalsRevision = -1;

    public long Revision { get; private set; }
    public int Count => _effects.Count;

    // Item kinds the holder holds (bit per kind), for effects bound to a weapon or a shield.
    public uint Held { get; private set; }

    public void SetHeld(uint held)
    {
        if (held != Held)
        {
            Held = held;
            Revision++;
        }
    }
    public IReadOnlyList<ActiveEffect> Effects => _effects;

    // Keeps one permanent effect in line with its modifiers (the passive skills', the quests' and the premium timers'
    // effects): removed when modifiers is null, left as it is when already the same, applied anew otherwise.
    public void SetPermanent(uint code, EffectSourceKind source, bool harmful, StatModifier[] modifiers)
    {
        var hasEffect = TryGet(code, out var current);
        if (modifiers is null)
        {
            if (hasEffect)
            {
                Remove(code, 0, null);
            }

            return;
        }

        if (hasEffect && current.Permanent && current.Spec.Modifiers.SequenceEqual(modifiers))
        {
            return;
        }

        Apply(new EffectSpec(code, source, harmful, modifiers), 0, float.PositiveInfinity);
    }

    public ControlFlags Control
    {
        get
        {
            var control = ControlFlags.None;
            foreach (var effect in _effects)
            {
                control |= effect.Spec.Control;
            }

            return control;
        }
    }

    public bool Has(ControlFlags flag) => (Control & flag) != 0;

    public ModifierTotals Totals
    {
        get
        {
            if (_totalsRevision != Revision)
            {
                _totals.Rebuild(_effects, Held);
                _totalsRevision = Revision;
            }

            return _totals;
        }
    }

    public bool TryGet(uint code, out ActiveEffect effect)
    {
        foreach (var candidate in _effects)
        {
            if (candidate.Code == code)
            {
                effect = candidate;
                return true;
            }
        }

        effect = null;
        return false;
    }

    // durationMs = +infinity applies a permanent effect. Returns the change, or null when KeepStrongest kept the old one.
    public EffectChange? Apply(EffectSpec spec, ulong sourceEntity, float durationMs, ulong entity = 0)
    {
        ArgumentNullException.ThrowIfNull(spec);
        if (float.IsNaN(durationMs) || durationMs <= 0f)
        {
            throw new ArgumentOutOfRangeException(nameof(durationMs));
        }

        var running = Find(spec.Code, spec.Stacking == StackRule.PerSource ? sourceEntity : null);
        if (running is null)
        {
            var added = new ActiveEffect(spec, sourceEntity, durationMs);
            _effects.Add(added);
            Revision++;
            return new EffectChange(entity, added, EffectChangeKind.Added);
        }

        if (spec.Stacking == StackRule.KeepStrongest && spec.Magnitude < running.Spec.Magnitude)
        {
            return null;
        }

        running.Reapply(spec, sourceEntity, durationMs);
        Revision++;
        return new EffectChange(entity, running, EffectChangeKind.Refreshed);
    }

    public bool Remove(uint code, ulong entity, List<EffectChange> changes)
    {
        var removed = false;
        for (var index = _effects.Count - 1; index >= 0; index--)
        {
            if (_effects[index].Code == code)
            {
                RemoveAt(index, entity, changes);
                removed = true;
            }
        }

        return removed;
    }

    // Removes the effects one source applied, for example every modifier of an unequipped item.
    public int RemoveFrom(EffectSourceKind source, ulong sourceEntity, ulong entity, List<EffectChange> changes)
    {
        var count = 0;
        for (var index = _effects.Count - 1; index >= 0; index--)
        {
            var effect = _effects[index];
            if (effect.Spec.Source == source && effect.SourceEntity == sourceEntity)
            {
                RemoveAt(index, entity, changes);
                count++;
            }
        }

        return count;
    }

    // Removes the timed effects of one source kind (skill buffs that end in town).
    public void RemoveTimedFrom(EffectSourceKind source, ulong entity, List<EffectChange> changes)
    {
        for (var index = _effects.Count - 1; index >= 0; index--)
        {
            if (_effects[index].Spec.Source == source && !_effects[index].Permanent)
            {
                RemoveAt(index, entity, changes);
            }
        }
    }

    // Clears timed effects (death, field change); permanent effects stay with their source.
    public void ClearTimed(ulong entity, List<EffectChange> changes)
    {
        for (var index = _effects.Count - 1; index >= 0; index--)
        {
            if (!_effects[index].Permanent)
            {
                RemoveAt(index, entity, changes);
            }
        }
    }

    // Advances timers, reports periodic ticks and expiries into the caller's scratch list.
    public void Step(float dtMs, ulong entity, List<EffectChange> changes)
    {
        if (_effects.Count == 0 || !float.IsFinite(dtMs) || dtMs <= 0f)
        {
            return;
        }

        for (var index = _effects.Count - 1; index >= 0; index--)
        {
            var effect = _effects[index];
            effect.Advance(dtMs, entity, changes);
            if (!effect.Permanent && effect.RemainingMs <= 0f)
            {
                RemoveAt(index, entity, changes);
            }
        }
    }

    private ActiveEffect Find(uint code, ulong? source)
    {
        foreach (var effect in _effects)
        {
            if (effect.Code == code && (source is null || effect.SourceEntity == source.Value))
            {
                return effect;
            }
        }

        return null;
    }

    private void RemoveAt(int index, ulong entity, List<EffectChange> changes)
    {
        var effect = _effects[index];
        _effects.RemoveAt(index);
        Revision++;
        changes?.Add(new EffectChange(entity, effect, EffectChangeKind.Removed));
    }
}

// Flat and percent sums per stat over all active effects, stacks included.
public sealed class ModifierTotals
{
    private readonly int[] _flat = new int[(int)StatKind.Count];
    private readonly int[] _percent = new int[(int)StatKind.Count];

    public static ModifierTotals Empty { get; } = new();

    public int Flat(StatKind stat) => _flat[(int)stat];

    public int Percent(StatKind stat) => _percent[(int)stat];

    public bool IsEmpty
    {
        get
        {
            for (var index = 0; index < _flat.Length; index++)
            {
                if (_flat[index] != 0 || _percent[index] != 0)
                {
                    return false;
                }
            }

            return true;
        }
    }

    // (base + flat) x (100 + percent) / 100, truncated toward zero like the original integer stat code.
    public int Apply(StatKind stat, int value)
    {
        var sum = (long)value + _flat[(int)stat];
        var percent = _percent[(int)stat];
        if (percent != 0)
        {
            sum = sum * (100 + percent) / 100;
        }

        return (int)Math.Clamp(sum, int.MinValue, int.MaxValue);
    }

    internal void Rebuild(IReadOnlyList<ActiveEffect> effects, uint held)
    {
        Array.Clear(_flat);
        Array.Clear(_percent);
        foreach (var effect in effects)
        {
            if (effect.Spec.HeldMask != 0 && (effect.Spec.HeldMask & held) == 0)
            {
                continue;
            }

            foreach (var modifier in effect.Spec.Modifiers)
            {
                var target = modifier.Op == ModifierOp.Flat ? _flat : _percent;
                target[(int)modifier.Stat] += modifier.Value * effect.Stacks;
            }
        }
    }
}
