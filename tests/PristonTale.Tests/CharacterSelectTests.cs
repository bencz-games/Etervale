using PristonTale.Application.Services;
using PristonTale.Application.Sessions;
using PristonTale.Application.World;
using PristonTale.Gameplay.Screens.CharacterSelect;
using PristonTale.Gameplay.UI.Rendering;
using PristonTale.Gameplay.World.Remote;
using PristonTale.Persistence.Abstractions.Characters;
using PristonTale.Simulation.Characters;
using PristonTale.Simulation.Quests;
using PristonTale.Simulation.Items;
using PristonTale.Simulation.World;
using Microsoft.Xna.Framework.Graphics;
using PristonTale.Contracts.Characters;

namespace PristonTale.Tests;

internal static class CharacterSelectTests
{
    public static IEnumerable<(string, Func<Task>)> Cases()
    {
        yield return ("character summary carries persisted equipment into character selection, a stored weapon pair and a right-hand ring beside it", EquipmentSummaryAsync);
        yield return ("a character in play is not deleted; once released it is", DeleteInPlayAsync);
        yield return ("a new or moved character takes the account's lowest free slot, none past the sixth", FreeSlotAsync);
        yield return ("character-select roster and class positions preserve the original ordering", OriginalPlacementAsync);
        yield return ("character-select face layout scales the authored canvas uniformly", FaceLayoutAsync);
        yield return ("the Assassin, the Shaman and the Martial Artist use the original creation definitions and class facts", ExtendedClassDefinitionsAsync);
        yield return ("a text box takes the window's typed text, so a held Backspace or key repeats, and only while focused", TypedTextAsync);
    }

    private static Task FreeSlotAsync()
    {
        Assert.True(CharacterSlots.FirstFree([]) == 0 && CharacterSlots.FirstFree([0, 1, 3]) == 2, "the lowest gap is taken");
        Assert.True(CharacterSlots.FirstFree([5, 4, 3, 2, 1, 0]) is null, "six characters fill the account");
        return Task.CompletedTask;
    }

    private static Task TypedTextAsync()
    {
        var field = new PristonTale.Gameplay.UI.TextInputField { MaxLength = 8, Focused = true };
        field.Update("Pan zer!_9.-", 0);
        Assert.True(field.Text == "Panzer_9", $"the box took {field.Text}");
        // A held Backspace arrives as the system repeats it, one '\b' per repeat.
        field.Update(new string('\b', 10), 0);
        Assert.True(field.Text.Length == 0, $"a held Backspace left {field.Text}");
        field.Focused = false;
        field.Update("abc", 0);
        Assert.True(field.Text.Length == 0, "an unfocused box took typed text");
        return Task.CompletedTask;
    }

    private static async Task EquipmentSummaryAsync()
    {
        var classes = new CharacterClassRegistry();
        var armor = new ItemDefinition(0x0100_0001, "Doom Armor", ItemKind.Armor, EquipmentSlot.Armor)
        {
            CatalogKey = "DA108"
        };
        var weapon = new ItemDefinition(0x0100_0002, "Claw", ItemKind.Claw, EquipmentSlot.Weapon)
        {
            CatalogKey = "WC101"
        };
        var shield = new ItemDefinition(0x0100_0003, "Shield", ItemKind.Shield, EquipmentSlot.OffHand)
        {
            CatalogKey = "DS101"
        };
        var spare = new ItemDefinition(0x0100_0005, "Spare Claw", ItemKind.Claw, EquipmentSlot.Weapon)
        {
            CatalogKey = "WC102"
        };
        var ring = new ItemDefinition(0x0100_0006, "Ring", ItemKind.Ring, EquipmentSlot.RingLeft)
        {
            CatalogKey = "OR101"
        };
        var itemCatalog = new ItemCatalog([armor, weapon, shield, spare, ring]);
        var player = classes.CreatePlayer(PlayerClass.Mechanician, "Armored", 80);
        player.Equip(EquipmentSlot.Armor, new ItemInstance(armor));
        player.Equip(EquipmentSlot.Weapon, new ItemInstance(weapon));
        player.Equip(EquipmentSlot.OffHand, new ItemInstance(shield));
        player.Equip(EquipmentSlot.RingLeft, new ItemInstance(ring));
        player.Equip(EquipmentSlot.RingRight, new ItemInstance(ring));
        // The stored weapon pair (key W) is saved in the hand slots beside the held one; the summary shows the held pair.
        Assert.True(player.TrySwitchWeaponSet(1), "pair B was not shown");
        player.Equip(EquipmentSlot.Weapon, new ItemInstance(spare));
        Assert.True(player.TrySwitchWeaponSet(0) && player.StoredWeapon?.Definition == spare, "pair A was not held back with pair B stored");

        var starter = CharacterRecordFactory.NewStarter(classes, 1, player.Name, 0, player.Class.Class, 3);
        var record = CharacterRecordFactory.FromLiveSession(starter, player, 3, System.Numerics.Vector3.Zero);
        var repository = new MemoryCharacters(record);
        var sessions = new SessionRegistry();
        var session = sessions.Create(1, "owner");
        var empty = WorldData.Empty;
        var holder = new WorldDataHolder
        {
            Current = new WorldData(itemCatalog, empty.Monsters, empty.Npcs, empty.FieldSpawns, empty.Motions)
        };
        var service = new CharacterAppService(sessions, repository, classes, holder, new WorldCatalog());

        var summary = (await service.ListCharactersAsync(session.SessionToken)).Single();
        Assert.True(
            summary.ArmorKey == "DA108" &&
            summary.WeaponKey == "WC101" &&
            summary.OffHandKey == "DS101" &&
            summary.InVillage,
            "persisted equipment did not reach the character-select summary");

        // Equipment the catalog no longer knows leaves the character listed without its looks, not the login refused.
        var unknown = new WorldDataHolder { Current = new WorldData(new ItemCatalog([]), empty.Monsters, empty.Npcs, empty.FieldSpawns, empty.Motions) };
        var bare = (await new CharacterAppService(sessions, repository, classes, unknown, new WorldCatalog()).ListCharactersAsync(session.SessionToken)).Single();
        Assert.True(bare.Name == "Armored" && bare.ArmorKey.Length == 0 && bare.WeaponKey.Length == 0, "unreadable equipment refused the character list");
    }

    private static async Task DeleteInPlayAsync()
    {
        var classes = new CharacterClassRegistry();
        var record = CharacterRecordFactory.NewStarter(classes, 1, "Played", 0, PlayerClass.Fighter, 3) with { Id = 7 };
        var repository = new MemoryCharacters(record);
        var sessions = new SessionRegistry();
        var deleting = sessions.Create(1, "owner");
        var service = new CharacterAppService(sessions, repository, classes, new WorldDataHolder(), new WorldCatalog());
        Assert.True(sessions.TryClaimCharacter(record.Id, "playing"), "the character could not be claimed");
        Assert.True(!(await service.DeleteCharacterAsync(deleting.SessionToken, "Played")).Ok && !repository.Deleted,
            "a character in play was deleted");
        sessions.ReleaseCharacter(record.Id, "playing");
        Assert.True((await service.DeleteCharacterAsync(deleting.SessionToken, "Played")).Ok && repository.Deleted
            && sessions.TryClaimCharacter(record.Id, "next"), "a released character was not deleted, or its claim stayed");
    }

    private static Task OriginalPlacementAsync()
    {
        Assert.True(CharacterSelectScene.UserCharacterPosition(1, 0) == (0f, -110f), "single character is not centered");
        Assert.True(CharacterSelectScene.UserCharacterPosition(5, 1) == (-35f, -90f), "original second roster slot moved");
        Assert.True(CharacterSelectScene.UserCharacterPosition(5, 4) == (70f, -60f), "original fifth roster slot moved");
        var sixLeft = CharacterSelectScene.UserCharacterPosition(CharacterSlots.Maximum, 0);
        var sixRight = CharacterSelectScene.UserCharacterPosition(CharacterSlots.Maximum, 1);
        Assert.True(sixLeft.X == -sixRight.X && sixLeft.Z == sixRight.Z, "six-character extension is not balanced");
        Assert.True(
            CharacterSelectScene.JobClassForBrood(CharacterBrood.Tempskron, 0) == PlayerClass.Mechanician &&
            CharacterSelectScene.JobClassForBrood(CharacterBrood.Tempskron, 3) == PlayerClass.Archer &&
            CharacterSelectScene.JobClassForBrood(CharacterBrood.Tempskron, 4) == PlayerClass.Assassin &&
            CharacterSelectScene.JobClassForBrood(CharacterBrood.Tempskron, 5) == PlayerClass.MartialArtist &&
            CharacterSelectScene.JobClassForBrood(CharacterBrood.Morion, 2) == PlayerClass.Priestess &&
            CharacterSelectScene.JobClassForBrood(CharacterBrood.Morion, 3) == PlayerClass.Magician &&
            CharacterSelectScene.JobClassForBrood(CharacterBrood.Morion, 4) == PlayerClass.Shaman,
            "class lineup differs from the original select screen");
        return Task.CompletedTask;
    }

    private static Task FaceLayoutAsync()
    {
        var viewport = new Viewport(0, 0, 1920, 975);
        var top = CharSelectLayout.FaceBackdropTop(viewport);
        var bottom = CharSelectLayout.FaceBackdropBottom(viewport);
        var portrait = CharSelectLayout.FacePortrait(viewport);
        var input = CharSelectLayout.FaceInputPosition(viewport);
        var scale = MathF.Min(viewport.Width / 800f, viewport.Height / 600f);
        var expectedTopWidth = (int)MathF.Round(256f * scale);
        var expectedBottomHeight = (int)MathF.Round(128f * scale);

        Assert.True(top.Width == expectedTopWidth && top.Height == expectedTopWidth, "face panel does not preserve its authored square top");
        Assert.True(bottom == new Microsoft.Xna.Framework.Rectangle(top.X, top.Bottom, expectedTopWidth, expectedBottomHeight), "face panel pieces are separated or distorted");
        Assert.True(top.Contains(portrait.Left, portrait.Top) && bottom.Contains(portrait.Right - 1, portrait.Bottom - 1), "portrait escaped the face panel");
        Assert.True(input.Y >= bottom.Top && input.Y < bottom.Bottom, "name input is not inside the face panel");
        return Task.CompletedTask;
    }

    private static Task ExtendedClassDefinitionsAsync()
    {
        var registry = new CharacterClassRegistry();
        var assassin = registry.Get(PlayerClass.Assassin).Definition;
        var shaman = registry.Get(PlayerClass.Shaman).Definition;
        var martialArtist = registry.Get(PlayerClass.MartialArtist).Definition;

        Assert.True(assassin.BaseAttributes == new CharacterAttributes(25, 10, 22, 20, 22), "Assassin creation attributes differ from the original");
        Assert.True(shaman.BaseAttributes == new CharacterAttributes(15, 27, 20, 15, 22), "Shaman creation attributes differ from the original");
        Assert.True(martialArtist.BaseAttributes == new CharacterAttributes(26, 9, 20, 20, 24), "Martial Artist creation attributes differ from the original");
        Assert.True(CharacterSelectScene.JobCharacterPos(0, 5) == (-90f, -320f) && CharacterSelectScene.JobCharacterPos(2, 5) == (0f, -330f)
            && CharacterSelectScene.JobCharacterPos(1, 6) == (-54f, -330f) && CharacterSelectScene.JobCharacterPos(5, 6) == (90f, -320f),
            "the job lineup does not keep its width with five or six classes");
        Assert.True(assassin.FunctionProfile == new CharacterFunctionProfile(80, 3, 3, 2, 1, 2, 0)
            && martialArtist.FunctionProfile == assassin.FunctionProfile && shaman.FunctionProfile == new CharacterFunctionProfile(200, 2, 1, 2, 3, 2, 1),
            "a new class's sight, life, mana, stamina or damage functions differ from the original");
        var profile = ClassProfiles.Of(PlayerClass.MartialArtist);
        Assert.True(profile.TitleAt(1) == "Striker" && profile.TitleAt(4) == "Grand Master" && ClassProfiles.Of(PlayerClass.Shaman).TitleAt(3) == "Soul Catcher"
            && profile.QuestWeapon == "WV109" && profile.BeginnerWeapon == "WV102" && profile.RankUpRing == QuestItems.EmperorRing
            && ClassProfiles.Of(PlayerClass.Assassin) is { BeginnerWeapon: "WD102" } assassinProfile && assassinProfile.RankUpRing == QuestItems.ArcaneRing
            && ClassProfiles.Of(PlayerClass.Shaman).BeginnerWeapon == "WN101"
            && profile.Shift == new RequirementShift(new(10, 15), new(-20, -15), default, new(-20, -15))
            && assassinProfile.Shift == new RequirementShift(new(5, 15), new(-20, -10), default, new(-20, -15))
            && ClassProfiles.Of(PlayerClass.Shaman).Shift == new RequirementShift(new(-25, -20), new(15, 25), new(-15, -10), new(-20, -15)),
            "a new class's titles, quest items or specialization shift differ from the original");
        Assert.True(assassin.DefaultVisuals.Body.InxExtensionPath.EndsWith("e001.inx", StringComparison.OrdinalIgnoreCase), "Assassin body differs from the original");
        Assert.True(shaman.DefaultVisuals.Body.InxExtensionPath.EndsWith("me001.inx", StringComparison.OrdinalIgnoreCase), "Shaman body differs from the original");
        var armoredAssassin = EquipmentVisuals.Apply(assassin.DefaultVisuals, "DA108", "", "", true, assassin.MotionJobMask);
        var armoredShaman = EquipmentVisuals.Apply(shaman.DefaultVisuals, "DA208", "", "", true, shaman.MotionJobMask);
        Assert.True(armoredAssassin.Body.InxExtensionPath.EndsWith("E008.inx", StringComparison.OrdinalIgnoreCase), "Assassin armor did not use the E resource row");
        Assert.True(armoredShaman.Body.InxExtensionPath.EndsWith("mE008.inx", StringComparison.OrdinalIgnoreCase), "Shaman robe did not use the mE resource row");
        var armoredMartialArtist = EquipmentVisuals.Apply(martialArtist.DefaultVisuals, "DA102", "", "", true, martialArtist.MotionJobMask);
        Assert.True(martialArtist.DefaultVisuals.Body.InxExtensionPath.EndsWith("f001.inx", StringComparison.OrdinalIgnoreCase)
            && armoredMartialArtist.Body.InxExtensionPath.EndsWith("f002.inx", StringComparison.OrdinalIgnoreCase)
            && ClassProfiles.Of(PlayerClass.MartialArtist).Female, "Martial Artist did not use its female f body row");
        Assert.True(HudArt.ClassDirectory(PlayerClass.MartialArtist) == "Martial", "Martial Artist HUD assets use the wrong class folder");
        _ = registry.CreatePlayer(PlayerClass.Assassin, "Assassin");
        _ = registry.CreatePlayer(PlayerClass.Shaman, "Shaman");
        _ = registry.CreatePlayer(PlayerClass.MartialArtist, "Martial Artist");
        return Task.CompletedTask;
    }
}
