using PristonTale.Simulation.Characters;
using PristonTale.Simulation.Items;

namespace PristonTale.WorldImport.Parsers;

// Parses one OpenItem .txt → ParsedItem with the original item-definition rules.
// Each line's leading word is matched exactly (case-sensitive, ordinal) against
// the KeyMaps item tokens. The English name is the *Name label, then *E_NAME, then *B_NAME, then the *이름 name when it
// is not Korean (a few items carry their only English name there), then the item code.
// Item identity and foreign keys remain the original item code.
public sealed class ItemTxtParser
{
    public ParsedItem Parse(string path)
    {
        var lines = SourceTextReader.ReadChain(path);

        var code = "";
        var labels = new Dictionary<string, string>(StringComparer.Ordinal);
        var identityName = "";
        long unique = 0, jobMask = 0;
        int durMin = 0, durMax = 0, weight = 0, price = 0;
        int dmgMinLow = 0, dmgMinHigh = 0, dmgMaxLow = 0, dmgMaxHigh = 0;
        int arMin = 0, arMax = 0, crit = 0, defMin = 0, defMax = 0;
        float blockMin = 0, blockMax = 0, absorbMin = 0, absorbMax = 0, speedMin = 0, speedMax = 0;
        float manaRegMin = 0, manaRegMax = 0, lifeRegMin = 0, lifeRegMax = 0, staRegMin = 0, staRegMax = 0;
        int addManaMin = 0, addManaMax = 0, addLifeMin = 0, addLifeMax = 0, addStaMin = 0, addStaMax = 0;
        int reqLevel = 0, reqStr = 0, reqSpi = 0, reqTal = 0, reqDex = 0, reqHea = 0;
        int resStaMin = 0, resStaMax = 0, resManaMin = 0, resManaMax = 0, resLifeMin = 0, resLifeMax = 0;
        int shootRange = 0, atkSpeed = 0, maxStack = 1, potionSpace = 0;
        float magicMasteryMin = 0, magicMasteryMax = 0;

        var resMin = new int[8];
        var resMax = new int[8];
        var uniqueColor = new int[4];
        int uniqueBlink = 0, displayEffect = 0, dailyLimit = 0, setCode = 0, setPieces = 0;
        float uniqueScale = 0f;
        string setBonus = "", lastComment = "";
        var unknown = new List<string>();
        var jobRandom = new List<long>();
        var specials = new List<(ItemSpecBonusKind, float, float)>();

        foreach (var sourceLine in lines)
        {
            var line = sourceLine.Text;
            var trimmed = line.TrimStart();
            if (trimmed.StartsWith("//", StringComparison.Ordinal) && !trimmed.StartsWith("///", StringComparison.Ordinal))
            {
                lastComment = trimmed[2..].Trim();
            }

            var key = Tokenizer.LeadingKey(line);
            if (key.Length == 0)
                continue;
            var t = new Tokenizer(line);
            t.NextWord(); // consume the key
            if (AuthoredLabels.TryRead(key, t, labels))
            {
                continue;
            }

            // Resistances first (they share token text with damage/etc only across parsers, not within).
            var resHit = false;
            foreach (var (token, element) in KeyMaps.ItemResistances)
            {
                if (key == token)
                {
                    resMin[element] = t.NextInt();
                    resMax[element] = t.NextInt();
                    resHit = true;
                    break;
                }
            }
            if (resHit)
                continue;

            switch (key)
            {
                case KeyMaps.ItemName:
                    identityName = t.NextValueOrQuoted().Trim();
                    break;
                case KeyMaps.ItemCode: code = t.NextValueOrQuoted(); break;
                case KeyMaps.ItemUniqueColor:
                    for (var channel = 0; channel < 4; channel++)
                    {
                        uniqueColor[channel] = t.NextInt();
                    }

                    uniqueBlink = t.NextInt();
                    uniqueScale = t.AtEnd ? 0f : t.NextFloat();
                    break;
                case KeyMaps.ItemDisplayEffect: displayEffect = t.NextInt(); break;
                case KeyMaps.ItemDailyLimit: dailyLimit = t.NextInt(); break;
                case KeyMaps.ItemSet:
                    setCode = t.NextInt();
                    setPieces = t.NextInt();
                    setBonus = lastComment;
                    break;
                case KeyMaps.ItemLinkedFile:
                    break;
                case KeyMaps.ItemUnique: unique = t.AtEnd ? 1 : t.NextInt(); break;
                case KeyMaps.ItemDurability: durMin = t.NextInt(); durMax = t.NextInt(); break;
                case KeyMaps.ItemWeight: weight = t.NextInt(); break;
                case KeyMaps.ItemPrice: price = t.NextInt(); break;
                case "*최대수량": maxStack = Math.Max(1, t.NextInt()); break;
                case "*보유공간": potionSpace = t.NextInt(); break;
                case "*마법숙련도":
                case "*마법기술숙련도":
                case "*기술숙련도":
                    magicMasteryMin = t.NextFloat();
                    magicMasteryMax = t.AtEnd ? 0f : t.NextFloat();
                    break;
                case KeyMaps.ItemDamage:
                    dmgMinLow = t.NextInt(); dmgMinHigh = t.NextInt();
                    dmgMaxLow = t.NextInt(); dmgMaxHigh = t.NextInt(); break;
                case KeyMaps.ItemShootingRange: shootRange = t.NextInt(); break;
                case KeyMaps.ItemAttackSpeed: atkSpeed = t.NextInt(); break;
                case KeyMaps.ItemAttackRating: arMin = t.NextInt(); arMax = t.NextInt(); break;
                case KeyMaps.ItemCritical: crit = t.NextInt(); break;
                case KeyMaps.ItemAbsorb: absorbMin = t.NextFloat(); absorbMax = t.NextFloat(); break;
                case KeyMaps.ItemDefence: defMin = t.NextInt(); defMax = t.NextInt(); break;
                case KeyMaps.ItemBlock: blockMin = t.NextFloat(); blockMax = t.NextFloat(); break;
                case KeyMaps.ItemSpeed: speedMin = t.NextFloat(); speedMax = t.NextFloat(); break;
                case KeyMaps.ItemReqLevel: reqLevel = t.NextInt(); break;
                case KeyMaps.ItemReqStrength: reqStr = t.NextInt(); break;
                case KeyMaps.ItemReqSpirit: reqSpi = t.NextInt(); break;
                case KeyMaps.ItemReqTalent: reqTal = t.NextInt(); break;
                case KeyMaps.ItemReqDexterity: reqDex = t.NextInt(); break;
                case KeyMaps.ItemReqHealth: reqHea = t.NextInt(); break;
                case KeyMaps.ItemJobMask:
                    jobMask = LastJob(t.RemainingWords(), jobMask);
                    break;
                case KeyMaps.ItemJobRandom:
                    foreach (var w in t.RemainingWords())
                    {
                        // Names of classes the game does not have are skipped rather than stored as an empty class.
                        var job = JobBitFor(w);
                        if (job != 0)
                        {
                            jobRandom.Add(job);
                        }
                    }

                    break;
                default:
                    if (!HandleAliasGroups(key, t,
                        ref manaRegMin, ref manaRegMax, ref lifeRegMin, ref lifeRegMax, ref staRegMin, ref staRegMax,
                        ref addManaMin, ref addManaMax, ref addLifeMin, ref addLifeMax, ref addStaMin, ref addStaMax,
                        ref resStaMin, ref resStaMax, ref resManaMin, ref resManaMax, ref resLifeMin, ref resLifeMax,
                        specials))
                    {
                        unknown.Add(key);
                    }

                    break;
            }
        }

        var item = new ParsedItem
        {
            Code = code,
            Name = EnglishNames.Item(code, EnglishName(labels, identityName, code)),
            Kind = ItemKinds.Classify(code),
            UniqueItem = unique,
            MaxStack = maxStack,
            PotionSpace = potionSpace,
            MagicMasteryMin = magicMasteryMin,
            MagicMasteryMax = magicMasteryMax,
            DurabilityMin = durMin, DurabilityMax = durMax,
            Weight = weight, Price = price,
            DamageMinLow = dmgMinLow, DamageMinHigh = dmgMinHigh,
            DamageMaxLow = dmgMaxLow, DamageMaxHigh = dmgMaxHigh,
            AttackRatingMin = arMin, AttackRatingMax = arMax,
            CriticalHit = crit, DefenceMin = defMin, DefenceMax = defMax,
            BlockMin = blockMin, BlockMax = blockMax,
            AbsorbMin = absorbMin, AbsorbMax = absorbMax,
            SpeedMin = speedMin, SpeedMax = speedMax,
            ManaRegenMin = manaRegMin, ManaRegenMax = manaRegMax,
            LifeRegenMin = lifeRegMin, LifeRegenMax = lifeRegMax,
            StaminaRegenMin = staRegMin, StaminaRegenMax = staRegMax,
            AddManaMin = addManaMin, AddManaMax = addManaMax,
            AddLifeMin = addLifeMin, AddLifeMax = addLifeMax,
            AddStaminaMin = addStaMin, AddStaminaMax = addStaMax,
            ReqLevel = reqLevel, ReqStrength = reqStr, ReqSpirit = reqSpi,
            ReqTalent = reqTal, ReqDexterity = reqDex, ReqHealth = reqHea,
            RestoreStaminaMin = resStaMin, RestoreStaminaMax = resStaMax,
            RestoreManaMin = resManaMin, RestoreManaMax = resManaMax,
            RestoreLifeMin = resLifeMin, RestoreLifeMax = resLifeMax,
            ShootingRange = shootRange, AttackSpeed = atkSpeed,
            JobMask = jobMask,
            UniqueBlink = uniqueBlink, UniqueScale = uniqueScale, DisplayEffect = displayEffect, DailyDropLimit = dailyLimit,
            SetCode = setCode, SetPieces = setPieces, SetBonus = setCode != 0 ? setBonus : "",
        };
        Array.Copy(uniqueColor, item.UniqueColor, 4);
        item.UnknownKeys.AddRange(unknown);
        Array.Copy(resMin, item.ResMin, 8);
        Array.Copy(resMax, item.ResMax, 8);
        item.JobRandom.AddRange(jobRandom);
        item.Specials.AddRange(specials);
        return item;
    }

    private static string EnglishName(IReadOnlyDictionary<string, string> labels, string identityName, string code)
    {
        var label = AuthoredLabels.First(labels, "*NAME", "*E_NAME", "*B_NAME");
        if (label.Length > 0)
        {
            return label;
        }

        return identityName.Length > 0 && !AuthoredLabels.IsKorean(identityName) ? identityName : code;
    }

    // The dual-alias regen/add/restore tokens, plus the ** special bonuses (commented out in this dataset).
    private static bool HandleAliasGroups(
        string key, Tokenizer t,
        ref float manaRegMin, ref float manaRegMax, ref float lifeRegMin, ref float lifeRegMax,
        ref float staRegMin, ref float staRegMax,
        ref int addManaMin, ref int addManaMax, ref int addLifeMin, ref int addLifeMax,
        ref int addStaMin, ref int addStaMax,
        ref int resStaMin, ref int resStaMax, ref int resManaMin, ref int resManaMax,
        ref int resLifeMin, ref int resLifeMax,
        List<(ItemSpecBonusKind, float, float)> specials)
    {
        if (Match(key, KeyMaps.ItemManaRegen)) { manaRegMin = t.NextFloat(); manaRegMax = t.NextFloat(); return true; }
        if (Match(key, KeyMaps.ItemLifeRegen)) { lifeRegMin = t.NextFloat(); lifeRegMax = t.NextFloat(); return true; }
        if (Match(key, KeyMaps.ItemStaminaRegen)) { staRegMin = t.NextFloat(); staRegMax = t.NextFloat(); return true; }
        if (Match(key, KeyMaps.ItemAddMana)) { addManaMin = t.NextInt(); addManaMax = t.NextInt(); return true; }
        if (Match(key, KeyMaps.ItemAddLife)) { addLifeMin = t.NextInt(); addLifeMax = t.NextInt(); return true; }
        if (Match(key, KeyMaps.ItemAddStamina)) { addStaMin = t.NextInt(); addStaMax = t.NextInt(); return true; }
        if (Match(key, KeyMaps.ItemRestoreStamina)) { resStaMin = t.NextInt(); resStaMax = t.NextInt(); return true; }
        if (Match(key, KeyMaps.ItemRestoreMana)) { resManaMin = t.NextInt(); resManaMax = t.NextInt(); return true; }
        if (Match(key, KeyMaps.ItemRestoreLife)) { resLifeMin = t.NextInt(); resLifeMax = t.NextInt(); return true; }

        // ** special bonuses (rare/none in this build): store as a coded (bonus_code, v0, v1) child.
        // bonus_code = a stable index over the recognised ** tokens.
        if (key.StartsWith("**", StringComparison.Ordinal))
        {
            if (SpecialBonus(key) is { } bonus)
            {
                specials.Add((bonus, t.NextFloat(), t.NextFloat()));
                return true;
            }
        }

        return false;
    }

    private static bool Match(string key, string[] aliases) => Array.IndexOf(aliases, key) >= 0;

    // The specialization bonus of each ** token; null for a token that names none.
    private static ItemSpecBonusKind? SpecialBonus(string key) => key switch
    {
        "**흡수력" => ItemSpecBonusKind.Absorb,
        "**방어력" => ItemSpecBonusKind.Defence,
        "**이동속도" => ItemSpecBonusKind.MoveSpeed,
        "**블럭율" => ItemSpecBonusKind.Block,
        "**공격속도" => ItemSpecBonusKind.AttackSpeed,
        "**크리티컬" => ItemSpecBonusKind.Critical,
        "**사정거리" => ItemSpecBonusKind.ShootingRange,
        "**마법숙련도" => ItemSpecBonusKind.MagicMastery,
        "**마나추가" or "**기력추가" => ItemSpecBonusKind.LevelMana,
        "**라이프추가" or "**생명력추가" => ItemSpecBonusKind.LevelLife,
        "**명중력" => ItemSpecBonusKind.LevelAttackRating,
        "**공격력" => ItemSpecBonusKind.LevelDamage,
        "**마나재생" or "**기력재생" => ItemSpecBonusKind.ManaRegen,
        "**라이프재생" or "**생명력재생" => ItemSpecBonusKind.LifeRegen,
        "**스테미나재생" or "**근력재생" => ItemSpecBonusKind.StaminaRegen,
        "**LifeRegenRange" => ItemSpecBonusKind.LifeRegenRange,
        _ => null,
    };

    // A fixed specialization names one class; when several are written the last known one wins.
    private static long LastJob(IReadOnlyList<string> words, long current)
    {
        var mask = current;
        foreach (var w in words)
        {
            var job = JobBitFor(w);
            if (job != 0)
            {
                mask = job;
            }
        }

        return mask;
    }

    // Map a job name (Korean or English) to its JobBitCode.
    // The original matches the job table names; we accept the English class names + the canonical Korean.
    private static uint JobBitFor(string name) => name.Trim().ToLowerInvariant() switch
    {
        "fighter" or "전사" => (uint)JobBitCode.Fighter,
        "mechanician" or "mechanic" or "기계공" => (uint)JobBitCode.Mechanician,
        "archer" or "궁수" => (uint)JobBitCode.Archer,
        "pikeman" or "창병" => (uint)JobBitCode.Pikeman,
        "atalanta" or "atalanter" or "아탈란타" => (uint)JobBitCode.Atalanta,
        "knight" or "기사" => (uint)JobBitCode.Knight,
        "magician" or "magic" or "마법사" => (uint)JobBitCode.Magician,
        "priestess" or "priest" or "사제" => (uint)JobBitCode.Priestess,
        "assassin" => (uint)JobBitCode.Assassin,
        "shaman" => (uint)JobBitCode.Shaman,
        "martialartist" or "martial artist" or "martial" => (uint)JobBitCode.MartialArtist,
        _ => 0u,
    };
}
