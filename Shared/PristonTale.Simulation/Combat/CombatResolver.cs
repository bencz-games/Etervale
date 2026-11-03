using PristonTale.Simulation.World.Monsters;

namespace PristonTale.Simulation.Combat;

public readonly record struct HitResult(bool Hit, bool Blocked, bool Crit, int Damage, Element Element);

public static class CombatResolver
{
    public static int MonsterHitChance(in AttackProfile attack, in DefenderProfile defender)
    {
        var difference = ((long)attack.AttackRating - defender.Defence) * 2;
        var accuracy = AccuracyTable[^1].Hit;
        foreach (var entry in AccuracyTable)
        {
            if (difference <= entry.Threshold)
            {
                accuracy = entry.Hit;
                break;
            }
        }

        var chance = (int)(accuracy - ((defender.Level - attack.AttackerLevel) / 100f) * 50f);
        return Math.Clamp(chance, 30, 95);
    }

    public static HitResult ResolveMonsterAttack(CombatRng rng, in AttackProfile attack, in DefenderProfile defender, bool defenderHasShield)
    {
        if (RollBlock(rng, defender, attack.AttackerLevel, defenderHasShield))
        {
            return new HitResult(true, true, false, 0, attack.Element);
        }

        // The reference receiver uses <=, unlike the player's outgoing < comparison.
        if (rng.RollPercent() > MonsterHitChance(attack, defender))
        {
            return new HitResult(false, false, false, 0, attack.Element);
        }

        var damage = RollBaseDamage(rng, attack);
        damage = ApplyElement(damage, attack.Element, defender.Resistances);
        damage = Math.Max(1, damage - defender.Absorption);
        return new HitResult(true, false, false, damage, attack.Element);
    }

    public static HitResult ResolveMonsterSkill(CombatRng rng, MonsterRange damageRange, in DefenderProfile defender)
    {
        int damage = rng.Range(damageRange.Min, damageRange.Max);
        damage = (int)Math.Clamp((long)damage - defender.Absorption, 1, int.MaxValue);
        return new HitResult(true, false, false, damage, Element.Bionic);
    }

    // Accuracy step table {accuracy-rating threshold, hit%} ascending — the lookup walks to the bracket and takes
    // the matched entry's hit%, as in the original client.
    private static readonly (int Threshold, int Hit)[] AccuracyTable =
    {
        (-380, 50), (-360, 51), (-340, 52), (-320, 53), (-300, 54), (-280, 55), (-260, 56), (-240, 57),
        (-220, 58), (-200, 59), (-180, 60), (-160, 61), (-140, 62), (-120, 63), (-100, 64),  (-80, 65),
         (-60, 66),  (-40, 67),  (-20, 68),    (0, 69),   (10, 70),   (20, 71),   (30, 72),   (40, 73),
          (50, 74),   (60, 75),   (70, 76),   (80, 77),   (90, 78),  (100, 79),  (110, 80),  (120, 81),
         (150, 82),  (190, 83),  (240, 84),  (300, 85),  (370, 86),  (450, 87),  (540, 88),  (640, 89),
         (750, 90),  (950, 91), (1300, 92), (1600, 93), (2000, 94), (2500, 95)
    };

    public static int HitChance(in AttackProfile a, in DefenderProfile d)
    {
        int acR = (int)(((float)a.AttackRating - (float)d.Defence) * 1.4f);

        int realAc;
        if (acR < -190)
            realAc = 50;
        else if (acR > 2100)
            realAc = 95;
        else
            realAc = LookupAccuracy(acR);

        int result = (int)(realAc - ((d.Level - a.AttackerLevel) / 100.0f) * 28f);
        if (result < 30) result = 30;
        if (result > 95) result = 95;
        return result;
    }

    // Walk the ascending step table to the bracket the threshold falls in. The hit% is that of the last entry
    // whose threshold acR has reached; below the first threshold it is the table floor (50).
    private static int LookupAccuracy(int acR)
    {
        int hit = AccuracyTable[0].Hit;
        foreach (var (threshold, h) in AccuracyTable)
        {
            if (acR >= threshold)
                hit = h;
            else
                break;
        }
        return hit;
    }

    public static bool RollHit(CombatRng rng, int hitChance) => rng.RollPercent() < hitChance;

    public static int RollBaseDamage(CombatRng rng, in AttackProfile a) => rng.Range(a.DamageMin, a.DamageMax);

    // value x percent / 100 in 64 bits, saturated: damage is 32-bit and the percents stack.
    public static int PercentOf(int value, int percent) => (int)Math.Clamp((long)value * percent / 100, int.MinValue, int.MaxValue);

    public static int ApplySkill(int dmg, in AttackProfile a) =>
        dmg + PercentOf(dmg, a.SkillMultiplierPercent) + a.SkillFlatAdd;

    public static bool RollCrit(CombatRng rng, in AttackProfile a, in DefenderProfile d)
    {
        int chance = a.CritChance + ((a.AttackerLevel - d.Level) * 25) / 100 + (a.CritChance > 0 ? d.CritChanceTaken : 0);
        if (chance > 70) chance = 70;
        return rng.RollPercent() < chance;
    }

    public static int ApplyCrit(int dmg, int bonusPercent = 0) => PercentOf(dmg, AttackProfile.CritMultiplierPercent + bonusPercent);

    public static int ApplyElement(int dmg, Element e, in ResistanceBlock r) => ApplyResistance(dmg, r[e]);

    // A resistance from -100 (double damage) to 100 (no damage) takes its percent of the damage.
    public static int ApplyResistance(int dmg, int resistance)
    {
        var rs = Math.Clamp(resistance, -100, 100);
        return dmg - PercentOf(dmg, rs);
    }

    // A resistance from 0 to 100 shortens a duration or lowers a chance by its percent; below 0 it changes nothing.
    public static float Shortened(float value, int resistance) => value * (100 - Math.Clamp(resistance, 0, 100)) / 100f;

    public static int Shortened(int value, int resistance) => value * (100 - Math.Clamp(resistance, 0, 100)) / 100;

    public static bool RollBlock(CombatRng rng, in DefenderProfile d, int attackerLevel, bool hasShield)
    {
        int block = (int)(d.BlockChance + ((d.Level - attackerLevel) / 100.0f) * 25f);
        if (hasShield && block > 50) block = 50;
        if (block <= 0) return false;
        return rng.RollPercent() < block;
    }

    public static int ApplyAbsorbMonster(int dmg, int absorption)
    {
        dmg -= PercentOf(dmg, absorption);
        if (dmg <= 0) dmg = 1;
        return dmg;
    }

    public static HitResult Resolve(CombatRng rng, in AttackProfile a, in DefenderProfile d, bool defenderHasShield = false, bool alwaysHit = false)
    {
        int hitChance = HitChance(a, d);
        if (!alwaysHit && !RollHit(rng, hitChance))
            return new HitResult(Hit: false, Blocked: false, Crit: false, Damage: 0, Element: a.Element);

        // Block negates the landed hit (receiver-side) before any damage roll. Only players block: a monster's
        // authored block rating never stops a player's hit.
        if (!d.IsMonster && RollBlock(rng, d, a.AttackerLevel, defenderHasShield))
            return new HitResult(Hit: true, Blocked: true, Crit: false, Damage: 0, Element: a.Element);

        int dmg = RollBaseDamage(rng, a);

        dmg = ApplySkill(dmg, a);
        dmg += PercentOf(dmg, a.ExtraPercent);

        bool crit = RollCrit(rng, a, d);
        if (crit)
            dmg = ApplyCrit(dmg, a.CritBonusPercent);

        if (a.ResistedPart >= 0)
            dmg -= a.ResistedPart - ApplyElement(a.ResistedPart, a.Element, d.Resistances);
        else
            dmg = ApplyElement(dmg, a.Element, d.Resistances);

        dmg = ApplyAbsorbMonster(dmg, d.Absorption);

        return new HitResult(Hit: true, Blocked: false, Crit: crit, Damage: dmg, Element: a.Element);
    }
}
