using PristonTale.Simulation.Items;
using PristonTale.Simulation.Progression;

namespace PristonTale.Simulation.Skills;

[Flags]
public enum SkillMouseUse
{
    None = 0,
    Left = 1,
    Right = 2,
    Both = Left | Right
}

public enum JobTier
{
    Job1 = 0x1,
    Job2 = 0x2,
    Job3 = 0x4,
    Job4 = 0x8,
    Job5 = 0x10
}

public enum SkillCategory
{
    ActiveAttack,
    AreaAttack,
    MultiHit,
    Buff,
    Passive,
    Heal,
    Summon,
    Debuff
}

public sealed class Skill
{
    public uint Code { get; init; }
    public string Description { get; init; } = "";
    public string IconKey { get; init; } = "";
    public SkillMouseUse MouseUse { get; init; }
    public IReadOnlyList<int> ManaByRank { get; init; } = Array.Empty<int>();
    public int StaminaBase { get; init; }
    public int StaminaPerRank { get; init; }
    public uint AllowedWeaponsMask { get; init; }
    public required string Name { get; init; }
    public required JobTier Tier { get; init; }
    public required SkillCategory Category { get; init; }

    public int RequiredLevel { get; init; }

    public int ManaForRank(int rank)
    {
        ValidateRank(rank);
        return ManaByRank[rank - 1];
    }

    public int StaminaForRank(int rank)
    {
        ValidateRank(rank);
        return StaminaBase + StaminaPerRank * (rank - 1);
    }

    public bool AllowsWeapon(ItemKind kind)
    {
        return Enum.IsDefined(kind) && (AllowedWeaponsMask == 0 || (AllowedWeaponsMask & (1u << (int)kind)) != 0);
    }

    private void ValidateRank(int rank)
    {
        if (rank is < 1 or > SkillLearning.MaxRank || ManaByRank.Count != SkillLearning.MaxRank)
        {
            throw new ArgumentOutOfRangeException(nameof(rank), $"A skill rank must be between 1 and {SkillLearning.MaxRank}.");
        }
    }
}
