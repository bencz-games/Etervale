using PristonTale.Engine3D.Formats;
using PristonTale.Contracts.Motion;

namespace PristonTale.Engine3D.Rendering;

public readonly record struct PristonMotionContext(int ItemIndex, uint JobMask, int MapPosition);

public static class PristonMotionSelection
{
    public static PristonMotionInfo Select(PristonModelInfo info, MotionCode state, PristonMotionContext context)
    {
        PristonMotionInfo fallback = null;
        PristonMotionInfo unrestricted = null;
        for (int index = PristonFormatConstants.CharacterMotionBase; index < info.Motions.Count; index++)
        {
            var motion = info.Motions[index];
            if (motion.State != (uint)state || motion.EndFrame <= motion.StartFrame)
            {
                continue;
            }

            if ((motion.MapPosition != 0 && context.MapPosition != 0 && (motion.MapPosition & context.MapPosition) == 0)
                || (motion.JobCodeBit != 0 && context.JobMask != 0 && (motion.JobCodeBit & context.JobMask) == 0))
            {
                continue;
            }

            fallback ??= motion;
            if (motion.ItemCodeCount == 0)
            {
                unrestricted ??= motion;
                continue;
            }

            var item = context.MapPosition == 1 ? -1 : context.ItemIndex;
            foreach (var allowed in motion.ItemIndices)
            {
                if ((item < 0 && allowed is -1 or byte.MaxValue) || item == allowed)
                {
                    return motion;
                }
            }
        }

        return unrestricted ?? fallback;
    }

    // A skill clip is authored once per weapon group: the clips that share the indexed clip's state and skill tags differ
    // only in the weapons they list. Returns the sibling whose weapons include the context's, else the indexed clip.
    public static int SkillVariant(PristonModelInfo info, int motionIndex, PristonMotionContext context)
    {
        var count = Math.Min(info.Motions.Count, checked((int)info.MotionCount));
        if ((uint)motionIndex >= (uint)count)
        {
            return SkillFallback(info, context);
        }

        var authored = info.Motions[motionIndex];
        if (authored.State != (uint)MotionCode.Skill || !authored.IsUsable)
        {
            return SkillFallback(info, context);
        }

        if (authored.ItemCodeCount == 0 || context.ItemIndex < 0 || authored.ItemIndices.Contains(context.ItemIndex))
        {
            return motionIndex;
        }

        for (int index = PristonFormatConstants.CharacterMotionBase; index < info.Motions.Count; index++)
        {
            var motion = info.Motions[index];
            if (motion.State == authored.State && motion.SkillCodes.SequenceEqual(authored.SkillCodes) && motion.EndFrame > motion.StartFrame
                && motion.ItemIndices.Contains(context.ItemIndex))
            {
                return index;
            }
        }

        return motionIndex;
    }

    private static int SkillFallback(PristonModelInfo info, PristonMotionContext context)
    {
        var first = -1;
        var unrestricted = -1;
        var count = Math.Min(info.Motions.Count, checked((int)info.MotionCount));
        for (var index = PristonFormatConstants.CharacterMotionBase; index < count; index++)
        {
            var motion = info.Motions[index];
            if (motion.State != (uint)MotionCode.Skill || !motion.IsUsable
                || (motion.MapPosition != 0 && context.MapPosition != 0 && (motion.MapPosition & context.MapPosition) == 0)
                || (motion.JobCodeBit != 0 && context.JobMask != 0 && (motion.JobCodeBit & context.JobMask) == 0))
            {
                continue;
            }

            first = first < 0 ? index : first;
            if (motion.ItemCodeCount == 0)
            {
                unrestricted = index;
                continue;
            }

            var item = context.MapPosition == 1 ? -1 : context.ItemIndex;
            foreach (var allowed in motion.ItemIndices)
            {
                if (item < 0 && allowed is -1 or byte.MaxValue || item == allowed)
                {
                    return index;
                }
            }
        }

        if (unrestricted >= 0)
        {
            return unrestricted;
        }

        if (first >= 0)
        {
            return first;
        }

        for (var index = PristonFormatConstants.CharacterMotionBase; index < count; index++)
        {
            if (info.Motions[index].IsUsable)
            {
                return index;
            }
        }

        return 0;
    }
}
