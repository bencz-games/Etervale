using System.Collections.Immutable;
using System.Text.Json;
using System.Text.Json.Serialization;
using PristonTale.Contracts.Texts;

namespace PristonTale.Persistence.Abstractions.Characters;

// A saved operation's answer holds its line as {Id, Args}. Characters saved before the lines were game texts hold the
// line as plain text, and a later version may drop an id: both read as no line, which only a replay of that already
// answered operation would have shown.
public sealed class GameTextJsonConverter : JsonConverter<GameText>
{
    public override GameText Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.StartObject)
        {
            reader.Skip();
            return GameText.None;
        }

        using var document = JsonDocument.ParseValue(ref reader);
        var root = document.RootElement;
        if (!root.TryGetProperty(nameof(GameText.Id), out var saved) || saved.ValueKind != JsonValueKind.String
            || !Enum.TryParse<GameTextId>(saved.GetString(), out var id) || !Enum.IsDefined(id))
        {
            return GameText.None;
        }

        var args = ImmutableArray.CreateBuilder<string>();
        if (root.TryGetProperty(nameof(GameText.Args), out var list) && list.ValueKind == JsonValueKind.Array)
        {
            foreach (var arg in list.EnumerateArray())
            {
                args.Add(arg.ValueKind == JsonValueKind.String ? arg.GetString() ?? "" : arg.ToString());
            }
        }

        return new GameText(id, args.ToImmutable());
    }

    public override void Write(Utf8JsonWriter writer, GameText value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        writer.WriteString(nameof(GameText.Id), value.Id.ToString());
        writer.WriteStartArray(nameof(GameText.Args));
        foreach (var arg in value.Args)
        {
            writer.WriteStringValue(arg);
        }

        writer.WriteEndArray();
        writer.WriteEndObject();
    }
}
