using Microsoft.Xna.Framework;
using PristonTale.Engine3D.Formats;
using PristonTale.Engine3D.Rendering;

namespace PristonTale.Tests;

// The crowd level of detail of actor animation: how often an actor samples its pose by distance and visibility, and
// that the actors sharing an interval take their turns on different frames.
internal static class CrowdAnimationLodTests
{
    public static IEnumerable<(string, Func<Task>)> Cases()
    {
        yield return ("an actor samples its pose every frame near the camera or in the portrait, less often farther and off screen, each on its own frames", PolicyAsync);
        yield return ("a kept pose is a copy the animator cannot overwrite, sampled again for a new motion or a refresh that moved on", PoseAsync);
    }

    private static Task PolicyAsync()
    {
        Assert.True(CrowdAnimationLod.Interval(CrowdAnimationLod.NearDistance, true) == 1
            && CrowdAnimationLod.Interval(CrowdAnimationLod.FarDistance, true) == 2
            && CrowdAnimationLod.Interval(CrowdAnimationLod.FarDistance + 1f, true) == CrowdAnimationLod.FarInterval
            && CrowdAnimationLod.Interval(0f, false) == 1
            && CrowdAnimationLod.Interval(CrowdAnimationLod.NearDistance + 1f, false) == CrowdAnimationLod.OffScreenInterval,
            "the intervals do not follow distance and visibility");
        Assert.True(CrowdAnimationLod.Interval(CrowdAnimationLod.FarDistance * 3f, true, portrait: true) == 1
            && CrowdAnimationLod.Interval(CrowdAnimationLod.FarDistance * 3f, false, portrait: true) == 1,
            "the portrait's actor did not sample every frame far away");

        for (var interval = 1; interval <= CrowdAnimationLod.OffScreenInterval; interval++)
        {
            for (var slot = 0; slot < 20; slot++)
            {
                var due = Enumerable.Range(0, interval).Count(frame => CrowdAnimationLod.Due(interval, (uint)(1000 + frame), slot));
                Assert.True(due == 1, $"slot {slot} was due {due} times in {interval} frames");
            }

            var together = Enumerable.Range(0, interval).Count(slot => CrowdAnimationLod.Due(interval, 7, slot));
            Assert.True(together == 1, $"{together} of {interval} consecutive slots were due on one frame");
        }

        return Task.CompletedTask;
    }

    private static Task PoseAsync()
    {
        var walk = new PristonMotionInfo(2, 10, 40, [], 0, 0, 0, 1, 0, 0);
        var stand = new PristonMotionInfo(1, 50, 80, [], 0, 0, 0, 1, 0, 0);
        var pose = new ActorPose();
        Assert.True(pose.NeedsSample(false, 0f, walk), "an actor without a pose kept none");

        var animator = new[] { Matrix.CreateTranslation(1f, 0f, 0f) };
        pose.Take(animator, 1600f, walk);
        animator[0] = Matrix.CreateTranslation(9f, 0f, 0f);
        Assert.True(pose.Transforms[0].Translation.X == 1f, "the kept pose changed with the animator's buffer");
        Assert.True(!pose.NeedsSample(false, 3200f, walk) && !pose.NeedsSample(true, 1600f, walk),
            "a pose was sampled again though the level of detail kept it or its frame did not move");
        Assert.True(pose.NeedsSample(true, 3200f, walk) && pose.NeedsSample(false, 1600f, stand), "a refresh that moved on or a new motion kept the old pose");
        return Task.CompletedTask;
    }
}
