using System.Text.Json;
using System.Text.Json.Serialization;
using PristonTale.Simulation.Characters;

namespace PristonTale.Persistence.Abstractions.Characters;

public readonly record struct JsonText(string Value)
{
    public static readonly JsonText EmptyArray = new("[]");

    public static readonly JsonText EmptyObject = new("{}");

    public override string ToString() => Value;
}

public static class CharacterJson
{
    public static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = false,
        Converters = { new GameTextJsonConverter(), new JsonStringEnumConverter() },
    };

    public static JsonText Write<T>(T value) => new(JsonSerializer.Serialize(value, Options));

    public static T? Read<T>(JsonText json) =>
        string.IsNullOrWhiteSpace(json.Value) ? default : JsonSerializer.Deserialize<T>(json.Value, Options);
}
