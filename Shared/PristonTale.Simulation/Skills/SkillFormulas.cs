using PristonTale.Simulation.Characters;
using PristonTale.Simulation.Combat;
using PristonTale.Simulation.Items;
using PristonTale.Simulation.Items.Usable;
using PristonTale.Simulation.Modifiers;
using PristonTale.Simulation.World.Monsters;
using PristonTale.Simulation.World.Motion;
using PristonTale.Contracts.Skills;
using PristonTale.Simulation.Progression;

namespace PristonTale.Simulation.Skills;

public readonly record struct SkillTiming(float DurationMs, float[] HitsAtMs);

// A held charge's timeline (see SkillFormulas.ChargeTiming).
public sealed record SkillChargeTiming(float StartMs, float FullMs, float FullDurationMs, float[] FullHitsAtMs);

// The clip a skill rank plays: kind, index in the class motion table, length and strike frames, fixed frame step (0
// follows the attack speed).
public readonly record struct SkillMotionPlan(SkillMotion Motion, int MotionIndex, int Frames, int[] Events, int Speed);

// The shared skill formulas: cost, cooldown, cast checks, timing and damage. The server resolves with them; the
// client uses the same functions for tooltips and to predict whether a cast will be accepted.
public static partial class SkillFormulas
{
    // The original gauge advances once per 14 ms tick.
    public const int CooldownTickMs = 14;
    private const int Gauge = 35;
    public const float FrameStepAtRateOne = 80f;

    // Proficiency = min(talent / 3 + item mastery, 50) x 100 + uses (100 per completed use streak), at most LearnedSkills.MaxUses.
    // Mastery units M = base + perRank x rank - proficiency / 100, clamped to 1..70. The 35-step gauge then fills one
    // step every max(1, 35 / floor(70 / M)) ticks: from 0.49 s (M 1-3) to 17.15 s (M 36-70).
    public static int CooldownMs(SkillRule rule, int rank, int talent, int itemMastery, int uses)
    {
        if (rule.MasteryBase == 0 && rule.MasteryPerRank == 0)
        {
            return 0;
        }

        // Fourth- and fifth-tier skills have no proficiency to train: they always count as fully trained.
        var proficiency = IsFullyTrained(rule.Code) ? LearnedSkills.MaxUses : Math.Min(LearnedSkills.MaxUses, Math.Min(talent / 3 + itemMastery, 50) * 100 + Math.Max(0, uses));
        var mastery = Math.Clamp(rule.MasteryBase + rule.MasteryPerRank * rank - proficiency / 100, 1, 70);
        var step = Math.Max(1, 70 / mastery);
        var ticksPerStep = Math.Max(1, Gauge / step);
        return Gauge * ticksPerStep * CooldownTickMs;
    }

    public static bool IsFullyTrained(uint code) => (code >> 16 & 0xFF) is (uint)JobTier.Job4 or (uint)JobTier.Job5;

    // Casts per proficiency step by skill-window position; higher ranks need one more cast every three ranks.
    private static ReadOnlySpan<int> CastsPerStep => [5, 5, 5, 5, 7, 7, 7, 7, 9, 9, 9, 9, 14, 15, 16, 17, 18, 19, 20, 21];

    // Every completed streak of casts adds 100 to the skill's proficiency (one mastery unit), as the original use counter.
    public static int UsesAfterCast(int uses, int casts, int windowIndex, int rank)
    {
        var step = CastsPerStep[Math.Clamp(windowIndex, 0, CastsPerStep.Length - 1)] + (Math.Clamp(rank, 1, SkillLearning.MaxRank) - 1) / 3;
        return casts > 0 && casts % step == 0 ? Math.Min(LearnedSkills.MaxUses, uses + 100) : uses;
    }

    public static int ManaCost(Skill skill, int rank, ModifierTotals modifiers)
    {
        var cost = skill.ManaForRank(rank);
        var percent = modifiers.Percent(StatKind.SkillManaCost);
        return Math.Max(0, cost + cost * percent / 100 + modifiers.Flat(StatKind.SkillManaCost));
    }

    public static int StaminaCost(Skill skill, int rank, PremiumTimers premium = null) =>
        premium is null ? skill.StaminaForRank(rank) : PremiumRules.StaminaCost(skill.StaminaForRank(rank), premium);

    // Checks that need no world state: learned, castable, control states, weapon and resources.
    public static SkillCastVerdict CheckCaster(PlayerCharacter caster, Skill skill, SkillRule rule, out int rank)
    {
        rank = caster.Skills.RankOf(skill.Code);
        if (rank <= 0 || caster.Level < skill.RequiredLevel)
        {
            return SkillCastVerdict.NotLearned;
        }

        if (rule.Kind == SkillKind.Passive || skill.MouseUse == SkillMouseUse.None)
        {
            return SkillCastVerdict.NotCastable;
        }

        if (caster.Effects.Has(ControlFlags.Stun | ControlFlags.Silence))
        {
            return SkillCastVerdict.Busy;
        }

        // Support skills cast on another player check that player's effects instead (when the target is resolved).
        if (rule.Target != SkillTarget.Ally && BlockedByActiveEffect(caster, rule))
        {
            return SkillCastVerdict.AlreadyActive;
        }

        if (!HoldsRequiredEffect(caster, rule))
        {
            return SkillCastVerdict.NotCastable;
        }

        if (skill.AllowedWeaponsMask != 0 && !WeaponAllowed(caster, skill))
        {
            return SkillCastVerdict.WeaponNotAllowed;
        }

        if (caster.Mana < ManaCost(skill, rank, caster.Effects.Totals))
        {
            return SkillCastVerdict.NotEnoughMana;
        }

        return caster.Stamina < StaminaCost(skill, rank, caster.Premium) ? SkillCastVerdict.NotEnoughStamina : SkillCastVerdict.Accepted;
    }

    // The skill's weapon list names main-hand kinds and, for shield skills, the off-hand shield.
    public static bool WeaponAllowed(PlayerCharacter caster, Skill skill)
    {
        var weapon = caster.GetEquipped(EquipmentSlot.Weapon);
        if (weapon is not null && skill.AllowsWeapon(weapon.Definition.Kind))
        {
            return true;
        }

        var offHand = caster.GetEquipped(EquipmentSlot.OffHand);
        return offHand is not null && skill.AllowsWeapon(offHand.Definition.Kind);
    }

    // Cast range: the skill's table (at least the weapon's range, or added to it, per RangeMode), or the weapon's attack
    // range r grown by the skill: r + (r - 50) x percent / 100 plus a flat bonus.
    public static float Range(SkillRule rule, int rank, PlayerCharacter caster) => Range(rule, rank, PlayerAttackReach.For(caster));

    // The same for a caster's weapon reach (PlayerAttackReach, which the owner's client receives as its attack range).
    public static float Range(SkillRule rule, int rank, float reach)
    {
        var range = rule.At(rule.Range, rank);
        switch (rule.RangeMode)
        {
            case SkillRangeMode.AtLeastWeapon:
                return Math.Max(range, reach);
            case SkillRangeMode.AddWeapon:
                return reach + range;
        }

        if (range > 0)
        {
            return range;
        }

        return reach + (reach - MeleeReach.PlayerBaseRange) * rule.At(rule.ReachPercent, rank) / 100f + rule.At(rule.ReachBonus, rank);
    }

    // The timeline of a held charge (see SkillChargeRule): the full-charge hits and length, and where the window starts
    // and ends. A release before FullMs moves the rest of the timeline earlier by FullMs - max(release, StartMs).
    public static SkillChargeTiming ChargeTiming(SkillRule rule, int rank)
    {
        var charge = rule.Charge;
        var plan = Plan(rule, rank);
        var chargeRate = Math.Max(1, plan.Speed) / FrameStepAtRateOne;
        var releaseRate = Math.Max(1, charge.ReleaseSpeed) / FrameStepAtRateOne;
        var startMs = MotionTiming.DurationMs(charge.StartFrame, chargeRate);
        var fullMs = MotionTiming.DurationMs(charge.FullFrame, chargeRate);
        var hits = new List<float>();
        foreach (var frame in plan.Events)
        {
            if (frame >= charge.FullFrame && (charge.MaxHits == 0 || hits.Count < charge.MaxHits))
            {
                hits.Add(fullMs + MotionTiming.DurationMs(frame - charge.FullFrame, releaseRate));
            }
        }

        var durationMs = fullMs + MotionTiming.DurationMs(Math.Max(1, plan.Frames - charge.FullFrame), releaseRate);
        return new SkillChargeTiming(startMs, fullMs, durationMs, hits.ToArray());
    }

    // The motion a rank plays: the rule's own, or the variant whose FromRank the rank has reached.
    public static SkillMotionPlan Plan(SkillRule rule, int rank)
    {
        var ranked = rule.ForRank(rank);
        var events = At(rule.HitFramesByRank, rank, null) ?? ranked.HitFrames();
        var plan = new SkillMotionPlan(ranked.Motion, ranked.MotionIndex, ranked.MotionFrames, events, ranked.MotionSpeed);

        var speed = rule.At(rule.MotionSpeedByRank, rank);
        return speed > 0 ? plan with { Speed = speed } : plan;
    }

    // The hits one cast of the rank deals, as Timing places them: one per loop of a weapon clip, every hit frame of a
    // skill clip on every loop (at least one).
    public static int HitCount(SkillRule rule, int rank)
    {
        var plan = Plan(rule, rank);
        var loops = Math.Max(1, rule.At(rule.Loops, rank));
        return plan.Motion == SkillMotion.Attack ? loops : Math.Max(1, plan.Events.Length) * loops;
    }

    // Playback rate of a skill's motion: a fixed frame step (80 = rate 1), or the caster's attack rate plus the skill's
    // attack-speed steps (each step adds 32/256 to the attack speed, 0.125 to the rate).
    public static float MotionRate(SkillRule rule, int rank, float attackRate)
    {
        var plan = Plan(rule, rank);
        if (plan.Speed > 0)
        {
            return plan.Speed / FrameStepAtRateOne;
        }

        var steps = rule.At(rule.AttackSpeedSteps, rank) + rule.MotionSpeedSteps;
        return (Math.Max(0.1f, attackRate) + 0.125f * steps) * rule.MotionRatePercent / 100f * rule.MotionSpeedPercent / 100f;
    }

    // How many times faster than a plain swing a weapon-clip skill plays the caster's attack clip.
    public static float WeaponClipSpeedup(SkillRule rule, int rank, float attackRate) => MotionRate(rule, rank, attackRate) / Math.Max(0.1f, attackRate);

    // The playback rate of a skill clip at rank 1 (see MotionRate).
    public static float SkillClipRate(SkillRule rule, float attackRate) => MotionRate(rule, 1, attackRate);

    // Timing of a skill motion: duration and the offsets of its hits. Weapon-clip skills repeat the attack clip
    // (attackClip: duration and hit offset at the caster's attack rate) Loops times at the skill's motion rate.
    public static SkillTiming Timing(SkillRule rule, int rank, float attackRate, (float DurationMs, float HitAtMs) attackClip)
    {
        var plan = Plan(rule, rank);
        var rate = MotionRate(rule, rank, attackRate);
        if (plan.Motion == SkillMotion.Attack)
        {
            var loops = Math.Max(1, rule.At(rule.Loops, rank));
            var speedup = WeaponClipSpeedup(rule, rank, attackRate);
            var clip = attackClip.DurationMs / speedup;
            var hit = attackClip.HitAtMs / speedup;
            var hits = new float[loops];
            for (var loop = 0; loop < loops; loop++)
            {
                hits[loop] = loop * clip + hit;
            }

            return new SkillTiming(clip * loops, hits);
        }

        // A skill clip may also repeat (Loops), striking at its frames on every pass.
        var clipLoops = Math.Max(1, rule.At(rule.Loops, rank));
        var clipMs = MotionTiming.DurationMs(plan.Frames, rate);
        var events = new float[plan.Events.Length * clipLoops];
        for (var loop = 0; loop < clipLoops; loop++)
        {
            for (var index = 0; index < plan.Events.Length; index++)
            {
                events[loop * plan.Events.Length + index] = loop * clipMs + MotionTiming.DurationMs(plan.Events[index], rate);
            }
        }

        return new SkillTiming(clipMs * clipLoops, events);
    }

    // The attack profile of a weapon skill: the basic attack plus the skill's accuracy, damage and critical bonuses.
    public static AttackProfile WeaponAttack(PlayerCharacter caster, SkillRule rule, int rank, MonsterDefinition target, CombatRng rng, int hit = 0)
    {
        var basic = CombatProfileFactory.ForPlayerBasicAttack(caster);
        var damage = rule.Damage ?? new SkillDamageRule();
        var rating = basic.AttackRating + rule.At(rule.AttackRating, rank) + (rule.AttackRatingPlusLevel ? caster.Level : 0);
        rating += rating * rule.At(rule.AttackRatingPercent, rank) / 100;
        rating += rating * BorrowedRating(caster, rule, hit) / 100;
        var flat = 0;
        // An elemental add (FlatElement) is rolled by the caller, which resists it with its own element.
        if (damage.Min.Length > 0 && damage.FlatElement is null)
        {
            flat += rng.Range(rule.At(damage.Min, rank), rule.At(damage.Max, rank));
        }

        var divisor = LevelDivisor(rule, rank);
        if (divisor > 0)
        {
            flat += caster.Level / divisor;
        }

        var critical = basic.CritChance + rule.At(rule.CriticalAdd, rank);
        critical = critical * (100 + rule.At(rule.CriticalPercent, rank)) / 100 + BorrowedCritical(caster, rule);
        if (damage.NoCritical || rule.NoCriticalHits.Contains(hit + 1))
        {
            critical = 0;
        }

        return basic with
        {
            AttackRating = rating,
            Element = damage.Element,
            CritChance = critical,
            SkillMultiplierPercent = rule.At(damage.Percent, rank),
            SkillFlatAdd = flat + caster.Effects.Totals.Flat(StatKind.HitDamage),
            ExtraPercent = rule.At(damage.PercentAfter, rank),
            ResistedPart = damage.ResistAddedOnly ? flat : -1
        };
    }

    // Damage and accuracy of a normal attack: the basic profile plus the flat damage buffs of single-target hits.
    public static AttackProfile NormalAttack(PlayerCharacter caster)
    {
        var basic = CombatProfileFactory.ForPlayerBasicAttack(caster, normalAttack: true);
        return basic with { SkillFlatAdd = caster.Effects.Totals.Flat(StatKind.HitDamage) };
    }

    private static int BorrowedCritical(PlayerCharacter caster, SkillRule rule)
    {
        var borrowed = rule.BorrowedCritical;
        if (borrowed is null)
        {
            return 0;
        }

        var rank = caster.Skills.RankOf(borrowed.Skill);
        return rank == 0 ? 0 : rule.At(borrowed.Values, rank) / Math.Max(1, borrowed.Divisor);
    }

    private static int BorrowedRating(PlayerCharacter caster, SkillRule rule, int hit)
    {
        var borrowed = rule.BorrowedRating;
        if (borrowed is null || hit + 1 < borrowed.FromHit)
        {
            return 0;
        }

        var rank = caster.Skills.RankOf(borrowed.Skill);
        return rank == 0 ? 0 : rule.At(borrowed.Values, rank);
    }

    // The brood bonus of a weapon skill, applied after the hit like the original's race bonus: the fixed percent, then
    // the per-rank percent.
    public static int WithBroodBonus(int damage, SkillRule rule, MonsterDefinition target, int rank = 0) =>
        WithBroodBonus(damage, rule.Damage, target, rank);

    public static int WithBroodBonus(int damage, SkillDamageRule rule, MonsterDefinition target, int rank)
    {
        if (rule is null || damage <= 0 || !MatchesBrood(rule.BonusBroods, target))
        {
            return damage;
        }

        if (rule.BonusPercent != 0)
        {
            damage += damage * rule.BonusPercent / 100;
        }

        var perRank = At(rule.BonusPercentByRank, rank);
        return perRank == 0 ? damage : damage + damage * perRank / 100;
    }

    // The rolled power of a magic bolt or an area skill, before the target's resistance and absorption.
    public static int RollPower(PlayerCharacter caster, SkillRule rule, int rank, CombatRng rng) => RollPower(caster, rule, rule.Damage, rank, rng);

    // weaponItem: the weapon damage the spell terms use in place of the caster's own (a party echo strikes with the
    // weapon of the skill's caster).
    public static int RollPower(PlayerCharacter caster, SkillRule rule, SkillDamageRule damage, int rank, CombatRng rng, (int Min, int Max)? weaponItem = null)
    {
        var spell = caster.Effects.Totals.Flat(StatKind.SpellDamage);
        var tablePercent = At(damage.TablePercent, rank);
        var low = rule.At(damage.Min, rank);
        var high = rule.At(damage.Max, rank);
        low += low * tablePercent / 100 + spell;
        high += high * tablePercent / 100 + spell;
        if (damage.SpellPower)
        {
            var boost = caster.Effects.Totals.Flat(StatKind.SpellPower);
            low += low * boost / 100;
            high += high * boost / 100;
        }

        if (damage.AttackPower)
        {
            var attack = WeaponDamage.Compute(caster);
            var attackPercent = At(damage.AttackPowerPercent, rank);
            low += attack.Min + attack.Min * attackPercent / 100;
            high += attack.Max + attack.Max * attackPercent / 100;
        }

        var item = weaponItem ?? WeaponItemDamage(caster);
        if (damage.WeaponDivisor > 0)
        {
            var weapon = damage.WeaponFromAttack ? WeaponDamage.Compute(caster) : item;
            low += weapon.Min / damage.WeaponDivisor;
            high += weapon.Max / damage.WeaponDivisor;
        }

        low += item.Min * At(damage.WeaponTimesMin, rank);
        high += item.Max * At(damage.WeaponTimesMax, rank);
        var levelPart = caster.Level * At(damage.LevelPercent, rank) / 100;
        low += levelPart;
        high += levelPart;
        if (damage.StrengthMaxDivisor > 0)
        {
            high += caster.Attributes.Strength / damage.StrengthMaxDivisor;
        }

        var spirit = caster.Attributes.Spirit;
        var spiritMin = At(damage.SpiritMinDivisorByRank, rank, damage.SpiritMinDivisor);
        var spiritMax = At(damage.SpiritMaxDivisorByRank, rank, damage.SpiritMaxDivisor);
        if (spiritMin > 0)
        {
            low += spirit / spiritMin;
        }

        if (spiritMax > 0)
        {
            high += spirit / spiritMax;
        }

        var divisor = LevelDivisor(damage, rank);
        if (divisor > 0)
        {
            if (!damage.LevelOnMax)
            {
                low += caster.Level / divisor;
            }

            high += caster.Level / divisor;
        }

        var power = rng.Range(low, Math.Max(low, high));
        var hits = rule.At(damage.RandomHits, rank);
        if (hits > 0)
        {
            power *= rng.Range(hits / 2 + 1, hits);
        }

        power += power * rule.At(damage.Percent, rank) / 100;
        power += power * rule.At(damage.PercentAfter, rank) / 100;
        return Math.Max(1, power);
    }

    // What a resolved power deals to a monster: brood bonus, element resistance, the monster's absorption in percent and
    // the damage-dealt and damage-taken modifiers.
    public static int PowerAgainst(int power, SkillRule rule, MonsterDefinition target, ModifierTotals attacker, ModifierTotals defender, int rank = 0) =>
        PowerAgainst(power, rule.Damage, target, attacker, defender, rank);

    // Element resistance first (divided by the rule's divisor), then a spell's magic resistance, the brood bonus (the
    // original area order), absorption and the dealt/taken scaling. Resistances run -100 (double damage) to 100 (none).
    public static int PowerAgainst(int power, SkillDamageRule damage, MonsterDefinition target, ModifierTotals attacker, ModifierTotals defender, int rank)
    {
        var resistances = ModifierRules.Resistances(defender, target.Combat.Resistances);
        var value = damage.Unresisted ? power
            : damage.ResistanceDivisor > 1
                ? CombatResolver.ApplyResistance(power, resistances[damage.Element] / damage.ResistanceDivisor)
                : CombatResolver.ApplyElement(power, damage.Element, resistances);
        if (damage.Magic)
        {
            value = CombatResolver.ApplyResistance(value, target.MagicResistance);
        }

        value = WithBroodBonus(value, damage, target, rank);
        value = CombatResolver.ApplyAbsorbMonster(value, target.Combat.Absorption + defender.Flat(StatKind.Absorption));
        return ModifierRules.ScaleDamage(value, attacker, defender);
    }

    // Healing: the table plus the weapon's own damage / 3 and spirit / 8 (minimum) and / 6 (maximum).
    public static int RollHeal(PlayerCharacter caster, SkillRule rule, int rank, CombatRng rng)
    {
        var weapon = WeaponItemDamage(caster);
        var spirit = caster.Attributes.Spirit;
        var low = rule.At(rule.HealMin, rank) + weapon.Min / 3 + spirit / 8;
        var high = rule.At(rule.HealMax, rank) + weapon.Max / 3 + spirit / 6;
        return rng.Range(low, Math.Max(low, high));
    }

    // Life paid by a weapon skill hit: tenths of a percent of max life, never below 1 life left.
    public static int LifeCostPerHit(SkillRule rule, int rank, int maxLife) => rule.At(rule.LifeCost, rank) * maxLife / 1000;

    // The buff's duration on a target; the target's resistance shortens resisted debuffs.
    public static float BuffDurationMs(SkillRule rule, int rank, int resistance) => BuffDurationMs(rule.Buff, rank, resistance);

    public static float BuffDurationMs(SkillBuffRule buff, int rank, int resistance)
    {
        var ms = At(buff.DurationTenths, rank) * TenthMs;
        return buff.ResistedByBionic || buff.ResistedBy is not null ? CombatResolver.Shortened(ms, resistance) : ms;
    }

    // A per-rank table's value at a rank (clamped to the table), or fallback for a table without values.
    public static T At<T>(T[] table, int rank, T fallback) => table is { Length: > 0 } ? table[Math.Clamp(rank, 1, table.Length) - 1] : fallback;

    public static int At(int[] table, int rank) => At(table, rank, 0);

    // The buff's modifiers at this rank; ranged values roll once per cast (see the overload with the ally share).
    public static StatModifier[] BuffModifiers(SkillRule rule, int rank, CombatRng rng) => BuffModifiers(rule.Buff, rank, rng, null);

    public static StatModifier[] BuffModifiers(SkillBuffRule buff, int rank, CombatRng rng, PlayerCharacter caster) =>
        BuffModifiers(buff, rank, rng, caster, false, 100);

    public static StatModifier[] BuffModifiers(SkillRule rule, int rank, CombatRng rng, PlayerCharacter caster, bool onOther) =>
        BuffModifiers(rule.Buff, rank, rng, caster, onOther, rule.Allies?.OthersPercent ?? 100);

    // The buff's modifiers for one holder: the rank's values (rolled once for ranges), the caster's level term, the
    // rank of a related skill for tables that follow it (0 when it is not learned), and the reduced strength on other
    // players.
    public static StatModifier[] BuffModifiers(SkillBuffRule buff, int rank, CombatRng rng, PlayerCharacter caster, bool onOther, int othersPercent)
    {
        var rules = buff.Modifiers;
        var modifiers = new StatModifier[rules.Length];
        for (var index = 0; index < rules.Length; index++)
        {
            var entry = rules[index];
            var tableRank = entry.RankOf != 0 ? caster?.Skills.RankOf(entry.RankOf) ?? 0 : rank;
            var value = tableRank <= 0 ? 0 : At(entry.Values, tableRank);
            if (tableRank > 0 && entry.ValuesMax.Length > 0)
            {
                value = rng.Range(value, Math.Max(value, At(entry.ValuesMax, tableRank)));
            }

            if (entry.LevelDivisor > 0 && caster is not null)
            {
                value += caster.Level / entry.LevelDivisor;
            }

            if (onOther)
            {
                value = value * (entry.OthersPercent >= 0 ? entry.OthersPercent : othersPercent) / 100;
            }

            modifiers[index] = new StatModifier(entry.Stat, entry.Op, value);
        }

        return modifiers;
    }

    private static int LevelDivisor(SkillRule rule, int rank) => LevelDivisor(rule.Damage, rank);

    private static int LevelDivisor(SkillDamageRule damage, int rank) =>
        At(damage.LevelDivisorByRank, rank, damage.LevelDivisor);

}
