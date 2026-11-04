using Microsoft.Xna.Framework;
using PristonTale.Engine3D.Formats;
using PristonTale.Engine3D.Rendering;
using PristonTale.Simulation.Characters;
using PristonTale.Contracts.Motion;

namespace PristonTale.Gameplay.World.Remote;

public readonly record struct PlayerActorHandles(ActorHandle Body, ActorHandle Head)
{
    // The most actors an entity draws with: a player's body, head, weapon and off-hand (a monster has one).
    public const int Most = 4;

    public ActorHandle Weapon { get; init; } = ActorHandle.None;
    public ActorHandle OffHand { get; init; } = ActorHandle.None;

    // The off-hand actor is a second copy of the weapon (the Assassin's left dagger), glowing as the weapon does.
    public bool Twin { get; init; }

    // The body and the head move and play as one (the weapons follow by attachment).
    public void SetWorld(IActorScene scene, Matrix world)
    {
        scene.SetActorWorld(Body, world);
        scene.SetActorWorld(Head, world);
    }

    public void SetMotion(IActorScene scene, MotionCode motion)
    {
        scene.SetActorMotion(Body, motion);
        scene.SetActorMotion(Head, motion);
    }

    public void RestartMotion(IActorScene scene, MotionCode motion)
    {
        scene.RestartActorMotion(Body, motion);
        scene.RestartActorMotion(Head, motion);
    }

    public void SetMotionIndex(IActorScene scene, int motionIndex)
    {
        scene.SetActorMotionIndex(Body, motionIndex);
        scene.SetActorMotionIndex(Head, motionIndex);
    }

    public void SeekMotion(IActorScene scene, int framesFromStart)
    {
        scene.SeekActorMotion(Body, framesFromStart);
        scene.SeekActorMotion(Head, framesFromStart);
    }

    public void SetPlaybackRate(IActorScene scene, float rate)
    {
        scene.SetActorPlaybackRate(Body, rate);
        scene.SetActorPlaybackRate(Head, rate);
    }

    public void SetColorAdd(IActorScene scene, Vector3 colorAdd)
    {
        scene.SetActorColorAdd(Body, colorAdd);
        scene.SetActorColorAdd(Head, colorAdd);
    }

    // The body first: it carries the skeleton the others follow.
    public int CopyTo(Span<ActorHandle> destination)
    {
        var count = 0;
        foreach (var handle in (ReadOnlySpan<ActorHandle>)[Body, Head, Weapon, OffHand])
        {
            if (handle.IsValid && count < destination.Length)
            {
                destination[count++] = handle;
            }
        }

        return count;
    }

    public void Remove(IActorScene scene)
    {
        if (Weapon.IsValid)
        {
            scene.RemoveActor(Weapon);
        }

        if (OffHand.IsValid)
        {
            scene.RemoveActor(OffHand);
        }

        if (Head.IsValid)
        {
            scene.RemoveActor(Head);
        }

        if (Body.IsValid)
        {
            scene.RemoveActor(Body);
        }
    }
}

public interface IPlayerActorComposer
{
    Task PrepareAsync(string assetRoot, CharacterVisualSet visuals, int faceIndex) => Task.CompletedTask;
    PlayerActorHandles Compose(string assetRoot, CharacterVisualSet visuals, int faceIndex, Matrix world, MotionCode motion);
}

// Each part of a look (body, head, weapon, shield) is read and prepared once and shared by every look that wears it, so
// two players in the same armour, or one player stepping into a village (another stance, the same parts), read nothing
// again. A look is ready when its parts are.
public sealed class PlayerActorComposer(IActorScene scene, PristonActorLoader actorLoader) : IPlayerActorComposer
{
    private readonly IActorScene _scene = scene;
    private readonly PristonActorLoader _actorLoader = actorLoader;
    private readonly Dictionary<(string Root, CharacterVisualSet Visuals, int Face), Task<Prepared>> _prepared = new();
    private readonly Dictionary<(string Path, bool Rigid), Task<PristonActor>> _parts = new();

    public Task PrepareAsync(string assetRoot, CharacterVisualSet visuals, int faceIndex)
    {
        var key = (assetRoot, visuals, faceIndex);
        if (!_prepared.TryGetValue(key, out var preparation))
        {
            var (body, head, weapon, offHand) = PartPaths(assetRoot, visuals, faceIndex);
            preparation = Combine(Part(assetRoot, body, rigid: false), Part(assetRoot, head, rigid: false),
                weapon is null ? null : Part(assetRoot, weapon, rigid: true), offHand is null ? null : Part(assetRoot, offHand, rigid: true));
            _prepared.Add(key, preparation);
        }

        return preparation;
    }

    private static (string Body, string Head, string Weapon, string OffHand) PartPaths(string root, CharacterVisualSet visuals, int face) => (
        Path.Combine(root, visuals.Body.InxExtensionPath),
        Path.Combine(root, CharacterFace.ResolveHeadPath(visuals, face)),
        string.IsNullOrEmpty(visuals.WeaponKey) ? null : Path.Combine(root, EquipmentVisuals.MeshPath(visuals.WeaponKey)),
        string.IsNullOrEmpty(visuals.OffHandKey) ? null : Path.Combine(root, EquipmentVisuals.MeshPath(visuals.OffHandKey)));

    private Task<PristonActor> Part(string root, string path, bool rigid)
    {
        if (!_parts.TryGetValue((path, rigid), out var part))
        {
            part = Task.Run(async () =>
            {
                var actor = ReadPart(root, path, rigid);
                await _scene.PrepareActorResourcesAsync(actor);
                return actor;
            });
            _parts.Add((path, rigid), part);
        }

        return part;
    }

    private static async Task<Prepared> Combine(Task<PristonActor> body, Task<PristonActor> head, Task<PristonActor> weapon, Task<PristonActor> offHand) =>
        new(await body, await head, weapon is null ? null : await weapon, offHand is null ? null : await offHand);

    private PristonActor ReadPart(string root, string path, bool rigid) => rigid ? _actorLoader.LoadRigid(path) : _actorLoader.Load(root, path);

    // A look composed before its preparation: the parts already prepared, the others read now.
    private Prepared Load(string root, CharacterVisualSet visuals, int face)
    {
        var (body, head, weapon, offHand) = PartPaths(root, visuals, face);
        return new Prepared(Ready(root, body, false), Ready(root, head, false), weapon is null ? null : Ready(root, weapon, true),
            offHand is null ? null : Ready(root, offHand, true));
    }

    private PristonActor Ready(string root, string path, bool rigid) =>
        _parts.TryGetValue((path, rigid), out var part) && part.IsCompletedSuccessfully ? part.Result : ReadPart(root, path, rigid);

    public PlayerActorHandles Compose(string assetRoot, CharacterVisualSet visuals, int faceIndex, Matrix world, MotionCode motion)
    {
        return Compose(assetRoot, visuals, faceIndex, world, motion, visibleInMainPass: true);
    }

    public PlayerActorHandles ComposePortrait(string assetRoot, CharacterVisualSet visuals, int faceIndex, MotionCode motion)
    {
        return Compose(assetRoot, visuals, faceIndex, Matrix.Identity, motion, visibleInMainPass: false);
    }

    private PlayerActorHandles Compose(
        string assetRoot,
        CharacterVisualSet visuals,
        int faceIndex,
        Matrix world,
        MotionCode motion,
        bool visibleInMainPass)
    {
        var prepared = ResolvePrepared(assetRoot, visuals, faceIndex);

        var context = new PristonMotionContext(EquipmentVisuals.MotionIndex(visuals.WeaponKey), visuals.JobMask, visuals.InVillage ? 1 : 2);
        var handles = new PlayerActorHandles(ActorHandle.None, ActorHandle.None);
        try
        {
            handles = handles with
            {
                Body = _scene.AddActor(new PristonActorInstance(prepared.Body, world, motion)
                {
                    MotionContext = context,
                    VisibleInMainPass = visibleInMainPass
                })
            };
            handles = handles with
            {
                Head = _scene.AddActor(new PristonActorInstance(prepared.Head, world, motion)
                {
                    MotionContext = context,
                    VisibleInMainPass = visibleInMainPass
                })
            };
            if (prepared.Weapon is not null)
            {
                handles = handles with
                {
                    Weapon = _scene.AddActor(new PristonActorInstance(prepared.Weapon, world, MotionCode.Stand)
                    {
                        VisibleInMainPass = visibleInMainPass
                    })
                };
                var held = EquipmentVisuals.WeaponBone(visuals, visuals.InVillage);
                var back = EquipmentVisuals.WeaponBone(visuals, village: true);
                _scene.AttachActor(handles.Weapon, handles.Body, held, back == held ? null : back);
                if (EquipmentVisuals.WeaponHover(visuals) is { } hover)
                {
                    _scene.SetActorAttachOffset(handles.Weapon, hover.Offset, hover.Lift);
                }

                if (prepared.OffHand is null && EquipmentVisuals.TwinBone(visuals, visuals.InVillage) is { } twinHeld)
                {
                    handles = handles with
                    {
                        OffHand = _scene.AddActor(new PristonActorInstance(prepared.Weapon, world, MotionCode.Stand)
                        {
                            VisibleInMainPass = visibleInMainPass
                        }),
                        Twin = true
                    };
                    var twinBack = EquipmentVisuals.TwinBone(visuals, village: true);
                    _scene.AttachActor(handles.OffHand, handles.Body, twinHeld, twinBack == twinHeld ? null : twinBack);
                }
            }

            if (prepared.OffHand is not null)
            {
                handles = handles with
                {
                    OffHand = _scene.AddActor(new PristonActorInstance(prepared.OffHand, world, MotionCode.Stand)
                    {
                        VisibleInMainPass = visibleInMainPass
                    })
                };
                _scene.AttachActor(handles.OffHand, handles.Body, "Bip01 L Forearm");
            }

            return handles;
        }
        catch
        {
            handles.Remove(_scene);
            throw;
        }
    }

    public ActorHandle ComposeHead(
        string assetRoot,
        CharacterVisualSet visuals,
        int faceIndex,
        Matrix world,
        MotionCode motion,
        bool visibleInMainPass = true)
    {
        var prepared = ResolvePrepared(assetRoot, visuals, faceIndex);
        var context = new PristonMotionContext(EquipmentVisuals.MotionIndex(visuals.WeaponKey), visuals.JobMask, visuals.InVillage ? 1 : 2);
        return _scene.AddActor(new PristonActorInstance(prepared.Head, world, motion)
        {
            MotionContext = context,
            VisibleInMainPass = visibleInMainPass
        });
    }

    private Prepared ResolvePrepared(string assetRoot, CharacterVisualSet visuals, int faceIndex)
    {
        if (!_prepared.TryGetValue((assetRoot, visuals, faceIndex), out var preparation))
        {
            return Load(assetRoot, visuals, faceIndex);
        }

        if (!preparation.IsCompletedSuccessfully)
        {
            throw new InvalidOperationException("Player actor preparation is incomplete.");
        }

        return preparation.Result;
    }

    private sealed record Prepared(PristonActor Body, PristonActor Head, PristonActor Weapon, PristonActor OffHand);
}
