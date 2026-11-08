using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using PristonTale.Engine3D.Formats;
using PristonTale.Gameplay.Screens.Framework;
using PristonTale.Gameplay.World;
using PristonTale.Gameplay.UI;
using PristonTale.Gameplay.UI.Widgets;
using PristonTale.Contracts.Motion;
using PristonTale.Gameplay.UI.Rendering;
using PristonTale.Gameplay.World.Remote;

namespace PristonTale.Gameplay.Screens.Gameplay;

public sealed class GameplayScreen : GameScreen
{
    private readonly EnteredCharacter _entered;
    private GameScene _scene;
    private GameplayHud _hud;
    private readonly List<GroundLabel> _groundLabels = [];
    private KeyboardState _lastKeyboard;
    private bool _showRenderStats = true;
    private volatile PristonTale.Client.Net.WorldCatalog.WorldRenderCatalog _pendingHudCatalog;

    public GameplayScreen(ScreenContext context, EnteredCharacter entered) : base(context)
    {
        _entered = entered;
    }

    public override void Enter()
    {
        _scene = new GameScene(Context.GraphicsDevice, Context.Content, Context.Log, Context.Sky, Context.Effects, Context.Presentation, Context.Audio);
        _scene.LoadEntered(Context.AssetRoot, _entered);
        _hud = new GameplayHud(
            Context,
            _scene.ApplyInventoryAsync,
            _scene.ApplyProgressionAsync,
            _scene.InteractNpcAsync,
            _scene.PlayNpcInteractionAudio,
            _scene.StopNpcTalk,
            _scene.RequestNpcServiceAsync,
            _scene.PrepareField,
            _scene.RequestQuestAsync);
        _hud.CharacterName = _scene.Player.Name;
        _hud.CharacterClass = _scene.Player.Class.Class;
        _hud.PortraitScene = _scene;
        _scene.PresenceObserved += _hud.Combat.Observe;
        _scene.PresenceObserved += _hud.StringEffect.Observe;
        _scene.PresenceObserved += _hud.ObserveQuests;
        _hud.QuestFanfare += _scene.PlayQuestFanfare;
        _hud.SiegeEnded += _scene.PlaySiegeEnd;
        _hud.SiegeClosed += _scene.RestoreFieldMusic;
        _hud.CraftResult += _scene.PlayCraftResult;
        _scene.NpcSelected += _hud.OpenNpc;
        _scene.ShopSellerSelected += _hud.ViewPersonalShop;
        _scene.PickupToCursor = _hud.ExpectPickup;
        _scene.PickupToCursorAnswered += _hud.PickupAnswered;
        _hud.PersonalShops = _scene;
        _scene.PresenceObserved += _hud.ObserveParty;
        _scene.PresenceObserved += _hud.ObserveClan;
        _scene.PresenceObserved += _hud.ObserveCastle;
        _scene.PresenceObserved += _hud.ObserveBellatra;
        _hud.Parties = _scene;
        _hud.SendClan = _scene.SendClan;
        _hud.SendCastleOwner = _scene.SendCastleOwner;
        _hud.SendBellatraClan = _scene.SendBellatraClan;
        _hud.SendBellatraResult = _scene.SendBellatraResult;
        _hud.BellatraStage = _scene;
        _hud.SendClanMark = _scene.SendClanMark;
        _scene.PresenceObserved += _hud.ObserveTrade;
        _hud.Trades = _scene;
        _scene.PresenceObserved += _hud.ObserveContacts;
        _hud.Contacts = _scene;
        _hud.PlayerButtonsUsed += _scene.DismissPlayerButtons;
        _scene.SkillNotice += _hud.Chat.System;
        _hud.SummonModeRequested += _scene.ToggleSummonMode;
        _hud.CrystalPetDismissRequested += _scene.DismissCrystalPets;
        _hud.RunToggleRequested += _scene.ToggleRun;
        _hud.YahooKeyPressed += _scene.PressYahooKey;
        _hud.ItemSound += _scene.PlayItemSound;
        _hud.SummonAutomatic = _scene.SummonAutomatic;
        _hud.UseItem = _scene.UseItemAsync;
        _hud.Chat.PlayerName = _scene.Player.Name;
        _hud.Chat.Send = _scene.SendChatAsync;
        _hud.Overhead = _scene.Overhead;
        _hud.GroundLabels = _groundLabels;
        _hud.GroundLabelPlaced = _scene.PlaceGroundLabel;
        _scene.PresenceObserved += _hud.ObserveChat;
        _scene.PresenceObserved += _hud.ObserveGuide;
        _hud.Chat.WhisperArrived += _scene.PlayWhisper;
        _hud.Chat.CloseRequested += RequestExit;
        _hud.ExitRequested += RequestExit;
        _hud.Restart.Restart = _scene.RestartAsync;
        _hud.Restart.ExitRequested += RequestExit;
        _hud.WingGate.Travel = _scene.TravelWingGateAsync;
        _hud.WingGate.RequiredLevel = _scene.FieldLimitLevel;
        _scene.WingGateEntered += _hud.WingGate.Open;
        _scene.WingGateLeft += _hud.WingGate.Close;

        _scene.BeginRemotePresence(Context.Backend.OpenPlayChannel(), _entered);

        _ = FetchWorldCatalogAsync();
    }

    private async Task FetchWorldCatalogAsync()
    {
        try
        {
            var catalog = await Context.Backend.GetWorldCatalogAsync();
            _scene?.SetWorldRenderCatalog(catalog);
            _pendingHudCatalog = catalog;
        }
        catch (Exception ex)
        {
            Context.Log.Error("failed to fetch world render catalog (monsters will not render)", ex);
        }
    }

    public override string StatusText
    {
        get
        {
            if (_scene == null)
                return "Loading…";
            var shadows = _scene.ShadowsEnabled ? "on" : "OFF";
            if (!_showRenderStats)
                return $"Shadows {shadows} | {_scene.Status}";

            var stats = _scene.LastRenderStats;
            var poses = _scene.LastAnimationCounts;
            var hour = _scene.TimeOfDay;
            var clock = $"{(int)hour:00}:{(int)((hour - (int)hour) * 60):00}";
            return $"Shadows {shadows} | {clock} | Pos {_scene.PlayerPosition.X:F0} {-_scene.PlayerPosition.Z:F0} | Lights {_scene.StageLightCount} | " +
                   $"Draw {stats.DrawCalls} | Pose {poses.Posed}/{poses.Posed + poses.Reused} | " +
                   $"Shadow S{(stats.SunShadowUpdated ? 1 : 0)}/L{stats.LampShadowMapsUpdated}/D{stats.ShadowDrawCalls} | " +
                   $"Vis {stats.VisibleBatches}/{stats.TotalBatches} | Tri {stats.Triangles} | State {stats.StateChanges} | " +
                   $"View {_scene.RenderDebugView}";
        }
    }

    public override void Update(GameTime gameTime)
    {
        var catalog = _pendingHudCatalog;
        if (catalog is not null)
        {
            _pendingHudCatalog = null;
            _hud.Combat.SetCatalog(catalog);
        }

        var field = _scene.ActiveFieldDefinition;
        _ = _scene.TryGetNeighbourMap(out var neighbour, out var neighbourMap);
        var mapHidden = field is not null && HudMinimap.HiddenOn(field.FieldCode, field.State);
        _hud.World = new HudWorldView(field?.Id, field?.Underground == true, _scene.ActiveMapRectangle, _scene.PlayerPosition,
            _scene.PlayerForward, _scene.TimeOfDay, _scene.RunFlag, mapHidden, neighbour, neighbourMap);
        _hud.PremiumTimers = _scene.PremiumTimers;
        _hud.InCastleField = field?.State == Simulation.World.FieldState.Castle;
        _scene.OwnClanId = _hud.Combat.OwnClan;
        _scene.CastleLeague = _hud.Castle.League;
        _scene.CastleOwnerClanId = _hud.Castle.OwnerClanId;
        _hud.InTown = field?.State == Simulation.World.FieldState.Village;
        _hud.ShopSigns = _scene.ShopSigns;
        _hud.PlayerDead = _scene.PlayerDead;
        _hud.PlayerActing = _scene.PlayerActing;
        _hud.PlayerActionStarts = _scene.PlayerActionStarts;
        _hud.RestartOffer = _scene.RestartOffer;
        _hud.Update(_scene.Owner);
        _scene.PointerCaptured = _hud.PointerCaptured;
        _scene.MovementLocked = _hud.HoldsPlayer;
        _scene.KeyboardCaptured = _hud.KeyboardCaptured;
        _hud.Chat.SelfId = _scene.SelfEntityId;
        _scene.CameraInputEnabled = Context.IsWindowActive && !Context.Presentation.AwaitingConfirmation;
        _scene.RightAttackEnabled = _hud.RightAttackEnabled;
        _scene.LeftSkill = _hud.Bindings.LeftSkillId;
        _scene.RightSkill = _hud.Bindings.RightSkillId;
        _hud.Statuses = _scene.Skills?.Statuses;
        _hud.Cooldowns = _scene.Skills?.Cooldowns;
        _hud.SkillClockMs = gameTime.TotalGameTime.TotalMilliseconds;
        _hud.SelfId = _scene.SelfEntityId;
        if (!Context.Presentation.AwaitingConfirmation && !_hud.KeyboardCaptured)
        {
            UpdateMotionKeys();
            UpdateDebugKeys();
        }
        _scene.Update(gameTime);
        _hud.TargetId = _scene.SelectedTargetId;
        _hud.HelperPet = _scene.SelfHelperPet();
        _hud.HoveredId = _scene.HoveredTargetId;
        _hud.TracedId = _scene.TracedTargetId;
        _hud.ButtonsPlayerId = _scene.ReachedPlayerId;
        _lastKeyboard = Keyboard.GetState();
    }

    public override void Draw()
    {
        _hud.RenderPortrait();
        Context.GraphicsDevice.Clear(_scene.SkyColor);
        _scene.Draw();
        _scene.CollectGroundLabels(_groundLabels);
        _scene.DrawCombatText(Context.SpriteBatch, Context.Font);
        _hud.Draw();
    }

    private void UpdateMotionKeys()
    {
        var keyboard = Keyboard.GetState();
        if (!keyboard.IsKeyDown(Keys.LeftControl) && !keyboard.IsKeyDown(Keys.RightControl))
        {
            return;
        }

        if (Pressed(keyboard, Keys.D1))
        {
            _scene.DebugForceMotion(MotionCode.Stand);
        }
        if (Pressed(keyboard, Keys.D2))
        {
            _scene.DebugForceMotion(MotionCode.Walk);
        }
        if (Pressed(keyboard, Keys.D3))
        {
            _scene.DebugForceMotion(MotionCode.Run);
        }
        if (Pressed(keyboard, Keys.D4))
        {
            _scene.DebugForceMotion(MotionCode.Attack);
        }
        if (Pressed(keyboard, Keys.D5))
        {
            _scene.DebugForceMotion(MotionCode.Damage);
        }
        if (Pressed(keyboard, Keys.D6))
        {
            _scene.DebugForceMotion(MotionCode.Dead);
        }
        if (Pressed(keyboard, Keys.D7))
        {
            _scene.DebugForceMotion(MotionCode.Skill);
        }
    }

    private void UpdateDebugKeys()
    {
        var keyboard = Keyboard.GetState();
        var control = keyboard.IsKeyDown(Keys.LeftControl) || keyboard.IsKeyDown(Keys.RightControl);
        if (control && Pressed(keyboard, Keys.F3))
        {
            _showRenderStats = !_showRenderStats;
        }
        if (control && Pressed(keyboard, Keys.F5))
        {
            _scene.ToggleShadows();
        }
        if (control && Pressed(keyboard, Keys.F10))
        {
            _scene.CycleRenderDebugView();
        }
    }

    private bool Pressed(KeyboardState keyboard, Keys key) =>
        keyboard.IsKeyDown(key) && !_lastKeyboard.IsKeyDown(key);

    public override bool HandleEscape() => _hud?.ClosePanel() == true;

    // The scene clears its hovered entity while the HUD has the pointer, so windows show the arrow unless the HUD imposes
    // one (a shop's mode, a dragged item).
    public override HudCursorKind Cursor =>
        _scene is null || _hud is null
            ? HudCursorKind.Default
            : _hud.CursorOverride ?? HudCursor.ForHovered(_scene.HoveredDropId != Guid.Empty, Mouse.GetState().LeftButton == ButtonState.Pressed,
                _hud.Combat.KindOf(_scene.HoveredTargetId, out var owner), owner);

    // The server's shutdown asks every client to save and quit: the game closes as from the window's close button.
    private void RequestExit() => Context.ExitRequested = true;

    public override void Dispose()
    {
        if (_scene is not null && _hud is not null)
        {
            _scene.PresenceObserved -= _hud.Combat.Observe;
            _scene.PresenceObserved -= _hud.StringEffect.Observe;
            _scene.PresenceObserved -= _hud.ObserveQuests;
            _scene.PresenceObserved -= _hud.ObserveChat;
            _scene.PresenceObserved -= _hud.ObserveGuide;
            _hud.Chat.WhisperArrived -= _scene.PlayWhisper;
            _hud.Chat.CloseRequested -= RequestExit;
            _hud.ExitRequested -= RequestExit;
            _hud.Restart.ExitRequested -= RequestExit;
            _hud.QuestFanfare -= _scene.PlayQuestFanfare;
            _hud.SiegeEnded -= _scene.PlaySiegeEnd;
            _hud.SiegeClosed -= _scene.RestoreFieldMusic;
            _hud.CraftResult -= _scene.PlayCraftResult;
            _scene.NpcSelected -= _hud.OpenNpc;
            _scene.ShopSellerSelected -= _hud.ViewPersonalShop;
            _scene.PresenceObserved -= _hud.ObserveParty;
            _scene.PresenceObserved -= _hud.ObserveClan;
            _scene.PresenceObserved -= _hud.ObserveCastle;
            _scene.PresenceObserved -= _hud.ObserveBellatra;
            _scene.PresenceObserved -= _hud.ObserveTrade;
            _scene.PresenceObserved -= _hud.ObserveContacts;
            _hud.PlayerButtonsUsed -= _scene.DismissPlayerButtons;
            _scene.SkillNotice -= _hud.Chat.System;
            _hud.SummonModeRequested -= _scene.ToggleSummonMode;
            _hud.CrystalPetDismissRequested -= _scene.DismissCrystalPets;
            _hud.RunToggleRequested -= _scene.ToggleRun;
            _hud.YahooKeyPressed -= _scene.PressYahooKey;
            _hud.ItemSound -= _scene.PlayItemSound;
        }

        _hud?.Dispose();
        _scene?.Dispose();
    }

    public override Task Closing => _scene?.Closing ?? Task.CompletedTask;
}
