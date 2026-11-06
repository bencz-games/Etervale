using System.Collections.Concurrent;
using System.Globalization;
using System.Resources;
using PristonTale.Simulation.Characters;
using PristonTale.Simulation.Skills;
using PristonTale.Engine3D.Rendering;
using PristonTale.Contracts.Characters;

namespace PristonTale.Gameplay.Localization;

// The game data's texts in the shown language (ClientText.Use): skill names and descriptions and class titles. English
// is the data itself; DataText.<language>.resx holds a translation per key, and a text a language lacks shows as the
// data words it. Keys: Skill.<code as 8 hex digits>.Name and .Description, Class.<PlayerClass>.<tier>.
public static class DataText
{
    private static readonly ResourceManager Resources = new(typeof(DataText));
    private static readonly ConcurrentDictionary<uint, (string Name, string Description)> SkillKeys = new();

    public static string SkillName(uint code, string english) => Get(Keys(code).Name, english);

    public static string SkillDescription(uint code, string english) => Get(Keys(code).Description, english);

    public static string SkillName(Skill skill) => SkillName(skill.Code, skill.Name);

    public static string ClassTitle(PlayerClass playerClass, int tier)
    {
        var titles = ClassProfiles.Of(playerClass).Titles;
        var index = Math.Clamp(tier, 0, titles.Count - 1);
        return Get(ClassKey(playerClass, index), titles[index]);
    }

    public static string SkillKey(uint code, string part) => "Skill." + code.ToString("X8", CultureInfo.InvariantCulture) + "." + part;

    public static string ClassKey(PlayerClass playerClass, int tier) => "Class." + playerClass + "." + tier.ToString(CultureInfo.InvariantCulture);

    private static (string Name, string Description) Keys(uint code) =>
        SkillKeys.GetOrAdd(code, static key => (SkillKey(key, "Name"), SkillKey(key, "Description")));

    private static string Get(string key, string english) =>
        ClientText.Language == ClientSettings.EnglishLanguage ? english : Resources.GetString(key, ClientText.Culture) ?? english;
}
