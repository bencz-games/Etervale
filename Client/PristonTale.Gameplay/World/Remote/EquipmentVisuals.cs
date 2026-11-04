using Microsoft.Xna.Framework;
using PristonTale.Contracts.Characters;
using PristonTale.Contracts.Units;
using PristonTale.Simulation.Characters;

namespace PristonTale.Gameplay.World.Remote;

public static class EquipmentVisuals
{
    private static readonly Dictionary<string, (int Index, string Mesh, bool LeftHand, string VillageBone)> Items = BuildItems();

    // A class that carries a family otherwise than the catalog's hand and village bone: the Assassin holds a dagger in
    // each hand, the left one on its fifth weapon bone, and hangs the two on its back in a village; the Shaman's phantom
    // floats at its back, drawn or not (Hovers). A null twin bone holds the weapon once.
    private static readonly (PlayerClass Class, string Family, string Held, string Back, string TwinHeld, string TwinBack, bool Hovers)[] Carries =
    [
        (PlayerClass.Assassin, "WD1", "Bip weapon01", "Bip in07", "Bip weapon05", "Bip in08", false),
        (PlayerClass.Shaman, "WN1", "Bip in01", "Bip in01", null, null, true)
    ];

    // A hovering weapon turns 2250 angle units about its bone's Y axis and moves 10 back along it, then rises 1000 fixed
    // units (the bone frames are mirrored in Z, so the turn runs the other way).
    private static readonly Matrix HoverOffset =
        Matrix.CreateRotationY(-2250 * GameUnits.RadiansPerAngle) * Matrix.CreateTranslation(0f, 0f, -10f);

    private const float HoverLift = 1000f / GameUnits.FixedScale;

    private static Dictionary<string, (int, string, bool, string)> BuildItems()
    {
        var result = new Dictionary<string, (int, string, bool, string)>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in EquipmentVisualTables.Items)
        {
            result.TryAdd(item.Key, (item.MotionIndex, item.Mesh, item.LeftHand, item.VillageBone));
        }

        return result;
    }

    public static IEnumerable<string> CatalogKeys => Items.Keys;

    public static bool HasModel(string key) => Items.ContainsKey(key);

    public static int MotionIndex(string key) => Items.TryGetValue(key, out var item) ? item.Index : -1;

    public static string MeshPath(string key)
    {
        if (!Items.TryGetValue(key, out var item))
        {
            throw new InvalidDataException($"No original equipment resource for '{key}'.");
        }

        return $"image/sinimage/Items/DropItem/it{item.Mesh}.smd";
    }

    public static CharacterVisualSet Apply(CharacterVisualSet defaults, string armor, string weapon, string offHand, bool village, uint jobMask)
    {
        string body = defaults.Body.InxExtensionPath;
        // Premium costume bodies (CA) keep the class body; their wings are an effect (CostumeWings). An armour without a
        // body in the table also keeps it, so one unknown item cannot stop the frame that draws its wearer.
        if (!string.IsNullOrEmpty(armor) && !armor.StartsWith("CA", StringComparison.OrdinalIgnoreCase)
            && EquipmentVisualTables.ArmorSlots.TryGetValue(armor, out var index))
        {
            if ((uint)defaults.ArmorTableIndex >= (uint)EquipmentVisualTables.Armors.Length)
            {
                throw new InvalidDataException($"Invalid armor table index {defaults.ArmorTableIndex}.");
            }

            body = "char/tmABCD/" + EquipmentVisualTables.Armors[defaults.ArmorTableIndex][index];
        }

        return defaults with
        {
            Body = new CharacterAssetRef(body),
            WeaponKey = weapon ?? "",
            OffHandKey = offHand?.StartsWith("OM1", StringComparison.OrdinalIgnoreCase) == true ? "" : offHand ?? "",
            InVillage = village,
            JobMask = jobMask
        };
    }

    // True when the item is held in the left hand (bows); false for the right hand or an unknown key.
    public static bool HeldInLeftHand(string key) => Items.TryGetValue(key, out var item) && item.LeftHand;

    public static string WeaponBone(CharacterVisualSet visuals, bool village) =>
        Carry(visuals) is { } carry ? village ? carry.Back : carry.Held : WeaponBone(visuals.WeaponKey, village);

    // How the weapon sits off its bone, or null when it sits on it.
    public static (Matrix Offset, float Lift)? WeaponHover(CharacterVisualSet visuals) =>
        Carry(visuals) is { Hovers: true } ? (HoverOffset, HoverLift) : null;

    // The bone of the weapon's second copy, or null when the wearer holds it once.
    public static string TwinBone(CharacterVisualSet visuals, bool village) =>
        Carry(visuals) is { TwinHeld: not null } carry ? village ? carry.TwinBack : carry.TwinHeld : null;

    private static (PlayerClass Class, string Family, string Held, string Back, string TwinHeld, string TwinBack, bool Hovers)? Carry(CharacterVisualSet visuals)
    {
        if (!Items.ContainsKey(visuals.WeaponKey))
        {
            return null;
        }

        foreach (var carry in Carries)
        {
            if (visuals.JobMask == CharacterClassDef.JobMaskOf(carry.Class) && visuals.WeaponKey.StartsWith(carry.Family, StringComparison.OrdinalIgnoreCase))
            {
                return carry;
            }
        }

        return null;
    }

    public static string WeaponBone(string key, bool village)
    {
        if (!Items.TryGetValue(key, out var item))
        {
            throw new InvalidDataException($"No original equipment hand for '{key}'.");
        }

        if (village && item.VillageBone.Length > 0)
        {
            return item.VillageBone;
        }

        return item.LeftHand ? "Bip01 L Hand" : "Bip weapon01";
    }
}
