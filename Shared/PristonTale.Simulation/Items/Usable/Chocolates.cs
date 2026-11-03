using PristonTale.Simulation.Characters;
using PristonTale.Simulation.Modifiers;
using PristonTale.Simulation.Skills;
using PristonTale.Contracts.Characters;

namespace PristonTale.Simulation.Items.Usable;

// A chocolate or candy grants the Priestess's Virtual Life at a fixed level: its life share, damage and heal reduction
// and duration are the skill's at that rank. Each item is a gift for one gender: the other gender picks it up.
public sealed record ChocolateDefinition(string CatalogKey, string Name, bool ForFemales, int Level);

public static class Chocolates
{
    public const uint VirtualLifeCode = 0x07040004;

    public static IReadOnlyList<ChocolateDefinition> All { get; } =
    [
        new("CH101", "Black Chocolate", true, 10),
        new("CH102", "White Chocolate", false, 10),
        new("CH103", "Heart Candy", true, 4),
        new("CH104", "Stick Candy", true, 8)
    ];

    public static bool TryGet(string catalogKey, out ChocolateDefinition chocolate)
    {
        chocolate = All.FirstOrDefault(candidate => string.Equals(candidate.CatalogKey, catalogKey, StringComparison.OrdinalIgnoreCase));
        return chocolate is not null;
    }

    public static bool IsFemale(PlayerClass playerClass) => ClassProfiles.Of(playerClass).Female;

    // Used, refused for the other gender (MaleCannotUse or FemaleCannotUse), or AlreadyRunning while Virtual Life runs.
    public static ItemUseStatus Check(ChocolateDefinition chocolate, PlayerCharacter character)
    {
        var female = IsFemale(character.Class.Class);
        if (chocolate.ForFemales != female)
        {
            return female ? ItemUseStatus.FemaleCannotUse : ItemUseStatus.MaleCannotUse;
        }

        return character.Effects.TryGet(VirtualLifeCode, out _) ? ItemUseStatus.AlreadyRunning : ItemUseStatus.Used;
    }

    public static float DurationMs(int level) =>
        SkillRuleCatalog.TryGet(VirtualLifeCode, out var rule) ? SkillFormulas.At(rule.Buff.DurationTenths, level) * 100f : 0f;

    public static EffectSpec Spec(int level)
    {
        var modifiers = new List<StatModifier>();
        if (SkillRuleCatalog.TryGet(VirtualLifeCode, out var rule))
        {
            foreach (var modifier in rule.Buff.Modifiers)
            {
                modifiers.Add(new StatModifier(modifier.Stat, modifier.Op, SkillFormulas.At(modifier.Values, level)));
            }
        }

        return new EffectSpec(VirtualLifeCode, EffectSourceKind.PlayerSkill, false, modifiers.ToArray());
    }
}
