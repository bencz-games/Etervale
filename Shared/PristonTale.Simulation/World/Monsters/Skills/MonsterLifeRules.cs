using PristonTale.Simulation.World.Ai;
using PristonTale.Simulation.World.Npcs;

namespace PristonTale.Simulation.World.Monsters.Skills;

// Life a monster wins back: potions it drinks, the virtual lives of four bosses and Babel's regeneration.
public static class MonsterLifeRules
{
    // Below 30 % life a monster with potions drinks one when a clip ends; the drink restores half its life when the eating
    // clip ends.
    public const int PotionThresholdPercent = 30;
    public const int PotionHealDivisor = 2;

    // Valento, Kelvezu, Mokova, Hestian and the castle's standing defenders (door, Tower of Valhalla, crystal towers) carry
    // their potion count as lives: at 10 % life or less one is spent and life refills.
    public const int VirtualLifeThresholdPercent = 10;

    public static bool HasVirtualLives(MonsterDefinition definition) =>
        definition.SoundCode is CharacterSoundCodes.DeathKnight or CharacterSoundCodes.Kelvezu or CharacterSoundCodes.Mokova
            or CharacterSoundCodes.Hestian
        || Social.Castle.CastleSiegeRules.StandsStill(definition.SoundCode);

    // Potions or lives a new monster carries: lives for the monsters that have them, otherwise rand[1, count] potions with the
    // definition's percent chance.
    public static void Prepare(MonsterInstance monster, ISimRng rng)
    {
        ArgumentNullException.ThrowIfNull(monster);
        ArgumentNullException.ThrowIfNull(rng);
        var count = monster.Def.PotionCount;
        if (count <= 0)
        {
            return;
        }

        if (HasVirtualLives(monster.Def))
        {
            monster.VirtualLives = count;
            monster.VirtualLivesMax = count;
            return;
        }

        monster.Potions = rng.RollPercent() < monster.Def.PotionPercent ? rng.NextExclusive(0, count) + 1 : 0;
    }

    public static bool WantsPotion(MonsterInstance monster)
    {
        return monster.Potions > 0 && monster.DuelPlayerId == 0 && monster.LifePercent < PotionThresholdPercent;
    }

    public static int PotionHeal(MonsterDefinition definition) => definition.Combat.MaxLife / PotionHealDivisor;

    // Spends a virtual life when the remaining life falls to 10 % or less; returns true when one was spent.
    public static bool TrySpendVirtualLife(MonsterInstance monster)
    {
        if (monster.VirtualLives <= 0 || monster.LifePercent is not { } percent || percent > VirtualLifeThresholdPercent)
        {
            return false;
        }

        monster.VirtualLives--;
        monster.Life = monster.Def.Combat.MaxLife;
        return true;
    }

    // The life bar of a boss with lives: each life is 100 points of the bar, the current life its percent.
    public static (int Life, int Max) DisplayLife(MonsterInstance monster)
    {
        var life = monster.VirtualLives * 100 + (monster.LifePercent ?? 0);
        if (life <= 0 && monster.Life > 0)
        {
            life = 1;
        }

        var total = (monster.VirtualLivesMax + 1) * 100;
        return (Math.Min(life, total), total);
    }

    // Babel's regeneration: a row per player within 260 units beyond six (at most nine), the second column below 20 % life.
    private static readonly int[,] BabelRegenTable =
    {
        { 1800, 2300 }, { 1900, 2400 }, { 2000, 2550 }, { 2150, 2650 }, { 2300, 2800 },
        { 2400, 2900 }, { 2500, 3000 }, { 2600, 3100 }, { 2700, 3200 }, { 2800, 3300 }
    };

    public const float BabelRegenRadius = 260f;
    public const float BabelRegenHeight = 60f;

    public static int BabelRegen(int nearPlayers, int life, int maxLife)
    {
        var row = nearPlayers > 15 ? 9 : Math.Max(0, nearPlayers - 6);
        var column = life < maxLife / 5 ? 1 : 0;
        return BabelRegenTable[row, column];
    }

    public static bool Regenerates(MonsterDefinition definition) => definition.SoundCode == CharacterSoundCodes.Babel;
}
