using PristonTale.Simulation.Characters;

namespace PristonTale.Simulation.Items;

// Whether a character can use an item copy: level and the five attribute requirements of the copy, and the class and
// body restrictions of its kind. An equipped item that stops meeting them stays worn but adds nothing.
public static class ItemUsability
{
    public static bool MeetsRequirements(PlayerCharacter character, ItemInstance item) =>
        MeetsRequirements(item.Roll, character.Level, character.Attributes);

    public static bool MeetsRequirements(ItemRoll roll, int level, CharacterAttributes attributes) =>
        level >= roll.RequiredLevel && attributes.Meets(roll.Requirements);

    public static bool ClassAllowed(PlayerCharacter character, ItemDefinition definition) =>
        definition.AllowedJobs == 0 || (definition.AllowedJobs & (uint)character.Definition.JobBitCode) != 0;

    public static bool CanUse(PlayerCharacter character, ItemInstance item) =>
        MeetsRequirements(character, item) && ClassAllowed(character, item.Definition);
}
