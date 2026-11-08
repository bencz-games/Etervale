using PristonTale.Simulation.Characters;
using PristonTale.Simulation.Combat;
using PristonTale.Simulation.Modifiers;
using PristonTale.Simulation.Skills;
using PristonTale.Simulation.World.Monsters;
using PristonTale.Contracts.Skills;

namespace PristonTale.Application.World;

// Timed skill effects: buffs and debuffs with their modifiers, periodic ticks and control states, exclusive buffs,
// the stealth that the next hit consumes, stuns that damage breaks, the end of skill buffs in town, and the counter
// strike of shield buffs.
internal sealed partial class FieldSkills
{
    private readonly List<EffectChange> _removed = new();

    // One timed effect of a skill on an entity: its modifiers (the ally share on other players), periodic ticks, control
    // state and weapon binding. A positive durationMs replaces the rank's duration (a pulse's short copy).
    private bool ApplyEffect(EffectContainer effects, ulong entity, ulong source, uint code, SkillBuffRule buff, int rank, int resistance, PlayerCharacter caster,
        bool onOther = false, int othersPercent = 100, float durationMs = 0f)
    {
        if (buff is null || buff.KeptWhileRunning && effects.TryGet(code, out _))
        {
            return false;
        }

        var duration = durationMs > 0f ? durationMs : SkillFormulas.BuffDurationMs(buff, rank, resistance);
        if (duration <= 0f)
        {
            return false;
        }

        var periodic = new PeriodicEffect[buff.Periodic.Length];
        for (var index = 0; index < periodic.Length; index++)
        {
            var entry = buff.Periodic[index];
            var low = SkillFormulas.At(entry.Min, rank);
            var high = SkillFormulas.At(entry.Max, rank);
            periodic[index] = new PeriodicEffect(entry.Resource, _rng.Range(Math.Min(low, high), Math.Max(low, high)), entry.IntervalMs, entry.PercentOfMax,
                entry.NonLethal);
        }

        var held = buff.WeaponBound && ClassSkillCatalog.TryGet(code, out var skill) ? skill.AllowedWeaponsMask : 0u;
        var modifiers = SkillFormulas.BuffModifiers(buff, rank, _rng, caster, onOther, othersPercent);
        var spec = new EffectSpec(code, EffectSourceKind.PlayerSkill, buff.Harmful, modifiers, periodic, control: buff.Control,
            stacking: buff.Harmful ? StackRule.PerSource : StackRule.Refresh, heldMask: held);
        if (effects.Apply(spec, source, duration, entity) is { } change)
        {
            _statusEvents.Add(StatusView(change, rank));
        }

        return true;
    }

    private void RemoveEffect(EffectContainer effects, ulong entity, uint code)
    {
        _removed.Clear();
        if (effects.Remove(code, entity, _removed))
        {
            foreach (var change in _removed)
            {
                _statusEvents.Add(StatusView(change, 0));
            }
        }
    }

    // The stealth bonus of the next single-target hit (percent).
    private static int NextHitPercent(PlayerCharacter character) => character.Effects.Totals.Flat(StatKind.NextHitDamage);

    // A landed single-target hit ends the effects that give the next-hit bonus.
    private void ConsumeNextHit(WorldPlayer attacker) => EndEffects(attacker, static buff => buff.EndsOnHit);

    // Any area attack ends stealth without its bonus.
    private void EndOnAreaAttack(WorldPlayer attacker) => EndEffects(attacker, static buff => buff.EndsOnAreaAttack);

    private void EndEffects(WorldPlayer player, Func<SkillBuffRule, bool> ends)
    {
        var effects = player.Character.Effects;
        for (var index = effects.Count - 1; index >= 0; index--)
        {
            var effect = effects.Effects[index];
            if (effect.Spec.Source == EffectSourceKind.PlayerSkill && SkillRuleCatalog.TryGet(effect.Code, out var rule) && rule.Buff is { } buff
                && ends(buff))
            {
                RemoveEffect(effects, player.EntityId, effect.Code);
            }
        }
    }

    // Damage ends the control effects marked to break on damage (a stun from a roar).
    private void MonsterDamaged(MonsterInstance monster)
    {
        var effects = monster.Effects;
        for (var index = effects.Count - 1; index >= 0; index--)
        {
            var effect = effects.Effects[index];
            if (SkillRuleCatalog.TryGet(effect.Code, out var rule) && rule.Buff is { EndsOnDamage: true })
            {
                RemoveEffect(effects, monster.EntityId, effect.Code);
            }
        }
    }

    // A shield buff with a counter strikes the monster whose normal attack it blocked.
    private void CounterOnBlock(WorldPlayer defender, MonsterInstance attacker)
    {
        var effects = defender.Character.Effects;
        foreach (var effect in effects.Effects)
        {
            if (!SkillRuleCatalog.TryGet(effect.Code, out var rule) || rule.Buff?.Counter is not { } counter || !attacker.IsAlive)
            {
                continue;
            }

            var borrowedRank = defender.Character.Skills.RankOf(counter.BorrowSkill);
            if (borrowedRank == 0 || !SkillRuleCatalog.TryGet(counter.BorrowSkill, out var borrowed) || borrowed.Damage is null)
            {
                continue;
            }

            var rank = defender.Character.Skills.RankOf(rule.Code);
            var percent = SkillFormulas.At(counter.Percent, rank);
            var low = SkillFormulas.At(borrowed.Damage.Min, borrowedRank) * percent / 100;
            var high = SkillFormulas.At(borrowed.Damage.Max, borrowedRank) * percent / 100;
            var damageRule = new SkillDamageRule { Element = counter.Element, BonusBroods = counter.BonusBroods, BonusPercent = counter.BonusPercent };
            var power = Math.Max(1, _rng.Range(low, Math.Max(low, high)));
            var dealt = SkillFormulas.PowerAgainst(power, damageRule, attacker.Def, defender.Character.Effects.Totals, attacker.Effects.Totals, rank);
            Pay(defender, attacker, dealt, false);
            _skillEvents.Add(new SkillEventView(defender.EntityId, attacker.EntityId, rule.Code, SkillEventPhase.Impact, rank,
                SkillEventFlags.Counter, 0f, 0f, attacker.Position, defender.Facing, 1, 0f));
            return;
        }
    }

    private void StepEffects(float dtMs)
    {
        _changes.Clear();
        foreach (var player in _host.Players)
        {
            if (player.TakeGuardChange())
            {
                _statusEvents.Add(GraceView(player));
            }

            if (player.IsDead)
            {
                player.Character.Effects.ClearTimed(player.EntityId, _changes);
                player.Regeneration.Reset();
                continue;
            }

            if (player.InVillage)
            {
                // Skill buffs end in town, as the original ends them on entering a village.
                player.Character.Effects.RemoveTimedFrom(EffectSourceKind.PlayerSkill, player.EntityId, _changes);
            }

            DropUnwieldedEffects(player);
            player.TickGuard(dtMs);
            player.Character.Effects.Step(dtMs, player.EntityId, _changes);
            player.Regeneration.Step(player.Character, player.Posture, dtMs);
        }

        foreach (var monster in _host.Monsters)
        {
            if (monster.Effects.Count == 0)
            {
                continue;
            }

            if (!monster.IsAlive)
            {
                monster.Effects.ClearTimed(monster.EntityId, _changes);
                continue;
            }

            monster.Effects.Step(dtMs, monster.EntityId, _changes);
        }

        for (var index = 0; index < _changes.Count; index++)
        {
            var change = _changes[index];
            if (change.Kind == EffectChangeKind.Ticked)
            {
                ApplyPeriodic(change);
            }

            _statusEvents.Add(StatusView(change, change.Effect.Stacks));
            if (change.Kind == EffectChangeKind.Removed)
            {
                OnEffectEnded(change);
            }
        }
    }

    // A periodic entry restores or drains a resource of its entity; life damage can kill unless it is non-lethal, which
    // stops at one life and ends the effect.
    private void ApplyPeriodic(in EffectChange change)
    {
        var periodic = change.Periodic;
        if (_host.TryGetPlayer(change.Entity, out var player) && !player.IsDead)
        {
            var character = player.Character;
            switch (periodic.Resource)
            {
                case ResourceKind.Life:
                    var amount = periodic.PercentOfMax ? character.MaxLife * periodic.Amount / 100 : periodic.Amount;
                    if (amount >= 0)
                    {
                        player.Heal(amount);
                    }
                    else
                    {
                        player.ApplyDamage(periodic.NonLethal ? Math.Min(-amount, character.Life - 1) : -amount);
                    }

                    break;
                case ResourceKind.Mana:
                    var mana = periodic.PercentOfMax ? character.MaxMana * periodic.Amount / 100 : periodic.Amount;
                    character.Mana = Math.Clamp(character.Mana + mana, 0, character.MaxMana);
                    break;
                case ResourceKind.Stamina:
                    var stamina = periodic.PercentOfMax ? character.MaxStamina * periodic.Amount / 100 : periodic.Amount;
                    character.Stamina = Math.Clamp(character.Stamina + stamina, 0, character.MaxStamina);
                    break;
            }
        }
        else if (periodic.Resource == ResourceKind.Life && _host.TryGetMonster(change.Entity, out var monster) && monster.IsAlive)
        {
            var amount = periodic.PercentOfMax ? monster.Def.Combat.MaxLife * periodic.Amount / 100 : periodic.Amount;
            if (amount < 0 && periodic.NonLethal && monster.Life + amount < 1)
            {
                amount = 1 - monster.Life;
                RemoveEffect(monster.Effects, monster.EntityId, change.Effect.Code);
            }

            if (amount < 0 && _host.TryGetPlayer(change.Effect.SourceEntity, out var source))
            {
                Pay(source, monster, -amount, false);
            }
            else if (amount != 0)
            {
                monster.Life = Math.Clamp(monster.Life + amount, 1, monster.Def.Combat.MaxLife);
            }
        }
    }

    // The player's guard as a status its viewers see; a guard cut short removes it.
    private static StatusEventView GraceView(WorldPlayer player)
    {
        var left = (int)player.GuardMsLeft;
        return new StatusEventView(player.EntityId, player.EntityId, PlayerGrace.StatusCode, left > 0 ? StatusEffectOp.Add : StatusEffectOp.Remove, 0,
            (byte)EffectSourceKind.System, 0, false, false, left, left, 0);
    }

    private StatusEventView StatusView(in EffectChange change, int stacks)
    {
        var effect = change.Effect;
        var op = change.Kind switch
        {
            EffectChangeKind.Added => StatusEffectOp.Add,
            EffectChangeKind.Refreshed => StatusEffectOp.Refresh,
            EffectChangeKind.Ticked => StatusEffectOp.Tick,
            _ => StatusEffectOp.Remove
        };
        var value = change.Kind == EffectChangeKind.Ticked ? change.Periodic.Amount : StatusValue(change.Entity, effect);
        var permanent = effect.Permanent;
        return new StatusEventView(change.Entity, effect.SourceEntity, effect.Code, op, stacks, (byte)effect.Spec.Source, (byte)effect.Spec.Control,
            effect.Spec.Harmful, permanent, permanent ? 0 : (int)effect.DurationMs, permanent ? 0 : (int)effect.RemainingMs, value);
    }

    private static int MainValue(ActiveEffect effect)
    {
        var modifiers = effect.Spec.Modifiers;
        return modifiers.Length > 0 ? modifiers[0].Value : 0;
    }
}
