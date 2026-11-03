using Microsoft.Xna.Framework;
using PristonTale.Engine3D.Diagnostics;
using PristonTale.Engine3D.IO;
using PristonTale.Gameplay.Effects;
using PristonTale.Simulation.Items;
using PristonTale.Assets;
using PristonTale.Contracts.Characters;
using PristonTale.Simulation.Characters;

namespace PristonTale.Gameplay.Audio;

// What a character sound folder's file plays for, by the start of its name.
public enum CharacterSoundCue
{
    Attack,
    Damage,
    Dead,
    Neutral,
    Move,
    Warp,
    Skill,
    Hammer
}

// How a player's swing ended, for its weapon sound.
public enum WeaponSoundOutcome
{
    Miss,
    Hit,
    Critical
}

// Character, weapon and footstep sounds as the original plays them: a random file of the character's sound folder for a
// motion (monsters by sound code, players by class), a weapon file by weapon family and hand for each swing, and the
// floor's footsteps. Folders are read when a character first needs them and decoded on a worker; a sound that is not
// ready yet is skipped, as the original loads monster sounds when they first play.
public sealed class CombatSounds : IDisposable
{
    private const string WeaponDirectory = "wav/Effects/weapon";
    private const string RestartSound = "wav/Effects/event/fieldstart.wav";

    // Weapon file name starts and their sound codes; the first start that matches a file names it.
    private static readonly (string Start, int Code)[] WeaponFiles =
    [
        ("one hand swing axe", 1), ("two hand swing axe", 2), ("one hand swing sword", 3), ("two hand swing sword", 4),
        ("one hand swing spear", 5), ("two hand swing spear", 6), ("one hand scratch", 7), ("bow shoot", 8),
        ("one hand swing blunt", 9), ("two hand swing blunt", 10), ("throwing", 11), ("one hand miss", 12), ("two hand miss", 13),
        ("punch hit", 14), ("one hand small swing", 15), ("critical hit", 16), ("one hand casting", 17), ("two hand casting", 18)
    ];

    private static readonly (string Start, CharacterSoundCue Cue)[] CharacterFiles =
    [
        ("attack", CharacterSoundCue.Attack), ("damage", CharacterSoundCue.Damage), ("dead", CharacterSoundCue.Dead),
        ("neutral", CharacterSoundCue.Neutral), ("move", CharacterSoundCue.Move), ("disappear", CharacterSoundCue.Warp),
        ("change", CharacterSoundCue.Warp), ("skill", CharacterSoundCue.Skill), ("hammer", CharacterSoundCue.Hammer)
    ];

    private static readonly Dictionary<int, string> Directories = CharacterSoundBanks.Directories.ToDictionary(bank => bank.Code, bank => bank.Directory);

    private readonly string _assetRoot;
    private readonly SkillSoundBank _bank;
    private readonly Dictionary<int, Dictionary<CharacterSoundCue, string[]>> _characters = new();
    private readonly Dictionary<int, string[]> _weapons;
    // Every character sound folder, read once on a worker: the game thread never enumerates a folder.
    private readonly Task<Dictionary<int, Dictionary<CharacterSoundCue, string[]>>> _characterIndex;

    public CombatSounds(string assetRoot, IClientLog log)
    {
        _assetRoot = assetRoot;
        _bank = new SkillSoundBank(assetRoot, log);
        _weapons = Group(Files(WeaponDirectory), WeaponFiles);
        _bank.Preload([.. _weapons.Values.SelectMany(paths => paths), .. FootstepFiles.SelectMany(files => files), RestartSound]);
        _characterIndex = Task.Run(() => Directories.ToDictionary(pair => pair.Key, pair => Group(Files(pair.Value), CharacterFiles)));
        _characterIndex.ContinueWith(task => log.Error($"[Audio] character sound index failed: {task.Exception}"), TaskContinuationOptions.OnlyOnFaulted);
    }

    public bool Enabled
    {
        get => _bank.Enabled;
        set => _bank.Enabled = value;
    }

    public void Update() => _bank.Update();

    // Starts reading a character's sound folder before it first plays (a monster entering view).
    public void Prepare(int soundCode) => Bank(soundCode);

    // Walking sounds are heard only close by (three quarters of full volume and more), as in the original. A monster
    // drawn smaller than its model plays higher, one drawn more than twice as large plays lower.
    public void PlayCharacter(int soundCode, CharacterSoundCue cue, Vector3 source, Vector3 listener, float scale = 1f)
    {
        if (soundCode == 0 || Bank(soundCode) is not { } bank || !bank.TryGetValue(cue, out var files))
        {
            return;
        }

        if (cue == CharacterSoundCue.Move && Volume(source, listener) < 0.75f)
        {
            return;
        }

        var pitch = scale <= 0f || MathF.Abs(scale - 1f) < 0.001f ? 0f : scale < 1f ? MathF.Log2(3600f / 2205f) : scale > 2f ? MathF.Log2(1400f / 2205f) : 0f;
        _bank.Play(files[Random.Shared.Next(files.Length)], source, listener, 1f, pitch);
    }

    // A player's swing: the miss sound of its hand, or the swing sound of its weapon family (punches without a weapon),
    // and the critical sound on top of a critical hit. Magicians and priestesses cast with staves.
    public void PlayWeapon(string weaponKey, PlayerClass playerClass, WeaponSoundOutcome outcome, Vector3 source, Vector3 listener)
    {
        var twoHanded = weaponKey.Length > 0 && ItemEquipmentMetadata.IsTwoHanded(weaponKey);
        var code = outcome == WeaponSoundOutcome.Miss ? twoHanded ? 13 : 12 : WeaponCode(weaponKey, twoHanded, playerClass);
        if (!PlayWeaponCode(code, source, listener))
        {
            return;
        }

        if (outcome == WeaponSoundOutcome.Critical && weaponKey.Length > 0)
        {
            PlayWeaponCode(16, source, listener);
        }
    }

    // The footsteps of the floor the local player walks on, at the original's fixed volume.
    public void PlayFootstep(FootstepFloor floor, int step)
    {
        var files = FootstepFiles[(int)floor];
        var file = floor == FootstepFloor.Water ? files[step % files.Length] : files[Random.Shared.Next(files.Length)];
        _bank.Play(file, Vector3.Zero, Vector3.Zero);
    }

    // The restart chime where a character rises again.
    public void PlayRestart(Vector3 source, Vector3 listener) => _bank.Play(RestartSound, source, listener);

    public void Dispose() => _bank.Dispose();

    internal static float Volume(Vector3 source, Vector3 listener) => SoundFalloff.Volume(source, listener);

    // The original's weapon sound code: axes, swords (the first three are small blades), spears, claws, bows, staves (cast
    // by magicians and priestesses, struck by others), hammers and throwing weapons; anything else punches.
    internal static int WeaponCode(string weaponKey, bool twoHanded, PlayerClass playerClass)
    {
        if (!ItemCodes.TryParse(weaponKey, out var code))
        {
            return 14;
        }

        return ItemEquipmentMetadata.KindOf(weaponKey) switch
        {
            ItemKind.Axe => twoHanded ? 2 : 1,
            ItemKind.Sword when ItemCodes.Ordinal(code) <= 3 => 15,
            ItemKind.Sword => twoHanded ? 4 : 3,
            ItemKind.Pole => twoHanded ? 6 : 5,
            ItemKind.Claw => 7,
            ItemKind.Bow => 8,
            ItemKind.Staff when ClassProfiles.CastsWithStaff(playerClass) => twoHanded ? 18 : 17,
            ItemKind.Staff or ItemKind.Hammer => twoHanded ? 10 : 9,
            ItemKind.ThrowingWeapon => 11,
            _ => 14
        };
    }

    private bool PlayWeaponCode(int code, Vector3 source, Vector3 listener)
    {
        if (!_weapons.TryGetValue(code, out var files))
        {
            return false;
        }

        _bank.Play(files[Random.Shared.Next(files.Length)], source, listener);
        return true;
    }

    // Null while the index is still being read: that sound is skipped rather than read on the game thread.
    private Dictionary<CharacterSoundCue, string[]> Bank(int soundCode)
    {
        if (_characters.TryGetValue(soundCode, out var bank))
        {
            return bank;
        }

        if (!_characterIndex.IsCompletedSuccessfully)
        {
            return null;
        }

        bank = _characterIndex.Result.GetValueOrDefault(soundCode) ?? [];
        _characters[soundCode] = bank;
        _bank.Preload(bank.Values.SelectMany(paths => paths));
        return bank;
    }

    // Each file under the key of the first prefix its name starts with; files matching none are left out.
    private static Dictionary<TKey, string[]> Group<TKey>(IEnumerable<string> paths, (string Start, TKey Key)[] prefixes)
        where TKey : notnull
    {
        var grouped = new Dictionary<TKey, List<string>>();
        foreach (var path in paths)
        {
            var name = Path.GetFileName(path);
            foreach (var (start, key) in prefixes)
            {
                if (name.StartsWith(start, StringComparison.OrdinalIgnoreCase))
                {
                    (grouped.TryGetValue(key, out var list) ? list : grouped[key] = []).Add(path);
                    break;
                }
            }
        }

        return grouped.ToDictionary(pair => pair.Key, pair => pair.Value.ToArray());
    }

    // The .wav files of a folder as asset-relative paths.
    private IEnumerable<string> Files(string relativeDirectory)
    {
        var directory = AssetFiles.Resolve(Path.Combine(_assetRoot, relativeDirectory));
        if (!Directory.Exists(directory))
        {
            return [];
        }

        return Directory.EnumerateFiles(directory, "*.wav", SearchOption.TopDirectoryOnly)
            .Order(StringComparer.OrdinalIgnoreCase)
            .Select(path => Path.GetRelativePath(_assetRoot, path).Replace('\\', '/'))
            .ToArray();
    }

    private static readonly string[][] FootstepFiles =
    [
        ["wav/Effects/FootSteps/step01.wav", "wav/Effects/FootSteps/step02.wav"],
        ["wav/Effects/FootSteps/stonerun1.wav", "wav/Effects/FootSteps/stonerun2.wav", "wav/Effects/FootSteps/stonerun3.wav"],
        ["wav/Effects/FootSteps/stone-dun1.wav", "wav/Effects/FootSteps/stone-dun2.wav", "wav/Effects/FootSteps/stone-dun3.wav"],
        ["wav/Effects/FootSteps/inwater 1.wav", "wav/Effects/FootSteps/inwater 2.wav", "wav/Effects/FootSteps/inwater 3.wav"]
    ];
}

// The floor under the local player's step: soft ground (forest, desert), stone (towns, ruins), dungeon stone, water.
public enum FootstepFloor
{
    Soft,
    Stone,
    Dungeon,
    Water
}
