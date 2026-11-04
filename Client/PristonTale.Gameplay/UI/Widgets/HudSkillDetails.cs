using System.Globalization;
using PristonTale.Simulation.Modifiers;
using PristonTale.Simulation.Skills;

namespace PristonTale.Gameplay.UI.Widgets;

// The rank's numbers of a skill for its tooltip, read from the same rule the server resolves with: damage, accuracy,
// area, duration, the modifiers it grants and the special effects of support and defence skills.
public static class HudSkillDetails
{
    public static IReadOnlyList<string> Lines(SkillRule rule, int rank)
    {
        var lines = new List<string>(6);
        var damage = rule.Damage;
        if (damage is not null)
        {
            Damage(lines, rule, damage, rank);
        }

        var rating = rule.At(rule.AttackRating, rank);
        var ratingPercent = rule.At(rule.AttackRatingPercent, rank);
        if (rating != 0 || ratingPercent != 0)
        {
            lines.Add(ratingPercent != 0 ? $"Attack rating {Signed(ratingPercent)}%" : $"Attack rating {Signed(rating)}");
        }

        if (rule.At(rule.CriticalAdd, rank) is var critical and > 0)
        {
            lines.Add($"Critical {Signed(critical)}%");
        }

        if (rule.HealMin.Length == 10)
        {
            lines.Add($"Heals {rule.At(rule.HealMin, rank)}-{rule.At(rule.HealMax, rank)} (+ weapon / 3, spirit)");
        }

        Area(lines, rule, rank);
        if (rule.Buff is { } buff)
        {
            Buff(lines, rule, buff, rank);
        }

        if (rule.Passive is { } passive)
        {
            foreach (var modifier in passive.Modifiers)
            {
                lines.Add(Modifier(modifier.Stat, modifier.Op, modifier.Values[Math.Clamp(rank, 1, 10) - 1]));
            }
        }

        if (rule.Charm is { } charm)
        {
            lines.Add($"Success {SkillFormulas.RankValue(charm.Chance, rank)}%, life {SkillFormulas.RankValue(charm.LifePercent, rank)}%");
        }

        if (rule.Resurrect is { } resurrect)
        {
            lines.Add($"Revive chance {SkillFormulas.RankValue(resurrect.Chance, rank)}%, life {resurrect.LifePercent}%");
        }

        if (rule.Summon is { } summon)
        {
            lines.Add($"{summon.Monster}: damage {rule.At(summon.DamageMin, rank)}-{rule.At(summon.DamageMax, rank)}, life {rule.At(summon.Life, rank)}");
        }

        return lines;
    }

    private static void Damage(List<string> lines, SkillRule rule, SkillDamageRule damage, int rank)
    {
        switch (damage.Mode)
        {
            case SkillDamageMode.MaxLifeShare:
                lines.Add($"Drains {rule.At(damage.Share, rank)}% of maximum life");
                return;
            case SkillDamageMode.CurrentLifeShare:
                lines.Add($"{rule.At(damage.Chance, rank)}% chance: {rule.At(damage.Share, rank)}% of life");
                return;
        }

        var percent = rule.At(damage.Percent, rank);
        if (damage.Min.Length == 10)
        {
            lines.Add($"Damage {rule.At(damage.Min, rank)}-{rule.At(damage.Max, rank)}{(damage.Element == Simulation.Combat.Element.Bionic ? "" : " " + damage.Element)}");
            var tablePercent = SkillFormulas.RankValue(damage.TablePercent, rank);
            if (tablePercent != 0)
            {
                lines.Add($"Adding rate {Signed(tablePercent)}%");
            }
        }

        if (percent != 0)
        {
            lines.Add($"Damage {Signed(percent)}%");
        }

        if (damage.RandomHits.Length == 10)
        {
            lines.Add($"Up to {rule.At(damage.RandomHits, rank)} sparks");
        }

        if (rule.Volley.Length == 10)
        {
            lines.Add($"{rule.At(rule.Volley, rank)} shots");
        }

        var bonus = SkillFormulas.RankValue(damage.BonusPercentByRank, rank);
        bonus = bonus != 0 ? bonus : damage.BonusPercent;
        if (bonus != 0 && damage.BonusBroods.Length > 0)
        {
            lines.Add($"{string.Join("/", damage.BonusBroods)} {Signed(bonus)}%");
        }

        var hits = SkillFormulas.HitCount(rule, rank);
        if (SkillFormulas.Plan(rule, rank).Motion == SkillMotion.Skill && hits > 1)
        {
            lines.Add($"{hits} hits");
        }
    }

    private static void Area(List<string> lines, SkillRule rule, int rank)
    {
        var range = rule.At(rule.Range, rank);
        if (range > 0)
        {
            lines.Add(rule.RangeMode == SkillRangeMode.AddWeapon ? $"Range +{range}" : $"Range {range}");
        }

        if (rule.Kind != SkillKind.Area)
        {
            return;
        }

        var radius = rule.At(rule.Radius, rank);
        var targets = rule.MaxTargetsByRank.Length > 0 ? $", {rule.At(rule.MaxTargetsByRank, rank)} targets" : "";
        lines.Add(rule.AreaShape switch
        {
            SkillAreaShape.Box => $"Area {rule.BoxWidth} x {radius}{targets}",
            SkillAreaShape.Chain => $"Chain of {rule.At(rule.MaxTargetsByRank, rank)}, link {radius}",
            _ => $"Area {radius}{targets}"
        });
    }

    private static void Buff(List<string> lines, SkillRule rule, SkillBuffRule buff, int rank)
    {
        lines.Add($"Duration {rule.At(buff.DurationTenths, rank) / 10f:0.#} s");
        foreach (var modifier in buff.Modifiers)
        {
            var value = modifier.RankOf != 0 ? "rank of a related skill" : null;
            lines.Add(value is null ? Modifier(modifier.Stat, modifier.Op, rule.At(modifier.Values, rank)) + (modifier.LevelDivisor > 0 ? $" + level/{modifier.LevelDivisor}" : "")
                : $"{Label(modifier.Stat)} by the {value}");
        }

        if (buff.Absorb is { } absorb)
        {
            lines.Add($"Absorbs {SkillFormulas.RankValue(absorb.Percent, rank)}% of damage with {absorb.Resource}");
        }

        if (buff.Reflect is { } reflect)
        {
            lines.Add($"Reflects {SkillFormulas.RankValue(reflect.Percent, rank)}% of {string.Join("/", reflect.Broods)} attacks");
        }

        if (buff.Guard is { } guard)
        {
            lines.Add($"{SkillFormulas.RankValue(guard.HealPercent, rank)}% of {string.Join("/", guard.Broods)} {(guard.OnBlock ? "blocked" : "")} damage to life");
        }

        if (buff.Enchant is { } enchant)
        {
            foreach (var choice in enchant.Choices)
            {
                lines.Add($"{choice.Element} +{SkillFormulas.RankValue(choice.Min, rank)}-{SkillFormulas.RankValue(choice.Max, rank)}");
            }
        }

        if (buff.Companion is { } companion)
        {
            lines.Add($"Strikes every {rule.At(companion.IntervalMs, rank) / 1000f:0.#} s");
        }

        if (rule.Allies is { Radius.Length: 10 } allies)
        {
            lines.Add($"Allies within {rule.At(allies.Radius, rank)}{(allies.OthersPercent < 100 ? $" ({allies.OthersPercent}%)" : "")}");
        }
    }

    private static string Modifier(StatKind stat, ModifierOp op, int value) =>
        $"{Label(stat)} {Signed(value)}{(op == ModifierOp.Percent || PercentStat(stat) ? "%" : "")}";

    private static bool PercentStat(StatKind stat) => stat is StatKind.BlockChance or StatKind.CriticalChance or StatKind.DamageVsUndead or StatKind.Evasion
        or StatKind.SpellPower or StatKind.WeaponDamagePercent or StatKind.AbsorptionVsUndead;

    private static string Label(StatKind stat) => stat switch
    {
        StatKind.DamageMin => "Minimum damage",
        StatKind.DamageMax => "Maximum damage",
        StatKind.DamageVsUndead => "Damage vs undead",
        StatKind.AbsorptionVsUndead => "Absorption vs undead",
        StatKind.WeaponDamagePercent => "Weapon damage",
        StatKind.SpellPower => "Spell damage",
        StatKind.ManaRegen => "Mana regeneration (tenths/s)",
        StatKind.LifeRegen => "Life regeneration (tenths/s)",
        StatKind.MoveSpeed => "Speed",
        StatKind.DamageTaken => "Damage taken",
        StatKind.SkillDamageTaken => "Skill damage taken",
        StatKind.HealingTaken => "Healing taken",
        StatKind.AttackRange => "Range",
        _ => SplitWords(stat.ToString())
    };

    private static string SplitWords(string text) => string.Concat(text.Select((character, index) =>
        index > 0 && char.IsUpper(character) ? " " + char.ToLowerInvariant(character) : character.ToString()));

    private static string Signed(int value) => value > 0 ? "+" + value.ToString(CultureInfo.InvariantCulture) : value.ToString(CultureInfo.InvariantCulture);
}
