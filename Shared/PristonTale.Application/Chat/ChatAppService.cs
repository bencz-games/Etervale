using System.Globalization;
using PristonTale.Application.GameMasters;
using PristonTale.Application.World;
using PristonTale.Simulation.Social;
using PristonTale.Contracts.Social;
using PristonTale.Contracts.Texts;

namespace PristonTale.Application.Chat;

// The chat on the server. A spoken line reaches the players near the speaker with the speaker's name in front; lines
// that start with a slash are commands: whispers, the trade and clan channels, whisper refusal, the boss minute, a party
// request by name, then the game-master commands. A slash line is never spoken, nor is a yahoo word, which cheers instead. Runs on the caller's thread; positions come from the tick.
public sealed class ChatAppService
{
    private static readonly GameText WhisperRefused = GameText.Of(GameTextId.WhisperUnable);
    private readonly IPartyRoster _parties;
    private readonly IClanRoster _clans;

    public ChatAppService(ChatDirectory directory, WorldServer world, GameMasterCommands gameMasters, IPartyRoster? parties = null, IClanRoster? clans = null)
    {
        Directory = directory;
        World = world;
        GameMasters = gameMasters;
        _parties = parties ?? world.Parties;
        _clans = clans ?? NoClans.Instance;
    }

    public ChatDirectory Directory { get; }
    public WorldServer World { get; }
    public GameMasterCommands GameMasters { get; }
    public Func<long> NowMs { get; init; } = static () => Environment.TickCount64;

    // The client's flood rules, checked again on lines that reach other players. A party, trade or clan line counts
    // toward the rapid-line rule only, as the client counts its channel lines; commands and the yahoo word are free.
    private bool Floods(ChatMember member, string line, bool channel) =>
        member.Flood.Check(line, channel, whisper: false, NowMs()) != ChatFloodVerdict.Allowed;

    // A line typed in the chat input.
    public async Task SayAsync(ulong entityId, string text)
    {
        if (!Directory.TryGet(entityId, out var member))
        {
            return;
        }

        text = ChatRules.WithoutRoute(ChatRules.ClipToCapacity(ChatCommandText.Clean(text)));
        if (text.Length == 0)
        {
            return;
        }

        if (YahooRules.IsWord(text))
        {
            try
            {
                await World.YahooAsync(entityId, key: false);
            }
            catch (WorldBusyException)
            {
                // A full tick queue drops the cheer, as it drops a spoken line.
            }

            return;
        }

        if (!ChatRules.IsCommand(text))
        {
            await SpeakAsync(member, text);
            return;
        }

        if (Simulation.Characters.HelpPets.Commands.Any(command => ChatCommandText.Match(command, text) > 0))
        {
            try
            {
                await World.ToggleHelpPetAsync(entityId);
            }
            catch (WorldBusyException)
            {
                // A full tick queue drops the command, as it drops a spoken line.
            }

            return;
        }

        if (!await PlayerCommandAsync(member, text))
        {
            await GameMasters.HandleAsync(member, text);
        }
    }

    // A party line: every other member of the speaker's party receives it.
    public Task PartyAsync(ulong entityId, string text)
    {
        if (!Directory.TryGet(entityId, out var member))
        {
            return Task.CompletedTask;
        }

        text = ChatRules.ClipToCapacity(ChatCommandText.Clean(text));
        if (text.Length == 0 || Floods(member, text, channel: true))
        {
            return Task.CompletedTask;
        }

        var line = new ChatLine(ChatKind.Party, entityId, ChatRules.Shorten(ChatRules.Compose(member.Name, text)));
        foreach (var id in _parties.MembersOf(entityId))
        {
            if (id != entityId && Directory.TryGet(id, out var hearer))
            {
                hearer.Link.Deliver(line);
            }
        }

        return Task.CompletedTask;
    }

    // Called with each heartbeat: a standing notice the player has not received yet goes out now.
    public void Heartbeat(ulong entityId)
    {
        if (Directory.TryGet(entityId, out var member) && Directory.TryTakeNotice(member, out var text))
        {
            member.Link.Deliver(new ChatLine(ChatKind.Notice, 0, ChatRules.Notice(text)));
        }
    }

    private async Task SpeakAsync(ChatMember member, string text)
    {
        if (Floods(member, text, channel: false))
        {
            return;
        }

        var line = new ChatLine(ChatKind.Player, member.EntityId, ChatRules.Shorten(ChatRules.Compose(member.Name, text)));
        ulong[] near;
        try
        {
            near = await World.PlayersNearAsync(member.EntityId, ChatRules.AreaRangeSquared);
        }
        catch (WorldBusyException)
        {
            return;
        }

        foreach (var id in near)
        {
            if (Directory.TryGet(id, out var hearer))
            {
                hearer.Link.Deliver(line);
            }
        }
    }

    private Task<bool> PlayerCommandAsync(ChatMember member, string text)
    {
        if (ChatCommandText.Match("/bosstime", text) > 0)
        {
            member.Link.Deliver(ChatLine.Reply(GameText.Of(GameTextId.BossTime, World.BossMinute.ToString("00", CultureInfo.InvariantCulture))));
            return Task.FromResult(true);
        }

        if (ChatCommandText.Match("/TRADE>", text) is var trade and > 0)
        {
            var message = ChatCommandText.Rest(text, trade);
            if (message.Length > 0 && !Floods(member, message, channel: true))
            {
                Directory.Broadcast(new ChatLine(ChatKind.Trade, 0, ChatRules.ClipToCapacity(ChatRules.Compose(member.Name, message))));
            }

            return Task.FromResult(true);
        }

        if (ChatRules.IsWhisper(text))
        {
            Whisper(member, text);
            return Task.FromResult(true);
        }

        if (ChatCommandText.Match("/disable_whisper", text) > 0)
        {
            member.WhisperRefused = true;
            member.Link.Deliver(ChatLine.Reply(GameText.Of(GameTextId.WhispersDenied)));
            return Task.FromResult(true);
        }

        if (ChatCommandText.Match("/enable_whisper", text) > 0)
        {
            member.WhisperRefused = false;
            member.Link.Deliver(ChatLine.Reply(GameText.Of(GameTextId.WhispersAccepted)));
            return Task.FromResult(true);
        }

        if (PartyRequestLength(text) is var party and > 0)
        {
            return InviteToPartyAsync(member, text, party);
        }

        var clan = _clans.ClanOf(member.EntityId);
        if (clan != 0 && ChatCommandText.Match("/CLAN>", text) is var clanLength and > 0)
        {
            var message = ChatCommandText.Rest(text, clanLength);
            if (message.Length > 0 && !Floods(member, message, channel: true))
            {
                var line = new ChatLine(ChatKind.Clan, 0, ChatRules.ClipToCapacity(member.Name + "> " + message));
                foreach (var other in Directory.Members)
                {
                    if (_clans.ClanOf(other.EntityId) == clan)
                    {
                        other.Link.Deliver(line);
                    }
                }
            }

            return Task.FromResult(true);
        }

        return Task.FromResult(false);
    }

    private static int PartyRequestLength(string text) => Math.Max(ChatCommandText.Match("//party", text), ChatCommandText.Match("//PARTY", text));

    // "//party name": the original's party request, also what the button under a player's portrait sends.
    private async Task<bool> InviteToPartyAsync(ChatMember member, string text, int length)
    {
        var name = ChatCommandText.ReadWord(text, ref length);
        if (name.Length == 0)
        {
            return true;
        }

        try
        {
            Directory.Deliver(await World.InviteToPartyByNameAsync(member.Session, name));
        }
        catch (WorldBusyException)
        {
        }

        return true;
    }

    // "/:name text" or "/;name text": the sender sees "To> name: text", the receiver "From> sender: text". A receiver who
    // refuses whispers or ignores the sender, or a missing text, gets the sender a server line instead.
    private void Whisper(ChatMember sender, string text)
    {
        var index = 2;
        var name = ChatCommandText.ReadWord(text, ref index);
        var message = text[index..];
        var target = name.Length == 0 ? null : Directory.FindByName(name);
        if (target is null)
        {
            if (name.Length > 0)
            {
                sender.Link.Deliver(ChatLine.Reply(Simulation.Parties.PartyMessages.NotFound(name)));
            }

            return;
        }

        if (target.Contacts.Ignores(sender.Name))
        {
            sender.Link.Deliver(ChatLine.Reply(ContactMessages.WhisperRefused));
            return;
        }

        if (target.WhisperRefused)
        {
            sender.Link.Deliver(ChatLine.Reply(WhisperRefused));
            return;
        }

        var probe = 0;
        if (ChatCommandText.ReadWord(message, ref probe).Length == 0)
        {
            sender.Link.Deliver(ChatLine.Reply(GameText.Of(GameTextId.PlayerOnline, name)));
            return;
        }

        sender.Link.Deliver(new ChatLine(ChatKind.Whisper, 0, ChatRules.ClipToCapacity("To> " + name + ": " + message)));
        target.Link.Deliver(new ChatLine(ChatKind.Whisper, 0, ChatRules.ClipToCapacity("From> " + sender.Name + ": " + message)));
    }
}
