using Microsoft.Xna.Framework;
using PristonTale.Contracts.Characters;
using PristonTale.Contracts.Motion;
using PristonTale.Engine3D.Formats;
using PristonTale.Engine3D.Rendering;
using PristonTale.Gameplay.World.Remote;
using PristonTale.Simulation.Characters;

namespace PristonTale.Tests;

// The parts of a player's look (body, head, weapon) are read and prepared once and shared by every look that wears
// them: another weapon or a village stance reads only what it does not share.
internal static class PlayerPartsTests
{
    public static IEnumerable<(string, Func<Task>)> Cases()
    {
        yield return ("looks that share a body, head or weapon read and prepare each part once and show the same model", SharedAsync);
    }

    private static async Task SharedAsync()
    {
        var root = FindAssets();
        if (root is null)
        {
            Console.WriteLine("  (skipped: no assets next to the repository)");
            return;
        }

        var scene = new PartScene();
        var composer = new PlayerActorComposer(scene, new PristonActorLoader());
        var plain = new CharacterClassRegistry().Get(PlayerClass.Mechanician).Definition.DefaultVisuals;
        var axe = plain with { WeaponKey = "WA101" };
        var claw = plain with { WeaponKey = "WC101" };
        await composer.PrepareAsync(root, axe, 0);
        await composer.PrepareAsync(root, claw, 0);
        await composer.PrepareAsync(root, axe with { InVillage = true }, 0);
        Assert.True(scene.Prepared.Count == 4 && scene.Prepared.Distinct().Count() == 4,
            $"{scene.Prepared.Count} parts were prepared for a body, a head and two weapons");

        composer.Compose(root, axe, 0, Matrix.Identity, MotionCode.Stand);
        composer.Compose(root, claw, 0, Matrix.Identity, MotionCode.Stand);
        var bodies = scene.Added.Where(actor => ReferenceEquals(actor, scene.Added[0])).Count();
        Assert.True(bodies == 2 && scene.Added.Distinct().Count() == 4, "two looks in the same armour did not show the same body model");
    }

    // The repository's assets, when they hold the char folder this case reads.
    private static string? FindAssets() =>
        PristonTale.Assets.AssetFiles.FindAssetsRoot() is { } root && Directory.Exists(Path.Combine(root, "char")) ? root : null;

    private sealed class PartScene : IActorScene
    {
        private int _next;

        public List<PristonActor> Prepared { get; } = [];

        public List<PristonActor> Added { get; } = [];

        public Task PrepareActorResourcesAsync(PristonActor actor)
        {
            lock (Prepared)
            {
                Prepared.Add(actor);
            }

            return Task.CompletedTask;
        }

        public ActorHandle AddActor(PristonActorInstance instance)
        {
            Added.Add(instance.Actor);
            return new ActorHandle(_next++, 1);
        }

        public void AttachActor(ActorHandle child, ActorHandle parent, string boneName, string? cheerBoneName = null)
        {
        }

        public void SetActorWorld(ActorHandle handle, Matrix world) { }
        public void SetActorMotion(ActorHandle handle, MotionCode motion) { }
        public void SetActorMotionIndex(ActorHandle handle, int motionIndex) { }
        public void RestartActorMotion(ActorHandle handle, MotionCode motion) { }
        public void SetActorPlaybackRate(ActorHandle handle, float rateFactor) { }
        public void RemoveActor(ActorHandle handle) { }
    }
}
