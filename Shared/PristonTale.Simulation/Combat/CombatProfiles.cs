using PristonTale.Simulation.Characters;
using PristonTale.Simulation.Items;
using PristonTale.Simulation.World.Monsters;

namespace PristonTale.Simulation.Combat;

public readonly record struct AttackProfile(
    int AttackRating,
    int DamageMin,
    int DamageMax,
    int AttackerLevel,
    Element Element,
    int CritChance,
    int SkillMultiplierPercent,
    int SkillFlatAdd)
{
    public const int CritMultiplierPercent = 170;

    // Points added to the critical multiplier (a buff of the attacker).
    public int CritBonusPercent { get; init; }

    // A further percent after the skill percent (a stealth's next-hit bonus, a second skill step).
    public int ExtraPercent { get; init; }

    // When not negative, the element resistance reduces only this part of the hit (a bonus added to a weapon hit).
    public int ResistedPart { get; init; } = -1;
}

public readonly record struct DefenderProfile(
    int Defence,
    int Absorption,
    int BlockChance,
    int Level,
    ResistanceBlock Resistances,
    int MaxLife,
    int CurrentLife,
    bool IsMonster)
{
    // Critical chance an attacker with any critical chance gains against this defender (a marked target).
    public int CritChanceTaken { get; init; }

    public static DefenderProfile FromMonster(MonsterInstance monster) =>
        FromMonster(monster.Def.Combat, monster.Life) with { CritChanceTaken = monster.Effects.Totals.Flat(Modifiers.StatKind.CriticalChanceTaken) };

    public static DefenderProfile FromMonster(MonsterCombatStats stats, int currentLife) =>
        new(
            Defence: stats.Defence,
            Absorption: stats.Absorption,
            BlockChance: stats.BlockChance,
            Level: stats.Level,
            Resistances: stats.Resistances,
            MaxLife: stats.MaxLife,
            CurrentLife: currentLife,
            IsMonster: true);
}
