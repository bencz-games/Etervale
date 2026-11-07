using PristonTale.Application.Castle;
using PristonTale.Application.World;
using PristonTale.Simulation.Combat;
using PristonTale.Simulation.Social.Castle;
using PristonTale.Simulation.Social.Clans;
using PristonTale.Simulation.World;
using PristonTale.Simulation.World.Ai;
using PristonTale.Simulation.World.Monsters;
using PristonTale.Simulation.World.Monsters.Skills;
using PristonTale.Simulation.World.Npcs;

namespace PristonTale.Tests;

internal static class CastleSiegeTests
{
    public static IEnumerable<(string, Func<Task>)> Cases()
    {
        yield return ("castle siege rules: twenty mercenary posts a kind, the defenders' roles, and an ownerless castle's defenders", RulesAsync);
        yield return ("the damage board sums each clan's damage and ranks the leader and the best clans", BoardAsync);
        yield return ("a siege places the owner's defenders in the castle field: standing still with lives, of the owner clan, the wild monsters gone",
            PlaceAsync);
        yield return ("the owner clan never hurts its defenders; the tower stays whole while a crystal tower stands; its fall goes to the leading clan",
            FallAsync);
        yield return ("defenders and the owner clan's players spare each other: no attack target, no blow; defenders stay without players near",
            SpareAsync);
        yield return ("a standing defender strikes in its reach and lets go of a locked prey out of it", RootedAsync);
        yield return ("the easy siege leaves the tower one life it keeps while a crystal tower stands, then a blow fells it", EasyAsync);
        yield return ("players of different clans fight in the battle zone, never north of it, of one clan or in the league; blows score in a siege",
            PvpAsync);
        yield return ("the easy siege weakens the defenders, closing a siege removes them, ending it sends players outside the owner clan to the arrival area",
            EndAsync);
    }

    private static Task RulesAsync()
    {
        var posts = Enumerable.Range(0, CastleSiegeRules.MercenaryMax).Select(post => CastleSiegeRules.MercenaryPost(0, post)).ToList();
        Assert.True(posts.Distinct().Count() == CastleSiegeRules.MercenaryMax && posts[0] == new System.Numerics.Vector3(33251, 688, -27968)
            && posts[4].X == 33605 && posts[5].Z == -28052 && posts[10].X == 35497 && posts[19] == new System.Numerics.Vector3(35851, 688, -28052),
            "the mercenary posts differ");
        Assert.True(CastleSiegeRules.ObjectOf(CharacterSoundCodes.CastleDoor) == CastleObject.Door
            && CastleSiegeRules.ObjectOf(CharacterSoundCodes.CastleCrystalIce) == CastleObject.Crystal
            && CastleSiegeRules.ObjectOf(CharacterSoundCodes.CastleSoldierB) == CastleObject.Mercenary && CastleSiegeRules.MercenaryKind(CharacterSoundCodes.CastleSoldierC) == 2
            && CastleSiegeRules.StandsStill(CharacterSoundCodes.CastleTower) && !CastleSiegeRules.StandsStill(CharacterSoundCodes.CastleSoldierA),
            "the defenders' roles differ");
        var (towers, mercenaries) = CastleSiegeRules.Defenders(0, [], [], new CombatRng(3));
        Assert.True(towers.Length == CastleSiegeRules.TowerSlots && towers.All(static tower => tower != CastleTower.None)
            && mercenaries.All(static count => count == CastleSiegeRules.MercenaryMax), "an ownerless castle's defenders differ");
        var (owned, hired) = CastleSiegeRules.Defenders(7, [CastleTower.Fire], [25, 3], new CombatRng(3));
        Assert.True(owned is [CastleTower.Fire, CastleTower.None, CastleTower.None, CastleTower.None, CastleTower.None, CastleTower.None]
            && hired is [CastleSiegeRules.MercenaryMax, 3, 0], "the owner's defenders differ");
        return Task.CompletedTask;
    }

    private static Task BoardAsync()
    {
        var board = new CastleDamageBoard();
        board.Record(0, 500);
        board.Record(9, 100);
        board.Record(4, 300);
        board.Record(9, 200);
        board.Record(5, -3);
        Assert.True(board.Total == 600 && board.Leader == 4 && board.Version == 3 && board.Top(1) is [{ ClanId: 4, Damage: 300 }]
            && board.Top(10) is [{ ClanId: 4 }, { ClanId: 9, Damage: 300 }], "the board differs");
        return Task.CompletedTask;
    }

    private static async Task PlaceAsync()
    {
        var harness = await SpecialItemTests.Harness.CreateAsync(FieldCodes.Castle, Defenders(), []);
        Assert.True(harness.World.TryGetOpenField(FieldCodes.Castle, out var field), "the castle field is not open");
        var wild = field.SpawnForGameMaster(Defenders()[^1] with { Key = "wild", SoundCode = 0 }, new System.Numerics.Vector3(34000, 0, -24000), false);
        await harness.AdvanceAsync(harness.World.OpenSiegeAsync(new CastleSiegeOrder(7, [CastleTower.Ice, CastleTower.Fire], [2, 0, 1])));
        harness.Tick(1);
        var defenders = field.MonstersForInspection.ToList();
        Assert.True(!defenders.Contains(wild) && defenders.Count == 2 + CastleSiegeRules.TowerSlots + 3 && defenders.All(static monster => monster.ClanId == 7),
            "the castle field does not hold the owner's defenders alone");
        Assert.True(!field.SpawnsNow, "the castle field spawns during a siege");
        var door = defenders.Single(static monster => monster.CastleRole == CastleObject.Door);
        Assert.True(door.Position == CastleSiegeRules.Door && door.VirtualLives == 10 && new MonsterAiTuning(door.Def).IsImmobile, "the door differs");
        var crystals = defenders.Where(static monster => monster.CastleRole == CastleObject.Crystal).Select(static monster => monster.Def.Key).ToList();
        Assert.True(crystals.Count(static key => key == "tower crystal") == 4 && crystals.Contains("ice crystal") && crystals.Contains("fire crystal")
            && new MonsterAiTuning(defenders.First(static monster => monster.CastleRole == CastleObject.Crystal).Def).StrikesInPlace,
            "the crystal towers differ");
        var mercenary = defenders.First(static monster => monster.CastleRole == CastleObject.Mercenary);
        Assert.True(mercenary.Potions == mercenary.Def.PotionCount && mercenary.VirtualLives == 0 && !new MonsterAiTuning(mercenary.Def).IsImmobile,
            "a mercenary differs");
        Assert.True(harness.World.Siege is { Running: true, DoorDown: false, Crystals: CastleSiegeRules.TowerSlots, Mercenaries: [2, 0, 1] },
            "the published siege differs");
    }

    private static async Task FallAsync()
    {
        var harness = await SpecialItemTests.Harness.CreateAsync(FieldCodes.Castle, Defenders(), []);
        harness.World.TryGetOpenField(FieldCodes.Castle, out var field);
        await harness.AdvanceAsync(harness.World.OpenSiegeAsync(new CastleSiegeOrder(7, [], [])));
        harness.Tick(1);
        var player = harness.Player;
        var defenders = field.MonstersForInspection.ToList();
        var valhalla = defenders.Single(static monster => monster.CastleRole == CastleObject.Valhalla);
        player.Clan = new ClanTag(7, "Lions", 0, Registered: true);
        field.Combat.ApplyPlayerDamage(player, valhalla, 100, false, field.NowMs);
        Assert.True(valhalla.Life == valhalla.Def.Combat.MaxLife && harness.World.Siege.TotalDamage == 0, "the owner clan hurt its tower");

        player.Clan = new ClanTag(9, "Tigers", 0, Registered: true);
        field.Combat.ApplyPlayerDamage(player, valhalla, 100, false, field.NowMs);
        harness.Tick(1);
        Assert.True(valhalla.Life == valhalla.Def.Combat.MaxLife && harness.World.Siege.TotalDamage == 0, "the tower was hurt while crystal towers stand");
        foreach (var crystal in defenders.Where(static monster => monster.CastleRole == CastleObject.Crystal))
        {
            crystal.VirtualLives = 0;
            field.Combat.ApplyPlayerDamage(player, crystal, crystal.Life + 50, false, field.NowMs);
        }

        harness.Tick(1);
        harness.Clock.Now += TimeSpan.FromSeconds(1);
        harness.Tick(1);
        Assert.True(harness.World.Siege is { Crystals: 0, ValhallaFallen: false } view && view.Top is [{ ClanId: 9 }]
            && view.TotalDamage == CastleSiegeRules.TowerSlots * 500L, "the crystal towers' damage was not scored");
        valhalla.VirtualLives = 0;
        field.Combat.ApplyPlayerDamage(player, valhalla, valhalla.Life, false, field.NowMs);
        harness.Tick(1);
        Assert.True(harness.World.Siege is { ValhallaFallen: true, Winner: 9 }, "the tower's fall did not go to the leading clan");
    }

    private static async Task SpareAsync()
    {
        var harness = await SpecialItemTests.Harness.CreateAsync(FieldCodes.Castle, Defenders(), []);
        harness.World.TryGetOpenField(FieldCodes.Castle, out var field);
        await harness.AdvanceAsync(harness.World.OpenSiegeAsync(new CastleSiegeOrder(7, [], [1, 0, 0])));
        harness.Tick(1);
        var player = harness.Player;
        var door = field.MonstersForInspection.Single(static monster => monster.CastleRole == CastleObject.Door);
        var mercenary = field.MonstersForInspection.Single(static monster => monster.CastleRole == CastleObject.Mercenary);
        player.Clan = new ClanTag(7, "Lions", 0, Registered: true);
        Assert.True(!field.SetAttackTarget(player.EntityId, door.EntityId, sustained: false), "the owner clan took its door as a target");
        var life = player.Life;
        field.Combat.BlowOnPlayer(mercenary, player, 100, splash: false);
        Assert.True(player.Life == life, "a defender hurt a player of its clan");
        player.Clan = new ClanTag(9, "Tigers", 0, Registered: true);
        Assert.True(field.SetAttackTarget(player.EntityId, door.EntityId, sustained: false), "another clan could not target the door");

        var count = field.MonstersForInspection.Count();
        harness.Tick(20 * 30);
        Assert.True(field.MonstersForInspection.Count() == count, "defenders left with no player near");
    }

    private static async Task RootedAsync()
    {
        var harness = await SpecialItemTests.Harness.CreateAsync(FieldCodes.Castle, Defenders(), []);
        harness.World.TryGetOpenField(FieldCodes.Castle, out var field);
        await harness.AdvanceAsync(harness.World.OpenSiegeAsync(new CastleSiegeOrder(7, [], [])));
        harness.Tick(1);
        var crystal = field.MonstersForInspection.First(static monster => monster.CastleRole == CastleObject.Crystal);
        var at = crystal.Position;
        crystal.TargetId = harness.Player.EntityId;
        crystal.TargetLockMsLeft = 4000f;
        harness.Tick(20);
        Assert.True(crystal.TargetId == 0 && crystal.TargetLockMsLeft == 0f && crystal.Position == at, "a crystal tower held a prey out of reach");
    }

    private static async Task EasyAsync()
    {
        var harness = await SpecialItemTests.Harness.CreateAsync(FieldCodes.Castle, Defenders(), []);
        harness.World.TryGetOpenField(FieldCodes.Castle, out var field);
        await harness.AdvanceAsync(harness.World.OpenSiegeAsync(new CastleSiegeOrder(7, [], [1, 0, 0])));
        harness.Tick(1);
        await harness.AdvanceAsync(harness.World.WeakenSiegeAsync());
        harness.Tick(20);
        var player = harness.Player;
        player.Clan = new ClanTag(9, "Tigers", 0, Registered: true);
        var defenders = field.MonstersForInspection.ToList();
        var valhalla = defenders.Single(static monster => monster.CastleRole == CastleObject.Valhalla);
        var mercenary = defenders.Single(static monster => monster.CastleRole == CastleObject.Mercenary);
        Assert.True(valhalla.Life == 1 && valhalla.VirtualLives == 0 && mercenary.Life == 1 && mercenary.Potions == 0, "the easy siege did not hold");
        field.Combat.ApplyPlayerDamage(player, valhalla, 100, false, field.NowMs);
        Assert.True(valhalla.IsAlive, "the tower fell while crystal towers stand");
        foreach (var crystal in defenders.Where(static monster => monster.CastleRole == CastleObject.Crystal))
        {
            field.Combat.ApplyPlayerDamage(player, crystal, 100, false, field.NowMs);
        }

        harness.Tick(1);
        field.Combat.ApplyPlayerDamage(player, valhalla, 100, false, field.NowMs);
        harness.Tick(1);
        Assert.True(!valhalla.IsAlive && harness.World.Siege is { ValhallaFallen: true, Winner: 9 }, "the weakened tower did not fall");
    }

    private static async Task PvpAsync()
    {
        var zone = new System.Numerics.Vector3(34500, 0, -26000);
        var north = new System.Numerics.Vector3(34500, 0, -23500);
        Assert.True(CastleSiegeRules.InBattleZone(new System.Numerics.Vector3(0, 0, CastleSiegeRules.BattleLineZ)) && !CastleSiegeRules.InBattleZone(north)
            && CastleSiegeRules.MayFight(zone, 0, zone, 0) && CastleSiegeRules.MayFight(zone, 7, zone, 0) && !CastleSiegeRules.MayFight(zone, 7, zone, 7)
            && !CastleSiegeRules.MayFight(north, 7, zone, 9) && !CastleSiegeRules.MayFight(zone, 7, north, 9), "who may fight differs");
        var blow = new AttackProfile(10_000, 100, 100, 1, Element.Bionic, 0, 0, 0);
        var player = new DefenderProfile(0, 40, 0, 1, ResistanceBlock.Zero, 1000, 1000, IsMonster: false);
        Assert.True(CombatResolver.Resolve(new CombatRng(1), blow, player, alwaysHit: true).Damage == 60
            && CombatResolver.Resolve(new CombatRng(1), blow, player with { IsMonster = true }, alwaysHit: true).Damage == 60
            && CombatResolver.Resolve(new CombatRng(1), blow, player with { Absorption = 100 }, alwaysHit: true).Damage == 1
            && CombatResolver.Resolve(new CombatRng(1), blow with { DamageMin = 1000, DamageMax = 1000 }, player, alwaysHit: true).Damage == 960,
            "a player's absorption is not taken flat");
        Assert.True(CastleSiegeRules.PkScore(95, 7, 9, 0) == 9 && CastleSiegeRules.PkScore(5, 7, 9, 0) == 1 && CastleSiegeRules.PkScore(95, 7, 0, 0) == 0
            && CastleSiegeRules.PkScore(95, 7, 0, 3) == 0 && CastleSiegeRules.PkScore(95, 7, 0, 7) == 9 && CastleSiegeRules.PkScore(95, 0, 9, 9) == 0
            && CastleSiegeRules.PkScore(0, 7, 9, 9) == 0, "the blows' score differs");

        var league = Application.Settings.ServerSettingCatalog.Read(new Dictionary<string, string> { ["castle.mode"] = "2" }, null, new List<string>());
        var weekly = Application.Settings.ServerSettingCatalog.Read(new Dictionary<string, string> { ["castle.mode"] = "1" }, null, new List<string>());
        Assert.True(league.World.CastleLeague && league.Castle.Mode == CastleMode.League && !weekly.World.CastleLeague, "the league setting does not reach the world");

        var harness = await SpecialItemTests.Harness.CreateAsync(FieldCodes.Castle, Defenders(), []);
        harness.World.TryGetOpenField(FieldCodes.Castle, out var field);
        Assert.True(field.PlayersFight, "the castle field does not let players fight");
        var attacker = harness.Player;
        var foe = await harness.JoinAsync("Foe", FieldCodes.Castle, 2);
        attacker.EnterField(zone, inVillage: false);
        foe.EnterField(zone + new System.Numerics.Vector3(20, 0, 0), inVillage: false);
        attacker.Clan = new ClanTag(9, "Tigers", 0, Registered: true);
        foe.Clan = new ClanTag(7, "Lions", 0, Registered: true);
        Assert.True(field.SetAttackTarget(attacker.EntityId, foe.EntityId, sustained: false), "a foe in the battle zone was not a target");
        foe.Clan = attacker.Clan;
        Assert.True(!field.SetAttackTarget(attacker.EntityId, foe.EntityId, sustained: false), "a clan mate was a target");
        foe.Clan = new ClanTag(7, "Lions", 0, Registered: true);
        field.PlayersFight = false;
        Assert.True(!field.SetAttackTarget(attacker.EntityId, foe.EntityId, sustained: false), "players fought during the league");
        field.PlayersFight = true;
        foe.EnterField(north, inVillage: false);
        Assert.True(!field.SetAttackTarget(attacker.EntityId, foe.EntityId, sustained: false), "a player north of the line was a target");
        foe.EnterField(zone + new System.Numerics.Vector3(20, 0, 0), inVillage: false);

        await harness.AdvanceAsync(harness.World.OpenSiegeAsync(new CastleSiegeOrder(7, [], [])));
        var life = foe.Life;
        Assert.True(field.SetAttackTarget(attacker.EntityId, foe.EntityId, sustained: true), "the foe was not taken as a target");
        for (var second = 0; second < 30 && foe.Life == life; second++)
        {
            harness.Tick(20);
        }

        harness.Clock.Now += TimeSpan.FromSeconds(1);
        harness.Tick(1);
        Assert.True(foe.Life < life && harness.World.Siege.Top is [{ ClanId: 9 } score] && score.Damage == Math.Max(1, (life - foe.Life) / CastleSiegeRules.PkScoreDivisor),
            "the swings on a player of the owner clan did not land and score");
        attacker.Hidden = true;
        Assert.True(!field.SetAttackTarget(attacker.EntityId, foe.EntityId, sustained: false), "a hidden player took a foe");
    }

    private static async Task EndAsync()
    {
        var harness = await SpecialItemTests.Harness.CreateAsync(FieldCodes.Castle, Defenders(), []);
        harness.World.TryGetOpenField(FieldCodes.Castle, out var field);
        var member = await harness.JoinAsync("Member", FieldCodes.Castle, 2);
        member.Clan = new ClanTag(7, "Lions", 0, Registered: true);
        await harness.AdvanceAsync(harness.World.OpenSiegeAsync(new CastleSiegeOrder(7, [], [1, 1, 1])));
        harness.Tick(1);
        var weakened = harness.World.WeakenSiegeAsync();
        await harness.AdvanceAsync(weakened);
        Assert.True(await weakened && field.MonstersForInspection.Where(static monster => monster.CastleRole is CastleObject.Door or CastleObject.Crystal)
            .All(static monster => monster.Life == 1 && monster.VirtualLives == 0), "the easy siege did not weaken the door and the crystal towers");
        await harness.AdvanceAsync(harness.World.CloseSiegeAsync());
        Assert.True(!field.MonstersForInspection.Any() && harness.World.Siege.Running == false, "closing kept the defenders");
        var memberAt = member.Position;
        var fallen = await harness.JoinAsync("Fallen", FieldCodes.Castle, 3);
        fallen.ApplyDamage(int.MaxValue / 2);
        var ended = harness.World.EndSiegeAsync(7);
        await harness.AdvanceAsync(ended);
        Assert.True(await ended is [{ } arrival] && arrival.EntityId == harness.Player.EntityId && arrival.FieldId == FieldCodes.Castle
            && member.Position == memberAt && harness.World.Siege == CastleSiegeView.None, "the end did not send only the living outsiders back");
        var after = harness.World.WeakenSiegeAsync();
        await harness.AdvanceAsync(after);
        Assert.True(!await after, "an easy siege ran without a siege");
    }

    // The castle's defenders with small lives: door 10 lives, the tower 50, crystal towers 3, mercenaries 3 potions.
    private static MonsterDefinition[] Defenders() =>
    [.. new[]
    {
        Defender(CastleSiegeRules.DoorKey, CharacterSoundCodes.CastleDoor, 10, 0),
        Defender(CastleSiegeRules.ValhallaKey, CharacterSoundCodes.CastleTower, 50, 300),
        Defender("ice crystal", CharacterSoundCodes.CastleCrystalIce, 3, 400),
        Defender("lightning crystal", CharacterSoundCodes.CastleCrystalLightning, 3, 400),
        Defender("fire crystal", CharacterSoundCodes.CastleCrystalFire, 3, 400),
        Defender("tower crystal", CharacterSoundCodes.CastleCrystalNeutral, 3, 400),
        Defender("ricarten militia", CharacterSoundCodes.CastleSoldierA, 3, 70),
        Defender("ricarten guard", CharacterSoundCodes.CastleSoldierB, 4, 80),
        Defender("bless royal guard", CharacterSoundCodes.CastleSoldierC, 5, 110)
    }.Select(static (definition, index) => definition with { Code = 900 + index })];

    private static MonsterDefinition Defender(string key, int soundCode, int potions, int reach) =>
        ChatFixture.Hopy with
        {
            Key = key,
            SoundCode = soundCode,
            PotionCount = potions,
            Combat = ChatFixture.Hopy.Combat with { MaxLife = 500, MeleeRange = reach }
        };
}
