using System.Numerics;
using PristonTale.Contracts.Combat;
using PristonTale.Simulation.Combat;
using PristonTale.Simulation.Modifiers;
using PristonTale.Simulation.Skills;

namespace PristonTale.Application.World;

// Damage skills on players in the castle's battle zone (IFieldSkillHost.MayFight; the skills SkillFormulas.ReachesPlayers
// names). A weapon skill rolls the player's evasion, then its accuracy, block, critical and element against the player
// as a normal attack does, with the caster's enchants; a bolt's, an area's, a burst's and a main hit's power meets the
// player through SkillFormulas.PowerAgainstPlayer (the player's resistances and flat absorption). Every hit goes through
// IFieldSkillHost.HurtPlayer (Might Stone, absorbing shields, the board). Effects, pushes and life-share damage never
// reach players.
internal sealed partial class FieldSkills
{
    private readonly List<WorldPlayer> _foes = new();

    private bool WeaponHitOnPlayer(WorldPlayer caster, ActiveCast cast, SkillRule rule, int hit, WorldPlayer foe)
    {
        var rank = cast.Rank;
        if (!_host.MayFight(caster, foe) || !InRange(caster, foe.Position, SkillFormulas.Range(cast.Rule, rank, caster.Character)))
        {
            return false;
        }

        if (((IPlayerSkillCombat)this).Evades(caster.EntityId, foe))
        {
            cast.Missed = true;
            return false;
        }

        var character = caster.Character;
        var damageRule = rule.Damage ?? new SkillDamageRule();
        var attack = SkillFormulas.WeaponAttack(character, rule, rank, SkillFormulas.PlayerStandIn, _rng, hit);
        attack = attack with { ExtraPercent = attack.ExtraPercent + NextHitPercent(character) };
        var defender = CombatProfileFactory.ForPlayerDefender(foe.Character);
        var elemental = 0;
        if (damageRule.FlatElement is not null && damageRule.Min.Length > 0)
        {
            elemental = SkillFormulas.WithBroodBonus(_rng.Range(rule.At(damageRule.Min, rank), rule.At(damageRule.Max, rank)), damageRule,
                SkillFormulas.PlayerStandIn, rank);
            attack = attack with { SkillFlatAdd = attack.SkillFlatAdd + elemental };
        }

        int damage;
        var crit = false;
        if (rule.At(rule.Volley, rank) is var shots and > 0)
        {
            damage = VolleyDamage(attack, defender, shots);
            damage = damage <= 0 ? 0 : CombatResolver.AbsorbFlat(CombatResolver.ApplyElement(damage, attack.Element, defender.Resistances), defender.Absorption);
        }
        else
        {
            var result = CombatResolver.Resolve(_rng, attack, defender, CombatProfileFactory.HasShield(foe.Character),
                alwaysHit: damageRule.NeverMiss || rule.NoAccuracy || rule.NoAccuracyHits.Contains(hit + 1));
            if (result.Blocked)
            {
                _host.EmitCombatEvent(new CombatEventView(caster.EntityId, foe.EntityId, CombatEventKind.Block));
            }

            damage = result.Damage;
            crit = result.Crit;
            if (damage > 0 && damageRule.FlatElement is { } flatElement)
            {
                damage = Math.Max(1, damage - (elemental - CombatResolver.ApplyResistance(elemental, defender.Resistances[flatElement])));
            }
        }

        if (damage <= 0)
        {
            cast.Missed = true;
            return false;
        }

        ConsumeNextHit(caster);
        if (damageRule.Element == Element.Bionic && damageRule.FlatElement is null)
        {
            damage += ((IPlayerSkillCombat)this).EnchantOnPlayer(caster, defender);
        }

        PayLifeCost(caster, cast);
        var origin = caster.Position;
        _host.HurtPlayer(caster, foe, ModifierRules.ScaleDamage(damage, character.Effects.Totals, foe.Character.Effects.Totals), crit);
        if (cast.Rule.Burst is { } burst && burst.AfterHit == hit)
        {
            Burst(caster, cast, rule, origin);
        }

        return true;
    }

    private bool MagicHitOnPlayer(WorldPlayer caster, ActiveCast cast, SkillRule rule, WorldPlayer foe)
    {
        if (!_host.MayFight(caster, foe))
        {
            return false;
        }

        var power = SkillFormulas.RollPower(caster.Character, rule, rule.Damage, cast.Rank, _rng);
        _host.HurtPlayer(caster, foe, PowerOnPlayer(caster, foe, power, rule.Damage, cast.Rank), false);
        return true;
    }

    // An area's share of its power on each player inside it, with the clicked player's main share or main spell hit.
    private int AreaHitOnPlayers(WorldPlayer caster, ActiveCast cast, SkillRule rule, int power)
    {
        var damageRule = rule.Damage;
        if (damageRule.Mode != SkillDamageMode.Rolled || power <= 0)
        {
            return 0;
        }

        var hits = 0;
        foreach (var foe in _foes)
        {
            var defender = CombatProfileFactory.ForPlayerDefender(foe.Character);
            var isMain = foe.EntityId == cast.TargetId;
            var share = AreaShare(caster, rule, power, isMain, defender, out var crit, out var resolved);
            if (share <= 0)
            {
                continue;
            }

            if (isMain && damageRule.Main is { } main)
            {
                MainSpellHitOnPlayer(caster, cast, foe, main, defender);
            }

            _host.HurtPlayer(caster, foe, SkillFormulas.PowerAgainstPlayer(Math.Max(1, share), resolved, defender, caster.Character.Effects.Totals,
                foe.Character.Effects.Totals, cast.Rank), crit);
            hits++;
        }

        return hits;
    }

    // The clicked player's own magic hit of an area skill, as a monster's (MainSpellHit).
    private void MainSpellHitOnPlayer(WorldPlayer caster, ActiveCast cast, WorldPlayer foe, SkillDamageRule main, in DefenderProfile defender)
    {
        var character = caster.Character;
        var power = SkillFormulas.RollPower(character, cast.Rule, main, cast.Rank, _rng);
        var crit = false;
        if (!main.NoCritical)
        {
            crit = CombatResolver.RollCrit(_rng, CombatProfileFactory.ForPlayerBasicAttack(character), defender);
            power = crit ? CombatResolver.ApplyCrit(power) : power;
        }

        _host.HurtPlayer(caster, foe, SkillFormulas.MainSpellPowerAgainstPlayer(power, main, cast.Rank, defender, character.Effects.Totals,
            foe.Character.Effects.Totals), crit);
    }

    // The clicked player's weapon-rolled hit of an area skill (MainHit), besides its area share.
    private bool MainWeaponHitOnPlayer(WorldPlayer caster, SkillRule rule, int rank, WorldPlayer foe)
    {
        var power = SkillFormulas.RollPower(caster.Character, rule, rule.MainHit, rank, _rng);
        _host.HurtPlayer(caster, foe, PowerOnPlayer(caster, foe, power, rule.MainHit, rank), false);
        return true;
    }

    // The players inside an area (InArea) after the monsters, up to the cap they share; the clicked player is taken
    // beyond the cap when the rule puts its target in the area, and inside it when the rule includes its target. An area
    // that rolls accuracy leaves out the players it misses (a miss still takes its place under the cap).
    private void CollectFoes(WorldPlayer caster, Vector3 center, float reach, Vector2 forward, float halfBox, int limit, AttackProfile? accuracy = null,
        WorldPlayer? main = null, bool mainBeyondCap = false, bool includesMain = false)
    {
        _foes.Clear();
        if (!_host.PlayersFight)
        {
            return;
        }

        var taken = _targets.Count;
        foreach (var foe in _host.Players)
        {
            if (taken >= limit)
            {
                break;
            }

            var isMain = ReferenceEquals(foe, main);
            if (isMain && mainBeyondCap || !_host.MayFight(caster, foe) || MathF.Abs(foe.Position.Y - center.Y) > HeightTolerance
                || !InArea(foe.Position, center, forward, reach, halfBox) && !(isMain && includesMain))
            {
                continue;
            }

            taken++;
            if (accuracy is not { } attack || CombatResolver.RollHit(_rng, CombatResolver.HitChance(attack, CombatProfileFactory.ForPlayerDefender(foe.Character))))
            {
                _foes.Add(foe);
            }
        }

        if (mainBeyondCap && main is not null)
        {
            _foes.Add(main);
        }
    }

    private static int PowerOnPlayer(WorldPlayer caster, WorldPlayer foe, int power, SkillDamageRule damage, int rank) =>
        SkillFormulas.PowerAgainstPlayer(power, damage, CombatProfileFactory.ForPlayerDefender(foe.Character), caster.Character.Effects.Totals,
            foe.Character.Effects.Totals, rank);

    // A skill's life cost per hit, never below 1 life.
    private static void PayLifeCost(WorldPlayer caster, ActiveCast cast)
    {
        var character = caster.Character;
        var lifeCost = SkillFormulas.LifeCostPerHit(cast.Rule, cast.Rank, character.MaxLife);
        if (lifeCost > 0)
        {
            character.Life = Math.Max(1, character.Life - lifeCost);
        }
    }
}
