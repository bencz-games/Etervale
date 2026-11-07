using PristonTale.Application.Chat;
using PristonTale.Application.GameMasters;
using PristonTale.Simulation.Social;
using PristonTale.Contracts.Social;

namespace PristonTale.Tests;

internal static class ChatRoutingTests
{
    public static IEnumerable<(string, Func<Task>)> Cases()
    {
        yield return ("a spoken line reaches players within the spoken range with the speaker's name and entityId", SpokenRangeAsync);
        yield return ("long spoken lines are cut to 68 characters and an ellipsis, slash lines are never spoken", ShortenAsync);
        yield return ("whispers name both sides, refuse when whispers are disabled and report missing players", WhisperAsync);
        yield return ("trade lines reach everybody, clan lines the clan, party lines the other members only", ChannelsAsync);
        yield return ("notices reach everybody at once; the standing notice comes once with the next heartbeat", NoticesAsync);
        yield return ("the shutdown countdown warns, asks every client to close and then stops", ShutdownAsync);
        yield return ("chat command text parsing follows the classic word and number rules", ParsingAsync);
        yield return ("the server drops a repeated line and freezes a speaker after ten quick lines, as the client does", FloodAsync);
    }

    private static async Task FloodAsync()
    {
        var fixture = new ChatFixture();
        var speaker = await fixture.JoinAsync("Talker", 0, 0);
        var listener = await fixture.JoinAsync("Hearer", 10, 10);
        await fixture.SayAsync(speaker, "same");
        fixture.NowMs += 1000;
        await fixture.SayAsync(speaker, "same");
        Assert.True(listener.Link.Lines.Count == 1, "a repeated line within five seconds was spoken twice");
        for (var line = 0; line < 12; line++)
        {
            fixture.NowMs += 100;
            await fixture.SayAsync(speaker, $"line {line}");
        }

        Assert.True(listener.Link.Lines.Count == 1 + ChatRules.FloodLines - 1, $"{listener.Link.Lines.Count} lines reached the listener during a flood");
        fixture.NowMs += ChatRules.FloodPenaltyMs + 1;
        await fixture.SayAsync(speaker, "after");
        Assert.True(listener.Link.Last == "Talker: after", "the speaker stayed frozen after the penalty");
    }

    private static async Task SpokenRangeAsync()
    {
        var fixture = new ChatFixture();
        var speaker = await fixture.JoinAsync("Talker", 1000, 1000);
        var near = await fixture.JoinAsync("Nearby", 1000 + 500, 1000);
        var edge = await fixture.JoinAsync("Edge", 1000 + 530, 1000 + 120);
        var far = await fixture.JoinAsync("Faraway", 1000 + 560, 1000);
        await fixture.SayAsync(speaker, "hello there");
        Assert.True(near.Link.Lines.Single() == new ChatLine(ChatKind.Player, speaker.Id, "Talker: hello there"),
            $"near player heard {string.Join("|", near.Link.Texts())}");
        Assert.True(edge.Link.Lines.Count == 0 && far.Link.Lines.Count == 0, "a player beyond the spoken range (543 units) heard the line");
        Assert.True(speaker.Link.Lines.Count == 0, "the speaker received its own spoken line; the client shows it itself");
    }

    private static async Task ShortenAsync()
    {
        var fixture = new ChatFixture();
        var speaker = await fixture.JoinAsync("Ann", 0, 0);
        var listener = await fixture.JoinAsync("Bob", 10, 10);
        var text = new string('x', 75);
        await fixture.SayAsync(speaker, text);
        var heard = listener.Link.Last;
        Assert.True(heard.Length == ChatRules.RelayKeep + 3 && heard.EndsWith("...", StringComparison.Ordinal) && heard.StartsWith("Ann: xxx", StringComparison.Ordinal),
            $"long line arrived as {heard}");
        listener.Link.Clear();
        await fixture.SayAsync(speaker, "/unknown command");
        await fixture.SayAsync(speaker, "`/also a command");
        Assert.True(listener.Link.Lines.Count == 0 && speaker.Link.Lines.Count == 0, "a slash line was spoken or answered");
        await fixture.SayAsync(speaker, "tab\there");
        Assert.True(listener.Link.Last == "Ann: tab here", "control characters were not replaced by spaces");
    }

    private static async Task WhisperAsync()
    {
        var fixture = new ChatFixture();
        var ann = await fixture.JoinAsync("Ann", 0, 0);
        var bob = await fixture.JoinAsync("Bob", 5000, 5000);
        await fixture.SayAsync(ann, "/:bob meet me");
        Assert.True(ann.Link.Lines.Single() == new ChatLine(ChatKind.Whisper, 0, "To> bob:  meet me"), $"sender saw {ann.Link.Last}");
        Assert.True(bob.Link.Lines.Single() == new ChatLine(ChatKind.Whisper, 0, "From> Ann:  meet me"), $"receiver saw {bob.Link.Last}");
        await fixture.SayAsync(ann, "/;Bob");
        Assert.True(ann.Link.Last == "Bob is currently online" && ann.Link.Lines[^1].Kind == ChatKind.Server, "an empty whisper did not report the player online");
        await fixture.SayAsync(ann, "/:Carl hi");
        Assert.True(ann.Link.Last == "Carl is currently offline, or cannot be found", "a missing player was not reported");
        await fixture.SayAsync(bob, "/disable_whisper");
        Assert.True(bob.Link.Last == "Deny private messages", "refusing whispers was not confirmed");
        bob.Link.Clear();
        await fixture.SayAsync(ann, "/:Bob again");
        Assert.True(ann.Link.Last == "Unable to send private messages" && bob.Link.Lines.Count == 0, "a refused whisper was delivered");
        await fixture.SayAsync(bob, "/enable_whisper");
        await fixture.SayAsync(ann, "/:Bob back");
        Assert.True(bob.Link.Last == "From> Ann:  back", "whispers did not resume after enabling them");
    }

    private static async Task ChannelsAsync()
    {
        var clans = new FakeClans();
        var parties = new FakeParties();
        var fixture = new ChatFixture(parties: parties, clans: clans);
        var ann = await fixture.JoinAsync("Ann", 0, 0);
        var bob = await fixture.JoinAsync("Bob", 9000, 9000);
        var cid = await fixture.JoinAsync("Cid", -9000, 9000);
        await fixture.SayAsync(ann, "/TRADE>   selling axes");
        foreach (var player in new[] { ann, bob, cid })
        {
            Assert.True(player.Link.Lines.Single() == new ChatLine(ChatKind.Trade, 0, "Ann: selling axes"), $"{player.Member.Name} trade line {player.Link.Last}");
        }

        await fixture.SayAsync(ann, "/CLAN> rally");
        Assert.True(bob.Link.Lines.Count == 1, "a player without a clan sent a clan line");
        clans.Clans[ann.Id] = 7;
        clans.Clans[bob.Id] = 7;
        await fixture.SayAsync(ann, "/CLAN> rally");
        Assert.True(ann.Link.Last == "Ann> rally" && bob.Link.Last == "Ann> rally" && bob.Link.Lines[^1].Kind == ChatKind.Clan && cid.Link.Lines.Count == 1,
            "clan lines did not reach exactly the clan");
        await fixture.RunAsync(fixture.Chat.PartyAsync(ann.Id, "pull"));
        Assert.True(bob.Link.Lines.Count == 2, "a party line reached somebody while nobody is in a party");
        parties.Members = [ann.Id, bob.Id];
        await fixture.RunAsync(fixture.Chat.PartyAsync(ann.Id, "pull"));
        Assert.True(bob.Link.Lines[^1] == new ChatLine(ChatKind.Party, ann.Id, "Ann: pull") && ann.Link.Lines.Count == 2 && cid.Link.Lines.Count == 1,
            "party lines must reach the other members only");
    }

    private static async Task NoticesAsync()
    {
        var fixture = new ChatFixture();
        var gm = await fixture.JoinAsync("Keeper", 0, 0, GameMasterLevel.Moderator);
        var ann = await fixture.JoinAsync("Ann", 9000, 0);
        await fixture.SayAsync(gm, "/>Server restarts at noon");
        Assert.True(ann.Link.Lines.Single() == new ChatLine(ChatKind.Notice, 0, "Notice> Server restarts at noon") && gm.Link.Last == "Notice> Server restarts at noon",
            "a game master's notice did not reach everybody");
        await fixture.SayAsync(gm, "/>>Double experience today");
        Assert.True(ann.Link.Lines.Count == 1, "the standing notice went out before a heartbeat");
        fixture.Chat.Heartbeat(ann.Id);
        fixture.Chat.Heartbeat(ann.Id);
        Assert.True(ann.Link.Lines.Count == 2 && ann.Link.Last == "Notice> Double experience today", "the standing notice was not sent exactly once");
        var late = await fixture.JoinAsync("Late", 0, 9000);
        fixture.Chat.Heartbeat(late.Id);
        Assert.True(late.Link.Lines.Count == 0, "a player who entered later received the earlier standing notice");
        ServerNotices.Broadcast(fixture.Directory, "maintenance");
        Assert.True(late.Link.Last == "Notice> maintenance", "a server notice did not reach everybody");
    }

    private static async Task ShutdownAsync()
    {
        var fixture = new ChatFixture();
        var ann = await fixture.JoinAsync("Ann", 0, 0);
        var stopped = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var schedule = new ShutdownSchedule(TimeSpan.FromMilliseconds(200), TimeSpan.FromMilliseconds(5), TimeSpan.FromMilliseconds(40),
            TimeSpan.FromMilliseconds(40), TimeSpan.FromMilliseconds(10));
        await ShutdownCountdown.RunAsync(fixture.Directory, schedule, () => stopped.TrySetResult()).WaitAsync(TimeSpan.FromSeconds(5));
        var texts = ann.Link.Texts();
        Assert.True(texts.Take(5).SequenceEqual(ShutdownCountdown.OpeningNotices), "the opening notices were not sent first and in order");
        Assert.True(texts.Skip(5).Any() && texts.Skip(5).All(text => text.StartsWith("Shutdown> 0 mins ", StringComparison.Ordinal)),
            $"the countdown reminders were wrong: {string.Join("|", texts)}");
        Assert.True(ann.Link.Lines.All(line => line.Kind == ChatKind.Notice) && ann.Link.CloseRequests == 1 && stopped.Task.IsCompleted,
            "every client must be asked to close before the server stops");
        Assert.True(ShutdownCountdown.Reminder(TimeSpan.FromSeconds(95)) == "Shutdown> 1 mins 35 secs left to exit. Please exit the game",
            "the reminder text changed");
    }

    private static Task ParsingAsync()
    {
        var index = 2;
        Assert.True(ChatCommandText.ReadWord("/: Bob  hi", ref index) == "Bob" && index == 6, "words skip spaces and colons");
        index = 0;
        Assert.True(ChatCommandText.ReadPhrase("\"Hob Goblin\" 3", ref index) == "Hob Goblin" && ChatCommandText.ReadWord("\"Hob Goblin\" 3", ref index) == "3",
            "quoted names did not read as one phrase");
        Assert.True(ChatCommandText.ToNumber("  -12abc") == -12 && ChatCommandText.ToNumber("x") == 0, "numbers follow C atoi");
        Assert.True(ChatCommandText.Match("/show", "/show_blackuser") == 5 && ChatCommandText.Match("/Show", "/show") == 0,
            "commands match by exact, case-sensitive prefix");
        return Task.CompletedTask;
    }

    private sealed class FakeClans : IClanRoster
    {
        public Dictionary<ulong, long> Clans { get; } = new();

        public long ClanOf(ulong entityId) => Clans.GetValueOrDefault(entityId);
    }

    private sealed class FakeParties : IPartyRoster
    {
        public ulong[] Members { get; set; } = [];

        public IReadOnlyCollection<ulong> MembersOf(ulong entityId) => Members.Contains(entityId) ? Members : [];
    }
}
