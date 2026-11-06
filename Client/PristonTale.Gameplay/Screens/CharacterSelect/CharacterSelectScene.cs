using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using PristonTale.Engine3D.Diagnostics;
using PristonTale.Engine3D.Formats;
using PristonTale.Engine3D.Physics;
using PristonTale.Engine3D.Rendering;
using PristonTale.Gameplay.World.Remote;
using PristonTale.Simulation.Characters;
using PristonTale.Contracts.Motion;
using PristonTale.Contracts.Sessions;
using PristonTale.Engine3D;
using PristonTale.Contracts.Characters;

namespace PristonTale.Gameplay.Screens.CharacterSelect;

internal sealed class CharacterSelectScene
{
    private static readonly (float X, float Z)[] OriginalUserCharacterPositions =
    [
        (0f, -110f),
        (-35f, -90f),
        (35f, -90f),
        (-70f, -60f),
        (70f, -60f),
    ];

    private static readonly (float X, float Z)[] SixUserCharacterPositions =
    [
        (-18f, -110f),
        (18f, -110f),
        (-53f, -85f),
        (53f, -85f),
        (-88f, -55f),
        (88f, -55f),
    ];

    // The job lineup stands from -90 to 90: five classes 45 apart, six 36 apart; the two at the ends stand 10 nearer.
    internal static (float X, float Z) JobCharacterPos(int index, int count)
    {
        var spacing = count > 5 ? 36 : 45;
        return ((2 * index - (count - 1)) * spacing / 2, index == 0 || index == count - 1 ? -320f : -330f);
    }

    private (float X, float Z) JobSlot(int jobIndex) => JobCharacterPos(_jobActors[jobIndex].SlotIndex, _jobActors.Count);

    private static readonly PlayerClass[] TempscronJobs =
        [PlayerClass.Mechanician, PlayerClass.Fighter, PlayerClass.Pikeman, PlayerClass.Archer, PlayerClass.Assassin, PlayerClass.MartialArtist];
    private static readonly PlayerClass[] MorionJobs =
        [PlayerClass.Knight, PlayerClass.Atalanta, PlayerClass.Priestess, PlayerClass.Magician, PlayerClass.Shaman];

    private const float HitHeadHeight = 45f;
    private const float HitHalfWidthPixels = 26f;

    private const float MeasuredLoginFloorY = 0f;
    private const float PlausibleFloorAbsY = 40f;

    private const float JobForwardZ = -420f;
    private const float JobStopZ = -415f;
    private const float JobRestZ = -429f;
    private const float JobForwardUnitsPerSecond = 120f;
    private const float JobTurnRate = 9f;
    private const float JobYawSettleEpsilon = 0.01f;

    private enum JobMovePhase { None, TurnToHeading, Running, TurnToCamera }

    private readonly IActorScene _actorScene;
    private readonly PlayerActorComposer _playerComposer;
    private readonly CharacterClassRegistry _classRegistry = new();
    private readonly string _assetRoot;
    private readonly CharacterSelectCamera _camera;
    private readonly IClientLog _log;
    private readonly PristonCollisionWorld _collision;

    private sealed record Placed(CharacterSummary Summary, (float X, float Z) Slot, PlayerActorHandles Handles);
    private readonly List<Placed> _placed = [];
    private sealed record JobActor(PlayerClass Class, int SlotIndex, CharacterVisualSet Visuals, PlayerActorHandles Handles);
    private readonly List<JobActor> _jobActors = [];

    private JobMovePhase _jobPhase = JobMovePhase.None;
    private int _jobMoverIndex = -1;
    private bool _jobMoverReturning;
    private float _jobMoveX, _jobMoveZ;
    private float _jobMoveYaw;
    private float _jobTargetX, _jobTargetZ;
    private int _forwardSubject = -1;
    private int _rosterHover = -1;
    private int _rosterSelection = -1;
    private int _jobHover = -1;
    private int _jobSelection = -1;
    private PlayerActorHandles _facePreview = new(ActorHandle.None, ActorHandle.None);
    private CharacterVisualSet _facePreviewVisuals;

    public CharacterSelectScene(
        PristonRenderer renderer,
        PristonActorLoader actorLoader,
        string assetRoot,
        CharacterSelectCamera camera,
        PristonStage stage,
        IClientLog log)
    {
        _actorScene = new RendererActorScene(renderer);
        _playerComposer = new PlayerActorComposer(_actorScene, actorLoader);
        _assetRoot = assetRoot;
        _camera = camera;
        _log = log;
        _collision = BuildCollision(stage);
    }

    public int RosterCount => _placed.Count;
    public int MaxRoster => CharacterSlots.Maximum;
    public CharacterSummary RosterSummary(int i) => _placed[i].Summary;

    public async Task PrepareRosterAsync(IReadOnlyList<CharacterSummary> roster, CancellationToken cancellationToken)
    {
        var count = Math.Min(roster.Count, CharacterSlots.Maximum);
        var preparations = new List<Task>(count);
        for (var index = 0; index < count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (TryResolveRosterVisuals(roster[index], out var visuals))
            {
                preparations.Add(_playerComposer.PrepareAsync(_assetRoot, visuals, roster[index].FaceIndex));
            }
        }

        await Task.WhenAll(preparations).WaitAsync(cancellationToken);
    }

    public void BuildRoster(IReadOnlyList<CharacterSummary> roster)
    {
        ClearRoster();
        var count = Math.Min(roster.Count, CharacterSlots.Maximum);
        for (var i = 0; i < count; i++)
        {
            var summary = roster[i];
            if (!TryResolveRosterVisuals(summary, out var visuals))
            {
                continue;
            }

            var slot = UserCharacterPosition(count, i);
            var handles = _playerComposer.Compose(
                _assetRoot,
                visuals,
                summary.FaceIndex,
                SlotWorld(slot),
                MotionCode.Stand);
            ApplySelectPose(handles);
            _placed.Add(new Placed(summary, slot, handles));
        }
    }

    public void ClearRoster()
    {
        foreach (var placed in _placed)
        {
            placed.Handles.Remove(_actorScene);
        }

        _placed.Clear();
        _rosterHover = -1;
        _rosterSelection = -1;
    }

    public int JobCount => _jobActors.Count;
    public PlayerClass JobClass(int i) => _jobActors[i].Class;
    public PlayerActorHandles FacePreviewHandles => _facePreview;

    public async Task PrepareJobLineupAsync(CharacterBrood brood, CancellationToken cancellationToken)
    {
        var jobs = JobsForBrood(brood);
        var preparations = new List<Task>(jobs.Length * CharacterFace.FaceCount);
        for (var jobIndex = 0; jobIndex < jobs.Length; jobIndex++)
        {
            var visuals = JobVisuals(jobs[jobIndex]);
            for (var faceIndex = 0; faceIndex < CharacterFace.FaceCount; faceIndex++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                preparations.Add(_playerComposer.PrepareAsync(_assetRoot, visuals, faceIndex));
            }
        }

        await Task.WhenAll(preparations).WaitAsync(cancellationToken);
    }

    public void BuildJobLineup(CharacterBrood brood)
    {
        ClearJobLineup();
        var jobs = JobsForBrood(brood);
        for (var i = 0; i < jobs.Length; i++)
        {
            var visuals = JobVisuals(jobs[i]);
            var handles = _playerComposer.Compose(
                _assetRoot,
                visuals,
                0,
                SlotWorld(JobCharacterPos(i, jobs.Length)),
                MotionCode.Stand);
            ApplySelectPose(handles);
            _jobActors.Add(new JobActor(jobs[i], i, visuals, handles));
        }
    }

    public void ClearJobLineup()
    {
        CloseFacePreview();
        foreach (var job in _jobActors)
        {
            job.Handles.Remove(_actorScene);
        }

        _jobActors.Clear();
        _jobPhase = JobMovePhase.None;
        _jobMoverIndex = -1;
        _jobMoveYaw = 0f;
        _forwardSubject = -1;
        _jobHover = -1;
        _jobSelection = -1;
    }

    public void OpenFacePreview(int jobIndex, int faceIndex)
    {
        if (jobIndex < 0 || jobIndex >= _jobActors.Count)
        {
            return;
        }

        CloseFacePreview();
        _facePreviewVisuals = _jobActors[jobIndex].Visuals;
        _facePreview = _playerComposer.ComposePortrait(
            _assetRoot,
            _facePreviewVisuals,
            faceIndex,
            MotionCode.Stand);
        ApplySelectPose(_facePreview);
        SetHandlesColorAdd(_facePreview, Vector3.Zero);
    }

    public void CloseFacePreview()
    {
        _facePreview.Remove(_actorScene);
        _facePreview = new PlayerActorHandles(ActorHandle.None, ActorHandle.None);
        _facePreviewVisuals = null;
    }

    public void StartForwardRun(int jobIndex)
    {
        if (jobIndex < 0 || jobIndex >= _jobActors.Count)
        {
            return;
        }

        CompleteActiveSlide();
        _forwardSubject = jobIndex;
        var slot = JobSlot(jobIndex);
        StartSlide(
            jobIndex,
            slot.X,
            slot.Z,
            0f,
            JobForwardZ,
            returning: false);
    }

    public void ReturnToLineup(int jobIndex)
    {
        _forwardSubject = -1;
        if (jobIndex < 0 || jobIndex >= _jobActors.Count)
        {
            CompleteActiveSlide();
            return;
        }

        if (_jobMoverIndex >= 0 && _jobMoverIndex != jobIndex)
        {
            CompleteActiveSlide();
        }

        var slot = JobSlot(jobIndex);
        var active = _jobMoverIndex >= 0;
        var startX = active ? _jobMoveX : 0f;
        var startZ = active ? _jobMoveZ : JobRestZ;
        StartSlide(
            jobIndex,
            startX,
            startZ,
            slot.X,
            slot.Z,
            returning: true);
    }

    private void CompleteActiveSlide()
    {
        if (_jobMoverIndex >= 0 && _jobMoverIndex < _jobActors.Count)
        {
            var destinationX = _jobMoverReturning ? _jobTargetX : 0f;
            var destinationZ = _jobMoverReturning ? _jobTargetZ : JobRestZ;
            var job = _jobActors[_jobMoverIndex];

            _jobMoveX = destinationX;
            _jobMoveZ = destinationZ;
            PlaceJobActor(job, (destinationX, destinationZ), MotionCode.Stand, 0f);
        }

        _jobMoverIndex = -1;
        _jobMoverReturning = false;
        _jobPhase = JobMovePhase.None;
        _jobMoveYaw = 0f;
    }

    private void StartSlide(
        int actorIndex,
        float startX,
        float startZ,
        float targetX,
        float targetZ,
        bool returning)
    {
        _jobMoverIndex = actorIndex;
        _jobMoveX = startX;
        _jobMoveZ = startZ;
        _jobTargetX = targetX;
        _jobTargetZ = targetZ;
        _jobMoverReturning = returning;
        _jobPhase = JobMovePhase.TurnToHeading;
        PlaceJobActor(_jobActors[actorIndex], (startX, startZ), MotionCode.Stand, _jobMoveYaw);
    }

    public void UpdateRun(float dt)
    {
        if (_jobMoverIndex < 0 || _jobMoverIndex >= _jobActors.Count)
        {
            return;
        }

        var job = _jobActors[_jobMoverIndex];
        var turn = JobTurnRate * dt;

        switch (_jobPhase)
        {
            case JobMovePhase.TurnToHeading:
                {
                    var to = new Vector2(_jobTargetX - _jobMoveX, _jobTargetZ - _jobMoveZ);
                    var headingYaw = to.LengthSquared() > 1e-4f ? MathF.Atan2(to.X, -to.Y) : _jobMoveYaw;
                    _jobMoveYaw = ApproachAngle(_jobMoveYaw, headingYaw, turn);
                    PlaceJobActor(job, (_jobMoveX, _jobMoveZ), MotionCode.Stand, _jobMoveYaw);
                    if (MathF.Abs(MathHelper.WrapAngle(headingYaw - _jobMoveYaw)) <= JobYawSettleEpsilon)
                    {
                        _jobPhase = JobMovePhase.Running;
                    }

                    break;
                }

            case JobMovePhase.Running:
                {
                    var step = JobForwardUnitsPerSecond * dt;
                    var pos = new Vector2(_jobMoveX, _jobMoveZ);
                    var toTarget = new Vector2(_jobTargetX, _jobTargetZ) - pos;
                    var distance = toTarget.Length();

                    var arrived = distance <= step || (!_jobMoverReturning && _jobMoveZ <= JobStopZ);
                    if (arrived)
                    {
                        if (_jobMoverReturning)
                        {
                            _jobMoveX = _jobTargetX;
                            _jobMoveZ = _jobTargetZ;
                        }
                        else
                        {
                            _jobMoveX = 0f;
                            _jobMoveZ = JobRestZ;
                        }
                        PlaceJobActor(job, (_jobMoveX, _jobMoveZ), MotionCode.Stand, _jobMoveYaw);
                        _jobPhase = JobMovePhase.TurnToCamera;
                        break;
                    }

                    var dir = toTarget / distance;
                    _jobMoveX += dir.X * step;
                    _jobMoveZ += dir.Y * step;
                    _jobMoveYaw = ApproachAngle(_jobMoveYaw, MathF.Atan2(dir.X, -dir.Y), turn);
                    PlaceJobActor(job, (_jobMoveX, _jobMoveZ), MotionCode.Run, _jobMoveYaw);
                    break;
                }

            case JobMovePhase.TurnToCamera:
                {
                    _jobMoveYaw = ApproachAngle(_jobMoveYaw, 0f, turn);
                    PlaceJobActor(job, (_jobMoveX, _jobMoveZ), MotionCode.Stand, _jobMoveYaw);
                    if (MathF.Abs(_jobMoveYaw) <= JobYawSettleEpsilon)
                    {
                        _jobMoveYaw = 0f;
                        PlaceJobActor(job, (_jobMoveX, _jobMoveZ), MotionCode.Stand, 0f);
                        _jobMoverIndex = -1;
                        _jobPhase = JobMovePhase.None;
                    }
                    break;
                }
        }
    }

    public void ApplyFace(int jobIndex, int faceIndex)
    {
        if (jobIndex < 0 || jobIndex >= _jobActors.Count)
        {
            return;
        }

        var job = _jobActors[jobIndex];
        var world = CurrentJobActorWorld(jobIndex);
        var motion = _jobPhase == JobMovePhase.Running ? MotionCode.Run : MotionCode.Stand;
        _actorScene.RemoveActor(job.Handles.Head);
        var head = _playerComposer.ComposeHead(_assetRoot, job.Visuals, faceIndex, world, motion);
        if (motion == MotionCode.Stand)
        {
            ApplySelectPose(head);
        }

        var handles = job.Handles with { Head = head };
        _jobActors[jobIndex] = job with { Handles = handles };
        var intensity = HighlightIntensity(jobIndex, _jobHover, _jobSelection);
        SetHandlesColorAdd(handles, new Vector3(intensity));

        if (_facePreview.Body.IsValid && _facePreviewVisuals is not null)
        {
            _actorScene.RemoveActor(_facePreview.Head);
            var previewHead = _playerComposer.ComposeHead(
                _assetRoot,
                _facePreviewVisuals,
                faceIndex,
                Matrix.Identity,
                MotionCode.Stand,
                visibleInMainPass: false);
            ApplySelectPose(previewHead);
            _facePreview = _facePreview with { Head = previewHead };
            SetHandlesColorAdd(_facePreview, Vector3.Zero);
        }
    }

    private Matrix CurrentJobActorWorld(int jobIndex)
    {
        if (jobIndex == _forwardSubject)
        {
            var active = _jobMoverIndex >= 0;
            return SlotWorld((active ? _jobMoveX : 0f, active ? _jobMoveZ : JobRestZ), active ? _jobMoveYaw : 0f);
        }
        return SlotWorld(JobSlot(jobIndex));
    }

    public int PickRoster(Vector2 cursor, Viewport viewport) =>
        PickCharacter(cursor, viewport, _placed.Count, index => _placed[index].Slot);
    public int PickJob(Vector2 cursor, Viewport viewport) =>
        PickCharacter(cursor, viewport, _jobActors.Count, JobSlot);

    public void SetRosterHighlight(int hoveredIndex, int selectedIndex)
    {
        if (hoveredIndex == _rosterHover && selectedIndex == _rosterSelection)
        {
            return;
        }

        _rosterHover = hoveredIndex;
        _rosterSelection = selectedIndex;
        for (var index = 0; index < _placed.Count; index++)
        {
            var intensity = HighlightIntensity(index, hoveredIndex, selectedIndex);
            SetHandlesColorAdd(_placed[index].Handles, new Vector3(intensity));
        }
    }

    public void SetJobHighlight(int hoveredIndex, int selectedIndex)
    {
        if (hoveredIndex == _jobHover && selectedIndex == _jobSelection)
        {
            return;
        }

        _jobHover = hoveredIndex;
        _jobSelection = selectedIndex;
        for (var index = 0; index < _jobActors.Count; index++)
        {
            var intensity = HighlightIntensity(index, hoveredIndex, selectedIndex);
            SetHandlesColorAdd(_jobActors[index].Handles, new Vector3(intensity));
        }
    }

    public Vector3 JobHeadWorld(int i) => JobFootWorld(i) + new Vector3(0f, HitHeadHeight, 0f);

    public CharSelectInfo? Info(int i)
    {
        if (i < 0 || i >= _placed.Count)
        {
            return null;
        }

        var summary = _placed[i].Summary;
        if (!Enum.IsDefined(summary.Class))
        {
            return null;
        }

        var klass = _classRegistry.Get(summary.Class);
        var broodName = klass.Brood == CharacterBrood.Tempskron ? "Tempskron" : "Morion";
        return new CharSelectInfo(summary.Name, summary.Level, broodName, klass.Name);
    }

    private int PickCharacter(Vector2 cursor, Viewport viewport, int count, Func<int, (float X, float Z)> positionOf)
    {
        var best = -1;
        var bestDepth = float.MaxValue;
        for (var i = 0; i < count; i++)
        {
            var slot = positionOf(i);
            var footWorld = SlotToWorld(slot, ResolveFloorY(slot));
            var foot = viewport.Project(footWorld, _camera.Projection, _camera.View, Matrix.Identity);
            var head = viewport.Project(footWorld + new Vector3(0f, HitHeadHeight, 0f), _camera.Projection, _camera.View, Matrix.Identity);
            if (foot.Z is < 0f or > 1f)
            {
                continue;
            }

            var minY = MathF.Min(foot.Y, head.Y);
            var maxY = MathF.Max(foot.Y, head.Y);
            var cx = foot.X;
            if (cursor.X >= cx - HitHalfWidthPixels && cursor.X <= cx + HitHalfWidthPixels &&
                cursor.Y >= minY && cursor.Y <= maxY && foot.Z < bestDepth)
            {
                best = i;
                bestDepth = foot.Z;
            }
        }
        return best;
    }

    private bool TryResolveRosterVisuals(CharacterSummary summary, out CharacterVisualSet visuals)
    {
        visuals = default;
        if (!Enum.IsDefined(summary.Class))
        {
            _log.Warn($"[CharSelect] '{summary.Name}' has out-of-range class {summary.Class}; skipping (no substitution).");
            return false;
        }

        var playerClass = summary.Class;
        var definition = _classRegistry.Get(playerClass).Definition;
        visuals = CharacterHead.Apply(
            definition.DefaultVisuals,
            playerClass,
            summary.Tier,
            summary.FaceIndex,
            summary.HairIndex);
        // The roster shows the character in its armour, empty-handed.
        visuals = EquipmentVisuals.Apply(
            visuals,
            summary.ArmorKey,
            weapon: "",
            offHand: "",
            summary.InVillage,
            definition.MotionJobMask);
        return true;
    }

    private CharacterVisualSet JobVisuals(PlayerClass playerClass)
    {
        var definition = _classRegistry.Get(playerClass).Definition;
        return definition.DefaultVisuals with
        {
            InVillage = true,
            JobMask = definition.MotionJobMask
        };
    }

    private static PlayerClass[] JobsForBrood(CharacterBrood brood)
    {
        return brood == CharacterBrood.Morion ? MorionJobs : TempscronJobs;
    }

    private void PlaceJobActor(JobActor job, (float X, float Z) slot, MotionCode motion, float yaw)
    {
        var world = SlotWorld(slot, yaw);
        SetHandlesWorld(job.Handles, world);
        if (motion == MotionCode.Stand)
        {
            ApplySelectPose(job.Handles);
            return;
        }

        job.Handles.SetMotion(_actorScene, motion);
    }

    private void ApplySelectPose(PlayerActorHandles handles)
    {
        ApplySelectPose(handles.Body);
        ApplySelectPose(handles.Head);
    }

    // The original select and creation lineups play motion slot 10 (the relaxed stand) directly,
    // not the weapon-dependent Stand that motion selection picks in the field (bare hands resolve to a guard stance).
    private void ApplySelectPose(ActorHandle handle)
    {
        if (!handle.IsValid)
        {
            return;
        }

        try
        {
            _actorScene.SetActorMotionIndex(handle, PristonFormatConstants.CharacterMotionBase);
        }
        catch (Exception exception) when (exception is ArgumentOutOfRangeException or InvalidDataException)
        {
            _actorScene.SetActorMotion(handle, MotionCode.Stand);
        }
    }

    private Vector3 RosterFootWorld(int i)
    {
        var slot = _placed[i].Slot;
        return SlotToWorld(slot, ResolveFloorY(slot));
    }

    private Vector3 JobFootWorld(int i)
    {
        var slot = JobSlot(i);
        return SlotToWorld(slot, ResolveFloorY(slot));
    }

    // PT planar coordinates map to world (x, -z).
    private Matrix SlotWorld((float X, float Z) slot) => SlotWorld(slot, 0f);
    private Matrix SlotWorld((float X, float Z) slot, float yaw) =>
        Matrix.CreateRotationY(yaw) * Matrix.CreateTranslation(SlotToWorld(slot, ResolveFloorY(slot)));

    private static Vector3 SlotToWorld((float X, float Z) slot, float floorY) => WorldSpace.Mirror(new Vector3(slot.X, floorY, slot.Z));

    internal static (float X, float Z) UserCharacterPosition(int characterCount, int index)
    {
        var positions = characterCount == CharacterSlots.Maximum
            ? SixUserCharacterPositions
            : OriginalUserCharacterPositions;
        return positions[index];
    }

    internal static PlayerClass JobClassForBrood(CharacterBrood brood, int index)
    {
        return JobsForBrood(brood)[index];
    }

    private void SetHandlesWorld(PlayerActorHandles handles, Matrix world)
    {
        handles.SetWorld(_actorScene, world);
        if (handles.Weapon.IsValid)
        {
            _actorScene.SetActorWorld(handles.Weapon, world);
        }

        if (handles.OffHand.IsValid)
        {
            _actorScene.SetActorWorld(handles.OffHand, world);
        }
    }

    private void SetHandlesColorAdd(PlayerActorHandles handles, Vector3 colorAdd)
    {
        handles.SetColorAdd(_actorScene, colorAdd);
        if (handles.Weapon.IsValid)
        {
            _actorScene.SetActorColorAdd(handles.Weapon, colorAdd);
        }

        if (handles.OffHand.IsValid)
        {
            _actorScene.SetActorColorAdd(handles.OffHand, colorAdd);
        }
    }

    private static float HighlightIntensity(int index, int hoveredIndex, int selectedIndex)
    {
        if (index == selectedIndex)
        {
            return 120f / 255f;
        }

        return index == hoveredIndex ? 50f / 255f : 0f;
    }

    private float ResolveFloorY((float X, float Z) slot)
    {
        if (_collision != null &&
            _collision.TryGetFloorHeight(WorldSpace.Mirror(new Vector3(slot.X, 200f, slot.Z)), out var y) &&
            MathF.Abs(y) <= PlausibleFloorAbsY)
        {
            return y;
        }

        return MeasuredLoginFloorY;
    }

    private PristonCollisionWorld BuildCollision(PristonStage stage)
    {
        try
        {
            return new PristonCollisionWorld(stage);
        }
        catch (Exception exception)
        {
            _log.Error("[CharSelect] could not build collision data for Login.smd; using the measured floor height.", exception);
            return null;
        }
    }

    private static float ApproachAngle(float current, float target, float maxDelta)
    {
        var delta = MathHelper.WrapAngle(target - current);
        if (MathF.Abs(delta) <= maxDelta)
        {
            return target;
        }

        return MathHelper.WrapAngle(current + MathF.Sign(delta) * maxDelta);
    }
}
