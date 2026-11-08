using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using PristonTale.Client.Net.Presence;
using PristonTale.Client.Net.WorldCatalog;
using PristonTale.Engine3D.Diagnostics;
using PristonTale.Engine3D.Formats;
using PristonTale.Engine3D.IO;
using PristonTale.Engine3D.Rendering;
using PristonTale.Gameplay.Screens.Framework;
using PristonTale.Gameplay.UI;
using PristonTale.Gameplay.UI.Chat;
using PristonTale.Gameplay.UI.Help;
using PristonTale.Gameplay.UI.Inventory;
using PristonTale.Gameplay.UI.Npc;
using PristonTale.Gameplay.UI.Quests;
using PristonTale.Gameplay.UI.Rendering;
using PristonTale.Gameplay.UI.Widgets;
using PristonTale.Simulation.Characters;
using PristonTale.Simulation.Social.Clans;
using PristonTale.Simulation.Skills;
using PristonTale.Simulation.Combat;
using PristonTale.Simulation.Items;
using PristonTale.Simulation.Quests;
using PristonTale.Simulation.World.Npcs;
using PristonTale.Contracts.Motion;
using PristonTale.Contracts.World;
using PristonTale.Contracts.Combat;
using PristonTale.Contracts.Skills;
using PristonTale.Assets;
using PristonTale.Contracts.Characters;
using PristonTale.Contracts.Texts;
using PristonTale.Gameplay.UI.Parties;

namespace PristonTale.Tests;

internal sealed class HudCaptureProbe : Game
{
    private enum Hover
    {
        None,
        Life,
        Mana,
        Experience,
        InventoryButton,
        MapToggle,
        Item,
        SkillNode,
        SkillBasic,
        SettingsRow,
        AllocateArrow,
        ShopItem,
        WarehouseItem,
        WarehouseSuit,
        BagFree,
        WarehouseCell,
        Buff,
        QuestTimer,
        ClassChange,
        QuestOk,
        RestartField,
        RestartTown
    }

    // The help window's page a scenario opens, or the level guide's notice it offers.
    private enum ProbeHelp
    {
        None,
        Window,
        GuideList,
        GuidePage,
        Notice,
        Summary
    }

    // Quest states a scenario sets up: the server's answer to the talk, the journal and the clicks that follow.
    private enum ProbeQuest
    {
        None,
        Dialog,
        Confirm,
        RewardList,
        RewardView,
        ClassChange,
        Window,
        WindowDescription,
        Timer,
        SkillReset
    }

    // What a warehouse scenario carries once the window is open: a stored axe, or a bag potion set down on the grid.
    private enum ProbeCarry
    {
        None,
        StoredAxe,
        RefusedPotion
    }

    private const uint SkillMasterNpc = 7;
    private const uint MerchantNpc = 8;
    private const uint WarehouseNpc = 9;
    private const uint TeleporterNpc = 10;
    private const uint MixerNpc = 11;
    private const uint AlchemistNpc = 12;
    private const uint SmelterNpc = 13;
    private const uint CraftsmanNpc = 14;
    private const uint ResetterNpc = 15;
    private const uint ClanMasterNpc = 16;
    private const uint ButlerNpc = 17;
    private const uint SecretaryNpc = 18;
    private const uint TeamBoothNpc = 19;

    private enum ProbePlates
    {
        None,
        Compact,
        Classic
    }

    // A clan mate and the target monster stand side by side in the middle of the frame.
    private static HudOverheadPoint? PlateAnchor(ulong id) => id switch
    {
        4 => new HudOverheadPoint(new Vector2(300, 300), 1f),
        2 => new HudOverheadPoint(new Vector2(500, 300), 1f),
        _ => null
    };

    private sealed record Scenario(string Name, int Width, int Height, Keys Panel = Keys.None, Hover Hover = Hover.None, int HealthPercent = 100,
        PlayerClass Class = PlayerClass.Mechanician, bool Npc = false, bool Settings = false, bool SystemMenu = false, bool Learned = false,
        bool Training = false, bool LowHealth = false, float Hour = 13, bool ClassicTarget = false, ulong Target = 2, ulong NpcId = SkillMasterNpc,
        int ServiceButton = -1, bool SellMode = false, int ServiceCount = 2, int RightSelect = -1, bool Buffs = false, bool Block = false,
        ProbeQuest Quest = ProbeQuest.None, bool Dead = false, bool FieldOpen = true, ProbeCarry Carry = ProbeCarry.None,
        ProbeHelp Help = ProbeHelp.None, bool TrainingEnter = false, bool FifthTier = false, bool SecondPage = false, bool Clan = false, ProbePlates Plates = ProbePlates.None, bool ClanPage = false,
        bool Siege = false, int CastleButton = -1, bool CastleChief = true, bool Bellatra = false, int BellatraButton = -1,
        ProbeBellatraResult BellatraResult = ProbeBellatraResult.None, ProbeBellatraShow BellatraShow = ProbeBellatraShow.None, ProbeSiegeEnd SiegeEnd = ProbeSiegeEnd.None, bool Portuguese = false);

    private static readonly Scenario[] Scenarios =
    [
        new("bar-800", 800, 600),
        new("restart-800", 800, 600, Dead: true),
        new("restart-field-hover-800", 800, 600, Hover: Hover.RestartField, Dead: true),
        new("restart-field-refused-800", 800, 600, Hover: Hover.RestartField, Dead: true, FieldOpen: false),
        new("restart-town-hover-1600x1200-scale2", 1600, 1200, Hover: Hover.RestartTown, Dead: true),
        new("buffs-800", 800, 600, Buffs: true),
        new("buffs-summon-tooltip-800", 800, 600, Hover: Hover.Buff, Buffs: true),
        new("buffs-1600x1200-scale2", 1600, 1200, Buffs: true),
        new("bar-1024", 1024, 768),
        new("bar-1280", 1280, 720),
        new("bar-1600x1200-scale2", 1600, 1200),
        new("bar-3440-scale2", 3440, 1440),
        new("bar-3840-scale3", 3840, 2160),
        new("bar-night-800", 800, 600, Hour: 1),
        new("target-classic-800", 800, 600, ClassicTarget: true),
        new("target-npc-800", 800, 600, Target: 3),
        new("target-1920", 1920, 1080),
        new("life-tooltip-800", 800, 600, Hover: Hover.Life, HealthPercent: 60),
        new("mana-tooltip-800", 800, 600, Hover: Hover.Mana),
        new("experience-tooltip-800", 800, 600, Hover: Hover.Experience),
        new("menu-button-hover-800", 800, 600, Hover: Hover.InventoryButton),
        new("map-toggle-hover-800", 800, 600, Hover: Hover.MapToggle),
        new("low-resources-800", 800, 600, LowHealth: true),
        new("block-800", 800, 600, Block: true),
        new("inventory-800", 800, 600, Keys.I),
        new("inventory-item-hover-800", 800, 600, Keys.I, Hover.Item),
        new("inventory-1600x1200-scale2", 1600, 1200, Keys.I),
        new("inventory-1920", 1920, 1080, Keys.I),
        new("status-800", 800, 600, Keys.C),
        new("status-clan-800", 800, 600, Keys.C, Clan: true),
        new("plates-clan-800", 800, 600, Clan: true, Plates: ProbePlates.Compact),
        new("plates-clan-classic-800", 800, 600, Clan: true, Plates: ProbePlates.Classic),
        new("siege-board-800", 800, 600, Clan: true, Siege: true),
        new("siege-board-1280x720", 1280, 720, Clan: true, Siege: true),
        new("siege-won-800", 800, 600, Clan: true, Siege: true, SiegeEnd: ProbeSiegeEnd.Won),
        new("siege-ended-800", 800, 600, Clan: true, Siege: true, SiegeEnd: ProbeSiegeEnd.Ended),
        new("siege-ended-pt-800", 800, 600, Clan: true, Siege: true, SiegeEnd: ProbeSiegeEnd.Ended, Portuguese: true),
        new("party-clan-800", 800, 600, Keys.D, Clan: true),
        new("party-clan-page-800", 800, 600, Keys.D, Clan: true, ClanPage: true),
        new("status-arrow-hover-800", 800, 600, Keys.C, Hover.AllocateArrow),
        new("status-1280", 1280, 720, Keys.C),
        new("skills-800", 800, 600, Keys.S),
        new("skills-learned-800", 800, 600, Keys.S, Learned: true),
        new("skill-details-800", 800, 600, Keys.S, Hover.SkillNode),
        new("skill-basic-800", 800, 600, Keys.S, Hover.SkillBasic),
        new("skill-right-select-800", 800, 600, Keys.S, Learned: true, RightSelect: 1),
        new("skills-fighter-800", 800, 600, Keys.S, Class: PlayerClass.Fighter),
        new("skills-knight-800", 800, 600, Keys.S, Class: PlayerClass.Knight),
        new("skills-priestess-800", 800, 600, Keys.S, Class: PlayerClass.Priestess),
        new("skills-shaman-800", 800, 600, Keys.S, Class: PlayerClass.Shaman),
        new("skills-fifth-800", 800, 600, Keys.S, FifthTier: true),
        new("skills-fifth-page-800", 800, 600, Keys.S, FifthTier: true, SecondPage: true),
        new("skill-master-fifth-800", 800, 600, Npc: true, FifthTier: true, SecondPage: true),
        new("npc-unicode-800", 800, 600, Npc: true),
        new("npc-unicode-1280", 1280, 720, Npc: true),
        new("skill-training-800", 800, 600, Npc: true, Learned: true, Training: true),
        new("skill-master-800", 800, 600, Npc: true, Learned: true),
        new("skill-master-quest-800", 800, 600, Npc: true, Learned: true, Quest: ProbeQuest.Dialog),
        new("skill-training-enter-800", 800, 600, Npc: true, Learned: true, Training: true, TrainingEnter: true),
        new("npc-merchant-dialog-800", 800, 600, Npc: true, NpcId: MerchantNpc),
        new("npc-teleporter-dialog-800", 800, 600, Npc: true, NpcId: TeleporterNpc),
        new("npc-teleport-destinations-800", 800, 600, Npc: true, NpcId: TeleporterNpc, ServiceButton: 1, ServiceCount: 4),
        new("npc-shop-800", 800, 600, Hover: Hover.ShopItem, Npc: true, NpcId: MerchantNpc, ServiceButton: 1),
        new("npc-shop-sell-800", 800, 600, Hover: Hover.Item, Npc: true, NpcId: MerchantNpc, ServiceButton: 1, SellMode: true),
        new("npc-shop-1280", 1280, 720, Npc: true, NpcId: MerchantNpc, ServiceButton: 1),
        new("npc-warehouse-800", 800, 600, Hover: Hover.WarehouseItem, Npc: true, NpcId: WarehouseNpc, ServiceButton: 1),
        new("npc-warehouse-1600x1200-scale2", 1600, 1200, Npc: true, NpcId: WarehouseNpc, ServiceButton: 1),
        new("npc-warehouse-carry-exchange-800", 800, 600, Hover: Hover.WarehouseSuit, Npc: true, NpcId: WarehouseNpc, ServiceButton: 1,
            Carry: ProbeCarry.StoredAxe),
        new("npc-warehouse-carry-bag-800", 800, 600, Hover: Hover.BagFree, Npc: true, NpcId: WarehouseNpc, ServiceButton: 1, Carry: ProbeCarry.StoredAxe),
        new("npc-warehouse-potion-refused-800", 800, 600, Hover: Hover.WarehouseCell, Npc: true, NpcId: WarehouseNpc, ServiceButton: 1,
            Carry: ProbeCarry.RefusedPotion),
        new("npc-aging-800", 800, 600, Npc: true, NpcId: TeleporterNpc, ServiceButton: 2, ServiceCount: 4),
        new("npc-aging-1600x1200-scale2", 1600, 1200, Npc: true, NpcId: TeleporterNpc, ServiceButton: 2, ServiceCount: 4),
        new("npc-mix-800", 800, 600, Npc: true, NpcId: MixerNpc, ServiceButton: 1),
        new("npc-alchemy-800", 800, 600, Npc: true, NpcId: AlchemistNpc, ServiceButton: 1),
        new("npc-smelting-800", 800, 600, Npc: true, NpcId: SmelterNpc, ServiceButton: 0, ServiceCount: 1),
        new("npc-manufacture-800", 800, 600, Npc: true, NpcId: CraftsmanNpc, ServiceButton: 0, ServiceCount: 1),
        new("npc-mixture-reset-800", 800, 600, Npc: true, NpcId: ResetterNpc, ServiceButton: 0, ServiceCount: 1),
        new("clan-intro-800", 800, 600, Npc: true, NpcId: ClanMasterNpc, ServiceButton: 0, ServiceCount: 1),
        new("clan-chief-800", 800, 600, Npc: true, NpcId: ClanMasterNpc, ServiceButton: 0, ServiceCount: 1, Clan: true),
        new("castle-owner-800", 800, 600, Npc: true, NpcId: ButlerNpc, ServiceButton: 0, ServiceCount: 1),
        new("castle-owner-member-800", 800, 600, Npc: true, NpcId: ButlerNpc, ServiceButton: 0, ServiceCount: 1, CastleChief: false),
        new("castle-owner-towers-800", 800, 600, Npc: true, NpcId: ButlerNpc, ServiceButton: 0, ServiceCount: 1, CastleButton: 2),
        new("castle-owner-tax-800", 800, 600, Npc: true, NpcId: ButlerNpc, ServiceButton: 0, ServiceCount: 1, CastleButton: 0),
        new("castle-owner-mercenaries-1280", 1280, 720, Npc: true, NpcId: ButlerNpc, ServiceButton: 0, ServiceCount: 1, CastleButton: 3),
        new("bellatra-board-800", 800, 600, Bellatra: true),
        new("bellatra-board-1280x720", 1280, 720, Bellatra: true),
        new("bellatra-secretary-800", 800, 600, Npc: true, NpcId: SecretaryNpc, ServiceButton: 0, ServiceCount: 1),
        new("bellatra-ranking-800", 800, 600, Npc: true, NpcId: SecretaryNpc, ServiceButton: 0, ServiceCount: 1, BellatraButton: 1),
        new("bellatra-result-800", 800, 600, Bellatra: true, BellatraResult: ProbeBellatraResult.Final),
        new("bellatra-result-live-800", 800, 600, Npc: true, NpcId: TeamBoothNpc, ServiceButton: 0, ServiceCount: 1, BellatraResult: ProbeBellatraResult.Live),
        new("bellatra-result-1280x720", 1280, 720, Bellatra: true, BellatraResult: ProbeBellatraResult.Final),
        new("bellatra-show-loading-800", 800, 600, BellatraShow: ProbeBellatraShow.Loading),
        new("bellatra-show-round-800", 800, 600, BellatraShow: ProbeBellatraShow.Round),
        new("bellatra-show-start-800", 800, 600, BellatraShow: ProbeBellatraShow.Start),
        new("bellatra-show-complete-800", 800, 600, BellatraShow: ProbeBellatraShow.Complete),
        new("bellatra-show-fail-800", 800, 600, BellatraShow: ProbeBellatraShow.Fail),
        new("bellatra-show-closed-800", 800, 600, BellatraShow: ProbeBellatraShow.Closed),
        new("bellatra-show-closed-1280x720", 1280, 720, BellatraShow: ProbeBellatraShow.Closed),
        new("bellatra-show-quake-800", 800, 600, BellatraShow: ProbeBellatraShow.Quake),
        new("bellatra-show-stun-800", 800, 600, BellatraShow: ProbeBellatraShow.Stun),
        new("bellatra-show-freeze-800", 800, 600, BellatraShow: ProbeBellatraShow.Freeze),
        new("bellatra-show-rabbit-800", 800, 600, BellatraShow: ProbeBellatraShow.Rabbit),
        new("bellatra-show-points-800", 800, 600, BellatraShow: ProbeBellatraShow.Points),
        new("bellatra-show-congratulations-1280x720", 1280, 720, BellatraShow: ProbeBellatraShow.Congratulations),
        new("system-menu-800", 800, 600, SystemMenu: true),
        new("settings-800", 800, 600, Hover: Hover.SettingsRow, Settings: true),
        new("settings-1600x1200-scale2", 1600, 1200, Settings: true),
        new("quest-dialog-800", 800, 600, Hover: Hover.QuestOk, Npc: true, Quest: ProbeQuest.Dialog),
        new("quest-confirm-800", 800, 600, Npc: true, Quest: ProbeQuest.Confirm),
        new("quest-reward-list-800", 800, 600, Npc: true, Quest: ProbeQuest.RewardList),
        new("quest-reward-view-800", 800, 600, Npc: true, Quest: ProbeQuest.RewardView),
        new("quest-class-change-800", 800, 600, Hover: Hover.ClassChange, Npc: true, Quest: ProbeQuest.ClassChange),
        new("quest-window-800", 800, 600, Keys.Q, Quest: ProbeQuest.Window),
        new("quest-window-description-800", 800, 600, Keys.Q, Quest: ProbeQuest.WindowDescription),
        new("quest-window-1600x1200-scale2", 1600, 1200, Keys.Q, Quest: ProbeQuest.WindowDescription),
        new("quest-timer-800", 800, 600, Hover: Hover.QuestTimer, Quest: ProbeQuest.Timer),
        new("quest-timer-buffs-800", 800, 600, Buffs: true, Quest: ProbeQuest.Timer),
        new("quest-skill-reset-800", 800, 600, Quest: ProbeQuest.SkillReset),
        new("help-window-800", 800, 600, Help: ProbeHelp.Window),
        new("help-guide-list-800", 800, 600, Learned: true, Help: ProbeHelp.GuideList),
        new("help-guide-page-800", 800, 600, Learned: true, Help: ProbeHelp.GuidePage),
        new("help-guide-page-1600x1200-scale2", 1600, 1200, Learned: true, Help: ProbeHelp.GuidePage),
        new("help-level-notice-800", 800, 600, Help: ProbeHelp.Notice),
        new("help-level-summary-800", 800, 600, Help: ProbeHelp.Summary)
    ];

    private readonly string _assetRoot;
    private readonly string _output;
    private readonly string? _backgroundPath;
    private SpriteBatch _batch = null!;
    private GameplayHud _hud = null!;
    private Texture2D? _background;
    private RenderTarget2D? _target;
    private OwnerState _owner = null!;
    private HudWorldView _world;
    private int _scenario;
    private int _frames;
    private bool _prepared;
    private int _preparedFrame;
    private bool _carried;
    // The help scenario's window page or notice is up (they wait for the guide's files).
    private bool _helpReady;
    private readonly PresentationSettings _presentation = new();

    public HudCaptureProbe(string assetRoot, string contentRoot, string output, string? background)
    {
        _assetRoot = assetRoot;
        _output = output;
        _backgroundPath = background;
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
        Console.WriteLine($"HUD font: {font.Texture.Width}x{font.Texture.Height}; format={font.Texture.Format}; glyphs={font.Characters.Count}; lineSpacing={font.LineSpacing}; M={font.MeasureString("M")}");
        var context = new ScreenContext(GraphicsDevice, Content, _batch, font,
            _assetRoot, null!, ConsoleClientLog.Instance, _ => { }, _presentation);
        _hud = new GameplayHud(context,
            _ => Task.FromResult(new InventoryOutcome(false, 0, GameText.Of(GameTextId.RequestRefused))),
            interactNpc: id => Task.FromResult(Interaction(id)),
            requestNpcService: command => Task.FromResult(Reply(command)),
            requestQuest: command => Task.FromResult(QuestReply(command)));
        _hud.CharacterName = "Panzer";
        _hud.CharacterClass = PlayerClass.Mechanician;
        var player = new CharacterClassRegistry().CreatePlayer(PlayerClass.Mechanician, "Panzer");
        _owner = OwnerState.Capture(player) with
        {
            Money = 12750,
            StatPoints = 5,
            CarriedWeight = 132,
            MaxWeight = 196,
            Resistances = new ResistanceBlock(12, 0, 8, 5, 3, 15, 0, 0),
            Items = new OwnedItemView[]
            {
                new(Guid.NewGuid(), 1, "Axe", ItemKind.Axe, 1, 0, -1, 2, 3, "WA101")
                {
                    Details = Detail(new ItemRoll(5, 9, 22, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, ResistanceBlock.Zero, 1, new CharacterAttributes(18, 0, 0, 0, 0), 120, 0,
                        ItemSpecBonus.None), critical: 5, attackSpeed: 6)
                },
                new(Guid.NewGuid(), 2, "Battle armor with a deliberately long localized display name", ItemKind.Armor, 1, 3, -1, 2, 3, "DA102")
                {
                    Details = Detail(new ItemRoll(0, 0, 0, 24, 1.4f, 0, 0, 0.8f, 0, 0, 0, 0, 0, new ResistanceBlock(6, 0, 4, 0, 0, 0, 0, 0), 4,
                        new CharacterAttributes(60, 0, 0, 0, 0), 1440, (uint)JobBitCode.Mechanician,
                        ItemSpecBonus.None with { Defence = 6, Absorb = 0.6f, LevelLife = 5 }))
                },
                new(Guid.NewGuid(), 3, "Small mana potion", ItemKind.Potion, 20, 8, -1, 1, 1, "PM101"),
                new(Guid.NewGuid(), 4, "Small healing potion", ItemKind.Potion, 7, 9, -1, 1, 1, "PL101"),
                new(Guid.NewGuid(), 5, "Equipped axe", ItemKind.Axe, 1, -1, 0, 2, 3, "WA101"),
                new(Guid.NewGuid(), 6, "Ring of Strength", ItemKind.Ring, 1, -1, (int)EquipmentSlot.RingLeft, 1, 1, "OR105")
                {
                    Details = Detail(new ItemRoll(0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 10, 0, 0, ResistanceBlock.Zero, 30, CharacterAttributes.Empty, 900, 0,
                        ItemSpecBonus.None))
                }
            }
        };

        // Diagnostic bootstrap only: the Ricarten stage supplies the authored minimap rectangle.
        var stage = new PristonSmdReader().ReadStage(AssetFiles.Resolve(Path.Combine(_assetRoot, "Field", "Ricarten", "village-2.smd")));
        var map = stage.AuthoredMapRectangle;
        var center = new Vector3((map.Left + map.Right) / 512f, 0, -(map.Top + map.Bottom) / 512f);
        _world = new HudWorldView("village-2", false, map, center, new Vector3(0.6f, 0, -0.8f), 13, false);
        Console.WriteLine($"HUD minimap rectangle: {map}");

        _hud.Combat.SetCatalog(new WorldRenderCatalog(new Dictionary<uint, MonsterRenderInfo>
        {
            [1] = new(1, "", 1, "Hopy", 1)
        }));
        _hud.Combat.Observe(new PresenceUpdate(1,
        [
            new(1, EntityKind.Player, true, default, 0, MotionState.Stand, 64, 64,
                new PlayerAppearanceInfo("Panzer", PlayerClass.Fighter, 0, 0, 0, "", "", 1), 0),
            new(4, EntityKind.Player, false, default, 0, MotionState.Stand, 64, 64,
                new PlayerAppearanceInfo("Panzer", PlayerClass.Mechanician, 0, 0, 0, "", "", 175) { ClanId = 7, ClanName = "GM" }, 0),
            new(2, EntityKind.Monster, false, default, 0, MotionState.Stand, 31, 50, null, 1, Level: 7),
            new(3, EntityKind.Npc, false, new System.Numerics.Vector3(center.X + 300, 0, center.Z - 200), 0, MotionState.Stand, 0, 0, null, 1,
                Name: "Lena")
        ], [], [],
        [
            new(1, 2, CombatEventKind.Hit, 0, 0, 8, false, false),
            new(2, 1, CombatEventKind.Hit, 0, 0, 3, false, false),
            new(1, 2, CombatEventKind.Hit, 0, 0, 11, true, false)
        ]));
        _hud.TargetId = 2;
        if (_backgroundPath is not null)
        {
            var image = PristonImageDecoder.Decode(File.ReadAllBytes(_backgroundPath), ".png");
            _background = new Texture2D(GraphicsDevice, image.Width, image.Height);
            _background.SetData(image.Pixels);
        }
    }

    protected override void Update(GameTime gameTime)
    {
        _frames++;
        if (_frames > 3600 || _hud.AssetsFailed)
        {
            throw new InvalidOperationException("HUD assets did not become ready within the capture timeout.");
        }

        if (_scenario >= Scenarios.Length)
        {
            Exit();
            return;
        }

        var scenario = Scenarios[_scenario];
        if (!_prepared)
        {
            _target?.Dispose();
            _target = new RenderTarget2D(GraphicsDevice, scenario.Width, scenario.Height);
            GraphicsDevice.SetRenderTarget(_target);
            _hud.ClosePanel();
            _hud.CharacterClass = scenario.Class;
            _hud.World = _world with { TimeOfDay = scenario.Hour };
            _hud.TargetId = scenario.Target;
            _hud.Statuses = scenario.Buffs ? BuffBook() : null;
            _hud.SelfId = 1;
            _hud.SkillClockMs = BuffClockMs;
            _hud.SummonAutomatic = code => code == MetalGolem ? true : null;
            _hud.PlayerDead = scenario.Dead;
            _hud.RestartOffer = scenario.Dead ? new PristonTale.Simulation.Progression.RestartOffer(scenario.FieldOpen, false, 0, 0) : null;
            _presentation.Requested = _presentation.Requested with
            {
                ClassicTargetHealthBar = scenario.ClassicTarget,
                ClassicNamePlates = scenario.Plates == ProbePlates.Classic
            };
            _hud.Overhead = scenario.Plates == ProbePlates.None ? static _ => null : PlateAnchor;
            var state = ScenarioState(scenario);
            var layout = HudLayout.Create(scenario.Width, scenario.Height);
            _quest = scenario.Quest;
            _hud.ObserveQuests(new PresenceUpdate(1, [], [], [], []) { Quest = QuestJournal(scenario.Quest) });
            if (scenario.Npc)
            {
                _hud.OpenNpc(scenario.NpcId);
            }

            _hud.Update(state, default, default);
            _hud.Update(state, scenario.Panel == Keys.None ? default : new KeyboardState(scenario.Panel), default);
            if (scenario.Settings || scenario.SystemMenu)
            {
                Click(state, layout, layout.Bottom(HudBar.MenuButton(HudMenuButton.System)).Center);
            }

            if (scenario.Settings)
            {
                Click(state, layout, layout.Bottom(HudSystemMenu.Entry(HudSystemEntry.Settings)).Center);
            }

            if (scenario.Clan)
            {
                _hud.ObserveClan(new PresenceUpdate(1, [], [], [], [])
                {
                    Clan = new ClanInfo(7, "Lions", ClanState.Registering, DateTimeOffset.UtcNow.ToUnixTimeSeconds() + 3 * 86400 + 5 * 3600, ClanRank.Chief, 22, "", 0,
                    [
                        new ClanMemberInfo("Founder", PlayerClass.Knight, 42, ClanRank.Chief, true),
                        new ClanMemberInfo("Ann", PlayerClass.Priestess, 38, ClanRank.Vice, true),
                        new ClanMemberInfo("Bob", PlayerClass.Archer, 41, ClanRank.Member, false),
                        new ClanMemberInfo("Cid", PlayerClass.Fighter, 35, ClanRank.Member, true)
                    ])
                });
            }

            Gameplay.Localization.ClientText.Use(scenario.Portuguese ? Engine3D.Rendering.ClientSettings.BrazilianPortugueseLanguage : Engine3D.Rendering.ClientSettings.EnglishLanguage);

            // Boards of an earlier scenario stay on the shared HUD otherwise.
            _hud.InCastleField = false;
            _hud.ObserveBellatra(new PresenceUpdate(1, [], [], [], []) { Bellatra = BellatraBoardInfo.Hidden });
            if (scenario.Siege)
            {
                _hud.InCastleField = true;
                var castle = new CastleInfo(8, "Eagles", 5, Simulation.Social.Castle.SiegeState.Running, false, 75 * 60 + 12, true, 4,
                [
                    new CastleClanInfo(9, "Tigers", 52_000), new CastleClanInfo(7, "Lions", 31_000), new CastleClanInfo(11, "Wolves", 9_500),
                    new CastleClanInfo(12, "Hawks", 4_100)
                ], 96_600);
                _hud.ObserveCastle(new PresenceUpdate(1, [], [], [], []) { Castle = castle });
                if (scenario.SiegeEnd != ProbeSiegeEnd.None)
                {
                    _hud.ObserveCastle(new PresenceUpdate(1, [], [], [], [])
                    {
                        Castle = castle with { OwnerClanId = scenario.SiegeEnd == ProbeSiegeEnd.Won ? 7 : 8, Siege = Simulation.Social.Castle.SiegeState.Over }
                    });
                }
            }

            if (scenario.Bellatra)
            {
                _hud.ObserveBellatra(new PresenceUpdate(1, [], [], [], [])
                {
                    Bellatra = new BellatraBoardInfo(true, 5, 1, 48_250, [31_200, 96_400, 12_050, 0], Contracts.Social.BellatraOutcome.GoesOn)
                });
            }

            if (scenario.BellatraShow != ProbeBellatraShow.None)
            {
                Show(state, scenario.BellatraShow);
            }

            if (scenario.ClanPage)
            {
                _hud.Update(state, default, default);
                Click(state, layout, HudPartyWindow.ClanTab(HudSidePanel.Origin(layout)).Center);
            }

            if (scenario.ServiceButton >= 0)
            {
                Click(state, layout, HudNpcPanel.ServiceButton(scenario.ServiceButton, scenario.ServiceCount).Center);
                _hud.Update(state, default, default);
                _hud.Update(state, default, default);
            }

            if (scenario.NpcId == ButlerNpc)
            {
                _hud.ObserveCastle(new PresenceUpdate(1, [], [], [], [])
                {
                    CastleOwner = new CastleOwnerInfo(true, 12_345_678, 3,
                        new Simulation.Social.Castle.CastleSetup(5, Simulation.Social.Castle.CastleClanSkill.Damage,
                        [
                            Simulation.Social.Castle.CastleTower.Fire, Simulation.Social.Castle.CastleTower.Ice, Simulation.Social.Castle.CastleTower.None,
                            Simulation.Social.Castle.CastleTower.Lightning, Simulation.Social.Castle.CastleTower.None, Simulation.Social.Castle.CastleTower.None
                        ], [8, 4, 2]),
                        Simulation.Social.Castle.CastleClanSkill.Evasion, scenario.CastleChief),
                    CastleOwnerResult = new CastleOwnerResultInfo(Protocol.Packets.CastleOwnerAction.Open, Simulation.Social.Castle.CastleOwnerStatus.Ok)
                });
                if (scenario.CastleButton >= 0)
                {
                    Click(state, layout, HudNpcPanel.ServiceButton(scenario.CastleButton, 5).Center);
                    Click(state, layout, new Point(HudNpcPanel.TextLeft + 30, HudNpcPanel.TextTop + 2 * HudPainter.LineHeight + 4));
                }
            }

            if (scenario.NpcId == SecretaryNpc)
            {
                _hud.ObserveBellatra(new PresenceUpdate(1, [], [], [], [])
                {
                    BellatraClan = new BellatraClanInfo(true, 7, "Lions", "Founder", 5, 1_234_567, true,
                        [new BellatraClanRankInfo("Lions", 412_000), new BellatraClanRankInfo("Tigers", 288_500), new BellatraClanRankInfo("Wolves", 97_300)]),
                    BellatraClanResult = new BellatraClanResultInfo(Protocol.Packets.BellatraClanAction.Open, Contracts.Social.BellatraClanStatus.Ok)
                });
                if (scenario.BellatraButton >= 0)
                {
                    Click(state, layout, HudNpcPanel.ServiceButton(scenario.BellatraButton, 2).Center);
                }
            }

            if (scenario.BellatraResult != ProbeBellatraResult.None)
            {
                var final = scenario.BellatraResult == ProbeBellatraResult.Final;
                if (final)
                {
                    _hud.ObserveBellatra(new PresenceUpdate(1, [], [], [], [])
                    {
                        Bellatra = new BellatraBoardInfo(true, 0, 1, 96_400, [31_200, 96_400, 12_050, 0], Contracts.Social.BellatraOutcome.Cleared)
                    });
                }

                string[] names = ["Valkyrie", "Hammer", "Ann", "Bob", "Cid", "Dorothea", "Eve", "Fang", "Gil", "Hilda"];
                _hud.ObserveBellatra(new PresenceUpdate(1, [], [], [], [])
                {
                    BellatraResult = new BellatraResultInfo(final, [.. names.Take(final ? 10 : 3).Select(static (name, index) =>
                        new BellatraResultLineInfo(name, (PlayerClass)(index % 11 + 1), 96_400 - index * 9_100, 120 - index * 11))])
                });
            }

            if (scenario.SellMode)
            {
                Click(state, layout, HudStoreLayout.Sell(HudStoreLayout.Origin(0)).Center);
            }

            if (scenario.SecondPage)
            {
                Click(state, layout, HudSkillTreeLayout.PageForward(HudInventoryLayout.Origin(layout)).Center);
            }

            if (scenario.RightSelect >= 0)
            {
                _hud.Update(state, default, default);
                var row = HudSkillsPanel.ClassRow(HudInventoryLayout.Origin(layout), scenario.RightSelect).Center;
                var (x, y) = (row.X * layout.Scale, row.Y * layout.Scale);
                _hud.Update(state, default, new MouseState(x, y, 0, ButtonState.Released, ButtonState.Released, ButtonState.Pressed,
                    ButtonState.Released, ButtonState.Released));
                _hud.Update(state, default, new MouseState(x, y, 0, ButtonState.Released, ButtonState.Released, ButtonState.Released,
                    ButtonState.Released, ButtonState.Released));
                Console.WriteLine($"HUD {scenario.Name}: right socket {_hud.Bindings.RightSkillId:X8}");
            }

            if (scenario.Quest == ProbeQuest.Confirm)
            {
                Click(state, layout, HudQuestDialog.OkButton.Center);
            }

            if (scenario.Quest == ProbeQuest.WindowDescription)
            {
                Click(state, layout, HudQuestWindow.Row(HudSidePanel.Origin(layout), 0).Center);
            }

            _helpReady = scenario.Help == ProbeHelp.None;
            if (scenario.Training)
            {
                Click(state, layout, HudSkillsPanel.ClassRow(HudInventoryLayout.Origin(layout), 0).Center);
                if (scenario.TrainingEnter)
                {
                    _hud.Update(state, new KeyboardState(Keys.Enter), default);
                    _hud.Update(state, default, default);
                }
            }

            _prepared = true;
            _preparedFrame = _frames;
            _carried = scenario.Carry == ProbeCarry.None;
        }
        else
        {
            GraphicsDevice.SetRenderTarget(_target);
            // The windows take clicks once they have slid fully open.
            if (!_carried && _frames - _preparedFrame > 60)
            {
                Carry(scenario);
                _carried = true;
                _preparedFrame = _frames;
            }

            if (!_helpReady && _hud.HelpWindow.Book.Subjects.Count > 0)
            {
                PrepareHelp(scenario);
                _helpReady = true;
            }

            // A carry keeps the HUD clock at the frame rate, so the notice its refusal shows is still up when captured; Bellatra's
            // show stays at the moment it was stepped to.
            _hud.Update(ScenarioState(scenario), default, Pointer(scenario),
                scenario.BellatraShow != ProbeBellatraShow.None ? 0 : scenario.Carry == ProbeCarry.None ? 1 : 1d / 60);
            if (scenario.Block)
            {
                _hud.StringEffect.Show(HudArt.BlockText);
            }
        }
    }

    protected override void Draw(GameTime gameTime)
    {
        // A dead scenario waits for the restart window to open after the death motion; a carry for its clicks to settle.
        if (!_prepared || !_hud.IsReady || !_hud.IconsIdle || _scenario >= Scenarios.Length || Scenarios[_scenario].Dead && !_hud.Restart.Open
            || !_carried || !_helpReady || Scenarios[_scenario].Carry != ProbeCarry.None && _frames - _preparedFrame < 10)
        {
            GraphicsDevice.SetRenderTarget(null);
            return;
        }

        var scenario = Scenarios[_scenario];
        GraphicsDevice.SetRenderTarget(_target);
        GraphicsDevice.Clear(new Color(45, 53, 47));
        if (_background is not null)
        {
            _batch.Begin();
            _batch.Draw(_background, new Rectangle(0, 0, scenario.Width, scenario.Height), Color.White);
            _batch.End();
        }

        _hud.Draw();
        GraphicsDevice.SetRenderTarget(null);
        using var stream = File.Create(Path.Combine(_output, scenario.Name + ".png"));
        _target!.SaveAsPng(stream, scenario.Width, scenario.Height);
        Console.WriteLine($"HUD {scenario.Name}: captured; textureBytes={_hud.TextureBytes}; GPU={GraphicsDevice.Adapter.DeviceName}");
        _scenario++;
        _prepared = false;
    }

    protected override void UnloadContent()
    {
        _hud.Dispose();
        _target?.Dispose();
        _background?.Dispose();
        _batch.Dispose();
    }

    // Opens the help window on the scenario's page through its clicks, or offers the notice of the level-10 guide page.
    private void PrepareHelp(Scenario scenario)
    {
        var state = ScenarioState(scenario);
        var layout = HudLayout.Create(scenario.Width, scenario.Height);
        if (scenario.Help is ProbeHelp.Notice or ProbeHelp.Summary)
        {
            _hud.LevelGuide.Offer(_hud.HelpWindow.Book.Page(10, tempskron: true)!, atOnce: scenario.Help == ProbeHelp.Summary);
            return;
        }

        Click(state, layout, layout.Bottom(HudBar.MenuButton(HudMenuButton.System)).Center);
        Click(state, layout, layout.Bottom(HudSystemMenu.Entry(HudSystemEntry.Help)).Center);
        if (scenario.Help != ProbeHelp.Window)
        {
            Click(state, layout, HudHelpWindow.GuideLink.Center);
        }

        if (scenario.Help == ProbeHelp.GuidePage)
        {
            Click(state, layout, HudHelpWindow.ListRow(0).Center);
        }
    }

    private void Carry(Scenario scenario)
    {
        var state = ScenarioState(scenario);
        var layout = HudLayout.Create(scenario.Width, scenario.Height);
        var store = HudStoreLayout.Origin(0);
        if (scenario.Carry == ProbeCarry.StoredAxe)
        {
            Click(state, layout, HudStoreLayout.Cell(store, 2, 2, 3).Center);
            return;
        }

        Click(state, layout, HudInventoryLayout.Cell(HudInventoryLayout.Origin(layout), 8).Center);
        Click(state, layout, HudStoreLayout.Cell(store, 40).Center);
    }

    private void Click(OwnerState state, HudLayout layout, Point logical)
    {
        var x = logical.X * layout.Scale;
        var y = logical.Y * layout.Scale;
        _hud.Update(state, default, new MouseState(x, y, 0,
            ButtonState.Pressed, ButtonState.Released, ButtonState.Released, ButtonState.Released, ButtonState.Released));
        _hud.Update(state, default, new MouseState(x, y, 0,
            ButtonState.Released, ButtonState.Released, ButtonState.Released, ButtonState.Released, ButtonState.Released));
    }

    private static MouseState Pointer(Scenario scenario)
    {
        var layout = HudLayout.Create(scenario.Width, scenario.Height);
        var origin = HudInventoryLayout.Origin(layout);
        var point = scenario.Hover switch
        {
            Hover.Life => layout.Bottom(HudBar.LifeHover).Center,
            Hover.Mana => layout.Bottom(HudBar.ManaHover).Center,
            Hover.Experience => layout.Bottom(HudBar.ExperienceHover).Center,
            Hover.InventoryButton => layout.Bottom(HudBar.MenuButton(HudMenuButton.Inventory)).Center,
            Hover.MapToggle => layout.Bottom(HudBar.Toggle(HudToggle.Map)).Center,
            Hover.Item => HudInventoryLayout.Cell(origin, 3, 2, 3).Center,
            Hover.SkillNode => HudSkillsPanel.ClassRow(origin, 0).Center,
            Hover.SkillBasic => HudSkillTreeLayout.BasicAttack(origin).Center,
            Hover.SettingsRow => HudSettingsPanel.Row(4).Center,
            Hover.AllocateArrow => HudStatusPanel.AllocateButton(origin, 0).Center,
            Hover.ShopItem => HudStoreLayout.Cell(HudStoreLayout.Origin(0), 0, 2, 3).Center,
            Hover.WarehouseItem => HudStoreLayout.Cell(HudStoreLayout.Origin(0), 2, 2, 3).Center,
            Hover.WarehouseSuit => HudStoreLayout.Cell(HudStoreLayout.Origin(0), 5, 2, 3).Center,
            Hover.BagFree => HudInventoryLayout.Cell(origin, 6, 2, 3).Center,
            Hover.WarehouseCell => HudStoreLayout.Cell(HudStoreLayout.Origin(0), 40).Center,
            Hover.Buff => new Point(10 + 16, 26),
            Hover.QuestTimer => new Point(10 + 16, 26),
            Hover.ClassChange => HudSkillTreeLayout.ClassChange.Center,
            Hover.QuestOk => HudQuestDialog.OkButton.Center,
            Hover.RestartField => HudRestartPanel.Button(layout, RestartChoice.Field).Center,
            Hover.RestartTown => HudRestartPanel.Button(layout, RestartChoice.Town).Center,
            _ => new Point(layout.Width - 200, layout.Height / 2)
        };
        return new MouseState(point.X * layout.Scale, point.Y * layout.Scale, 0,
            ButtonState.Released, ButtonState.Released, ButtonState.Released, ButtonState.Released, ButtonState.Released);
    }

    private const double BuffClockMs = 100_000;
    private const uint MetalGolem = 0x01080004;

    // A long-running shield, a fresh summon, a nearly spent absorption and a debuff, oldest first after sorting.
    private static PristonTale.Gameplay.Effects.StatusBook BuffBook()
    {
        var book = new PristonTale.Gameplay.Effects.StatusBook();
        void Add(uint code, int duration, int remaining, ushort flags = 0) => book.Apply(new StatusEffectInfo(1, 1, code, StatusEffectOp.Add, 1, 1, 0,
            flags, duration, remaining, 0), BuffClockMs);
        Add(0x01010001, 60_000, 20_000);
        Add(MetalGolem, 360_000, 300_000);
        Add(0x01020002, 60_000, 45_000);
        Add(0x01010003, 30_000, 1_500);
        Add(0x01010001 + 0x00100000, 10_000, 6_000, PristonTale.Protocol.Packets.StatusEffectFlags.Harmful);
        return book;
    }

    private static NpcInteraction Interaction(ulong id)
    {
        NpcServiceOffer Offer(NpcServiceKind kind, int value, bool available) => new(kind, value, available);
        return id switch
        {
            MerchantNpc => new NpcInteraction(true, id, "Blacksmith Goden",
                [Offer(NpcServiceKind.QuestGiver, 1018, false), Offer(NpcServiceKind.Shop, 0, true)],
                ["Welcome to Goden's. We deal with equipment for warriors of every level, so take your time."], GameText.None),
            WarehouseNpc => new NpcInteraction(true, id, "Warehouse keeper, Arent",
                [Offer(NpcServiceKind.QuestGiver, 1015, false), Offer(NpcServiceKind.Warehouse, 0, true)],
                ["Here is the key. Just take it and do not touch my things."], GameText.None),
            TeleporterNpc => new NpcInteraction(true, id, "Clever Newter",
                [Offer(NpcServiceKind.QuestGiver, 2201, false), Offer(NpcServiceKind.Teleporter, 1, true), Offer(NpcServiceKind.ItemAging, 0, true),
                    Offer(NpcServiceKind.ItemMix, 200, false)],
                ["I have researched and studied for the prosperity of Pillai all my life. Teleporting is not free, though."], GameText.None),
            MixerNpc => new NpcInteraction(true, id, "Mixer Rayon", [Offer(NpcServiceKind.QuestGiver, 1016, false), Offer(NpcServiceKind.ItemMix, 0, true)],
                ["Bring me the item and the sheltoms of a formula."], GameText.None),
            AlchemistNpc => new NpcInteraction(true, id, "Alchemist Lisa",
                [Offer(NpcServiceKind.QuestGiver, 1017, false), Offer(NpcServiceKind.ItemMix, Simulation.Items.Mixing.MixingWork.AlchemyMode, true)],
                ["Sheltoms become force orbs here."], GameText.None),
            ClanMasterNpc => new NpcInteraction(true, id, "Clan Master", [Offer(NpcServiceKind.Clan, 0, true)],
                ["All matters of clans are handled by me!"], GameText.None),
            ButlerNpc => new NpcInteraction(true, id, "Butler Anders", [Offer(NpcServiceKind.BlessCastle, 0, true)],
                ["How may I serve the lords of Bless Castle?"], GameText.None),
            TeamBoothNpc => new NpcInteraction(true, id, "Ticket booth Ariel", [Offer(NpcServiceKind.EventBooth, 2, true)],
                ["Team 2's tickets are sold here."], GameText.None),
            SecretaryNpc => new NpcInteraction(true, id, "Secretary Karina", [Offer(NpcServiceKind.EventBooth, Simulation.World.Bellatra.BellatraRules.SecretaryBooth, true)],
                ["I manage Bellatra's operation."], GameText.None),
            SmelterNpc => new NpcInteraction(true, id, "Smelter Ruby", [Offer(NpcServiceKind.QuestGiver, 1019, false), Offer(NpcServiceKind.Smelting, 0, true)],
                ["Three ores or crystals of one colour and their sheltom."], GameText.None),
            CraftsmanNpc => new NpcInteraction(true, id, "Craftsman Salon",
                [Offer(NpcServiceKind.QuestGiver, 2200, false), Offer(NpcServiceKind.Manufacture, 0, true)], ["A recipe and its runes."], GameText.None),
            ResetterNpc => new NpcInteraction(true, id, "Mixer Rayon",
                [Offer(NpcServiceKind.QuestGiver, 1016, false), Offer(NpcServiceKind.MixtureReset, 0, true)], ["A mixed item and the stone."], GameText.None),
            _ => new NpcInteraction(true, id, "Skill Master, Verkan", [Offer(NpcServiceKind.QuestGiver, 1003, false), Offer(NpcServiceKind.SkillMaster, 4, true)],
                ["Welcome to the school of skills. Choose a skill to learn or improve.", "Choose a skill."], GameText.None)
        };
    }

    // Bellatra's show at a moment of its timeline: the boards and seal notices the server would send, then the HUD
    // stepped to that moment.
    private void Show(OwnerState state, ProbeBellatraShow show)
    {
        long[] scores = [31_200, 96_400, 12_050, 48_250];
        void Board(int round, Contracts.Social.BellatraOutcome outcome) => _hud.ObserveBellatra(new PresenceUpdate(1, [], [], [], [])
        {
            Bellatra = new BellatraBoardInfo(true, round, 1, 48_250, scores, outcome, StageTeams: 0b1111, GoingOn: 0b1010)
        });
        void Seal(GameTextId text) => _hud.ObserveBellatra(new PresenceUpdate(1, [], [], [], [])
        {
            Chat = [new ChatLineInfo(Contracts.Social.ChatKind.System, 0, "", GameText.Of(text, "Hammer"))]
        });
        void Wait(double seconds)
        {
            for (var step = 0; step < (int)(seconds * 20); step++)
            {
                _hud.Update(state, default, default, 0.05);
            }
        }

        switch (show)
        {
            case ProbeBellatraShow.Loading:
                Board(0, Contracts.Social.BellatraOutcome.Playing);
                Wait(0.6);
                break;
            case ProbeBellatraShow.Round or ProbeBellatraShow.Start:
                Board(2, Contracts.Social.BellatraOutcome.Playing);
                Board(3, Contracts.Social.BellatraOutcome.Playing);
                Wait(show == ProbeBellatraShow.Round ? 3.9 : 5.3);
                break;
            case ProbeBellatraShow.Complete or ProbeBellatraShow.Closed:
                Board(3, Contracts.Social.BellatraOutcome.Playing);
                Board(4, Contracts.Social.BellatraOutcome.GoesOn);
                Wait(show == ProbeBellatraShow.Complete ? 1.4 : 7);
                break;
            case ProbeBellatraShow.Fail:
                Board(3, Contracts.Social.BellatraOutcome.Playing);
                Board(3, Contracts.Social.BellatraOutcome.Out);
                Wait(1.4);
                break;
            case ProbeBellatraShow.Quake or ProbeBellatraShow.Stun or ProbeBellatraShow.Freeze or ProbeBellatraShow.Rabbit or ProbeBellatraShow.Points:
                Board(2, Contracts.Social.BellatraOutcome.Playing);
                Seal(show switch
                {
                    ProbeBellatraShow.Quake => GameTextId.BellatraQuake,
                    ProbeBellatraShow.Stun => GameTextId.BellatraStun,
                    ProbeBellatraShow.Freeze => GameTextId.BellatraFreeze,
                    ProbeBellatraShow.Rabbit => GameTextId.BellatraRabbit,
                    _ => GameTextId.BellatraPoints
                });
                Wait(0.6);
                break;
            case ProbeBellatraShow.Congratulations:
                Board(8, Contracts.Social.BellatraOutcome.Playing);
                Board(0, Contracts.Social.BellatraOutcome.Cleared);
                Wait(0.8);
                break;
        }
    }

    private ProbeQuest _quest;

    // The server's answer to the probe's quest talk.
    private QuestOutcome QuestReply(QuestCommand command)
    {
        var rewards = new[] { "Minotaur Axe", "Extreme Talon", "Dragon Bone Hammer", "Hellfire Scythe", "Revenge Bow", "Immortal Sword", "Salamander Javelin",
            "Gothic Staff", "Enigma Dagger", "Kahsehtha Phantom", "Arina Vambrace" };
        var documents = Enumerable.Range(1, 11).Select(index => $"haQuestFuryItem{index}").ToArray();
        QuestPrompt? prompt = _quest switch
        {
            ProbeQuest.Dialog or ProbeQuest.Confirm => QuestSteps.Confirm(QuestCode.Level30, 0, "LevelQuest30start", QuestTexts.SearchQuest),
            ProbeQuest.RewardList => new QuestPrompt(QuestCode.FuryOfPhantom, 12, "100LVQuest_7End", QuestFollowup.ChooseFromList)
            {
                Title = QuestTexts.FuryTitle,
                Choices = rewards,
                ChoiceDocuments = documents
            },
            ProbeQuest.RewardView => new QuestPrompt(QuestCode.FuryOfPhantom, 13, documents[1], QuestFollowup.ConfirmChoice)
            {
                Title = QuestTexts.FuryTitle,
                Choices = rewards,
                ChoiceDocuments = documents,
                Choice = 2
            },
            _ => null
        };
        return new QuestOutcome(true, QuestStatus.Ok, GameText.None, 1, prompt, []) { ClassChangeOffered = _quest == ProbeQuest.ClassChange };
    }

    private static QuestView QuestJournal(ProbeQuest quest) => quest switch
    {
        ProbeQuest.Window or ProbeQuest.WindowDescription => new QuestView(2, QuestCode.Level30, 1, "Bargon", 7, 20, QuestTimerKind.None, 0,
            [new QuestBoardEntry("For her", "LevelQuest\\Quest30.Msg"), new QuestBoardEntry("Rank up quest", "ChangeJob1a.Msg")],
            [new QuestBoardEntry("A tired warehouse keeper(Complete)", "HaQuest\\Quest_tired_1end.msg")]),
        ProbeQuest.Timer => new QuestView(3, QuestCode.RankUpMasterTrial, 1, "Muffin", 12, 50, QuestTimerKind.Expiring, 2_450_000,
            [new QuestBoardEntry("Job Quest 3th[1]", "Quest4\\ChangeJob4_1.msg")], []),
        ProbeQuest.SkillReset => new QuestView(4, QuestCode.None, 0, "", 0, 0, QuestTimerKind.None, 0, [], []) { SkillResetOffered = true },
        _ => new QuestView(1, QuestCode.None, 0, "", 0, 0, QuestTimerKind.None, 0, [], [])
    };

    private NpcServiceReply Reply(NpcServiceCommand command)
    {
        var ok = new NpcServiceOutcome(true, NpcServiceStatus.Ok, _owner.InventoryRevision, 1, GameText.None);
        if (command.Action != NpcServiceAction.Open)
        {
            return new NpcServiceReply(ok);
        }

        return command.Service switch
        {
            NpcServiceKind.Shop => new NpcServiceReply(ok)
            {
                Catalog =
                [
                    Stock("WA101", 0, 0, "Stone Axe", ItemKind.Axe, 60, 2, 3), Stock("WA102", 0, 1, "Bronze Axe", ItemKind.Axe, 320, 2, 3),
                    Stock("WC101", 0, 2, "Iron Claw", ItemKind.Claw, 150, 2, 2), Stock("WP101", 0, 3, "Spear", ItemKind.Pole, 280, 2, 4),
                    Stock("WS201", 0, 4, "Short Sword", ItemKind.Sword, 110, 1, 3), Stock("DA102", 1, 0, "Battle Suit", ItemKind.Armor, 400, 2, 3)
                ],
                Quotes = new ShopQuotes(_owner.InventoryRevision, _owner.Items.Select(item => new ShopQuote(item.InstanceId,
                    item.Slot >= 0 ? -1 : 12 * (int)item.Code, item.Slot >= 0 ? 8 : 0, 30, 36)).ToArray())
            },
            NpcServiceKind.Warehouse => new NpcServiceReply(ok)
            {
                Warehouse = new WarehouseView(1, 125000, 37, WarehouseStorage.MaxWeight, StoreGrid.Columns, StoreGrid.Rows,
                [
                    new(Guid.NewGuid(), ItemCodes.Parse("WA101"), "Stone Axe", ItemKind.Axe, 1, 2, -1, 2, 3, "WA101"),
                    new(Guid.NewGuid(), ItemCodes.Parse("DA102"), "Battle Suit", ItemKind.Armor, 1, 5, -1, 2, 3, "DA102"),
                    new(Guid.NewGuid(), ItemCodes.Parse("PL101"), "Mini Life Potion", ItemKind.Potion, 1, 27, -1, 1, 1, "PL101")
                ])
            },
            NpcServiceKind.Teleporter => new NpcServiceReply(ok)
            {
                Destinations = TeleportRoutes.For(1).Select(destination => new TeleportOffer(destination.Index, destination.FieldCode,
                    destination.Name, destination.Price, destination.Index == 4 ? 70 : 0)).ToArray()
            },
            _ => new NpcServiceReply(ok)
        };
    }

    private static ShopOffer Stock(string key, int tab, int ordinal, string name, ItemKind kind, long price, int width, int height) =>
        new(ItemCodes.Parse(key), tab, ordinal, name, kind, price, width, height, 1, key);

    private static ItemDetailView Detail(ItemRoll roll, int critical = 0, int attackSpeed = 0) =>
        new(54, 70, 0, roll, critical, attackSpeed, 0, 0, 0, 30, 0, false, true);

    private OwnerState ScenarioState(Scenario scenario)
    {
        if (scenario.FifthTier)
        {
            var skills = ClassSkillCatalog.For(scenario.Class);
            return _owner with
            {
                Level = 92,
                Tier = PlayerCharacter.LastTier,
                EliteSkillPoints = 3,
                Money = 10_000_000,
                Skills = skills.Select((skill, index) => new OwnedSkillView(skill.Code, index < 17 ? 10 - index % 4 : 0)).ToArray()
            };
        }

        if (scenario.Learned)
        {
            return _owner with
            {
                Level = 24,
                SkillPoints = 4,
                Skills = new OwnedSkillView[] { new(0x01010001, 3, 6000), new(0x01010002, 1, 2500), new(0x01010004, 1) }
            };
        }

        return scenario.LowHealth
            ? _owner with { Life = 1, Mana = 0, Stamina = 2 }
            : _owner with { Life = _owner.MaxLife * scenario.HealthPercent / 100, Experience = (_owner.LevelStart + _owner.NextLevel) / 2 };
    }
}

internal enum ProbeBellatraShow
{
    None,
    Loading,
    Round,
    Start,
    Complete,
    Fail,
    Closed,
    Quake,
    Stun,
    Freeze,
    Rabbit,
    Points,
    Congratulations
}

internal enum ProbeBellatraResult
{
    None,
    Final,
    Live
}

internal enum ProbeSiegeEnd
{
    None,
    Won,
    Ended
}
