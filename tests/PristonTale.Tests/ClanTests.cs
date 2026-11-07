using System.Numerics;
using System.Threading.Channels;
using Microsoft.Extensions.DependencyInjection;
using PristonTale.Application.Chat;
using PristonTale.Application.Clans;
using PristonTale.Application.Sessions;
using PristonTale.Application.World;
using PristonTale.Contracts.Characters;
using PristonTale.Contracts.World;
using PristonTale.Persistence.Abstractions;
using PristonTale.Persistence.Abstractions.Accounts;
using PristonTale.Persistence.Abstractions.Characters;
using PristonTale.Persistence.Abstractions.Clans;
using PristonTale.Persistence.Abstractions.WorldData;
using PristonTale.Persistence.Sqlite;
using PristonTale.Protocol.Packets;
using PristonTale.Simulation.Characters;
using PristonTale.Simulation.Social.Clans;
using PristonTale.Simulation.World;
using PristonTale.Simulation.World.Fields;
using PristonTale.Simulation.World.Npcs;

namespace PristonTale.Tests;

internal static class ClanTests
{
    public static IEnumerable<(string, Func<Task>)> Cases()
    {
        yield return ("clan rules: capacity by the chief's level up to 80, names, the six-member registration window and the entrust checks", RulesAsync);
        yield return ("clan messages round-trip: requests, the clan's state with its members, invitations and results", WireAsync);
        yield return ("clan storage: founding pays with the character, members, ranks, dissolution with its cooldown, and loading back", StorageAsync);
        yield return ("founding at the Clan Master takes 500,000 gold at level 40 and refuses a short purse, a low level and a taken name", FoundAsync);
        yield return ("a clan recruits by invitation, registers at six members, keeps one vice chief, expels, lets members leave and is entrusted", MembersAsync);
        yield return ("a storage failure changes no clan nor gold and answers; levels are live; an heir's account cannot lead two clans",
            FailuresAsync);
        yield return ("a clan short of six members past its deadline is dissolved and its members wait four days to join another", ExpiryAsync);
        yield return ("the Clan Master's window founds a clan from a typed name and asks before the chief's actions; a recruiter gets the clan button",
            WindowAsync);
    }

    private static Task RulesAsync()
    {
        Assert.True(ClanRules.Capacity(39) == 0 && ClanRules.Capacity(40) == 20 && ClanRules.Capacity(70) == 50 && ClanRules.Capacity(100) == 80
            && ClanRules.Capacity(180) == 80, "the capacity table differs");
        Assert.True(ClanRules.IsValidName("Lions") && ClanRules.IsValidName("Os Leões_2") && !ClanRules.IsValidName("A") && !ClanRules.IsValidName(" Lions")
            && !ClanRules.IsValidName("Two  Spaces") && !ClanRules.IsValidName("<b>") && !ClanRules.IsValidName(new string('x', 17)), "clan name rules differ");

        var clan = new Clan(1, "Lions", ClanState.Registering, 0, ClanRules.GraceSeconds);
        clan.Add(Member(1, "Chief", 50, ClanRank.Chief));
        for (var index = 2; index <= 5; index++)
        {
            clan.Add(Member(index, $"M{index}", 40, ClanRank.Member));
        }

        Assert.True(ClanRules.Counted(clan, clan.Count, 100) == (ClanState.Registering, ClanRules.GraceSeconds), "five members registered the clan or moved its deadline");
        clan.Add(Member(6, "M6", 60, ClanRank.Member));
        (clan.State, clan.DeadlineSeconds) = ClanRules.Counted(clan, clan.Count, 100);
        Assert.True(clan is { State: ClanState.Registered, DeadlineSeconds: 0 }, "six members did not register the clan");
        clan.Remove(6);
        (clan.State, clan.DeadlineSeconds) = ClanRules.Counted(clan, clan.Count, 1000);
        Assert.True(clan.State == ClanState.Registered && clan.DeadlineSeconds == 1000 + ClanRules.GraceSeconds && !ClanRules.Expired(clan, 1000)
            && ClanRules.Expired(clan, 1000 + ClanRules.GraceSeconds), "a registered clan below six has no countdown");

        var chief = clan.Chief;
        Assert.True(ClanRules.CheckEntrust(clan, chief, clan.Find("M2"), 1_000_000) == ClanStatus.TargetLevelTooLow
            && ClanRules.CheckEntrust(clan, chief, clan.Find("Chief"), 1_000_000) == ClanStatus.NotMember, "entrust checks differ");
        clan.Replace(clan.Find("M2")! with { Level = 50 });
        Assert.True(ClanRules.CheckEntrust(clan, chief, clan.Find("M2"), 299_999) == ClanStatus.NotEnoughGold
            && ClanRules.CheckEntrust(clan, chief, clan.Find("M2"), 300_000) == ClanStatus.Ok, "entrusting a registered clan failed");
        clan.State = ClanState.Registering;
        Assert.True(ClanRules.CheckEntrust(clan, chief, clan.Find("M2"), 300_000) == ClanStatus.NotRegistered, "a registering clan was entrusted");
        return Task.CompletedTask;
    }

    private static Task WireAsync()
    {
        var buffer = new byte[ClanStatePacket.MaxSize];
        var request = ClanRequestPacket.Read(buffer.AsSpan(0, new ClanRequestPacket(ClanAction.Entrust, 77, "Heir").Write(buffer)));
        Assert.True(request is { Action: ClanAction.Entrust, Id: 77, Name: "Heir" }, "the clan request did not round-trip");
        var state = new ClanStatePacket(5, "Lions", 1, 123456, 2, 21, "", 0,
            [new ClanMemberWire("Chief", PlayerClass.Knight, 41, 2, true), new ClanMemberWire("Ann", PlayerClass.Priestess, 30, 0, false)]);
        var read = ClanStatePacket.Read(buffer.AsSpan(0, state.Write(buffer)));
        Assert.True(read.HasClan && read.Name == "Lions" && read.Deadline == 123456 && read.Capacity == 21 && read.Members.Count == 2
            && read.Members[1] == new ClanMemberWire("Ann", PlayerClass.Priestess, 30, 0, false), "the clan state did not round-trip");
        Assert.True(!ClanStatePacket.Read(buffer.AsSpan(0, ClanStatePacket.None.Write(buffer))).HasClan, "no clan read as a clan");
        Assert.True(ClanInvitePacket.Read(buffer.AsSpan(0, new ClanInvitePacket(9, "Lions", "Chief").Write(buffer))) == new ClanInvitePacket(9, "Lions", "Chief")
            && ClanResultPacket.Read(buffer.AsSpan(0, new ClanResultPacket(ClanAction.Found, 8).Write(buffer))) == new ClanResultPacket(ClanAction.Found, 8),
            "invitations or results did not round-trip");
        var length = new ClanRequestPacket(ClanAction.Open, 0, "").Write(buffer);
        buffer[8 + 4] = 99;
        Assert.Throws<InvalidDataException>(() => ClanRequestPacket.Read(buffer.AsSpan(0, length)), "an unknown clan action was read");
        return Task.CompletedTask;
    }

    private static async Task StorageAsync()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"priston-clans-{Guid.NewGuid():N}");
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddPristonSqlite(Path.Combine(directory, "world.db"));
        try
        {
            await using var provider = services.BuildServiceProvider();
            await provider.GetRequiredService<IDatabaseMigrator>().MigrateAsync();
            var account = await provider.GetRequiredService<IAccountRepository>().CreateAsync("keeper", Persistence.Abstractions.Security.PasswordHasher.Hash("secret"));
            var characters = provider.GetRequiredService<ICharacterRepository>();
            var classes = new CharacterClassRegistry();
            var chief = await characters.CreateAsync(CharacterRecordFactory.NewStarter(classes, account.Id, "Chief", 0, PlayerClass.Knight, 3));
            var ann = await characters.CreateAsync(CharacterRecordFactory.NewStarter(classes, account.Id, "Ann", 1, PlayerClass.Priestess, 3));
            var clans = provider.GetRequiredService<IClanRepository>();

            var paid = chief with { Money = 123, SaveSeq = chief.SaveSeq + 1 };
            var id = await clans.FoundAsync(new ClanRecord(0, "Lions", 0, 10, 20, "", 0), new ClanMemberRecord(0, chief.Id, (short)ClanRank.Chief, 10), paid);
            Assert.True(id > 0 && (await characters.FindByNameForAccountAsync(account.Id, "Chief"))!.Money == 123, "founding did not store the payment");
            Assert.True(await clans.FoundAsync(new ClanRecord(0, "LIONS", 0, 10, 20, "", 0), new ClanMemberRecord(0, ann.Id, 2, 10),
                ann with { SaveSeq = ann.SaveSeq + 1 }) == 0, "a taken name founded a second clan");
            Assert.True(await clans.AddMemberAsync(new ClanMemberRecord(id, ann.Id, 0, 11), 0, 20) && !await clans.AddMemberAsync(new ClanMemberRecord(id, ann.Id, 0, 12), 0, 20),
                "a member was added twice");
            Assert.True(await clans.SetRanksAsync(id, [new ClanRankChange(ann.Id, (short)ClanRank.Vice)]) && await clans.IsMemberAsync(ann.Id), "ranks did not change");
            Assert.True(await clans.ClanNamesAsync(account.Id) is { Count: 2 } names && names[ann.Id] == "Lions" && names[chief.Id] == "Lions",
                "the account's clan names differ");
            var sessions = new SessionRegistry();
            var listing = new Application.Services.CharacterAppService(sessions, characters, classes, new WorldDataHolder(), new WorldCatalog(), clans);
            Assert.True((await listing.ListCharactersAsync(sessions.Create(account.Id, "keeper").SessionToken)).All(summary => summary.ClanName == "Lions"),
                "the character list did not carry the clan");
            var (loaded, members) = await clans.LoadAsync();
            Assert.True(loaded is [{ Name: "Lions", State: 0, DeadlineAt: 20 }] && members.Count == 2
                && members.Single(member => member.CharacterId == ann.Id) is { Rank: 1, Name: "Ann", JobCode: (int)PlayerClass.Priestess, AccountId: var owner }
                && owner == account.Id, "the clan did not load back");
            Assert.True(await clans.RemoveMemberAsync(id, ann.Id, 0, 30) && !await clans.IsMemberAsync(ann.Id), "the member was not removed");
            Assert.True(await clans.DisbandAsync(id, [chief.Id], 999) && await clans.CooldownAsync(chief.Id) == 999 && await clans.CooldownAsync(ann.Id) == 0
                && (await clans.LoadAsync()).Clans.Count == 0, "the dissolution or its cooldown differs");
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            Directory.Delete(directory, recursive: true);
        }
    }

    private static async Task FoundAsync()
    {
        await using var poor = await Fixture.CreateAsync(499_999, 40);
        var poorStatus = await poor.Service.FoundAsync(poor.Session.SessionToken, poor.NpcId, "Lions");
        Assert.True(poorStatus == ClanStatus.NotEnoughGold, $"a short purse founded a clan ({poorStatus})");
        await using var young = await Fixture.CreateAsync(1_000_000, 39);
        Assert.True(await young.Service.FoundAsync(young.Session.SessionToken, young.NpcId, "Lions") == ClanStatus.LevelTooLow, "a level 39 founded a clan");
        Assert.True(await young.Service.FoundAsync(young.Session.SessionToken, young.NpcId, "<b>") == ClanStatus.NameInvalid, "an invalid name was taken");

        await using var fixture = await Fixture.CreateAsync(1_000_000, 40);
        Assert.True(await fixture.Service.FoundAsync(fixture.Session.SessionToken, 0, "Lions") == ClanStatus.OutOfReach, "a clan was founded far from the master");
        Assert.True(await fixture.Service.FoundAsync(fixture.Session.SessionToken, fixture.NpcId, "Lions") == ClanStatus.Ok, "founding failed");
        Assert.True(fixture.Session.Character!.Money == 500_000 && fixture.Characters.Record.Money == 500_000, "the fee was not taken and stored");
        Assert.True(fixture.Link.View is { Name: "Lions", Rank: ClanRank.Chief, State: ClanState.Registering, Capacity: 20 } view
            && view.DeadlineSeconds > 0 && fixture.Player.Clan?.Name == "Lions" && fixture.Service.ClanOf(fixture.Session.EntityId) != 0,
            "the founder did not get its clan, tag and chat roster");
        Assert.True(await fixture.Service.FoundAsync(fixture.Session.SessionToken, fixture.NpcId, "Tigers") == ClanStatus.AlreadyInClan, "a chief founded twice");
        var other = await fixture.AddMemberAsync("Other", 2, 50, account: 2);
        Assert.True(await fixture.Service.FoundAsync(other.Session.SessionToken, fixture.NpcId, "lions") is not ClanStatus.Ok, "a taken name founded a clan");
    }

    private static async Task MembersAsync()
    {
        await using var fixture = await Fixture.CreateAsync(1_000_000, 60);
        var service = fixture.Service;
        Assert.True(await service.FoundAsync(fixture.Session.SessionToken, fixture.NpcId, "Lions") == ClanStatus.Ok, "founding failed");
        var chiefId = fixture.Session.EntityId;
        var recruits = new List<Fixture.Extra>();
        for (var index = 0; index < 5; index++)
        {
            recruits.Add(await fixture.AddMemberAsync($"Recruit{index}", 10 + index, 40 + index));
        }

        var first = recruits[0];
        Assert.True(await service.InviteAsync(first.Session.EntityId, chiefId) == ClanStatus.NotInClan, "a player without a clan recruited");
        Assert.True(await service.InviteAsync(chiefId, first.Session.EntityId) == ClanStatus.Ok && first.Link.Invitation is { ClanName: "Lions", ChiefName: "Founder" },
            "the invitation did not reach the player");
        Assert.True(await service.AnswerAsync(first.Session.EntityId, false) == ClanStatus.InviteRefused
            && await service.AnswerAsync(first.Session.EntityId, true) == ClanStatus.NoInvitation, "a refused invitation still joined");
        foreach (var recruit in recruits)
        {
            Assert.True(await service.InviteAsync(chiefId, recruit.Session.EntityId) == ClanStatus.Ok
                && await service.AnswerAsync(recruit.Session.EntityId, true) == ClanStatus.Ok, $"{recruit.Name} did not join");
        }

        Assert.True(fixture.Link.View is { State: ClanState.Registered, DeadlineSeconds: 0, Members.Count: 6 } && first.Link.View?.Rank == ClanRank.Member,
            "six members did not register the clan");
        Assert.True(await service.InviteAsync(chiefId, first.Session.EntityId) == ClanStatus.TargetInClan, "a member was invited again");

        Assert.True(await service.SetViceAsync(first.Session.EntityId, "Recruit1", true) == ClanStatus.NotChief
            && await service.SetViceAsync(chiefId, "Recruit1", true) == ClanStatus.Ok && await service.SetViceAsync(chiefId, "Recruit2", true) == ClanStatus.ViceExists,
            "vice chief rules differ");
        var outsider = await fixture.AddMemberAsync("Outsider", 30, 40);
        Assert.True(await service.InviteAsync(recruits[1].Session.EntityId, outsider.Session.EntityId) == ClanStatus.Ok
            && await service.AnswerAsync(outsider.Session.EntityId, true) == ClanStatus.Ok, "the vice chief could not recruit");

        Assert.True(await service.LeaveAsync(chiefId) == ClanStatus.ChiefCannotLeave, "the chief left the clan");
        Assert.True(await service.LeaveAsync(outsider.Session.EntityId) == ClanStatus.Ok && outsider.Link.View is null && service.ClanOf(outsider.Session.EntityId) == 0,
            "a member could not leave");
        Assert.True(await service.ExpelAsync(recruits[1].Session.EntityId, "Recruit4") == ClanStatus.NotChief
            && await service.ExpelAsync(chiefId, "Recruit4") == ClanStatus.Ok && recruits[4].Link.View is null
            && fixture.Link.View is { State: ClanState.Registered, Members.Count: 5 } view && view.DeadlineSeconds > 0,
            "expelling below six did not start the countdown");

        Assert.True(await service.EntrustAsync(fixture.Session.SessionToken, fixture.NpcId, "Recruit3") == ClanStatus.TargetLevelTooLow,
            "the clan was handed to a lower level");
        var heir = await fixture.AddMemberAsync("Heir", 40, 70);
        Assert.True(await service.InviteAsync(chiefId, heir.Session.EntityId) == ClanStatus.Ok && await service.AnswerAsync(heir.Session.EntityId, true) == ClanStatus.Ok,
            "the heir did not join");
        var entrusted = await service.EntrustAsync(fixture.Session.SessionToken, fixture.NpcId, "Heir");
        Assert.True(entrusted == ClanStatus.Ok && heir.Link.View?.Rank == ClanRank.Chief
            && fixture.Link.View?.Rank == ClanRank.Member && fixture.Session.Character!.Money == 1_000_000 - 500_000 - 300_000,
            $"entrusting the clan differs ({entrusted}, {heir.Link.View?.Rank}, {fixture.Link.View?.Rank}, {fixture.Session.Character!.Money})");
        Assert.True(await service.DisbandAsync(chiefId) == ClanStatus.NotChief && await service.DisbandAsync(heir.Session.EntityId) == ClanStatus.Ok
            && fixture.Link.View is null && fixture.Player.Clan is null && fixture.Clans.Cooldowns.Count == 6, "the clan was not disbanded");
    }

    private static async Task FailuresAsync()
    {
        await using var fixture = await Fixture.CreateAsync(1_000_000, 60);
        var service = fixture.Service;
        fixture.Clans.Fail = true;
        Assert.True(await service.FoundAsync(fixture.Session.SessionToken, fixture.NpcId, "Lions") == ClanStatus.Unavailable
            && fixture.Session.Character!.Money == 1_000_000 && fixture.Link.View is null && service.ClanOf(fixture.Session.EntityId) == 0,
            "a failed founding kept the clan or the fee");
        fixture.Clans.Fail = false;
        Assert.True(await service.FoundAsync(fixture.Session.SessionToken, fixture.NpcId, "Lions") == ClanStatus.Ok, "founding failed");
        var chiefId = fixture.Session.EntityId;

        var heir = await fixture.AddMemberAsync("Heir", 20, 40, account: 2);
        Assert.True(await service.InviteAsync(chiefId, heir.Session.EntityId) == ClanStatus.Ok, "the invitation failed");
        fixture.Clans.Fail = true;
        Assert.True(await service.AnswerAsync(heir.Session.EntityId, true) == ClanStatus.Unavailable && heir.Link.View is null
            && fixture.Link.View is { Members.Count: 1 }, "a join storage refused still added the member");
        fixture.Clans.Fail = false;
        Assert.True(await service.InviteAsync(chiefId, heir.Session.EntityId) == ClanStatus.Ok && await service.AnswerAsync(heir.Session.EntityId, true) == ClanStatus.Ok,
            "the heir did not join");
        for (var index = 0; index < 4; index++)
        {
            var recruit = await fixture.AddMemberAsync($"Recruit{index}", 30 + index, 40);
            Assert.True(await service.InviteAsync(chiefId, recruit.Session.EntityId) == ClanStatus.Ok
                && await service.AnswerAsync(recruit.Session.EntityId, true) == ClanStatus.Ok, "a recruit did not join");
        }

        Assert.True(await service.EntrustAsync(fixture.Session.SessionToken, fixture.NpcId, "Heir") == ClanStatus.TargetLevelTooLow, "a level 40 heir took the clan");
        heir.Session.Character = new CharacterClassRegistry().CreatePlayer(PlayerClass.Fighter, "Heir", 70);
        Assert.True(fixture.Link.View is { State: ClanState.Registered } && heir.Link.View is not null, "the clan did not register");

        // Loading back reads each member's name and account from its character, as the database joins them.
        foreach (var (id, member) in fixture.Clans.Members.ToArray())
        {
            var (memberName, account) = id switch { 1 => ("Founder", 1L), 20 => ("Heir", 2L), _ => ($"Recruit{id - 30}", 1L) };
            fixture.Clans.Members[id] = member with { Name = memberName, AccountId = account, Level = 40 };
        }

        fixture.Clans.Clans[99] = new ClanRecord(99, "Tigers", (short)ClanState.Registering, 0, ClanRules.GraceSeconds, "", 0);
        fixture.Clans.Members[98] = new ClanMemberRecord(99, 98, (short)ClanRank.Chief, 0, "Other", (int)PlayerClass.Fighter, 50, AccountId: 2);
        await service.LoadAsync();
        Assert.True(await service.EntrustAsync(fixture.Session.SessionToken, fixture.NpcId, "Heir") == ClanStatus.ChiefOnAccount,
            "the heir's account came to lead two clans");
        fixture.Clans.Members.Remove(98);
        fixture.Clans.Clans.Remove(99);
        await service.LoadAsync();
        Assert.True(await service.EntrustAsync(fixture.Session.SessionToken, fixture.NpcId, "Heir") == ClanStatus.Ok && heir.Link.View?.Rank == ClanRank.Chief,
            "the heir at its live level 70 did not take the clan");
    }

    private static async Task ExpiryAsync()
    {
        var clock = new ManualTime(DateTimeOffset.FromUnixTimeSeconds(1_000_000));
        await using var fixture = await Fixture.CreateAsync(1_000_000, 40, clock);
        Assert.True(await fixture.Service.FoundAsync(fixture.Session.SessionToken, fixture.NpcId, "Lions") == ClanStatus.Ok, "founding failed");
        clock.Advance(TimeSpan.FromSeconds(ClanRules.GraceSeconds - 1));
        await fixture.Service.ExpireAsync();
        Assert.True(fixture.Link.View is not null, "the clan was dissolved before its deadline");
        clock.Advance(TimeSpan.FromSeconds(1));
        await fixture.Service.ExpireAsync();
        Assert.True(fixture.Link.View is null && fixture.Clans.Clans.Count == 0, "the clan outlived its deadline");
        Assert.True(await fixture.Service.FoundAsync(fixture.Session.SessionToken, fixture.NpcId, "Tigers") == ClanStatus.JoinCooldown,
            "the dissolved clan's chief founded another at once");
        clock.Advance(TimeSpan.FromSeconds(ClanRules.JoinCooldownSeconds));
        Assert.True(await fixture.Service.FoundAsync(fixture.Session.SessionToken, fixture.NpcId, "Tigers") == ClanStatus.Ok, "the cooldown never ended");
    }

    private static Task WindowAsync()
    {
        var sent = new List<(ClanAction Action, ulong Id, string Name)>();
        var window = new Gameplay.UI.Clans.HudClanWindow { Send = (action, id, name) => sent.Add((action, id, name)) };
        window.Open(42);
        Assert.True(window.IsOpen && !window.Typing && sent is [(ClanAction.Open, 42, "")], "opening did not ask for the clan");
        Click(window, 0, 1);
        Assert.True(window.Typing, "the founding button did not ask for the name");
        window.Type("Lions", key => key == Microsoft.Xna.Framework.Input.Keys.Enter, static _ => true);
        Assert.True(sent[^1] == (ClanAction.Found, 42, "Lions") && window.Pending, "the typed name was not sent at the Clan Master");

        window.Observe(new Client.Net.Presence.ClanInfo(7, "Lions", ClanState.Registering, 0, ClanRank.Chief, 20, "", 0,
            [new Client.Net.Presence.ClanMemberInfo("Founder", PlayerClass.Knight, 40, ClanRank.Chief, true)]));
        window.Result(new Client.Net.Presence.ClanResultInfo((byte)ClanAction.Found, ClanStatus.Ok));
        Assert.True(!window.Pending && !window.Typing && window.Clan.Rank == ClanRank.Chief, "the founded clan was not shown");
        var count = sent.Count;
        Click(window, 3, 4);
        Assert.True(sent.Count == count, "disbanding did not ask first");
        Click(window, 0, 2);
        Assert.True(sent[^1] == (ClanAction.Disband, 0, "") && window.Pending, "the confirmed disband was not sent");
        window.Result(new Client.Net.Presence.ClanResultInfo((byte)ClanAction.Accept, ClanStatus.Ok));
        Assert.True(window.Pending, "another request's result ended the disband's wait");
        window.Click(Gameplay.UI.Widgets.HudNpcPanel.OkButton.Center);
        Assert.True(!window.IsOpen, "OK did not close a window waiting for its result");

        var frame = new Gameplay.UI.Widgets.HudTargetFrame(new Microsoft.Xna.Framework.Rectangle(300, 10, 200, 60), default, default, default, 0, 0);
        var menu = Gameplay.UI.Parties.HudPlayerButtons.Menu(frame, clan: true);
        var buttons = Enum.GetValues<Gameplay.UI.Parties.HudPlayerButton>().Select(button => Gameplay.UI.Parties.HudPlayerButtons.Button(frame, button, clan: true))
            .ToArray();
        Assert.True(menu.Width == 120 && buttons.All(button => menu.Contains(button.Center))
            && Gameplay.UI.Parties.HudPlayerButtons.At(frame, buttons[3].Center, clan: true) == Gameplay.UI.Parties.HudPlayerButton.Clan
            && Gameplay.UI.Parties.HudPlayerButtons.At(frame, buttons[3].Center) is null, "the clan button differs");
        return Task.CompletedTask;
    }

    private static void Click(Gameplay.UI.Clans.HudClanWindow window, int index, int count) =>
        window.Click(Gameplay.UI.Widgets.HudNpcPanel.ServiceButton(index, count).Center);

    private static ClanMember Member(long id, string name, int level, ClanRank rank) => new(id, id, name, PlayerClass.Fighter, level, rank, 0);

    private sealed class ManualTime(DateTimeOffset now) : TimeProvider
    {
        private DateTimeOffset _now = now;

        public override DateTimeOffset GetUtcNow() => _now;

        public void Advance(TimeSpan by) => _now += by;
    }

    private sealed class RecordingLink : IClanLink
    {
        public ClanView? View { get; private set; }
        public ClanInvitation? Invitation { get; private set; }

        public void Deliver(ClanView? view) => View = view;

        public void Invite(ClanInvitation invitation) => Invitation = invitation;
    }

    // The clan rows in memory; a payment stores the character through MemoryCharacters.
    private sealed class MemoryClans(MemoryCharacters characters) : IClanRepository
    {
        public Dictionary<long, ClanRecord> Clans { get; } = new();
        public Dictionary<long, ClanMemberRecord> Members { get; } = new();
        public Dictionary<long, long> Cooldowns { get; } = new();
        private long _nextId = 1;

        // Every write throws as a locked database does.
        public bool Fail { get; set; }

        public Task<(IReadOnlyList<ClanRecord> Clans, IReadOnlyList<ClanMemberRecord> Members)> LoadAsync(CancellationToken ct = default) =>
            Task.FromResult<(IReadOnlyList<ClanRecord>, IReadOnlyList<ClanMemberRecord>)>(([.. Clans.Values], [.. Members.Values]));

        public async Task<long> FoundAsync(ClanRecord clan, ClanMemberRecord chief, CharacterRecord payment, CancellationToken ct = default)
        {
            ThrowIfFailing();
            if (Clans.Values.Any(existing => existing.Name.Equals(clan.Name, StringComparison.OrdinalIgnoreCase)) || !await PayAsync(payment))
            {
                return 0;
            }

            var id = _nextId++;
            Clans[id] = clan with { Id = id };
            Members[chief.CharacterId] = chief with { ClanId = id };
            return id;
        }

        public Task<bool> AddMemberAsync(ClanMemberRecord member, short state, long deadlineAt, CancellationToken ct = default)
        {
            ThrowIfFailing();
            if (!Members.TryAdd(member.CharacterId, member))
            {
                return Task.FromResult(false);
            }

            Clans[member.ClanId] = Clans[member.ClanId] with { State = state, DeadlineAt = deadlineAt };
            return Task.FromResult(true);
        }

        public Task<bool> RemoveMemberAsync(long clanId, long characterId, short state, long deadlineAt, CancellationToken ct = default)
        {
            var removed = Members.Remove(characterId);
            Clans[clanId] = Clans[clanId] with { State = state, DeadlineAt = deadlineAt };
            return Task.FromResult(removed);
        }

        public async Task<bool> SetRanksAsync(long clanId, IReadOnlyList<ClanRankChange> changes, CharacterRecord? payment = null, CancellationToken ct = default)
        {
            ThrowIfFailing();
            if (payment is not null && !await PayAsync(payment))
            {
                return false;
            }

            foreach (var change in changes)
            {
                Members[change.CharacterId] = Members[change.CharacterId] with { Rank = change.Rank };
            }

            return true;
        }

        public Task<bool> DisbandAsync(long clanId, IReadOnlyList<long> members, long cooldownUntil, CancellationToken ct = default)
        {
            Clans.Remove(clanId);
            foreach (var member in members)
            {
                Members.Remove(member);
                Cooldowns[member] = cooldownUntil;
            }

            return Task.FromResult(true);
        }

        public Task<long> CooldownAsync(long characterId, CancellationToken ct = default) => Task.FromResult(Cooldowns.GetValueOrDefault(characterId));

        public Task<bool> IsMemberAsync(long characterId, CancellationToken ct = default) => Task.FromResult(Members.ContainsKey(characterId));

        public Task<IReadOnlyDictionary<long, string>> ClanNamesAsync(long accountId, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyDictionary<long, string>>(Members.Values.Where(member => member.AccountId == accountId)
                .ToDictionary(member => member.CharacterId, member => Clans[member.ClanId].Name));

        private void ThrowIfFailing()
        {
            if (Fail)
            {
                throw new Microsoft.Data.Sqlite.SqliteException("database is locked", 5);
            }
        }

        private async Task<bool> PayAsync(CharacterRecord payment) => payment.Id != characters.Record.Id || await characters.UpdateAsync(payment);
    }

    // One founder in the world beside a Clan Master; other players are sessions the service knows (no field needed). The
    // world ticks in the background until the fixture is disposed.
    private sealed class Fixture : IAsyncDisposable
    {
        private readonly CancellationTokenSource _stop = new();
        private Task _ticker = Task.CompletedTask;

        public required WorldServer World { get; init; }
        public required ulong NpcId { get; init; }
        public required PlayerSession Session { get; init; }
        public required SessionRegistry Sessions { get; init; }
        public required ClanAppService Service { get; init; }
        public required MemoryCharacters Characters { get; init; }
        public required MemoryClans Clans { get; init; }
        public required WorldPlayer Player { get; init; }
        public required RecordingLink Link { get; init; }

        public sealed record Extra(string Name, PlayerSession Session, RecordingLink Link);

        public async ValueTask DisposeAsync()
        {
            await _stop.CancelAsync();
            await _ticker;
            _stop.Dispose();
        }

        private async Task TickAsync()
        {
            uint tick = 10_000;
            while (!_stop.IsCancellationRequested)
            {
                World.Tick(0.05f, tick++);
                await Task.Delay(1);
            }
        }

        public async Task<Extra> AddMemberAsync(string name, long characterId, int level, long account = 1)
        {
            var session = Sessions.Create(account, name);
            var character = new CharacterClassRegistry().CreatePlayer(PlayerClass.Fighter, name, level);
            session.Character = character;
            session.Loaded = CharacterRecordFactory.NewStarter(new CharacterClassRegistry(), account, name, 1, PlayerClass.Fighter, 3) with { Id = characterId };
            var link = new RecordingLink();
            await Service.AttachAsync(session, name, PlayerClass.Fighter, level, link);
            return new Extra(name, session, link);
        }

        public static async Task<Fixture> CreateAsync(long money, int level, TimeProvider? clock = null)
        {
            var classes = new CharacterClassRegistry();
            var starter = CharacterRecordFactory.NewStarter(classes, 1, "Founder", 0, PlayerClass.Fighter, 3) with { Id = 1 };
            var character = classes.CreatePlayer(PlayerClass.Fighter, starter.Name, level);
            character.RestoreProgress(0, money);
            var characters = new MemoryCharacters(CharacterRecordFactory.FromLiveSession(starter, character, 3, new Vector3(100, 0, 100)));
            var npc = new NpcDefinition(1, "Clan Master", "master.inx", [new NpcService(NpcServiceKind.Clan)]) { DisplayName = "Clan Master" };
            var empty = WorldData.Empty;
            var holder = new WorldDataHolder
            {
                Current = new WorldData(empty.Items, empty.Monsters, new NpcCatalog([npc]),
                    new Dictionary<int, FieldSpawnData> { [3] = FieldSpawnData.Empty with { FixedNpcs = [new FixedNpc(1, 100 * 256, 100 * 256, 0)] } },
                    empty.Motions)
            };
            var catalog = new WorldCatalog();
            var world = new WorldServer(holder, catalog);
            var sessions = new SessionRegistry();
            var session = sessions.Create(1, "founder");
            var sessionService = new Application.Services.SessionAppService(sessions, classes, characters, new ImmediateSaves(characters), catalog, world, holder);
            Assert.True((await sessionService.EnterWorldAsync(session.SessionToken, starter.Name)).Ok && session.TryBeginPlay(), "entry failed");
            var outbox = Channel.CreateUnbounded<WorldSnapshot>();
            var player = WorldPlayerFactory.Create(session);
            await WorldPump.RunAsync(world, world.JoinAsync(3, player, outbox.Writer, session));
            Assert.True(outbox.Reader.TryRead(out var snapshot), "roster missing");
            var placed = snapshot.Entities.Single(entity => entity.Kind == EntityKind.Npc);
            var clans = new MemoryClans(characters);
            var service = new ClanAppService(clans, world, new ChatDirectory(), sessions, new ImmediateSaves(characters), clock);
            await service.LoadAsync();
            var link = new RecordingLink();
            await WorldPump.RunAsync(world, service.AttachAsync(session, starter.Name, PlayerClass.Fighter, level, link));
            var fixture = new Fixture
            {
                World = world,
                NpcId = placed.EntityId,
                Session = session,
                Sessions = sessions,
                Service = service,
                Characters = characters,
                Clans = clans,
                Player = player,
                Link = link
            };
            fixture._ticker = Task.Run(fixture.TickAsync);
            return fixture;
        }
    }
}
