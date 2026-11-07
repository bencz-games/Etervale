using System.Numerics;
using PristonTale.Simulation.Combat;
using PristonTale.Simulation.Items.Usable;
using PristonTale.Simulation.Modifiers;
using PristonTale.Simulation.Skills;
using PristonTale.Simulation.World.Monsters;
using PristonTale.Contracts.Skills;

namespace PristonTale.Application.World;

// Familiars of buff skills (a falcon that joins its owner's fight, orbs, a phantom sword, a spirit): while the holder
// keeps the effect they strike on their own once per interval, never missing and never critical, and credit the holder.
// A familiar of its owner's fight waits for the owner's next attack; one that seeks the nearest enemy within its radius
// starts its interval over when none is near. A strike that flies waits for its flight there and back.
internal sealed partial class FieldSkills
{
    private readonly List<Companion> _companions = new();

    private void StartCompanion(WorldPlayer holder, SkillRule rule, int rank, long nowMs)
    {
        _companions.RemoveAll(companion => companion.HolderId == holder.EntityId && companion.Rule.Code == rule.Code);
        _companions.Add(new Companion(holder.EntityId, rule, rank) { NextAtMs = nowMs + SkillFormulas.At(rule.Buff.Companion.IntervalMs, rank) });
    }

    private void StepCompanions(long nowMs)
    {
        for (var index = _companions.Count - 1; index >= 0; index--)
        {
            var companion = _companions[index];
            if (!_host.TryGetPlayer(companion.HolderId, out var holder) || holder.IsDead || !holder.Character.Effects.TryGet(companion.Rule.Code, out _))
            {
                _companions.RemoveAt(index);
                continue;
            }

            if (companion.NextAtMs > nowMs)
            {
                continue;
            }

            var rules = companion.Rule.Buff.Companion;
            var interval = SkillFormulas.At(rules.IntervalMs, companion.Rank);
            if (CompanionTarget(holder, rules) is not { } target)
            {
                if (!rules.OwnerTarget)
                {
                    companion.NextAtMs = nowMs + interval;
                }

                continue;
            }

            var flightMs = FlightMs(rules, Vector3.Distance(holder.Position, target.Position));
            companion.NextAtMs = nowMs + interval + (long)flightMs;
            Strike(holder, companion, target);
        }
    }

    private static float FlightMs(SkillCompanionRule companion, float distance)
    {
        var flight = 0f;
        foreach (var speed in companion.FlightSpeeds)
        {
            flight += speed > 0 ? distance / speed * 1000f : 0f;
        }

        return flight;
    }

    private MonsterInstance? CompanionTarget(WorldPlayer holder, SkillCompanionRule companion)
    {
        if (companion.OwnerTarget)
        {
            var fighting = holder.IsBusy && holder.Action is PlayerActionState.Swing or PlayerActionState.Cast || holder.AttackTargetId != 0;
            return fighting && _host.TryGetMonster(holder.CombatTargetId, out var target) && Strikeable(holder, target) ? target : null;
        }

        MonsterInstance? nearest = null;
        var best = (float)companion.Radius * companion.Radius;
        foreach (var monster in _host.Monsters)
        {
            if (!Strikeable(holder, monster) || MathF.Abs(monster.Position.Y - holder.Position.Y) > HeightTolerance)
            {
                continue;
            }

            var distance = new Vector2(monster.Position.X - holder.Position.X, monster.Position.Z - holder.Position.Z).LengthSquared();
            if (distance < best)
            {
                best = distance;
                nearest = monster;
            }
        }

        return nearest;
    }

    private void Strike(WorldPlayer holder, Companion companion, MonsterInstance target)
    {
        var rule = companion.Rule;
        var rules = rule.Buff.Companion;
        var rank = companion.Rank;
        var character = holder.Character;
        int dealt;
        if (rules.Choices.Length > 0)
        {
            // A drawn element (spirit or phantom weapon): its range with the weapon and spirit terms, resisted by its element.
            var choice = rules.Choices[Math.Clamp(_choices.GetValueOrDefault((holder.EntityId, rule.Code)), 0, rules.Choices.Length - 1)];
            var (low, high) = SkillFormulas.CompanionDamage(rules, choice, rank, character);
            var damage = _rng.Range(low, high) + character.Effects.Totals.Flat(StatKind.CompanionDamage);
            var resistances = ModifierRules.Resistances(target.Effects.Totals, target.Def.Combat.Resistances);
            damage = CombatResolver.ApplyResistance(damage, resistances[choice.Element]);
            damage = CombatResolver.ApplyAbsorbMonster(damage, target.Def.Combat.Absorption + target.Effects.Totals.Flat(StatKind.Absorption));
            dealt = ModifierRules.ScaleDamage(damage, character.Effects.Totals, target.Effects.Totals);
            Slow(holder, target, rule.Code, choice, rank);
        }
        else
        {
            var low = SkillFormulas.At(rules.Min, rank);
            var high = SkillFormulas.At(rules.Max, rank);
            if (rules.BorrowSkill != 0 && SkillRuleCatalog.TryGet(rules.BorrowSkill, out var borrowed) && borrowed.Buff?.Companion is { } source)
            {
                var borrowedRank = character.Skills.RankOf(rules.BorrowSkill);
                var percent = SkillFormulas.At(rules.Percent, rank);
                low = SkillFormulas.At(source.Min, borrowedRank) * percent / 100;
                high = SkillFormulas.At(source.Max, borrowedRank) * percent / 100;
            }

            var power = _rng.Range(low, Math.Max(low, high)) + character.Effects.Totals.Flat(StatKind.CompanionDamage);
            var damageRule = new SkillDamageRule { Element = rules.Element, BonusBroods = rules.BonusBroods, BonusPercent = rules.BonusPercent };
            dealt = SkillFormulas.PowerAgainst(Math.Max(1, power), damageRule, target.Def, character.Effects.Totals, target.Effects.Totals, rank);
        }

        _skillEvents.Add(new SkillEventView(holder.EntityId, target.EntityId, rule.Code, SkillEventPhase.Impact, rank, SkillEventFlags.Companion,
            0f, 0f, target.Position, holder.Facing, 1, 0f));
        Pay(holder, target, dealt, false);
    }

    private void OnEffectEnded(in EffectChange change)
    {
        var code = change.Effect.Code;
        if (_summonSkills.TryGetValue(change.Entity, out var summonSkill) && summonSkill == code)
        {
            DismissSummon(change.Entity);
        }

        if (MonsterCrystals.TryGetByPetStatus(code, out _))
        {
            EndCrystalPets(change.Entity, code);
        }

        EndShrink(change);
    }

    private sealed class Companion(ulong holderId, SkillRule rule, int rank)
    {
        public ulong HolderId { get; } = holderId;
        public SkillRule Rule { get; } = rule;
        public int Rank { get; } = rank;
        public long NextAtMs { get; set; }
    }
}
