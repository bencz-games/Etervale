using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using PristonTale.Effects.Vfx;
using PristonTale.Engine3D.Formats;
using PristonTale.Engine3D.Rendering;
using PristonTale.Gameplay.Audio;
using PristonTale.Gameplay.Effects;
using PristonTale.Gameplay.Screens.Framework;
using PristonTale.Gameplay.Screens.Gameplay;
using PristonTale.Gameplay.Screens.Opening;
using PristonTale.Gameplay.UI;
using PristonTale.Gameplay.World;
using PristonTale.Simulation.Characters;
using PristonTale.Contracts.Sessions;
using PristonTale.Contracts.Characters;

namespace PristonTale.Gameplay.Screens.CharacterSelect;

public sealed class CharacterSelectScreen : GameScreen
{
    internal const string LoginStagePath = "cSelect/Login.smd";

    private static readonly Color LoginClearColor = new(8, 7, 10);

    private const int MaxNameLength = CharacterNames.MaxLength;

    private readonly PristonAssetCache _assetCache = new();
    private readonly PristonSmdReader _reader;
    private readonly PristonActorLoader _actorLoader;

    private PristonRenderer _renderer;
    private CharacterSelectCamera _cameraCtl;
    private CharacterSelectScene _scene;
    private CharacterSelectPortrait _portrait;

    private VfxSystem _vfx;
    private VfxRenderer _vfxRenderer;
    private CharSelectFlames _flames;

    // Where the green flames burn (CharSelectEffects).
    public static readonly Vector3[] BrazierPositions =
    [
        new(-70f, 55f, 156f), new(-77f, 60f, 143f), new(-81f, 65f, 143f),
        new(70f, 56f, 155f),  new(75f, 60f, 152f),  new(81f, 66f, 142f),
    ];

    private LoginAudio _audio;
    private MouseState _lastMouse;
    private KeyboardState _lastKeyboard;
    private double _lastClickSeconds = -1d;
    private int _lastClickIndex = -1;
    private double _elapsedSeconds;

    private CharSelectAssets _assets;
    private CharacterSelectView _view;
    private readonly CharSelectViewModel _viewModel = new();

    private int _hoveredIndex = -1;
    private int _selectedIndex = -1;
    private bool _hoverNewCharacter;

    private CharacterBrood _chosenBrood;
    private int _jobHovered = -1;
    private int _jobSelected = -1;
    private int _faceIndex;
    private int _faceHovered = -1;
    private int _raceHovered = -1;
    private TextInputField _nameField;

    private enum State { CharacterSelect = 0, RaceSelect = 1, JobSelect = 2, FaceSelect = 3, Name = 4 }
    private enum Busy { None, LoadingRoster, PreparingRoster, PreparingJobLineup, Entering, Deleting, Creating }
    private State _state = State.CharacterSelect;
    private Busy _busy = Busy.None;
    private string _errorMessage = string.Empty;

    private CancellationTokenSource _backendCts;
    private Task<IReadOnlyList<CharacterSummary>> _rosterTask;
    private Task _rosterPreparationTask;
    private IReadOnlyList<CharacterSummary> _pendingRoster;
    private Task _jobPreparationTask;
    private Task<EnterWorldOutcome> _enterTask;
    private CharacterSummary _enteringSummary;
    private Task<DeleteCharacterOutcome> _deleteTask;
    private Task<CreateCharacterOutcome> _createTask;

    public CharacterSelectScreen(ScreenContext context) : base(context)
    {
        _reader = new PristonSmdReader(_assetCache);
        _actorLoader = new PristonActorLoader(_assetCache);
    }

    public override string StatusText => "Character select";

    public override void Enter()
    {
        _renderer = new PristonRenderer(Context.GraphicsDevice, Context.Content, Context.Log, Context.Presentation);

        ConfigureLighting(_renderer);

        var stage = _reader.ReadStage(AssetPath(LoginStagePath));
        _renderer.AddStage(stage, Array.Empty<PristonModel>());

        _cameraCtl = new CharacterSelectCamera(Context.GraphicsDevice, (int)State.CharacterSelect);
        _scene = new CharacterSelectScene(_renderer, _actorLoader, Context.AssetRoot, _cameraCtl, stage, Context.Log);

        _nameField = new TextInputField { MaxLength = MaxNameLength };

        _assets = new CharSelectAssets(Context.GraphicsDevice, AssetPath);
        _portrait = new CharacterSelectPortrait(Context.GraphicsDevice);
        _view = new CharacterSelectView(Context.SpriteBatch, Context.Font, _assets, _cameraCtl);
        InitEffects();
        _audio = LoginAudio.CharacterSelect(Context.Audio);
        BeginLoadRoster();
    }

    private void InitEffects()
    {
        var library = Context.Effects?.Vfx.Library;
        if (library is null)
        {
            return;
        }

        library.Register(CharSelectEffects.GreenBeamDefinition());
        if (!library.TryGet(CharSelectEffects.GreenBeam, out var beam))
        {
            return;
        }

        _vfx = new VfxSystem(VfxNoAnchors.Instance, new TransientLightServices(_renderer.TransientLights), library);
        _vfxRenderer = new VfxRenderer(Context.GraphicsDevice, Context.Content);
        _flames = new CharSelectFlames(_vfx, beam, BrazierPositions);
    }

    public override void Update(GameTime gameTime)
    {
        _elapsedSeconds = gameTime.TotalGameTime.TotalSeconds;
        var dt = (float)gameTime.ElapsedGameTime.TotalSeconds;
        _audio.Update(dt);

        _cameraCtl.Update(Context.GraphicsDevice, (int)_state, dt);

        _scene.UpdateRun(dt);

        _renderer.Update(gameTime);
        _flames?.Update(dt);

        var mouse = Mouse.GetState();
        var keyboard = Keyboard.GetState();
        // Drained every frame, so keys typed before the name box opens never reach it.
        var typed = Context.TypedText.Drain();

        if (_busy != Busy.None)
        {
            PollBusy();
        }
        else if (!Context.Presentation.AwaitingConfirmation)
        {
            switch (_state)
            {
                case State.CharacterSelect:
                    UpdateCharacterSelect(mouse);
                    break;
                case State.RaceSelect:
                    UpdateRaceSelect(mouse);
                    break;
                case State.JobSelect:
                    UpdateJobSelect(mouse);
                    break;
                case State.FaceSelect:
                case State.Name:
                    UpdateFaceSelect(mouse, typed, gameTime.ElapsedGameTime.TotalSeconds);
                    break;
            }
        }

        _lastMouse = mouse;
        _lastKeyboard = keyboard;
    }

    // The scene's light: an even interior ambient and no lamps, so the statues keep their own colours under the green
    // flames (also used by the regression's capture probe).
    internal static void ConfigureLighting(PristonRenderer renderer)
    {
        renderer.Underground = true;
        renderer.UndergroundAmbient = new Vector3(0.85f, 0.82f, 0.78f);
        renderer.ActorFloorLight = false;
    }

    private void BeginLoadRoster()
    {
        _busy = Busy.LoadingRoster;
        _errorMessage = string.Empty;
        ResetBackendCall();
        _rosterTask = Context.Backend.ListCharactersAsync(_backendCts.Token);
    }

    private void PollBusy()
    {
        switch (_busy)
        {
            case Busy.LoadingRoster:
                PollRoster();
                break;
            case Busy.PreparingRoster:
                PollRosterPreparation();
                break;
            case Busy.PreparingJobLineup:
                PollJobPreparation();
                break;
            case Busy.Entering:
                PollEnterWorld();
                break;
            case Busy.Deleting:
                PollDelete();
                break;
            case Busy.Creating:
                PollCreate();
                break;
        }
    }

    private void PollRoster()
    {
        if (_rosterTask is not { IsCompleted: true })
        {
            return;
        }

        if (_rosterTask.IsCompletedSuccessfully)
        {
            _pendingRoster = _rosterTask.Result;
            _rosterTask = null;
            _rosterPreparationTask = _scene.PrepareRosterAsync(_pendingRoster, _backendCts.Token);
            _busy = Busy.PreparingRoster;
            return;
        }

        Context.Log.Error("[CharSelect] character-list request failed", _rosterTask.Exception?.GetBaseException());
        _rosterTask = null;
        _errorMessage = "Could not load characters.";
        _busy = Busy.None;
        _state = State.CharacterSelect;
    }

    private void PollRosterPreparation()
    {
        if (_rosterPreparationTask is not { IsCompleted: true })
        {
            return;
        }

        if (_rosterPreparationTask.IsCompletedSuccessfully)
        {
            BuildCharacters(_pendingRoster);
            _errorMessage = string.Empty;
        }
        else
        {
            Context.Log.Error("[CharSelect] roster actor preparation failed", _rosterPreparationTask.Exception?.GetBaseException());
            _errorMessage = "Could not load character models.";
        }

        _pendingRoster = null;
        _rosterPreparationTask = null;
        _busy = Busy.None;
        _state = State.CharacterSelect;
    }

    private void BuildCharacters(IReadOnlyList<CharacterSummary> roster)
    {
        _scene.BuildRoster(roster);
        _hoveredIndex = -1;
        _selectedIndex = -1;
    }

    private void UpdateCharacterSelect(MouseState mouse)
    {
        var cursor = new Vector2(mouse.X, mouse.Y);
        _hoveredIndex = _scene.PickRoster(cursor, Context.GraphicsDevice.Viewport);
        _hoverNewCharacter = HasFreeSlot && CharSelectLayout.NewCharHotspot(Context.GraphicsDevice.Viewport).Contains(mouse.X, mouse.Y);
        _scene.SetRosterHighlight(_hoveredIndex, _selectedIndex);

        if (!Clicked(mouse))
        {
            return;
        }

        if (_hoverNewCharacter)
        {
            EnterRaceSelect();
            return;
        }

        if (HandleSelectButtons(cursor))
        {
            return;
        }

        if (_hoveredIndex >= 0)
        {
            var now = _elapsedSeconds;
            var isDouble = _lastClickIndex == _hoveredIndex && (now - _lastClickSeconds) < 0.35d;
            _selectedIndex = _hoveredIndex;
            _audio.Play(LoginCue.PickCharacter);
            if (isDouble)
            {
                BeginEnterWorld();
            }

            _lastClickSeconds = now;
            _lastClickIndex = _hoveredIndex;
        }
        else
        {
            _selectedIndex = -1;
        }

        _scene.SetRosterHighlight(_hoveredIndex, _selectedIndex);
    }

    private bool HasFreeSlot => _scene.RosterCount < _scene.MaxRoster;

    private bool HandleSelectButtons(Vector2 cursor)
    {
        var viewport = Context.GraphicsDevice.Viewport;
        var y = CharSelectLayout.ButtonRowY(viewport);
        var hasChars = _scene.RosterCount > 0;

        var primary = hasChars ? _assets.BtSelect : _assets.BtNewChar;
        var primaryKind = hasChars ? CharSelectLayout.ButtonKind.Select : CharSelectLayout.ButtonKind.NewCharacter;
        if (InButton(cursor, primary, CharSelectLayout.TwoButtonPrimaryX(viewport), y, primaryKind))
        {
            if (hasChars)
            {
                _audio.Play(LoginCue.Button);
                if (_selectedIndex >= 0)
                {
                    BeginEnterWorld();
                }
            }
            else
            {
                EnterRaceSelect();
            }
            return true;
        }
        if (InButton(cursor, _assets.BtExit, CharSelectLayout.TwoButtonQuitX(viewport), y, CharSelectLayout.ButtonKind.Exit))
        {
            _audio.Play(LoginCue.Button);
            Context.RequestScreen(new OpeningScreen(Context));
            return true;
        }
        if (_selectedIndex >= 0 &&
            InButton(cursor, _assets.BtDelete, CharSelectLayout.TwoButtonDeleteX, y, CharSelectLayout.ButtonKind.Delete))
        {
            _audio.Play(LoginCue.Button);
            BeginDelete();
            return true;
        }
        return false;
    }

    private void BeginEnterWorld()
    {
        if (_selectedIndex < 0 || _selectedIndex >= _scene.RosterCount)
        {
            return;
        }

        _audio.Play(LoginCue.GameStart);
        _enteringSummary = _scene.RosterSummary(_selectedIndex);
        var name = _enteringSummary.Name;
        _errorMessage = string.Empty;
        _busy = Busy.Entering;
        ResetBackendCall();
        _enterTask = Context.Backend.EnterWorldAsync(name, _backendCts.Token);
    }

    private void PollEnterWorld()
    {
        if (_enterTask is not { IsCompleted: true })
        {
            return;
        }

        if (_enterTask.IsCompletedSuccessfully && _enterTask.Result.Ok)
        {
            var outcome = _enterTask.Result;
            var s = _enteringSummary;
            var entered = new EnteredCharacter(
                s.Class, s.Name ?? string.Empty, s.Level, s.FaceIndex,
                outcome.FieldId, outcome.SpawnX, outcome.SpawnZ, s.Tier, s.HairIndex);
            _enterTask = null;
            Context.RequestScreen(new GameplayScreen(Context, entered));
            return;
        }
        _errorMessage = _enterTask.IsCompletedSuccessfully
            ? Fallback(_enterTask.Result.Message, "Could not enter the world.")
            : "Could not reach server.";
        _enterTask = null;
        _busy = Busy.None;
        _state = State.CharacterSelect;
    }

    private void BeginDelete()
    {
        if (_selectedIndex < 0 || _selectedIndex >= _scene.RosterCount)
        {
            return;
        }

        var name = _scene.RosterSummary(_selectedIndex).Name;
        _errorMessage = string.Empty;
        _busy = Busy.Deleting;
        ResetBackendCall();
        _deleteTask = Context.Backend.DeleteCharacterAsync(name, _backendCts.Token);
    }

    private void PollDelete()
    {
        if (_deleteTask is not { IsCompleted: true })
        {
            return;
        }

        var ok = _deleteTask.IsCompletedSuccessfully && _deleteTask.Result.Ok;
        var message = _deleteTask.IsCompletedSuccessfully ? _deleteTask.Result.Message : "delete failed";
        _deleteTask = null;
        if (ok)
        {
            _audio.Play(LoginCue.Delete);
            BeginLoadRoster();
        }
        else
        {
            _errorMessage = Fallback(message, "Could not delete the character.");
            _busy = Busy.None;
            _state = State.CharacterSelect;
        }
    }

    private void EnterRaceSelect()
    {
        _audio.Play(LoginCue.NewCharacter);
        ClearJobActors();
        _state = State.RaceSelect;
        _errorMessage = string.Empty;
    }

    private void UpdateRaceSelect(MouseState mouse)
    {
        var cursor = new Vector2(mouse.X, mouse.Y);
        var viewport = Context.GraphicsDevice.Viewport;
        _raceHovered = -1;
        if (CharSelectLayout.TempscronMark(viewport).Contains(mouse.X, mouse.Y))
        {
            _raceHovered = 0;
        }
        else if (CharSelectLayout.MorionMark(viewport).Contains(mouse.X, mouse.Y))
        {
            _raceHovered = 1;
        }

        if (!Clicked(mouse))
        {
            return;
        }

        if (_raceHovered == 0)
        {
            ChooseBrood(CharacterBrood.Tempskron);
            return;
        }

        if (_raceHovered == 1)
        {
            ChooseBrood(CharacterBrood.Morion);
            return;
        }

        if (HandleCreateButtons(cursor))
        {
            return;
        }
    }

    private void ChooseBrood(CharacterBrood brood)
    {
        _audio.Play(LoginCue.NewCharacter);
        _chosenBrood = brood;
        _errorMessage = string.Empty;
        ResetBackendCall();
        _jobPreparationTask = _scene.PrepareJobLineupAsync(brood, _backendCts.Token);
        _busy = Busy.PreparingJobLineup;
    }

    private void PollJobPreparation()
    {
        if (_jobPreparationTask is not { IsCompleted: true })
        {
            return;
        }

        if (_jobPreparationTask.IsCompletedSuccessfully)
        {
            BuildJobLineup();
            _state = State.JobSelect;
        }
        else
        {
            Context.Log.Error("[CharSelect] job-lineup actor preparation failed", _jobPreparationTask.Exception?.GetBaseException());
            _errorMessage = "Could not load class models.";
        }

        _jobPreparationTask = null;
        _busy = Busy.None;
    }

    private void BuildJobLineup()
    {
        _scene.BuildJobLineup(_chosenBrood);
        _jobHovered = -1;
        _jobSelected = -1;
        _faceIndex = 0;
    }

    private void UpdateJobSelect(MouseState mouse)
    {
        var cursor = new Vector2(mouse.X, mouse.Y);
        _jobHovered = _scene.PickJob(cursor, Context.GraphicsDevice.Viewport);
        _scene.SetJobHighlight(_jobHovered, _jobSelected);

        if (!Clicked(mouse))
        {
            return;
        }

        if (HandleCreateButtons(cursor))
        {
            return;
        }

        if (_jobHovered >= 0)
        {
            var now = _elapsedSeconds;
            var isDouble = _lastClickIndex == _jobHovered && (now - _lastClickSeconds) < 0.35d;
            _jobSelected = _jobHovered;
            _audio.Play(LoginCue.PickCharacter);
            if (isDouble)
            {
                EnterFaceSelect();
            }

            _lastClickSeconds = now;
            _lastClickIndex = _jobHovered;
            _scene.SetJobHighlight(_jobHovered, _jobSelected);
        }
    }

    private void EnterFaceSelect()
    {
        if (_jobSelected < 0 || _jobSelected >= _scene.JobCount)
        {
            return;
        }

        _faceIndex = 0;
        _nameField.Clear();
        _nameField.Focused = true;
        _assets.LoadFaceThumbs(_scene.JobClass(_jobSelected));
        _scene.ApplyFace(_jobSelected, _faceIndex);
        _scene.OpenFacePreview(_jobSelected, _faceIndex);
        _scene.StartForwardRun(_jobSelected);
        _scene.SetJobHighlight(-1, -1);

        _state = State.FaceSelect;
        _errorMessage = string.Empty;
    }

    private void ReturnChosenJobToLineup() => _scene.ReturnToLineup(_jobSelected);

    private void UpdateFaceSelect(MouseState mouse, ReadOnlySpan<char> typed, double dtSeconds)
    {
        _nameField.Update(typed, dtSeconds);
        _faceHovered = -1;
        for (var index = 0; index < CharSelectLayout.FacesPerJob; index++)
        {
            if (CharSelectLayout.FaceThumbHit(Context.GraphicsDevice.Viewport, index).Contains(mouse.X, mouse.Y))
            {
                _faceHovered = index;
                break;
            }
        }

        if (!Clicked(mouse))
        {
            return;
        }
        var cursor = new Vector2(mouse.X, mouse.Y);

        for (var i = 0; i < CharSelectLayout.FacesPerJob; i++)
        {
            if (CharSelectLayout.FaceThumbHit(Context.GraphicsDevice.Viewport, i).Contains((int)cursor.X, (int)cursor.Y))
            {
                if (_faceIndex != i)
                {
                    _faceIndex = i;
                    _scene.ApplyFace(_jobSelected, i);
                }
                return;
            }
        }

        if (HandleCreateButtons(cursor))
        {
            return;
        }
    }

    private bool HandleCreateButtons(Vector2 cursor)
    {
        var viewport = Context.GraphicsDevice.Viewport;
        var y = CharSelectLayout.ButtonRowY(viewport);

        if (InButton(cursor, _assets.BtSelect, CharSelectLayout.ThreeButtonOkX(viewport), y, CharSelectLayout.ButtonKind.Select))
        {
            _audio.Play(LoginCue.Button);
            OnCreateOk();
            return true;
        }

        if (InButton(cursor, _assets.BtBack, CharSelectLayout.ThreeButtonBackX(viewport), y, CharSelectLayout.ButtonKind.Back))
        {
            _audio.Play(LoginCue.Back);
            StepBack();
            return true;
        }

        if (InButton(cursor, _assets.BtExit, CharSelectLayout.ThreeButtonQuitX(viewport), y, CharSelectLayout.ButtonKind.Exit))
        {
            _audio.Play(LoginCue.Button);
            Context.RequestScreen(new OpeningScreen(Context));
            return true;
        }

        return false;
    }

    private void OnCreateOk()
    {
        switch (_state)
        {
            case State.RaceSelect:
                _errorMessage = "Choose a tribe.";
                break;
            case State.JobSelect:
                if (_jobSelected >= 0)
                {
                    EnterFaceSelect();
                }
                else
                {
                    _errorMessage = "Choose a class.";
                }

                break;
            case State.FaceSelect:
            case State.Name:
                BeginCreate();
                break;
        }
    }

    private void StepBack()
    {
        switch (_state)
        {
            case State.RaceSelect:
                ClearJobActors();
                _state = State.CharacterSelect;
                break;
            case State.JobSelect:
                ClearJobActors();
                _state = State.RaceSelect;
                break;
            case State.FaceSelect:
            case State.Name:
                ReturnChosenJobToLineup();
                _scene.CloseFacePreview();
                _jobSelected = -1;
                _nameField.Focused = false;
                _assets.ClearFaceThumbs();
                _state = State.JobSelect;
                break;
        }
        _errorMessage = string.Empty;
    }

    private void BeginCreate()
    {
        if (_jobSelected < 0 || _jobSelected >= _scene.JobCount)
        {
            _errorMessage = "Choose a class.";
            return;
        }
        var name = _nameField.Text.Trim();
        if (string.IsNullOrEmpty(name))
        {
            _errorMessage = "Enter a name.";
            return;
        }
        _audio.Play(LoginCue.GameStart);
        _errorMessage = string.Empty;
        _busy = Busy.Creating;
        ResetBackendCall();
        _createTask = Context.Backend.CreateCharacterAsync(name, _scene.JobClass(_jobSelected), _faceIndex, _backendCts.Token);
    }

    private void PollCreate()
    {
        if (_createTask is not { IsCompleted: true })
        {
            return;
        }

        var ok = _createTask.IsCompletedSuccessfully && _createTask.Result.Ok;
        var message = _createTask.IsCompletedSuccessfully ? _createTask.Result.Message : "create failed";
        _createTask = null;
        if (ok)
        {
            ClearJobActors();
            _state = State.CharacterSelect;
            BeginLoadRoster();
        }
        else
        {
            _errorMessage = Fallback(message, "That name is taken.");
            _busy = Busy.None;
        }
    }

    private void ClearJobActors()
    {
        _scene.ClearJobLineup();
        _jobHovered = -1;
        _jobSelected = -1;
    }

    public override void Draw()
    {
        var viewport = Context.GraphicsDevice.Viewport;
        var viewModel = BuildViewModel();
        viewModel.FacePortrait = RenderFacePortrait(viewport);

        Context.GraphicsDevice.Clear(LoginClearColor);
        _renderer.Draw(_cameraCtl.Camera);

        // Effects retain scene depth; the 2D view must remain last.
        if (_vfx is not null)
        {
            _vfxRenderer.Draw(_vfx, _cameraCtl.View, _cameraCtl.Projection, _cameraCtl.Position, _renderer.SceneLinearDepth);
        }

        _renderer.ComposeScene();

        _view.Draw(viewport, viewModel);
    }

    private Texture2D RenderFacePortrait(Viewport viewport)
    {
        if (_state is not State.FaceSelect and not State.Name || _jobSelected < 0 || _jobSelected >= _scene.JobCount)
        {
            return null;
        }

        var size = CharSelectLayout.FacePortrait(viewport).Size;
        return _portrait.Render(_renderer, _scene.FacePreviewHandles, size.X, size.Y);
    }

    private CharSelectViewModel BuildViewModel()
    {
        _viewModel.StateIndex = (int)_state;
        _viewModel.ErrorMessage = _errorMessage;
        _viewModel.ElapsedSeconds = _elapsedSeconds;
        _viewModel.RosterHasChars = _scene.RosterCount > 0;
        _viewModel.HasSelectedRoster = _selectedIndex >= 0;
        _viewModel.ShowNewCharacterPrompt = HasFreeSlot && (_hoverNewCharacter || _scene.RosterCount == 0);
        _viewModel.Info = _scene.Info(_hoveredIndex);
        _viewModel.HoveredRace = _raceHovered;
        _viewModel.JobCount = Math.Min(_scene.JobCount, _viewModel.Jobs.Length);
        for (var index = 0; index < _viewModel.JobCount; index++)
        {
            _viewModel.Jobs[index] = new CharSelectJobView(
                _scene.JobClass(index),
                _scene.JobHeadWorld(index),
                index == _jobHovered,
                index == _jobSelected);
        }

        _viewModel.FaceIndex = _faceIndex;
        _viewModel.HoveredFace = _faceHovered;
        _viewModel.FaceBrood = _chosenBrood;
        _viewModel.NameDisplay = _nameField.DisplayText;
        _viewModel.CaretVisible = _nameField.CaretVisible;
        return _viewModel;
    }

    private static bool InButton(
        Vector2 cursor,
        Texture2D texture,
        int x,
        int y,
        CharSelectLayout.ButtonKind kind)
    {
        if (texture == null)
        {
            return false;
        }

        return CharSelectLayout.ButtonHit(texture, x, y, kind).Contains((int)cursor.X, (int)cursor.Y);
    }

    private bool Clicked(in MouseState mouse) =>
        mouse.LeftButton == ButtonState.Pressed && _lastMouse.LeftButton == ButtonState.Released;

    private void ResetBackendCall()
    {
        _backendCts?.Cancel();
        _backendCts?.Dispose();
        _backendCts = new CancellationTokenSource();
    }

    public override void Dispose()
    {
        _backendCts?.Cancel();
        _backendCts?.Dispose();
        _audio?.Dispose();
        _vfxRenderer?.Dispose();
        _portrait?.Dispose();
        _assets?.Dispose();
        _renderer?.Dispose();
    }
}
