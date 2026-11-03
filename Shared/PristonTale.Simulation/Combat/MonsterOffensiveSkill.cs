using System.Numerics;
using PristonTale.Simulation.World.Monsters;
using PristonTale.Simulation.Physics;
using PristonTale.Simulation.World.Ai;

namespace PristonTale.Simulation.Combat;

public static class MonsterOffensiveSkill
{
    private const double ConnectedDistanceSquared = World.ViewRanges.ConnectSquared;

    public static Vector3 AreaCenter(Vector3 origin, float facing, MonsterSkillStats skill)
    {
        if (skill.SkillRange == 0 || skill.SkillDistance == 0)
        {
            return origin;
        }

        var forward = Planar.Forward(facing);
        return origin + forward * skill.SkillDistance;
    }

    public static float QueryRadius(MonsterSkillStats skill)
    {
        // Query around the caster. Include forward square corners, bounded by the original connected-player distance.
        float extent = skill.SkillRange;
        if (skill.SkillDistance > 0)
        {
            extent = skill.SkillDistance + skill.SkillRange * MathF.Sqrt(2f);
        }

        return MathF.Min(extent, MathF.Sqrt((float)ConnectedDistanceSquared));
    }

    public static bool Contains(MonsterSkillStats skill, Vector3 origin, Vector3 center, Vector3 target)
    {
        double dx = (double)target.X - origin.X;
        double dy = (double)target.Y - origin.Y;
        double dz = (double)target.Z - origin.Z;
        double distanceSquared = dx * dx + dy * dy + dz * dz;
        if (Math.Abs(dy) >= MonsterAiTuning.VerticalAggroGate || distanceSquared >= ConnectedDistanceSquared)
        {
            return false;
        }

        if (skill.SkillRange == 0)
        {
            // A bounded single-target reach: a skill never reaches a target beyond it, whatever the request asks.
            return distanceSquared < MonsterAttackReach.RangedImpactDistanceSquared;
        }

        if (skill.SkillDistance > 0)
        {
            return Math.Abs((double)target.X - center.X) < skill.SkillRange
                && Math.Abs((double)target.Z - center.Z) < skill.SkillRange;
        }

        return distanceSquared < (double)skill.SkillRange * skill.SkillRange;
    }
}
