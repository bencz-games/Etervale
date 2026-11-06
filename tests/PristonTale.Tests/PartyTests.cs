using PristonTale.Persistence.Abstractions.GameLogs;
using System.Numerics;
using PristonTale.Application.Chat;
using PristonTale.Application.Services;
using PristonTale.Application.World;
using PristonTale.Contracts.Characters;
using PristonTale.Contracts.Social;
using PristonTale.Protocol.Packets;
using PristonTale.Simulation.Characters;
using PristonTale.Simulation.Combat;
using PristonTale.Simulation.Items;
using PristonTale.Simulation.Parties;
using PristonTale.Simulation.Progression;
using PristonTale.Simulation.World;
using PristonTale.Simulation.World.Fields;
using PristonTale.Simulation.World.Monsters;
using PristonTale.Simulation.World.Npcs;
using PristonTale.Simulation.World.Spawning;

namespace PristonTale.Tests;

// Parties on the server: requests and answers, the leader's commands, leaving the world, and the experience and gold
// the members share.
internal static class PartyTests
{
    public static IEnumerable<(string, Func<Task>)> Cases()
    {
        yield return ("a party request answered yes forms a party: the leader first, its members hear party lines; an old request expires", FormAsync);
        yield return ("an answer joins a party only while the one who asked or answers still leads it and the levels still match", LateAnswerAsync);
        yield return ("a member's request goes to its leader, who asks in the party's name; the level gap, other parties and a full party refuse", RequestsAsync);
        yield return ("the leader delegates, kicks and disbands, a member only leaves, and a party of two is released", CommandsAsync);
        yield return ("a player leaving the world leaves its party: its leader hands over, a party of two is released", LeaveWorldAsync);
        yield return ("a kill's experience is taken at the party's average level and shared by size with the living members in range", ExperienceAsync);
        yield return ("gold under 10000 picked up is split with the members in range, the picker's part stored; a member who cannot carry its part or whose save gate is held is left out", GoldAsync);
        yield return ("party rules: shares by size, the average level, the level gap, the share range and the gold split", RulesAsync);
        yield return ("party messages round-trip on the wire and refuse unknown commands and answers", WireAsync);
    }

    private static (ChatFixture Fixture, PartyAppService Parties) Fixture(TimeProvider? clock = null)
    {
        var fixture = new ChatFixture(clock: clock);
        return (fixture, new PartyAppService(fixture.Sessions, fixture.World, fixture.Directory));
    }

    private static Task Invite(ChatFixture fixture, PartyAppService parties, ChatFixture.Player from, ChatFixture.Player to) =>
        fixture.RunAsync(parties.InviteAsync(from.Session.SessionToken, to.Id));

    private static Task Answer(ChatFixture fixture, PartyAppService parties, ChatFixture.Player player, ChatFixture.Player from, bool accept = true) =>
        fixture.RunAsync(parties.AnswerAsync(player.Session.SessionToken, from.Id, accept));

    private static Task Command(ChatFixture fixture, PartyAppService parties, ChatFixture.Player player, PartyCommand command, ulong member = 0) =>
        fixture.RunAsync(parties.CommandAsync(player.Session.SessionToken, command, member));

    private static async Task Group(ChatFixture fixture, PartyAppService parties, ChatFixture.Player leader, ChatFixture.Player member)
    {
        await Invite(fixture, parties, leader, member);
        await Answer(fixture, parties, member, leader);
    }

    // The members the player's latest snapshot shows; empty without a party.
    private static ulong[] Members(ChatFixture fixture, ChatFixture.Player player)
    {
        fixture.Tick(1);
        return ChatFixture.Latest(player)?.Party?.Members.Select(member => member.EntityId).ToArray() ?? [];
    }

    private static async Task FormAsync()
    {
        var (fixture, parties) = Fixture();
        var ann = await fixture.JoinAsync("Ann", 0, 0);
        var bob = await fixture.JoinAsync("Bob", 100, 0);
        await Invite(fixture, parties, ann, bob);
        Assert.True(ann.Link.Last == "sent a party request to Bob", $"inviter reply: {ann.Link.Last}");
        fixture.Tick(1);
        Assert.True(ChatFixture.Latest(bob)?.PartyRequest is { FromId: var from, FromName: "Ann" } && from == ann.Id, "the request did not reach Bob");

        await fixture.RunAsync(parties.AnswerAsync(bob.Session.SessionToken, 999, true));
        Assert.True(Members(fixture, ann).Length == 0, "an answer to nobody's request formed a party");
        await Answer(fixture, parties, bob, ann);
        Assert.True(Members(fixture, ann).SequenceEqual([ann.Id, bob.Id]) && Members(fixture, bob).SequenceEqual([ann.Id, bob.Id]),
            "the party must list its leader first");
        Assert.True(fixture.World.Parties.MembersOf(bob.Id).Contains(ann.Id), "the chat roster missed the party");

        await fixture.RunAsync(fixture.Chat.PartyAsync(ann.Id, "pull"));
        Assert.True(bob.Link.Lines[^1] == new ChatLine(ChatKind.Party, ann.Id, "Ann: pull"), "a party line missed the other member");

        var clock = new SpecialItemTests.ManualClock();
        (fixture, parties) = Fixture(clock);
        var cid = await fixture.JoinAsync("Cid", 0, 0);
        var dan = await fixture.JoinAsync("Dan", 100, 0);
        await Invite(fixture, parties, cid, dan);
        clock.Now += TimeSpan.FromMilliseconds(PartyRules.InviteMs + 1);
        await Answer(fixture, parties, dan, cid);
        Assert.True(Members(fixture, dan).Length == 0, "an expired request formed a party");
    }

    private static async Task LateAnswerAsync()
    {
        var (fixture, parties) = Fixture();
        var ann = await fixture.JoinAsync("Ann", 0, 0);
        var bob = await fixture.JoinAsync("Bob", 100, 0);
        var dan = await fixture.JoinAsync("Dan", 0, 100);
        var eve = await fixture.JoinAsync("Eve", 100, 100);
        await Group(fixture, parties, dan, eve);

        // Ann asks Bob, Bob joins Dan's party, then answers Ann: Dan never agreed to take Ann.
        await Invite(fixture, parties, ann, bob);
        await Invite(fixture, parties, dan, bob);
        await Answer(fixture, parties, bob, dan);
        await Answer(fixture, parties, bob, ann);
        Assert.True(Members(fixture, ann).Length == 0 && Members(fixture, dan).SequenceEqual([dan.Id, eve.Id, bob.Id]),
            "a member's late answer took a player into its party");

        // Ann asks Fay, then joins Dan's party as a member: Fay's yes must not take her into it.
        var fay = await fixture.JoinAsync("Fay", 50, 50);
        await Invite(fixture, parties, ann, fay);
        await Invite(fixture, parties, dan, ann);
        await Answer(fixture, parties, ann, dan);
        await Answer(fixture, parties, fay, ann);
        Assert.True(Members(fixture, fay).Length == 0 && Members(fixture, dan).Length == 4, "a request outlived its sender's leadership");

        // A request made while the levels matched is refused once they no longer do.
        var gus = await fixture.JoinAsync("Gus", 0, -50, level: 20);
        var hal = await fixture.JoinAsync("Hal", 0, -100, level: 20);
        await Invite(fixture, parties, gus, hal);
        hal.Session.Character!.AwardExperience(ExperienceTable.ForLevel(40) - hal.Session.Character.Experience);
        await Answer(fixture, parties, hal, gus);
        Assert.True(Members(fixture, gus).Length == 0 && hal.Link.LastLine == PartyMessages.LevelGap, "a request joined levels that grew apart");
    }

    private static async Task RequestsAsync()
    {
        var (fixture, parties) = Fixture();
        var ann = await fixture.JoinAsync("Ann", 0, 0);
        var bob = await fixture.JoinAsync("Bob", 100, 0);
        var cid = await fixture.JoinAsync("Cid", 0, 100);
        await Group(fixture, parties, ann, bob);

        await fixture.SayAsync(bob, "//PARTY Cid");
        Assert.True(bob.Link.Last == "Party Leader Ann has recommended Cid to join the party", $"member reply: {bob.Link.Last}");
        fixture.Tick(1);
        Assert.True(ChatFixture.Latest(ann)?.PartyRequest is { FromName: "Bob", JoinerName: "Cid", Recommendation: true },
            "the recommendation did not reach the leader with the player's name");
        Assert.True(ChatFixture.Latest(cid)?.PartyRequest is null, "the recommended player was asked before the leader agreed");
        await Answer(fixture, parties, ann, bob);
        fixture.Tick(1);
        Assert.True(ann.Link.Last == "sent a party request to Cid" && ChatFixture.Latest(cid)?.PartyRequest is { FromName: "Ann" },
            "the leader's agreement must ask the player in the party's name");
        await Answer(fixture, parties, cid, ann);
        Assert.True(Members(fixture, cid).SequenceEqual([ann.Id, bob.Id, cid.Id]) && bob.Link.Last == "Cid has entered the party", "Cid did not join");

        var dan = await fixture.JoinAsync("Dan", 0, -100, level: 40);
        await Invite(fixture, parties, ann, dan);
        Assert.True(ann.Link.LastLine == PartyMessages.LevelGap, "a 39-level gap was asked");
        await Invite(fixture, parties, ann, bob);
        Assert.True(ann.Link.Last == "Bob is already a party member", "a member was asked again");
        var eve = await fixture.JoinAsync("Eve", -100, 0);
        var fay = await fixture.JoinAsync("Fay", -100, 100);
        await Group(fixture, parties, eve, fay);
        await Invite(fixture, parties, ann, fay);
        Assert.True(ann.Link.Last == "Fay is already a member of other party", "a member of another party was asked");
        await fixture.SayAsync(ann, "//party Nobody");
        Assert.True(ann.Link.Last == "Nobody is currently offline, or cannot be found", "an unknown name was not answered");

        // A leader asked by a player alone answers for its party.
        var gus = await fixture.JoinAsync("Gus", 50, 50);
        await Invite(fixture, parties, gus, eve);
        await Answer(fixture, parties, eve, gus);
        Assert.True(Members(fixture, gus).SequenceEqual([eve.Id, fay.Id, gus.Id]), "the player alone did not join the leader's party");

        for (var index = 0; index < 3; index++)
        {
            var extra = await fixture.JoinAsync("Extra" + index, 10 * index, 10);
            await Group(fixture, parties, ann, extra);
        }

        var late = await fixture.JoinAsync("Late", 20, 20);
        await Group(fixture, parties, ann, late);
        Assert.True(Members(fixture, ann).Length == PartyLimits.MaxMembers && late.Link.LastLine == PartyMessages.CannotEnter, "a full party took a seventh");
    }

    private static async Task CommandsAsync()
    {
        var (fixture, parties) = Fixture();
        var ann = await fixture.JoinAsync("Ann", 0, 0);
        var bob = await fixture.JoinAsync("Bob", 100, 0);
        var cid = await fixture.JoinAsync("Cid", 0, 100);
        await Group(fixture, parties, ann, bob);
        await Group(fixture, parties, ann, cid);

        await Command(fixture, parties, bob, PartyCommand.Kick, cid.Id);
        await Command(fixture, parties, bob, PartyCommand.Disband);
        Assert.True(Members(fixture, ann).Length == 3, "a member used the leader's buttons");

        await Command(fixture, parties, ann, PartyCommand.Delegate, bob.Id);
        Assert.True(Members(fixture, cid).SequenceEqual([bob.Id, ann.Id, cid.Id]) && ann.Link.Texts().Contains("party leader is now Bob")
            && ann.Link.Last == "Bob has been delegated as the new party leader", "delegating failed");

        await Command(fixture, parties, bob, PartyCommand.Kick, cid.Id);
        Assert.True(Members(fixture, cid).Length == 0 && Members(fixture, ann).SequenceEqual([bob.Id, ann.Id])
            && ann.Link.Texts().Contains("Cid has left the party") && ann.Link.Last == "Cid has been expelled from the party", "kicking failed");

        await Command(fixture, parties, ann, PartyCommand.Leave);
        Assert.True(Members(fixture, bob).Length == 0 && bob.Link.Last == "party has been disbanded" && ann.Link.LastLine == PartyMessages.YouLeft,
            "a party of two must be released when one leaves");

        await Group(fixture, parties, ann, bob);
        await Command(fixture, parties, ann, PartyCommand.Disband);
        Assert.True(Members(fixture, bob).Length == 0 && Members(fixture, ann).Length == 0 && ann.Link.LastLine == PartyMessages.Disbanded, "disbanding failed");
        Assert.True(fixture.World.Parties.MembersOf(ann.Id).Count == 0, "the chat roster kept a released party");
    }

    private static async Task LeaveWorldAsync()
    {
        var (fixture, parties) = Fixture();
        var ann = await fixture.JoinAsync("Ann", 0, 0);
        var bob = await fixture.JoinAsync("Bob", 100, 0);
        var cid = await fixture.JoinAsync("Cid", 0, 100);
        await Group(fixture, parties, ann, bob);
        await Group(fixture, parties, ann, cid);

        var left = await fixture.RunAsync(fixture.World.LeaveAsync(ChatFixture.Field, ann.Id, ann.Outbox.Writer));
        fixture.Directory.Deliver(left.Notices);
        Assert.True(Members(fixture, bob).SequenceEqual([bob.Id, cid.Id]) && cid.Link.Texts().Contains("party leader is now Bob")
            && cid.Link.Last == "Ann has left the party", "the leader leaving the world must hand the party over");

        left = await fixture.RunAsync(fixture.World.LeaveAsync(ChatFixture.Field, bob.Id, bob.Outbox.Writer));
        fixture.Directory.Deliver(left.Notices);
        Assert.True(Members(fixture, cid).Length == 0 && cid.Link.Last == "party has been disbanded", "a party of two outlived a member leaving");

        // A request from a player who left answers nothing.
        var dan = await fixture.JoinAsync("Dan", 0, -100);
        var eve = await fixture.JoinAsync("Eve", -100, 0);
        await Invite(fixture, parties, dan, eve);
        await fixture.RunAsync(fixture.World.LeaveAsync(ChatFixture.Field, dan.Id, dan.Outbox.Writer));
        await Answer(fixture, parties, eve, dan);
        Assert.True(Members(fixture, eve).Length == 0, "a request outlived its sender");
    }

    private static Task ExperienceAsync()
    {
        var definition = new MonsterDefinition(1, "Party fixture", MonsterFaction.Enemy, MonsterClass.Normal, MonsterBrood.Normal,
            new MonsterCombatStats(4, 1, new MonsterRange(1, 1), 6, 30, 0, 0, 0, 1, ResistanceBlock.Zero), "")
        {
            ExpReward = 105
        };
        var field = new FieldInstance(1, FieldSpawnData.Empty, new MonsterCatalog([definition]), new EntityIdAllocator(),
            new CombatRng(123), experienceMultiplier: 5);
        var classes = new CharacterClassRegistry();
        WorldPlayer Join(ulong id, int level, Vector3 position)
        {
            var player = new WorldPlayer(id, position, classes.CreatePlayer(PlayerClass.Fighter, "Member" + id, level), false, default);
            field.Add(player);
            return player;
        }

        var killer = Join(1, 1, Vector3.Zero);
        var near = Join(2, 1, new Vector3(1000, 0, 0));
        var far = Join(3, 70, new Vector3(2000, 0, 0));
        var elsewhere = new WorldPlayer(4, new Vector3(10, 0, 0), classes.CreatePlayer(PlayerClass.Fighter, "Elsewhere"), false, default);
        var fallen = Join(5, 1, new Vector3(500, 0, 0));
        fallen.ApplyDamage(fallen.Character.Life);
        var party = SkillFixtures.Group(killer, near, far, elsewhere, fallen);
        party.AverageLevel = PartyRules.AverageLevel([1, 1, 70, 1, 1]);
        var monster = ((ISpawnSink)field).Spawn(definition, new Vector3(24, 0, 0), -1, 0);
        Assert.True(field.SetAttackTarget(1, monster.EntityId, true), "fixture target rejected");
        for (var index = 0; index < 400 && monster.IsAlive; index++)
        {
            if (!killer.InAttackRange(monster.Position))
            {
                killer.ReportMovement(killer.Position + Vector3.Normalize(monster.Position - killer.Position) * 6f, 0f, true, true, monster.EntityId, field.NowMs);
            }

            field.Step(0.05f);
        }

        // 525 at the average level 14, 10 levels from the monster, then each share for a party of five; the member in another
        // field and the dead one gain nothing.
        Assert.True(!monster.IsAlive && killer.Character.Experience > 0 && near.Character.Experience == killer.Character.Experience
            && far.Character.Experience == 0 && elsewhere.Character.Experience == 0 && fallen.IsDead && fallen.Character.Experience == 0,
            $"shares {killer.Character.Experience}/{near.Character.Experience}/{far.Character.Experience}/{elsewhere.Character.Experience}/{fallen.Character.Experience}");
        return Task.CompletedTask;
    }

    private static async Task GoldAsync()
    {
        var (fixture, parties) = Fixture();
        var ann = await fixture.JoinAsync("Ann", 0, 0);
        var bob = await fixture.JoinAsync("Bob", 100, 0);
        var cid = await fixture.JoinAsync("Cid", 3000, 0);
        await Group(fixture, parties, ann, bob);
        await Group(fixture, parties, ann, cid);
        var sessions = new SessionAppService(fixture.Sessions, new CharacterClassRegistry(), fixture.Characters, new ImmediateSaves(fixture.Characters), fixture.Catalog, fixture.World, fixture.Data);
        var field = fixture.World.FieldFor(ChatFixture.Field)!;

        // betweenTicks runs after the tick that prepares the pickup and before the one that commits it.
        async Task<long> PickUp(int gold, Func<Task>? betweenTicks = null, long thrownBy = FieldGroundLoot.NoThrower)
        {
            if (thrownBy == FieldGroundLoot.NoThrower)
            {
                Assert.True(field.GroundLoot.PlaceCreated(null, gold, Vector3.Zero, ann.Id, field.NowMs), "fixture gold not placed");
            }
            else
            {
                field.GroundLoot.PlaceThrown(null, gold, Vector3.Zero, ann.Id, field.NowMs, thrownBy);
            }

            fixture.Tick(1);
            var drop = field.GroundLoot.Snapshot().Single().View;
            // The gold falls on a cell around the spot; the picker walks to it, as the client does.
            Assert.True(await fixture.RunAsync(fixture.World.ReportMovementAsync(ChatFixture.Field, ann.Id, drop.Position, 0f, true, true, 0)) == MovementVerdict.Accepted,
                "the walk to the gold was refused");
            var command = new InventoryCommand(Guid.NewGuid(), ann.Session.Character!.InventoryRevision, InventoryAction.Pickup, drop.Id, 0, 1, default);
            var pickup = sessions.ApplyInventoryAsync(ann.Session.SessionToken, command);
            if (betweenTicks is not null)
            {
                fixture.Tick(1);
                await betweenTicks();
            }

            Assert.True((await fixture.RunAsync(pickup)).Ok, "pickup refused");
            return gold;
        }

        await PickUp(1001);
        Assert.True(ann.Session.Character!.Money == 501 && bob.Session.Character!.Money == 500 && cid.Session.Character!.Money == 0,
            $"split {ann.Session.Character.Money}/{bob.Session.Character!.Money}/{cid.Session.Character!.Money}");
        Assert.True(fixture.Characters.Records["Ann"].Money == 501, "the picker's part was not stored");

        await PickUp(10_000);
        Assert.True(ann.Session.Character!.Money == 10_501 && bob.Session.Character!.Money == 500, "gold of 10000 was split");

        // Bob's save gate is busy (a trade being stored): Bob is left out and Ann takes the whole gold.
        await bob.Session.SaveGate.WaitAsync();
        await PickUp(800);
        bob.Session.SaveGate.Release();
        Assert.True(ann.Session.Character!.Money == 11_301 && bob.Session.Character!.Money == 500, "a busy member took a share");

        // Bob's gate is taken between the prepare and the commit of Ann's pickup: the part split for Bob is not given.
        await PickUp(800, () => bob.Session.SaveGate.WaitAsync());
        bob.Session.SaveGate.Release();
        Assert.True(ann.Session.Character!.Money == 11_701 && bob.Session.Character!.Money == 500,
            "a share landed on a member whose gate was taken during the pickup");

        var bobCharacter = bob.Session.Character;
        var room = MoneyLimits.CarryLimit(bobCharacter.Level, bobCharacter.Tier) - bobCharacter.Money;
        Assert.True(bobCharacter.TryChangeMoney(room - 399), "fixture gold not given");
        await PickUp(800);
        Assert.True(ann.Session.Character!.Money == 12_501 && MoneyLimits.CanCarry(bobCharacter, 399) && !MoneyLimits.CanCarry(bobCharacter, 400),
            "a member who cannot carry its part within the carry limit kept the picker from the whole gold");

        // Gold a character threw is logged from it to the picker and to each member given a part; nothing else here was.
        Assert.True(fixture.Log.Rows.Count == 0, "a pickup of gold nobody threw was logged");
        Assert.True(bobCharacter.TryChangeMoney(-bobCharacter.Money), "fixture gold not taken");
        await PickUp(1000, thrownBy: 77);
        Assert.True(fixture.Log.Rows is [var picked, var shared] && picked is { Kind: ItemTransferKind.GroundPickup, FromCharacterId: 77, Amount: 500 }
            && picked.ToCharacterId == fixture.Characters.Records["Ann"].Id && shared is { FromCharacterId: 77, Amount: 500 }
            && shared.ToCharacterId == fixture.Characters.Records["Bob"].Id && shared.TransferId == picked.TransferId,
            "the thrown gold was not logged to the picker and the member given a part");
    }

    private static Task RulesAsync()
    {
        Assert.True(PartyRules.ExpShare(1) == (100, 0) && PartyRules.ExpShare(2) == (65, 65) && PartyRules.ExpShare(6) == (37, 37), "share table");
        Assert.True(PartyRules.Share(1000, 37) == 370 && PartyRules.AverageLevel([50, 51]) == 50, "share or average");
        Assert.True(PartyRules.LevelsMatch(10, 24) && !PartyRules.LevelsMatch(10, 25), "level gap");
        Assert.True(PartyRules.InShareRange(Vector3.Zero, new Vector3(1151, 900, 0)) && !PartyRules.InShareRange(Vector3.Zero, new Vector3(0, 0, 1152)),
            "the share range is planar and short of 18 x 64");
        Assert.True(PartyRules.SplitGold(1001, 2) == (501, 500) && PartyRules.SharesGold(9999) && !PartyRules.SharesGold(10_000), "gold split");
        return Task.CompletedTask;
    }

    private static Task WireAsync()
    {
        var buffer = new byte[PartyMembersPacket.MaxSize];
        var invite = PartyInvitePacket.Read(buffer.AsSpan(0, new PartyInvitePacket(42).Write(buffer)));
        var answer = PartyAnswerPacket.Read(buffer.AsSpan(0, new PartyAnswerPacket(7, true).Write(buffer)));
        var command = PartyCommandPacket.Read(buffer.AsSpan(0, new PartyCommandPacket(PartyCommand.Kick, 9).Write(buffer)));
        var request = PartyRequestPacket.Read(buffer.AsSpan(0, new PartyRequestPacket(5, "Ann", "Cid").Write(buffer)));
        Assert.True(invite.TargetId == 42 && answer is { FromId: 7, Accept: true } && command is { Command: PartyCommand.Kick, MemberId: 9 }
            && request is { FromId: 5, FromName: "Ann", Recommended: "Cid" }, "party requests did not round-trip");

        var members = new PartyMembersPacket([new PartyMemberWire(1, "Ann", 40, 120, 300, 10.5f, -3f), new PartyMemberWire(2, "Bob", 38, 0, 250, 0f, 0f)]);
        var read = PartyMembersPacket.Read(buffer.AsSpan(0, members.Write(buffer)));
        Assert.True(read.Members.SequenceEqual(members.Members), "party members did not round-trip");
        Assert.True(PartyMembersPacket.Read(buffer.AsSpan(0, new PartyMembersPacket([]).Write(buffer))).Members.Count == 0, "no party did not round-trip");

        var length = new PartyCommandPacket(PartyCommand.Leave, 0).Write(buffer);
        buffer[length - 9] = 9;
        Assert.Throws<InvalidDataException>(() => PartyCommandPacket.Read(buffer.AsSpan(0, length)), "an unknown party command was read");
        length = new PartyAnswerPacket(1, false).Write(buffer);
        buffer[length - 1] = 2;
        Assert.Throws<InvalidDataException>(() => PartyAnswerPacket.Read(buffer.AsSpan(0, length)), "an answer that is neither yes nor no was read");
        return Task.CompletedTask;
    }
}
