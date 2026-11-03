using PristonTale.Simulation.Combat;
using PristonTale.Simulation.Modifiers;
using PristonTale.Simulation.Skills;
using PristonTale.Simulation.World.Monsters;
using PristonTale.Contracts.Combat;

namespace PristonTale.Application.World;

// How skill effects take part in the field's normal combat: the attack side (weapon enchants, the bonus against undead
// and the stealth bonus a landed basic attack consumes), the defence side (evasion, reflected damage, absorb shields
// that a resource pays for, the life a guard returns on blocks or hits, the counter strike of a shield buff) and the
// stuns that damage breaks. FieldCombat calls these on the field tick.
internal interface IPlayerSkillCombat
{
    // The player's basic attack landed: effects that end with the next hit end.
    void ConsumeNextHit(WorldPlayer player);

    // A player's hit damaged the monster: control effects that damage breaks end.
    void MonsterDamaged(MonsterInstance monster);

    // The player's basic attack landed on the monster for this damage; returns the damage with the skill effects.
    int BasicStrike(WorldPlayer player, MonsterInstance monster, int damage);

    // True when a skill effect lets the player evade the monster's normal attack entirely.
    bool Evades(MonsterInstance attacker, WorldPlayer target);

    // A matching monster attacks the player: reflection effects strike it back with a share of its own attack.
    void Reflect(MonsterInstance attacker, WorldPlayer target);

    // The player blocked the monster's normal attack.
    void Blocked(MonsterInstance attacker, WorldPlayer target);

    // The monster's attack reaches the player for this damage; returns what the player's life still loses.
    int Defend(MonsterInstance attacker, WorldPlayer target, int damage, bool skillAttack);
}

internal sealed partial class FieldSkills : IPlayerSkillCombat
{
    void IPlayerSkillCombat.ConsumeNextHit(WorldPlayer player) => ConsumeNextHit(player);

    void IPlayerSkillCombat.MonsterDamaged(MonsterInstance monster) => MonsterDamaged(monster);

    int IPlayerSkillCombat.BasicStrike(WorldPlayer player, MonsterInstance monster, int damage)
    {
        damage = SkillFormulas.WithUndeadBonus(damage, player.Character.Effects.Totals, monster.Def);
        return damage + EnchantStrike(player, monster, basicAttack: true);
    }

    // Evasion effects roll first, then the evasion passives (value + 1).
    bool IPlayerSkillCombat.Evades(MonsterInstance attacker, WorldPlayer target)
    {
        var evasion = target.Character.Effects.Totals.Flat(StatKind.Evasion);
        var passive = PassiveSkills.Evasion(target.Character);
        if ((evasion <= 0 || _rng.RollPercent() >= evasion) && (passive <= 0 || _rng.RollPercent() >= passive))
        {
            return false;
        }

        _host.EmitCombatEvent(new CombatEventView(attacker.EntityId, target.EntityId, CombatEventKind.Evade));
        return true;
    }

    void IPlayerSkillCombat.Reflect(MonsterInstance attacker, WorldPlayer target)
    {
        foreach (var (code, rule) in SkillEffects(target, static buff => buff.Reflect is not null))
        {
            if (!attacker.IsAlive || !SkillFormulas.MatchesBrood(rule.Buff.Reflect.Broods, attacker.Def))
            {
                continue;
            }

            var range = attacker.Def.Combat.AttackDamage;
            var power = _rng.Range(range.Min, Math.Max(range.Min, range.Max)) * SkillFormulas.RankValue(rule.Buff.Reflect.Percent, RankOfEffect(target.EntityId, code)) / 100;
            power = CombatResolver.ApplyResistance(power, attacker.Def.Combat.Resistances.Bionic);
            power -= power * attacker.Def.Combat.Absorption / 100;
            if (power > 0)
            {
                Pay(target, attacker, power, false);
            }
        }
    }

    void IPlayerSkillCombat.Blocked(MonsterInstance attacker, WorldPlayer target)
    {
        CounterOnBlock(target, attacker);
        var range = attacker.Def.Combat.AttackDamage;
        Guard(attacker, target, _rng.Range(range.Min, Math.Max(range.Min, range.Max)), onBlock: true);
    }

    int IPlayerSkillCombat.Defend(MonsterInstance attacker, WorldPlayer target, int damage, bool skillAttack)
    {
        if (damage <= 0)
        {
            return damage;
        }

        if (skillAttack)
        {
            damage = ModifierRules.ChangedBy(damage, target.Character.Effects.Totals, StatKind.SkillDamageTaken, 1);
        }

        var character = target.Character;
        foreach (var (code, rule) in SkillEffects(target, static buff => buff.Absorb is not null))
        {
            var absorb = rule.Buff.Absorb;
            var absorbed = damage * SkillFormulas.RankValue(absorb.Percent, RankOfEffect(target.EntityId, code)) / 100;
            damage -= absorbed;
            // The shield takes its share even when the resource runs out; the resource stops at zero.
            switch (absorb.Resource)
            {
                case ResourceKind.Mana:
                    character.Mana = Math.Max(0, character.Mana - absorbed);
                    break;
                case ResourceKind.Stamina:
                    character.Stamina = Math.Max(0, character.Stamina - absorbed);
                    break;
                case ResourceKind.Life:
                    damage += absorbed;
                    break;
            }
        }

        if (!skillAttack)
        {
            Guard(attacker, target, damage, onBlock: false);
        }

        return Math.Max(0, damage);
    }

    private void Guard(MonsterInstance attacker, WorldPlayer target, int power, bool onBlock)
    {
        if (power <= 0 || target.IsDead)
        {
            return;
        }

        foreach (var (code, rule) in SkillEffects(target, onBlock ? GuardsOnBlock : GuardsOnHit))
        {
            var guard = rule.Buff.Guard;
            if (!SkillFormulas.MatchesBrood(guard.Broods, attacker.Def))
            {
                continue;
            }

            var heal = power * SkillFormulas.RankValue(guard.HealPercent, RankOfEffect(target.EntityId, code)) / 100;
            if (heal > 0)
            {
                HealPlayer(target, target, heal);
            }
        }
    }

    // Element damage an enchant adds to the holder's hit, resisted and absorbed like the hit; ice also slows the monster.
    private int EnchantStrike(WorldPlayer player, MonsterInstance monster, bool basicAttack)
    {
        var added = 0;
        foreach (var (code, rule) in SkillEffects(player, static buff => buff.Enchant is not null))
        {
            var enchant = rule.Buff.Enchant;
            if (enchant.BasicOnly && !basicAttack || !monster.IsAlive)
            {
                continue;
            }

            var key = (player.EntityId, code);
            var choice = enchant.Choices[Math.Clamp(_choices.GetValueOrDefault(key), 0, enchant.Choices.Length - 1)];
            var rank = RankOfEffect(player.EntityId, code);
            var part = _rng.Range(SkillFormulas.RankValue(choice.Min, rank), Math.Max(SkillFormulas.RankValue(choice.Min, rank), SkillFormulas.RankValue(choice.Max, rank)));
            var resistances = ModifierRules.Resistances(monster.Effects.Totals, monster.Def.Combat.Resistances);
            part = CombatResolver.ApplyResistance(part, resistances[choice.Element]);
            added += part > 0 ? CombatResolver.ApplyAbsorbMonster(part, monster.Def.Combat.Absorption) : 0;
            Slow(player, monster, rule.Code, choice, rank);
        }

        return added;
    }

    // An ice choice slows the struck monster; ice resistance shortens the slow.
    private void Slow(WorldPlayer source, MonsterInstance monster, uint code, SkillEnchantChoice choice, int rank)
    {
        var percent = SkillFormulas.RankValue(choice.SlowPercent, rank);
        if (percent == 0 || !monster.IsAlive)
        {
            return;
        }

        var duration = SkillFormulas.SlowDurationMs(choice, rank, monster.Def.Combat.Resistances.Ice);
        if (duration <= 0f)
        {
            return;
        }

        var spec = new EffectSpec(code, EffectSourceKind.PlayerSkill, true, [StatModifier.Percent(StatKind.MoveSpeed, percent)], control: ControlFlags.Slow,
            stacking: StackRule.PerSource);
        if (monster.Effects.Apply(spec, source.EntityId, duration, monster.EntityId) is { } change)
        {
            _statusEvents.Add(StatusView(change, rank));
        }
    }

    private static readonly Func<SkillBuffRule, bool> GuardsOnBlock = static buff => buff.Guard is { OnBlock: true };
    private static readonly Func<SkillBuffRule, bool> GuardsOnHit = static buff => buff.Guard is { OnBlock: false };

    // The holder's running player-skill effects whose rule's buff matches; walked on the tick for every blow, block and
    // hit, so it allocates nothing.
    private static SkillEffectRun SkillEffects(WorldPlayer holder, Func<SkillBuffRule, bool> predicate) => new(holder.Character.Effects.Effects, predicate);

    private struct SkillEffectRun(IReadOnlyList<ActiveEffect> effects, Func<SkillBuffRule, bool> predicate)
    {
        private int _index = -1;

        public (uint Code, SkillRule Rule) Current { get; private set; }

        public readonly SkillEffectRun GetEnumerator() => this;

        public bool MoveNext()
        {
            while (++_index < effects.Count)
            {
                var effect = effects[_index];
                if (effect.Spec.Source == EffectSourceKind.PlayerSkill && !effect.Permanent && SkillRuleCatalog.TryGet(effect.Code, out var rule)
                    && rule.Buff is { } buff && predicate(buff))
                {
                    Current = (effect.Code, rule);
                    return true;
                }
            }

            return false;
        }
    }
}
