using System.Diagnostics;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using PristonTale.Client.Net.Presence;
using PristonTale.Engine3D.Diagnostics;
using PristonTale.Engine3D.IO;
using PristonTale.Engine3D.Rendering;
using PristonTale.Gameplay.Effects;
using PristonTale.Gameplay.Screens.Framework;
using PristonTale.Gameplay.UI;
using PristonTale.Gameplay.UI.Inventory;
using PristonTale.Gameplay.UI.Widgets;
using PristonTale.Simulation.Characters;
using PristonTale.Simulation.Items;
using PristonTale.Simulation.Items.Usable;
using PristonTale.Contracts.Skills;
using PristonTale.Contracts.Characters;
using PristonTale.Contracts.Texts;

namespace PristonTale.Tests;

// Captures the special-item HUD: the premium timer column with its grey covers, hover box and last-seconds blink, the
// Teleport Core field list and its confirmation, usable item tooltips, item effects in the running-effects bar (castle
// items, boosters and the skill-delay item), and the puzzle board, its finished glow and the collector's question.
internal sealed class ItemHudCaptureProbe : Game
{
    private enum Scene
    {
        Timers,
        TimerHover,
        Blink,
        Teleport,
        TeleportPage3,
        TeleportConfirm,
        OrbTooltip,
        PackageTooltip,
        ItemBuffs,
        MobileShopConfirm,
        CastleBuffs,
        BoosterHover,
        CrystalTooltip,
        GiftTooltip,
        ChocolateTooltip,
        CastleTooltip,
        LuckyBagTooltip,
        PuzzleBoard,
        PuzzleComplete,
        PuzzleExchange,
        // The crystal in the first cell carried over a free cell, over the gift box beside it, and over the weapon slot.
        CarryFree,
        CarryExchange,
        CarrySlot,
        // A stack of five potions carried above the window: the count box asks how many to drop.
        ThrowCount,
        // The gold coin clicked: the count box asks how much gold to drop.
        GoldCount,
        // The pointer over the weapon pair B button: it lights and shows its label.
        PairHint,
        // The special item box opened over the bag with three quest items, the pointer on its button (the close label).
        QuestBox
    }

    private sealed record Scenario(string Name, Scene Scene, int Width = 800, int Height = 600, double WaitMs = 4200);

    private static readonly Scenario[] Scenarios =
    [
        new("premium-timers-800", Scene.Timers),
        new("premium-hover-800", Scene.TimerHover),
        new("premium-blink-800", Scene.Blink, WaitMs: 4600),
        new("premium-timers-1600x1200-scale2", Scene.Timers, 1600, 1200),
        new("teleport-core-800", Scene.Teleport, WaitMs: 300),
        new("teleport-core-page3-800", Scene.TeleportPage3, WaitMs: 300),
        new("teleport-core-confirm-800", Scene.TeleportConfirm, WaitMs: 300),
        new("item-tooltip-orb-800", Scene.OrbTooltip, WaitMs: 300),
        new("item-tooltip-package-800", Scene.PackageTooltip, WaitMs: 300),
        new("item-buffs-800", Scene.ItemBuffs, WaitMs: 300),
        new("mobile-shop-confirm-800", Scene.MobileShopConfirm, WaitMs: 300),
        new("castle-booster-buffs-800", Scene.CastleBuffs, WaitMs: 300),
        new("booster-hover-800", Scene.BoosterHover, WaitMs: 300),
        new("item-tooltip-crystal-800", Scene.CrystalTooltip, WaitMs: 300),
        new("item-tooltip-giftbox-800", Scene.GiftTooltip, WaitMs: 300),
        new("item-tooltip-chocolate-800", Scene.ChocolateTooltip, WaitMs: 300),
        new("item-tooltip-castle-800", Scene.CastleTooltip, WaitMs: 300),
        new("item-tooltip-luckybag-800", Scene.LuckyBagTooltip, WaitMs: 300),
        new("puzzle-board-800", Scene.PuzzleBoard, WaitMs: 300),
        new("puzzle-complete-800", Scene.PuzzleComplete, WaitMs: 1500),
        new("puzzle-exchange-800", Scene.PuzzleExchange, WaitMs: 300),
        new("carry-free-800", Scene.CarryFree, WaitMs: 1200),
        new("carry-exchange-800", Scene.CarryExchange, WaitMs: 1200),
        new("carry-slot-800", Scene.CarrySlot, WaitMs: 1200),
        new("throw-count-800", Scene.ThrowCount, WaitMs: 1200),
        new("gold-count-800", Scene.GoldCount, WaitMs: 1200),
        new("pair-hint-800", Scene.PairHint, WaitMs: 1200),
        new("quest-box-800", Scene.QuestBox, WaitMs: 1200)
    ];

    private readonly string _assetRoot;
    private Dictionary<string, string>? _icons;
    private readonly string _output;
    private readonly PresentationSettings _presentation = new();
    private readonly Stopwatch _clock = new();
    private SpriteBatch _batch = null!;
    private GameplayHud _hud = null!;
    private RenderTarget2D? _target;
    private OwnerState _owner = null!;
    private OwnerState _items = null!;
    private OwnerState _stack = null!;
    private OwnerState _boxed = null!;
    private OwnerState _board = null!;
    private OwnerState _complete = null!;
    private int _scenario;
    private bool _carrying;
    private bool _thrown;
    private int _frames;
    private bool _prepared;
    private double _startedMs;

    public ItemHudCaptureProbe(string assetRoot, string contentRoot, string output)
    {
        _assetRoot = assetRoot;
        _output = output;
        _ = new GraphicsDeviceManager(this)
        {
            PreferredBackBufferWidth = 1280,
            PreferredBackBufferHeight = 720,
            SynchronizeWithVerticalRetrace = false
        };
        Content = new AbsoluteContentManager(Services, contentRoot);
        IsFixedTimeStep = true;
        TargetElapsedTime = TimeSpan.FromSeconds(1d / 60);
    }

    protected override void LoadContent()
    {
        Directory.CreateDirectory(_output);
        _batch = new SpriteBatch(GraphicsDevice);
        var font = Content.Load<SpriteFont>("Fonts/UI");
        var context = new ScreenContext(GraphicsDevice, Content, _batch, font, _assetRoot, null!, ConsoleClientLog.Instance, _ => { }, _presentation);
        _hud = new GameplayHud(context, _ => Task.FromResult(new InventoryOutcome(false, 0, GameText.Of(GameTextId.RequestRefused))));
        _hud.CharacterName = "Panzer";
        _hud.CharacterClass = PlayerClass.Mechanician;
        _hud.UseItem = command => Task.FromResult(ItemUseOutcome.Refused(command.ExpectedRevision, ItemUseStatus.NotUsable));
        var player = new CharacterClassRegistry().CreatePlayer(PlayerClass.Mechanician, "Panzer");
        _owner = OwnerState.Capture(player) with
        {
            Money = 12750,
            Items = new OwnedItemView[]
            {
                Item("FO112", "Enigma Force", 0),
                Item("BI224", "Superior Package(1Day)", 1),
                Item("BI108", "Teleport Core", 2),
                Item("BI123", "Firecracker", 4)
            }
        };
        _items = _owner with
        {
            InventoryRevision = _owner.InventoryRevision + 1,
            Items = new OwnedItemView[]
            {
                Sized("GP117", "Skeleton Crystal", 0), Sized("SP105", "Easter Egg (A)", 2), Sized("CH101", "Black Chocolet", 4),
                Sized("BC106", "defiance Stone", 6), Sized(Puzzles.LuckyBagIcon, Puzzles.LuckyBagName, 24, "PZ101")
            }
        };
        _stack = _items with
        {
            InventoryRevision = _items.InventoryRevision + 10,
            Items = [.. _items.Items, Sized("PL101", "Mini Life Potion", 40) with { Kind = ItemKind.Potion, Count = 5 },
                Sized("PM101", "Mini Mana Potion", -1) with { Kind = ItemKind.Potion, Count = 2, Slot = PotionBelt.FirstOwnerSlot + 1 }]
        };
        _boxed = _stack with
        {
            InventoryRevision = _stack.InventoryRevision + 1,
            Items = [.. _stack.Items, Sized("QT101", "Letter", -1) with { Kind = ItemKind.Quest, Slot = QuestItemBox.FirstOwnerSlot },
                Sized("QT106", "Old Ring", -1) with { Kind = ItemKind.Quest, Slot = QuestItemBox.FirstOwnerSlot + QuestItemBox.Columns },
                Sized("MA201", "Honey", -1) with { Kind = ItemKind.Material, Slot = QuestItemBox.FirstOwnerSlot + 1 }]
        };
        _board = _owner with { InventoryRevision = _owner.InventoryRevision + 2, Items = Puzzle(6).Append(Sized(Puzzles.LuckyBagIcon, Puzzles.LuckyBagName, 0, "PZ107")).ToArray() };
        _complete = _owner with { InventoryRevision = _owner.InventoryRevision + 3, Items = Puzzle(8).ToArray() };
        _clock.Start();
    }

    private static OwnedItemView Item(string key, string name, int anchor) =>
        new(Guid.NewGuid(), ItemCodes.Parse(key), name, ItemKind.Misc, 1, anchor, -1, 1, 1, key);

    // An item with the size of its inventory sprite; a lucky bag takes the size of the piece it hides.
    private OwnedItemView Sized(string key, string name, int anchor, string? sizeKey = null)
    {
        _icons ??= ItemIconFiles.Index(_assetRoot);
        var width = 1;
        var height = 1;
        if (_icons.TryGetValue(sizeKey ?? key, out var path))
        {
            ItemIconFiles.TryFootprint(path, out width, out height);
        }

        return new OwnedItemView(Guid.NewGuid(), ItemCodes.Parse(key), name, ItemKind.Misc, 1, anchor, -1, width, height, key);
    }

    // The first pieces of the first puzzle, each in its board cell.
    private IEnumerable<OwnedItemView> Puzzle(int pieces)
    {
        for (var number = 1; number <= pieces; number++)
        {
            var (column, row) = Puzzles.Cell(number);
            yield return Sized($"PZ10{number}", $"priestess {number}", row * HudInventoryLayout.Columns + column);
        }
    }

    private OwnerState Owner(Scene scene) => scene switch
    {
        Scene.CrystalTooltip or Scene.GiftTooltip or Scene.ChocolateTooltip or Scene.CastleTooltip or Scene.LuckyBagTooltip
            or Scene.CarryFree or Scene.CarryExchange or Scene.CarrySlot => _items,
        Scene.ThrowCount or Scene.GoldCount or Scene.PairHint => _stack,
        Scene.QuestBox => _boxed,
        Scene.PuzzleBoard => _board,
        Scene.PuzzleComplete or Scene.PuzzleExchange => _complete,
        _ => _owner
    };

    protected override void Update(GameTime gameTime)
    {
        _frames++;
        if (_frames > 7200 || _hud.AssetsFailed)
        {
            throw new InvalidOperationException("Item HUD assets did not become ready within the capture timeout.");
        }

        if (_scenario >= Scenarios.Length)
        {
            Exit();
            return;
        }

        var scenario = Scenarios[_scenario];
        var layout = HudLayout.Create(scenario.Width, scenario.Height);
        if (!_prepared)
        {
            _target?.Dispose();
            _target = new RenderTarget2D(GraphicsDevice, scenario.Width, scenario.Height);
            // Escape's order: one call closes one thing (a count box left open first).
            while (_hud.ClosePanel())
            {
            }

            _hud.Statuses = scenario.Scene switch
            {
                Scene.ItemBuffs => ItemBuffs(),
                Scene.CastleBuffs or Scene.BoosterHover => CastleBuffs(),
                _ => null
            };
            _hud.SelfId = 1;
            _hud.SkillClockMs = BuffClockMs;
            _hud.PremiumTimers = scenario.Scene switch
            {
                Scene.Timers or Scene.TimerHover or Scene.Blink => Timers(scenario.Scene, ++_revision),
                Scene.CastleBuffs or Scene.BoosterHover => Boosters(++_revision),
                _ => null
            };
            var owner = Owner(scenario.Scene);
            _hud.Update(owner, default, default);
            var keys = scenario.Scene is Scene.OrbTooltip or Scene.PackageTooltip or Scene.CrystalTooltip or Scene.GiftTooltip or Scene.ChocolateTooltip
                or Scene.CastleTooltip or Scene.LuckyBagTooltip or Scene.PuzzleBoard or Scene.PuzzleComplete
                or Scene.CarryFree or Scene.CarryExchange or Scene.CarrySlot or Scene.ThrowCount or Scene.GoldCount or Scene.PairHint or Scene.QuestBox
                ? new KeyboardState(Keys.I)
                : default;
            _hud.Update(owner, keys, default);
            switch (scenario.Scene)
            {
                case Scene.Teleport:
                case Scene.TeleportPage3:
                case Scene.TeleportConfirm:
                    _hud.ItemUse.Request(_owner.Items[2], _owner);
                    if (scenario.Scene == Scene.TeleportPage3)
                    {
                        Click(layout, HudTeleportCore.NextPage.Center);
                        Click(layout, HudTeleportCore.NextPage.Center);
                    }

                    if (scenario.Scene == Scene.TeleportConfirm)
                    {
                        Click(layout, HudTeleportCore.Row(3).Center);
                    }

                    break;
                case Scene.PuzzleExchange:
                    _hud.Puzzle.Offer(owner, 5, "Puzzle Collector Lamont");
                    break;
                case Scene.MobileShopConfirm:
                    // The mobile shop item has no inventory icon in the assets, so it is used without being drawn.
                    _hud.ItemUse.Request(Item("BI142", "Mobile Shop", 3), _owner);
                    break;
            }

            _startedMs = _clock.Elapsed.TotalMilliseconds;
            _prepared = true;
            _carrying = false;
            _thrown = false;
        }

        // The window takes clicks once it has slid fully open. The HUD reads the pointer against the back buffer's layout
        // in Update (the capture target is bound only to draw).
        if (scenario.Scene is Scene.CarryFree or Scene.CarryExchange or Scene.CarrySlot && !_carrying && _clock.Elapsed.TotalMilliseconds - _startedMs > 700)
        {
            var updateLayout = HudLayout.Create(GraphicsDevice.Viewport.Width, GraphicsDevice.Viewport.Height);
            Click(updateLayout, HudInventoryLayout.Cell(HudInventoryLayout.Origin(updateLayout), 0).Center, _items);
            _carrying = true;
        }

        if (scenario.Scene == Scene.ThrowCount && _clock.Elapsed.TotalMilliseconds - _startedMs > 700)
        {
            var updateLayout = HudLayout.Create(GraphicsDevice.Viewport.Width, GraphicsDevice.Viewport.Height);
            if (!_carrying)
            {
                Click(updateLayout, HudInventoryLayout.Cell(HudInventoryLayout.Origin(updateLayout), 40).Center, _stack);
                _carrying = true;
            }
            else if (!_thrown)
            {
                Click(updateLayout, new Point(400, HudInventoryLayout.Origin(updateLayout).Y + 150), _stack);
                _thrown = true;
            }
        }

        if (scenario.Scene is Scene.GoldCount or Scene.QuestBox && !_carrying && _clock.Elapsed.TotalMilliseconds - _startedMs > 700)
        {
            var updateLayout = HudLayout.Create(GraphicsDevice.Viewport.Width, GraphicsDevice.Viewport.Height);
            var origin = HudInventoryLayout.Origin(updateLayout);
            var button = scenario.Scene == Scene.GoldCount ? HudInventoryLayout.GoldButton(origin) : HudInventoryLayout.QuestBoxButton(origin);
            Click(updateLayout, button.Center, Owner(scenario.Scene));
            _carrying = true;
        }

        _hud.Update(Owner(scenario.Scene), default, Pointer(scenario, layout));
    }

    private long _revision;

    protected override void Draw(GameTime gameTime)
    {
        if (!_prepared || !_hud.IsReady || !_hud.IconsIdle || _scenario >= Scenarios.Length)
        {
            return;
        }

        var scenario = Scenarios[_scenario];
        GraphicsDevice.SetRenderTarget(_target);
        GraphicsDevice.Clear(new Color(45, 53, 47));
        _hud.Draw();
        GraphicsDevice.SetRenderTarget(null);
        if (_clock.Elapsed.TotalMilliseconds - _startedMs < scenario.WaitMs)
        {
            return;
        }

        using var stream = File.Create(Path.Combine(_output, scenario.Name + ".png"));
        _target!.SaveAsPng(stream, scenario.Width, scenario.Height);
        Console.WriteLine($"Item HUD {scenario.Name}: captured");
        _scenario++;
        _prepared = false;
    }

    protected override void UnloadContent()
    {
        _hud.Dispose();
        _target?.Dispose();
        _batch.Dispose();
    }

    private void Click(HudLayout layout, Point logical, OwnerState? owner = null)
    {
        var x = logical.X * layout.Scale;
        var y = logical.Y * layout.Scale;
        owner ??= _owner;
        _hud.Update(owner, default, new MouseState(x, y, 0, ButtonState.Pressed, ButtonState.Released, ButtonState.Released, ButtonState.Released,
            ButtonState.Released));
        _hud.Update(owner, default, new MouseState(x, y, 0, ButtonState.Released, ButtonState.Released, ButtonState.Released, ButtonState.Released,
            ButtonState.Released));
    }

    private static MouseState Pointer(Scenario scenario, HudLayout layout)
    {
        var origin = HudInventoryLayout.Origin(layout);
        var point = scenario.Scene switch
        {
            Scene.TimerHover => new Point(HudPremiumTimers.Left + 15, HudPremiumTimers.Top + HudPremiumTimers.Step + 15),
            Scene.Teleport => HudTeleportCore.Row(3).Center,
            Scene.TeleportPage3 => HudTeleportCore.Row(7).Center,
            Scene.OrbTooltip => HudInventoryLayout.Cell(origin, 0, 1, 1).Center,
            Scene.PackageTooltip => HudInventoryLayout.Cell(origin, 1, 1, 1).Center,
            Scene.ItemBuffs => new Point(10 + 16, 26),
            Scene.BoosterHover => new Point(10 + 32 * 3 + 16, 26),
            Scene.CrystalTooltip => HudInventoryLayout.Cell(origin, 0, 1, 1).Center,
            Scene.GiftTooltip => HudInventoryLayout.Cell(origin, 2, 1, 1).Center,
            Scene.ChocolateTooltip => HudInventoryLayout.Cell(origin, 4, 1, 1).Center,
            Scene.CastleTooltip => HudInventoryLayout.Cell(origin, 6, 1, 1).Center,
            Scene.LuckyBagTooltip => HudInventoryLayout.Cell(origin, 24, 1, 1).Center,
            Scene.CarryFree => HudInventoryLayout.Cell(origin, 30, 1, 1).Center,
            Scene.CarryExchange => HudInventoryLayout.Cell(origin, 2, 1, 1).Center,
            Scene.CarrySlot => HudInventoryLayout.Equipment(origin, (int)EquipmentSlot.Weapon).Center,
            Scene.PairHint => HudInventoryLayout.PairButton(origin, 1).Center,
            Scene.QuestBox => HudInventoryLayout.QuestBoxButton(origin).Center,
            _ => new Point(layout.Width - 200, layout.Height / 2)
        };
        return new MouseState(point.X * layout.Scale, point.Y * layout.Scale, 0, ButtonState.Released, ButtonState.Released, ButtonState.Released,
            ButtonState.Released, ButtonState.Released);
    }

    // Seven timers with remaining times across the standard tiers, or one about to end for the blink.
    private static PremiumTimerSnapshot Timers(Scene scene, long revision)
    {
        if (scene == Scene.Blink)
        {
            return new PremiumTimerSnapshot(revision, [new PremiumTimerInfo(PremiumEffectKind.ExperienceUp, 30, 9, 3600)]);
        }

        return new PremiumTimerSnapshot(revision,
        [
            new PremiumTimerInfo(PremiumEffectKind.ThirdEyes, 50, 50_000, 86_400),
            new PremiumTimerInfo(PremiumEffectKind.ExperienceUp, 100, 1_800, 3_600),
            new PremiumTimerInfo(PremiumEffectKind.VampiricCuspid, 0, 8_000, 10_800),
            new PremiumTimerInfo(PremiumEffectKind.ManaReduce, 30, 400_000, 604_800),
            new PremiumTimerInfo(PremiumEffectKind.HelpPetTerry, 0, 2_000_000, 2_592_000),
            new PremiumTimerInfo(PremiumEffectKind.PhoenixPet, 0, 3_000, 10_800),
            new PremiumTimerInfo(PremiumEffectKind.StaminaReduce, 30, 600, 3_600)
        ]);
    }

    private const double BuffClockMs = 100_000;

    // A life and a mana booster and the skill-delay item beside an EXP potion that stays in the premium column.
    private static PremiumTimerSnapshot Boosters(long revision) => new(revision,
    [
        new PremiumTimerInfo(PremiumEffectKind.ExperienceUp, 30, 1_800, 3_600),
        new PremiumTimerInfo(PremiumEffectKind.LifeBooster, 15, 2_000, 3_600),
        new PremiumTimerInfo(PremiumEffectKind.ManaBooster, 15, 60_000, 86_400),
        new PremiumTimerInfo(PremiumEffectKind.SkillDelay, 0, 150, 300)
    ]);

    // Athanasia, a fire Defiance Stone and a Might Stone against Knights in the running-effects bar.
    private static StatusBook CastleBuffs()
    {
        var book = new StatusBook();
        void Add(uint code, int duration, int remaining) => book.Apply(new StatusEffectInfo(1, 1, code, StatusEffectOp.Add, 1, 1, 0,
            0, duration, remaining, 0), BuffClockMs);
        Add(CastleItems.StatusBase + 1, 30_000, 20_000);
        Add(CastleItems.StatusBase + 6, 60_000, 45_000);
        Add(CastleItems.StatusBase + 13, 60_000, 10_000);
        return book;
    }

    // Eternal Life, Fatal Edge and a force orb in the running-effects bar.
    private static StatusBook ItemBuffs()
    {
        var book = new StatusBook();
        void Add(uint code, int duration, int remaining) => book.Apply(new StatusEffectInfo(1, 1, code, StatusEffectOp.Add, 1, 1, 0,
            0, duration, remaining, 0), BuffClockMs);
        Add(ItemScrolls.For(ScrollEffectKind.Invulnerability).StatusCode, 30_000, 12_000);
        Add(ItemScrolls.For(ScrollEffectKind.Critical).StatusCode, 3_600_000, 3_000_000);
        ForceOrbs.TryGet("FO112", out var orb);
        Add(orb.StatusCode, 3_600_000, 1_200_000);
        return book;
    }
}
