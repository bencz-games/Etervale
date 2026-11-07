using Microsoft.Xna.Framework;
using PristonTale.Engine3D.Diagnostics;
using PristonTale.Engine3D.Formats;
using PristonTale.Engine3D.Rendering;
using PristonTale.Contracts.Motion;

namespace PristonTale.Gameplay.World.Remote;

// The crown over every player of the clan holding Bless Castle within CrownRange of the viewer: weapons/Crown.smd
// floats AboveHead over the head bone (the top of the head, then 10 more), bobs up and down by Bob once a second and
// turns once every SpinMs. Without a head bone the head is taken HeadHeight above the feet.
public sealed class CrownPresentation
{
    public const float HeadHeight = 36f;
    private const float AboveHead = 20f;
    private const float Bob = 0.5f;
    private const double BobMs = 1024;
    private const double SpinMs = 16384;
    private const float CrownRange = 12 * 64f;

    private readonly IActorScene _scene;
    private readonly Task<PristonActor> _crown;
    private readonly Dictionary<ulong, ActorHandle> _shown = new();
    private readonly List<ulong> _gone = new();
    private readonly HashSet<ulong> _seen = new();

    public CrownPresentation(IActorScene scene, PristonActorLoader loader, string assetRoot, IClientLog log)
    {
        _scene = scene;
        var path = Path.Combine(assetRoot, "weapons", "Crown.smd");
        _crown = Task.Run(async () =>
        {
            var actor = loader.LoadRigid(path);
            await _scene.PrepareActorResourcesAsync(actor);
            return actor;
        });
        _ = _crown.ContinueWith(failed => log.Warn($"[Crown] {path} unavailable: {failed.Exception?.GetBaseException().Message}"),
            CancellationToken.None, TaskContinuationOptions.OnlyOnFaulted, TaskScheduler.Default);
    }

    // Called once a frame: Show for each player of the owner clan near the viewer, then Finish.
    public void Show(ulong entityId, Vector3 head, Vector3 viewer, double nowMs)
    {
        if (!_crown.IsCompletedSuccessfully || Vector3.DistanceSquared(head, viewer) > CrownRange * CrownRange)
        {
            return;
        }

        _seen.Add(entityId);
        if (!_shown.TryGetValue(entityId, out var handle))
        {
            handle = _scene.AddActor(new PristonActorInstance(_crown.Result, Matrix.Identity, MotionCode.Stand));
            _shown[entityId] = handle;
        }

        var phase = nowMs % BobMs / BobMs;
        var bob = (float)(phase < 0.5 ? phase * 2 : 2 - phase * 2) * Bob;
        var spin = (float)(nowMs % SpinMs / SpinMs * MathHelper.TwoPi);
        _scene.SetActorWorld(handle, Matrix.CreateRotationY(spin) * Matrix.CreateTranslation(head + Vector3.Up * (AboveHead + bob)));
    }

    // Crowns not shown this frame go.
    public void Finish()
    {
        _gone.Clear();
        foreach (var entityId in _shown.Keys)
        {
            if (!_seen.Contains(entityId))
            {
                _gone.Add(entityId);
            }
        }

        foreach (var entityId in _gone)
        {
            _scene.RemoveActor(_shown[entityId]);
            _shown.Remove(entityId);
        }

        _seen.Clear();
    }
}
