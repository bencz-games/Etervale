namespace PristonTale.Simulation.Items.Usable;

// The keep icons and names of item effects in the running-effects bar (the client words their effect lines): scrolls, force orbs, castle items,
// crystal pets, and the boosters and skill-delay item, which are premium timers drawn in that bar under their own codes.
public static class ItemStatuses
{
    public const uint PremiumKeepBase = 0xF0000110;


    // A crystal pet shows its crystal's inventory sprite, every crystal's lying in the Event folder.
    private const string CrystalIcons = "image/Sinimage/Items/Event/it";

    public static uint PremiumKeepCode(PremiumEffectKind kind) => PremiumKeepBase + (uint)kind;

    // A premium timer's name, as the timer column and the game master's replies show it; the boosters and the skill
    // delay item go by their keep icon's name.
    public static string PremiumName(PremiumEffectKind kind) => kind switch
    {
        PremiumEffectKind.ThirdEyes => "Third Eyes",
        PremiumEffectKind.ExperienceUp => "EXP Increase Potion",
        PremiumEffectKind.VampiricCuspid => "Vampiric Cuspid",
        PremiumEffectKind.ManaRecharging => "Mana Recharging Potion",
        PremiumEffectKind.MightOfAwell => "Might of Awell",
        PremiumEffectKind.ManaReduce => "Mana Reduce Potion",
        PremiumEffectKind.PhoenixPet => "Phoenix",
        PremiumEffectKind.HelpPetTerry => "Terry",
        PremiumEffectKind.HelpPetNepsys => "Nepsys",
        PremiumEffectKind.HelpPetIo => "Io",
        PremiumEffectKind.HelpPetMute => "Mute",
        PremiumEffectKind.VampiricCuspidEx => "Super Vampiric Cuspid",
        PremiumEffectKind.StaminaReduce => "Stamina Reduce Potion",
        _ => Name(PremiumKeepCode(kind)) ?? ""
    };

    public static bool TryGetPremiumKeep(uint code, out PremiumEffectKind kind)
    {
        kind = (PremiumEffectKind)(code - PremiumKeepBase);
        return code > PremiumKeepBase && code <= PremiumKeepBase + 0x40 && Enum.IsDefined(kind) && PremiumTimers.PausesOffline(kind);
    }

    public static string Icon(uint code)
    {
        if (ItemScrolls.TryGetByStatus(code, out var scroll))
        {
            return scroll.KeepIcon;
        }

        if (ForceOrbs.TryGetByStatus(code, out var orb))
        {
            return orb.Icon;
        }

        if (CastleItems.TryGetByStatus(code, out var castle))
        {
            return castle.KeepIcon;
        }

        if (Social.Castle.CastleClanSkills.TryGetByStatus(code, out var clanSkill))
        {
            return Social.Castle.CastleClanSkills.Icon(clanSkill);
        }

        if (MonsterCrystals.TryGetByPetStatus(code, out var crystal))
        {
            return CrystalIcons + crystal.CatalogKey + ".bmp";
        }

        return TryGetPremiumKeep(code, out var kind) ? KeepIcons.Folder + kind switch
        {
            PremiumEffectKind.LifeBooster => "Booster_01.tga",
            PremiumEffectKind.ManaBooster => "Booster_02.tga",
            PremiumEffectKind.StaminaBooster => "Booster_03.tga",
            _ => "Booster_04.tga"
        } : null;
    }

    public static string Name(uint code)
    {
        if (ItemScrolls.TryGetByStatus(code, out var scroll))
        {
            return scroll.Name;
        }

        if (ForceOrbs.TryGetByStatus(code, out var orb))
        {
            return orb.Name;
        }

        if (CastleItems.TryGetByStatus(code, out var castle))
        {
            return castle.Name;
        }

        if (Social.Castle.CastleClanSkills.TryGetByStatus(code, out var clanSkill))
        {
            return Social.Castle.CastleClanSkills.Name(clanSkill);
        }

        if (MonsterCrystals.TryGetByPetStatus(code, out var crystal))
        {
            return crystal.Name + " Crystal";
        }

        return TryGetPremiumKeep(code, out var kind) ? kind switch
        {
            PremiumEffectKind.LifeBooster => "Life Booster",
            PremiumEffectKind.ManaBooster => "Mana Booster",
            PremiumEffectKind.StaminaBooster => "Stamina Booster",
            _ => "Skill Delay"
        } : null;
    }

}
