using PristonTale.Contracts.Characters;
using PristonTale.Engine3D.Equipment;

namespace PristonTale.Engine3D.Formats;

// The names the lines of a model descriptor (PristonModelText) give: the weapons a motion is worn with (item
// categories, exact spelling, as the motion index the character motion files list), the classes it plays for (any
// case, PlayerClass names with Priest for the priestess, PlayerClasses.JobMask) and the skills it plays (any case, the
// code the model-info file stores).
public static class PristonModelTextNames
{
    private static readonly Dictionary<string, int> Items = BuildItems();

    // Descriptors name the priestess's class Priest.
    private const string PriestName = "Priest";

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
        if (name.Equals(PriestName, StringComparison.OrdinalIgnoreCase))
        {
            return PlayerClasses.JobMask(PlayerClass.Priestess);
        }

        return name.Length > 0 && char.IsLetter(name[0]) && Enum.TryParse<PlayerClass>(name, ignoreCase: true, out var playerClass)
            && Enum.IsDefined(playerClass) ? PlayerClasses.JobMask(playerClass) : 0u;
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
