using System.Text.Json;
using PristonTale.Contracts.Characters;
using PristonTale.Contracts.Texts;
using PristonTale.Persistence.Abstractions.Characters;
using PristonTale.Simulation.Characters;

namespace PristonTale.Tests;

internal static class SavedGameTextTests
{
    public static IEnumerable<(string, Func<Task>)> Cases()
    {
        yield return ("saved game texts round-trip; a line saved as plain text or an unknown id loads as no line", ReadAsync);
        yield return ("a character whose item use answer was saved as plain text before game texts still loads", OldItemUseAsync);
    }

    private static Task ReadAsync()
    {
        var options = CharacterJson.Options;
        var text = GameText.Of(GameTextId.NotEnoughGold, "12");
        Assert.True(JsonSerializer.Deserialize<GameText>(JsonSerializer.Serialize(text, options), options) == text, "a game text did not round-trip");
        Assert.True(JsonSerializer.Deserialize<GameText>("\"Hopy Crystal used\"", options).IsNone, "an old line");
        Assert.True(JsonSerializer.Deserialize<GameText>("{\"Id\":\"NoSuchLine\",\"Args\":[]}", options).IsNone, "an unknown id");
        Assert.True(JsonSerializer.Deserialize<GameText>("{\"Id\":\"None\",\"Args\":[],\"IsNone\":true}", options).IsNone, "a saved none");
        Assert.True(JsonSerializer.Deserialize<GameText>("{\"Id\":\"NotEnoughGold\",\"Args\":[\"5\"],\"IsNone\":false}", options)
            == GameText.Of(GameTextId.NotEnoughGold, "5"), "a game text saved with its IsNone");
        return Task.CompletedTask;
    }

    private static Task OldItemUseAsync()
    {
        const string premium = """
            {"Version":1,"Timers":[],"Operations":[{"Command":{"OperationId":"0f6e3c1a-5b1e-4bf4-871c-18f275c27f3f","ExpectedRevision":6,
            "ItemId":"bc05f071-868d-4750-ad45-19aa2a85033b","Argument":0,"Text":""},"Revision":7,
            "Outcome":{"Ok":true,"Revision":7,"Status":"Used","ItemCode":1576449,"Message":"Hopy Crystal used"}}]}
            """;
        var character = new CharacterClassRegistry().CreatePlayer(PlayerClass.Fighter, "Panzer");
        SavedPremium.Restore(character, new JsonText(premium), 0);
        Assert.True(character.ItemUses.Find(Guid.Parse("0f6e3c1a-5b1e-4bf4-871c-18f275c27f3f")) is { Outcome.Message.IsNone: true, Revision: 7 },
            "the old item use was not restored");
        return Task.CompletedTask;
    }
}
