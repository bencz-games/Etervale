using PristonTale.Engine3D.Equipment;

namespace PristonTale.Engine3D.Formats;

// The names the lines of a model descriptor (PristonModelText) give: the weapons a motion is worn with (item
// categories, exact spelling, as the motion index the character motion files list), the classes it plays for (any
// case, bit class - 1) and the skills it plays (any case, the code the model-info file stores).
public static class PristonModelTextNames
{
    private static readonly Dictionary<string, int> Items = BuildItems();

    private static readonly (string Name, uint Bit)[] Jobs =
    [
        ("Fighter", 0x0001), ("Mechanician", 0x0002), ("Archer", 0x0004), ("Pikeman", 0x0008), ("Atalanta", 0x0010),
        ("Knight", 0x0020), ("Magician", 0x0040), ("Priest", 0x0080), ("Assassin", 0x0100), ("Shaman", 0x0200),
        ("MartialArtist", 0x0400)
    ];

    // In code order: code 0 names no skill.
    private static readonly string[] Skills =
    [
        "xxxxxxxxxxxxx", "ExtremeShield", "MechanicBomb", "PhysicalAbsorption", "PoisonAttribute", "roundimpact", "Maximize",
        "Automation", "Spark", "MetalArmor", "GrandSmash", "SparkShield", "IMPULSION", "COMPULSION", "MAGNETICSPHERE", "METALGOLEM",
        "MeleeMastery", "FireAttribute", "Raving", "Impact", "TripleImpact", "BrutalSwing", "Roar", "RageofZecram", "Concentration",
        "AvangingCrash", "SwiftAxe", "BoneSmash", "DESTROYER", "BERSERKER", "CYCLONESTRIKE", "PikeWind", "IceAttribute", "CriticalHit",
        "JumpingCrash", "GroundPike", "Tornado", "WeaponDefenceMastery", "Expansion", "VenomSpear", "BladeOfBlaze", "ChainLance",
        "ASSASSINEYE", "CHARGINGSTRIKE", "VAGUE", "SHADOWMASTER", "ScoutHawk", "ShootingMastery", "WindArrow", "PerfectAim", "DionsEye",
        "Falcon", "ArrowofRage", "Avalanche", "ElementalShot", "GoldenFalcon", "BombShot", "Perforation", "RECALLWOLVERIN",
        "PHOENIXSHOT", "FORCEOFNATURE", "SwordBlast", "HolyBody", "PhysicalTraning", "DoubleCrash", "holyteam", "Brandish", "Piercing",
        "DrasticSpirit", "FlameBrandish", "Divineinhalation", "HolyIncantation", "GrandCross", "DIVINEPIERCING", "DIVINEPIERCING2",
        "DIVINEPIERCING3", "GODLYSHIELD", "GODSBLESS", "SWORDOFJUSTICE", "ShieldStrike", "Farina", "ThrowingMastery", "VigorSpear",
        "Windy", "TwistJavelin", "SoulSucker", "FireJavelin", "SplitJavelin", "TriumphOfValhalla", "LightningJavelin", "StormJavelin",
        "HALLOFVALHALLA", "XRAGE", "FROSTJAVELIN", "VENGEANCE", "Healing", "HolyBolt", "MultiSpark", "HolyMind", "Meditation",
        "DivineLightning", "HolyReflection", "GrandHealing", "VigorBall", "Resurrection", "Extinction", "VirtualLife", "GLACIALSPIKE",
        "REGENERATIONFIELD", "CHAINLIGHTNING", "SUMMONMUSPELL", "Agony", "FireBolt", "Zenith", "FireBall", "MentalMastery",
        "ColumnOfWater", "EnchantWeapon", "DeadRay", "EnergyShield", "Diastrophism", "SpiritElemental", "DancingSword", "FIREELEMENTAL",
        "FLAMEWAVE", "DISTORTION", "METEO", "Stingger", "RunningHit", "DualSwordMastery", "Wisp", "VenomThrone", "Alas", "SoulShock",
        "AttackMastery", "SoreSword", "BeatUp", "Inpes", "Blind", "FrostWind", "CriticalMastery", "Polluted", "PastingShadow", "DarkBolt",
        "DarkWave", "CurseLazy", "InnerPeace", "SpiritualFlare", "SoulManacle", "ChasingHunt", "AdventMigal", "RainMaker",
        "LandOfGhost", "Haunt", "Scratch", "RecallBloodyKnight", "Judgement", "AdventMidranda", "MourningOfPray", "Lowkick",
        "StrengthMastery", "DoubleBackspinBlow", "HyperStraight", "RageUp", "Patriot", "RushElbow", "StaminaMastery", "InstantBulkUp",
        "TigerCannon", "WarCry", "JumpingHeelKick", "Combination", "Steelers", "BodyCheck", "Typhoon", "LandMining", "HyperSonic",
        "RollingSmash", "PowerEnhance", "DOWNHIT", "POWERDASH", "MOTALBLOW", "BLOODYBERSERKER", "DANCINGREAPER", "FINALSPEAR",
        "AMPLIFIED", "SIDESPINATTACK", "EVADESHOT", "THUNDERROOP", "NATURESPLASH", "CIRCLETRAP", "SOULBREAKER", "CRESCENTMOON",
        "SAINTBLADE", "HOLYBENEDIC", "TALARIA", "GALAXYCOUP", "SUMMONARCUDA", "SNIPPYFEAR", "SpiritImpact", "PiercingIce",
        "SummonRamiel", "BlessingKrishna", "Silraphim", "Vistenus", "Primaignis", "Thirdanima", "ShadowBomb", "RisingSlash",
        "ViolenceStab", "Storm", "Creed", "PressOfDeity", "GhostyNail", "HighRegeneration", "DefenceMastery", "HuntingHawk",
        "LineBreaking", "HardTraining"
    ];

    // -1 when no item has the category.
    public static int ItemIndex(string category) => Items.TryGetValue(category, out var index) ? index : -1;

    // 0 when no class has the name.
    public static uint JobBit(string name)
    {
        foreach (var (job, bit) in Jobs)
        {
            if (job.Equals(name, StringComparison.OrdinalIgnoreCase))
            {
                return bit;
            }
        }

        return 0;
    }

    // -1 when no skill has the name.
    public static int SkillCode(string name) => Array.FindIndex(Skills, skill => skill.Equals(name, StringComparison.OrdinalIgnoreCase));

    private static Dictionary<string, int> BuildItems()
    {
        var items = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var item in EquipmentVisualTables.Items)
        {
            items.TryAdd(item.Key, item.MotionIndex);
        }

        return items;
    }
}
