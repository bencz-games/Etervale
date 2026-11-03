using Microsoft.Xna.Framework;
using PristonTale.Client.Net.Presence;
using PristonTale.Engine3D.Diagnostics;
using PristonTale.Engine3D.Formats;
using PristonTale.Engine3D.Rendering;
using PristonTale.Engine3D.Rendering.Lighting;
using PristonTale.Gameplay.Effects;
using PristonTale.Simulation.Characters;
using PristonTale.Simulation.Items.Usable;
using PristonTale.Contracts.Motion;
using PristonTale.Contracts.Units;
using PristonTale.Engine3D;

namespace PristonTale.Gameplay.World.Remote;

// The premium pets that fly beside their owners. A helper pet hovers 10 units to one side of its owner and the Phoenix
// to the other; both close a fifteenth of the distance to their place per 70 Hz frame, play their fly loop while far
// and their idle loop while near, and play their attack when the server reports a strike: the helper's hit lands 30
// frames in, the Phoenix's 60, with the strike's projectile, impact particles and sounds.
public sealed class PremiumPetPresentation : IDisposable
{
    private const float Scale = (GameUnits.FixedScale + 80f) / GameUnits.FixedScale;
    private const float Near = 300f / GameUnits.FixedScale;
    // About the height of a character's head above its feet.
    private const float OwnerHeight = 36f;
    private const float ProjectileSpeed = 1800f / GameUnits.FixedScale * GameUnits.TicksPerSecond;
    private static readonly string[] HelperMeshes = ["Pet_terry.smd", "Pet_nepsys.smd", "Pet_io.smd", "Pet_mute.smd"];

    private readonly IActorScene _scene;
    private readonly string _assetRoot;
    private readonly IClientLog _log;
    private readonly IScriptEffects _scripts;
    private readonly PristonTransientLights _lights;
    private readonly SkillSoundBank _sounds;
    private readonly Task<PristonActor>[] _helpers = new Task<PristonActor>[4];
    private readonly Task<PristonActor> _phoenix;
    private readonly Dictionary<ulong, Owner> _owners = new();
    private readonly List<Projectile> _projectiles = new();
    private readonly Random _random = new();

    public PremiumPetPresentation(IActorScene scene, PristonActorLoader loader, string assetRoot, IScriptEffects scripts,
        PristonTransientLights lights, SkillSoundBank sounds, IClientLog log)
    {
        _scene = scene;
        _assetRoot = assetRoot;
        _log = log;
        _scripts = scripts;
        _lights = lights;
        _sounds = sounds;
        for (var index = 0; index < HelperMeshes.Length; index++)
        {
            var mesh = Path.Combine(assetRoot, "Effect", "LowLevelPet", HelperMeshes[index]);
            var motion = Path.Combine(assetRoot, "Effect", "LowLevelPet", "Pet_motion.smb");
            _helpers[index] = Prepare(loader, mesh, motion);
        }

        var phoenix = Path.Combine(assetRoot, "Effect", "PcBangPet", "Pet_Phoenimin", "Phoenix");
        _phoenix = Prepare(loader, phoenix + ".smd", phoenix + ".smb");
        sounds?.Preload(Sounds(PremiumEffectKind.HelpPetTerry).Concat(Sounds(PremiumEffectKind.HelpPetNepsys)).Concat(Sounds(PremiumEffectKind.HelpPetIo)));
    }

    public int OwnerCount => _owners.Count;

    // The pets drawn now, for diagnostics and captures.
    internal IEnumerable<(ulong Owner, PremiumEffectKind Kind, Vector3 Position)> Pets =>
        _owners.SelectMany(pair => new[] { pair.Value.Helper, pair.Value.Phoenix }.Where(pet => pet is { HasActor: true })
            .Select(pet => (pair.Key, pet.Kind, pet.Position)));

    // The helper pet beside a player (kind 1 to 4) and where it is drawn, null until its model is placed.
    public bool TryGetHelper(ulong ownerId, out int kind, out Vector3? position)
    {
        var helper = _owners.TryGetValue(ownerId, out var owner) ? owner.Helper : null;
        kind = helper is null ? 0 : HelpPets.KindOf(helper.Kind);
        position = helper is { HasActor: true } ? helper.Position : null;
        return helper is not null;
    }

    // The pets each player shows, from the spawns of players, and the strikes the server reports.
    public void Observe(PresenceUpdate update, Func<ulong, Vector3?> targetPosition)
    {
        foreach (var spawn in update.Spawns)
        {
            if (spawn.Player is { } player)
            {
                SetPets(spawn.EntityId, player.HelperPet, player.Phoenix);
            }
        }

        foreach (var removed in update.Removed)
        {
            RemoveOwner(removed);
        }

        foreach (var effect in update.ItemEffects)
        {
            if (effect.Effect == ItemEffectKind.PetAttack && _owners.TryGetValue(effect.Entity, out var owner))
            {
                var kind = (PremiumEffectKind)effect.ItemCode;
                var pet = kind == PremiumEffectKind.PhoenixPet ? owner.Phoenix : owner.Helper;
                if (pet is not null)
                {
                    pet.BeginAttack(effect.Target);
                }
            }
        }
    }

    public void SetPets(ulong ownerId, int helper, bool phoenix)
    {
        if (!_owners.TryGetValue(ownerId, out var owner))
        {
            if (helper == 0 && !phoenix)
            {
                return;
            }

            owner = new Owner();
            _owners[ownerId] = owner;
        }

        var helperKind = helper is >= 1 and <= HelpPets.Kinds ? HelpPets.Effect(helper) : (PremiumEffectKind?)null;
        if (owner.Helper?.Kind != helperKind)
        {
            owner.Helper?.Remove(_scene);
            owner.Helper = helperKind is { } kind ? new Pet(kind) : null;
        }

        if (phoenix != (owner.Phoenix is not null))
        {
            owner.Phoenix?.Remove(_scene);
            owner.Phoenix = phoenix ? new Pet(PremiumEffectKind.PhoenixPet) : null;
        }

        if (owner.Helper is null && owner.Phoenix is null)
        {
            _owners.Remove(ownerId);
        }
    }

    public void RemoveOwner(ulong ownerId)
    {
        if (_owners.Remove(ownerId, out var owner))
        {
            owner.Helper?.Remove(_scene);
            owner.Phoenix?.Remove(_scene);
        }
    }

    // pose: the owner's drawn position and facing (yaw), or null when it is not drawn.
    public void Update(float dt, Func<ulong, (Vector3 Position, float Facing)?> pose, Func<ulong, Vector3?> targetPosition, Vector3 listener)
    {
        var ticks = dt * GameUnits.TicksPerSecond;
        foreach (var (ownerId, owner) in _owners)
        {
            if (pose(ownerId) is not { } at)
            {
                continue;
            }

            _ownerPets[0] = owner.Helper;
            _ownerPets[1] = owner.Phoenix;
            foreach (var pet in _ownerPets)
            {
                if (pet is null || !EnsureActor(pet, at.Position))
                {
                    continue;
                }

                var struck = pet.Step(ticks, at.Position, at.Facing, targetPosition);
                _scene.SetActorWorld(pet.Handle, Matrix.CreateScale(Scale) * Matrix.CreateRotationY(pet.Facing) * Matrix.CreateTranslation(pet.Position));
                _scene.SeekActorMotion(pet.Handle, (int)pet.Frame);
                if (struck && targetPosition(pet.Target) is { } target)
                {
                    Fire(pet, target + Vector3.Up * 20f, listener);
                }
            }
        }

        StepProjectiles(dt);
    }

    private bool EnsureActor(Pet pet, Vector3 owner)
    {
        if (pet.HasActor)
        {
            return true;
        }

        var preparation = pet.Kind == PremiumEffectKind.PhoenixPet ? _phoenix : _helpers[pet.Kind - PremiumEffectKind.HelpPetTerry];
        if (!preparation.IsCompletedSuccessfully)
        {
            return false;
        }

        pet.Position = owner + new Vector3(30f, OwnerHeight + 10f, 30f);
        pet.Handle = _scene.AddActor(new PristonActorInstance(preparation.Result, Matrix.CreateTranslation(pet.Position), MotionCode.Stand));
        _scene.SetActorPlaybackRate(pet.Handle, 0f);
        pet.HasActor = true;
        return true;
    }

    private void Fire(Pet pet, Vector3 target, Vector3 listener)
    {
        var sounds = Sounds(pet.Kind);
        _sounds?.Play(sounds[_random.Next(sounds.Length)], pet.Position, listener);
        switch (pet.Kind)
        {
            case PremiumEffectKind.HelpPetIo:
            case PremiumEffectKind.HelpPetMute:
                Spawn("LightStart1", pet.Position);
                Spawn("LightHIt1", target);
                _lights?.Add(target, new Color(255, 255, 255), 120, 2);
                break;
            default:
                var ice = pet.Kind == PremiumEffectKind.HelpPetNepsys;
                var trail = Spawn(ice ? "IceShot1" : "FireShot1", pet.Position);
                _projectiles.Add(new Projectile(trail, pet.Position, target, ice));
                break;
        }
    }

    private void StepProjectiles(float dt)
    {
        for (var index = _projectiles.Count - 1; index >= 0; index--)
        {
            var projectile = _projectiles[index];
            var toTarget = projectile.Target - projectile.Position;
            var step = ProjectileSpeed * dt;
            if (toTarget.Length() <= Math.Max(step, 5f))
            {
                _projectiles.RemoveAt(index);
                projectile.Trail?.Stop();
                Spawn(projectile.Ice ? "IceHIt1" : "FireHIt1", projectile.Target);
                _lights?.Add(projectile.Target, projectile.Ice ? new Color(150, 150, 255) : new Color(255, 200, 100), 120, 2);
                continue;
            }

            projectile.Position += Vector3.Normalize(toTarget) * step;
            projectile.Trail?.MoveTo(projectile.Position);
        }
    }

    private IScriptEffect Spawn(string name, Vector3 position) => _scripts?.Spawn(name, position, Matrix.Identity);

    private static string[] Sounds(PremiumEffectKind kind) => kind switch
    {
        PremiumEffectKind.HelpPetNepsys => ["wav/Effects/Skill/Morion/EnchantWpn_magic_C 1.wav", "wav/Effects/Skill/Morion/EnchantWpn_magic_C 2.wav"],
        PremiumEffectKind.HelpPetIo or PremiumEffectKind.HelpPetMute =>
            ["wav/Effects/Skill/Morion/EnchantWpn_magic_L 1.wav", "wav/Effects/Skill/Morion/EnchantWpn_magic_L 2.wav"],
        _ => ["wav/Effects/Skill/Morion/Firebolt 1.wav", "wav/Effects/Skill/Morion/Firebolt 2.wav"]
    };

    private Task<PristonActor> Prepare(PristonActorLoader loader, string mesh, string motion)
    {
        var task = Task.Run(async () =>
        {
            var actor = loader.LoadSkinned(mesh, motion);
            await _scene.PrepareActorResourcesAsync(actor);
            return actor;
        });
        _ = task.ContinueWith(failed => _log.Warn($"[Pets] {Path.GetFileName(mesh)} unavailable: {failed.Exception?.GetBaseException().Message}"),
            CancellationToken.None, TaskContinuationOptions.OnlyOnFaulted, TaskScheduler.Default);
        return task;
    }

    public void Dispose()
    {
        foreach (var owner in _owners.Values)
        {
            owner.Helper?.Remove(_scene);
            owner.Phoenix?.Remove(_scene);
        }

        _owners.Clear();
    }

    private sealed class Owner
    {
        public Pet Helper { get; set; }
        public Pet Phoenix { get; set; }
    }

    private sealed class Projectile(IScriptEffect trail, Vector3 position, Vector3 target, bool ice)
    {
        public IScriptEffect Trail { get; } = trail;
        public Vector3 Position { get; set; } = position;
        public Vector3 Target { get; } = target;
        public bool Ice { get; } = ice;
    }

    // One pet's place, animation frame (keyframes of its clip) and attack.
    // An owner's helper pet and Phoenix, walked each frame through one reused pair.
    private readonly Pet[] _ownerPets = new Pet[2];

    private sealed class Pet(PremiumEffectKind kind)
    {
        public PremiumEffectKind Kind { get; } = kind;
        public ActorHandle Handle { get; set; }
        public bool HasActor { get; set; }
        public Vector3 Position { get; set; }
        public float Facing { get; set; }
        public float Frame { get; set; }
        public ulong Target { get; private set; }
        private bool _attacking;
        private bool _struck;

        private bool Phoenix => Kind == PremiumEffectKind.PhoenixPet;

        // Frame ranges of the clips: idle, fly, attack start, hit and end.
        private (int Start, int End) Idle => Phoenix ? (10, 74) : (80, 230);
        private (int Start, int End) Fly => Phoenix ? (80, 145) : (10, 70);
        private (int Start, int Hit, int End) Attack => Kind switch
        {
            PremiumEffectKind.PhoenixPet => (150, 180, 200),
            PremiumEffectKind.HelpPetIo or PremiumEffectKind.HelpPetMute => (310, 340, 370),
            _ => (240, 270, 300)
        };

        public void BeginAttack(ulong target)
        {
            Target = target;
            _attacking = true;
            _struck = false;
            Frame = Attack.Start;
        }

        // Advances by ticks of the 70 Hz clock; true on the frame the attack's hit lands.
        public bool Step(float ticks, Vector3 owner, float ownerFacing, Func<ulong, Vector3?> targetPosition)
        {
            var side = Phoenix ? -10f : 10f;
            var height = OwnerHeight + (_attacking ? 10f : 5f);
            var place = _attacking
                ? owner + Forward(ownerFacing) * (Phoenix ? -2f : 2f)
                : owner + Forward(ownerFacing + MathHelper.PiOver2) * side;
            place.Y = owner.Y + height;
            var offset = place - Position;
            var far = offset.Length() > Near;
            if (far)
            {
                Position += offset * (1f - MathF.Pow(14f / 15f, ticks));
            }

            Facing = _attacking && targetPosition(Target) is { } target && Vector3.DistanceSquared(target, Position) > 1f
                ? WorldSpace.RenderYaw(target - Position)
                : ownerFacing;
            if (_attacking)
            {
                var attack = Attack;
                Frame += Phoenix ? ticks / 2f : ticks;
                var hit = !_struck && Frame >= attack.Hit;
                _struck |= hit;
                if (Frame >= attack.End)
                {
                    _attacking = false;
                    Frame = Idle.Start;
                }

                return hit;
            }

            var (start, end) = far ? Fly : Idle;
            if (Frame < start || Frame >= end)
            {
                Frame = start;
            }

            Frame += ticks;
            if (Frame >= end)
            {
                Frame = start + (Frame - end);
            }

            return false;
        }

        public void Remove(IActorScene scene)
        {
            if (HasActor)
            {
                scene.RemoveActor(Handle);
                HasActor = false;
            }
        }

        private static Vector3 Forward(float yaw) => new(MathF.Sin(yaw), 0f, MathF.Cos(yaw));
    }
}
