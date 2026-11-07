namespace PristonTale.Persistence.Abstractions.WorldData;

// Id: the importer's stable monster id, also the runtime monster code; Key: its unique English key; Name: its selected-
// language name (monster_texts), null without one.
public sealed record MonsterRecord(
    int Id,
    string Key,
    string? Name,
    string Model,
    int Level,
    int Hp,
    int AttackMin,
    int AttackMax,
    int AttackSpeed,
    int AttackRange,
    int Accuracy,
    int Defence,
    int AbsorbPct,
    int BlockPct,
    int StunPct,
    int Sight,
    int SkillDamageMin,
    int SkillDamageMax,
    int SkillDistance,
    int SkillRange,
    int SkillRatingPct,
    int SkillCurseCode,
    int MoveSpeed,
    int MoveRange,
    int Iq,
    int GroupMin,
    int GroupMax,
    long Exp,
    int ActiveHour,
    short Nature,
    short Brood,
    short Class,
    bool Hostile,
    int ResBionic,
    int ResEarth,
    int ResFire,
    int ResIce,
    int ResLightning,
    int ResPoison,
    int ResWater,
    int ResWind,
    int ResMagic,
    int SizeLevel,
    int SoundCode,
    float ModelScale,
    int PortraitOffsetY,
    int PortraitOffsetZ,
    int QuestCode,
    string? EventItem,
    int PotionCount,
    int PotionPercent,
    int MoveType,
    int SpecialAttackPct,
    int GoldMin,
    int GoldMax,
    int GoldPct,
    string EventModel,
    int EventCode,
    int EventInfo,
    int MaxDropsPerKill,
    bool DropsSeenByAll)
{
    public IReadOnlyList<MonsterDropLineRecord> DropLines { get; init; } = [];

    public IReadOnlyList<MonsterBonusDropRecord> BonusDrops { get; init; } = [];
}

// One authored drop line: its weight and nothing, gold (GoldMin..GoldMax) or items (Items, the weight shared by those
// the catalog knows; a null code is an item the catalog lacks, which takes no share).
public sealed record MonsterDropLineRecord(
    int MonsterId,
    int Line,
    int Weight,
    int Kind,
    int GoldMin,
    int GoldMax)
{
    public IReadOnlyList<string?> Items { get; init; } = [];
}

public sealed record MonsterDropLineItemRecord(
    int MonsterId,
    int Line,
    int Ordinal,
    string? ItemCode);

// An independent extra drop: Chance percent of ItemCode (null when the catalog lacks it).
public sealed record MonsterBonusDropRecord(
    int MonsterId,
    int Ordinal,
    int Chance,
    string? ItemCode);
