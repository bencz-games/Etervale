using System.Diagnostics;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using PristonTale.Gameplay.Screens.Framework;
using PristonTale.Gameplay.UI.Chat;
using PristonTale.Gameplay.UI.HelpPet;
using PristonTale.Gameplay.UI.Inventory;
using PristonTale.Gameplay.UI.Npc;
using PristonTale.Gameplay.UI.Parties;
using PristonTale.Gameplay.UI.PersonalShops;
using PristonTale.Gameplay.UI.Quests;
using PristonTale.Gameplay.UI.Rendering;
using PristonTale.Gameplay.UI.Travel;
using PristonTale.Gameplay.UI.Widgets;
using PristonTale.Gameplay.World.Remote;
using PristonTale.Simulation.Characters;
using PristonTale.Simulation.Items;
using PristonTale.Simulation.Progression;
using PristonTale.Simulation.Quests;
using PristonTale.Simulation.World.Npcs;
using PristonTale.Contracts.Characters;
using PristonTale.Gameplay.Localization;
using PristonTale.Gameplay.UI.Clans;

namespace PristonTale.Gameplay.UI;

// The original in-game interface in its draw order: speech bubbles, the chat window, status, skill, inventory, quest and
// party windows behind the bottom bar, then item overlays, help boxes and message boxes. The system menu opens the
// settings, the help window or the game's exit. Refusals show in the notice strip; confirmations and server lines go to
// the chat.
public sealed partial class GameplayHud : IDisposable
{
    private readonly ScreenContext _context;
    private readonly HudSprites _sprites;
    private readonly HudPainter _painter;
    private readonly HudItemIcons _icons;
    private readonly Dictionary<Guid, string> _itemKeys = new();
    private readonly HudSkillBindings _bindings = new();
    private readonly HudBar _bar = new();
    private readonly HudGroundLabels _dropNames = new();
    private readonly HudStatusPanel _status = new();
    private readonly HudSkillsPanel _skills;
    private readonly HudSkillHud _skillHud;
    private static readonly CharacterClassRegistry SkillClasses = new();
    private PlayerClass _class;
    private IReadOnlyList<OwnedSkillView> _syncedSkills;
    private readonly HudInventoryPanel _inventoryPanel = new();
    private readonly HudInventoryController _inventory;
    private readonly HudProgressionController _progression;
    private readonly HudNpcPanel _npc;
    private readonly HudNpcServiceController _services;
    private readonly HudShopWindow _shop = new();
    private readonly HudCountBox _countBox = new();
    private readonly HudWarehouseWindow _warehouse = new();
    // StoredPlacement as a delegate made once, for the warehouse's per-frame update.
    private readonly Func<OwnedItemView, Point, HudPlacement> _storedPlacement;
    private readonly HudAgingWindow _aging = new();
    private readonly HudMixWindow _mix = new();
    private readonly HudSmeltingWindow _smelting = new();
    private readonly HudManufactureWindow _manufacture = new();
    private readonly HudMixtureResetWindow _mixtureReset = new();

    // The crafting windows, each open while its NPC service is.
    private readonly HudWorkbench[] _benches;
    // The service windows in draw order: bodies, then overlays, shop first.
    private readonly IHudServiceWindow[] _serviceWindows;
    private readonly HudQuestController _quests;
    private readonly HudQuestDocuments _questDocuments;
    private readonly HudQuestDialog _questDialog = new();
    private readonly HudQuestWindow _questWindow = new();
    private QuestView _questView;
    private double _questViewMs;
    // The quest weapon's hunt line for its item box, made when the quest view changes.
    private string _weaponHunt = "";
    private int _questCompletions;
    private int _questDialogs;
    private readonly HudSettingsPanel _settings;
    private readonly HudMinimap _minimap = new();
    private readonly List<Vector3> _minimapNpcs = new(64);
    private readonly HudTargetPortrait _portrait;
    private readonly HudNotice _notice = new();
    private readonly HudChat _chat = new();
    private readonly HudSlide _inventorySlide = new();
    private readonly HudSlide _statusSlide = new();
    private readonly HudSlide _skillSlide = new();
    private readonly HudItemUseController _itemUse;
    private readonly HudPremiumTimers _premium;
    private readonly HudPremiumKeeps _premiumKeeps = new();
    private readonly HudPuzzleExchange _puzzle = new();
    private readonly HudCrystalDismiss _crystalDismiss = new();
    private readonly HudRestartPanel _restart;
    private readonly HudWingGate _wingGate;
    private int _itemUseCompletions;
    private int _wingGateCompletions;
    private int _shopsOpened;
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private readonly HudQuestBoxGlow _questBoxGlow = new();
    // Delegates the frame passes, made once: the speakers' name colour, a typing box's key test (on this frame's keys)
    // and the painter's glyph and width tests.
    private Func<ulong, Color> _nameColor;
    private Func<Keys, bool> _typedKey;
    private KeyboardState _typingKeyboard;
    private Func<char, bool> _canDraw;
    private Func<string, int> _measure;
    private KeyboardState _previousKeyboard;
    private MouseState _previousMouse;
    private OwnerState _state;
    private HudMenuButton? _hoveredButton;
    private HudToggle? _hoveredToggle;
    private ulong _skillMaster;
    private bool _systemMenu;
    private bool _settingsOpen;
    private bool _npcOpen;
    private bool _mapVisible = true;
    private bool _ready;
    private int _inventoryCompletions;
    private int _progressionCompletions;
    private int _serviceCompletions;
    private int _bindingRefusals;
    private int _serviceOpened;
    private int _hoveredTeleportFieldCode = -1;
    private double _hoveredTeleportSince;
    private bool _teleportFieldPrepared;
    private double _lastUpdate;
    private readonly Action<int> _prepareField;

    public GameplayHud(ScreenContext context, Func<InventoryCommand, Task<InventoryOutcome>> applyInventory,
        Func<ProgressionCommand, Task<ProgressionOutcome>> applyProgression = null,
        Func<ulong, Task<NpcInteraction>> interactNpc = null,
        Action<NpcInteraction> npcOpened = null,
        Action npcClosed = null,
        Func<NpcServiceCommand, Task<NpcServiceReply>> requestNpcService = null,
        Action<int> prepareField = null,
        Func<QuestCommand, Task<QuestOutcome>> requestQuest = null)
    {
        _context = context;
        _clanWindow = new HudClanWindow(context.Log);
        _castleWindow.AskAmount = AskWithdrawal;
        _bellatraWindow.AskAmount = AskRedemption;
        _settings = new HudSettingsPanel(context.Presentation);
        _sprites = new HudSprites(context.GraphicsDevice, context.AssetRoot, context.Log);
        _sprites.Request(HudArt.Static);
        _sprites.Request(HudNpcArt.All);
        _sprites.Request(HudCraftArt.All);
        _sprites.Request(HudArt.BlockText);
        _sprites.Request(HudQuestArt.All);
        _sprites.Request(Bellatra.HudBellatraResult.Art);
        SetUpBellatraShow();
        _painter = new HudPainter(context.SpriteBatch, context.Font, _sprites);
        _icons = new HudItemIcons(context.GraphicsDevice, context.AssetRoot, context.Log);
        _personalShop = new HudPersonalShopController(context.Log) { Notify = _notice.Show, AskCount = (first, second, maximum, accepted) => _countBox.Open(first, second, maximum, accepted) };
        _benches = [_aging, _mix, _smelting, _manufacture, _mixtureReset];
        _serviceWindows = [_shop, _warehouse, .. _benches, _personalShop.Window, _trade];
        _sprites.Request(HudPersonalShopArt.All);
        _skills = new HudSkillsPanel(context.Font, _sprites);
        _skillHud = new HudSkillHud(_sprites, context.GraphicsDevice);
        _inventory = new HudInventoryController(context, applyInventory);
        _progression = new HudProgressionController(applyProgression, context.Log);
        _npc = new HudNpcPanel(interactNpc, context.Log, npcOpened, npcClosed);
        _portrait = new HudTargetPortrait(context.GraphicsDevice);
        _services = new HudNpcServiceController(requestNpcService, context.Log);
        _prepareField = prepareField;
        _itemUse = new HudItemUseController(UseItemAsync, context.Log);
        _restart = new HudRestartPanel(context.Log);
        _premium = new HudPremiumTimers(_sprites);
        _sprites.Request(HudTeleportCore.Art);
        _wingGate = new HudWingGate(context.Log);
        _sprites.Request(HudWingGate.Art);
        _inventory.UseRequested = item => _itemUse.Request(item, _state);
        _quests = new HudQuestController(requestQuest, context.Log);
        _inventory.QuestItemUse = _quests.UseItem;
        _questDocuments = new HudQuestDocuments(context.AssetRoot, context.Log);
        _inventory.InterceptClick = InterceptInventoryClick;
        _inventory.ExternalDrop = DropFromInventory;
        _inventory.ThrowRequested = ThrowFromInventory;
        _inventory.GoldRequested = GoldFromInventory;
        // The special item box opens and closes but not while a service window is open, which closes it.
        _inventory.QuestBoxRequested = () =>
        {
            if (!ServiceWindowOpen(blockingOnly: false))
            {
                _inventory.ToggleQuestBox();
            }
        };
        _inventory.ItemSound = key => ItemSound?.Invoke(key);
        _inventory.QuestBoxStored = frame =>
        {
            _questBoxGlow.Start(frame, _clock.Elapsed.TotalMilliseconds);
            QuestFanfare?.Invoke();
        };
        _services.Succeeded = ServiceSucceeded;
        _storedPlacement = StoredPlacement;
        _inventoryPanel.Note = InventoryNote;
        _inventoryPanel.Hunt = item => item.Code == QuestClasses.QuestWeapon(_class) ? _weaponHunt : "";
        _inventoryPanel.Withheld = id => BenchHolds(id) || _personalShop.Holds(id) || _trade.Holds(id);
        _inventory.ShopRequested = OpenOwnShop;
        _sprites.Request(HudChatWindow.Art);
        _sprites.Request(HudChatBubbles.Art);
        _chat.Lookup = id => (Combat.KindOf(id, out var owner), owner);
        _helpPet = new HudHelpPet(HelpPetMessages.LoadAsync(context.AssetRoot, context.Log, ClientText.Language), new Random(), context.Log,
            language => HelpPetMessages.LoadAsync(context.AssetRoot, context.Log, language));
        _sprites.Request(HudHelpPet.Art);
        SetUpParty();
        SetUpTrade();
        SetUpContacts();
        SetUpClans();
        SetUpHelp();
    }

    // The chat window, its input line and the speech bubbles.
    public HudChat Chat => _chat;

    // The chat part of each presence update (server lines, whispers, notices, the close request).
    public void ObserveChat(PristonTale.Client.Net.Presence.PresenceUpdate update) => _chat.Observe(update, _clock.Elapsed.TotalMilliseconds);

    // The input line has the keyboard: game keys must not act on typed letters.
    public bool KeyboardCaptured => _chat.KeyboardCaptured;

    // Where a speaker's bubble hangs on screen, from the scene; null when the speaker is not drawn.
    public Func<ulong, HudOverheadPoint?> Overhead { get; set; } = static _ => null;

    public HudCombat Combat { get; } = new();
    public HudStringEffect StringEffect { get; } = new();

    // Uses a carried item through the server; set by the screen.
    public Func<PristonTale.Simulation.Items.Usable.ItemUseCommand, Task<PristonTale.Simulation.Items.Usable.ItemUseOutcome>> UseItem { get; set; }

    // The local player's running premium timers as the server last sent them.
    public PristonTale.Client.Net.Presence.PremiumTimerSnapshot PremiumTimers { get; set; }
    public ulong TargetId { get; set; }
    // The entity under the cursor and the clicked one, for the name boxes over the scene.
    public ulong HoveredId { get; set; }
    public ulong TracedId { get; set; }
    // The drop names to draw under the entity names, and where each one landed (back-buffer pixels) for clicks on it.
    public IReadOnlyList<GroundLabel> GroundLabels { get; set; } = [];
    public Action<Guid, Rectangle> GroundLabelPlaced { get; set; }
    public HudWorldView World { get; set; }

    // The player stands in a castle field, where a Restoration revives.
    public bool InCastleField { get; set; }

    // The local player is down, and what standing up would cost (from the server).
    public bool PlayerDead { get; set; }
    public bool PlayerActing { get; set; }
    public long PlayerActionStarts { get; set; }
    public RestartOffer? RestartOffer { get; set; }

    // The restart window; the screen sets its request and quits on its exit choice.
    public HudRestartPanel Restart => _restart;

    // The wing warp map a warp stone opens; the screen opens it and sets its trip.
    public HudWingGate WingGate => _wingGate;
    public IHudPortraitScene PortraitScene { get; set; }
    public bool PortraitReady => _portrait.Texture(TargetId) is not null;
    public float MinimapViewSize => _minimap.CurrentViewSize;
    public bool PointerCaptured { get; private set; }
    public bool AssetsFailed => _sprites.Failed || _icons.Failed;
    public bool IconsIdle => _icons.IsIdle && _sprites.IsIdle && !_questDocuments.IsLoading && !_helpTexts.IsLoading;
    public long TextureBytes => _sprites.TextureBytes;
    public bool RightAttackEnabled => _bindings.RightSkillId == HudSkillBindings.BasicAttackId;
    internal uint LeftSkillId => _bindings.LeftSkillId;
    internal HudItemUseController ItemUse => _itemUse;
    internal HudPuzzleExchange Puzzle => _puzzle;
    internal uint RightSkillId => _bindings.RightSkillId;
    public HudSkillBindings Bindings => _bindings;

    // The local player's statuses and cooldowns from the server's skill messages, and the clock they are kept on
    // (GameScene.Skills).
    public PristonTale.Gameplay.Effects.StatusBook Statuses { get; set; }
    public PristonTale.Gameplay.Effects.SkillCooldownBook Cooldowns { get; set; }
    public double SkillClockMs { get; set; }
    public ulong SelfId { get; set; }

    // A double click on a summon's buff icon asks to switch the summon's fighting mode.
    public event Action<uint> SummonModeRequested;

    // The owner confirmed dismissing the pet of one status (the code of its icon).
    public event Action<uint> CrystalPetDismissRequested;

    // R or the bar's walk/run button.
    public event Action RunToggleRequested;

    // Quit in the exit button's menu.
    public event Action ExitRequested;

    public event Action YahooKeyPressed;

    // The fighting mode the player chose for a running summon skill (true: automatic), or null for other skills.
    public Func<uint, bool?> SummonAutomatic { get; set; }
    private uint _lastBuffClicked;
    private double _lastBuffClickSeconds;

    // The quest fanfare (the drink sound): a quest started, moved on or ended, or a carried item set into the closed
    // special item box.
    public event Action QuestFanfare;

    public event Action<bool> CraftResult;

    // An item lifted, set, bought, sold, stored or taken back, by catalog key, for its sound.
    public event Action<string> ItemSound;

    // The quest part of each presence update (the journal, quest notices and dialogs the world opened).
    public void ObserveQuests(PristonTale.Client.Net.Presence.PresenceUpdate update) => _quests.Observe(update);

    public bool IsReady
    {
        get
        {
            _ready = _ready || HudArt.Static.All(_sprites.IsResolved);
            return _ready;
        }
    }

    public PlayerClass CharacterClass
    {
        set
        {
            _class = value;
            _syncedSkills = null;
            _skillHud.SetClass(value);
            _skills.SetClass(value);
            _status.ClassName = DataText.ClassTitle(value, 0);
            _status.Invalidate();
        }
    }

    public string CharacterName
    {
        get => _status.Name;
        set
        {
            _status.Name = value;
            _status.Invalidate();
        }
    }

    public void OpenNpc(ulong entityId) => _npc.Request(entityId);

    // A ground item picked up with the inventory open comes to its cursor (HudInventoryController.ExpectPickup).
    public bool ExpectPickup(uint itemCode) => _inventory.ExpectPickup(itemCode);

    public void PickupAnswered(bool ok, long revision) => _inventory.PickupAnswered(ok, revision);

    public void OpenSettings()
    {
        _systemMenu = false;
        _settingsOpen = true;
    }

    public bool ClosePanel()
    {
        if (_countBox.IsOpen)
        {
            _countBox.Close();
            return true;
        }

        if (_personalShop.ModalOpen)
        {
            return _personalShop.Close();
        }

        // As in the original, Escape first closes an open chat window and nothing else.
        if (_chat.Window.Open && !_settingsOpen)
        {
            _chat.Window.Close();
            return true;
        }

        _npc.Dismiss();
        var closed = _clanWindow.IsOpen || _castleWindow.IsOpen || _siegeResult.IsOpen || _bellatraWindow.IsOpen || _bellatraResult.IsOpen || _npcOpen || _settingsOpen || _systemMenu || _skills.TrainingPending
            || _inventorySlide.Open || _statusSlide.Open || _skillSlide.Open || ServiceWindowOpen(blockingOnly: false)
            || _questWindow.Open || _partyWindow.Open || _quests.Dialog is not null || _quests.SkillResetBox;
        closed |= _personalShop.Close();
        CloseServices();
        closed |= _itemUse.Teleport.IsOpen || _itemUse.Confirming || _puzzle.Confirming || _crystalDismiss.Confirming || _wingGate.IsOpen || _wingGate.Confirming
            || _helpWindow.IsOpen;
        _helpWindow.Close();
        _clanWindow.Close();
        _castleWindow.Close();
        _siegeResult.Close();
        _bellatraWindow.Close();
        _bellatraResult.Close();
        _itemUse.Close();
        _wingGate.Close();
        _puzzle.Close();
        _crystalDismiss.Close();
        _quests.CloseDialog();
        _quests.AnswerSkillReset(false);
        _quests.LeaveNpc();
        _questWindow.Close();
        _partyWindow.Close();
        _npcOpen = false;
        _settingsOpen = false;
        _systemMenu = false;
        _inventorySlide.Open = false;
        _statusSlide.Open = false;
        _skillSlide.Open = false;
        _skillMaster = 0;
        _skills.CancelTraining();
        _inventory.CancelDrag();
        return closed;
    }

    public void Update(OwnerState state)
    {
        var now = _clock.Elapsed.TotalSeconds;
        var elapsed = now - _lastUpdate;
        _lastUpdate = now;
        Update(state, Keyboard.GetState(), Mouse.GetState(), elapsed);
    }

    internal void Update(OwnerState state, KeyboardState keyboard, MouseState mouse, double elapsedSeconds = 1)
    {
        _sprites.Update();
        _icons.Update();
        _progression.Update();
        _services.Update();
        _itemUse.Update();
        _wingGate.Update();
        _itemUse.Restore(state, InCastleField, _clock.Elapsed.TotalMilliseconds);
        _restart.Update(PlayerDead, _clock.Elapsed.TotalMilliseconds);
        _premium.Apply(PremiumTimers, _clock.Elapsed.TotalMilliseconds);
        _quests.Update();
        _npc.Update(_context.Font);
        _minimap.Update(_sprites, World, elapsedSeconds);
        _notice.Update(elapsedSeconds);
        StringEffect.Update((float)elapsedSeconds);
        _inventorySlide.Advance(elapsedSeconds);
        _statusSlide.Advance(elapsedSeconds);
        _skillSlide.Advance(elapsedSeconds);
        foreach (var window in _serviceWindows)
        {
            window.Advance(elapsedSeconds);
        }

        _questWindow.Advance(elapsedSeconds);
        _partyWindow.Advance(elapsedSeconds);
        _clanWindow.Update();
        _partyRequest.Update(_clock.Elapsed.TotalMilliseconds);
        _clanRequest.Update(_clock.Elapsed.TotalMilliseconds);
        _tradeRequest.Update(_clock.Elapsed.TotalMilliseconds);
        UpdateContacts(_clock.Elapsed.TotalMilliseconds);
        _bellatraResult.Update(_clock.Elapsed.TotalMilliseconds, _npcOpen);
        _bellatraShow?.Update(elapsedSeconds);
        ForwardMessages();
        ApplyQuests();
        ApplyServices(state);
        _personalShop.SignShown = SelfId != 0 && ShopSigns.ContainsKey(SelfId);
        _personalShop.Update(state, _clock.Elapsed.TotalMilliseconds);
        if (_npc.ReadyToOpen)
        {
            _skillMaster = 0;
            _skills.CancelTraining();
            _settingsOpen = false;
            CloseServices();
            // While the previous window's close is still in flight the dialog shows, and its button opens the service.
            var direct = _npc.DirectService;
            _npcOpen = direct is null || !_services.Request(_npc.Current.EntityId, _npc.Current.Name, direct.Kind, NpcServiceAction.Open);
            _quests.LeaveNpc();
            if (_npc.TakesPartInQuests)
            {
                _quests.Talk(_npc.Current.EntityId);
            }
        }

        var layout = Layout();
        HudMessageBox.Place(layout);
        var pointer = layout.ToLogical(mouse.Position);
        var design = new Point(pointer.X, pointer.Y - layout.BottomOffset);
        UpdateTeleportPrefetch(pointer);
        if (_quests.Dialog is not null && HudQuestDialog.Box.Contains(pointer))
        {
            _questDialog.Scroll(mouse.ScrollWheelValue - _previousMouse.ScrollWheelValue);
        }
        else if (_clanWindow.Contains(pointer))
        {
            _clanWindow.Scroll(mouse.ScrollWheelValue - _previousMouse.ScrollWheelValue);
        }
        else if (_partyWindow.Page == HudPartyPage.Clan && _partyWindow.Contains(layout, pointer))
        {
            _partyWindow.Clan.Scroll(mouse.ScrollWheelValue - _previousMouse.ScrollWheelValue);
        }
        else if (_npcOpen && HudNpcPanel.Box.Contains(pointer))
        {
            _npc.Scroll(mouse.ScrollWheelValue - _previousMouse.ScrollWheelValue);
        }
        else if (mouse.ScrollWheelValue != _previousMouse.ScrollWheelValue && _helpWindow.Contains(pointer))
        {
            _helpWindow.Scroll(mouse.ScrollWheelValue - _previousMouse.ScrollWheelValue);
        }

        if (state is not null && (_state is null || state.InventoryRevision != _state.InventoryRevision))
        {
            RequestIcons(state);
            if (_trade.IsOpen)
            {
                _trade.Follow(state.Items);
            }
        }

        _state = state;
        if (state is not null && !ReferenceEquals(state.Skills, _syncedSkills))
        {
            _syncedSkills = state.Skills;
            _bindings.SyncLearned(SkillClasses.Get(_class).Skills, state.Skills);
        }

        if (_context.Presentation.AwaitingConfirmation)
        {
            PointerCaptured = true;
            _previousKeyboard = keyboard;
            _previousMouse = mouse;
            return;
        }

        _hoveredButton = ButtonAt(design);
        _hoveredToggle = ToggleAt(design);
        UpdateHelpPet(state, layout, pointer, design, mouse, _clock.Elapsed.TotalMilliseconds);
        UpdateHelp(state);
        var clicked = mouse.LeftButton == ButtonState.Pressed && _previousMouse.LeftButton == ButtonState.Released;
        // A click the HUD takes stays its own when it closes the window under it (a close button over the ground).
        var clickTaken = clicked && Captures(layout, pointer, design);
        var typedText = _context.TypedText.Drain();
        // A count box or the shop's sign box takes the keyboard: neither the chat nor a game key sees what is typed there.
        var boxTyping = _countBox.IsOpen || _personalShop.SignOpen || _clanWindow.Typing;
        if (_countBox.IsOpen)
        {
            _typingKeyboard = keyboard;
            _countBox.Type(typedText, _typedKey ??= key => Pressed(_typingKeyboard, key));
        }
        else if (_personalShop.SignOpen)
        {
            _typingKeyboard = keyboard;
            _personalShop.Type(typedText, _typedKey ??= key => Pressed(_typingKeyboard, key), _canDraw ??= _painter.CanDraw, _measure ??= _painter.Measure);
        }
        else if (_clanWindow.Typing)
        {
            _typingKeyboard = keyboard;
            _clanWindow.Type(typedText, _typedKey ??= key => Pressed(_typingKeyboard, key), _canDraw ??= _painter.CanDraw);
        }
        else if (_crystalDismiss.Confirming && !_chat.Window.Focused && Pressed(keyboard, Keys.Enter))
        {
            // Enter answers Yes to the dismiss question instead of opening the chat input.
            RequestCrystalPetDismiss(_crystalDismiss.Accept());
        }
        else if (_skills.TrainingPending && !_chat.Window.Focused && Pressed(keyboard, Keys.Enter))
        {
            _skills.AcceptTraining(state, _skillMaster, _progression);
        }
        else if (_quests.SkillResetBox && !_chat.Window.Focused && Pressed(keyboard, Keys.Enter))
        {
            _quests.AnswerSkillReset(true);
        }
        else
        {
            _chat.Update(keyboard, _previousKeyboard, typedText, _clock.Elapsed.TotalMilliseconds, _chat.Window.Log.Tab);
        }

        var typing = boxTyping || _chat.KeyboardCaptured;
        _inventory.SidesLocked = _warehouse.IsOpen && (_inventory.Dragging != Guid.Empty || _warehouse.Dragging != Guid.Empty);
        if (!typing)
        {
            UpdateKeys(keyboard);
        }

        var workLocked = _services.Pending || _settingsOpen || ModalOpen;
        // A click that sets a warehouse item down in the bag is not also a pick-up there.
        var storedCarried = _warehouse.Dragging != Guid.Empty;
        var warehouseAction = _warehouse.Update(mouse, _previousMouse, pointer, _storedPlacement, workLocked, _inventory.Dragging != Guid.Empty);
        HudWorkbench actedBench = null;
        var benchAction = HudWorkActionKind.None;
        foreach (var bench in _benches)
        {
            var action = bench.Update(mouse, _previousMouse, pointer, state, workLocked);
            if (actedBench is null && action != HudWorkActionKind.None)
            {
                (actedBench, benchAction) = (bench, action);
            }
        }

        if (warehouseAction.Kind != HudWarehouseActionKind.None)
        {
            ApplyWarehouseAction(state, warehouseAction);
        }
        else if (actedBench is not null)
        {
            ApplyWorkAction(actedBench, state, benchAction);
        }
        else if (clicked)
        {
            Click(state, layout, pointer, design, keyboard);
        }

        // A right click on a crystal pet's icon in the running-effects bar asks whether to dismiss the pet.
        if (mouse.RightButton == ButtonState.Pressed && _previousMouse.RightButton == ButtonState.Released && !ModalOpen && !_settingsOpen)
        {
            _crystalDismiss.Offer(_skillHud.BuffAt(pointer));
        }
        else if (_crystalDismiss.Confirming && !_skillHud.Shows(_crystalDismiss.StatusCode))
        {
            // The pet went (expired, died, or its owner entered a town) while its question was open.
            _crystalDismiss.Close();
        }

        var skillOrigin = PanelOrigin(layout, _skillSlide);
        _skills.RefreshRanks(_state);
        var hoveredSkill = _skillSlide.FullyOpen && _skillMaster == 0 ? _skills.HoveredSkill(skillOrigin, pointer) : 0;
        _bindings.Update(typing ? default : keyboard, mouse, hoveredSkill);
        PointerCaptured = clickTaken || Captures(layout, pointer, design) || _chat.Window.Contains(layout, pointer, SideWindowOpen) || _inventory.Dragging != Guid.Empty || _warehouse.Dragging != Guid.Empty;
        _inventory.PickupLocked = PlayerActing || storedCarried;
        _inventory.ActionStarts = PlayerActionStarts;
        _inventory.Update(state, layout, _inventorySlide.FullyOpen && !_settingsOpen && !ModalOpen, keyboard, mouse);
        _previousKeyboard = keyboard;
        _previousMouse = mouse;
    }

    // A status, skill or inventory window shrinks the chat window above it.
    private bool SideWindowOpen => _inventorySlide.Visible || _statusSlide.Visible || _skillSlide.Visible;

    private long QuestTimeLeftMs => _questView is null ? 0 : Math.Max(0, _questView.TimerMs - (long)(_clock.Elapsed.TotalMilliseconds - _questViewMs));

    // The pointer the HUD imposes, or null to let the scene choose: the arrow over a dialog or message box, the shop
    // mode's pointer while a shop is open, none while an item is dragged.
    public HudCursorKind? CursorOverride =>
        ModalOpen ? HudCursorKind.Default
        : _shop.IsOpen ? _shop.Mode switch { HudShopMode.Sell => HudCursorKind.Sell, HudShopMode.Repair => HudCursorKind.Repair, _ => HudCursorKind.Buy }
        : _inventory.Dragging != Guid.Empty || _warehouse.Dragging != Guid.Empty ? HudCursorKind.Hidden
        : null;

    // A dialog, a message box or the warp map: it takes every click, the bag's included.
    private bool ModalOpen =>
        _countBox.IsOpen || _personalShop.ModalOpen || _npcOpen || _clanWindow.IsOpen || _castleWindow.IsOpen || _bellatraWindow.IsOpen || _quests.Dialog is not null || _quests.Confirming || _quests.SkillResetBox || _skills.TrainingPending
        || _wingGate.IsOpen || _wingGate.Confirming || _itemUse.Teleport.IsOpen || _itemUse.Confirming || _puzzle.Confirming
        || _crystalDismiss.Confirming;

    // As in the original, a modal or a work window keeps every click from the world (the work windows still trade items
    // with the bag); the status, inventory, skill and shop windows keep only their own area.
    private bool BlocksWorld => ModalOpen || ServiceWindowOpen(blockingOnly: true);

    // As in the original, the player stands still while it deals with an NPC (its dialog, a service window, the skill
    // master) or a modal: a click into the world neither walks nor acts, and a walk in progress stops.
    public bool HoldsPlayer => ModalOpen || ServiceWindowOpen(blockingOnly: false) || _skillMaster != 0 || _personalShop.Selling;

    private bool MinimapShown => _mapVisible && !World.MapHidden;

    // Renders the target frame's 3D portrait into its own target. Call before the world and HUD draw of the frame: binding
    // another target discards the back buffer.
    public void RenderPortrait()
    {
        var subject = TargetId != 0 && Combat.TryGetPortraitSubject(TargetId, out var target) ? target : default;
        _portrait.Render(PortraitScene, subject, Layout());
    }

    public void Draw()
    {
        var layout = Layout();
        HudMessageBox.Place(layout);
        var pointer = layout.ToLogical(_previousMouse.Position);
        var pressed = _previousMouse.LeftButton == ButtonState.Pressed;
        _painter.Begin(layout);
        _dropNames.Draw(_painter, layout, GroundLabels, GroundLabelPlaced);
        Combat.DrawNamePlates(_painter, layout, Overhead, HoveredId, TracedId, _context.Presentation.Requested.ClassicNamePlates);
        _chat.Bubbles.Signs = ShopSigns;
        _chat.Bubbles.Draw(_painter, layout, _clock.Elapsed.TotalMilliseconds, Overhead, _nameColor ??= id => _chat.NameColor(id, TargetId), TargetId);
        if (_helpPet.Bubble is { } petLine && HelperPet.Point is { } petPoint)
        {
            _chat.Bubbles.DrawAt(_painter, layout, petLine, petPoint);
        }

        _painter.Flush();
        if (MinimapShown)
        {
            Combat.CollectNpcPositions(_minimapNpcs);
            _minimap.Draw(_painter, layout, World, _minimapNpcs, MinimapParty(), HudMinimap.Box(layout).Contains(pointer));
        }

        Combat.DrawTarget(_painter, layout, TargetId, _context.Presentation.Requested.ClassicTargetHealthBar, _portrait.Texture(TargetId),
            ShowsPlayerButtons);
        if (ShowsPlayerButtons)
        {
            HudPlayerButtons.Draw(_painter, HudCombat.TargetFrame(layout), pointer, ClanButton);
        }
        _premium.Draw(_painter, _clock.Elapsed.TotalMilliseconds);
        _siegeBoard.Draw(_painter, layout, InCastleField, _clock.Elapsed.TotalMilliseconds, Combat.OwnClan);
        _bellatraBoard.Draw(_painter, layout);
        _personalShop.Draw(_painter);
        _chat.Window.Draw(_painter, layout, SideWindowOpen);

        if (_statusSlide.Visible)
        {
            _status.ClanName = Clan.Name;
            _status.Draw(_painter, PanelOrigin(layout, _statusSlide), _state, pointer, pressed, _statusSlide.FullyOpen);
        }

        if (_skillSlide.Visible)
        {
            _skills.Draw(_painter, PanelOrigin(layout, _skillSlide), pointer, _state, _bindings, _skillMaster, pressed, _skillSlide.FullyOpen,
                Cooldowns, SkillClockMs);
        }

        if (_inventorySlide.Visible)
        {
            _inventoryPanel.ClockMs = _clock.Elapsed.TotalMilliseconds;
            _inventoryPanel.Draw(_painter, PanelOrigin(layout, _inventorySlide), _state, _inventory, _icons, pointer, _inventorySlide.FullyOpen,
                _warehouse.Carried);
        }
        else
        {
            _inventoryPanel.TrackHeld(_state, _inventory, pointer);
        }

        if (_questWindow.Visible)
        {
            _questWindow.Draw(_painter, layout, _context.Font, _questView, _questDocuments, pointer);
        }

        _partyWindow.Draw(_painter, layout, Party, SelfId, pointer);

        var incoming = OwnedItemView.Find(_state?.Items, _inventory.Dragging);
        var serviceFrame = new HudServiceFrame(_painter, layout, _icons, _state, pointer, incoming, _settingsOpen, _clock.Elapsed.TotalMilliseconds);
        foreach (var window in _serviceWindows)
        {
            if (window.Visible)
            {
                window.Draw(serviceFrame);
            }
        }

        _painter.Flush();
        var left = _bindings.LeftSkillId == HudSkillBindings.BasicAttackId;
        _bar.Draw(_painter, layout, _state, new HudBarView(World.TimeOfDay, World.Running, _mapVisible, left, RightAttackEnabled,
            _hoveredButton, pressed && _hoveredButton is not null, _hoveredToggle));
        _inventoryPanel.DrawBelt(_painter, HudInventoryLayout.Origin(layout), _state, _inventory, _icons);
        _skillHud.DrawSockets(_painter, layout.BottomOffset, _bindings.LeftSkillId, _bindings.RightSkillId, Cooldowns, SkillClockMs);
        if (Statuses is not null && SelfId != 0)
        {
            _skillHud.DrawBuffs(_painter, _premiumKeeps.With(Statuses.For(SelfId), PremiumTimers, SkillClockMs), SkillClockMs);
        }
        else
        {
            _skillHud.ClearBuffs();
        }

        _skillHud.DrawQuestTimer(_painter, _questView, QuestTimeLeftMs);

        StringEffect.Draw(_painter, layout);
        _bar.DrawHoverText(_painter, layout, _state, pointer);
        if (_systemMenu)
        {
            HudSystemMenu.Draw(_painter, layout, pointer);
        }

        _painter.Flush();
        _helpPet.Draw(_painter, pointer);
        DrawHelp(pointer);

        var trainer = _skillSlide.FullyOpen ? _skillMaster : 0;
        _skills.DrawMasterBox(_painter, _state, trainer, trainer != 0 && _quests.ClassChangeNpc == trainer);

        // The quest dialog takes the help box from the NPC dialog; its yes/no box replaces both, as the classic client closed
        // the help box before asking.
        if (_quests.Dialog is not null)
        {
            if (!_quests.Confirming)
            {
                _painter.Flush();
                _questDialog.Draw(_painter, _context.Font, _quests, _questDocuments, pointer);
            }
        }
        else if (_clanWindow.IsOpen)
        {
            _painter.Flush();
            _clanWindow.Draw(_painter, _context.Font, pointer);
        }
        else if (_castleWindow.IsOpen)
        {
            _painter.Flush();
            _castleWindow.Draw(_painter, pointer);
        }
        else if (_bellatraWindow.IsOpen)
        {
            _painter.Flush();
            _bellatraWindow.Draw(_painter, pointer);
        }
        else if (_npcOpen)
        {
            _painter.Flush();
            _npc.Draw(_painter, pointer);
        }

        if (_bellatraResult.IsOpen)
        {
            _painter.Flush();
            _bellatraResult.Draw(_painter, pointer);
        }

        if (SiegeResultShown)
        {
            _painter.Flush();
            _siegeResult.Draw(_painter, pointer);
        }

        if (_itemUse.Teleport.IsOpen || _itemUse.Confirming)
        {
            _painter.Flush();
            _itemUse.Draw(_painter, pointer);
        }

        if (_wingGate.IsOpen || _wingGate.Confirming)
        {
            _painter.Flush();
            _wingGate.Draw(_painter, _context.Font, _questDocuments, _state, pointer, _clock.Elapsed.TotalMilliseconds);
        }

        _puzzle.Draw(_painter, pointer);
        _crystalDismiss.Draw(_painter, pointer);
        if (_personalShop.ModalOpen)
        {
            _painter.Flush();
            _personalShop.DrawModal(_painter, pointer);
        }

        if (_countBox.IsOpen)
        {
            _painter.Flush();
            _countBox.Draw(_painter, pointer);
        }

        if (_settingsOpen)
        {
            _painter.Flush();
            _settings.Draw(_painter, pointer);
        }

        foreach (var window in _serviceWindows)
        {
            if (window.FullyOpen)
            {
                window.DrawOverlay(serviceFrame);
            }
        }

        if (_inventorySlide.FullyOpen || _inventory.Dragging != Guid.Empty)
        {
            _inventoryPanel.DrawOverlay(_painter, layout, _icons, pointer);
        }

        _questBoxGlow.Draw(_painter, _clock.Elapsed.TotalMilliseconds);

        if (_skillSlide.FullyOpen)
        {
            _skills.DrawOverlay(_painter, layout, pointer);
        }

        if (Statuses is not null && SelfId != 0)
        {
            _skillHud.DrawBuffTooltip(_painter, _premiumKeeps.With(Statuses.For(SelfId), PremiumTimers, SkillClockMs), pointer, SkillClockMs,
                SummonAutomatic);
        }

        _skillHud.DrawQuestTimerTooltip(_painter, _questView, QuestTimeLeftMs, pointer);
        if (_quests.Confirming)
        {
            _painter.Flush();
            HudQuestDialog.DrawConfirm(_painter, _quests, pointer);
        }
        else if (_quests.SkillResetBox)
        {
            _painter.Flush();
            HudQuestDialog.DrawBox(_painter, ClientText.Format(QuestTexts.SkillPointName), ClientText.Format(QuestTexts.Redistribute.Item1),
                ClientText.Format(QuestTexts.Redistribute.Item2), pointer);
        }

        _painter.Flush();
        _premium.DrawHover(_painter, pointer, _clock.Elapsed.TotalMilliseconds);
        _painter.Flush();
        _partyRequest.Draw(_painter, pointer, _clock.Elapsed.TotalMilliseconds);
        _clanRequest.Draw(_painter, pointer, _clock.Elapsed.TotalMilliseconds);
        _tradeRequest.Draw(_painter, pointer, _clock.Elapsed.TotalMilliseconds);
        _restart.Draw(_painter, layout, pointer, RestartOffer?.FieldOpen == true, InCastleField);
        _painter.Flush();
        _notice.Draw(_painter, layout);
        _painter.End();
        DrawBellatraShow();
    }

    private HudLayout Layout() => HudLayout.Of(_context.GraphicsDevice.Viewport);

    private static Point PanelOrigin(HudLayout layout, HudSlide slide) => HudInventoryLayout.Origin(layout, slide.Offset);

    private static bool WindowContains(HudLayout layout, HudSlide slide, Point pointer) =>
        slide.Visible && HudInventoryLayout.Window(PanelOrigin(layout, slide)).Contains(pointer);

    public void Dispose()
    {
        _portrait.Dispose();
        _skillHud.Dispose();
        _icons.Dispose();
        _clanWindow.Dispose();
        _painter.Dispose();
        _sprites.Dispose();
    }
}
