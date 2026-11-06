using System.Globalization;
using System.Resources;
using PristonTale.Engine3D.Rendering;

namespace PristonTale.Gameplay.Localization;

public static class ClientText
{
    private static readonly ResourceManager Resources = new(typeof(ClientText));

    public static string Loading(string language) => Get("Loading", language);

    public static string SavingAndDisconnecting(string language) => Get("SavingAndDisconnecting", language);

    public static string CharacterSelect(string language) => Get("CharacterSelect", language);

    public static string Opening(string language) => Get("Opening", language);

    public static string SkillNotReady(string language) => Get("SkillNotReady", language);

    public static string NotEnoughMana(string language) => Get("NotEnoughMana", language);

    public static string NotEnoughStamina(string language) => Get("NotEnoughStamina", language);

    public static string LearnSkillFirst(string language) => Get("LearnSkillFirst", language);

    public static string PetDismissed(string language) => Get("PetDismissed", language);

    public static string SkillTargetMode(string language, string skillName, bool aggressive) =>
        string.Format(Get(aggressive ? "SkillTargetAggressive" : "SkillTargetFocused", language), skillName);

    private static string Get(string key, string language)
    {
        var culture = CultureInfo.GetCultureInfo(ClientSettings.NormalizeLanguage(language));
        return Resources.GetString(key, culture) ?? Resources.GetString(key, CultureInfo.InvariantCulture) ?? key;
    }
}
