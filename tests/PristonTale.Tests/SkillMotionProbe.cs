using System.Numerics;
using PristonTale.Engine3D.Formats;
using PristonTale.Simulation.Characters;
using PristonTale.Simulation.Skills;
using PristonTale.Contracts.Motion;
using PristonTale.Contracts.Characters;

namespace PristonTale.Tests;

// Checks the skill rules' motion indices against the shipped class motion tables: the indexed clip must be a skill
// motion tagged with the rule's motion tag whose length and event frames match the rule (--verify-skill-motions <assets>).
// Every tagged skill clip of the class is listed first so a rule can be matched by hand.
internal static class SkillMotionProbe
{
    public static void Run(string root)
    {
        var classes = new CharacterClassRegistry();
        var loader = new PristonActorLoader();
        var failures = new List<string>();
        foreach (var playerClass in new[]
        {
            PlayerClass.Mechanician, PlayerClass.Fighter, PlayerClass.Pikeman, PlayerClass.Archer, PlayerClass.Knight, PlayerClass.Atalanta,
            PlayerClass.Priestess, PlayerClass.Magician, PlayerClass.Assassin, PlayerClass.Shaman, PlayerClass.MartialArtist
        })
        {
            var path = classes.Get(playerClass).Definition.DefaultVisuals.Body.InxPath;
            var actor = loader.Load(root, Path.Combine(root, path));
            var motions = actor.Info.Motions;
            for (var index = 0; index < motions.Count; index++)
            {
                var motion = motions[index];
                if (motion.State == (uint)MotionCode.Skill)
                {
                    Console.WriteLine($"{playerClass} clip {index}: frames {motion.EndFrame - motion.StartFrame} events [{string.Join(',', Events(motion))}]"
                        + $" tags [{string.Join(',', motion.SkillCodes.Where(static code => code != 0))}] items [{string.Join(',', motion.ItemIndices)}]");
                }
            }

            foreach (var skill in ClassSkillCatalog.For(playerClass))
            {
                if (!SkillRuleCatalog.TryGet(skill.Code, out var rule) || rule.Kind == SkillKind.Passive)
                {
                    continue;
                }

                // A variant may play another skill clip of the class (a hit count that grows into a sibling skill's motion). The
                // Knight's final-tier clips carry tags of the class that do not follow the slot order, so a skill's own clip
                // must carry a tag of its class; its length and events still have to match.
                if (!FirstTags.TryGetValue(skill.Code >> 24, out var first))
                {
                    continue;
                }

                // A class's tags run up to the next class's first one (the Knight's last two pass its twentieth).
                var next = FirstTags.Values.Where(tag => tag > first).DefaultIfEmpty(first + 20).Min();
                var classTags = Enumerable.Range(first, next - first).Select(static tag => (byte)tag).ToArray();
                if (rule.Motion == SkillMotion.Skill)
                {
                    Check(playerClass, rule, rule.MotionIndex, rule.MotionFrames, rule.MotionEvents, classTags, motions, failures);
                }

                foreach (var variant in rule.MotionVariants.Where(static variant => variant.Motion == SkillMotion.Skill))
                {
                    Check(playerClass, rule, variant.MotionIndex, variant.MotionFrames, variant.MotionEvents, classTags, motions, failures);
                }
            }
        }

        foreach (var failure in failures)
        {
            Console.Error.WriteLine("MISMATCH " + failure);
        }

        Assert.True(failures.Count == 0, $"{failures.Count} skill motions disagree with the class motion tables");
    }

    // The class motion tables tag skill clips with the class's first tag + 4 x tier + slot - 1 (tier 0 to 4); the first
    // tags of the eight class groups, as the shipped tables number them.
    private static readonly Dictionary<uint, int> FirstTags = new()
    {
        [1] = 1, [2] = 21, [3] = 41, [4] = 61, [5] = 81, [6] = 103, [7] = 123, [8] = 143, [0x10] = 163, [0x20] = 203, [0x50] = 183
    };

    public static byte Tag(uint code)
    {
        var tier = BitOperations.Log2((code >> 16) & 0x1F);
        return (byte)(FirstTags[code >> 24] + 4 * tier + (int)(code & 0xF) - 1);
    }

    private static void Check(PlayerClass playerClass, SkillRule rule, int index, int frames, int[] events, byte[] tags, IReadOnlyList<PristonMotionInfo> motions,
        List<string> failures)
    {
        var tag = Tag(rule.Code);
        var line = $"{playerClass} {rule.Name}: index {index} tag {tag}";
        if (index >= motions.Count)
        {
            failures.Add(line + " is outside the table");
            return;
        }

        var clip = motions[index];
        var length = (int)(clip.EndFrame - clip.StartFrame);
        var clipEvents = Events(clip);
        var report = $"{line} frames {length} events [{string.Join(',', clipEvents)}] tags [{string.Join(',', clip.SkillCodes.Where(static code => code != 0))}]";
        Console.WriteLine(report);
        if (clip.State != (uint)MotionCode.Skill || !clip.SkillCodes.Any(tags.Contains) || length != frames
            || !events.SequenceEqual(clipEvents.Take(events.Length)))
        {
            var candidates = Enumerable.Range(0, motions.Count).Where(candidate => motions[candidate].State == (uint)MotionCode.Skill
                && motions[candidate].SkillCodes.Contains(tag))
                .Select(candidate => $"{candidate}:{motions[candidate].EndFrame - motions[candidate].StartFrame}:[{string.Join(',', Events(motions[candidate]))}]");
            failures.Add(report + " mismatch; candidates " + string.Join(' ', candidates));
        }
    }

    private static int[] Events(PristonMotionInfo motion) => motion.EventFrames.Where(static frame => frame > 0).Select(static frame => (int)frame / 160).ToArray();
}
