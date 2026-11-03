using System.Numerics;
using PristonTale.Simulation.World.Monsters;
using PristonTale.Simulation.World.Npcs;

namespace PristonTale.Simulation.Combat;

public static class MonsterAttackReach
{
    public const float VerticalEntryLimit = 64f;
    public const float BasicImpactDistanceSquared = 48f * 256f;
    public const float RangedImpactDistanceSquared = 460f * 460f;

    public static bool CanBegin(MonsterDefinition monster, Vector3 position, Vector3 target)
    {
        var range = monster.Combat.MeleeRange;
        return range > 0
            && MathF.Abs(position.Y - target.Y) <= VerticalEntryLimit
            && PlanarDistanceSquared(position, target) < (double)range * range;
    }

    public static bool CanLandBasic(MonsterDefinition monster, Vector3 position, Vector3 target)
    {
        // The attack packet carries a reach of 48 in 8.8 fixed point (48 * 256), and the client compares it with a
        // squared distance. Preserve that effective reach, rather than treating 48 as a radius or adding a guessed pad.
        var limit = HasRangedBasicAttack(monster.SoundCode) ? RangedImpactDistanceSquared : BasicImpactDistanceSquared;
        return MathF.Abs(position.Y - target.Y) <= VerticalEntryLimit
            && PlanarDistanceSquared(position, target) < limit;
    }

    private static double PlanarDistanceSquared(Vector3 left, Vector3 right)
    {
        var x = (double)left.X - right.X;
        var z = (double)left.Z - right.Z;
        return x * x + z * z;
    }

    // Sound families whose basic attack plays as ranged.
    public static bool HasRangedBasicAttack(int soundCode) => soundCode switch
    {
        CharacterSoundCodes.Imp => true,
        CharacterSoundCodes.Scorpion => true,
        CharacterSoundCodes.Web => true,
        CharacterSoundCodes.Figon => true,
        CharacterSoundCodes.SkeletonRanger => true,
        CharacterSoundCodes.Beedog => true,
        CharacterSoundCodes.MutantTree => true,
        CharacterSoundCodes.Naz => true,
        CharacterSoundCodes.Succubus => true,
        CharacterSoundCodes.Shadow => true,
        CharacterSoundCodes.Berserker => true,
        CharacterSoundCodes.StygianLord => true,
        CharacterSoundCodes.DMachine => true,
        CharacterSoundCodes.HyperMachine => true,
        CharacterSoundCodes.RunicGuardian => true,
        CharacterSoundCodes.Sadness => true,
        CharacterSoundCodes.AvelinArcher => true,
        CharacterSoundCodes.AvelinQueen => true,
        CharacterSoundCodes.Babel => true,
        CharacterSoundCodes.ColdEye => true,
        CharacterSoundCodes.Frozen => true,
        CharacterSoundCodes.ChaosCara => true,
        CharacterSoundCodes.MLord => true,
        CharacterSoundCodes.TurtleCannon => true,
        CharacterSoundCodes.Kelvezu => true,
        CharacterSoundCodes.FireWorm => true,
        CharacterSoundCodes.DarkGuard => true,
        CharacterSoundCodes.DarkMage => true,
        CharacterSoundCodes.Seto => true,
        CharacterSoundCodes.GoblinShaman => true,
        CharacterSoundCodes.SkillMaster => true,
        CharacterSoundCodes.MagicMaster => true,
        CharacterSoundCodes.NazSenior => true,
        CharacterSoundCodes.CastleCrystalFire or CharacterSoundCodes.CastleCrystalLightning or CharacterSoundCodes.CastleCrystalIce => true,
        _ => false
    };
}
