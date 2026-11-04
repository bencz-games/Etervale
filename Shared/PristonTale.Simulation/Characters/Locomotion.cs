using PristonTale.Contracts.Units;

namespace PristonTale.Simulation.Characters;

public static class Locomotion
{
    private const int WalkFieldFactor = 180; // >>8  ≈ 0.703x outside villages
    private const int RunFactor = 460;       // >>8  ≈ 1.797x

    // The original's walk steps by a level of the move speed held to StepLimit; a lifted limit (Talaria) steps by the
    // move speed itself, one level above the held one.
    public const int StepLimit = 9;

    public static int StepLevel(int moveSpeed, bool unbound) => unbound ? moveSpeed + 1 : Math.Min(moveSpeed, StepLimit);

    public static int BaseTranslation(int stepLevel)
    {
        int level = Math.Max(stepLevel, 1);
        return 250 + (10 * (level - 1));
    }

    public static float WalkUnitsPerSecond(int moveSpeedStat, bool inVillage)
    {
        int baseTranslation = BaseTranslation(moveSpeedStat);
        int perTick = inVillage ? baseTranslation : (baseTranslation * WalkFieldFactor) >> GameUnits.FixedShift;
        return PerTickToUnitsPerSecond(perTick);
    }

    // A game master running with Shift held moves 3000/256 units per original tick.
    public const int GameMasterDashPerTick = 3000;

    public static float GameMasterDashUnitsPerSecond => PerTickToUnitsPerSecond(GameMasterDashPerTick);

    public static float RunUnitsPerSecond(int moveSpeedStat) => PerTickToUnitsPerSecond(RunTranslation(moveSpeedStat));

    public static int RunTranslation(int moveSpeedStat) => (BaseTranslation(moveSpeedStat) * RunFactor) >> GameUnits.FixedShift;

    // The walk or run clip's playback rate for the move speed (the frame step follows the per-tick translation).
    public static float StepRate(int moveSpeedStat, bool running) =>
        AnimationRate.MoveRate(running ? RunTranslation(moveSpeedStat) : BaseTranslation(moveSpeedStat));

    private static float PerTickToUnitsPerSecond(int perTick)
        => perTick / GameUnits.FixedScale * GameUnits.TicksPerSecond;
}
