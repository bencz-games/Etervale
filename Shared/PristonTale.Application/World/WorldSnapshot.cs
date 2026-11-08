using PristonTale.Simulation.World.Loot;
using System.Numerics;
using PristonTale.Simulation.Characters;
using PristonTale.Contracts.Motion;
using PristonTale.Contracts.World;
using PristonTale.Contracts.Combat;
using PristonTale.Contracts.Skills;
using PristonTale.Contracts.Characters;

namespace PristonTale.Application.World;

public readonly struct PlayerAppearanceView
{
    public PlayerAppearanceView(
        string name, PlayerClass playerClass, int tier, int faceIndex, int hairIndex,
        string modelName, string modelName2)
    {
        Name = name;
        Class = playerClass;
        Tier = tier;
        FaceIndex = faceIndex;
        HairIndex = hairIndex;
        ModelName = modelName;
        ModelName2 = modelName2;
    }

    public string WeaponKey { get; init; } = "";
    public string ArmorKey { get; init; } = "";
    public string OffHandKey { get; init; } = "";
    public bool InVillage { get; init; }

    // How the held weapon and off-hand item glow (HeldItemGlow).
    public Simulation.Items.HeldItemGlow WeaponGlow { get; init; } = Simulation.Items.HeldItemGlow.None;
    public Simulation.Items.HeldItemGlow OffHandGlow { get; init; } = Simulation.Items.HeldItemGlow.None;

    // Premium looks: the helper pet (1 Terry, 2 Nepsys, 3 Io, 4 Mute), the Phoenix and the big head (1 or 2).
    public int HelperPet { get; init; }
    public bool Phoenix { get; init; }
    public int BigHead { get; init; }

    // The sign of the personal shop the player sells from, empty when it sells nothing.
    public string ShopSign { get; init; } = "";

    // The player's clan (0 for none), its name and its mark's version.
    public long ClanId { get; init; }
    public string ClanName { get; init; } = "";
    public int ClanMark { get; init; }

    public string Name { get; }
    public PlayerClass Class { get; }
    public int Tier { get; init; }
    public int FaceIndex { get; }
    public int HairIndex { get; }
    public string ModelName { get; }
    public string ModelName2 { get; }
}

public readonly struct EntityView
{
    public EntityView(
        ulong entityId, EntityKind kind, bool isSelf, Vector3 position, float facing,
        MotionState moveState, int level, int life, int maxLife,
        PlayerAppearanceView appearance, uint templateId = 0,
        int moveSpeed = 0, float attackRate = 1f, ulong attackTargetId = 0, ulong attackSequence = 0)
    {
        EntityId = entityId;
        Kind = kind;
        IsSelf = isSelf;
        Position = position;
        Facing = facing;
        MoveState = moveState;
        Level = level;
        Life = life;
        MaxLife = maxLife;
        Appearance = appearance;
        TemplateId = templateId;
        MoveSpeed = moveSpeed;
        AttackRate = attackRate;
        AttackTargetId = attackTargetId;
        AttackSequence = attackSequence;
    }

    public ulong EntityId { get; }
    public EntityKind Kind { get; }

    // A summon's owner and its own model size (0 keeps the catalog size).
    public ulong OwnerId { get; init; }
    public float ModelScale { get; init; }

    // The player a quest duel opponent was opened for; only that player sees it in the arena.
    public ulong DuelPlayerId { get; init; }

    // The field's descriptor revision of the entity (1..255), the same for every viewer; 0 for a view built outside a field.
    public byte Revision { get; init; }

    public bool IsSelf { get; }

    // A hidden game master: only the player itself receives its view.
    public bool Hidden { get; init; }

    public Vector3 Position { get; }
    public float Facing { get; }
    public MotionState MoveState { get; }
    public int Level { get; }

    public int Life { get; }
    public int MaxLife { get; }

    public PlayerAppearanceView Appearance { get; }
    public uint TemplateId { get; }

    public int MoveSpeed { get; }

    public float AttackRate { get; }
    public ulong AttackTargetId { get; }
    public ulong AttackSequence { get; }
}

public readonly record struct SkillEventView(
    ulong CasterId,
    ulong TargetId,
    uint SkillCode,
    SkillEventPhase Phase,
    int Rank,
    SkillEventFlags Flags,
    float DurationMs,
    float HitAtMs,
    Vector3 Position,
    float Facing,
    int TargetCount,
    float Radius);

public readonly record struct StatusEventView(
    ulong EntityId,
    ulong SourceId,
    uint StatusCode,
    StatusEffectOp Op,
    int Stacks,
    byte SourceKind,
    byte Control,
    bool Harmful,
    bool Permanent,
    int DurationMs,
    int RemainingMs,
    int Value);

public readonly record struct SkillCooldownView(uint SkillCode, int RemainingMs, int DurationMs);

public readonly struct CombatEventView
{
    public CombatEventView(
        ulong attackerId, ulong targetId, CombatEventKind kind,
        float durationMs = 0f, float hitAtMs = 0f, int damage = 0, bool crit = false, bool killed = false, int resources = 0,
        int sourceSoundCode = 0, Vector3 position = default, float facing = 0f)
    {
        AttackerId = attackerId;
        TargetId = targetId;
        Kind = kind;
        DurationMs = durationMs;
        HitAtMs = hitAtMs;
        Damage = damage;
        Crit = crit;
        Killed = killed;
        Resources = resources;
        SourceSoundCode = sourceSoundCode;
        Position = position;
        Facing = facing;
    }

    public ulong AttackerId { get; }
    public ulong TargetId { get; }
    public CombatEventKind Kind { get; }
    public float DurationMs { get; }
    public float HitAtMs { get; }
    public int Damage { get; }
    public bool Crit { get; }
    public bool Killed { get; }
    public int Resources { get; }
    public int SourceSoundCode { get; }
    public Vector3 Position { get; }
    public float Facing { get; }

    // A monster's swing: the motion-table slot of the clip it plays (-1 for the first clip of the state) and whether it is
    // its skill. A hit: whether a skill (or a pet's strike) dealt it rather than the attacker's basic attack.
    public int Clip { get; init; } = -1;
    public bool Skill { get; init; }
}

// The number of each kind of event that belongs to one tick, in the order of the joined arrays.
public readonly record struct EventSegment(uint Tick, int Combat, int Skills, int Statuses, int Items);

public readonly struct WorldSnapshot
{
    public WorldSnapshot(uint serverTick, EntityView[] entities, CombatEventView[]? events = null, int? fieldId = null, OwnerState? owner = null, GroundDropState[]? groundDrops = null)
    {
        ServerTick = serverTick;
        Entities = entities;
        Events = events ?? Array.Empty<CombatEventView>();
        FieldId = fieldId;
        Owner = owner;
        GroundDrops = groundDrops ?? [];
    }

    public uint ServerTick { get; }
    public EntityView[] Entities { get; }
    public CombatEventView[] Events { get; }
    public int? FieldId { get; }
    public OwnerState? Owner { get; }
    public GroundDropState[] GroundDrops { get; }

    // Built once per field and tick; subscribers share it to find nearby entities without scanning the field.
    public EntityGrid? Grid { get; init; }

    public SkillEventView[] SkillEvents { get; init; } = [];
    public StatusEventView[] StatusEvents { get; init; } = [];
    public ItemEffectView[] ItemEffects { get; init; } = [];

    // The receiver's running cooldowns; null when they did not change since the last snapshot it was sent.
    public SkillCooldownView[]? Cooldowns { get; init; }

    // How the event arrays split by tick when a reader fell behind and several snapshots' events were joined (in order);
    // null when every event belongs to ServerTick.
    public EventSegment[]? EventSegments { get; init; }

    // Set when the reader fell so far behind that its outbox dropped the events it had not read.
    public bool EventsLost { get; init; }

    // The field's shared frame of this tick (views and grid) and its generation; null for snapshots built by hand.
    public PresenceFrame? Frame { get; init; }
    public int FrameGeneration { get; init; }

    // True once a reader pinned the frame; the reader releases it after encoding.
    public bool Pinned { get; init; }
    public ulong PinKey { get; init; }

    public void ReleaseFrame()
    {
        if (Pinned)
        {
            Frame!.Release(PinKey);
        }
    }

    // The statuses running in the field and its clock, for entities a receiver starts seeing after their statuses began.
    public ActiveStatusSet ActiveStatuses { get; init; }
    public long FieldNowMs { get; init; }

    // The field's weather (FieldInstance.Weather); the encoder sends it when it changes.
    public WeatherState Weather { get; init; }

    // The receiver's quest changes: the journal view when it changed, quest notices and a dialog the world opened.
    public QuestUpdate? Quest { get; init; }

    // What standing up again would cost the receiver; set while it is dead.
    public Simulation.Progression.RestartOffer? Restart { get; init; }

    // The receiver's party (null when it has none) and the request to join one it has not answered; every snapshot
    // carries the current ones, and the encoder sends what changed.
    public PartyView? Party { get; init; }
    public PartyRequest? PartyRequest { get; init; }

    // The receiver's Bellatra board while it is in the arena (null elsewhere); a new one each time it changes.
    public BellatraView? Bellatra { get; init; }

    // The receiver's trade window (null when it trades with nobody) and the request to trade it has not answered.
    public TradeView? Trade { get; init; }
    public TradeRequest? TradeRequest { get; init; }

    public WorldSnapshot WithOwner(OwnerState? owner, SkillCooldownView[]? cooldowns = null) => new(ServerTick, Entities, Events, FieldId, owner, GroundDrops)
    {
        Grid = Grid,
        SkillEvents = SkillEvents,
        StatusEvents = StatusEvents,
        ItemEffects = ItemEffects,
        ActiveStatuses = ActiveStatuses,
        FieldNowMs = FieldNowMs,
        Frame = Frame,
        FrameGeneration = FrameGeneration,
        Cooldowns = cooldowns,
        Weather = Weather
    };

    internal WorldSnapshot WithPin(ulong key) => this with { Pinned = true, PinKey = key };

    public WorldSnapshot WithEvents(CombatEventView[] events) => new(ServerTick, Entities, events, FieldId, Owner, GroundDrops)
    {
        Grid = Grid,
        SkillEvents = SkillEvents,
        StatusEvents = StatusEvents,
        ItemEffects = ItemEffects,
        ActiveStatuses = ActiveStatuses,
        FieldNowMs = FieldNowMs,
        Frame = Frame,
        FrameGeneration = FrameGeneration,
        Cooldowns = Cooldowns,
        Weather = Weather,
        Quest = Quest,
        Restart = Restart,
        Party = Party,
        PartyRequest = PartyRequest,
        Trade = Trade,
        TradeRequest = TradeRequest,
        Bellatra = Bellatra,
        Pinned = Pinned,
        PinKey = PinKey
    };

    // The snapshot without the field's entity state (its frame was reused), the receiver's own parts kept.
    internal WorldSnapshot WithoutFrame() => new(ServerTick, [], Events, FieldId, Owner, GroundDrops)
    {
        SkillEvents = SkillEvents,
        StatusEvents = StatusEvents,
        ItemEffects = ItemEffects,
        Cooldowns = Cooldowns,
        Weather = Weather,
        Quest = Quest,
        Restart = Restart,
        Party = Party,
        PartyRequest = PartyRequest,
        Trade = Trade,
        TradeRequest = TradeRequest,
        Bellatra = Bellatra
    };
}
