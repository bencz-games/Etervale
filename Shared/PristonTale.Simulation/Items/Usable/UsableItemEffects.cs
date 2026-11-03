using PristonTale.Simulation.Characters;
using PristonTale.Simulation.Progression;
using PristonTale.Simulation.Skills;

namespace PristonTale.Simulation.Items.Usable;

// The part of an item use that changes the character's own saved state: premium timers, stat and skill resets,
// skill mastery, aging mastery, the Gravity Scroll and the head looks. The authority checks it on a copy, stores the copy, then applies
// it again to the live character with the same clock, so both reach the same state.
public static class UsableItemEffects
{
    // Seconds the big-head potion lasts.
    public const long BigHeadSeconds = 600;

    public static ItemUseStatus Check(PlayerCharacter character, UsableItemRule rule, long nowSeconds)
    {
        switch (rule.Action)
        {
            case UsableItemAction.PremiumTimers:
                return Start(character.Premium, rule, nowSeconds, dryRun: true);
            case UsableItemAction.ResetAttributes:
                return ResetTarget(character, rule.Argument) is null ? ItemUseStatus.NothingToChange : ItemUseStatus.Used;
            case UsableItemAction.ResetSkills:
                return character.Skills.Ranks.Count == 0 ? ItemUseStatus.NothingToChange : ItemUseStatus.Used;
            case UsableItemAction.ResetAttributesAndSkills:
                return ResetTarget(character, 0) is null && character.Skills.Ranks.Count == 0 ? ItemUseStatus.NothingToChange : ItemUseStatus.Used;
            case UsableItemAction.SkillMastery:
                return MasterySkills(character, rule.Argument).Any() ? ItemUseStatus.Used : ItemUseStatus.NothingToChange;
            case UsableItemAction.AgingMastery:
                return AgingTarget(character, rule.Argument) is null ? ItemUseStatus.NothingToChange : ItemUseStatus.Used;
            case UsableItemAction.GravityScroll:
                return character.Premium.GravityScrolls >= UsableItemCatalog.MaxGravityScrolls ? ItemUseStatus.NothingToChange : ItemUseStatus.Used;
            case UsableItemAction.BigHead:
                return character.Premium.BigHeadMode != 0 && character.Premium.BigHeadExpiresAtSeconds > nowSeconds ? ItemUseStatus.AlreadyRunning : ItemUseStatus.Used;
            default:
                return ItemUseStatus.Used;
        }
    }

    // Applies the character part; argument is the rolled big-head mode (1 or 2) for the big-head potion.
    public static void Apply(PlayerCharacter character, UsableItemRule rule, long nowSeconds, int argument = 0)
    {
        switch (rule.Action)
        {
            case UsableItemAction.PremiumTimers:
                Start(character.Premium, rule, nowSeconds, dryRun: false);
                break;
            case UsableItemAction.ResetAttributes:
                ResetAttributes(character, rule.Argument);
                break;
            case UsableItemAction.ResetSkills:
                character.ResetLearnedSkills();
                break;
            case UsableItemAction.ResetAttributesAndSkills:
                ResetAttributes(character, 0);
                character.ResetLearnedSkills();
                break;
            case UsableItemAction.SkillMastery:
                foreach (var skill in MasterySkills(character, rule.Argument).ToArray())
                {
                    character.Skills.RecordUses(skill.Code, character.Skills.UsesOf(skill.Code) + LearnedSkills.MaxUses);
                }

                break;
            case UsableItemAction.AgingMastery:
                if (AgingTarget(character, rule.Argument) is { } item)
                {
                    Aging.AgingRules.Count(item, item.Maturity.Goal);
                    character.TouchInventory();
                }

                break;
            case UsableItemAction.GravityScroll:
                character.Premium.TryAddGravityScroll(UsableItemCatalog.MaxGravityScrolls);
                break;
            case UsableItemAction.BigHead:
                character.Premium.TryStartBigHead(argument, BigHeadSeconds, nowSeconds);
                break;
            case UsableItemAction.SoccerHead:
                character.Premium.TryStartBigHead(PremiumTimers.SoccerHeadMode, EventFoods.SoccerHeadSeconds, nowSeconds);
                break;
        }

        if (character.Life > character.MaxLife)
        {
            character.Life = character.MaxLife;
        }
    }

    private static ItemUseStatus Start(PremiumTimers timers, UsableItemRule rule, long nowSeconds, bool dryRun)
    {
        var first = rule.Grants[0];
        var status = timers.CanStart(first.Kind, rule.Conflict, nowSeconds) switch
        {
            PremiumStartResult.AlreadyRunning => ItemUseStatus.AlreadyRunning,
            PremiumStartResult.SameAttribute => ItemUseStatus.SameAttribute,
            PremiumStartResult.Full => ItemUseStatus.AlreadyRunning,
            _ => ItemUseStatus.Used
        };
        if (dryRun || status != ItemUseStatus.Used)
        {
            return status;
        }

        foreach (var grant in rule.Grants)
        {
            timers.Start(grant.Kind, grant.Value, grant.DurationSeconds, nowSeconds);
        }

        return status;
    }

    // The attributes after a reset (attribute 0 all five, 1..5 one of them) or null when nothing would change.
    private static CharacterAttributes ResetTarget(PlayerCharacter character, int attribute)
    {
        var start = character.Definition.AttributesAtLevel(1);
        var now = character.Attributes;
        var target = attribute switch
        {
            1 => now with { Strength = start.Strength },
            2 => now with { Spirit = start.Spirit },
            3 => now with { Talent = start.Talent },
            4 => now with { Agility = start.Agility },
            5 => now with { Health = start.Health },
            _ => start
        };
        return target == now || target.Total > now.Total ? null : target;
    }

    private static void ResetAttributes(PlayerCharacter character, int attribute)
    {
        if (ResetTarget(character, attribute) is { } target)
        {
            character.RedistributeAttributes(target, (int)(character.StatPoints + character.Attributes.Total - target.Total));
        }
    }

    // The learned skills of a class tier (1..3: the tier's window of SkillsPerTier skills) that are not fully trained.
    private static IEnumerable<Skill> MasterySkills(PlayerCharacter character, int tier)
    {
        var skills = character.Class.Skills;
        var end = SkillLearning.OpenedBy(tier - 1);
        for (var index = end - SkillLearning.SkillsPerTier; index < end && index < skills.Count; index++)
        {
            var skill = skills[index];
            if (skill.Code != 0 && character.Skills.RankOf(skill.Code) > 0 && character.Skills.UsesOf(skill.Code) < LearnedSkills.MaxUses)
            {
                yield return skill;
            }
        }
    }

    // The aging an Aging Master finishes on the worn item: 0 weapon, 1 shield or orb, 2 armor or robe, 3 armlet, 4 gloves,
    // 5 boots. Only an item whose aging is under way matures.
    private static ItemInstance AgingTarget(PlayerCharacter character, int group)
    {
        var slot = group switch
        {
            0 => EquipmentSlot.Weapon,
            1 => EquipmentSlot.OffHand,
            2 => EquipmentSlot.Armor,
            3 => EquipmentSlot.Bracelet,
            4 => EquipmentSlot.Gloves,
            _ => EquipmentSlot.Boots
        };
        var item = character.GetEquipped(slot);
        if (item?.Maturity is not { Goal: > 0 })
        {
            return null;
        }

        return group == 1 && item.Definition.Kind is not (ItemKind.Shield or ItemKind.Orb) ? null : item;
    }

}
