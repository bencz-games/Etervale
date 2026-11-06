using Microsoft.Xna.Framework;
using PristonTale.Application.World;
using PristonTale.Engine3D.Rendering;
using PristonTale.Gameplay.World.Remote;
using PristonTale.Protocol.Binary;
using PristonTale.Simulation.Characters;
using PristonTale.Simulation.Combat;
using PristonTale.Simulation.Items;
using PristonTale.Simulation.World.Fields;
using PristonTale.Simulation.World.Monsters;
using PristonTale.Simulation.World.Spawning;
using NumVector3 = System.Numerics.Vector3;
using PristonTale.Contracts.Characters;

namespace PristonTale.Tests;

internal static class RangedEquipmentTests
{
    public static IEnumerable<(string, Func<Task>)> Cases()
    {
        yield return ("attached weapons copy parent bone matrices while ground items retain their own transforms", AttachmentAsync);
        yield return ("bows and crossbows preserve original item hands and distinct village attachments", BowHandsAsync);
        yield return ("class equipment permissions distinguish robes and costumes from weapon specialization", RestrictionsAsync);
        yield return ("ranged weapons use item reach in authority, prediction and private presence", ReachAsync);
        yield return ("a bow repeats attacks at range without approaching melee distance", FieldAsync);
    }

    private static Task AttachmentAsync()
    {
        Matrix[] skeleton = [Matrix.CreateRotationZ(1.2f) * Matrix.CreateTranslation(17, 24, -9)];
        Matrix parent = Matrix.CreateRotationY(0.8f) * Matrix.CreateTranslation(100, 50, 80);
        Matrix[] palette = new Matrix[1];
        PristonActorPalette.Fill(skeleton, [0], parent, true, palette);
        Assert.True(palette[0] == parent, "weapon applied its ground bind transform after attachment");
        PristonActorPalette.Fill(skeleton, [0], parent, false, palette);
        Assert.True(palette[0] == skeleton[0] * parent, "ground item lost its authored transform");
        return Task.CompletedTask;
    }

    private static PlayerCharacter Character(PlayerClass job) =>
        new(new CharacterClassRegistry().Get(job), "Ranged", 80, new CharacterAttributes(800, 800, 800, 800, 800));

    private static Task BowHandsAsync()
    {
        // The original item table declares the right hand for crossbows; two-handed occupancy does not select an attachment hand.
        string[] crossbows = ["WS104", "WS108", "WS110", "WS113", "WS117"];
        foreach (string key in crossbows)
        {
            Assert.True(EquipmentVisuals.WeaponBone(key, false) == "Bip weapon01", $"{key} crossbow attached to the left hand");
            Assert.True(EquipmentVisuals.WeaponBone(key, true) == "Bip in-cro", $"{key} crossbow attached to the wrong back bone");
        }

        Assert.True(EquipmentVisuals.WeaponBone("WS101", false) == "Bip01 L Hand", "conventional bow lost its left-hand attachment");
        Assert.True(EquipmentVisuals.WeaponBone("WS101", true) == "Bip in-bow", "conventional bow uses the crossbow back bone");
        Assert.True(EquipmentVisuals.WeaponBone("WS102", true) == "Bip weapon01", "village in-hand crossbow exception was lost");
        Assert.True(EquipmentVisuals.WeaponBone("WS120", false) == "Bip weapon01", "late catalog entry lost its imported hand");
        Assert.True(EquipmentVisuals.WeaponBone("WS120", true) == "Bip in01", "unlisted village weapon bypassed the original default bone");
        Assert.True(EquipmentVisuals.WeaponBone("ws110", false) == "Bip weapon01", "catalog hand lookup became case-sensitive");
        Assert.True(EquipmentVisuals.HasModel("WV101") && EquipmentVisuals.MeshPath("WV101").EndsWith("itWV101.smd", StringComparison.OrdinalIgnoreCase),
            "Martial Artist's starter vambrace has no equipment model");
        Assert.True(EquipmentVisuals.WeaponBone("WV101", false) == "Bip weapon01", "generic vambrace lookup changed");
        var martialArtist = new CharacterClassRegistry().Get(PlayerClass.MartialArtist).Definition;
        var visuals = EquipmentVisuals.Apply(martialArtist.DefaultVisuals, "", "WV101", "", false, martialArtist.MotionJobMask);
        Assert.True(EquipmentVisuals.WeaponBone(visuals, false) == "Bip01 R Forearm"
            && EquipmentVisuals.WeaponBone(visuals, true) == "Bip01 R Forearm"
            && EquipmentVisuals.TwinBone(visuals, false) == "Bip01 L Forearm"
            && EquipmentVisuals.TwinBone(visuals, true) == "Bip01 L Forearm", "Martial Artist's vambraces are not on its forearms");
        Assert.True(EquipmentVisuals.TwinMeshPath(visuals).EndsWith("LitWV101.smd", StringComparison.Ordinal)
            && EquipmentVisuals.TwinMeshPath(EquipmentVisuals.Apply(martialArtist.DefaultVisuals, "", "WV151", "", false, martialArtist.MotionJobMask))
                .EndsWith("LitWV120.smd", StringComparison.Ordinal)
            && EquipmentVisuals.MotionIndex("WV137") == 361 && EquipmentVisuals.MotionIndex("WV151") == EquipmentVisuals.MotionIndex("WV120"),
            "the left vambrace is not its own model, or a vambrace lost its motion index");
        var assassin = new CharacterClassRegistry().Get(PlayerClass.Assassin).Definition;
        Assert.True(EquipmentVisuals.TwinMeshPath(EquipmentVisuals.Apply(assassin.DefaultVisuals, "", "WD101", "", false, assassin.MotionJobMask))
            .EndsWith("itWD101.smd", StringComparison.Ordinal), "the Assassin's left dagger is not the dagger's own model");
        return Task.CompletedTask;
    }

    private static ItemInstance Weapon(ItemKind kind, int range) => new(new ItemDefinition(500, "Fixture", kind, EquipmentSlot.Weapon)
    {
        ShootingRange = range,
        BaseStats = new ItemStatBlock(20, 30, 0, 5000, 0, 0, 0, 0, 0, 0)
    });

    private static Task RestrictionsAsync()
    {
        var mechanic = Character(PlayerClass.Mechanician);
        var mage = Character(PlayerClass.Magician);
        var armor = new ItemInstance(new ItemDefinition(1, "Armor", ItemKind.Armor, EquipmentSlot.Armor)
        {
            AllowedJobs = ItemClassRestrictions.AllowedJobs("DA119")
        });
        var robe = new ItemInstance(new ItemDefinition(2, "Robe", ItemKind.Armor, EquipmentSlot.Armor)
        {
            AllowedJobs = ItemClassRestrictions.AllowedJobs("DA219")
        });
        Assert.True(mechanic.CanEquip(EquipmentSlot.Armor, armor) && !mage.CanEquip(EquipmentSlot.Armor, armor), "heavy armor class restriction differs from original");
        Assert.True(mage.CanEquip(EquipmentSlot.Armor, robe) && !mechanic.CanEquip(EquipmentSlot.Armor, robe), "robe class restriction differs from original");
        Assert.True((ItemClassRestrictions.AllowedJobs("DA131") & (uint)JobBitCode.Archer) == 0, "male costume accepted a female class");
        Assert.True((ItemClassRestrictions.AllowedJobs("DA133") & (uint)JobBitCode.Mechanician) == 0, "female costume accepted a male class");
        Assert.True(ItemClassRestrictions.AllowedJobs("WD101") == (uint)JobBitCode.Assassin && ItemClassRestrictions.AllowedJobs("WN101") == (uint)JobBitCode.Shaman
            && ItemClassRestrictions.AllowedJobs("WV101") == (uint)JobBitCode.MartialArtist
            && (ItemClassRestrictions.AllowedJobs("WA101") & (uint)JobBitCode.MartialArtist) == 0 && (ItemClassRestrictions.AllowedJobs("WA101") & (uint)JobBitCode.Fighter) != 0
            && ItemClassRestrictions.AllowedJobs("WR101") == 0 && ItemClassRestrictions.AllowedJobs("DS101") == 0,
            "a class weapon is not its class's alone, the Martial Artist takes another weapon, or a recipe or shield gained a class");
        var specialized = new ItemInstance(new ItemDefinition(3, "Fighter spec", ItemKind.Axe, EquipmentSlot.Weapon)
        {
            SpecializationJobs = (uint)JobBitCode.Fighter,
            AllowedJobs = ItemClassRestrictions.AllowedJobs("WA101")
        });
        Assert.True(specialized.JobSpecMask == (uint)JobBitCode.Fighter && mechanic.CanEquip(EquipmentSlot.Weapon, specialized),
            "specialization incorrectly prohibits using the weapon");
        return Task.CompletedTask;
    }

    private static Task ReachAsync()
    {
        var character = Character(PlayerClass.Mechanician);
        character.Equip(EquipmentSlot.Weapon, Weapon(ItemKind.Bow, 245));
        var player = new WorldPlayer(1, NumVector3.Zero, character, false, default);
        Assert.True(player.InAttackRange(new NumVector3(245, 0, 0)) && !player.InAttackRange(new NumVector3(246, 0, 0)), "bow reach ignored item data");
        Assert.True(!player.InAttackRange(new NumVector3(100, 65, 0)), "ranged attack bypassed vertical gate");
        var owner = OwnerState.Capture(character);
        Assert.True(AttackPresentation.Decide(Vector2.Zero, new Vector2(200, 0), owner.AttackRange).ShouldAttack, "prediction still approached melee range");
        var buffer = new byte[PristonTale.Protocol.Packets.OwnerStatusPacket.Size];
        PristonTale.Server.Net.WireMapping.Status(owner).Write(buffer);
        Assert.True(PristonTale.Protocol.Packets.OwnerStatusPacket.Read(buffer).AttackRange == 245, "TCP owner state lost ranged reach");
        character.Equip(EquipmentSlot.Weapon, Weapon(ItemKind.ThrowingWeapon, 205));
        owner = OwnerState.Capture(character, owner);
        Assert.True(owner.AttackRange == 205 && !player.InAttackRange(new NumVector3(206, 0, 0)), "weapon swap retained previous bow reach");
        character.Equip(EquipmentSlot.Weapon, Weapon(ItemKind.Staff, 160));
        Assert.True(PlayerAttackReach.For(character) == 50, "Mechanician staff became a spell projectile");
        var mage = Character(PlayerClass.Magician);
        mage.Equip(EquipmentSlot.Weapon, Weapon(ItemKind.Staff, 160));
        Assert.True(PlayerAttackReach.For(mage) == 160, "Magician lost staff shooting mode");
        return Task.CompletedTask;
    }

    private static Task FieldAsync()
    {
        var definition = new MonsterDefinition(1, "Stationary fixture", MonsterFaction.Enemy, MonsterClass.Normal, MonsterBrood.Normal,
            new MonsterCombatStats(4, 0, new MonsterRange(1, 1), 6, 30, 0, 0, 0, 10000, ResistanceBlock.Zero), "");
        var field = new FieldInstance(1, FieldSpawnData.Empty, new MonsterCatalog([definition]), new EntityIdAllocator(), new CombatRng(17));
        var character = Character(PlayerClass.Mechanician);
        character.Equip(EquipmentSlot.Weapon, Weapon(ItemKind.Bow, 245));
        var player = new WorldPlayer(1, NumVector3.Zero, character, false, default);
        field.Add(player);
        var monster = ((ISpawnSink)field).Spawn(definition, new NumVector3(200, 0, 0), -1, 0);
        Assert.True(field.SetAttackTarget(1, monster.EntityId, true), "ranged target rejected");
        int previous = monster.Life;
        int hits = 0;
        for (int index = 0; index < 200; index++)
        {
            field.Step(.05f);
            if (monster.Life < previous)
            {
                hits++;
                previous = monster.Life;
            }
        }

        Assert.True(hits >= 2, "ranged attacks stopped after the first hit");
        Assert.True(player.Position.LengthSquared() < .001f, "bow wielder approached the monster despite being in range");
        return Task.CompletedTask;
    }
}
