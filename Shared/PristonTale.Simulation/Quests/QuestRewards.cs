using PristonTale.Simulation.Characters;
using PristonTale.Simulation.Modifiers;

namespace PristonTale.Simulation.Quests;

// Lasting rewards of finished quests and the effects running quests put on their holder.
public static class QuestRewards
{
    public const int LifeAfterSecondRankUp = 15;
    public const int LifeAfterLevel90 = 40;
    public const int CursedDamagePercent = -30;

    // Stat points each level-up adds on top of the regular five once the bonus chains are finished: two more from level
    // 80 after the level 80 chain, three more from level 90 when the level 90 chain is finished as well.
    public static int LevelUpBonusStatPoints(int newLevel, LevelQuestLog log)
    {
        if ((log & LevelQuestLog.Level80Bonus) == 0)
        {
            return 0;
        }

        var bonus = newLevel >= 80 ? 2 : 0;
        if ((log & LevelQuestLog.Level90Bonus) != 0 && newLevel >= 90)
        {
            bonus += 3;
        }

        return bonus;
    }

    // The points a finished bonus chain pays at once for the levels already reached.
    public static int BonusChainBackPay(int level, QuestCode chain) => chain switch
    {
        QuestCode.Level80Bonus when level >= 80 => (level - 79) * 2,
        QuestCode.Level90Bonus when level >= 90 => (level - 89) * 3,
        _ => 0
    };

    // Finishing the level 85 quest lowers the experience a death costs from level 85 on.
    public static bool ReducedDeathPenalty(PlayerCharacter character) =>
        (character.Quests.LevelLog & LevelQuestLog.Level85) != 0 && character.Level >= 85;

    public static bool SecondRankUpLife(PlayerCharacter character) =>
        character.Quests.Active.Code != QuestCode.RankUpQuestWeapon && character.Tier >= 2 && character.Level >= 40;

    public static bool Level90Life(PlayerCharacter character) =>
        (character.Quests.LevelLog & LevelQuestLog.Level90) != 0 && character.Level >= 90;

    // The level 90 bonus chain curses its hunter: thirty percent less attack damage from the hunt until the chain is
    // finished.
    public static bool Cursed(PlayerCharacter character) =>
        character.Quests.Active is { Code: QuestCode.Level90Bonus, State: >= 2 };
}

// Keeps the quest effects of a character in line with its journal, level, tier and equipped quest weapon. They are
// permanent system effects; an unchanged effect is left alone so the container revision moves only on real changes.
public static class QuestEffects
{
    public const uint SecondRankUpLifeCode = 0x51000001;
    public const uint Level90LifeCode = 0x51000002;
    public const uint CurseCode = 0x51000003;
    public const uint QuestWeaponCode = 0x51000004;

    public static void Sync(PlayerCharacter character)
    {
        Set(character, SecondRankUpLifeCode, QuestRewards.SecondRankUpLife(character), [StatModifier.Flat(StatKind.MaxLife, QuestRewards.LifeAfterSecondRankUp)]);
        Set(character, Level90LifeCode, QuestRewards.Level90Life(character), [StatModifier.Flat(StatKind.MaxLife, QuestRewards.LifeAfterLevel90)]);
        Set(character, CurseCode, QuestRewards.Cursed(character),
            [StatModifier.Percent(StatKind.DamageMin, QuestRewards.CursedDamagePercent), StatModifier.Percent(StatKind.DamageMax, QuestRewards.CursedDamagePercent)]);

        var active = character.Quests.Active;
        var weapon = character.GetEquipped(EquipmentSlot.Weapon);
        var wielded = active.Code == QuestCode.RankUpQuestWeapon && active.WeaponId != Guid.Empty && weapon?.InstanceId == active.WeaponId;
        var modifiers = new List<StatModifier>(3);
        if (wielded && active.WeaponLifeBonus != 0)
        {
            modifiers.Add(StatModifier.Flat(StatKind.MaxLife, active.WeaponLifeBonus));
        }

        if (wielded && active.WeaponManaRegenTenths != 0)
        {
            modifiers.Add(StatModifier.Flat(StatKind.ManaRegen, active.WeaponManaRegenTenths));
        }

        if (wielded && character.Quests.WeaponSlowed)
        {
            modifiers.Add(StatModifier.Flat(StatKind.AttackSpeed, -2));
        }

        Set(character, QuestWeaponCode, modifiers.Count > 0, modifiers.ToArray());
    }

    private static void Set(PlayerCharacter character, uint code, bool active, StatModifier[] modifiers)
    {
        var hasEffect = character.Effects.TryGet(code, out var current);
        if (!active)
        {
            if (hasEffect)
            {
                character.Effects.Remove(code, 0, null);
            }

            return;
        }

        if (hasEffect && current.Permanent && current.Spec.Modifiers.SequenceEqual(modifiers))
        {
            return;
        }

        character.Effects.Apply(new EffectSpec(code, EffectSourceKind.System, CurseCode == code, modifiers), 0, float.PositiveInfinity);
    }
}
