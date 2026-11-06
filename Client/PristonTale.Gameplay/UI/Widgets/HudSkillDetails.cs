using System.Globalization;
using PristonTale.Simulation.Modifiers;
using PristonTale.Simulation.Skills;
using PristonTale.Gameplay.Localization;

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
            lines.Add(ratingPercent != 0 ? ClientText.Format("SkillInfo.AttackRatingPercent", Signed(ratingPercent)) : ClientText.Format("SkillInfo.AttackRating", Signed(rating)));
        }

        if (rule.At(rule.CriticalAdd, rank) is var critical and > 0)
        {
            lines.Add(ClientText.Format("SkillInfo.Critical", Signed(critical)));
        }

        if (rule.HealMin.Length == 10)
        {
            lines.Add(ClientText.Format("SkillInfo.Heals", rule.At(rule.HealMin, rank), rule.At(rule.HealMax, rank)));
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
            lines.Add(ClientText.Format("SkillInfo.Charm", SkillFormulas.RankValue(charm.Chance, rank), SkillFormulas.RankValue(charm.LifePercent, rank)));
        }

        if (rule.Resurrect is { } resurrect)
        {
            lines.Add(ClientText.Format("SkillInfo.Revive", SkillFormulas.RankValue(resurrect.Chance, rank), resurrect.LifePercent));
        }

        if (rule.Summon is { } summon)
        {
            lines.Add(ClientText.Format("SkillInfo.Summon", summon.Monster, rule.At(summon.DamageMin, rank), rule.At(summon.DamageMax, rank), rule.At(summon.Life, rank)));
        }

        return lines;
    }

    private static void Damage(List<string> lines, SkillRule rule, SkillDamageRule damage, int rank)
    {
        switch (damage.Mode)
        {
            case SkillDamageMode.MaxLifeShare:
                lines.Add(ClientText.Format("SkillInfo.Drains", rule.At(damage.Share, rank)));
                return;
            case SkillDamageMode.CurrentLifeShare:
                lines.Add(ClientText.Format("SkillInfo.ShareChance", rule.At(damage.Chance, rank), rule.At(damage.Share, rank)));
                return;
        }

        var percent = rule.At(damage.Percent, rank);
        if (damage.Min.Length == 10)
        {
            lines.Add(damage.Element == Simulation.Combat.Element.Bionic
                ? ClientText.Format("SkillInfo.Damage", rule.At(damage.Min, rank), rule.At(damage.Max, rank))
                : ClientText.Format("SkillInfo.DamageElement", rule.At(damage.Min, rank), rule.At(damage.Max, rank), ClientText.Get("Element." + damage.Element)));
            var tablePercent = SkillFormulas.RankValue(damage.TablePercent, rank);
            if (tablePercent != 0)
            {
                lines.Add(ClientText.Format("SkillInfo.AddingRate", Signed(tablePercent)));
            }
        }

        if (percent != 0)
        {
            lines.Add(ClientText.Format("SkillInfo.DamagePercent", Signed(percent)));
        }

        if (damage.RandomHits.Length == 10)
        {
            lines.Add(ClientText.Format("SkillInfo.Sparks", rule.At(damage.RandomHits, rank)));
        }

        if (rule.Volley.Length == 10)
        {
            lines.Add(ClientText.Format("SkillInfo.Shots", rule.At(rule.Volley, rank)));
        }

        var bonus = SkillFormulas.RankValue(damage.BonusPercentByRank, rank);
        bonus = bonus != 0 ? bonus : damage.BonusPercent;
        if (bonus != 0 && damage.BonusBroods.Length > 0)
        {
            lines.Add(ClientText.Format("SkillInfo.BroodBonus", Broods(damage.BonusBroods), Signed(bonus)));
        }

        var hits = SkillFormulas.HitCount(rule, rank);
        if (SkillFormulas.Plan(rule, rank).Motion == SkillMotion.Skill && hits > 1)
        {
            lines.Add(ClientText.Format("SkillInfo.Hits", hits));
        }
    }

    private static void Area(List<string> lines, SkillRule rule, int rank)
    {
        var range = rule.At(rule.Range, rank);
        if (range > 0)
        {
            lines.Add(ClientText.Format(rule.RangeMode == SkillRangeMode.AddWeapon ? "SkillInfo.RangeAdded" : "SkillInfo.Range", range));
        }

        if (rule.Kind != SkillKind.Area)
        {
            return;
        }

        var radius = rule.At(rule.Radius, rank);
        var targets = rule.MaxTargetsByRank.Length > 0 ? ClientText.Format("SkillInfo.Targets", rule.At(rule.MaxTargetsByRank, rank)) : "";
        lines.Add(rule.AreaShape switch
        {
            SkillAreaShape.Box => ClientText.Format("SkillInfo.AreaBox", rule.BoxWidth, radius, targets),
            SkillAreaShape.Chain => ClientText.Format("SkillInfo.Chain", rule.At(rule.MaxTargetsByRank, rank), radius),
            _ => ClientText.Format("SkillInfo.Area", radius, targets)
        });
    }

    private static void Buff(List<string> lines, SkillRule rule, SkillBuffRule buff, int rank)
    {
        lines.Add(ClientText.Format("SkillInfo.Duration", (rule.At(buff.DurationTenths, rank) / 10f).ToString("0.#", CultureInfo.InvariantCulture)));
        foreach (var modifier in buff.Modifiers)
        {
            lines.Add(modifier.RankOf != 0
                ? ClientText.Format("SkillInfo.ByRelatedRank", Label(modifier.Stat))
                : modifier.LevelDivisor > 0
                    ? ClientText.Format("SkillInfo.PlusLevel", Modifier(modifier.Stat, modifier.Op, rule.At(modifier.Values, rank)), modifier.LevelDivisor)
                    : Modifier(modifier.Stat, modifier.Op, rule.At(modifier.Values, rank)));
        }

        if (buff.Absorb is { } absorb)
        {
            lines.Add(ClientText.Format("SkillInfo.Absorbs", SkillFormulas.RankValue(absorb.Percent, rank), ClientText.Get("Resource." + absorb.Resource)));
        }

        if (buff.Reflect is { } reflect)
        {
            lines.Add(ClientText.Format("SkillInfo.Reflects", SkillFormulas.RankValue(reflect.Percent, rank), Broods(reflect.Broods)));
        }

        if (buff.Guard is { } guard)
        {
            lines.Add(ClientText.Format(guard.OnBlock ? "SkillInfo.GuardBlocked" : "SkillInfo.Guard", SkillFormulas.RankValue(guard.HealPercent, rank), Broods(guard.Broods)));
        }

        if (buff.Enchant is { } enchant)
        {
            foreach (var choice in enchant.Choices)
            {
                lines.Add(ClientText.Format("SkillInfo.Enchant", ClientText.Get("Element." + choice.Element), SkillFormulas.RankValue(choice.Min, rank), SkillFormulas.RankValue(choice.Max, rank)));
            }
        }

        if (buff.Companion is { } companion)
        {
            lines.Add(ClientText.Format("SkillInfo.StrikesEvery", (rule.At(companion.IntervalMs, rank) / 1000f).ToString("0.#", CultureInfo.InvariantCulture)));
        }

        if (rule.Allies is { Radius.Length: 10 } allies)
        {
            lines.Add(allies.OthersPercent < 100
                ? ClientText.Format("SkillInfo.AlliesShare", rule.At(allies.Radius, rank), allies.OthersPercent)
                : ClientText.Format("SkillInfo.Allies", rule.At(allies.Radius, rank)));
        }
    }

    private static string Modifier(StatKind stat, ModifierOp op, int value) =>
        $"{Label(stat)} {Signed(value)}{(op == ModifierOp.Percent || PercentStat(stat) ? "%" : "")}";

    private static bool PercentStat(StatKind stat) => stat is StatKind.BlockChance or StatKind.CriticalChance or StatKind.DamageVsUndead or StatKind.Evasion
        or StatKind.SpellPower or StatKind.WeaponDamagePercent or StatKind.AbsorptionVsUndead;

    private static string Label(StatKind stat) => ClientText.Get("Stat." + stat);

    private static string Broods(IEnumerable<Simulation.World.Monsters.MonsterBrood> broods) =>
        string.Join("/", broods.Select(static brood => ClientText.Get("Brood." + brood)));

    private static string Signed(int value) => value > 0 ? "+" + value.ToString(CultureInfo.InvariantCulture) : value.ToString(CultureInfo.InvariantCulture);
}
