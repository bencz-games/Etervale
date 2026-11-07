using PristonTale.Application.Castle;
using PristonTale.Application.World;
using PristonTale.Contracts.Characters;
using PristonTale.Contracts.Combat;
using PristonTale.Contracts.Skills;
using PristonTale.Simulation.Characters;
using PristonTale.Simulation.Combat;
using PristonTale.Simulation.Items;
using PristonTale.Simulation.Items.Usable;
using PristonTale.Simulation.Skills;
using PristonTale.Simulation.Social.Castle;
using PristonTale.Simulation.Social.Clans;
using PristonTale.Simulation.World.Ai;
using PristonTale.Simulation.World.Monsters.Skills;
using PristonTale.Simulation.World.Monsters;
using PristonTale.Simulation.World.Npcs;
using PristonTale.Simulation.World;

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
        yield return ("damage skills reach players in the battle zone: a bolt on a foe, a ball on the foes around it, never a clan mate or the north",
            SkillsAsync);
        yield return ("a kill calls its summons: a Wanted Morif three Mini Morifs, a crystal tower ten guards and the door six soldiers at their posts",
            KillSummonsAsync);
        yield return ("a member of the owner clan calls one mercenary with a crystal during a siege, a relog too, five of a kind, and they leave with the siege",
            CalledMercenariesAsync);
        yield return ("the soul fountain's turns go round the owner clan's members at it, and a recipient that left gets nothing", FountainTurnsAsync);
        yield return ("the owner clan's members hold the clan skill in force, renewed every minute; others and an old skill lose it", ClanSkillAsync);
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

    private static Task SkillsAsync()
    {
        const uint fireBolt = 0x08010002;
        const uint fireBall = 0x08010004;
        const uint raving = 0x02010003;
        const uint holyMind = 0x07010004;
        const uint chainLightning = 0x07080003;
        Assert.True(SkillRuleCatalog.TryGet(fireBolt, out var bolt) && SkillFormulas.ReachesPlayers(bolt)
            && SkillRuleCatalog.TryGet(raving, out var weapon) && SkillFormulas.ReachesPlayers(weapon)
            && SkillRuleCatalog.TryGet(holyMind, out var debuff) && !SkillFormulas.ReachesPlayers(debuff)
            && SkillRuleCatalog.TryGet(chainLightning, out var chain) && !SkillFormulas.ReachesPlayers(chain), "the skills that reach players differ");

        var zone = new System.Numerics.Vector3(34500, 0, CastleSiegeRules.BattleLineZ - 2000);
        var north = new System.Numerics.Vector3(34500, 0, CastleSiegeRules.BattleLineZ + 500);
        var scenario = SkillScenario.Create(PlayerClass.Magician, 60, SkillScenario.Weapon(ItemKind.Staff, "WM101")).Learn(fireBolt, 5).Learn(fireBall, 5);
        scenario.Field.PlayersFight = true;
        scenario.Player.EnterField(zone, inVillage: false);
        scenario.Player.Clan = new ClanTag(9, "Tigers", 0, Registered: true);
        var classes = new CharacterClassRegistry();
        WorldPlayer Join(FieldInstance field, ulong id, System.Numerics.Vector3 at, long clan)
        {
            var player = new WorldPlayer(id, at, classes.CreatePlayer(PlayerClass.Knight, "Foe" + id, 60), false, default)
            {
                Clan = new ClanTag(clan, "Clan" + clan, 0, Registered: true)
            };
            field.Add(player);
            player.EnterField(at, inVillage: false);
            return player;
        }

        var foe = Join(scenario.Field, 2, zone + new System.Numerics.Vector3(120, 0, 0), 7);
        var near = Join(scenario.Field, 3, zone + new System.Numerics.Vector3(140, 0, 20), 7);
        var mate = Join(scenario.Field, 4, zone + new System.Numerics.Vector3(130, 0, -20), 9);
        var (foeLife, nearLife, mateLife) = (foe.Life, near.Life, mate.Life);
        Assert.True(scenario.Cast(fireBolt, mate.EntityId).Verdict == SkillCastVerdict.InvalidTarget, "a bolt took a clan mate");
        Assert.True(scenario.Cast(fireBolt, foe.EntityId).Verdict == SkillCastVerdict.Accepted, "a bolt did not take a foe");
        scenario.Run(20 * 3, () => foe.Life < foeLife);
        Assert.True(foe.Life < foeLife && scenario.Combat.Any(hit => hit.Kind == CombatEventKind.Hit && hit.TargetId == foe.EntityId),
            "the bolt did not hurt the foe");

        scenario.ClearCooldowns();
        scenario.Refill();
        foeLife = foe.Life;
        Assert.True(scenario.Cast(fireBall, foe.EntityId).Verdict == SkillCastVerdict.Accepted, "a ball did not take a foe");
        scenario.Run(20 * 3, () => foe.Life < foeLife && near.Life < nearLife);
        Assert.True(foe.Life < foeLife && near.Life < nearLife && mate.Life == mateLife, "the ball did not hurt the foes around and spare the mate");

        scenario.ClearCooldowns();
        foe.EnterField(north, inVillage: false);
        scenario.Player.EnterField(north + new System.Numerics.Vector3(0, 0, -100), inVillage: false);
        Assert.True(scenario.Cast(fireBolt, foe.EntityId).Verdict == SkillCastVerdict.InvalidTarget, "a bolt took a foe north of the line");

        var priestess = SkillScenario.Create(PlayerClass.Priestess, 80, SkillScenario.Weapon(ItemKind.Staff, "WM101")).Learn(holyMind, 5).Learn(chainLightning, 5);
        priestess.Field.PlayersFight = true;
        priestess.Player.EnterField(zone, inVillage: false);
        var target = Join(priestess.Field, 2, zone + new System.Numerics.Vector3(100, 0, 0), 7);
        Assert.True(priestess.Cast(holyMind, target.EntityId).Verdict == SkillCastVerdict.InvalidTarget
            && priestess.Cast(chainLightning, target.EntityId).Verdict == SkillCastVerdict.InvalidTarget, "a skill that cannot hurt a player took one");

        var fighter = SkillScenario.Create(PlayerClass.Fighter, 60, SkillScenario.Weapon(ItemKind.Axe, "WA101", 200, 220)).Learn(raving, 5);
        fighter.Field.PlayersFight = true;
        fighter.Player.EnterField(zone, inVillage: false);
        var struck = Join(fighter.Field, 2, zone + new System.Numerics.Vector3(30, 0, 0), 7);
        var struckLife = struck.Life;
        Assert.True(fighter.Cast(raving, struck.EntityId).Verdict == SkillCastVerdict.Accepted, "a weapon skill did not take a foe");
        fighter.Run(20 * 4, () => struck.Life < struckLife);
        Assert.True(struck.Life < struckLife, "the weapon skill did not hurt the foe");
        return Task.CompletedTask;
    }

    private static async Task KillSummonsAsync()
    {
        MonsterDefinition[] called =
        [
            .. KillSummons.CrystalGuards.Select(static (key, index) => Defender(key, 0, 0, 70) with { Code = 950 + index }),
            Defender(KillSummons.DoorSoldier, 0, 0, 70) with { Code = 953 },
            Defender("Wanted Morif", 0, 0, 70) with { Code = 954 },
            Defender("Mini morif", 0, 0, 70) with { Code = 955 }
        ];
        var harness = await SpecialItemTests.Harness.CreateAsync(FieldCodes.Castle, [.. Defenders(), .. called], []);
        harness.World.TryGetOpenField(FieldCodes.Castle, out var field);
        var killer = harness.Player.EntityId;
        static float Planar(System.Numerics.Vector3 a, System.Numerics.Vector2 b) => System.Numerics.Vector2.Distance(new(a.X, a.Z), b);
        var morif = field.SpawnForGameMaster(called[4], new System.Numerics.Vector3(34000, 0, -24000), false);
        field.CallKillSummons(morif, killer);
        var minis = field.MonstersForInspection.Where(static monster => monster.Def.Key == "Mini morif").ToList();
        Assert.True(minis.Count == 3 && minis.All(mini => Planar(mini.Position, new(morif.Position.X, morif.Position.Z)) <= KillSummons.Spread * 1.5f
            && mini.CastleRole == CastleObject.None), "a Wanted Morif's death did not call three Mini Morifs where it fell");
        await harness.AdvanceAsync(harness.World.OpenSiegeAsync(new CastleSiegeOrder(7, [], [])));
        harness.Tick(1);
        var defenders = field.MonstersForInspection.ToList();
        var crystal = defenders.First(static monster => monster.CastleRole == CastleObject.Crystal
            && Planar(monster.Position, new(CastleSiegeRules.CrystalSlots[0].X, CastleSiegeRules.CrystalSlots[0].Z)) < 1);
        field.CallKillSummons(crystal, killer);
        var guards = field.MonstersForInspection.Where(static monster => KillSummons.CrystalGuards.Contains(monster.Def.Key)).ToList();
        Assert.True(guards.Count == KillSummons.CrystalGuardCount && guards.Select(static guard => guard.Def.Key).Distinct().Count() == 1
            && guards.All(guard => guard.ClanId == 7 && guard.CastleRole == CastleObject.Mercenary
                && Planar(guard.Position, KillSummons.GuardPosts[1]) <= KillSummons.Spread * 1.5f),
            "a fallen crystal tower did not call ten guards of one kind at the post near it, for the owner clan");
        var door = defenders.Single(static monster => monster.CastleRole == CastleObject.Door);
        field.CallKillSummons(door, killer);
        var soldiers = field.MonstersForInspection.Where(static monster => monster.Def.Key == KillSummons.DoorSoldier).ToList();
        Assert.True(soldiers.Count == KillSummons.DoorSoldierCount && soldiers.All(soldier => Planar(soldier.Position, KillSummons.GuardPosts[0]) <= KillSummons.Spread * 1.5f),
            "the fallen door did not call six soldiers at its post");
        await harness.AdvanceAsync(harness.World.CloseSiegeAsync());
        harness.Tick(1);
        Assert.True(!field.MonstersForInspection.Any(static monster => KillSummons.CrystalGuards.Contains(monster.Def.Key) || monster.Def.Key == KillSummons.DoorSoldier),
            "the called guards outlived the siege");
        field.CallKillSummons(crystal, killer);
        Assert.True(!field.MonstersForInspection.Any(static monster => KillSummons.CrystalGuards.Contains(monster.Def.Key)), "a tower called guards without a siege");
    }

    private static async Task CalledMercenariesAsync()
    {
        var harness = await SpecialItemTests.Harness.CreateAsync(FieldCodes.Castle, Defenders(), [], "GP114", "GP115");
        harness.World.TryGetOpenField(FieldCodes.Castle, out var field);
        var character = harness.Session.Character!;
        harness.Player.Clan = new ClanTag(7, "Lions", 0, Registered: true);
        var early = harness.Use(new ItemUseCommand(Guid.NewGuid(), character.InventoryRevision, harness.Id("GP114")));
        await harness.AdvanceAsync(early);
        Assert.True((await early).Outcome.Status == ItemUseStatus.CrystalRefused && harness.Item("GP114") is not null, "a mercenary was called without a siege");
        await harness.AdvanceAsync(harness.World.OpenSiegeAsync(new CastleSiegeOrder(7, [], [])));
        harness.Tick(1);
        var called = harness.Use(new ItemUseCommand(Guid.NewGuid(), character.InventoryRevision, harness.Id("GP114")));
        await harness.AdvanceAsync(called);
        harness.Clock.Now = harness.Clock.Now.AddSeconds(2);
        harness.Tick(1);
        var mercenary = field.MonstersForInspection.Single(static monster => monster.CastleRole == CastleObject.Mercenary);
        Assert.True((await called).Outcome.Ok && harness.Item("GP114") is null && mercenary.ClanId == 7 && mercenary.Def.Key == CastleSiegeRules.MercenaryKeys[0]
            && mercenary.Potions == mercenary.Def.PotionCount && System.Numerics.Vector3.Distance(mercenary.Position, harness.Player.Position) < 64
            && harness.World.Siege.Mercenaries is [1, 0, 0], "the owner clan's member did not call a militia where it stands, counted with the siege");
        var second = harness.Use(new ItemUseCommand(Guid.NewGuid(), character.InventoryRevision, harness.Id("GP115")));
        await harness.AdvanceAsync(second);
        Assert.True((await second).Outcome.Status == ItemUseStatus.MercenaryLimit && harness.Item("GP115") is not null,
            "a member called a second mercenary");
        Simulation.World.Monsters.MonsterDefinition Mercenary(int kind) => field.TryFindMonsterKey(CastleSiegeRules.MercenaryKeys[kind], out var found)
            ? found
            : throw new AssertionException("a mercenary is not in the catalog");
        var (militia, guard) = (Mercenary(0), Mercenary(1));
        var relogged = await harness.JoinAsync(character.Name, FieldCodes.Castle, 9);
        relogged.Clan = new ClanTag(7, "Lions", 0, Registered: true);
        Assert.True(field.MercenaryRoom(relogged, guard) == ItemUseStatus.MercenaryLimit, "a relog called a second mercenary");
        var members = new List<WorldPlayer>();
        for (var account = 2; account <= 6; account++)
        {
            var member = await harness.JoinAsync("Member" + account, FieldCodes.Castle, account);
            member.Clan = new ClanTag(7, "Lions", 0, Registered: true);
            members.Add(member);
            var room = field.MercenaryRoom(member, militia);
            Assert.True(account < 6 ? room == ItemUseStatus.Used && field.CallMercenary(member, militia) is not null
                    : room == ItemUseStatus.MercenaryLimit && field.MercenaryRoom(member, guard) == ItemUseStatus.Used,
                "the kind's limit of five called mercenaries differs");
        }

        mercenary.Life = 0;
        Assert.True(field.MercenaryRoom(members[^1], militia) == ItemUseStatus.Used, "a fallen called mercenary still held its place");
        var outsider = await harness.JoinAsync("Outsider", FieldCodes.Castle, 7);
        outsider.Clan = new ClanTag(8, "Tigers", 0, Registered: true);
        Assert.True(field.MercenaryRoom(outsider, guard) == ItemUseStatus.CrystalRefused, "a player outside the owner clan may call a mercenary");
        await harness.AdvanceAsync(harness.World.WeakenSiegeAsync());
        var weak = field.CallMercenary(members[^1], guard);
        var easy = field.MonstersForInspection.Last(static monster => monster.CastleRole == CastleObject.Mercenary);
        Assert.True(weak is not null && easy.Def.Key == CastleSiegeRules.MercenaryKeys[1] && easy.Life == 1 && easy.Potions == 0,
            "a mercenary called in an easy siege kept its life");
        await harness.AdvanceAsync(harness.World.CloseSiegeAsync());
        harness.Tick(1);
        Assert.True(!field.MonstersForInspection.Any(static monster => monster.CastleRole == CastleObject.Mercenary)
            && field.CallMercenary(members[^1], guard) is null, "the called mercenaries outlived the siege");
        await harness.AdvanceAsync(harness.World.OpenSiegeAsync(new CastleSiegeOrder(0, [], [])));
        harness.Tick(1);
        Assert.True(field.MercenaryRoom(members[^1], guard) == ItemUseStatus.CrystalRefused, "a mercenary was called for a castle without an owner");
    }

    private static async Task FountainTurnsAsync()
    {
        var harness = await SpecialItemTests.Harness.CreateAsync(FieldCodes.Castle, Defenders(), []);
        var first = harness.Player;
        var second = await harness.JoinAsync("Second", FieldCodes.Castle, 2);
        var outsider = await harness.JoinAsync("Outsider", FieldCodes.Castle, 3);
        var away = await harness.JoinAsync("Away", FieldCodes.Castle, 4);
        var fountain = new System.Numerics.Vector3(37100, 0, -30200);
        foreach (var player in new[] { first, second, outsider })
        {
            player.Teleport(fountain);
        }

        foreach (var player in new[] { first, second, away })
        {
            player.Clan = new ClanTag(7, "Lions", 0, Registered: true);
        }

        harness.World.SetOwner(7, CastleClanSkill.None, 0);
        var (low, high) = first.EntityId < second.EntityId ? (first.EntityId, second.EntityId) : (second.EntityId, first.EntityId);
        async Task<ulong> After(ulong last) => (await WorldPump.RunAsync(harness.World, harness.World.FountainRecipientAsync(7, last)))?.EntityId ?? 0;
        Assert.True(await After(0) == low && await After(low) == high && await After(high) == low && await After(ulong.MaxValue) == low,
            "the fountain's turns do not go round the owner clan's members at it by entity id");
        Assert.True(await WorldPump.RunAsync(harness.World, harness.World.FountainRecipientAsync(8, 0)) is null,
            "a player outside the owner clan, or away from the fountain, had a turn");
        harness.Player.Teleport(new System.Numerics.Vector3(37100, 0, -30000));
        Assert.True(await WorldPump.RunAsync(harness.World, harness.World.PrepareFountainPotionAsync(harness.Session)) is null,
            "a recipient that left the fountain before its potion was prepared still received it");
    }

    private static async Task ClanSkillAsync()
    {
        var harness = await SpecialItemTests.Harness.CreateAsync(FieldCodes.Castle, Defenders(), []);
        var member = harness.Player;
        member.Clan = new ClanTag(7, "Lions", 0, Registered: true);
        var outsider = await harness.JoinAsync("Outsider", FieldCodes.Castle, 2);
        var absorb = CastleClanSkills.StatusCode(CastleClanSkill.Absorb);
        var damage = CastleClanSkills.StatusCode(CastleClanSkill.Damage);
        var absorption = member.Character.Effects.Totals.Flat(Simulation.Modifiers.StatKind.Absorption);
        harness.World.SetOwner(7, CastleClanSkill.Absorb, 0);
        harness.Tick(2);
        Assert.True(member.Character.Effects.TryGet(absorb, out var running) && running.DurationMs > (CastleClanSkills.LastSeconds - 1) * 1000f
            && member.Character.Effects.Totals.Flat(Simulation.Modifiers.StatKind.Absorption) == absorption + CastleClanSkills.AbsorbBonus
            && !outsider.Character.Effects.TryGet(absorb, out _), "the owner clan's member did not hold the absorb skill alone");
        harness.World.SetOwner(7, CastleClanSkill.Damage, 0);
        harness.Tick(2);
        Assert.True(!member.Character.Effects.TryGet(absorb, out _) && member.Character.Effects.TryGet(damage, out _), "the old skill outlived the new one");
        harness.World.SetOwner(8, CastleClanSkill.Damage, 0);
        harness.Tick(2);
        Assert.True(!member.Character.Effects.TryGet(damage, out _), "a member of a clan that lost the castle kept its skill");
        Assert.True(Simulation.Items.Usable.ItemStatuses.Icon(damage)!.EndsWith("wa1-3.tga") && Simulation.Items.Usable.ItemStatuses.Name(absorb) == "Absorb Up",
            "the clan skill's status shows no icon or name");
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
