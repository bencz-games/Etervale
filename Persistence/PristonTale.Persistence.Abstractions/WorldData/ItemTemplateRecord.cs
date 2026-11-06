namespace PristonTale.Persistence.Abstractions.WorldData;

// Which definition of an item a row of item_definitions is: the regular one, a named unique (Vega, Rage...) or the
// item's quest version.
public enum ItemVariant : short
{
    Regular = 0,
    NamedUnique = 1,
    Quest = 2
}

// One item definition (item_definitions joined with its item and selected-language name). Ranges are min/max as authored; a
// range whose upper value is 0 is a fixed value.
public sealed record ItemTemplateRecord(
    long Id,
    string Code,
    short Variant,
    int VariantOrdinal,
    string? Name,
    int Kind,
    int GridWidth,
    int GridHeight,
    int UniqueItem,
    long SpecClass,
    int DurabilityMin,
    int DurabilityMax,
    int Weight,
    int Price,
    int DamageMinLow,
    int DamageMinHigh,
    int DamageMaxLow,
    int DamageMaxHigh,
    int AttackRatingMin,
    int AttackRatingMax,
    int Critical,
    int DefenceMin,
    int DefenceMax,
    float BlockMin,
    float BlockMax,
    float AbsorbMin,
    float AbsorbMax,
    float MoveSpeedMin,
    float MoveSpeedMax,
    float LifeRegenMin,
    float LifeRegenMax,
    float ManaRegenMin,
    float ManaRegenMax,
    float StaminaRegenMin,
    float StaminaRegenMax,
    int AddLifeMin,
    int AddLifeMax,
    int AddManaMin,
    int AddManaMax,
    int AddStaminaMin,
    int AddStaminaMax,
    int ReqLevel,
    int ReqStrength,
    int ReqSpirit,
    int ReqTalent,
    int ReqAgility,
    int ReqHealth,
    int RestoreLifeMin,
    int RestoreLifeMax,
    int RestoreManaMin,
    int RestoreManaMax,
    int RestoreStaminaMin,
    int RestoreStaminaMax,
    int ShootingRange,
    int AttackSpeed,
    // Resistance min/max per element, in ResistanceBlock order (bionic..wind).
    int ResBionicMin, int ResBionicMax,
    int ResEarthMin, int ResEarthMax,
    int ResFireMin, int ResFireMax,
    int ResIceMin, int ResIceMax,
    int ResLightningMin, int ResLightningMax,
    int ResPoisonMin, int ResPoisonMax,
    int ResWaterMin, int ResWaterMax,
    int ResWindMin, int ResWindMax,
    int MaxStack,
    int PotionSpace,
    float MagicMasteryMin,
    float MagicMasteryMax,
    // The glow of a unique weapon: signed colour offsets, the pulse (2^blink ms each way, below 0 steady) and its scale.
    int GlowR,
    int GlowG,
    int GlowB,
    int GlowA,
    int GlowBlink,
    float GlowScale,
    int DisplayEffect,
    // Drops of the item per day across the server (0 unlimited); its set and the pieces worn for the set bonus.
    int DailyDropLimit,
    int? SetCode,
    int SetPieces)
{
    public IReadOnlyList<ItemSpecBonusRecord> SpecBonuses { get; init; } = [];

    // The classes a dropped copy may specialise for instead of SpecClass, in authored order.
    public IReadOnlyList<long> RandomSpecClasses { get; init; } = [];
}

public sealed record ItemSpecBonusRecord(
    long DefinitionId,
    short Bonus,
    float MinValue,
    float MaxValue);

public sealed record ItemSpecClassRecord(
    long DefinitionId,
    int Ordinal,
    long Class);
