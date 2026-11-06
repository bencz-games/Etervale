using Microsoft.Extensions.DependencyInjection;
using PristonTale.Application.Chat;
using PristonTale.Application.Contacts;
using PristonTale.Client.Net.Presence;
using PristonTale.Contracts.Characters;
using PristonTale.Contracts.Social;
using PristonTale.Gameplay.UI.Contacts;
using PristonTale.Persistence.Abstractions;
using PristonTale.Persistence.Abstractions.Accounts;
using PristonTale.Persistence.Abstractions.Characters;
using PristonTale.Persistence.Abstractions.Contacts;
using PristonTale.Persistence.Abstractions.Security;
using PristonTale.Persistence.Sqlite;
using PristonTale.Protocol.Packets;
using PristonTale.Simulation.Characters;
using PristonTale.Simulation.Social;
using Microsoft.Xna.Framework;

namespace PristonTale.Tests;

// The friends page: the lists' rules, their storage, the server's answers with the friends' presence, ignored whispers,
// the wire and the page in the HUD.
internal static class ContactTests
{
    public static IEnumerable<(string, Func<Task>)> Cases()
    {
        yield return ("contact lists hold a name once whatever its case, never the owner's, at most 100 each, and change only once loaded", RulesAsync);
        yield return ("the server stores each change, answers both lists with the friends' presence, and refuses an ignored player's whisper", ServiceAsync);
        yield return ("contacts are stored one row per name in the order added, and leave with their character", StorageAsync);
        yield return ("contact messages round-trip on the wire and refuse bad lists", WireAsync);
        yield return ("the friends page keeps the last seven players met, picks then whispers, and moves a recent player onto a list", PageAsync);
    }

    private static Task RulesAsync()
    {
        var book = new ContactBook([(ContactList.Friends, "Ann"), (ContactList.Ignored, "Bob"), (ContactList.Friends, "ann")]);
        Assert.True(book.Friends is ["Ann"] && book.Ignored is ["Bob"] && book.Ignores("BOB"), "the loaded lists kept a name twice");
        Assert.True(book.Check(ContactList.Friends, "ANN", "Me") == ContactStatus.Listed && book.Check(ContactList.Friends, "me", "Me") == ContactStatus.Rejected
            && book.Check(ContactList.Friends, "Al", "Me") == ContactStatus.Rejected && book.Check(ContactList.Ignored, "Cid", "Me") == ContactStatus.Ok,
            "a listed, own or too short name was taken");
        Assert.True(book.Find(ContactList.Friends, "aNn") == "Ann", "a name was not found whatever its case");
        book.Remove(ContactList.Friends, "ANN");
        Assert.True(book.Friends.Count == 0, "a name was not removed whatever its case");
        for (var index = 0; index < ContactLimits.MaxEntries; index++)
        {
            book.Add(ContactList.Friends, $"Friend{index:000}");
        }

        Assert.True(book.Check(ContactList.Friends, "Another", "Me") == ContactStatus.Full, "a full list took another name");
        Assert.True(ContactBook.Unavailable().Check(ContactList.Friends, "Ann", "Me") == ContactStatus.Unavailable,
            "lists storage could not read took a change");
        return Task.CompletedTask;
    }

    private static async Task ServiceAsync()
    {
        var fixture = new ChatFixture();
        var store = new MemoryContacts();
        var service = new ContactAppService(fixture.Directory, store);
        var ann = await fixture.JoinAsync("Ann", 0, 0);
        var bob = await fixture.JoinAsync("Bob", 100, 0);
        var lists = await service.ChangeAsync(ann.Id, ContactList.Friends, true, " Bob ");
        Assert.True(lists?.Friends is [{ Name: "Bob", Online: true }] && store.Rows is [(_, ContactList.Friends, "Bob")], "a friend was not stored online");
        lists = await service.ChangeAsync(ann.Id, ContactList.Friends, true, "Zed");
        Assert.True(lists?.Friends is [_, { Name: "Zed", Online: false }], "a friend not playing showed online");
        await service.ChangeAsync(ann.Id, ContactList.Friends, true, "bob");
        Assert.True(ann.Link.Last == "bob is already on the list" && store.Rows.Count == 2, "a listed name was stored twice");
        lists = await service.ChangeAsync(ann.Id, ContactList.Friends, false, "ZED");
        Assert.True(lists?.Friends is [{ Name: "Bob" }] && store.Rows.Count == 1, "a name was not removed");
        store.Refuse = true;
        lists = await service.ChangeAsync(ann.Id, ContactList.Friends, true, "Cid");
        Assert.True(lists?.Friends is [{ Name: "Bob" }] && ann.Link.LastLine == ContactMessages.Of(ContactStatus.Unavailable, "Cid"),
            "a change storage refused was not answered with the lists as they were");
        store.Refuse = false;

        await service.ChangeAsync(ann.Id, ContactList.Ignored, true, "Bob");
        bob.Link.Clear();
        ann.Link.Clear();
        await fixture.SayAsync(bob, "/:Ann hello");
        Assert.True(bob.Link.LastLine == ContactMessages.WhisperRefused && ann.Link.Lines.Count == 0, "an ignored player's whisper reached Ann");
    }

    private static async Task StorageAsync()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"priston-contacts-{Guid.NewGuid():N}");
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddPristonSqlite(Path.Combine(directory, "world.db"));
        try
        {
            await using var provider = services.BuildServiceProvider();
            await provider.GetRequiredService<IDatabaseMigrator>().MigrateAsync();
            var account = await provider.GetRequiredService<IAccountRepository>().CreateAsync("keeper", PasswordHasher.Hash("secret"));
            var characters = provider.GetRequiredService<ICharacterRepository>();
            var created = await characters.CreateAsync(CharacterRecordFactory.NewStarter(new CharacterClassRegistry(), account.Id, "Keeper", 0, PlayerClass.Fighter, 3));
            var contacts = provider.GetRequiredService<IContactRepository>();
            Assert.True(await contacts.AddAsync(created.Id, ContactList.Friends, "Zed") && await contacts.AddAsync(created.Id, ContactList.Ignored, "Bob")
                && await contacts.AddAsync(created.Id, ContactList.Friends, "Ann") && !await contacts.AddAsync(created.Id, ContactList.Friends, "Ann"),
                "a name was stored twice on a list");
            Assert.True(await contacts.ListAsync(created.Id) is [(ContactList.Friends, "Zed"), (ContactList.Ignored, "Bob"), (ContactList.Friends, "Ann")],
                "the lists did not keep the order added");
            Assert.True(await contacts.RemoveAsync(created.Id, ContactList.Friends, "ZED") && !await contacts.RemoveAsync(created.Id, ContactList.Friends, "Zed"),
                "a removal did not report what it removed, whatever the name's case");
            Assert.True(await characters.DeleteByNameAsync(account.Id, "Keeper") && (await contacts.ListAsync(created.Id)).Count == 0,
                "the contacts outlived their character");
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            Directory.Delete(directory, recursive: true);
        }
    }

    private static Task WireAsync()
    {
        var buffer = new byte[ContactListPacket.MaxSize];
        var change = ContactChangePacket.Read(buffer.AsSpan(0, new ContactChangePacket(ContactList.Ignored, true, "Bob").Write(buffer)));
        ContactRefreshPacket.Read(buffer.AsSpan(0, new ContactRefreshPacket().Write(buffer)));
        var list = ContactListPacket.Read(buffer.AsSpan(0, new ContactListPacket(ContactList.Friends, [new("Ann", true), new("Zed", false)]).Write(buffer)));
        Assert.True(change is { List: ContactList.Ignored, Add: true, Name: "Bob" } && list is { List: ContactList.Friends, Entries: [("Ann", true), ("Zed", false)] },
            "contact messages did not round-trip");
        var length = new ContactChangePacket(ContactList.Friends, false, "Ann").Write(buffer);
        buffer[8 + 4] = 7;
        Assert.Throws<InvalidDataException>(() => ContactChangePacket.Read(buffer.AsSpan(0, length)), "an unknown list was read");
        return Task.CompletedTask;
    }

    private static Task PageAsync()
    {
        var page = new HudFriendsPage();
        for (var index = 0; index < ContactLimits.RecentCount + 1; index++)
        {
            page.Met($"Player{index}");
        }

        page.Met("player3");
        Assert.True(page.Recent.Count == ContactLimits.RecentCount && page.Recent[0] == "player3" && !page.Recent.Contains("Player0"),
            "the recent list did not keep the last seven players met");

        var origin = new Point(539, 155);
        var changes = new List<(ContactList, bool, string)>();
        var whispers = new List<string>();
        page.Changed = (list, add, name) => changes.Add((list, add, name));
        page.Whispered = whispers.Add;
        page.Show(HudFriendsView.Recent);
        page.Click(origin, HudFriendsPage.Row(origin, 1).Center);
        Assert.True(whispers.Count == 0, "a first click whispered");
        page.Click(origin, HudFriendsPage.Row(origin, 1).Center);
        Assert.True(whispers is ["Player7"], "a click on the picked name did not whisper");
        page.Click(origin, HudFriendsPage.Button(origin, 4).Center);
        Assert.True(changes is [(ContactList.Friends, true, "Player7")] && !page.Recent.Contains("Player7"), "make friend did not move the recent player");

        page.Apply(new ContactListInfo(ContactList.Friends, [("Ann", true), ("Zed", false)]));
        page.Click(origin, HudFriendsPage.Button(origin, (int)HudFriendsView.Friends).Center);
        page.Click(origin, HudFriendsPage.Row(origin, 1).Center);
        page.Click(origin, HudFriendsPage.Button(origin, 5).Center);
        Assert.True(page.View == HudFriendsView.Friends && changes[^1] == (ContactList.Friends, false, "Zed"), "delete did not take the picked friend off");
        return Task.CompletedTask;
    }

    // Refuse throws on every change, as storage that is down does.
    private sealed class MemoryContacts : IContactRepository
    {
        public bool Refuse;
        public List<(long Character, ContactList List, string Name)> Rows { get; } = [];

        public Task<IReadOnlyList<ContactRecord>> ListAsync(long characterId, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<ContactRecord>>(Rows.Where(row => row.Character == characterId).Select(row => new ContactRecord(row.List, row.Name)).ToArray());

        public Task<bool> AddAsync(long characterId, ContactList list, string name, CancellationToken ct = default)
        {
            ThrowIfRefused();
            Rows.Add((characterId, list, name));
            return Task.FromResult(true);
        }

        public Task<bool> RemoveAsync(long characterId, ContactList list, string name, CancellationToken ct = default)
        {
            ThrowIfRefused();
            return Task.FromResult(Rows.RemoveAll(row => row.Character == characterId && row.List == list && row.Name == name) > 0);
        }

        private void ThrowIfRefused()
        {
            if (Refuse)
            {
                throw new RefusedStorage();
            }
        }
    }

    private sealed class RefusedStorage() : System.Data.Common.DbException("storage is down");
}
