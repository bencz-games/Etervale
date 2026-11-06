using System.Collections.Concurrent;
using System.Globalization;
using System.Resources;
using PristonTale.Simulation.Characters;
using PristonTale.Simulation.Skills;
using PristonTale.Engine3D.Rendering;
using PristonTale.Contracts.Characters;
using PristonTale.Simulation.Items.Usable;

namespace PristonTale.Gameplay.Localization;

// The game data's texts in the shown language (ClientText.Use): skill names and descriptions, class titles and quest
// window titles. English
// is the data itself; DataText.<language>.resx holds a translation per key, and a text a language lacks shows as the
// data words it. Keys: Skill.<code as 8 hex digits>.Name and .Description, Class.<PlayerClass>.<tier>, Quest.<board title>,
// Castle.<catalog key>.Effect.
public static class DataText
{
    private static readonly ResourceManager Resources = new(typeof(DataText));
    private static readonly ConcurrentDictionary<uint, (string Name, string Description)> SkillKeys = new();
    private static readonly ConcurrentDictionary<string, string> QuestKeys = new(StringComparer.Ordinal);

    public static string SkillName(uint code, string english) => Get(Keys(code).Name, english);

    public static string SkillDescription(uint code, string english) => Get(Keys(code).Description, english);

    public static string SkillName(Skill skill) => SkillName(skill.Code, skill.Name);

    public static string ClassTitle(PlayerClass playerClass, int tier)
    {
        var titles = ClassProfiles.Of(playerClass).Titles;
        var index = Math.Clamp(tier, 0, titles.Count - 1);
        return Get(ClassKey(playerClass, index), titles[index]);
    }

    // The effect line of a castle item.
    public static string CastleEffect(CastleItemDefinition castle) => Get(CastleKey(castle.CatalogKey), castle.Effect);

    public static string CastleKey(string catalogKey) => "Castle." + catalogKey + ".Effect";

    // The effect line under an item effect's name in the running-effects bar, or null.
    public static string StatusEffect(uint code)
    {
        if (CastleItems.TryGetByStatus(code, out var castle))
        {
            return CastleEffect(castle);
        }

        if (MonsterCrystals.TryGetByPetStatus(code, out _))
        {
            return ClientText.Get("Status.CrystalPet");
        }

        return ItemStatuses.TryGetPremiumKeep(code, out var kind) ? kind switch
        {
            PremiumEffectKind.LifeBooster => ClientText.Format("Status.LifeBooster", PremiumRules.BoosterPercent),
            PremiumEffectKind.ManaBooster => ClientText.Format("Status.ManaBooster", PremiumRules.BoosterPercent),
            PremiumEffectKind.StaminaBooster => ClientText.Format("Status.StaminaBooster", PremiumRules.BoosterPercent),
            _ => ClientText.Get("Status.SkillDelay")
        } : null;
    }

    // A title of the quest window, as the server sends it (QuestBoard).
    public static string QuestTitle(string english) => english.Length == 0 ? english : Get(QuestKeys.GetOrAdd(english, QuestKey), english);

    public static string QuestKey(string title) => "Quest." + title;

    public static string SkillKey(uint code, string part) => "Skill." + code.ToString("X8", CultureInfo.InvariantCulture) + "." + part;

    public static string ClassKey(PlayerClass playerClass, int tier) => "Class." + playerClass + "." + tier.ToString(CultureInfo.InvariantCulture);

    private static (string Name, string Description) Keys(uint code) =>
        SkillKeys.GetOrAdd(code, static key => (SkillKey(key, "Name"), SkillKey(key, "Description")));

    private static string Get(string key, string english) =>
        ClientText.Language == ClientSettings.EnglishLanguage ? english : Resources.GetString(key, ClientText.Culture) ?? english;
}
