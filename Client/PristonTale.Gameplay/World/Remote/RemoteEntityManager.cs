using PristonTale.Client.Net.Presence;
using PristonTale.Client.Net.WorldCatalog;
using PristonTale.Engine3D;
using PristonTale.Engine3D.Diagnostics;
using PristonTale.Engine3D.Formats;
using PristonTale.Engine3D.Rendering;
using PristonTale.Simulation.Characters;
using PristonTale.Simulation.Physics;
using XnaVector3 = Microsoft.Xna.Framework.Vector3;
using NumVector3 = System.Numerics.Vector3;
using PristonTale.Contracts.Motion;
using PristonTale.Contracts.World;
using PristonTale.Contracts.Combat;
using PristonTale.Contracts.Characters;
using PristonTale.Simulation.Items;
using PristonTale.Simulation.World.Monsters;

namespace PristonTale.Gameplay.World.Remote;

public sealed partial class RemoteEntityManager
{
    private readonly IActorScene _scene;
    private readonly IPlayerActorComposer _playerComposer;
    private readonly IModelActorComposer _monsterComposer;
    private readonly CharacterClassRegistry _classRegistry;
    private readonly string _assetRoot;
    private readonly IClientLog _log;

    private WorldRenderCatalog _catalog;

    private readonly Dictionary<ulong, RemoteEntityBase> _entities = new();
    // Summons and their owners: allies that cannot be picked as targets.
    private readonly Dictionary<ulong, ulong> _summonOwners = new();
    private readonly RemoteCollisionRoster _collisionRoster = new();

    private readonly Dictionary<ulong, EntitySpawnInfo> _pendingModels = new();
    private readonly Dictionary<ulong, (EntitySpawnInfo Spawn, Task Preparation)> _preparingActors = new();
    private readonly RemoteServerClock _clock = new();

    private ulong _selfEntityId;

    private Action<EntitySpawnInfo> _onSelfSpawn;
    private Action<EntityStateInfo> _onSelfState;
    private Action<CombatEventInfo> _onSelfCombatEvent;

    public RemoteEntityManager(
        IActorScene scene,
        IPlayerActorComposer playerComposer,
        IModelActorComposer monsterComposer,
        CharacterClassRegistry classRegistry,
        string assetRoot,
        WorldRenderCatalog catalog = null,
        IClientLog log = null)
    {
        _scene = scene;
        _playerComposer = playerComposer;
        _monsterComposer = monsterComposer;
        _classRegistry = classRegistry;
        _assetRoot = assetRoot;
        _catalog = catalog ?? WorldRenderCatalog.Empty;
        _log = log ?? ConsoleClientLog.Instance;
    }

    public int Count => _entities.Count;

    public ulong SelfEntityId => _selfEntityId;

    public event Action<int, MotionCode, XnaVector3> NpcMotionStarted;

    // Another player entered a motion: its class code, the motion and where it stands.
    public event Action<PlayerClass, MotionCode, XnaVector3> PlayerMotionStarted;

    public void SetSelfSink(
        Action<EntitySpawnInfo> onSelfSpawn,
        Action<EntityStateInfo> onSelfState,
        Action<CombatEventInfo> onSelfCombatEvent = null)
    {
        _onSelfSpawn = onSelfSpawn;
        _onSelfState = onSelfState;
        _onSelfCombatEvent = onSelfCombatEvent;
    }

    public void SetCatalog(WorldRenderCatalog catalog)
    {
        if (catalog == null)
        {
            return;
        }

        _catalog = catalog;
        if (_pendingModels.Count == 0)
        {
            return;
        }

        var pending = new List<EntitySpawnInfo>(_pendingModels.Values);
        _pendingModels.Clear();
        foreach (var spawn in pending)
        {
            ApplySpawn(spawn);
        }
    }

    public void Apply(PresenceUpdate update) => Apply(update, mirror: false);

    // An update as the server sends it: positions and facings in its space, mirrored into the world's as they are read.
    public void ApplyServer(PresenceUpdate update) => Apply(update, mirror: true);

    private void Apply(PresenceUpdate update, bool mirror)
    {
        if (update.ServerTimeMs is { } serverTime)
        {
            _clock.Observe(serverTime);
        }

        var eventTime = _clock.Stamp(update.ServerTimeMs);
        foreach (var received in update.Spawns)
        {
            var spawn = mirror ? received with { Position = WorldSpace.Mirror(received.Position), Facing = WorldSpace.MirrorFacing(received.Facing) } : received;
            _collisionRoster.Spawn(spawn);
            ApplySpawn(spawn);
            if (_entities.TryGetValue(spawn.EntityId, out var entity))
            {
                entity.SetRates(spawn.MoveSpeed, spawn.AttackRate);
            }
        }

        for (var index = 0; index < update.States.Count; index++)
        {
            var state = update.States[index];
            if (mirror)
            {
                state = state with { Position = WorldSpace.Mirror(state.Position), Facing = WorldSpace.MirrorFacing(state.Facing) };
            }

            _collisionRoster.Update(in state);
            ApplyState(in state);
        }

        foreach (var combatEvent in update.Events)
        {
            var entityId = combatEvent.Kind == CombatEventKind.SwingBegin ? combatEvent.AttackerId : combatEvent.TargetId;
            if (entityId == _selfEntityId)
            {
                _onSelfCombatEvent?.Invoke(combatEvent);
            }
            else if (_entities.TryGetValue(entityId, out var entity))
            {
                entity.ApplyCombatEvent(combatEvent, eventTime);
            }
        }

        foreach (var removed in update.Removed)
        {
            ApplyRemoved(removed);
        }
    }

    private readonly Dictionary<ulong, PlayerAppearanceInfo> _playerAppearances = new();
    private readonly Dictionary<ulong, string> _shopSigns = new();

    // No sign, for views without a world; never written.
    public static readonly Dictionary<ulong, string> NoShopSigns = new();

    // The signs of the players selling from a personal shop, the local player's included, as their bubbles read them.
    // Read only: a dictionary rather than its interface so the HUD walks it every frame without allocating.
    public Dictionary<ulong, string> ShopSigns => _shopSigns;

    private void ApplySpawn(EntitySpawnInfo spawn, bool publish = false)
    {
        if (spawn.Player is { ShopSign: var sign })
        {
            if (sign.Length > 0)
            {
                _shopSigns[spawn.EntityId] = $"{spawn.Player.Name}: {sign}";
            }
            else
            {
                _shopSigns.Remove(spawn.EntityId);
            }
        }

        if (spawn.IsSelf)
        {
            _selfEntityId = spawn.EntityId;
            _onSelfSpawn?.Invoke(spawn);
            return;
        }

        // A new sign alone changes no model.
        if (_entities.ContainsKey(spawn.EntityId) && !publish
            && (spawn.Player is null || (_playerAppearances.TryGetValue(spawn.EntityId, out var known) && known with { ShopSign = spawn.Player.ShopSign } == spawn.Player)))
        {
            if (spawn.Player is not null)
            {
                _playerAppearances[spawn.EntityId] = spawn.Player;
            }

            if (_preparingActors.Remove(spawn.EntityId, out var superseded))
            {
                _ = ObservePreparationAsync(superseded.Preparation);
            }

            return;
        }

        Task preparation = null;
        if (spawn.Kind == EntityKind.Player && spawn.Player is not null && TryResolveVisuals(spawn.Player.Class, out var visuals))
        {
            visuals = ResolvePlayerHead(visuals, spawn.Player);
            preparation = _playerComposer.PrepareAsync(_assetRoot, visuals, spawn.Player.FaceIndex);
        }
        else if (spawn.Kind is EntityKind.Monster or EntityKind.Npc)
        {
            var info = ModelInfo(spawn);
            if (info is not null && !string.IsNullOrEmpty(info.ModelInxPath))
            {
                preparation = _monsterComposer.PrepareAsync(_assetRoot, info.ModelInxPath);
            }
        }

        if (preparation is not null && (!publish || !preparation.IsCompletedSuccessfully))
        {
            if (_preparingActors.TryGetValue(spawn.EntityId, out var superseded) && superseded.Preparation != preparation)
            {
                _ = ObservePreparationAsync(superseded.Preparation);
            }

            _preparingActors[spawn.EntityId] = (spawn, preparation);
            return;
        }

        switch (spawn.Kind)
        {
            case EntityKind.Player:
                ApplyPlayerSpawn(spawn);
                break;
            case EntityKind.Monster:
            case EntityKind.Npc:
                ApplyModelSpawn(spawn);
                break;
        }
    }

    private void ApplyPlayerSpawn(EntitySpawnInfo spawn)
    {
        if (spawn.Player is null)
        {
            _log.Warn($"[Remote] player spawn {spawn.EntityId} carried no appearance; skipped.");
            return;
        }

        if (!TryResolveVisuals(spawn.Player.Class, out var visuals))
        {
            _log.Warn($"[Remote] spawn {spawn.EntityId}: unknown class {spawn.Player.Class}; skipped.");
            return;
        }

        var position = (XnaVector3)spawn.Position;
        var sampleTime = _clock.Stamp(spawn.ServerTimeMs);
        var seedTime = sampleTime;
        var seedFacing = spawn.Facing;
        if (_entities.TryGetValue(spawn.EntityId, out var existingPlayer))
        {
            // An appearance change keeps playing from the drawn pose towards the latest authoritative sample.
            position = existingPlayer.DrawnPosition;
            seedFacing = existingPlayer.RenderFacing;
            seedTime = double.IsFinite(existingPlayer.RenderTimeMs) ? Math.Min(existingPlayer.RenderTimeMs, sampleTime) : sampleTime;
        }

        var world = Microsoft.Xna.Framework.Matrix.CreateRotationY(seedFacing)
                    * Microsoft.Xna.Framework.Matrix.CreateTranslation(position);
        var motion = MotionCodes.Of(spawn.MoveState);
        visuals = ResolvePlayerHead(visuals, spawn.Player);
        var handles = new PlayerActorHandles(ActorHandle.None, ActorHandle.None);
        try
        {
            handles = _playerComposer.Compose(_assetRoot, visuals, spawn.Player.FaceIndex, world, motion);
            if (existingPlayer is RemoteEntity player)
            {
                _scene.CopyActorPlayback(player.Handles.Body, handles.Body);
                _scene.CopyActorPlayback(player.Handles.Head, handles.Head);
            }
        }
        catch (Exception exception)
        {
            handles.Remove(_scene);
            _log.Error($"[Remote] player appearance publication failed: {exception.Message}");
            return;
        }

        if (_entities.Remove(spawn.EntityId, out var previous))
        {
            previous.Dispose();
        }

        _playerAppearances[spawn.EntityId] = spawn.Player;
        var replacement = new RemoteEntity(
            _scene,
            handles,
            seedTime,
            position,
            seedFacing,
            spawn.MoveState,
            spawn.Life)
        {
            InVillage = spawn.Player.InVillage,
            EntityId = spawn.EntityId
        };
        replacement.MotionStarted += OnMotionStarted;
        replacement.SetRates(spawn.MoveSpeed, spawn.AttackRate);
        replacement.SetTarget(sampleTime, (XnaVector3)spawn.Position, spawn.Facing, spawn.MoveState, spawn.Life);
        _entities[spawn.EntityId] = replacement;
    }

    private ModelRenderInfo ModelInfo(EntitySpawnInfo spawn)
    {
        if (spawn.Kind == EntityKind.Monster)
        {
            return _catalog.GetMonster(spawn.TemplateId);
        }

        return _catalog.GetNpc(spawn.TemplateId);
    }

    private void ApplyModelSpawn(EntitySpawnInfo spawn)
    {
        var info = ModelInfo(spawn);
        if (info is null || string.IsNullOrEmpty(info.ModelInxPath))
        {
            _pendingModels[spawn.EntityId] = spawn;
            _log.Info($"[Remote] monster spawn {spawn.EntityId} template {spawn.TemplateId} deferred (catalog not ready / no entry).");
            return;
        }

        var position = (XnaVector3)spawn.Position;
        if (spawn.OwnerId != 0)
        {
            _summonOwners[spawn.EntityId] = spawn.OwnerId;
        }

        var scale = spawn.ModelScale > 0f ? spawn.ModelScale : info.ModelScale;
        var world = Microsoft.Xna.Framework.Matrix.CreateScale(scale) * Microsoft.Xna.Framework.Matrix.CreateRotationY(spawn.Facing) * Microsoft.Xna.Framework.Matrix.CreateTranslation(position);
        var motion = MotionCodes.Of(spawn.MoveState);
        var composed = _monsterComposer.Compose(_assetRoot, info.ModelInxPath, world, motion);
        var monster = new RemoteModelEntity(
            _scene,
            composed.Handle,
            scale,
            _clock.Stamp(spawn.ServerTimeMs),
            position,
            spawn.Facing,
            spawn.MoveState,
            spawn.Life,
            spawn.Kind,
            spawn.Kind == EntityKind.Npc ? ((NpcRenderInfo)info).SoundCode : ((MonsterRenderInfo)info).SoundCode,
            composed.Info,
            spawn.EntityId,
            composed.Alternate,
            composed.AlternateInfo)
        {
            Level = info is MonsterRenderInfo monsterInfo ? monsterInfo.Level : 0,
            MonsterClass = info is MonsterRenderInfo classInfo ? classInfo.Class : 0,
            ShadowSize = info.ShadowSize
        };
        monster.SetRates(spawn.MoveSpeed, spawn.AttackRate);
        monster.MotionStarted += OnMotionStarted;
        _entities[spawn.EntityId] = monster;
    }

    private void ApplyState(in EntityStateInfo state)
    {
        if (state.EntityId == _selfEntityId && _selfEntityId != 0)
        {
            _onSelfState?.Invoke(state);
            return;
        }

        if (_entities.TryGetValue(state.EntityId, out var entity))
        {
            entity.SetRates(state.MoveSpeed, state.AttackRate);
            entity.SetTarget(_clock.Stamp(state.ServerTimeMs), (XnaVector3)state.Position, state.Facing, state.MoveState, state.Life);
            if (!_preparingActors.ContainsKey(state.EntityId))
            {
                return;
            }
        }

        if (_preparingActors.TryGetValue(state.EntityId, out var preparing))
        {
            _preparingActors[state.EntityId] = (WithState(preparing.Spawn, state), preparing.Preparation);
            return;
        }

        if (_pendingModels.TryGetValue(state.EntityId, out var pending))
        {
            _pendingModels[state.EntityId] = WithState(pending, state);
            return;
        }

        _log.Info($"[Remote] state for unknown entity {state.EntityId} dropped (spawn not yet seen).");
    }


    // The monsters drawn this frame, for the presentation that follows their clips.
    public void CollectMonsters(List<PristonTale.Gameplay.Effects.MonsterActorView> into)
    {
        into.Clear();
        foreach (var (id, entity) in _entities)
        {
            if (entity is RemoteModelEntity { Kind: EntityKind.Monster } monster && monster.Actor.IsValid)
            {
                into.Add(new PristonTale.Gameplay.Effects.MonsterActorView(id, monster.Actor, monster.SoundCode, monster.Level, monster.Scale,
                    monster.DrawnPosition, monster.RenderFacing, monster.IsDead, monster.MonsterClass == (int)MonsterClass.Hammer));
            }
        }
    }

    public void SeekSkillMotion(ulong entityId, int framesFromStart, float rate)
    {
        if (_entities.TryGetValue(entityId, out var entity) && entity is RemoteEntity player)
        {
            player.SeekSkill(framesFromStart, rate);
        }
    }

    private void ApplyRemoved(ulong entityId)
    {
        _summonOwners.Remove(entityId);
        _playerAppearances.Remove(entityId);
        _shopSigns.Remove(entityId);
        _collisionRoster.Remove(entityId);
        _pendingModels.Remove(entityId);
        if (_preparingActors.Remove(entityId, out var pending))
        {
            _ = ObservePreparationAsync(pending.Preparation);
        }

        if (!_entities.Remove(entityId, out var entity))
        {
            return;
        }

        entity.MotionStarted -= OnMotionStarted;
        entity.Dispose();
    }

    private void OnMotionStarted(RemoteEntityBase entity, MotionCode motion)
    {
        if (entity.Kind == EntityKind.Npc && entity.SoundCode != 0)
        {
            NpcMotionStarted?.Invoke(entity.SoundCode, motion, entity.DrawnPosition);
        }
        else if (entity.Kind == EntityKind.Player && _playerAppearances.TryGetValue(entity.EntityId, out var appearance))
        {
            PlayerMotionStarted?.Invoke(appearance.Class, motion, entity.DrawnPosition);
        }
    }

    // The class, equipped weapon and drawn position of another player, for its swing sounds.
    public bool TryGetPlayerAudio(ulong entityId, out PlayerClass playerClass, out string weaponKey, out XnaVector3 position)
    {
        if (_entities.TryGetValue(entityId, out var entity) && entity.Kind == EntityKind.Player
            && _playerAppearances.TryGetValue(entityId, out var appearance))
        {
            playerClass = appearance.Class;
            weaponKey = appearance.WeaponKey ?? "";
            position = entity.DrawnPosition;
            return true;
        }

        playerClass = default;
        weaponKey = "";
        position = default;
        return false;
    }

    public void Update(float dt)
    {
        _clock.Advance(dt * 1000d);
        ulong completed = 0;
        foreach (var (id, pending) in _preparingActors)
        {
            if (!pending.Preparation.IsCompleted)
            {
                continue;
            }

            completed = id;
            if (pending.Preparation.IsCompletedSuccessfully)
            {
                ApplySpawn(pending.Spawn, publish: true);
                if (_entities.TryGetValue(id, out var loaded))
                {
                    loaded.SetRates(pending.Spawn.MoveSpeed, pending.Spawn.AttackRate);
                }
            }
            else
            {
                _log.Error($"[Remote] actor preparation failed: {pending.Preparation.Exception}");
            }

            break;
        }

        if (completed != 0)
        {
            _preparingActors.Remove(completed);
        }

        var now = _clock.NowMs;
        foreach (var entity in _entities.Values)
        {
            entity.SideView = SideView;
            entity.Update(dt, now);
        }
    }

    private const float MonsterPickRadius = 60f;
    private const float PlayerPickRadius = 40f;
    private const float NpcPickRadius = 40f;

    private static float PickRadius(EntityKind kind) => kind switch
    {
        EntityKind.Monster => MonsterPickRadius,
        EntityKind.Player => PlayerPickRadius,
        EntityKind.Npc => NpcPickRadius,
        _ => 0f,
    };

    // Draws every actor facing left or right (an action field).
    public bool SideView { get; set; }

    // The live entity nearest along x that stands within depth of the origin's z and within reach on the side it faces:
    // a monster, or with allies a player or a summoned creature.
    public ulong FindAhead(ulong selfId, XnaVector3 origin, bool facingRight, float reach, float depth, bool allies)
    {
        ulong found = 0;
        var nearest = reach;
        foreach (var (id, entity) in _entities)
        {
            if (id == selfId || entity.IsDead)
            {
                continue;
            }

            var summon = _summonOwners.ContainsKey(id);
            var wanted = allies ? entity.Kind == EntityKind.Player || summon : entity.Kind == EntityKind.Monster && !summon;
            var position = entity.DrawnPosition;
            var across = MathF.Abs(position.X - origin.X);
            if (wanted && MathF.Abs(position.Z - origin.Z) < depth && across < nearest
                && (facingRight ? position.X > origin.X : position.X < origin.X))
            {
                nearest = across;
                found = id;
            }
        }

        return found;
    }

    public bool TryPick(Microsoft.Xna.Framework.Ray ray, ulong selfId, out ulong entityId, out EntityKind kind)
    {
        entityId = 0;
        kind = EntityKind.None;
        var bestDistance = float.MaxValue;
        foreach (var (id, entity) in _entities)
        {
            if (id == selfId)
                continue;
            if (entity.IsDead || _summonOwners.ContainsKey(id))
                continue;
            var radius = PickRadius(entity.Kind);
            if (radius <= 0f)
                continue;
            var hit = CursorPicking.IntersectSphere(ray, entity.DrawnPosition, radius);
            if (hit is null)
                continue;
            if (hit.Value < bestDistance)
            {
                bestDistance = hit.Value;
                entityId = id;
                kind = entity.Kind;
            }
        }
        return entityId != 0;
    }

    // Entities still loading their models are not in the roster and have no actors yet.
    public int CopyActorHandles(ulong entityId, Span<ActorHandle> destination) =>
        _entities.TryGetValue(entityId, out var entity) ? entity.CopyActorHandles(destination) : 0;

    public bool TryGetDrawnPosition(ulong entityId, out XnaVector3 position)
    {
        if (_entities.TryGetValue(entityId, out var entity))
        {
            position = entity.DrawnPosition;
            return true;
        }

        position = default;
        return false;
    }

    // An entity's drawing delay behind the server's timeline; the base delay for one not in the roster.
    public double PlaybackDelayMsOf(ulong entityId) =>
        _entities.TryGetValue(entityId, out var entity) ? entity.PlaybackDelayMs : RemoteSnapshotBuffer.BaseDelayMs;

    public bool TryGetDrawnFacing(ulong entityId, out float facing)
    {
        if (_entities.TryGetValue(entityId, out var entity))
        {
            facing = entity.RenderFacing;
            return true;
        }

        facing = 0f;
        return false;
    }

    // Every other player's body item ("" while unknown), whether it is shown (alive, outside a village) and its model yaw.
    public void ForEachWearer(Action<ulong, string, bool, float> visit)
    {
        foreach (var (entityId, appearance) in _playerAppearances)
        {
            if (_entities.TryGetValue(entityId, out var entity) && entity is RemoteEntity)
            {
                visit(entityId, appearance.ArmorKey, !entity.IsDead && !appearance.InVillage, entity.RenderFacing);
            }
        }
    }

    // Every other player's held items: its actors, how its weapon and off-hand item glow, whether it is shown (alive and
    // drawn) and the weapon's catalog key.
    public void ForEachHolder(Action<ulong, PlayerActorHandles, PlayerAppearanceInfo, bool> visit)
    {
        foreach (var (entityId, appearance) in _playerAppearances)
        {
            if ((!appearance.WeaponGlow.IsNone || !appearance.OffHandGlow.IsNone) && _entities.TryGetValue(entityId, out var entity)
                && entity is RemoteEntity player)
            {
                visit(entityId, player.Handles, appearance, !entity.IsDead);
            }
        }
    }

    // Every drawn character's ground shadow: its position, model yaw, size (0 small to 3 large) and whether it is dead.
    public void ForEachShadow(Action<ulong, XnaVector3, float, int, bool> visit)
    {
        foreach (var (entityId, entity) in _entities)
        {
            if (entity.ShadowSize >= 0)
            {
                visit(entityId, entity.RenderPosition, entity.RenderFacing, entity.ShadowSize, entity.IsDead);
            }
        }
    }

    // Every other drawn player: its actors, looks and drawn position.
    public void ForEachPlayer(Action<ulong, PlayerActorHandles, PlayerAppearanceInfo, XnaVector3> visit)
    {
        foreach (var (entityId, appearance) in _playerAppearances)
        {
            if (_entities.TryGetValue(entityId, out var entity) && entity is RemoteEntity player && !entity.IsDead)
            {
                visit(entityId, player.Handles, appearance, entity.DrawnPosition);
            }
        }
    }

    // The actors a skill look paints: a player's body, head and held items, or a monster's model; and whether the
    // player holds a shield.
    public bool TryGetLookHandles(ulong entityId, out PlayerActorHandles handles, out bool holdsShield)
    {
        handles = new PlayerActorHandles(ActorHandle.None, ActorHandle.None);
        holdsShield = false;
        if (!_entities.TryGetValue(entityId, out var entity))
        {
            return false;
        }

        switch (entity)
        {
            case RemoteEntity player:
                handles = player.Handles;
                holdsShield = _playerAppearances.TryGetValue(entityId, out var appearance)
                    && ItemEquipmentMetadata.IsShield(appearance.OffHandKey);
                return true;
            case RemoteModelEntity model:
                handles = new PlayerActorHandles(model.Actor, ActorHandle.None);
                return true;
            default:
                return false;
        }
    }

    // The actor whose bones effects attach to: a player's body or a monster's model.
    public bool TryGetBodyActor(ulong entityId, out ActorHandle handle)
    {
        handle = default;
        if (!_entities.TryGetValue(entityId, out var entity))
        {
            return false;
        }

        handle = entity switch
        {
            RemoteEntity player => player.Body,
            RemoteModelEntity model => model.Actor,
            _ => default
        };
        return handle.IsValid;
    }

    public float AttackRateOf(ulong entityId) => _entities.TryGetValue(entityId, out var entity) ? entity.AttackRate : 1f;

    public bool PlaySkillMotion(ulong entityId, int motionIndex, float rate, bool attackClip)
    {
        if (!_entities.TryGetValue(entityId, out var entity) || entity.IsDead)
        {
            return false;
        }

        entity.PlaySkill(motionIndex, rate, attackClip);
        return true;
    }

    public bool TryGetNpcAudioSource(ulong entityId, out int soundCode, out XnaVector3 position)
    {
        if (_entities.TryGetValue(entityId, out var entity) && entity.Kind == EntityKind.Npc)
        {
            soundCode = entity.SoundCode;
            position = entity.DrawnPosition;
            return true;
        }

        soundCode = 0;
        position = default;
        return false;
    }

    public bool TryGetTarget(ulong entityId, out XnaVector3 position, out bool isDead)
    {
        if (_entities.TryGetValue(entityId, out var entity))
        {
            position = entity.RenderPosition;
            isDead = entity.IsDead;
            return true;
        }
        position = default;
        isDead = false;
        return false;
    }

    public void CollectNeighbours(ulong excludeId, List<EntityNeighbour> into) => _collisionRoster.Collect(excludeId, into);

    public void Clear()
    {
        foreach (var entity in _entities.Values)
        {
            entity.MotionStarted -= OnMotionStarted;
            entity.Dispose();
        }

        _entities.Clear();
        _playerAppearances.Clear();
        _shopSigns.Clear();
        _collisionRoster.Clear();
        _pendingModels.Clear();
        foreach (var pending in _preparingActors.Values)
        {
            _ = ObservePreparationAsync(pending.Preparation);
        }

        _preparingActors.Clear();
    }

    private async Task ObservePreparationAsync(Task preparation)
    {
        try
        {
            await preparation;
        }
        catch (Exception ex)
        {
            _log.Error($"[Remote] abandoned actor preparation failed: {ex}");
        }
    }

    public void Dispose() => Clear();

    private CharacterVisualSet ResolvePlayerHead(CharacterVisualSet defaults, PlayerAppearanceInfo appearance)
    {
        var head = CharacterHead.ApplyBigHead(
            CharacterHead.Apply(defaults, appearance.Class, appearance.Tier, appearance.FaceIndex, appearance.HairIndex),
            appearance.Class, appearance.BigHead);
        uint jobMask = _classRegistry.Get(appearance.Class).Definition.MotionJobMask;
        return EquipmentVisuals.Apply(head, appearance.ArmorKey, appearance.WeaponKey, appearance.OffHandKey, appearance.InVillage, jobMask);
    }

    private bool TryResolveVisuals(PlayerClass playerClass, out CharacterVisualSet visuals)
    {
        if (System.Enum.IsDefined(playerClass))
        {
            visuals = _classRegistry.Get(playerClass).Definition.DefaultVisuals;
            return true;
        }
        visuals = null;
        return false;
    }

    // A spawn still waiting for its model, moved to the latest state the server sent.
    private static EntitySpawnInfo WithState(EntitySpawnInfo spawn, EntityStateInfo state) => spawn with
    {
        Position = state.Position,
        Facing = state.Facing,
        MoveState = state.MoveState,
        Life = state.Life,
        MoveSpeed = state.MoveSpeed,
        AttackRate = state.AttackRate,
        ServerTimeMs = state.ServerTimeMs
    };
}
