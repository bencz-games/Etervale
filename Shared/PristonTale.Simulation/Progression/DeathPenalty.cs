using PristonTale.Simulation.Characters;
using PristonTale.Simulation.Quests;
using PristonTale.Simulation.World;
using PristonTale.Contracts.Characters;

namespace PristonTale.Simulation.Progression;

// What a restart would cost a dead player now: whether the field restart is open after the death's share, whether a
// Resurrection Scroll waives the shares, and the death's share of experience and gold.
public readonly record struct RestartOffer(bool FieldOpen, bool Scroll, ulong ExperienceShare, long GoldShare);

// What dying costs. From level 11 a death takes a share of the level's experience span and of the carried gold (a
// thirty-third, a fiftieth from level 85 after the level 85 quest or wearing the aging ring at +6, a hundredth wearing it
// at +8, nothing in the castle field), and standing up again in
// the field where the player fell takes a hundredth more. A Resurrection Scroll carried at death is used up instead and
// waives both. Experience never drops below the start of the current level.
public static class DeathPenalty
{
    public const int FirstLevel = 11;
    public const int StandardDivisor = 33;
    public const int ReducedDivisor = 50;
    public const int FieldDivisor = 100;
    public const string ResurrectionScroll = "BI104";

    // Gameplay seconds after a restart in which no damage lands.
    public const float RestartGuardMs = 10_000f;

    // The divisor of a death's share; 0 takes nothing.
    public static int Divisor(PlayerCharacter character, FieldState field)
    {
        if (field == FieldState.Castle)
        {
            return 0;
        }

        var ring = AgedRingLevel(character);
        return ring >= 8 ? FieldDivisor : ring >= 6 || QuestRewards.ReducedDeathPenalty(character) ? ReducedDivisor : StandardDivisor;
    }

    public static int AgedRingLevel(PlayerCharacter character)
    {
        var level = 0;
        foreach (var slot in (ReadOnlySpan<EquipmentSlot>)[EquipmentSlot.RingLeft, EquipmentSlot.RingRight])
        {
            if (character.Equipment.TryGetValue(slot, out var ring) && Items.Aging.AgingRules.CounterOf(ring) == Items.Aging.AgingCounter.Time)
            {
                level = Math.Max(level, ring.UpgradeLevel);
            }
        }

        return level;
    }

    // The share of the level's experience span a divisor takes (the last level spans up to the experience ceiling).
    public static ulong ExperienceShare(int level, int divisor)
    {
        if (level < FirstLevel || divisor <= 0 || level > ExperienceTable.MaximumLevel)
        {
            return 0;
        }

        var start = ExperienceTable.ForLevel(level);
        var end = level < ExperienceTable.MaximumLevel ? ExperienceTable.ForLevel(level + 1) : ExperienceTable.Ceiling;
        return (end - start) / (ulong)divisor;
    }

    public static long GoldShare(int level, long money, int divisor) => level < FirstLevel || divisor <= 0 || money <= 0 ? 0 : money / divisor;

    // Standing up in the field where the player fell: never in the castle field; below level 11 only in a forest field;
    // from level 11 only while the experience above the level's start covers the hundredth the restart takes.
    public static bool CanRestartInField(PlayerCharacter character, FieldState field)
    {
        ArgumentNullException.ThrowIfNull(character);
        return CanRestartInField(character.Level, character.Experience, field);
    }

    // The same rule from the owner's level and experience (the client's restart window).
    public static bool CanRestartInField(int level, ulong experience, FieldState field)
    {
        if (field == FieldState.Castle)
        {
            return false;
        }

        if (level < FirstLevel)
        {
            return field == FieldState.Forest;
        }

        var start = ExperienceTable.ForLevel(Math.Clamp(level, 1, ExperienceTable.MaximumLevel));
        return experience >= start && experience - start >= ExperienceShare(level, FieldDivisor);
    }

    // The offer a dead player's restart window shows; RestartTransactions takes exactly these shares.
    public static RestartOffer Offer(PlayerCharacter character, FieldState field)
    {
        ArgumentNullException.ThrowIfNull(character);
        var scroll = ScrollAnchor(character) >= 0;
        var divisor = scroll ? 0 : Divisor(character, field);
        var start = ExperienceTable.ForLevel(Math.Clamp(character.Level, 1, ExperienceTable.MaximumLevel));
        var above = character.Experience > start ? character.Experience - start : 0;
        var experience = Math.Min(ExperienceShare(character.Level, divisor), above);
        return new RestartOffer(CanRestartInField(character.Level, character.Experience - experience, field), scroll, experience,
            GoldShare(character.Level, character.Money, divisor));
    }

    // The inventory anchor of a carried Resurrection Scroll, or -1.
    public static int ScrollAnchor(PlayerCharacter character)
    {
        // Asked every tick for a dead player: the bag is walked by index, without an iterator.
        var inventory = character.Inventory;
        for (var anchor = 0; anchor < inventory.Capacity; anchor++)
        {
            if (inventory[anchor]?.Definition.CatalogKey == ResurrectionScroll)
            {
                return anchor;
            }
        }

        return -1;
    }

    // The town of a class, where a new character starts and a player stands up: Ricarten for the Tempskron classes,
    // Pillai for the Morion classes.
    public static int TownField(PlayerClass playerClass) =>
        ClassProfiles.Of(playerClass).Brood == CharacterBrood.Tempskron ? FieldCodes.Ricarten : FieldCodes.Pillai;
}
