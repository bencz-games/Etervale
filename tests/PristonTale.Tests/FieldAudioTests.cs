using System.Text;
using Microsoft.Xna.Framework;
using PristonTale.Engine3D.IO;
using PristonTale.Gameplay.Audio;
using PristonTale.Simulation.World;
using PristonTale.Simulation.World.Npcs;
using PristonTale.Assets;
using PristonTale.Contracts.Characters;

namespace PristonTale.Tests;

internal static class FieldAudioTests
{
    public static IEnumerable<(string, Func<Task>)> Cases()
    {
        yield return ("original JODO ambience is normalized without changing RIFF audio", WaveHeaderAsync);
        yield return ("field ambience uses the original radius and distance volume curve", ProximityVolumeAsync);
        yield return ("continuous field ambience follows original state code and day night rules", FieldAmbientSelectionAsync);
        yield return ("field catalogue preserves all 72 authored positional ambience sources", FieldCatalogueAsync);
        yield return ("Ricarten cart and windmill ambience preserve their authored sources", RicartenMachineryAmbienceAsync);
        yield return ("NPC sound-bank names preserve the classic 32-bit lookup", NpcSoundCodesAsync);
        yield return ("NPC talk filenames preserve the classic CP949 checksum", NpcTalkSoundCodeAsync);
        yield return ("NPC motion sounds preserve the classic positional attenuation", NpcProximityVolumeAsync);
        yield return ("swings pick the original weapon sound by weapon family, hand and class", WeaponSoundCodesAsync);
        yield return ("character sound banks cover monsters, summons, NPCs and every class with an asset folder", CharacterBanksAsync);
        yield return ("items sound by family with their own exceptions, and every item sound file exists", ItemSoundsAsync);
        yield return ("a potion taking effect plays the original's potion sound once with its drops", PotionSoundAsync);
        yield return ("every login and character-screen cue has a file, and every one of them and the loops exists", LoginSoundsAsync);
    }

    private static Task PotionSoundAsync()
    {
        using var sounds = new PristonTale.Gameplay.Effects.SkillSoundBank(Path.GetTempPath(), PristonTale.Engine3D.Diagnostics.NullClientLog.Instance)
        {
            Enabled = false
        };
        var played = new List<string>();
        sounds.Requested += played.Add;
        var effects = new PristonTale.Gameplay.Effects.ItemUseEffects(null, null, sounds);
        effects.Potion(0b111, new Vector3(0f, 48f, 0f), Vector3.Zero);
        Assert.True(played.SequenceEqual([PristonTale.Gameplay.Effects.ItemUseEffects.EatSound]), $"a potion played {string.Join(", ", played)}");
        return Task.CompletedTask;
    }

    private static Task LoginSoundsAsync()
    {
        var cued = LoginAudio.CueFiles.Select(static entry => entry.Cue).ToHashSet();
        Assert.True(cued.SetEquals(Enum.GetValues<LoginCue>()), "a login cue has no file");

        var root = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "assets");
        if (Directory.Exists(Path.Combine(root, "wav")))
        {
            foreach (var path in LoginAudio.CueFiles.Select(static entry => entry.Path)
                .Concat(LoginAudio.CharacterSelectLoopFiles.Select(static entry => entry.Path)))
            {
                Assert.True(File.Exists(AssetFiles.Resolve(Path.Combine(root, path))), $"login sound {path} is missing");
            }
        }

        return Task.CompletedTask;
    }

    private static Task ItemSoundsAsync()
    {
        (string Key, string File)[] cases =
        [
            ("WA101", "Axes.wav"), ("WS101", "Shooters.wav"), ("WS201", "Swords.wav"), ("WM101", "Magicial_weapon.wav"),
            ("DA304", "Armor.wav"), ("DA401", "Armor.wav"), ("DB101", "Boots.wav"), ("OA101", "Amulet.wav"), ("OA201", "Armlet.wav"),
            ("OR201", "Ring.wav"), ("PL101", "Potion.wav"), ("GG101", "Coin.wav"), ("GG102", "Magicial_Stuffs.wav"),
            ("BI101", "Sheltom.wav"), ("BI150", "Potion.wav"), ("BI213", "Potion.wav"), ("BI201", "Sheltom.wav"),
            ("QT113", "Ring.wav"), ("QT101", "Armlet.wav"), ("ZZ101", "Armlet.wav"), ("", "Armlet.wav")
        ];
        foreach (var (key, file) in cases)
        {
            Assert.True(ItemSoundRules.For(key) == file, $"{key} sounds {ItemSoundRules.For(key)}, expected {file}");
        }

        var root = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "assets");
        if (Directory.Exists(Path.Combine(root, "image")))
        {
            foreach (var file in ItemSoundRules.Files)
            {
                Assert.True(File.Exists(AssetFiles.Resolve(Path.Combine(root, ItemSoundRules.Directory + file))), $"item sound {file} is missing");
            }
        }

        return Task.CompletedTask;
    }

    private static Task WeaponSoundCodesAsync()
    {
        (string Key, int Class, int Code)[] cases =
        [
            ("WA101", 1, 1), ("WA105", 1, 2), ("WS201", 1, 15), ("WS203", 1, 15), ("WS204", 6, 3), ("WS205", 6, 4),
            ("WP101", 4, 5), ("WP104", 4, 6), ("WC101", 1, 7), ("WS101", 3, 8), ("WM101", 7, 17), ("WM104", 8, 18),
            ("WM101", 1, 9), ("WH101", 2, 9), ("WH106", 2, 10), ("WT101", 5, 11), ("", 1, 14)
        ];
        foreach (var (key, classCode, code) in cases)
        {
            var actual = CombatSounds.WeaponCode(key, key.Length > 0 && Simulation.Items.ItemEquipmentMetadata.IsTwoHanded(key), (PlayerClass)classCode);
            Assert.True(actual == code, $"{key} for class {classCode} picked weapon sound {actual}, expected {code}");
        }

        return Task.CompletedTask;
    }

    private static Task CharacterBanksAsync()
    {
        var codes = CharacterSoundBanks.Directories.Select(bank => bank.Code).ToHashSet();
        Assert.True(codes.Contains(0x1600) && codes.Contains(CharacterSoundCodes.Wolverine) && codes.Contains(CharacterSoundCodes.Morif)
            && Enumerable.Range(1, 11).All(codes.Contains), "a monster, NPC or class bank is missing");
        Assert.True(CharacterSoundCodes.Resolve("Furia") == CharacterSoundCodes.Fury, "the red gladiator lost the Fury's sounds");
        foreach (var keyword in (string[])["Greedy", "Hestian", "ISoldier", "Lizard_Papa", "UndeadStalker", "Yagditha", "Soldier_D",
            "Adevayo", "Amesyst", "Amonk", "Aragonian", "CrystalWrath", "Deius", "Grangium", "Korpion", "Oblanko", "QuartzBat",
            "QuartzSaurus", "Raeda", "Rakoo", "Roblanko", "Sylers", "Tees", "Toos", "UndeadSlave", "Ziddane"])
        {
            var code = CharacterSoundCodes.Resolve(keyword);
            Assert.True(code >= 0x6000 && codes.Contains(code), $"the newer monster sound {keyword} has no code or bank");
        }

        var root = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "assets");
        if (Directory.Exists(Path.Combine(root, "wav")))
        {
            foreach (var (code, directory) in CharacterSoundBanks.Directories)
            {
                Assert.True(Directory.Exists(AssetFiles.Resolve(Path.Combine(root, directory))), $"bank 0x{code:X4} has no folder {directory}");
            }
        }

        return Task.CompletedTask;
    }

    private static Task WaveHeaderAsync()
    {
        var original = CreateWaveHeader("JODO");
        PristonWaveDecoder.NormalizeHeader(original);
        Assert.True(original.AsSpan(0, 4).SequenceEqual("RIFF"u8), "original WAVE signature was not normalized");

        var riff = CreateWaveHeader("RIFF");
        var expected = riff.ToArray();
        PristonWaveDecoder.NormalizeHeader(riff);
        Assert.True(riff.SequenceEqual(expected), "ordinary RIFF data was modified");

        AssertInvalid(() => PristonWaveDecoder.NormalizeHeader(CreateWaveHeader("NOPE")));
        AssertInvalid(() => PristonWaveDecoder.NormalizeHeader(CreateWaveHeader("RIFF", 5)));
        AssertInvalid(() => PristonWaveDecoder.NormalizeHeader(new byte[11]));
        return Task.CompletedTask;
    }

    private static Task ProximityVolumeAsync()
    {
        var ambient = new FieldAmbient(new FieldVector(10, 20, -30), 20, 7);
        var atSource = FieldAudioController.ProximityVolume(new Vector3(10, 20, 30), ambient);
        Assert.Near(399f / 400f, atSource, 0.0001f);

        var finalAudibleDistance = MathF.Sqrt((20 * 20) + (299 * 1100));
        var finalAudible = FieldAudioController.ProximityVolume(new Vector3(10 + finalAudibleDistance, 20, 30), ambient);
        Assert.Near(201f / 400f, finalAudible, 0.0001f);

        var silentDistance = MathF.Sqrt((20 * 20) + (300 * 1100));
        var silent = FieldAudioController.ProximityVolume(new Vector3(10 + silentDistance, 20, 30), ambient);
        Assert.Near(0f, silent);
        return Task.CompletedTask;
    }

    private static Task FieldCatalogueAsync()
    {
        var catalog = new WorldCatalog();
        var expectedCounts = new Dictionary<string, int>(StringComparer.Ordinal)
        {
            ["fore-3"] = 1,
            ["fore-2"] = 1,
            ["village-2"] = 2,
            ["dun-1"] = 1,
            ["forever-fall-04"] = 7,
            ["forever-fall-03"] = 2,
            ["forever-fall-02"] = 6,
            ["forever-fall-01"] = 8,
            ["pilai"] = 10,
            ["dun-4"] = 14,
            ["dun-5"] = 9,
            ["dun-6"] = 11
        };

        var total = 0;
        foreach (var field in catalog.AllFields)
        {
            total += field.Ambients.Count;
            if (expectedCounts.TryGetValue(field.Id, out var expected))
            {
                Assert.True(field.Ambients.Count == expected, $"{field.Id} ambience count changed");
            }
            else
            {
                Assert.True(field.Ambients.Count == 0, $"unexpected ambience source in {field.Id}");
            }

            foreach (var ambient in field.Ambients)
            {
                Assert.True(ambient.SoundCode is >= 0 and <= 27, $"{field.Id} has an invalid ambience sound code");
            }
        }

        Assert.True(total == 72, "the original field ambience table is incomplete");

        var forestToPillai = catalog.GetField("forever-fall-01").WarpGates.Single();
        Assert.True(
            forestToPillai.SpecialEffect == 1 && forestToPillai.Y == 559 && forestToPillai.DestinationY == 449,
            "Forever Fall to Pillai special portal is incomplete");
        var pillaiWarps = catalog.GetField("pilai").WarpGates;
        Assert.True(pillaiWarps.Count == 2, "Pillai is missing an original warp gate");
        Assert.True(pillaiWarps.Any(static warp => warp.SpecialEffect == 1 && warp.DestinationFieldId == "forever-fall-01"), "Pillai return effect portal is missing");
        Assert.True(pillaiWarps.Any(static warp => warp.SpecialEffect == 2 && warp.DestinationFieldId == "pilai"), "Pillai town-warp selector gate is missing");
        return Task.CompletedTask;
    }

    private static Task FieldAmbientSelectionAsync()
    {
        var catalog = new WorldCatalog();
        Assert.True(FieldAudioController.SelectFieldAmbientCode(catalog.GetField("village-2"), 12f) == 0, "Ricarten town ambience is wrong");
        Assert.True(FieldAudioController.SelectFieldAmbientCode(catalog.GetField("fore-1"), 3.99f) == 2, "forest night did not continue until 04:00");
        Assert.True(FieldAudioController.SelectFieldAmbientCode(catalog.GetField("fore-1"), 4f) == 1, "forest day did not begin at 04:00");
        Assert.True(FieldAudioController.SelectFieldAmbientCode(catalog.GetField("fore-1"), 23f) == 2, "forest night did not begin at 23:00");
        Assert.True(FieldAudioController.SelectFieldAmbientCode(catalog.GetField("ruin-1"), 12f) == 3, "ruin wind ambience is wrong");
        Assert.True(FieldAudioController.SelectFieldAmbientCode(catalog.GetField("De-1"), 12f) == 4, "desert wind ambience is wrong");
        Assert.True(FieldAudioController.SelectFieldAmbientCode(catalog.GetField("dun-1"), 12f) == 5, "early dungeon ambience is wrong");
        Assert.True(FieldAudioController.SelectFieldAmbientCode(catalog.GetField("dun-4"), 12f) == 6, "temple ambience is wrong");
        Assert.True(FieldAudioController.SelectFieldAmbientCode(catalog.GetField("Tcave"), 12f) == 7, "cave ambience is wrong");
        Assert.True(FieldAudioController.SelectFieldAmbientCode(catalog.GetField("Dcave"), 12f) == 8, "dark sanctuary ambience is wrong");
        Assert.True(FieldAudioController.SelectFieldAmbientCode(catalog.GetField("dun-7"), 12f) == (int)FieldAmbience.Dungeon, "a field's own ambience lost to its state's");
        Assert.True(FieldAudioController.SelectFieldAmbientCode(catalog.GetField("HeartOfFire"), 12f) == (int)FieldAmbience.DarkSanctuary
            && FieldAudioController.SelectFieldAmbientCode(catalog.GetField("CrystalNest"), 12f) == (int)FieldAmbience.IceWind,
            "a later field plays another loop than its own");
        foreach (var field in catalog.AllFields.Where(static field => field.State is FieldState.Dungeon or FieldState.Action))
        {
            var code = FieldAudioController.SelectFieldAmbientCode(field, 12f);
            Assert.True(code >= 0 || field.Ambients.Count > 0, $"{field.Id} plays no background loop");
        }

        Assert.True(Enum.GetValues<FieldAmbience>().Length == 13, "the field ambience names and the client's loops differ in number");
        Assert.True(FieldAudioController.SelectFieldAmbientCode(catalog.GetField("iron-1"), 12f) == 9, "iron ambience is wrong");
        Assert.True(FieldAudioController.SelectFieldAmbientCode(catalog.GetField("ice1"), 12f) == 12, "ice wind ambience is wrong");
        Assert.True(FieldAudioController.SelectFieldAmbientCode(catalog.GetField("castle"), 12f) == -1, "castle gained an invented ambience");
        return Task.CompletedTask;
    }

    private static Task NpcSoundCodesAsync()
    {
        Assert.True(CharacterSoundCodes.Resolve("WOLVERLIN") == CharacterSoundCodes.Wolverine, "Wolverine NPC bank resolved to the earlier monster duplicate");
        Assert.True(CharacterSoundCodes.Resolve("fury") == CharacterSoundCodes.Fury, "NPC sound lookup became case-sensitive");
        Assert.True(CharacterSoundCodes.Resolve("SKILLMASTER") == CharacterSoundCodes.SkillMaster, "skill-master NPC bank was not preserved");
        Assert.True(CharacterSoundCodes.Resolve("unknown-bank") == 0, "unknown NPC bank invented a sound code");
        return Task.CompletedTask;
    }

    private static Task RicartenMachineryAmbienceAsync()
    {
        var ricarten = new WorldCatalog().GetField("village-2");
        Assert.True(ricarten.Ambients.Count == 2, "Ricarten machinery ambience sources are incomplete");
        Assert.True(ricarten.Ambients[0] == new FieldAmbient(new FieldVector(-1006, 170, -17835), 80, 27), "store-cart ambience differs from the original field table");
        Assert.True(ricarten.Ambients[1] == new FieldAmbient(new FieldVector(2632, 321, -17285), 80, 27), "mechanical-windmill ambience differs from the original field table");

        return Task.CompletedTask;
    }

    private static Task NpcTalkSoundCodeAsync()
    {
        Assert.True(TalkSound.GetSoundCode("Hello") == 285541489u, "ASCII talk checksum differs from the classic implementation");
        Assert.True(TalkSound.GetSoundCode("크릉.. 크릉") == 4127925336u, "CP949 Wolverine talk checksum differs from the classic implementation");
        Assert.True(TalkSound.GetSoundCode("요즘 말이지") == 2534093287u, "CP949 NPC talk checksum differs from the classic implementation");
        return Task.CompletedTask;
    }

    private static Task NpcProximityVolumeAsync()
    {
        var origin = Vector3.Zero;
        Assert.Near(1f, NpcAudioController.ProximityVolume(origin, origin), 0.0001f);

        var finalAudible = NpcAudioController.ProximityVolume(new Vector3(MathF.Sqrt(399f * 1100f), 0f, 0f), origin);
        Assert.Near(1f / 400f, finalAudible, 0.0001f);

        var silent = NpcAudioController.ProximityVolume(new Vector3(MathF.Sqrt(400f * 1100f), 0f, 0f), origin);
        Assert.Near(0f, silent, 0.0001f);
        return Task.CompletedTask;
    }

    private static byte[] CreateWaveHeader(string signature, uint declaredSize = 4)
    {
        var bytes = new byte[12];
        Encoding.ASCII.GetBytes(signature).CopyTo(bytes, 0);
        BitConverter.TryWriteBytes(bytes.AsSpan(4, 4), declaredSize);
        "WAVE"u8.CopyTo(bytes.AsSpan(8, 4));
        return bytes;
    }

    private static void AssertInvalid(Action operation)
    {
        var rejected = false;
        try
        {
            operation();
        }
        catch (InvalidDataException)
        {
            rejected = true;
        }

        Assert.True(rejected, "invalid WAVE container was accepted");
    }
}
