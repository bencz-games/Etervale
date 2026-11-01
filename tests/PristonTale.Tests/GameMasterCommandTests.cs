using PristonTale.Persistence.Abstractions.GameLogs;
using System.Numerics;
using PristonTale.Application.Chat;
using PristonTale.Application.GameMasters;
using PristonTale.Application.World;
using PristonTale.Simulation.Characters;
using PristonTale.Simulation.Items;
using PristonTale.Simulation.Items.Usable;
using PristonTale.Simulation.Modifiers;
using PristonTale.Simulation.Progression;
using PristonTale.Simulation.World;
using PristonTale.Contracts.Motion;
using PristonTale.Contracts.World;
using PristonTale.Contracts.Social;
using PristonTale.Contracts.Characters;
using PristonTale.Simulation.World.Monsters;

namespace PristonTale.Tests;

internal static class GameMasterCommandTests
{
    // The level each command needs, in the order the server tries them.
    private static readonly (string Text, int Level)[] ExpectedLevels =
    [
        ("/localtime", 1), ("/get_bosstime", 1), ("/get_forceorb", 1), ("/get_primitem", 1), ("/get_currency", 1), ("/init_premiumitem", 4),
        ("/specific", 1), ("/Performance", 1), ("/@get_perf", 4), ("/@get", 4), ("/@put", 4), ("/AgingSetting", 3), ("/get_potion", 1), ("/>", 2), ("/netlog", 1),
        ("//netlog", 1), ("/user_limit", 2), ("/kick", 2), ("/kill", 2), ("/open_warehouse", 2), ("/shutdown", 2), ("/weather", 2),
        ("/reload_settings", 4), ("/setting", 4), ("/insert", 2),
        ("/usercode", 1), ("/userid", 1), ("/username", 1), ("/near", 2), ("/call", 2), ("/where", 2), ("/field", 2),
        ("/player_location", 2), ("/expup", 4), ("/GiveMeExp!", 4), ("/GiveMeMoney!", 4),
        ("/user", 1), ("/sockets", 1), ("/talk", 1), ("/set_absorb", 4), ("/area_users", 1), ("/monster", 1), ("/mymonster", 1),
        ("/event_monster", 1), ("/area_monster", 1), ("/show_blackuser", 1), ("/reset_blackuser", 1), ("/free_blackuser", 1), ("/hide", 1),
        ("/show", 1), ("/get_money", 3), ("/dm_info", 1), ("/get_guid", 1), ("/grant", 1)
    ];

    public static IEnumerable<(string, Func<Task>)> Cases()
    {
        yield return ("game-master commands keep their levels; lower levels are refused and every attempt is audited", PermissionsAsync);
        yield return ("information commands report the clock, boss minute, players, connections, money and damage", InformationAsync);
        yield return ("kick, kill, near, call and where act on the named player on the server", PlayerCommandsAsync);
        yield return ("a hidden game master leaves other players' views and monsters' sight, and shows again", HideAsync);
        yield return ("monster calls spawn on the server without drops below the highest level, and pets take the caller's side", MonstersAsync);
        yield return ("a hidden game master's called monsters and pet stay and show to the caller", HiddenMonstersAsync);
        yield return ("event monsters, field reports, absorption and monster speech", MonsterDetailsAsync);
        yield return ("item commands drop rolled, perfect and specialized copies and gold, and gifts are stored before delivery", ItemsAsync);
        yield return ("experience, premium resets, force orb and premium reports", CharacterAsync);
        yield return ("server commands set the player limit, start the shutdown, release refused logins and move characters", ServerCommandsAsync);
        yield return ("a lent level opens commands until the borrower leaves, and test servers open the gift commands", GrantAsync);
    }

    private static async Task PermissionsAsync()
    {
        var actual = GameMasterCommands.Describe(GameMasterOptions.Default).ToArray();
        Assert.True(actual.SequenceEqual(ExpectedLevels), $"command table changed: {string.Join(", ", actual.Select(entry => $"{entry.Text}={entry.Level}"))}");
        var test = GameMasterCommands.Describe(new GameMasterOptions(TestServer: true));
        Assert.True(test.Single(entry => entry.Text == "/GiveMeExp!").Level == 1 && test.Single(entry => entry.Text == "/GiveMeMoney!").Level == 1,
            "a test server opens the experience and gold commands to every game master");
        var fixture = new ChatFixture();
        for (var level = 0; level <= GameMasterLevel.Highest; level++)
        {
            var player = await fixture.JoinAsync($"Level{level}", level * 2000, 0, level);
            foreach (var (text, required) in ExpectedLevels)
            {
                fixture.Audit.Entries.Clear();
                await fixture.SayAsync(player, text + " nobody");
                var entry = fixture.Audit.Entries.Single();
                Assert.True(entry.Command == text + " nobody" && entry.RequiredLevel == required && entry.Accepted == level >= required
                    && entry.GmLevel == level && entry.CharacterName == player.Member.Name, $"{text} at level {level}: {entry}");
                Assert.True(entry.Accepted || entry.Result.StartsWith("denied", StringComparison.Ordinal), $"{text} refusal not recorded");
                if (!entry.Accepted)
                {
                    var told = player.Link.Lines.Count > 0 && player.Link.Last == $"> {text} needs game-master level {required} (yours is {level})";
                    Assert.True(level == 0 ? player.Link.Lines.Count == 0 : told, $"{text} refusal at level {level}: {(player.Link.Lines.Count > 0 ? player.Link.Last : "nothing")}");
                }
            }
        }

        fixture.Audit.Entries.Clear();
        var guide = fixture.Directory.FindByName("Level1")!;
        await fixture.RunAsync(fixture.Chat.SayAsync(guide.EntityId, "/no_such_command"));
        Assert.True(fixture.Audit.Entries.Count == 0, "an unknown command was audited as a game-master command");
    }

    private static async Task InformationAsync()
    {
        var fixture = new ChatFixture(new GameMasterOptions(ServerCode: 3));
        var gm = await fixture.JoinAsync("Keeper", 0, 0, GameMasterLevel.Operator);
        var ann = await fixture.JoinAsync("Ann", 100, 0);
        // The date is read on both sides of the command: a run across midnight sees either day.
        var before = fixture.World.LocalNow;
        await fixture.SayAsync(gm, "/localtime");
        var after = fixture.World.LocalNow;
        Assert.True(new[] { before, after }.Any(now => gm.Link.Last.StartsWith($"TIME [3]> {now.Year}/{now.Month}/{now.Day} ( ", StringComparison.Ordinal)),
            $"local time {gm.Link.Last}");
        await fixture.SayAsync(gm, "/get_bosstime");
        Assert.True(gm.Link.Last == $"3> Boss Time ( {fixture.World.BossMinute} min.)", gm.Link.Last);
        await fixture.SayAsync(ann, "/bosstime");
        Assert.True(ann.Link.Last == $"Boss Time: XX:{fixture.World.BossMinute:00}", "players read the boss minute too");
        await fixture.SayAsync(gm, "/user");
        Assert.True(gm.Link.Last == "Users 2 / Active Monsters 0", gm.Link.Last);
        await fixture.SayAsync(gm, "/sockets");
        Assert.True(gm.Link.Last == "Open (5) / Active (0)", gm.Link.Last);
        await fixture.SayAsync(gm, "/area_users");
        Assert.True(gm.Link.Last == "> Area(3) Users(2)", gm.Link.Last);
        await fixture.SayAsync(gm, "/netlog");
        Assert.True(gm.Link.Last == "> Rcv(12) Snd(34) ScoopRcv(12) ScoopFail(0) SndFail(0) RcvFail(0)", gm.Link.Last);
        fixture.NowMs += 5 * 60_000;
        await fixture.SayAsync(gm, "/username ann");
        Assert.True(gm.Link.Last == "Account( ann-account ) Name( Ann ) IP( 203.0.113.7 ) Connected( 5 mins )", gm.Link.Last);
        await fixture.SayAsync(gm, "/userid ANN-ACCOUNT");
        Assert.True(gm.Link.Last.StartsWith("Account( ann-account ) Name( Ann )", StringComparison.Ordinal), gm.Link.Last);
        await fixture.SayAsync(gm, $"/usercode {ann.Id}");
        Assert.True(gm.Link.Last.StartsWith("Account( ann-account )", StringComparison.Ordinal), gm.Link.Last);
        await fixture.SayAsync(gm, "/username ghost");
        Assert.True(gm.Link.Last == "Currently not online, or cannot be found", gm.Link.Last);
        await fixture.SayAsync(gm, "/get_money");
        Assert.True(gm.Link.Last == $"> Server Money ( {gm.Member.StartingMoney} )( {gm.Session.Character!.Money} )", gm.Link.Last);
        await fixture.SayAsync(gm, "/dm_info");
        var derived = gm.Session.Character.Derived;
        Assert.True(gm.Link.Last == $"Limit Damage ( {derived.AttackDamageMin} {derived.AttackDamageMax} {derived.CriticalChance} )", gm.Link.Last);
        await fixture.SayAsync(gm, "/get_guid");
        Assert.True(gm.Link.Last == $"> Guid( {gm.Session.SessionToken} ) GpCode(  ) PCRno( 0 ) GpType( 0 )", gm.Link.Last);
        Assert.True(ann.Session.Character!.Inventory.TryAdd(new ItemInstance(ChatFixture.Potion, 7)), "fixture potions");
        await fixture.SayAsync(gm, "/get_potion Ann");
        Assert.True(gm.Link.Last == ">PL(7 0 0 0) PM(0 0 0 0) PS(0 0 0 0)", gm.Link.Last);
        fixture.World.Currency.Record(CurrencySink.Shop, 2_500_000);
        fixture.World.Currency.Record(CurrencySink.LifePotions, 40);
        fixture.World.Currency.Record(CurrencySink.Teleports, 100);
        await fixture.SayAsync(gm, "/get_currency");
        var lines = gm.Link.Texts();
        Assert.True(lines[^2] == ">currency: Mx(0) Ag(0) Sh(2500000) Po(40 0 0) Fo(0) Wp(100) Sk(0 ) Tt(2500140)"
            && lines[^1] == ">currency[M]: Mx(0) Ag(0) Sh(2) Po(0 0 0) Fo(0) Wp(0) Sk(0 ) Tt(2)", string.Join("|", lines.TakeLast(2)));
        Assert.True(CurrencyLedger.ShopSinkFor("PM102") == CurrencySink.ManaPotions && CurrencyLedger.ShopSinkFor("WA101") == CurrencySink.Shop,
            "shop purchases are not sorted by potion kind");
    }

    private static async Task PlayerCommandsAsync()
    {
        var fixture = new ChatFixture();
        var gm = await fixture.JoinAsync("Keeper", 0, 0, GameMasterLevel.Moderator);
        var ann = await fixture.JoinAsync("Ann", 3000, 2000);
        await fixture.SayAsync(gm, "/where ann");
        Assert.True(gm.Link.Last == "Position of Ann ( Fixture Field ) x=3000 / z=2000 ", gm.Link.Last);
        await fixture.SayAsync(gm, "/near Ann");
        Assert.True(gm.Link.Warps.Single() == (ChatFixture.Field, new Vector3(3000, 0, 2000)), "the game master was not moved to the player");
        Assert.True((await fixture.RunAsync(fixture.World.LocateAsync(gm.Id, gm.Id)))!.Value.Position == new Vector3(3000, 0, 2000),
            "the server did not place the game master at the player");
        await fixture.SayAsync(gm, "/where Keeper");
        await fixture.RunAsync(fixture.World.RelocateAsync(gm.Id, gm.Id, new GmPlacement(ChatFixture.Field, new Vector3(-500, 0, 700))));
        await fixture.SayAsync(gm, "/call Ann");
        Assert.True(ann.Link.Warps.Single() == (ChatFixture.Field, new Vector3(-500, 0, 700)), "the player was not called to the game master");
        await fixture.SayAsync(gm, "~/where Ann");
        Assert.True(gm.Link.Last == "Position of Ann ( Fixture Field ) x=-500 / z=700 ", "a command with the classic '~' mark: " + gm.Link.Last);
        await fixture.SayAsync(gm, "/player_location 1200 0 -300");
        Assert.True(gm.Link.Warps[^1] == (ChatFixture.Field, new Vector3(1200, 0, -300)), "the game master did not move within the field");
        await fixture.SayAsync(gm, "/field " + ChatFixture.Field);
        Assert.True(gm.Link.Warps[^1] == (ChatFixture.Field, Vector3.Zero), "the field warp did not land on the field's start");
        var warps = gm.Link.Warps.Count;
        await fixture.SayAsync(gm, "/field 99");
        Assert.True(gm.Link.Warps.Count == warps, "an unknown field moved the game master");
        await fixture.SayAsync(gm, "/kill Ann");
        Assert.True(gm.Link.Last == "> Killed ( Ann )", gm.Link.Last);
        fixture.Tick(1);
        var view = ChatFixture.Latest(ann)!.Value.Entities.Single(entity => entity.EntityId == ann.Id);
        Assert.True(view.Life == 0 && view.MoveState == MotionState.Dead, "the killed player is not dead on the server");
        await fixture.SayAsync(gm, "/kick Ann");
        Assert.True(ann.Link.Disconnected is not null && gm.Link.Last == "Congratulations. You have been kicked Ann", "the player was not disconnected");
    }

    private static async Task HideAsync()
    {
        var fixture = new ChatFixture();
        var gm = await fixture.JoinAsync("Keeper", 0, 0, GameMasterLevel.Guide);
        var ann = await fixture.JoinAsync("Ann", 50, 0);
        await fixture.SayAsync(gm, "/hide");
        Assert.True(gm.Link.Last == "Hide OK!", gm.Link.Last);
        fixture.Tick(1);
        var snapshot = ChatFixture.Latest(ann)!.Value;
        Assert.True(snapshot.Entities.Single(entity => entity.EntityId == gm.Id).Hidden, "the hidden game master's view is not marked hidden");
        var prey = new FieldPreyView();
        var hidden = new WorldPlayer(1, Vector3.Zero, new CharacterClassRegistry().CreatePlayer(PlayerClass.Fighter, "Ghost"), false, default) { Hidden = true };
        prey.Rebuild([hidden]);
        Assert.True(prey.Candidates.Count == 0, "monsters can see a hidden game master");
        await fixture.SayAsync(gm, "/show");
        fixture.Tick(1);
        Assert.True(gm.Link.Last == "Show OK!" && !ChatFixture.Latest(ann)!.Value.Entities.Single(entity => entity.EntityId == gm.Id).Hidden,
            "the game master did not show again");
    }

    private static async Task MonstersAsync()
    {
        var fixture = new ChatFixture();
        var guide = await fixture.JoinAsync("Guide", 0, 0, GameMasterLevel.Guide);
        var admin = await fixture.JoinAsync("Admin", 4000, 0, GameMasterLevel.Administrator);
        await fixture.SayAsync(guide, "/monster Hopy 3");
        fixture.Tick(1);
        var monsters = ChatFixture.Latest(guide)!.Value.Entities.Where(entity => entity.Kind == EntityKind.Monster).ToArray();
        Assert.True(monsters.Length == 4 && monsters.All(monster => MathF.Abs(monster.Position.X) <= WorldServer.GameMasterSpawnScatter
            && MathF.Abs(monster.Position.Z) <= WorldServer.GameMasterSpawnScatter), $"expected 4 monsters around the caller, got {monsters.Length}");
        await fixture.SayAsync(guide, "/monster Hopy 21");
        fixture.Tick(1);
        Assert.True(ChatFixture.Latest(guide)!.Value.Entities.Count(entity => entity.Kind == EntityKind.Monster) == 5,
            "more than 20 extra monsters must call only the first");
        await fixture.SayAsync(guide, "/monster Nobody");
        Assert.True(fixture.Audit.Entries[^1].Result == "", "an unknown monster still spawned");
        var guideMonster = monsters[0].EntityId;
        var field = FieldOf(fixture);
        Assert.True(field.TryGetMonster(guideMonster, out var called) && called.DropsDisabled, "a guide's monster may drop items");
        await fixture.SayAsync(admin, "/monster Hopy");
        fixture.Tick(1);
        var adminMonster = ChatFixture.Latest(admin)!.Value.Entities.Single(entity => entity.Kind == EntityKind.Monster && entity.Position.X > 3000);
        Assert.True(field.TryGetMonster(adminMonster.EntityId, out var dropping) && !dropping.DropsDisabled, "an administrator's monster must drop");
        await fixture.SayAsync(guide, "/mymonster Hopy");
        fixture.Tick(1);
        var pet = ChatFixture.Latest(guide)!.Value.Entities.Single(entity => entity.Kind == EntityKind.Monster && entity.OwnerId == guide.Id);
        Assert.True(field.TryGetMonster(pet.EntityId, out var owned) && owned.Def.Brood == Simulation.World.Monsters.MonsterBrood.User && owned.Aggressive,
            "a pet does not take the caller's side");
        await fixture.SayAsync(guide, "/mymonster Hopy");
        await fixture.SayAsync(guide, "/mymonster Hopy 2");
        fixture.Tick(1);
        var pets = ChatFixture.Latest(guide)!.Value.Entities.Count(entity => entity.Kind == EntityKind.Monster && entity.OwnerId == guide.Id);
        Assert.True(pets == 5, $"a later pet replaced an earlier one or the extra count was ignored: {pets} pets of 5");
    }

    private static async Task HiddenMonstersAsync()
    {
        var fixture = new ChatFixture();
        var gm = await fixture.JoinAsync("Shade", 0, 0, GameMasterLevel.Guide);
        await fixture.SayAsync(gm, "/hide");
        await fixture.SayAsync(gm, "/monster Hopy 2");
        await fixture.SayAsync(gm, "/mymonster Hopy");
        fixture.Tick(40);
        var seen = ChatFixture.Latest(gm)!.Value.Entities.Where(entity => entity.Kind == EntityKind.Monster).ToArray();
        Assert.True(seen.Count(entity => entity.OwnerId == 0) == 3 && seen.Count(entity => entity.OwnerId == gm.Id) == 1,
            $"a hidden game master saw {seen.Length} monsters of 3 called and a pet");
    }

    private static async Task MonsterDetailsAsync()
    {
        var fixture = new ChatFixture();
        var gm = await fixture.JoinAsync("Keeper", 0, 0, GameMasterLevel.Administrator);
        await fixture.SayAsync(gm, "/event_monster Hopy 10 250");
        Assert.True(gm.Link.Last == "> Set Event Monster ( Hopy ) (10)Min. (250/1000)", gm.Link.Last);
        await fixture.SayAsync(gm, "/event_monster");
        Assert.True(gm.Link.Last.StartsWith("> Event Monster ( Hopy ) Left ", StringComparison.Ordinal) && gm.Link.Last.EndsWith(" Sec. (250/1000)", StringComparison.Ordinal),
            gm.Link.Last);
        var spawner = FieldOf(fixture).Spawner;
        Assert.True(spawner.EventMonster is { Permille: 250 } running && running.Definition.Name == "Hopy", "the field's event monster was not set");
        await fixture.SayAsync(gm, "/monster Hopy");
        fixture.Tick(1);
        await fixture.SayAsync(gm, "/area_monster");
        Assert.True(gm.Link.Last == "> ResponePoint( 0 ) ResponeMonster( 0 ) Monster( 1 ) MonsterCount( 1 ) LimitTime( 1 )", gm.Link.Last);
        var monster = ChatFixture.Latest(gm)!.Value.Entities.Single(entity => entity.Kind == EntityKind.Monster);
        await fixture.SayAsync(gm, $"/set_absorb {monster.EntityId} 40");
        Assert.True(gm.Link.Last == "> set_absorb (Hopy) 10->40 ", gm.Link.Last);
        Assert.True(FieldOf(fixture).TryGetMonster(monster.EntityId, out var instance)
            && instance.Def.Combat.Absorption + instance.Effects.Totals.Flat(StatKind.Absorption) == 40, "the monster's absorption did not change");
        var listener = await fixture.JoinAsync("Ann", 100, 0);
        var away = await fixture.JoinAsync("Bob", 4000, 0);
        await fixture.SayAsync(gm, $"/talk {monster.EntityId} Leave this forest");
        var spoken = new ChatLine(ChatKind.Server, monster.EntityId, "Hopy:  Leave this forest ");
        Assert.True(listener.Link.Lines.Single() == spoken && gm.Link.Lines[^1] == spoken && away.Link.Lines.Count == 0,
            "a monster's speech must reach the players near the game master, in a bubble over the monster");
    }

    private static async Task ItemsAsync()
    {
        var fixture = new ChatFixture();
        var gm = await fixture.JoinAsync("Keeper", 0, 0, GameMasterLevel.Administrator);
        var ann = await fixture.JoinAsync("Ann", 9000, 0);
        await fixture.SayAsync(gm, "/@get WA101");
        Assert.True(gm.Link.Last == "WA101 items have been created", gm.Link.Last);
        await fixture.SayAsync(gm, "/@get \"Stone Axe\"");
        await fixture.SayAsync(gm, "/GiveMeMoney! 5000");
        Assert.True(gm.Link.Last == ">What the hell~!!( 5000 )", gm.Link.Last);
        await fixture.SayAsync(gm, "/GiveMeMoney! 1000001");
        Assert.True(gm.Link.Last == ">What the hell~!!( 5000 )", "more than 1,000,000 gold must be refused");
        fixture.Tick(1);
        var drops = FieldOf(fixture).GroundLoot.Snapshot();
        Assert.True(drops.Count(drop => drop.View.CatalogKey == "WA101") == 2 && drops.Single(drop => drop.View.Gold).View.Amount == 5000 && drops.All(drop => drop.Public),
            "the created items and gold were not dropped at the game master");
        await fixture.SayAsync(gm, "/@get_perf WA101 MS");
        Assert.True(gm.Link.Last == "Perfect WA101 created (Mechanician)", gm.Link.Last);
        await fixture.SayAsync(gm, "/@get_perf WA101 KS");
        Assert.True(gm.Link.Last == "WA101 cannot take the Knight spec", gm.Link.Last);
        await fixture.SayAsync(gm, "/@get_perf WA101 cooks");
        Assert.True(gm.Link.Last.StartsWith("Unknown spec cooks", StringComparison.Ordinal), gm.Link.Last);
        await fixture.SayAsync(gm, "/@get_perf ZZ999");
        Assert.True(gm.Link.Last == "Unknown item code ZZ999", gm.Link.Last);
        var perfect = ItemRoller.CreatePerfect(ChatFixture.Axe, 1, (uint)JobBitCode.Mechanician)!;
        Assert.True(perfect.Roll!.DamageMin == 6 && perfect.Roll.DamageMax == 14 && perfect.Roll.AttackRating == 30 && perfect.DurabilityMax == 80
            && perfect.DurabilityCurrent == 80 && perfect.Roll.SpecJob == (uint)JobBitCode.Mechanician && perfect.Roll.Price == 120
            && perfect.Roll.Spec.Defence == 9 && perfect.Roll.Spec.LevelAttackRating == 1 && perfect.Roll.Requirements.Strength == 42
            && perfect.Roll.Requirements.Agility == 15, $"perfect roll {perfect.Roll}");
        Assert.True(ItemRoller.CreatePerfect(ChatFixture.Axe, 1, 0)!.Roll!.SpecJob == (uint)JobBitCode.Fighter, "no spec keeps the authored one");
        await fixture.SayAsync(gm, "/specific 2");
        Assert.True(gm.Link.Last == ">Specific (2) 1 minute operation", gm.Link.Last);
        await fixture.SayAsync(gm, "/Performance 1");
        Assert.True(gm.Link.Last == ">Item Performance setting (1) effective 1 minutes", gm.Link.Last);
        var overrides = fixture.GameMasters.ItemOverrides.Current(fixture.NowMs);
        Assert.True(overrides == new ItemRollOverrides((uint)JobBitCode.Mechanician, true), $"overrides {overrides}");
        for (var seed = 0; seed < 20; seed++)
        {
            var copy = ItemRoller.Create(ChatFixture.Axe, 1, new Simulation.Combat.CombatRng(seed), overrides);
            Assert.True(copy.Roll!.SpecJob == (uint)JobBitCode.Mechanician && copy.Roll.DamageMin is >= 3 and <= 5 && copy.Roll.AttackRating is >= 15 and <= 25,
                $"forced middle roll out of range: {copy.Roll}");
        }

        Assert.True(fixture.GameMasters.ItemOverrides.Current(fixture.NowMs + 60_000) == ItemRollOverrides.None, "the overrides outlived their minute");
        var before = fixture.Characters.Updates;
        await fixture.SayAsync(gm, "/@put Ann PL101");
        Assert.True(gm.Link.Last == "PL101 items have been sent to Ann", gm.Link.Last);
        Assert.True(fixture.Characters.Updates == before + 1 && fixture.Characters.Records["Ann"].Inventory.Value.Contains(ChatFixture.Potion.Code.ToString(System.Globalization.CultureInfo.InvariantCulture), StringComparison.Ordinal),
            "the gift was not stored before delivery");
        var carried = Enumerable.Range(0, ann.Session.Character!.Inventory.Capacity).Select(anchor => ann.Session.Character.Inventory[anchor])
            .Where(item => item is not null).ToArray();
        Assert.True(carried.Any(item => item.Definition.CatalogKey == "PL101"), "the gift did not reach the player's bag");
        Assert.True(fixture.Log.Rows.Single(row => row.Kind == ItemTransferKind.GameMasterGift) is { ItemCode: "PL101", FromCharacterId: null } gift
            && gift.ToCharacterId == fixture.Characters.Records["Ann"].Id, "the gift was not logged");
    }

    private static async Task CharacterAsync()
    {
        var fixture = new ChatFixture();
        var gm = await fixture.JoinAsync("Keeper", 0, 0, GameMasterLevel.Administrator);
        await fixture.SayAsync(gm, "/GiveMeExp! 20");
        Assert.True(gm.Session.Character!.Level == 20 && gm.Session.Character.Experience == ExperienceTable.ForLevel(20)
            && gm.Link.Last == $">Here you go~( {ExperienceTable.ForLevel(20)} )", gm.Link.Last);
        await fixture.SayAsync(gm, "/GiveMeExp! 1");
        Assert.True(gm.Link.Lines.Count == 1, "level 1 is out of range");
        await fixture.SayAsync(gm, "/get_primitem");
        Assert.True(gm.Link.Lines.Count == 1, "the premium report spoke without a running premium item");
        var before = gm.Session.Character.Experience;
        await fixture.SayAsync(gm, "/expup 50");
        Assert.True(gm.Session.Character.Experience == before + 50 && gm.Link.Last == ">( 50 )", "experience up: " + gm.Link.Last);
        await fixture.SayAsync(gm, "/expup -5");
        Assert.True(gm.Session.Character.Experience == before + 50 && gm.Link.Lines.Count == 2, "a negative amount changed experience");
        var nowSeconds = fixture.World.UnixSeconds;
        gm.Session.Character.Premium.Start(PremiumEffectKind.ThirdEyes, 50, 3600, nowSeconds);
        gm.Session.Character.Premium.Start(PremiumEffectKind.ExperienceUp, 30, 7200, nowSeconds);
        await fixture.SayAsync(gm, "/get_primitem");
        Assert.True(gm.Link.Last is "0> Prime Item - Package[0] DropItem( 60 min ) ExpUp( 120 min )" or "0> Prime Item - Package[0] DropItem( 59 min ) ExpUp( 119 min )",
            gm.Link.Last);
        await fixture.SayAsync(gm, "/init_premiumitem 1");
        Assert.True(gm.Link.Last == "(Third Eyes)Item Init Success" && !gm.Session.Character.Premium.IsActive(PremiumEffectKind.ThirdEyes)
            && gm.Session.Character.Premium.IsActive(PremiumEffectKind.ExperienceUp), "the premium reset did not end exactly Third Eyes");
        await fixture.SayAsync(gm, "/init_premiumitem 1");
        Assert.True(gm.Link.Last == "(Third Eyes)Item Init Success" && gm.Link.Lines.Count == 4, "resetting a timer that does not run must answer nothing");
        Assert.True(ForceOrbs.TryGet("FO101", out var orb), "fixture force orb");
        gm.Session.Character.Effects.Apply(orb.Spec(10), gm.Id, 30_000f, gm.Id);
        await fixture.SayAsync(gm, "/get_forceorb");
        Assert.True(gm.Link.Last.StartsWith($"0> Force Orb - [{orb.IconNumber}] ( 29 sec )", StringComparison.Ordinal)
            || gm.Link.Last.StartsWith($"0> Force Orb - [{orb.IconNumber}] ( 30 sec )", StringComparison.Ordinal), gm.Link.Last);
    }

    private static async Task ServerCommandsAsync()
    {
        var fixture = new ChatFixture();
        var gm = await fixture.JoinAsync("Keeper", 0, 0, GameMasterLevel.Moderator);
        var ann = await fixture.JoinAsync("Ann", 9000, 0);
        await fixture.SayAsync(gm, "/user_limit");
        Assert.True(gm.Link.Last == "Current maximum users setting is 0", gm.Link.Last);
        await fixture.SayAsync(gm, "/user_limit 150");
        Assert.True(gm.Link.Last == "Maximum users have been set to 150" && fixture.Server.UserLimit == 150, gm.Link.Last);
        await fixture.SayAsync(gm, "/shutdown");
        await fixture.SayAsync(gm, "/shutdown");
        Assert.True(fixture.Server.Shutdowns == 2 && fixture.Audit.Entries[^1].Result == "shutdown already running", "the shutdown started twice");
        await fixture.SayAsync(gm, "/show_blackuser");
        Assert.True(gm.Link.Texts().TakeLast(2).SequenceEqual(["> ID ( guesser ) / ( 4 )Min.", "> Total 1 Users"]), string.Join("|", gm.Link.Texts()));
        await fixture.SayAsync(gm, "/free_blackuser someone");
        Assert.True(gm.Link.Last == "> Non-Blackuser, Check again.", gm.Link.Last);
        await fixture.SayAsync(gm, "/free_blackuser guesser");
        Assert.True(gm.Link.Last == "> Free BlackUser OK!" && fixture.Server.Blocked.Count == 0, gm.Link.Last);
        fixture.Server.Blocked.Add(new BlockedLogin("again", 60_000));
        await fixture.SayAsync(gm, "/reset_blackuser");
        Assert.True(gm.Link.Last == "> Reset BlackUser OK!" && fixture.Server.Blocked.Count == 0, gm.Link.Last);
        await fixture.SayAsync(gm, "/insert banker Retired");
        Assert.True(gm.Link.Last == "banker account has been set to character Retired" && fixture.Audit.Moves.Single() == ("Retired", "banker"), gm.Link.Last);
        await fixture.SayAsync(gm, "/insert banker Ann");
        Assert.True(gm.Link.Last == "Unable to connect to account" && fixture.Audit.Moves.Count == 1, "a character in the world was moved");
        await fixture.SayAsync(gm, "/insert nobody Retired");
        Assert.True(gm.Link.Last == "Unable to connect to account", gm.Link.Last);
        await fixture.SayAsync(gm, "/open_warehouse banker");
        Assert.True(gm.Link.Texts().TakeLast(1).Single() == "> Warehouse ( banker ) Gold( 4500 ) Items( 0 )", gm.Link.Last);
        Assert.True(ann.Link.Lines.Count == 0, "server commands spoke to other players");
    }

    private static async Task GrantAsync()
    {
        var fixture = new ChatFixture();
        var gm = await fixture.JoinAsync("Keeper", 0, 0, GameMasterLevel.Moderator);
        var ann = await fixture.JoinAsync("Ann", 9000, 0);
        await fixture.SayAsync(ann, "/kick Keeper");
        Assert.True(gm.Link.Disconnected is null && !fixture.Audit.Entries[^1].Accepted, "a player kicked a game master");
        await fixture.SayAsync(gm, "/grant Ann");
        Assert.True(ann.Member.GmLevel == GameMasterLevel.Moderator && ann.Link.Last == "> Ann has been given level (2)", ann.Link.Last);
        await fixture.SayAsync(ann, "/where Keeper");
        Assert.True(ann.Link.Last.StartsWith("Position of Keeper", StringComparison.Ordinal), "the lent level did not open the command");
        Assert.True(ann.Session.GmLevel == 0, "a lent level must not change the account's stored level");
        var test = new ChatFixture(new GameMasterOptions(TestServer: true));
        var guide = await test.JoinAsync("Guide", 0, 0, GameMasterLevel.Guide);
        await test.SayAsync(guide, "/GiveMeMoney! 10");
        Assert.True(guide.Link.Last == ">What the hell~!!( 10 )", "a test server keeps the gold command from a guide");
        var debug = new ChatFixture(new GameMasterOptions(DebugServer: true));
        var tester = await debug.JoinAsync("Tester", 0, 0, GameMasterLevel.Guide);
        await debug.SayAsync(tester, "/WA101");
        debug.Tick(1);
        Assert.True(FieldOf(debug).GroundLoot.Snapshot().Single().View.CatalogKey == "WA101" && debug.Audit.Entries[^1].Result == "dropped",
            "a debug server did not drop the item code");
    }

    private static FieldInstance FieldOf(ChatFixture fixture)
    {
        var fields = typeof(WorldServer).GetField("_fields", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
        return ((Dictionary<int, FieldInstance>)fields.GetValue(fixture.World)!)[ChatFixture.Field];
    }
}
