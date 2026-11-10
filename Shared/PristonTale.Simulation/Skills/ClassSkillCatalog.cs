using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using PristonTale.Simulation.World.Monsters;
using PristonTale.Simulation.Characters;
using PristonTale.Simulation.Items;
using PristonTale.Contracts.Characters;

namespace PristonTale.Simulation.Skills;

// One object per class skill in Skills/Data/Classes/<Class>.json, in skill-window order (tier, then the window
// position that also gives the learning order and price). The same object holds the definition below and, when
// "kind" is present, every SkillRule field (see SkillRules.cs):
//   code         "0x01020001": class group (high byte) | tier bit (0x1, 0x2, 0x4, 0x8) << 16 | slot.
//   name, description, icon (skill-window button), tier ("Job1".."Job4"), category (SkillCategory).
//   requiredLevel, mouse ("Left", "Right", "Both", "None"), mana (ten ranks), stamina ([base, per rank]),
//   weapons (ItemKind names the main hand or the off-hand must hold; empty allows any).
// An entry without "kind" is a definition the server cannot cast yet.
public sealed record ClassSkillEntry
{
    public uint Code { get; init; }
    public string Name { get; init; } = "";
    public string Description { get; init; } = "";
    public string Icon { get; init; } = "";
    public JobTier Tier { get; init; }
    public SkillCategory Category { get; init; }
    public int RequiredLevel { get; init; }
    public SkillMouseUse Mouse { get; init; }
    public int[] Mana { get; init; } = [];
    public int[] Stamina { get; init; } = [];
    public string[] Weapons { get; init; } = [];
    public SkillKind? Kind { get; init; }
}

public static class ClassSkillCatalog
{
    private const string ResourcePrefix = "PristonTale.Simulation.Skills.Data.Classes.";
    private static readonly Type[] EntryTypes = [typeof(ClassSkillEntry), typeof(SkillRule)];

    private static readonly Catalog Loaded = Load();

    public static IReadOnlyList<Skill> For(PlayerClass playerClass) =>
        Loaded.Skills.TryGetValue(playerClass, out var skills) ? skills : Array.Empty<Skill>();

    public static IEnumerable<Skill> All => Loaded.Skills.Values.SelectMany(static skills => skills);

    public static bool TryGet(uint code, out Skill skill) => Loaded.ByCode.TryGetValue(code, out skill);

    internal static IReadOnlyDictionary<uint, SkillRule> Rules => Loaded.Rules;

    // Parses one class file into its skills and the rules of the castable and passive entries.
    public static IReadOnlyList<Skill> Parse(string json, out IReadOnlyList<SkillRule> rules)
    {
        using var document = JsonDocument.Parse(json, new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
        if (document.RootElement.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidDataException("A class skill file must be an array of skills.");
        }

        var skills = new List<Skill>();
        var parsedRules = new List<SkillRule>();
        foreach (var element in document.RootElement.EnumerateArray())
        {
            SkillJson.RequireKnownProperties(element, EntryTypes, element.TryGetProperty("name", out var name) ? name.ToString() : "skill");
            var entry = element.Deserialize<ClassSkillEntry>(SkillJson.Options)
                ?? throw new InvalidDataException("A class skill entry is empty.");
            skills.Add(ToSkill(entry));
            if (entry.Kind is not null)
            {
                var rule = element.Deserialize<SkillRule>(SkillJson.Options);
                SkillRuleCatalog.Validate(rule);
                SkillRuleCatalog.ShareForceOrbs(rule, entry.Tier);
                parsedRules.Add(rule);
            }
        }

        rules = parsedRules;
        return skills;
    }

    private static Catalog Load()
    {
        var assembly = typeof(ClassSkillCatalog).Assembly;
        var skills = new Dictionary<PlayerClass, IReadOnlyList<Skill>>();
        var rules = new Dictionary<uint, SkillRule>();
        var byCode = new Dictionary<uint, Skill>();
        foreach (var resource in assembly.GetManifestResourceNames().Where(static name => name.StartsWith(ResourcePrefix, StringComparison.Ordinal)))
        {
            var className = resource[ResourcePrefix.Length..^".json".Length];
            if (!Enum.TryParse<PlayerClass>(className, out var playerClass))
            {
                throw new InvalidDataException($"Class skill file {className} names no class.");
            }

            using var stream = assembly.GetManifestResourceStream(resource)!;
            using var reader = new StreamReader(stream);
            var classSkills = Parse(reader.ReadToEnd(), out var classRules);
            foreach (var skill in classSkills)
            {
                if (!byCode.TryAdd(skill.Code, skill))
                {
                    throw new InvalidDataException($"Skill {skill.Code:X8} is defined twice.");
                }
            }

            foreach (var rule in classRules)
            {
                rules.Add(rule.Code, rule);
            }

            skills.Add(playerClass, Array.AsReadOnly(classSkills.ToArray()));
        }

        if (skills.Count == 0)
        {
            throw new InvalidDataException("The class skill files are missing from the simulation assembly.");
        }

        return new Catalog(skills, rules, byCode);
    }

    private static Skill ToSkill(ClassSkillEntry entry)
    {
        var tierBit = (entry.Code >> 16) & 0xFF;
        var slot = entry.Code & 0xFF;
        if (entry.Code == 0 || entry.Name.Length == 0 || tierBit != (uint)entry.Tier || slot is 0 or > 4 || entry.Mana.Length is not (0 or 10)
            || entry.Stamina.Length is not (0 or 2) || entry.RequiredLevel < 1 || entry.Mana.Any(static mana => mana < 0)
            || !Enum.IsDefined(entry.Mouse) || !Enum.IsDefined(entry.Category))
        {
            throw new InvalidDataException($"Class skill {entry.Code:X8} ({entry.Name}) is malformed.");
        }

        var weapons = 0u;
        foreach (var name in entry.Weapons)
        {
            if (!Enum.TryParse<ItemKind>(name, out var kind) || !Enum.IsDefined(kind))
            {
                throw new InvalidDataException($"Class skill {entry.Name} names an unknown weapon {name}.");
            }

            weapons |= 1u << (int)kind;
        }

        return new Skill
        {
            Code = entry.Code,
            Name = entry.Name,
            Description = entry.Description,
            IconKey = entry.Icon,
            Tier = entry.Tier,
            Category = entry.Category,
            RequiredLevel = entry.RequiredLevel,
            MouseUse = entry.Mouse,
            ManaByRank = Array.AsReadOnly(entry.Mana),
            StaminaBase = entry.Stamina.Length > 0 ? entry.Stamina[0] : 0,
            StaminaPerRank = entry.Stamina.Length > 1 ? entry.Stamina[1] : 0,
            AllowedWeaponsMask = weapons
        };
    }

    private sealed record Catalog(IReadOnlyDictionary<PlayerClass, IReadOnlyList<Skill>> Skills, IReadOnlyDictionary<uint, SkillRule> Rules,
        IReadOnlyDictionary<uint, Skill> ByCode);
}

// The serializer settings of the skill data files: camel-case names, string enums and hex skill codes.
internal static class SkillJson
{
    public static JsonSerializerOptions Options { get; } = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        Converters = { new JsonStringEnumConverter(), new HexCodeConverter() },
        TypeInfoResolver = new DefaultJsonTypeInfoResolver()
    };

    // The serializer skips unknown properties, so a misspelled name would silently drop its value. Every property of a
    // skill object, at any depth, must belong to one of the types it is read into; brood lists must name MonsterBrood
    // values.
    public static void RequireKnownProperties(JsonElement element, IReadOnlyList<Type> types, string path)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            return;
        }

        var infos = types.Select(type => Options.GetTypeInfo(type)).Where(static info => info.Kind == JsonTypeInfoKind.Object).ToArray();
        if (infos.Length == 0)
        {
            return;
        }

        foreach (var property in element.EnumerateObject())
        {
            var matches = infos.SelectMany(static info => info.Properties).Where(info => info.Name == property.Name).ToArray();
            if (matches.Length == 0)
            {
                throw new InvalidDataException($"{path}: unknown property '{property.Name}'.");
            }

            var propertyPath = path + "." + property.Name;
            if (property.Name.EndsWith("roods", StringComparison.Ordinal) && property.Value.ValueKind == JsonValueKind.Array)
            {
                foreach (var brood in property.Value.EnumerateArray())
                {
                    if (!Enum.TryParse<MonsterBrood>(brood.GetString(), ignoreCase: true, out var parsed) || !Enum.IsDefined(parsed))
                    {
                        throw new InvalidDataException($"{propertyPath}: unknown brood '{brood}'.");
                    }
                }
            }

            var nested = matches.Select(static info => ObjectType(info.PropertyType)).OfType<Type>().Distinct().ToArray();
            if (nested.Length > 0)
            {
                CheckNested(property.Value, nested, propertyPath);
            }
        }
    }

    private static void CheckNested(JsonElement value, Type[] types, string path)
    {
        if (value.ValueKind == JsonValueKind.Array)
        {
            var index = 0;
            foreach (var item in value.EnumerateArray())
            {
                CheckNested(item, types, $"{path}[{index++}]");
            }

            return;
        }

        RequireKnownProperties(value, types, path);
    }

    // The object type a property holds, through arrays, lists and nullable wrappers; null for scalars.
    private static Type ObjectType(Type type)
    {
        while (true)
        {
            type = Nullable.GetUnderlyingType(type) ?? type;
            if (type.IsArray)
            {
                type = type.GetElementType()!;
                continue;
            }

            if (type.IsGenericType && type.GetGenericArguments() is [var element]
                && typeof(System.Collections.IEnumerable).IsAssignableFrom(type))
            {
                type = element;
                continue;
            }

            return type == typeof(string) || type.IsPrimitive || type.IsEnum || type == typeof(decimal) ? null : type;
        }
    }

    // Skill codes are written as hex strings ("0x01010001") in the data files.
    private sealed class HexCodeConverter : JsonConverter<uint>
    {
        public override uint Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType == JsonTokenType.Number)
            {
                return reader.GetUInt32();
            }

            var text = reader.GetString() ?? "";
            return text.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
                ? Convert.ToUInt32(text[2..], 16)
                : uint.Parse(text, System.Globalization.CultureInfo.InvariantCulture);
        }

        public override void Write(Utf8JsonWriter writer, uint value, JsonSerializerOptions options) => writer.WriteStringValue($"0x{value:X8}");
    }
}
