using System.Numerics;
using PristonTale.Simulation.Combat;
using PristonTale.Simulation.Modifiers;
using PristonTale.Simulation.Skills;
using PristonTale.Contracts.Combat;
using PristonTale.Contracts.Skills;

namespace PristonTale.Application.World;

// Support skills: buffs on the caster, on one ally or on every ally around the caster, group heals, effects that end
// with the weapon, and the short buffs a channelled field keeps renewing on the allies around its holder. An ally is any
// other living player of the field within the skill's ally radius, or only a member of the caster's party when the rule
// says so (SkillAllyRule.Party). A buff that starts a familiar or ends the caster's summon goes through here as well.
internal sealed partial class FieldSkills
{
    private readonly List<WorldPlayer> _allies = new();
    private readonly List<uint> _codeScratch = new();
    private readonly List<EffectChange> _supportChanges = new();
    private readonly List<Pulse> _pulses = new();
    // The rank an effect was applied with and the element choice of enchants and companions, per holder and skill.
    private readonly Dictionary<(ulong Entity, uint Code), int> _effectRanks = new();
    private readonly Dictionary<(ulong Entity, uint Code), int> _choices = new();

    // The resolved rule may be a mode of the cast skill; the effect keeps the skill's code and takes the mode's buff.
    private int ApplySupport(WorldPlayer caster, ActiveCast cast, SkillRule resolved, long nowMs)
    {
        var rule = resolved.Buff is null || ReferenceEquals(resolved, cast.Rule) ? cast.Rule : cast.Rule with { Buff = resolved.Buff };
        var rank = cast.Rank;
        if (rule.Buff is null)
        {
            return 0;
        }

        if (rule.Target == SkillTarget.Ally)
        {
            var holder = caster;
            if (cast.TargetId != caster.EntityId && (!_host.TryGetPlayer(cast.TargetId, out holder) || holder.IsDead))
            {
                return 0;
            }

            if (SupportBlocked(holder, rule))
            {
                return 0;
            }

            ApplyPlayerEffect(holder, caster, rule, rank, !ReferenceEquals(holder, caster), 0f, nowMs);
            if (!ReferenceEquals(holder, caster))
            {
                EmitAllyImpact(caster, holder, rule, rank);
            }

            return 1 + (rule.Allies is { Radius.Length: > 0 } ? SupportAround(caster, holder, rule, rank, nowMs) : 0);
        }

        ApplyPlayerEffect(caster, caster, rule, rank, false, 0f, nowMs);
        if (rule.Buff.Pulse is not null)
        {
            StartPulse(caster, rule, rank, nowMs);
            return 1;
        }

        var count = 1;
        foreach (var ally in AlliesAround(caster, rule, rank))
        {
            ApplyPlayerEffect(ally, caster, rule, rank, true, 0f, nowMs);
            EmitAllyImpact(caster, ally, rule, rank);
            count++;
        }

        return count;
    }

    // An ally rule with an allies radius also reaches the caster and the allies around the caster, besides the player it
    // went to; those already holding it are skipped.
    private int SupportAround(WorldPlayer caster, WorldPlayer holder, SkillRule rule, int rank, long nowMs)
    {
        var count = 0;
        if (!ReferenceEquals(holder, caster) && !SupportBlocked(caster, rule))
        {
            ApplyPlayerEffect(caster, caster, rule, rank, false, 0f, nowMs);
            count++;
        }

        foreach (var ally in AlliesAround(caster, rule, rank))
        {
            if (ReferenceEquals(ally, holder) || SupportBlocked(ally, rule))
            {
                continue;
            }

            ApplyPlayerEffect(ally, caster, rule, rank, true, 0f, nowMs);
            EmitAllyImpact(caster, ally, rule, rank);
            count++;
        }

        return count;
    }

    // A support effect the holder already carries (or one it cannot be combined with) refuses another application.
    private static bool SupportBlocked(WorldPlayer holder, SkillRule rule)
    {
        var effects = holder.Character.Effects;
        foreach (var code in rule.Exclusive)
        {
            if (effects.TryGet(code, out _))
            {
                return true;
            }
        }

        foreach (var code in rule.Allies?.ExcludeWith ?? [])
        {
            if (effects.TryGet(code, out _))
            {
                return true;
            }
        }

        return false;
    }

    private void ApplyPlayerEffect(WorldPlayer holder, WorldPlayer caster, SkillRule rule, int rank, bool onOther, float durationMs, long nowMs)
    {
        var buff = rule.Buff;
        var key = (holder.EntityId, rule.Code);
        _effectRanks[key] = rank;
        var choices = buff.Enchant?.Choices ?? buff.Companion?.Choices;
        if (choices is { Length: > 0 })
        {
            _choices[key] = _rng.NextExclusive(0, choices.Length);
        }

        if (!ApplyEffect(holder.Character.Effects, holder.EntityId, caster.EntityId, rule.Code, buff, rank, 0, caster.Character, onOther,
            rule.Allies?.OthersPercent ?? 100, durationMs))
        {
            return;
        }

        if (buff.Companion is not null)
        {
            StartCompanion(holder, rule, rank, nowMs);
        }
    }

    // The other living players of the field within the skill's ally radius of the caster.
    private List<WorldPlayer> AlliesAround(WorldPlayer caster, SkillRule rule, int rank)
    {
        _allies.Clear();
        var allies = rule.Allies;
        var radius = allies is null ? 0 : rule.At(allies.Radius, rank);
        if (allies is null || radius <= 0)
        {
            return _allies;
        }

        var reach = radius * (float)radius;
        foreach (var player in _host.Players)
        {
            if (ReferenceEquals(player, caster) || player.IsDead || player.EntityId == caster.EntityId || allies.Party && !caster.SharesPartyWith(player)
                || MathF.Abs(player.Position.Y - caster.Position.Y) > allies.Height || SupportBlocked(player, rule))
            {
                continue;
            }

            var offset = new Vector2(player.Position.X - caster.Position.X, player.Position.Z - caster.Position.Z);
            if (offset.LengthSquared() <= reach)
            {
                _allies.Add(player);
            }
        }

        return _allies;
    }

    // One heal roll shared by every ally in range (and the caster when the rule includes it).
    private int HealAllies(WorldPlayer caster, ActiveCast cast)
    {
        var amount = SkillFormulas.RollHeal(caster.Character, cast.Rule, cast.Rank, _rng);
        var count = 0;
        if (cast.Rule.Allies.IncludeCaster)
        {
            HealPlayer(caster, caster, amount);
            count++;
        }

        foreach (var ally in AlliesAround(caster, cast.Rule, cast.Rank))
        {
            HealPlayer(caster, ally, amount);
            EmitAllyImpact(caster, ally, cast.Rule, cast.Rank);
            count++;
        }

        return count;
    }

    // Heals are lowered by the holder's healing-taken modifiers (a virtual life shrinks the heals it receives).
    private void HealPlayer(WorldPlayer caster, WorldPlayer target, int amount)
    {
        amount = ModifierRules.ChangedBy(amount, target.Character.Effects.Totals, StatKind.HealingTaken, 0);
        var before = target.Life;
        target.Heal(amount);
        _host.EmitCombatEvent(new CombatEventView(caster.EntityId, target.EntityId, CombatEventKind.Heal, damage: target.Life - before));
    }

    private void EmitAllyImpact(WorldPlayer caster, WorldPlayer ally, SkillRule rule, int rank)
    {
        _skillEvents.Add(new SkillEventView(caster.EntityId, ally.EntityId, rule.Code, SkillEventPhase.Impact, rank, SkillEventFlags.Ally, 0f, 0f,
            ally.Position, caster.Facing, 1, 0f));
    }

    // Casting a skill ends the effects it replaces on the caster (one shield blessing at a time, one familiar), and the
    // summon of a replaced summon skill.
    private void RemoveReplaced(WorldPlayer player, SkillRule rule)
    {
        if (rule.Replaces.Length == 0)
        {
            return;
        }

        _supportChanges.Clear();
        foreach (var code in rule.Replaces)
        {
            if (code == _summonSkills.GetValueOrDefault(player.EntityId))
            {
                DismissSummon(player.EntityId);
            }

            player.Character.Effects.Remove(code, player.EntityId, _supportChanges);
        }

        foreach (var change in _supportChanges)
        {
            _statusEvents.Add(StatusView(change, change.Effect.Stacks));
        }
    }

    // Effects bound to the skill's weapons end as soon as the holder wields another weapon.
    private void DropUnwieldedEffects(WorldPlayer player)
    {
        var effects = player.Character.Effects;
        if (effects.Count == 0)
        {
            return;
        }

        _codeScratch.Clear();
        foreach (var effect in effects.Effects)
        {
            if (effect.Spec.Source == EffectSourceKind.PlayerSkill && !effect.Permanent && SkillRuleCatalog.TryGet(effect.Code, out var rule)
                && rule.Buff is { RequiresWeapon: true } && FindSkill(player.Character, effect.Code) is { } skill
                && !SkillFormulas.WeaponAllowed(player.Character, skill))
            {
                _codeScratch.Add(effect.Code);
            }
        }

        foreach (var code in _codeScratch)
        {
            effects.Remove(code, player.EntityId, _changes);
        }
    }

    private void StartPulse(WorldPlayer caster, SkillRule rule, int rank, long nowMs)
    {
        _pulses.RemoveAll(pulse => pulse.CasterId == caster.EntityId && pulse.Rule.Code == rule.Code);
        _pulses.Add(new Pulse(caster.EntityId, rule, rank) { NextAtMs = nowMs });
    }

    // While the holder keeps the field's effect, the allies around it receive a short copy at every pulse.
    private void StepPulses(long nowMs)
    {
        for (var index = _pulses.Count - 1; index >= 0; index--)
        {
            var pulse = _pulses[index];
            if (!_host.TryGetPlayer(pulse.CasterId, out var caster) || caster.IsDead || !caster.Character.Effects.TryGet(pulse.Rule.Code, out _))
            {
                _pulses.RemoveAt(index);
                continue;
            }

            if (pulse.NextAtMs > nowMs)
            {
                continue;
            }

            var timing = pulse.Rule.Buff.Pulse;
            foreach (var ally in AlliesAround(caster, pulse.Rule, pulse.Rank))
            {
                ApplyPlayerEffect(ally, caster, pulse.Rule, pulse.Rank, true, timing.DurationTenths * 100f, nowMs);
            }

            pulse.NextAtMs = Math.Max(pulse.NextAtMs + timing.IntervalMs, nowMs + 1);
        }
    }

    private void ForgetSupport(ulong playerId)
    {
        _pulses.RemoveAll(pulse => pulse.CasterId == playerId);
        _companions.RemoveAll(companion => companion.HolderId == playerId);
        foreach (var key in _effectRanks.Keys.Where(key => key.Entity == playerId).ToArray())
        {
            _effectRanks.Remove(key);
            _choices.Remove(key);
        }
    }

    // Status values: the element drawn by enchants and companions (Element as an integer), the main modifier otherwise.
    private int StatusValue(ulong entity, ActiveEffect effect)
    {
        if (_choices.TryGetValue((entity, effect.Code), out var choice) && SkillRuleCatalog.TryGet(effect.Code, out var rule)
            && (rule.Buff?.Enchant?.Choices ?? rule.Buff?.Companion?.Choices) is { } choices && choice < choices.Length)
        {
            return (int)choices[choice].Element;
        }

        return MainValue(effect);
    }

    private int RankOfEffect(ulong entity, uint code) => _effectRanks.GetValueOrDefault((entity, code), 1);

    private sealed class Pulse(ulong casterId, SkillRule rule, int rank)
    {
        public ulong CasterId { get; } = casterId;
        public SkillRule Rule { get; } = rule;
        public int Rank { get; } = rank;
        public long NextAtMs { get; set; }
    }
}
