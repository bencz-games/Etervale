using PristonTale.Contracts.Characters;

namespace PristonTale.Simulation.Characters;

public sealed record CharacterClassDef(
    PlayerClass Class,
    string Name,
    CharacterBrood Brood,
    CharacterVisualSet DefaultVisuals,
    CharacterAttributes BaseAttributes,
    CharacterAttributes GrowthPerLevel,
    CharacterFunctionProfile FunctionProfile)
{
    public uint MotionJobMask => JobMaskOf(Class);

    public static uint JobMaskOf(PlayerClass playerClass) => 1u << ((int)playerClass - 1);

    public JobBitCode JobBitCode => JobBits.Of(Class);

    public ClassProfile Profile => ClassProfiles.Of(Class);

    public CharacterAttributes AttributesAtLevel(int level)
    {
        var levelDelta = Math.Max(0, level - 1);
        return BaseAttributes.Add(GrowthPerLevel.Scale(levelDelta));
    }
}
