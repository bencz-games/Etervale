using System.Text;
using PristonTale.Assets;
using PristonTale.Contracts.Motion;
using PristonTale.Engine3D.Formats;

namespace PristonTale.Tests;

internal static class ModelTextTests
{
    public static IEnumerable<(string, Func<Task>)> Cases()
    {
        yield return ("a model descriptor reads its files with its directory, levels of detail, motions with their states, events, "
            + "loops, keys, weapons, classes, places and skills, talk motions, and shares the idle and talk chances over 100 slots", DescriptorAsync);
        yield return ("a model-info file compiled from a descriptor reads back the same, its frames protected and its model "
            + "keywords valid for its path and its linked model's", CompileAsync);
        yield return ("model info without an .inx is read from its descriptor, and models and motions resolve to the compiled "
            + "file, then glTF, an explicit format kept", ResolveAsync);
        yield return ("every descriptor of the game's characters reads as the model-info file compiled beside it, and compiles to "
            + "one that reads back the same", AssetsAsync);
    }

    private const string Motions = """
        // a test descriptor
        *모양파일	Myth.ASE
        *동작파일	"Myth Motion.ASE"
        *보조동작파일	Myth_d.ini
        *정밀모양	"Body"	Head
        *보통모양	Body
        *동작모음	"Myth Motion.ASE"
        *서있기동작		10		50	반복	(30)
        *걷는동작		60		90	75
        *공격동작		100		140	110	120	121	122	123
        *착용무기	WA101	없음	XX999
        *적용직업	knight	Priest	Nobody
        *해당위치	필드
        *기술동작		150		190	160	a
        *적용기술	BrutalSwing	roar	NotASkill
        *죽는동작		200		261
        *가끔동작		270		300	x	(40)
        """;

    // A chance follows a loop word or a key: right after the frames it would end the events and be lost.
    private const string Talk = """
        *동작모음	"Myth Talk.ASE"
        *아표정		0		10	반복	(50)
        *눈깜빡표정		20		30		(20)
        *끝
        """;

    private const string Descriptor = Motions + "\n" + Talk;

    private static Task DescriptorAsync() => InAssets(root =>
    {
        var path = Write(root, @"char/npc/Myth/Myth.ini", Descriptor);
        var info = PristonModelText.Read(path);
        Assert.True(info.ModelFile == @"char\npc\Myth\Myth.ASE" && info.MotionFile == @"char\npc\Myth\Myth Motion.ASE"
            && info.SubModelFile == @"char\npc\Myth\Myth_d.ini" && info.LinkFile == "", $"the files differ: {info.ModelFile}, {info.MotionFile}");
        Assert.True(info.HighModel is { ModelNameCount: 2, ModelNames: ["Body", "Head", "", ""] } && info.DefaultModel.ModelNameCount == 1
            && info.LowModel.ModelNameCount == 0, "the levels of detail differ");
        Assert.True(info.MotionCount == PristonFormatConstants.CharacterMotionBase + 6, $"motion count {info.MotionCount}");

        var stand = info.Motions[10];
        Assert.True(stand is { State: (uint)MotionCode.Stand, StartFrame: 10, EndFrame: 50, Repeat: 1, MotionFrame: 1 }, "the stand differs");
        var walk = info.Motions[11];
        Assert.True(walk is { State: (uint)MotionCode.Walk, Repeat: 1 } && walk.EventFrames[0] == 15 * 160, "the walk or its event differs");
        // Four events at most; the fifth number ends them and is read as the key.
        var attack = info.Motions[12];
        Assert.True(attack is { State: (uint)MotionCode.Attack, Repeat: 0, KeyCode: (byte)'1', ItemCodeCount: 2, JobCodeBit: 0x20 | 0x80, MapPosition: 2 }
            && attack.EventFrames.SequenceEqual([10u * 160, 20u * 160, 21u * 160, 22u * 160])
            && attack.ItemIndices.SequenceEqual([PristonModelTextNames.ItemIndex("WA101"), -1]), "the attack, its weapons, classes or place differ");
        var skill = info.Motions[13];
        Assert.True(skill is { State: (uint)MotionCode.Skill, KeyCode: (byte)'a' }
            && skill.SkillCodes.Take(3).SequenceEqual([(byte)PristonModelTextNames.SkillCode("BRUTALSWING"), (byte)PristonModelTextNames.SkillCode("Roar"), (byte)0]),
            "the skill motion or its skills differ");
        Assert.True(info.Motions[14] is { State: (uint)MotionCode.Dead, EndFrame: 253 }, "the death does not end 8 frames early");
        Assert.True(info.Motions[15] is { State: (uint)MotionCode.Sometime, MotionFrame: 1, KeyCode: (byte)'x' }, "the occasional motion differs");

        Assert.True(info.TalkMotionCount == PristonFormatConstants.CharacterMotionBase + 2
            && info.TalkMotions[10] is { State: (uint)MotionCode.TalkAh, MotionFrame: 1, Repeat: 1 }
            && info.TalkMotions[11] is { State: (uint)MotionCode.TalkEyeBlink, MotionFrame: 1 } && info.TalkMotionRates[11] == 0,
            "the talk motions differ");

        // Stand 30, the occasional motion 40: the largest takes the rest of 100.
        Assert.True(info.NpcMotionRates[10] == 30 && info.NpcMotionRates[15] == 70
            && info.NpcMotionSelection.Count(static slot => slot == 10) == 30 && info.NpcMotionSelection.Count(static slot => slot == 15) == 70,
            $"the idle chances differ: {string.Join(",", info.NpcMotionRates)}");
        Assert.True(info.TalkMotionRates[10] == 50 && info.TalkMotionSelection[1].All(static slot => slot == 10),
            "the talk chances differ");
    });

    private static Task CompileAsync() => InAssets(root =>
    {
        var path = Write(root, @"char/npc/Myth/Myth.ini", Motions + "\n*파일연결\tM1bip.in\n");
        var text = PristonModelText.Read(path);
        var compiled = Path.ChangeExtension(path, ".inx");
        var bytes = PristonInxWriter.Write(text, compiled);
        File.WriteAllBytes(compiled, bytes);
        var info = new PristonInxReader().Read(compiled);
        Assert.True(bytes.Length == 95268 && info.Path == compiled && info.ModelKeywordsValid && info.MotionFramesWereProtected
            && !info.MotionKeywordsFromOtherPath && info.LinkFile == @"char\npc\Myth\M1bip.in", "the compiled file's keywords or link differ");
        Same(text, info, "compiled");
    });

    private static Task ResolveAsync() => InAssets(root =>
    {
        var descriptor = Write(root, @"char/npc/Myth/Myth.ini", Descriptor);
        Assert.True(new PristonInxReader().Read(Path.ChangeExtension(descriptor, ".inx")).ModelFile == @"char\npc\Myth\Myth.ASE",
            "model info without an .inx was not read from its descriptor");
        Assert.True(PristonModelPaths.Info(descriptor) == descriptor, "a descriptor alone is not its model info");
        File.WriteAllBytes(Path.ChangeExtension(descriptor, ".inx"), PristonInxWriter.Write(PristonModelText.Read(descriptor), Path.ChangeExtension(descriptor, ".inx")));
        Assert.True(PristonModelPaths.Info(descriptor) == Path.ChangeExtension(descriptor, ".inx"), "the compiled model info does not come first");

        var directory = Path.GetDirectoryName(descriptor)!;
        File.WriteAllBytes(Path.Combine(directory, "Myth.glb"), []);
        Assert.True(PristonModelPaths.Mesh(root, @"char\npc\Myth\Myth.ASE").EndsWith("Myth.glb", StringComparison.Ordinal),
            "a model with only a glTF beside it does not resolve to it");
        File.WriteAllBytes(Path.Combine(directory, "Myth.smd"), []);
        File.WriteAllBytes(Path.Combine(directory, "Myth.smb"), []);
        Assert.True(PristonModelPaths.Mesh(root, @"char\npc\Myth\Myth.ASE").EndsWith("Myth.smd", StringComparison.Ordinal)
            && PristonModelPaths.Motion(root, @"char\npc\Myth\Myth.ase").EndsWith("Myth.smb", StringComparison.Ordinal),
            "the compiled model or motion file does not come first");
        Assert.True(PristonModelPaths.Mesh(root, @"char\npc\Myth\Myth.glb").EndsWith("Myth.glb", StringComparison.Ordinal)
            && PristonModelPaths.Mesh(root, @"char\npc\Myth\Other.smd").EndsWith("Other.smd", StringComparison.Ordinal),
            "an explicit format was not kept");
        Assert.True(PristonInxReader.WithExtension(@"char\a.b\c", ".inx") == @"char\a.b\c.inx"
            && PristonInxReader.WithExtension("char/x/M1bip.in", ".inx") == "char/x/M1bip.inx", "an extension is not the last name's");
    });

    // Edited after their model-info file was compiled: a folder renamed, motions added.
    private static readonly string[] Revised = [@"char\monster\death_night\death_knight.ini", @"char\npc\Mage\Mage.ini", @"char\npc\SkillMaster\SkillMaster.ini"];

    // Skips without the game's assets.
    private static Task AssetsAsync()
    {
        if (AssetFiles.FindAssetsRoot() is not { } root)
        {
            return Task.CompletedTask;
        }

        var reader = new PristonInxReader();
        var compared = 0;
        var failures = new List<string>();
        return InAssets(copies =>
        {
            foreach (var descriptor in Directory.EnumerateFiles(Path.Combine(root, "char"), "*.*", SearchOption.AllDirectories)
                         .Where(PristonModelText.IsDescriptor).Order(StringComparer.Ordinal))
            {
                var compiled = PristonInxReader.CompiledPath(descriptor);
                var name = Path.GetRelativePath(root, descriptor).Replace('/', '\\');
                if (!File.Exists(AssetFiles.Resolve(compiled)) || Revised.Contains(name, StringComparer.OrdinalIgnoreCase)
                    || Path.GetFileName(descriptor).Equals("desktop.ini", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                compared++;
                try
                {
                    var text = PristonModelText.Read(descriptor);
                    Same(text, reader.Read(compiled), name);
                    var copy = AssetFiles.Combine(copies, PristonInxReader.CompiledPath(name));
                    Directory.CreateDirectory(Path.GetDirectoryName(copy)!);
                    File.WriteAllBytes(copy, PristonInxWriter.Write(text, copy));
                    Same(text, reader.Read(copy), name + " compiled");
                }
                catch (Exception exception) when (exception is AssertionException or InvalidDataException)
                {
                    failures.Add(exception.Message);
                }
            }

            Assert.True(compared > 700 && failures.Count == 0, $"{failures.Count} of {compared} descriptors differ from their model-info files:\n"
                + string.Join("\n", failures.Take(40)));
        });
    }

    private static void Same(PristonModelInfo text, PristonModelInfo compiled, string name)
    {
        static bool Path(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
        static bool Group(PristonModelGroup a, PristonModelGroup b) => a.ModelNameCount == b.ModelNameCount && a.ModelNames.SequenceEqual(b.ModelNames);
        Check(Path(text.ModelFile, compiled.ModelFile) && Path(text.MotionFile, compiled.MotionFile) && Path(text.SubModelFile, compiled.SubModelFile)
            && Path(text.LinkFile, compiled.LinkFile) && Path(text.TalkLinkFile, compiled.TalkLinkFile) && Path(text.TalkMotionFile, compiled.TalkMotionFile),
            $"files ({text.ModelFile}|{compiled.ModelFile}, {text.MotionFile}|{compiled.MotionFile}, {text.LinkFile}|{compiled.LinkFile})");
        Check(Group(text.HighModel, compiled.HighModel) && Group(text.DefaultModel, compiled.DefaultModel) && Group(text.LowModel, compiled.LowModel),
            "levels of detail");
        Check(text.MotionCount == compiled.MotionCount && text.TalkMotionCount == compiled.TalkMotionCount,
            $"counts {text.MotionCount}/{compiled.MotionCount}, {text.TalkMotionCount}/{compiled.TalkMotionCount}");
        for (var slot = 0; slot < text.Motions.Count; slot++)
        {
            Check(SameMotion(text.Motions[slot], compiled.Motions[slot]), $"motion {slot}: {Show(text.Motions[slot])} | {Show(compiled.Motions[slot])}");
        }

        for (var slot = 0; slot < text.TalkMotions.Count; slot++)
        {
            Check(SameMotion(text.TalkMotions[slot], compiled.TalkMotions[slot]), $"talk motion {slot}");
        }

        Check(text.NpcMotionRates.SequenceEqual(compiled.NpcMotionRates) && text.NpcMotionSelection.SequenceEqual(compiled.NpcMotionSelection)
            && text.TalkMotionRates.SequenceEqual(compiled.TalkMotionRates)
            && text.TalkMotionSelection.Zip(compiled.TalkMotionSelection).All(static pair => pair.First.SequenceEqual(pair.Second)), "chances");

        void Check(bool same, string what) => Assert.True(same, $"{name}: {what}");
    }

    private static bool SameMotion(PristonMotionInfo a, PristonMotionInfo b) =>
        a.State == b.State && a.StartFrame == b.StartFrame && a.EndFrame == b.EndFrame && a.EventFrames.SequenceEqual(b.EventFrames)
        && a.ItemCodeCount == b.ItemCodeCount && a.ItemIndices.SequenceEqual(b.ItemIndices) && a.JobCodeBit == b.JobCodeBit
        && a.MapPosition == b.MapPosition && a.Repeat == b.Repeat && a.KeyCode == b.KeyCode && a.MotionFrame == b.MotionFrame
        && a.SkillCodes.SequenceEqual(b.SkillCodes);

    private static string Show(PristonMotionInfo motion) =>
        $"{motion.State:X} {motion.StartFrame}-{motion.EndFrame} ev[{string.Join(",", motion.EventFrames)}] r{motion.Repeat} k{motion.KeyCode} f{motion.MotionFrame}";

    private static string Write(string root, string relative, string text)
    {
        var path = AssetFiles.Combine(root, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, Cp949.Encoding.GetBytes(text.Replace("\n", "\r\n")));
        return path;
    }

    private static Task InAssets(Action<string> test)
    {
        var temp = Path.Combine(Path.GetTempPath(), "priston-model-text-" + Guid.NewGuid().ToString("N"));
        try
        {
            test(Path.Combine(temp, "assets"));
        }
        finally
        {
            if (Directory.Exists(temp))
            {
                Directory.Delete(temp, recursive: true);
            }
        }

        return Task.CompletedTask;
    }
}
