using PristonTale.Simulation.Characters;
using PristonTale.Simulation.Combat;
using PristonTale.Simulation.Items;
using PristonTale.Simulation.Progression;

namespace PristonTale.Simulation.Skills;

// The learned passive skills' contribution to the derived stats, resistances, weapon damage and combat chances. Every
// passive is a rule of the class skill files; the derived-stats pass and the combat profiles read these.
public static class PassiveSkills
{
    public static DerivedStats Apply(DerivedStats stats, PlayerCharacter character)
    {
        var staminaPercent = 0;
        var life = 0;
        var rating = 0;
        var weapon = Weapon(character);
        foreach (var (rule, value) in Learned(character.Skills))
        {
            switch (rule.Passive.Effect)
            {
                case PassiveEffect.MaxStaminaPercent:
                    staminaPercent += value;
                    break;
                case PassiveEffect.MaxLife:
                    life += value;
                    break;
                case PassiveEffect.WeaponAttackRatingPercent when weapon is not null && Qualifies(rule, character, weapon):
                    rating += value * weapon.TotalStats.AttackRating / 100;
                    break;
            }
        }

        // The stamina bonus is a truncated fraction of the already computed maximum, added once.
        var bonus = (int)(staminaPercent * (float)stats.MaxStamina / 100f);
        return stats with { MaxStamina = stats.MaxStamina + bonus, MaxLife = stats.MaxLife + life, AttackRating = stats.AttackRating + rating };
    }

    public static ResistanceBlock Resistances(LearnedSkills skills)
    {
        Span<int> values = stackalloc int[8];
        foreach (var (rule, value) in Learned(skills))
        {
            if (rule.Passive.Effect == PassiveEffect.Resistance)
            {
                values[(int)rule.Passive.Element] += value;
            }
        }

        return new ResistanceBlock((short)values[0], (short)values[1], (short)values[2], (short)values[3], (short)values[4], (short)values[5],
            (short)values[6], (short)values[7]);
    }

    public static (int Min, int Max) WeaponBonus(PlayerCharacter character, ItemInstance weapon)
    {
        if (weapon is null)
        {
            return default;
        }

        return WeaponDamage.PercentOfWeapon(weapon, Sum(character, PassiveEffect.WeaponDamagePercent, weapon));
    }

    public static int CriticalChance(PlayerCharacter character) => Sum(character, PassiveEffect.CriticalChance, Weapon(character));

    public static int BlockChance(PlayerCharacter character) => Sum(character, PassiveEffect.BlockChance, Weapon(character));

    // Evasion Mastery rolls value + 1 against a percent roll (a "less than or equal" test).
    public static int Evasion(PlayerCharacter character)
    {
        var total = 0;
        foreach (var (rule, value) in Learned(character.Skills))
        {
            if (rule.Passive.Effect == PassiveEffect.Evasion)
            {
                total += value + 1;
            }
        }

        return total;
    }

    private static int Sum(PlayerCharacter character, PassiveEffect effect, ItemInstance weapon)
    {
        if (weapon is null)
        {
            return 0;
        }

        var total = 0;
        foreach (var (rule, value) in Learned(character.Skills))
        {
            if (rule.Passive.Effect == effect && Qualifies(rule, character, weapon))
            {
                total += value;
            }
        }

        return total;
    }

    private static bool Qualifies(SkillRule rule, PlayerCharacter character, ItemInstance weapon)
    {
        if (rule.Passive.WithoutShield && CombatProfileFactory.HasShield(character))
        {
            return false;
        }

        if (rule.Passive.ClassWeapon)
        {
            return (weapon.JobSpecMask & (uint)character.Definition.JobBitCode) != 0;
        }

        return ClassSkillCatalog.TryGet(rule.Code, out var skill) && skill.AllowedWeaponsMask != 0 && skill.AllowsWeapon(weapon.Definition.Kind);
    }

    private static ItemInstance Weapon(PlayerCharacter character)
    {
        var weapon = character.GetEquipped(EquipmentSlot.Weapon);
        return weapon;
    }

    // The derived stats and combat profiles ask for the passives many times per tick; the list is rebuilt only when the
    // learned skills change.
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<LearnedSkills, LearnedPassives> Cache = new();

    private static (SkillRule Rule, int Value)[] Learned(LearnedSkills skills)
    {
        var cached = Cache.GetValue(skills, static _ => new LearnedPassives());
        if (cached.Revision != skills.Revision || cached.Entries is null)
        {
            var entries = new List<(SkillRule, int)>();
            foreach (var (code, rank) in skills.Ranks)
            {
                if (rank > 0 && SkillRuleCatalog.TryGet(code, out var rule) && rule.Passive is { Values.Length: SkillLearning.MaxRank })
                {
                    entries.Add((rule, rule.At(rule.Passive.Values, rank)));
                }
            }

            cached.Entries = entries.ToArray();
            cached.Revision = skills.Revision;
        }

        return cached.Entries;
    }

    private sealed class LearnedPassives
    {
        public long Revision = -1;
        public (SkillRule Rule, int Value)[] Entries;
    }
}
