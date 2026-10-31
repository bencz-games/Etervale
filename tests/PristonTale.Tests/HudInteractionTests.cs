using PristonTale.Simulation.Modifiers;
using PristonTale.Simulation.Skills;
using PristonTale.Simulation.Characters;
using PristonTale.Simulation.Items;
using PristonTale.Simulation.World;
using PristonTale.Gameplay.UI.Inventory;
using PristonTale.Gameplay.UI.Npc;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using PristonTale.Engine3D.Formats;
using PristonTale.Engine3D.Rendering;
using PristonTale.Gameplay.UI;
using PristonTale.Gameplay.World;
using PristonTale.Gameplay.World.Remote;
using PristonTale.Gameplay.UI.Widgets;
using PristonTale.Gameplay.UI.Rendering;
using PristonTale.Contracts.Characters;
using PristonTale.Contracts.World;

namespace PristonTale.Tests;

internal static class HudInteractionTests
{
    private static readonly Point[] Viewports =
    [
        new(800, 600), new(1024, 768), new(1280, 720), new(1600, 1200), new(1920, 1080), new(2560, 1440), new(3440, 1440), new(3840, 2160)
    ];

    public static IEnumerable<(string, Func<Task>)> Cases()
    {
        yield return ("original skill shortcuts bind the held mouse side without selecting on release", ShortcutsAsync);
        yield return ("name plate life bars follow the cursor when classic, the click or an attack otherwise; the outline fades", NamePlatesAsync);
        yield return ("UI color keys preserve crimson, metal and opaque world pixels", ColorKeysAsync);
        yield return ("original HUD uses the largest integer scale and the 800x600 bottom/right anchors", LayoutAsync);
        yield return ("owner combat statistics survive the owner-status packet", OwnerStatsAsync);
        yield return ("original HUD hit rectangles stay inside their windows without overlapping", HitRectsAsync);
        yield return ("a carried item is set down by a second click on an original equipment slot with the owner revision", EquipmentDragAsync);
        yield return ("with an item carried, the item under it is described but not hovered, and the carried one's own cells describe nothing", CarriedOverAsync);
        yield return ("a number typed above the count box's maximum shows the maximum at once", CountBoxAsync);
        yield return ("the weapon pair key holds the other pair with the window closed once the motion in progress ends, the next swing's start included", WeaponPairKeyAsync);
        yield return ("a worn two-handed weapon is also shown, taken and exchanged in the empty off-hand slot", TwoHandedShadowAsync);
        yield return ("the page button with an item on the cursor switches the page taking the item along, which stays on the cursor", CarriedPageButtonAsync);
        yield return ("an item set down stays where it was set down, not back in its old cell, until the owner state shows it moved", SettleAsync);
        yield return ("an item thrown to the ground shows nowhere until the owner state shows it gone, and back in its place when refused", ThrownAsync);
        yield return ("an item that arrives by itself sounds, a moved item sounds on its reply, and a page switch sounds nothing", ArrivalSoundAsync);
        yield return ("a ground item picked up with the window open comes to the cursor, and goes where any item goes with it closed", PickupToCursorAsync);
        yield return ("the pointer attacks over wild monsters, talks over NPCs and stays the arrow elsewhere", CursorKindAsync);
        yield return ("drop names on one spot rise into a column above the nearest, which keeps its place", DropNameColumnAsync);
        yield return ("the settings offer only the monitor's sizes: all in fullscreen, those that fit in a window, none borderless", ResolutionsAsync);
        yield return ("a carried item lands where the original sets it: free cells, one item to exchange, a potion to join, a slot of its kind", PlacementAsync);
        yield return ("original gauges crop bitmaps from the top and follow the source formulas", GaugeAsync);
        yield return ("atlas gauge separation preserves the frame and masks every rectangular corner", AtlasGaugeAsync);
        yield return ("HUD overlays tolerate uninitialized and tiny resized viewports", OverlayAsync);
        yield return ("bottom windows slide with the original 70 Hz steps", SlideAsync);
        yield return ("minimap crops the field map around the player like the original", MinimapAsync);
    }

    private static Task OwnerStatsAsync()
    {
        var character = new CharacterClassRegistry().CreatePlayer(PlayerClass.Mechanician, "Stats", 1);
        var state = OwnerState.Capture(character) with { AttackRating = 173, MoveSpeed = 3 };
        var buffer = new byte[PristonTale.Protocol.Packets.OwnerStatusPacket.Size];
        PristonTale.Server.Net.WireMapping.Status(state).Write(buffer);
        var decoded = PristonTale.Protocol.Packets.OwnerStatusPacket.Read(buffer);
        Assert.True(decoded is { AttackRating: 173, MoveSpeed: 3 }, "online HUD lost authoritative derived values");
        return Task.CompletedTask;
    }

    private static Task LayoutAsync()
    {
        var expected = new Dictionary<Point, int>
        {
            [new Point(800, 600)] = 1, [new Point(1024, 768)] = 1, [new Point(1280, 720)] = 1, [new Point(1600, 1200)] = 2,
            [new Point(1920, 1080)] = 1, [new Point(2560, 1440)] = 2, [new Point(3440, 1440)] = 2, [new Point(3840, 2160)] = 3
        };
        foreach (var size in Viewports)
        {
            var layout = HudLayout.Create(size.X, size.Y);
            Assert.True(layout.Scale == expected[size], $"wrong integer scale for {size}");
            Assert.True(layout.Width == size.X / layout.Scale && layout.Height == size.Y / layout.Scale, "logical size is not the integer division");
            Assert.True(layout.Width >= HudLayout.DesignWidth && layout.Height >= HudLayout.DesignHeight, "scale shrank the 800x600 design space");
            var viewport = new Rectangle(0, 0, layout.Width, layout.Height);
            foreach (var button in Enum.GetValues<HudMenuButton>())
            {
                var bounds = layout.Bottom(HudBar.MenuButton(button));
                Assert.True(viewport.Contains(bounds) && bounds.Bottom <= layout.Height && bounds.X < HudLayout.DesignWidth,
                    "menu button is not bottom-left anchored");
            }

            var minimap = HudMinimap.Box(layout);
            Assert.True(viewport.Contains(minimap) && minimap.Right == layout.Width - 16 && minimap.Bottom == layout.Height - 46,
                "minimap is not bottom-right anchored");
            var window = HudInventoryLayout.Window(HudInventoryLayout.Origin(layout));
            Assert.True(window.Bottom == layout.Height && window.X == 0, "bottom windows are not bottom-left anchored");
            Assert.True(viewport.Contains(HudCombat.TargetBox(layout)), "target box escaped the viewport");
            var scaled = new Point(minimap.Center.X * layout.Scale + layout.Scale - 1, minimap.Center.Y * layout.Scale);
            Assert.True(layout.ToLogical(scaled) == minimap.Center, "scaled pointer does not map back to logical pixels");
        }

        return Task.CompletedTask;
    }

    private static Task HitRectsAsync()
    {
        var menu2 = new Rectangle(544, 536, 256, 64);
        var buttons = Enum.GetValues<HudMenuButton>().Select(HudBar.MenuButton).ToArray();
        var toggles = Enum.GetValues<HudToggle>().Select(HudBar.Toggle).ToArray();
        AssertDisjointInside(buttons, menu2, "menu buttons");
        AssertDisjointInside(toggles, menu2, "walk/camera/map toggles");
        Assert.True(!buttons.Any(button => toggles.Any(button.Intersects)), "toggles overlap menu buttons");
        Assert.True(!HudBar.Socket(false).Intersects(HudBar.Socket(true)), "skill sockets overlap");
        Assert.True(HudBar.Body.Any(body => body.Contains(HudBar.Socket(false))) && HudBar.Body.Any(body => body.Contains(HudBar.Socket(true))),
            "skill sockets are not pointer-captured");

        var origin = Point.Zero;
        var window = HudInventoryLayout.Window(origin);
        var bag = HudInventoryLayout.Bag(origin);
        Assert.True(window.Contains(bag) && bag.Width == 12 * 22 && bag.Height == 6 * 22, "bag grid differs from the original inventory origin");
        AssertDisjointInside(Enumerable.Range(0, 10).Select(slot => HudInventoryLayout.Equipment(origin, slot)).Append(bag).ToArray(), window,
            "equipment slots and bag");
        Assert.True(HudInventoryLayout.Equipment(origin, (int)EquipmentSlot.Weapon) == new Rectangle(575, 434, 65, 110), "weapon slot moved");
        Assert.True(HudInventoryLayout.Cell(origin, 71) == new Rectangle(23 + 22 * 11, 436 + 22 * 5, 22, 22), "last bag cell not addressed");
        Assert.True(window.Contains(HudInventoryLayout.Close(origin)) && !HudInventoryLayout.Close(origin).Intersects(bag), "inventory close misplaced");

        // Skill node rectangles overlap at their transparent hexagon corners; every centre must hit only its own node.
        var nodes = Enumerable.Range(0, HudSkillTreeLayout.NodeCount).Select(index => HudSkillTreeLayout.Node(origin, index))
            .Append(HudSkillTreeLayout.BasicAttack(origin)).ToArray();
        foreach (var node in nodes)
        {
            Assert.True(window.Contains(node) && nodes.Count(other => other.Contains(node.Center)) == 1, "skill node escaped or is covered");
        }

        nodes = nodes[..HudSkillTreeLayout.NodeCount];
        for (var index = 0; index < nodes.Length; index++)
        {
            var gauge = HudSkillTreeLayout.RankGauge(origin, index);
            Assert.True(window.Contains(gauge) && gauge.Left > nodes[index].Left, "skill rank gauge escaped the window");
        }

        var allocate = Enumerable.Range(0, 5).Select(index => HudStatusPanel.AllocateButton(origin, index)).ToArray();
        AssertDisjointInside(allocate.Append(HudStatusPanel.Close(origin)).ToArray(), window, "status buttons");
        Assert.True(allocate[0] == new Rectangle(566, 432, 18, 17), "strength arrow differs from PointButton");

        var entries = Enum.GetValues<HudSystemEntry>().Select(HudSystemMenu.Entry).ToArray();
        AssertDisjointInside(entries, HudSystemMenu.Bounds, "system menu entries");
        Assert.True(!buttons.Any(HudSystemMenu.Bounds.Intersects), "system menu covers the menu buttons");
        Assert.True(HudSystemMenu.Entry(HudSystemEntry.Help) == new Rectangle(698, 504, 96, 20) && HudSystemMenu.Entry(HudSystemEntry.Exit) == new Rectangle(698, 535, 96, 20)
            && HudSystemMenu.Entry(HudSystemEntry.Settings).Bottom < 504, "the exit menu's rows left the original sheet, or settings is not above it");

        var design = new Rectangle(0, 0, HudLayout.DesignWidth, HudLayout.DesignHeight);
        Assert.True(design.Contains(HudNpcPanel.Box) && HudNpcPanel.Box.Contains(HudNpcPanel.OkButton) && HudNpcPanel.Box.Contains(HudNpcPanel.Title),
            "NPC help box escaped 800x600");
        Assert.True(HudNpcPanel.Box == new Rectangle(50, 45, 352, 352), "NPC help box differs from QuestMessageBoxPosi2/Size2");
        Assert.True(design.Contains(HudSettingsPanel.Box) && HudSettingsPanel.Box.Contains(HudSettingsPanel.OkButton)
            && HudSettingsPanel.Box.Contains(HudSettingsPanel.Row(15)), "settings rows escaped the help box");
        AssertDisjointInside([HudMessageBox.Accept, HudMessageBox.Reject], HudMessageBox.Bounds, "message box buttons");
        HudMessageBox.Place(HudLayout.Create(1920, 1080));
        Assert.True(HudMessageBox.Bounds == new Rectangle(876, 478, HudMessageBox.VisibleWidth, HudMessageBox.Height) && HudMessageBox.Bounds.Contains(HudMessageBox.Accept)
            && HudMessageBox.Bounds.Contains(HudMessageBox.Reject), "the message box is not centred on a wide screen, or its buttons stayed behind");
        var layout = HudLayout.Create(800, 600);
        HudMessageBox.Place(layout);
        Assert.True(HudNpcPanel.SkillMasterBox(layout).Contains(HudNpcPanel.TrainingButton(layout))
            && HudNpcPanel.SkillMasterBox(layout).Contains(HudNpcPanel.ExitButton(layout)), "skill master buttons escaped ReStartMain");
        return Task.CompletedTask;
    }

    private static void AssertDisjointInside(Rectangle[] rectangles, Rectangle container, string name)
    {
        for (var index = 0; index < rectangles.Length; index++)
        {
            Assert.True(container.Contains(rectangles[index]) && rectangles[index].Width > 0 && rectangles[index].Height > 0, $"{name} escaped");
            for (var other = index + 1; other < rectangles.Length; other++)
            {
                Assert.True(!rectangles[index].Intersects(rectangles[other]), $"{name} overlap");
            }
        }
    }

    private static Task CountBoxAsync()
    {
        var box = new HudCountBox();
        var accepted = -1;
        box.Open("How many items", "do you wanna buy?", 25, count => accepted = count);
        box.Type("100", static _ => false);
        Assert.True(box.Count == 25, $"a number above the maximum did not show the maximum while typing: {box.Count}");
        box.Type([], static key => key == Keys.Enter);
        Assert.True(accepted == 25 && !box.IsOpen, $"the maximum shown was not accepted: {accepted}");

        box.Open("How many items", "do you wanna sell?", 25, count => accepted = count);
        box.Type("7", static _ => false);
        box.Click(HudCountBox.Up.Center);
        box.Click(HudCountBox.Ok.Center);
        Assert.True(accepted == 8, "the arrow and OK did not give the number shown");
        return Task.CompletedTask;
    }

    private static Task WeaponPairKeyAsync()
    {
        var layout = HudLayout.Create(800, 600);
        var state = OwnerState.Capture(new CharacterClassRegistry().CreatePlayer(PlayerClass.Fighter, "Pair")) with { InventoryRevision = 3 };
        InventoryCommand? sent = null;
        var controller = new HudInventoryController(null!, command =>
        {
            sent = command;
            return Task.FromResult(new InventoryOutcome(true, 4, ""));
        });
        controller.Update(state, layout, false, default, default);
        controller.PickupLocked = true;
        controller.ToggleWeaponPair();
        controller.Update(state, layout, false, default, default);
        Assert.True(sent is null, "the pair changed during a swing");
        controller.ActionStarts++;
        controller.Update(state, layout, false, default, default);
        Assert.True(sent is { Action: InventoryAction.SwitchWeaponSet, Destination: 1, ExpectedRevision: 3 },
            $"the next swing of a fight did not end the wait for the other pair: {sent}");

        sent = null;
        var swapped = state with { WeaponSet = 1, InventoryRevision = 4 };
        controller.Update(swapped, layout, false, default, default);
        controller.ToggleWeaponPair();
        controller.Update(swapped, layout, false, default, default);
        Assert.True(sent is null, "the pair changed during the swing it was asked in");
        controller.PickupLocked = false;
        controller.Update(swapped, layout, false, default, default);
        Assert.True(sent is { Action: InventoryAction.SwitchWeaponSet, Destination: 0 }, $"the pair did not change once the player stopped: {sent}");
        return Task.CompletedTask;
    }

    private static Task CarriedOverAsync()
    {
        var layout = HudLayout.Create(800, 600);
        var carried = new OwnedItemView(Guid.NewGuid(), 1, "Carried", ItemKind.Axe, 1, 0, -1, 1, 1);
        var other = new OwnedItemView(Guid.NewGuid(), 2, "Other", ItemKind.Axe, 1, 4, -1, 1, 1);
        var state = OwnerState.Capture(new CharacterClassRegistry().CreatePlayer(PlayerClass.Fighter, "Carry")) with { Items = [carried, other], InventoryRevision = 1 };
        var controller = new HudInventoryController(null!, _ => Task.FromResult(new InventoryOutcome(true, 2, "")));
        var origin = HudInventoryLayout.Origin(layout);
        var start = HudInventoryLayout.Bounds(origin, carried).Center;
        var over = HudInventoryLayout.Bounds(origin, other).Center;
        controller.Update(state, layout, true, default, MouseAt(start, layout.Scale, ButtonState.Pressed));
        Assert.True(controller.Dragging == carried.InstanceId, "bag item was not picked up");
        controller.Update(state, layout, true, default, MouseAt(over, layout.Scale, ButtonState.Released));
        Assert.True(controller.Hovered is null, "an item under a carried one took the hover, which picks up on a click");
        Assert.True(HudInventoryPanel.CarriedOver(origin, state, controller, carried, over) == other, "the item under a carried one was not described");
        Assert.True(HudInventoryPanel.CarriedOver(origin, state, controller, carried, start) is null, "the carried item's own cells described it");
        return Task.CompletedTask;
    }

    private static Task EquipmentDragAsync()
    {
        foreach (var size in new[] { new Point(800, 600), new Point(1600, 1200), new Point(1920, 1080) })
        {
            var layout = HudLayout.Create(size.X, size.Y);
            var character = new CharacterClassRegistry().CreatePlayer(PlayerClass.Mechanician, "Inventory");
            var state = OwnerState.Capture(character);
            Assert.True(state.AttackRating > 0 && state.MoveSpeed > 0, "owner projection omitted derived combat statistics");
            Assert.True(ReferenceEquals(state, OwnerState.Capture(character, state)), "unchanged derived values invalidated owner cache");
            character.Effects.Apply(new EffectSpec(0x0A00_0003, EffectSourceKind.Item, false, [StatModifier.Flat(StatKind.DamageMax, 10)]), 0, 60_000f);
            var buffed = OwnerState.Capture(character, state);
            Assert.True(!ReferenceEquals(state, buffed) && buffed.DamageMax == state.DamageMax + 10, "a buff that changes only the damage left the owner state stale");
            var item = new OwnedItemView(Guid.NewGuid(), 1, "Weapon", ItemKind.Axe, 1, 0, -1, 2, 3);
            state = state with { Items = new[] { item }, InventoryRevision = 7 };
            InventoryCommand? sent = null;
            var controller = new HudInventoryController(null!, command =>
            {
                sent = command;
                return Task.FromResult(new InventoryOutcome(true, 8, ""));
            });
            var origin = HudInventoryLayout.Origin(layout);
            var pointer = HudInventoryLayout.Bag(origin).Location + new Point(10, 10);
            controller.Update(state, layout, true, default, MouseAt(pointer, layout.Scale, ButtonState.Pressed));
            Assert.True(controller.Dragging == item.InstanceId, "bag item was not picked up");
            var weapon = HudInventoryLayout.Equipment(origin, (int)EquipmentSlot.Weapon).Center;
            controller.Update(state, layout, true, default, MouseAt(weapon, layout.Scale, ButtonState.Released));
            Assert.True(sent is null && controller.Dragging == item.InstanceId, "releasing the button set the carried item down");
            controller.Update(state, layout, true, default, MouseAt(weapon, layout.Scale, ButtonState.Pressed));
            Assert.True(sent is { Action: InventoryAction.Equip, Slot: EquipmentSlot.Weapon, ExpectedRevision: 7 }
                && sent.ItemId == item.InstanceId && sent.OperationId != Guid.Empty, "a click on the weapon slot did not send the authoritative equipment command");
            Assert.True(state.Items[0].Slot == -1 && controller.Dragging == Guid.Empty, "gesture changed immutable owner state or retained drag");

            // An item a reply hands to the cursor waits for the owner state of that reply's revision, which travels apart.
            var handed = item with { InstanceId = Guid.NewGuid(), Anchor = 30 };
            controller.CarryAfter(handed.InstanceId, 9);
            controller.Update(state with { InventoryRevision = 8 }, layout, true, default, MouseAt(weapon, layout.Scale, ButtonState.Released));
            Assert.True(controller.Dragging == Guid.Empty, "an item was carried before the state that holds it arrived");
            controller.Update(state with { InventoryRevision = 9, Items = [item, handed] }, layout, true, default, MouseAt(weapon, layout.Scale, ButtonState.Released));
            Assert.True(controller.Dragging == handed.InstanceId, "the item the reply handed over was not carried once its state arrived");

            var asked = 0;
            controller.GoldRequested = () => asked++;
            var coin = HudInventoryLayout.GoldButton(origin).Center;
            controller.Update(state, layout, true, default, MouseAt(coin, layout.Scale, ButtonState.Pressed));
            controller.CancelDrag();
            controller.Update(state, layout, true, default, MouseAt(coin, layout.Scale, ButtonState.Released));
            controller.Update(state, layout, true, default, MouseAt(coin, layout.Scale, ButtonState.Pressed));
            Assert.True(asked == 1, "the gold coin asked for gold while an item was carried, or not once nothing was");

            // The special item box: its button asks the HUD, a page button closes it, and a box item set down goes back.
            var kept = new OwnedItemView(Guid.NewGuid(), 2, "Letter", ItemKind.Quest, 1, -1, QuestItemBox.FirstOwnerSlot, 2, 2, "QT101");
            var boxed = state with { Items = [item, kept], InventoryRevision = state.InventoryRevision + 1 };
            var requested = 0;
            controller.QuestBoxRequested = () =>
            {
                requested++;
                controller.ToggleQuestBox();
            };
            var questButton = HudInventoryLayout.QuestBoxButton(origin).Center;
            controller.Update(boxed, layout, true, default, MouseAt(questButton, layout.Scale, ButtonState.Released));
            controller.Update(boxed, layout, true, default, MouseAt(questButton, layout.Scale, ButtonState.Pressed));
            var keptAt = HudInventoryLayout.QuestBoxCell(origin, 0).Center;
            Assert.True(requested == 1 && controller.QuestBoxShown && HudInventoryLayout.ItemAt(origin, boxed.Items, keptAt, true) == kept
                && HudInventoryLayout.ItemAt(origin, boxed.Items, HudInventoryLayout.Cell(origin, 0).Center, true) != item,
                "the box did not open over the bag, or the bag's item still showed");
            sent = null;
            controller.Update(boxed, layout, true, default, MouseAt(keptAt, layout.Scale, ButtonState.Released));
            controller.Update(boxed, layout, true, default, MouseAt(keptAt, layout.Scale, ButtonState.Pressed));
            controller.Update(boxed, layout, true, default, MouseAt(keptAt, layout.Scale, ButtonState.Released));
            Assert.True(controller.Dragging == kept.InstanceId, "a box item was not picked up");
            controller.Update(boxed, layout, true, default, MouseAt(weapon, layout.Scale, ButtonState.Pressed));
            Assert.True(controller.Dragging == Guid.Empty && sent is null, "a box item set down did not go back to its box, or sent a command");
            controller.ShowBagPage(1);
            Assert.True(!controller.QuestBoxShown && sent is null, "a page button switched the page instead of closing the box");
        }

        return Task.CompletedTask;
    }

    private static Task TwoHandedShadowAsync()
    {
        var origin = HudInventoryLayout.Origin(HudLayout.Create(800, 600));
        var offHand = HudInventoryLayout.Equipment(origin, (int)EquipmentSlot.OffHand).Center;
        OwnedItemView Worn(string key, EquipmentSlot slot) => new(Guid.NewGuid(), 1, key, ItemKind.Axe, 1, -1, (int)slot, 2, 3, key);
        var twoHanded = Worn("WA105", EquipmentSlot.Weapon);
        Assert.True(HudInventoryLayout.TwoHandedShadow([twoHanded]) == twoHanded && HudInventoryLayout.ItemAt(origin, [twoHanded], offHand, false) == twoHanded,
            "a worn two-handed weapon does not fill the empty off-hand slot");
        Assert.True(HudInventoryLayout.TwoHandedShadow([twoHanded, Worn("DS101", EquipmentSlot.OffHand)]) is null,
            "a two-handed weapon was shown over an off-hand item");
        var shield = new OwnedItemView(Guid.NewGuid(), 2, "DS101", ItemKind.Shield, 1, 0, -1, 2, 3, "DS101");
        Assert.True(HudInventoryPlacement.Of(origin, [twoHanded, shield], shield, offHand, true) is { Kind: HudPlacementKind.Exchange, Slot: EquipmentSlot.OffHand } swap
            && swap.Covered == twoHanded, "a shield over the two-handed weapon's off-hand slot does not pick the weapon up");
        var oneHanded = Worn("WA101", EquipmentSlot.Weapon);
        Assert.True(HudInventoryLayout.TwoHandedShadow([oneHanded]) is null && HudInventoryLayout.ItemAt(origin, [oneHanded], offHand, false) is null,
            "a one-handed weapon filled the off-hand slot");
        return Task.CompletedTask;
    }

    private static Task PlacementAsync()
    {
        static OwnedItemView Item(ItemKind kind, int anchor, int width = 1, int height = 1, string key = "", int slot = -1) =>
            new(Guid.NewGuid(), 1, kind.ToString(), kind, 1, anchor, slot, width, height, key);
        var origin = HudInventoryLayout.Origin(HudLayout.Create(800, 600));
        var bag = HudInventoryLayout.Bag(origin);
        Point CellCentre(int anchor) => new(bag.X + anchor % 12 * 22 + 11, bag.Y + anchor / 12 * 22 + 11);

        var stone = Item(ItemKind.Material, 0);
        var other = Item(ItemKind.Material, 5);
        var potion = Item(ItemKind.Potion, 30, key: "PL101");
        var potions = Item(ItemKind.Potion, 31, key: "PL101");
        var upper = Item(ItemKind.Material, 7);
        var lower = Item(ItemKind.Material, 19);
        var worn = Item(ItemKind.Sword, -1, 1, 3, slot: (int)EquipmentSlot.Weapon);
        OwnedItemView[] items = [stone, other, potion, potions, upper, lower, worn];

        var free = HudInventoryPlacement.Of(origin, items, stone, CellCentre(3), true);
        Assert.True(free.Kind == HudPlacementKind.Fits && free.Anchor == 3, $"a free cell gave {free}");
        var exchange = HudInventoryPlacement.Of(origin, items, stone, CellCentre(5), true);
        Assert.True(exchange.Kind == HudPlacementKind.Exchange && exchange.Covered == other && exchange.Anchor == 5, $"one covered item gave {exchange}");
        var join = HudInventoryPlacement.Of(origin, items, potion, CellCentre(31), true);
        Assert.True(join.Kind == HudPlacementKind.Merge && join.Covered == potions, $"a potion over its kind gave {join}");
        var tall = Item(ItemKind.Material, 40, 1, 2);
        var two = HudInventoryPlacement.Of(origin, [.. items, tall], tall, new Point(bag.X + 7 * 22 + 11, bag.Y + 22), true);
        Assert.True(two.Kind == HudPlacementKind.Blocked, $"two covered items gave {two}");

        var weaponSlot = HudInventoryLayout.Equipment(origin, (int)EquipmentSlot.Weapon).Center;
        var sword = Item(ItemKind.Sword, 50, 1, 3);
        var wield = HudInventoryPlacement.Of(origin, items, sword, weaponSlot, true);
        Assert.True(wield.Kind == HudPlacementKind.Exchange && wield.Slot == EquipmentSlot.Weapon && wield.Covered == worn, $"a sword over the weapon slot gave {wield}");
        Assert.True(HudInventoryPlacement.Of(origin, items, sword, weaponSlot, false).Kind == HudPlacementKind.Blocked, "an unusable sword was accepted");
        Assert.True(HudInventoryPlacement.Of(origin, items, Item(ItemKind.Armor, 60, 2, 3), weaponSlot, true).Kind == HudPlacementKind.Blocked,
            "an armour was accepted in the weapon slot");
        var box = HudInventoryLayout.Belt(origin, 1).Center;
        var belt = HudInventoryPlacement.Of(origin, items, potion, box, true);
        Assert.True(belt.Kind == HudPlacementKind.Fits && belt.Belt == 1, $"a potion over an empty belt box gave {belt}");
        Assert.True(HudInventoryPlacement.Of(origin, items, stone, box, true).Kind == HudPlacementKind.Blocked, "a stone was accepted in a belt box");
        Assert.True(HudInventoryPlacement.Of(origin, items, stone, new Point(400, origin.Y + 100), true).Kind == HudPlacementKind.Outside,
            "an item above the window does not leave the bag");
        Assert.True(HudInventoryPlacement.Of(origin, items, stone, new Point(200, origin.Y + 585), true).Kind == HudPlacementKind.None,
            "an item over the window's frame has a place");

        // The warehouse grid: what it keeps out is red where it would land, a potion is placed (and refused on the click),
        // and a stored item over the bag goes only to bag cells.
        var store = HudStoreLayout.Origin(0);
        var grid = HudStoreLayout.Grid(store);
        var kept = Item(ItemKind.Axe, 2, 2, 3, "WA101");
        Point StoreCentre(int anchor) => new(grid.X + anchor % 9 * 22 + 11, grid.Y + anchor / 9 * 22 + 11);
        var bottle = HudWarehouseWindow.Placement(store, [kept], Item(ItemKind.Material, 0, key: "MA101"), StoreCentre(40));
        Assert.True(bottle.Kind == HudPlacementKind.Blocked && bottle.Highlight == HudStoreLayout.Cell(store, 40), $"a crafting material gave {bottle}");
        Assert.True(HudWarehouseWindow.Placement(store, [kept], potion, StoreCentre(40)).Kind == HudPlacementKind.Fits, "a potion was refused before the click");
        var swapped = HudWarehouseWindow.Placement(store, [kept], Item(ItemKind.Sword, 0, 1, 3, "WS101"), StoreCentre(12));
        Assert.True(swapped.Kind == HudPlacementKind.Exchange && swapped.Covered == kept, $"a sword over a stored axe gave {swapped}");
        Assert.True(HudInventoryPlacement.OfStored(origin, items, kept, weaponSlot).Kind == HudPlacementKind.Blocked, "a stored item was worn straight away");
        Assert.True(HudInventoryPlacement.OfStored(origin, items, potion with { InstanceId = Guid.NewGuid() }, CellCentre(31)).Kind == HudPlacementKind.Blocked,
            "a stored potion joined a bag stack");
        Assert.True(HudInventoryPlacement.OfStored(origin, items, kept, new Point(bag.X + 9 * 22 + 22, bag.Y + 3 * 22 + 33)).Kind == HudPlacementKind.Fits,
            "a stored item found no free bag cells");
        return Task.CompletedTask;
    }

    private static Task ResolutionsAsync()
    {
        DisplayModeOption[] modes = [new(0, 1920, 1080, 0), new(0, 800, 600, 0), new(0, 1600, 900, 0), new(0, 1280, 720, 0)];
        var desktop = new DisplayModeOption(0, 1920, 1080, 0);
        var full = HudSettingsPanel.Resolutions(WindowMode.Fullscreen, modes, desktop);
        Assert.True(full.SequenceEqual([new Point(800, 600), new Point(1280, 720), new Point(1600, 900), new Point(1920, 1080)]),
            $"fullscreen offers {string.Join(' ', full)}");
        var windowed = HudSettingsPanel.Resolutions(WindowMode.Windowed, modes, desktop);
        Assert.True(windowed.SequenceEqual([new Point(800, 600), new Point(1280, 720), new Point(1600, 900)]),
            $"a window offers {string.Join(' ', windowed)}, more than fits below its title bar");
        Assert.True(HudSettingsPanel.Resolutions(WindowMode.Borderless, modes, desktop).Count == 0, "borderless offers a size");
        return Task.CompletedTask;
    }

    private static Task CursorKindAsync()
    {
        Assert.True(HudCursor.ForHovered(EntityKind.Monster, 0) == HudCursorKind.Attack, "a wild monster does not show the attack pointer");
        Assert.True(HudCursor.ForHovered(EntityKind.Npc, 0) == HudCursorKind.Talk, "an NPC does not show the talk pointer");
        Assert.True(HudCursor.ForHovered(EntityKind.Player, 0) == HudCursorKind.Default, "a player changes the pointer");
        Assert.True(HudCursor.ForHovered(EntityKind.Monster, 42) == HudCursorKind.Default, "a summon changes the pointer");
        Assert.True(HudCursor.ForHovered(EntityKind.None, 0) == HudCursorKind.Default, "nothing under the pointer changes it");
        Assert.True(HudCursor.ArtPath(HudCursorKind.Attack).EndsWith("Attack_Cursor.tga", StringComparison.Ordinal)
            && HudCursor.ArtPath(HudCursorKind.Talk).EndsWith("Talk_Cursor.tga", StringComparison.Ordinal)
            && HudCursor.ArtPath(HudCursorKind.Default).EndsWith("DefaultCursor.tga", StringComparison.Ordinal), "pointer art differs");
        Assert.True(HudCursor.ForHovered(true, false, EntityKind.Monster, 0) == HudCursorKind.GetItem, "a drop before a monster does not show the open hand");
        Assert.True(HudCursor.ForHovered(true, true, EntityKind.None, 0) == HudCursorKind.GetItemHeld, "a held button over a drop does not close the hand");
        Assert.True(HudCursor.ForHovered(false, true, EntityKind.Npc, 0) == HudCursorKind.Talk, "a held button without a drop changes the pointer");
        Assert.True(HudCursor.ArtPath(HudCursorKind.GetItem).EndsWith("GetItem_Cursor1.tga", StringComparison.Ordinal)
            && HudCursor.ArtPath(HudCursorKind.GetItemHeld).EndsWith("GetItem_Cursor2.tga", StringComparison.Ordinal), "the hand art differs");
        Assert.True(HudCursor.ArtPath(HudCursorKind.Buy).EndsWith("BuyCursor.tga", StringComparison.Ordinal)
            && HudCursor.ArtPath(HudCursorKind.Sell).EndsWith("SellCursor.tga", StringComparison.Ordinal)
            && HudCursor.ArtPath(HudCursorKind.Repair).EndsWith("RepairCursor.tga", StringComparison.Ordinal), "the shop pointer art differs");
        return Task.CompletedTask;
    }

    private static Task DropNameColumnAsync()
    {
        var nearest = new Rectangle(100, 200, 80, 28);
        var second = HudGroundLabels.Stack(nearest, [nearest]);
        var third = HudGroundLabels.Stack(new Rectangle(110, 210, 60, 28), [nearest, second]);
        Assert.True(second == new Rectangle(100, 170, 80, 28) && third == new Rectangle(110, 140, 60, 28), "drop names on one spot did not rise into a column");
        var aside = new Rectangle(181, 200, 50, 28);
        var clear = new Rectangle(100, 170, 80, 28);
        Assert.True(HudGroundLabels.Stack(aside, [nearest]) == aside && HudGroundLabels.Stack(clear, [nearest]) == clear, "a drop name that covers none moved");

        // The stacking order: nearest first, but drops in one row, at nearly one depth, keep the order they had.
        const float slack = GroundLootPresentation.OrderHysteresis;
        var row = new List<float> { 100, 100 + slack, 100 + slack / 2 };
        GroundLootPresentation.SettleOrder(row, static depth => depth);
        Assert.True(row.SequenceEqual([100, 100 + slack, 100 + slack / 2]), $"drop names in one row swapped places: {string.Join(", ", row)}");
        var turned = new List<float> { 100 + slack * 3, 100 + slack, 100 };
        GroundLootPresentation.SettleOrder(turned, static depth => depth);
        GroundLootPresentation.InsertByDepth(turned, 100 + slack * 2, static depth => depth);
        Assert.True(turned.SequenceEqual([100 + slack, 100, 100 + slack * 2, 100 + slack * 3]),
            $"drop names did not come nearest first once the camera turned, or a new one was not placed by depth: {string.Join(", ", turned)}");
        return Task.CompletedTask;
    }

    private static Task CarriedPageButtonAsync()
    {
        var layout = HudLayout.Create(800, 600);
        var origin = HudInventoryLayout.Origin(layout);
        var character = new CharacterClassRegistry().CreatePlayer(PlayerClass.Mechanician, "Pages");
        var id = Guid.NewGuid();
        var stone = new OwnedItemView(id, 1, "Stone", ItemKind.Material, 1, 0, -1, 1, 1);
        var state = OwnerState.Capture(character) with { Items = new[] { stone }, InventoryRevision = 3 };
        var sent = new List<InventoryCommand>();
        var controller = new HudInventoryController(null!, command =>
        {
            sent.Add(command);
            return Task.FromResult(new InventoryOutcome(true, state.InventoryRevision + 1, ""));
        });
        controller.Update(state, layout, true, default, MouseAt(HudInventoryLayout.Cell(origin, 0).Center, layout.Scale, ButtonState.Pressed));
        controller.Update(state, layout, true, default, default);
        Assert.True(controller.Dragging == id, "the stone was not picked up");

        // The page button takes the click, not the bag below it: the stone goes along to page B and stays on the cursor.
        controller.Update(state, layout, true, default, MouseAt(HudInventoryLayout.PageButton(origin, 1).Center, layout.Scale, ButtonState.Pressed));
        Assert.True(sent is [{ Action: InventoryAction.SwitchBagPage, Destination: 1 } switched] && switched.ItemId == id && controller.Dragging == id,
            "the page button with a carried stone did not switch the page taking the stone, or dropped it");
        return Task.CompletedTask;
    }

    private static async Task SettleAsync()
    {
        var layout = HudLayout.Create(800, 600);
        var origin = HudInventoryLayout.Origin(layout);
        var character = new CharacterClassRegistry().CreatePlayer(PlayerClass.Mechanician, "Settle");
        var id = Guid.NewGuid();
        var stone = new OwnedItemView(id, 1, "Stone", ItemKind.Material, 1, 0, -1, 1, 1);
        var state = OwnerState.Capture(character) with { Items = new[] { stone }, InventoryRevision = 3 };
        var reply = new TaskCompletionSource<InventoryOutcome>();
        var controller = new HudInventoryController(null!, _ => reply.Task);
        controller.Update(state, layout, true, default, MouseAt(HudInventoryLayout.Cell(origin, 0).Center, layout.Scale, ButtonState.Pressed));
        controller.Update(state, layout, true, default, default);
        var target = HudInventoryLayout.Cell(origin, 5).Center;
        controller.Update(state, layout, true, default, MouseAt(target, layout.Scale, ButtonState.Pressed));

        // Set down, the stone stays where it was set down, not in its old cell, until the owner state shows it moved.
        var away = new Point(target.X + 100, target.Y);
        Assert.True(controller.Dragging == Guid.Empty && controller.Held == id && controller.HeldAt(away) == target, "the set-down stone left the click point");
        reply.SetResult(new InventoryOutcome(true, 4, ""));
        await Task.Yield();
        controller.Update(state, layout, true, default, default);
        Assert.True(controller.Held == id, "the stone went back to its old cell before the owner state showed the move");

        // A later command refused before that owner state does not put the stone back in its old cell either.
        reply = new TaskCompletionSource<InventoryOutcome>();
        controller.Update(state, layout, true, default, MouseAt(HudInventoryLayout.PageButton(origin, 1).Center, layout.Scale, ButtonState.Pressed));
        reply.SetResult(new InventoryOutcome(false, 3, "refused"));
        await Task.Yield();
        controller.Update(state, layout, true, default, default);
        Assert.True(controller.Completions == 2 && controller.Held == id, "a later refused command put the set-down stone back in its old cell");
        controller.Update(state with { Items = new[] { stone with { Anchor = 5 } }, InventoryRevision = 4 }, layout, true, default, default);
        Assert.True(controller.Held == Guid.Empty, "the stone stayed held once the owner state showed it moved");
    }

    private static async Task ArrivalSoundAsync()
    {
        var layout = HudLayout.Create(800, 600);
        var origin = HudInventoryLayout.Origin(layout);
        var character = new CharacterClassRegistry().CreatePlayer(PlayerClass.Mechanician, "Listener");
        var stone = new OwnedItemView(Guid.NewGuid(), 1, "Stone", ItemKind.Material, 1, 0, -1, 1, 1, "MA101");
        var gift = new OwnedItemView(Guid.NewGuid(), 2, "Suit", ItemKind.Armor, 1, 2, -1, 2, 3, "DA102");
        var state = OwnerState.Capture(character) with { Items = new[] { stone }, InventoryRevision = 3 };
        var reply = new TaskCompletionSource<InventoryOutcome>();
        var sounds = new List<string>();
        var controller = new HudInventoryController(null!, _ => reply.Task) { ItemSound = sounds.Add };
        controller.Update(state, layout, true, default, default);
        Assert.True(sounds.Count == 0, "the first owner state sounded its items");

        state = state with { Items = new[] { stone, gift }, InventoryRevision = 4 };
        controller.Update(state, layout, true, default, default);
        Assert.True(sounds is ["DA102"], $"the suit the server laid in the bag sounded {string.Join(", ", sounds)}");

        var otherPage = state with { Items = new[] { stone with { InstanceId = Guid.NewGuid() } }, InventoryRevision = 5, BagPage = 1 };
        controller.Update(otherPage, layout, true, default, default);
        controller.Update(state with { InventoryRevision = 6 }, layout, true, default, default);
        Assert.True(sounds.Count == 1, "switching the bag page sounded the other page's items");

        sounds.Clear();
        controller.Update(state with { InventoryRevision = 6 }, layout, true, default, MouseAt(HudInventoryLayout.Cell(origin, 0).Center, layout.Scale, ButtonState.Pressed));
        controller.Update(state with { InventoryRevision = 6 }, layout, true, default, default);
        controller.Update(state with { InventoryRevision = 6 }, layout, true, default, MouseAt(HudInventoryLayout.Cell(origin, 5).Center, layout.Scale, ButtonState.Pressed));
        reply.SetResult(new InventoryOutcome(true, 7, ""));
        await Task.Yield();
        controller.Update(state with { InventoryRevision = 6 }, layout, true, default, default);
        Assert.True(sounds is ["MA101", "MA101"], $"lifting and moving the stone sounded {string.Join(", ", sounds)}");
    }

    private static async Task ThrownAsync()
    {
        var layout = HudLayout.Create(800, 600);
        var origin = HudInventoryLayout.Origin(layout);
        var character = new CharacterClassRegistry().CreatePlayer(PlayerClass.Mechanician, "Thrower");
        var id = Guid.NewGuid();
        var stone = new OwnedItemView(id, 1, "Stone", ItemKind.Material, 1, 0, -1, 1, 1);
        var state = OwnerState.Capture(character) with { Items = new[] { stone }, InventoryRevision = 3 };
        var reply = new TaskCompletionSource<InventoryOutcome>();
        HudInventoryController controller = null!;
        controller = new HudInventoryController(null!, _ => reply.Task)
        {
            ThrowRequested = item =>
            {
                controller.Drop(item, item.Count);
                return true;
            }
        };
        var above = new Point(400, origin.Y + 100);
        void Throw()
        {
            controller.Update(state, layout, true, default, MouseAt(HudInventoryLayout.Cell(origin, 0).Center, layout.Scale, ButtonState.Pressed));
            controller.Update(state, layout, true, default, default);
            controller.Update(state, layout, true, default, MouseAt(above, layout.Scale, ButtonState.Pressed));
            controller.Update(state, layout, true, default, default);
        }

        // Thrown above the window, the stone shows nowhere, not back in a bag cell, until the owner state shows it gone.
        Throw();
        Assert.True(controller.Dragging == Guid.Empty && controller.Held == Guid.Empty && controller.Thrown == id, "the thrown stone showed back in the bag");
        reply.SetResult(new InventoryOutcome(true, 4, ""));
        await Task.Yield();
        controller.Update(state, layout, true, default, default);
        Assert.True(controller.Thrown == id, "the thrown stone showed in the bag before the owner state showed it gone");
        controller.Update(state with { Items = [], InventoryRevision = 4 }, layout, true, default, default);
        Assert.True(controller.Thrown == Guid.Empty, "the throw stayed pending once the owner state showed the stone gone");

        // A refused throw shows the stone in its place again.
        reply = new TaskCompletionSource<InventoryOutcome>();
        Throw();
        reply.SetResult(new InventoryOutcome(false, 3, "refused"));
        await Task.Yield();
        controller.Update(state, layout, true, default, default);
        Assert.True(controller.Thrown == Guid.Empty, "a refused throw kept the stone hidden");
    }

    private static Task PickupToCursorAsync()
    {
        var layout = HudLayout.Create(800, 600);
        var character = new CharacterClassRegistry().CreatePlayer(PlayerClass.Mechanician, "Picker");
        var stone = new OwnedItemView(Guid.NewGuid(), 1, "Stone", ItemKind.Material, 1, 0, -1, 1, 1);
        var state = OwnerState.Capture(character) with { Items = new[] { stone }, InventoryRevision = 3 };
        var sent = new List<InventoryCommand>();
        var controller = new HudInventoryController(null!, command =>
        {
            sent.Add(command);
            return Task.FromResult(new InventoryOutcome(true, 4, ""));
        });
        controller.Update(state, layout, false, default, default);
        Assert.True(!controller.ExpectPickup(2), "a pickup came to the cursor with the window closed");

        controller.Update(state, layout, true, default, default);
        Assert.True(controller.ExpectPickup(2), "a pickup with the window open did not come to the cursor");
        // The owner state may come before the reply: the new gem shows nowhere meanwhile, the stone stays.
        var gem = new OwnedItemView(Guid.NewGuid(), 2, "Gem", ItemKind.Material, 1, 1, -1, 1, 1);
        var arrived = state with { Items = new[] { stone, gem }, InventoryRevision = 4 };
        controller.Update(arrived, layout, true, default, default);
        Assert.True(controller.Hides(gem) && !controller.Hides(stone) && controller.Dragging == Guid.Empty, "the picked up gem showed in the bag before the reply");
        controller.PickupAnswered(true, 4);
        controller.Update(arrived, layout, true, default, default);
        Assert.True(controller.Dragging == gem.InstanceId, "the picked up gem did not come to the cursor");
        Assert.True(!controller.ExpectPickup(2), "a pickup came to the cursor over a carried item");

        // The window closed (V), the gem stays on the cursor, as the original keeps the mouse item, and nothing is sent; Esc
        // (CancelDrag) lets go of it where it lies.
        controller.Update(arrived, layout, false, default, default);
        Assert.True(controller.Dragging == gem.InstanceId && sent.Count == 0, "closing the window took the gem off the cursor, or moved it");
        controller.CancelDrag();
        Assert.True(controller.Dragging == Guid.Empty && sent.Count == 0, "Esc did not let go of the gem, or moved it");
        return Task.CompletedTask;
    }

    private static MouseState MouseAt(Point logical, int scale, ButtonState left) => new(logical.X * scale, logical.Y * scale, 0,
        left, ButtonState.Released, ButtonState.Released, ButtonState.Released, ButtonState.Released);

    private static Task GaugeAsync()
    {
        Assert.True(HudBar.Length(HudBar.LifeLength, 50, 100) == 47 && HudBar.Length(HudBar.LifeLength, 100, 100) == 94, "life length differs");
        Assert.True(HudBar.Length(HudBar.StaminaLength, 1, 0) == 0 && HudBar.Length(HudBar.ManaLength, -5, 10) == 0, "empty maximum filled a gauge");
        Assert.True(HudBar.Length(HudBar.ManaLength, 500, 100) == 94, "overfilled gauge escaped its bitmap");
        var half = HudBar.GaugeSource(16, HudBar.LifeLength, 47);
        Assert.True(half == new Rectangle(0, 47, 16, 47) && half.Bottom == HudBar.LifeLength, "gauge is not cropped from the top");
        Assert.True(HudBar.GaugeSource(8, HudBar.StaminaLength, 0).Height == 0, "empty gauge drew rows");
        Assert.True(HudBar.TimeGauge(4) == (true, 0) && HudBar.TimeGauge(13.5f) == (true, 23) && HudBar.TimeGauge(22.9f) == (true, 47),
            "day gauge differs from GetWordTimeDisplay");
        Assert.True(HudBar.TimeGauge(23) == (false, 0) && HudBar.TimeGauge(1) == (false, 20) && HudBar.TimeGauge(3) == (false, 40),
            "night gauge differs");
        Assert.True(HudBar.TimeGauge(float.NaN).Day == false && HudBar.TimeGauge(-1).Day == false, "invalid hour was not normalized");
        var owner = OwnerState.Capture(new CharacterClassRegistry().CreatePlayer(PlayerClass.Mechanician, "Exp"));
        Assert.True(HudBar.ExperienceFraction(owner with { Experience = owner.LevelStart }) == 0, "level start shows experience");
        Assert.True(HudBar.ExperienceFraction(owner with { Experience = owner.NextLevel + 10 }) == 1, "experience escaped its bar");
        Assert.True(HudCombat.StateColor(0, 10) == new Color(255, 0, 0) && HudCombat.StateColor(10, 10) == new Color(1, 255, 0),
            "target bar color differs from DrawStateBar");
        return Task.CompletedTask;
    }

    private static Task SlideAsync()
    {
        var slide = new HudSlide { Open = true };
        slide.Advance(1.0 / 70);
        Assert.True(slide.Move == 184 && !slide.FullyOpen && slide.Visible, "opening did not jump to 184");
        slide.Advance(1.0 / 70);
        Assert.True(slide.Move == 191, "opening step differs");
        slide.Advance(1);
        Assert.True(slide.FullyOpen && slide.Offset == 0, "window did not reach its open position");
        slide.Open = false;
        slide.Advance(1.0 / 70);
        Assert.True(slide.Move == 240 && !slide.FullyOpen, "closing step differs");
        slide.Advance(1);
        Assert.True(!slide.Visible && slide.Offset == HudSlide.Full, "window did not close");
        return Task.CompletedTask;
    }

    private static Task MinimapAsync()
    {
        // A 10240x10240 world-unit stage in fixed point; the player stands in the middle.
        var rectangle = new PristonIntRectangle(-5120 * 256, -5120 * 256, 5120 * 256, 5120 * 256);
        Assert.True(HudMinimap.MapRegion(rectangle, Vector3.Zero, false, out var source, out var destination), "centered map was rejected");
        var span = 2 * HudMinimap.ViewSize(false) / 10240f;
        Assert.True(MathF.Abs(source.Z - span) < 1e-4f && MathF.Abs(source.X - (0.5f - span / 2)) < 1e-4f, "map source span differs");
        Assert.True(destination == new Vector4(0, 0, 126, 126), "unclipped destination is not the 126 pixel interior");
        Assert.True(HudMinimap.MapRegion(rectangle, new Vector3(-5120, 0, 0), false, out source, out destination)
            && source.X == 0 && MathF.Abs(destination.X - 64) < 0.01f && MathF.Abs(destination.Z - 62) < 0.01f, "left edge was not clipped");
        Assert.True(!HudMinimap.MapRegion(rectangle, new Vector3(20000, 0, 0), false, out _, out _), "map outside the stage was drawn");
        Assert.True(!HudMinimap.MapRegion(default, Vector3.Zero, false, out _, out _), "empty stage rectangle was drawn");
        // Engine Z is the mirrored stage Z: walking towards -Z (north) moves up the map.
        var north = HudMinimap.MapPoint(rectangle, new Vector3(0, 0, -100)).Y;
        Assert.True(north < HudMinimap.MapPoint(rectangle, Vector3.Zero).Y, "map vertical axis is mirrored");
        Assert.True(MathF.Abs(HudMinimap.ArrowRotation(-Vector3.UnitZ) - MathF.PI) < 1e-5f, "north heading does not flip the arrow bitmap");
        // The view radius eases by 8 units per 70 Hz frame toward 16 (dungeon) or 24 cells of 64 units.
        Assert.True(MathF.Abs(HudMinimap.EaseViewSize(512f, HudMinimap.ViewSize(false), 1d / 70d) - 520f) < 1e-3f
            && HudMinimap.EaseViewSize(1530f, HudMinimap.ViewSize(false), 1d) == 1536f
            && HudMinimap.EaseViewSize(1536f, HudMinimap.ViewSize(true), 1d) == 1024f, "the minimap zoom does not ease to the field size");
        var marker = HudMinimap.MarkerOffset(new Vector3(100f, 0f, 100f), new Vector3(100f + 768f, 0f, 100f - 384f), 1536f);
        Assert.True(marker == new Vector2(32f, -16f), "an NPC marker left its map position (east right, north up)");
        Assert.True(HudMinimap.HiddenOn(30, FieldState.Forest) && HudMinimap.HiddenOn(32, FieldState.Forest) && HudMinimap.HiddenOn(36, FieldState.Dungeon)
            && HudMinimap.HiddenOn(1, FieldState.QuestArena) && !HudMinimap.HiddenOn(3, FieldState.Village), "the minimap shows in the wrong fields");
        return Task.CompletedTask;
    }

    private static Task OverlayAsync()
    {
        var empty = default(HudLayout).PlaceOverlay(new Point(-170, -47), 340, 37);
        Assert.True(empty == new Rectangle(0, 0, 1, 1), "empty viewport generated invalid clamp bounds");
        foreach (var (width, height) in new[] { (0, 0), (1, 1), (32, 24), (160, 90), (800, 600), (1280, 720), (3840, 2160) })
        {
            var layout = HudLayout.Create(width, height);
            Assert.True(layout.Scale >= 1 && layout.Width >= 1 && layout.Height >= 1, "viewport generated an invalid UI transform");
            var viewport = new Rectangle(0, 0, layout.Width, layout.Height);
            foreach (var point in new[] { new Point(-1000, -1000), new Point(100000, 100000) })
            {
                var overlay = layout.PlaceOverlay(point, 340, 1000);
                Assert.True(viewport.Contains(overlay) && overlay.Width > 0 && overlay.Height > 0, "overlay escaped the resized viewport");
            }

            Assert.True(layout.ToLogical(new Point(-1, -1)) == new Point(-1, -1), "negative pointer rounded into the viewport");
        }

        return Task.CompletedTask;
    }

    private static Task ShortcutsAsync()
    {
        var bindings = new HudSkillBindings();
        var basic = HudSkillBindings.BasicAttackId;
        Assert.True(bindings.LeftSkillId == basic && bindings.RightSkillId == basic, "sockets did not start with the normal attack");
        var rightDown = Mouse(right: ButtonState.Pressed);
        bindings.Update(default, rightDown, basic);
        bindings.Update(new KeyboardState(Keys.F5), rightDown, basic);
        Assert.True(bindings.Shortcut(4) == new HudSkillShortcut(basic, SkillMouseUse.Right), "F5 did not bind the held right button");
        bindings.Update(default, default, basic);
        Assert.True(bindings.RightSkillId == basic, "release after binding changed the right socket");
        bindings.Update(new KeyboardState(Keys.F5), default, 0);
        Assert.True(bindings.RightSkillId == basic && bindings.LeftSkillId == basic, "hotkey did not select the recorded side");

        Assert.True(bindings.Assign(0, basic, SkillMouseUse.Left), "valid reassignment rejected");
        Assert.True(bindings.Shortcut(4).SkillId == 0, "old shortcut remained after rebinding the same skill");
        Assert.True(!bindings.Assign(8, basic, SkillMouseUse.Left), "F9 outside original shortcut range accepted");
        Assert.True(!bindings.Select(42, SkillMouseUse.Right), "unknown skill selected");
        Assert.True(!bindings.Select(basic, SkillMouseUse.Both), "ambiguous mouse side accepted");

        var direct = new HudSkillBindings();
        direct.Update(default, rightDown, basic);
        direct.Update(default, default, basic);
        Assert.True(direct.RightSkillId == basic, "right release on a skill did not select it");
        var draggedAway = new HudSkillBindings();
        draggedAway.Update(default, rightDown, basic);
        draggedAway.Update(default, default, 0);
        Assert.True(draggedAway.RightSkillId == basic, "release outside the skill row changed the right socket");

        var debug = new HudSkillBindings();
        debug.Update(default, rightDown, basic);
        debug.Update(new KeyboardState(Keys.LeftControl, Keys.F5), rightDown, basic);
        Assert.True(debug.Shortcut(4).SkillId == 0, "debug chord overwrote a skill shortcut");
        return Task.CompletedTask;
    }

    private static Task NamePlatesAsync()
    {
        Assert.True(HudCombat.ShowsPlateBar(true, hovered: true, traced: false, attacked: false), "classic bar missing under the cursor");
        Assert.True(!HudCombat.ShowsPlateBar(true, hovered: false, traced: true, attacked: true), "classic bar shown without the cursor");
        Assert.True(!HudCombat.ShowsPlateBar(false, hovered: true, traced: false, attacked: false), "compact bar shown for the cursor alone");
        Assert.True(HudCombat.ShowsPlateBar(false, hovered: false, traced: true, attacked: false), "compact bar missing on the clicked monster");
        Assert.True(HudCombat.ShowsPlateBar(false, hovered: true, traced: false, attacked: true), "compact bar missing on an attacked monster");
        Assert.True(GameScene.OutlineStrength(300f) == 1f && GameScene.OutlineStrength(800f) == 0.5f && GameScene.OutlineStrength(1300f) == 0f,
            "the outline does not fade from 300 to 1300 units");
        return Task.CompletedTask;
    }

    private static Task ColorKeysAsync()
    {
        var colors = new[] { Color.Magenta, Color.Black, new Color(240, 20, 25), new Color(180, 175, 170), new Color(240, 5, 235) };
        var image = new DecodedPristonImage(colors.Length, 1, colors, false);
        PristonUiPixels.Prepare(image, PristonUiColorKey.Magenta);
        Assert.True(colors[0] == Color.Transparent && colors[1] == Color.Black, "matte removed the dark interior or remained opaque");
        Assert.True(colors[2] == new Color(240, 20, 25) && colors[3] == new Color(180, 175, 170), "crimson or silver changed");
        Assert.True(colors[4].R <= colors[4].A && colors[4].B <= colors[4].A, "edge pixels are not premultiplied");

        Assert.True(HudSprites.ColorKeyFor(".BMP") == PristonUiColorKey.Black && HudSprites.ColorKeyFor(".tga") == PristonUiColorKey.None,
            "Sinimage bitmaps are not black keyed or TGA files lost their alpha");
        var bitmap = new DecodedPristonImage(3, 1, [Color.Black, new Color(1, 0, 0), Color.White], false);
        PristonUiPixels.Prepare(bitmap, HudSprites.ColorKeyFor(".bmp"));
        Assert.True(bitmap.Pixels[0] == Color.Transparent && bitmap.Pixels[1] == new Color(1, 0, 0) && bitmap.Pixels[2] == Color.White,
            "black key is not exact RGB 0,0,0");
        var targa = new DecodedPristonImage(2, 1, [new Color(200, 100, 50, 128), new Color(0, 0, 0, 255)], true);
        PristonUiPixels.Prepare(targa, HudSprites.ColorKeyFor(".tga"));
        Assert.True(targa.Pixels[0] == new Color(100, 50, 25, 128) && targa.Pixels[1] == Color.Black, "TGA alpha is not premultiplied or black was keyed");
        return Task.CompletedTask;
    }

    private static Task AtlasGaugeAsync()
    {
        var original = new Color(220, 40, 30);
        var frame = new DecodedPristonImage(24, 24, Enumerable.Repeat(original, 24 * 24).ToArray(), false);
        var bounds = new Rectangle(4, 4, 16, 16);
        var fill = HudGaugePixels.Separate(frame, bounds);
        Assert.True(fill.Pixels[0] == Color.Transparent && fill.Pixels[15] == Color.Transparent
            && fill.Pixels[240] == Color.Transparent && fill.Pixels[255] == Color.Transparent, "rectangular corners remained visible");
        Assert.True(fill.Pixels[8 * 16 + 8] == original, "filled gauge changed the original liquid");
        Assert.True(frame.Pixels[12 * 24 + 12].R < 100, "empty frame still contains the bright liquid");
        for (var y = 0; y < frame.Height; y++)
        {
            for (var x = 0; x < frame.Width; x++)
            {
                if (!bounds.Contains(x, y) || fill.Pixels[(y - bounds.Y) * 16 + x - bounds.X].A == 0)
                {
                    Assert.True(frame.Pixels[y * 24 + x] == original, "mask darkened the surrounding frame");
                }
            }
        }

        var cropped = HudGaugePixels.Crop(frame, bounds);
        Assert.True(cropped.Width == 16 && cropped.Height == 16, "crop retained unused margins");
        for (var y = 0; y < 16; y++)
        {
            for (var x = 0; x < 16; x++)
            {
                Assert.True(cropped.Pixels[y * 16 + x] == frame.Pixels[(y + 4) * 24 + x + 4], "crop changed atlas pixels");
            }
        }

        var transparent = new DecodedPristonImage(4, 4, new Color[16], true);
        var transparentFill = HudGaugePixels.Separate(transparent, new Rectangle(0, 0, 4, 4));
        Assert.True(transparent.Pixels.All(pixel => pixel == Color.Transparent)
            && transparentFill.Pixels.All(pixel => pixel == Color.Transparent), "empty pixels lost premultiplied alpha");

        return Task.CompletedTask;
    }

    private static MouseState Mouse(ButtonState right)
    {
        return new MouseState(0, 0, 0, ButtonState.Released, ButtonState.Released, right, ButtonState.Released, ButtonState.Released);
    }
}
