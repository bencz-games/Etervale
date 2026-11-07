using System;
using PristonTale.Simulation.World.Fields;

namespace PristonTale.Simulation.World;

public enum FieldState
{
    Forest,
    Village,
    Ruin,
    Desert,
    Dungeon,
    Room,
    Iron,
    Ice,
    QuestArena,
    Castle,

    // A side-scrolling field: the camera stands at the field's fixed height and depth and follows the player along x,
    // and the arrow keys move the player.
    Action
}

// A field's continuous background loop, in the order of the client's field ambience files.
public enum FieldAmbience
{
    Town,
    ForestDay,
    ForestNight,
    RuinWind,
    DesertWind,
    Dungeon,
    Temple,
    Cave,
    DarkSanctuary,
    Iron,
    Iron2,
    BabelApproach,
    IceWind
}

public sealed record FieldBackdrop(
    string Day,
    string Glow,
    string Night);

public sealed record FieldPoint(
    int X,
    int Z)
{
    public static readonly FieldPoint Zero = new(0, 0);
}

public sealed record FieldVector(
    int X,
    int Y,
    int Z);

public sealed record FieldGate(
    string DestinationFieldId,
    int X,
    int Z,
    int Angle);

public sealed record FieldWarpGate(
    int X,
    int Z,
    int Y,
    int Width,
    int Height,
    string DestinationFieldId,
    int DestinationX,
    int DestinationZ,
    int DestinationY,
    int SpecialEffect)
{
    // The level the gate opens at: its destination's (WorldCatalog).
    public int LimitLevel { get; init; }

    private const float MinimumRadius = 48f;
    // The server answers a gate (a warp or a wing stone) this far past its radius, for a report a step late.
    private const float ServerTolerance = 64f;

    // Where the client's feet trigger the gate: its width, at least MinimumRadius.
    public float Radius => MathF.Max(Width, MinimumRadius);

    // How far from the gate the server accepts a player standing on it.
    public float ServerReach => Radius + ServerTolerance;

    // A gate may lead to several places in its destination: the traveller comes out at one of them, picked at random.
    public IReadOnlyList<FieldVector> MoreExits { get; init; } = [];

    public int ExitCount => 1 + MoreExits.Count;

    // Exit 0 is the destination point; the others follow in MoreExits.
    public FieldVector Exit(int index) => index == 0 ? new FieldVector(DestinationX, DestinationY, DestinationZ) : MoreExits[index - 1];
}

// Where an action field's camera stands: FixedHeight in fixed point (the camera rises 80 units above it), at depth Z,
// following the player along x between LeftX and RightX. It looks along +z at the player's height.
public sealed record FieldActionCamera(
    int FixedHeight,
    int Z,
    int LeftX,
    int RightX)
{
    public const int LookAtZ = 326711;
    // The side view's vertical angle (pi / 4.4): standing a few hundred units in front of the player and close to the
    // ground, a wider lens shows the void below the ground strip.
    public const float FieldOfViewRadians = MathF.PI / 4.4f;
    private const float Rise = 80f;

    public float Height => PristonTale.Contracts.Units.GameUnits.FromFixed(FixedHeight) + Rise;

    public float FollowX(float playerX) => Math.Clamp(playerX, LeftX, RightX);
}

public sealed record FieldAmbient(
    FieldVector Position,
    int Radius,
    int SoundCode);

// A hand-authored light for a field. Position is in engine world units (same values the
// renderer logs for the decoded SMD lights). Colour is 0-255 per channel.
public sealed record FieldLight(
    float X,
    float Y,
    float Z,
    float Range,
    byte R,
    byte G,
    byte B,
    float Intensity);

public readonly record struct FieldColor(float R, float G, float B)
{
    public static readonly FieldColor Black = new(0f, 0f, 0f);

    public static FieldColor Grey(float level) => new(level, level, level * 1.13f);

    public FieldColor Scaled(float k) => new(R * k, G * k, B * k);
}

public sealed record FieldLighting
{
    // Stage light colours are used as authored, like the original dynamic lights; values above 1 overbright the scene.
    public float LampIntensity { get; init; } = 1f;
    public float LampRangeScale { get; init; } = 1f;
    public IReadOnlyList<FieldLight> Lights { get; init; } = [];
    public FieldColor Ambient { get; init; } = FieldColor.Grey(0.30f);

    // Glow settings affect additive coronas, not scene illumination.
    public float GlowIntensity { get; init; } = 1f;
    public float GlowScale { get; init; } = 1f;
}

public sealed record FieldCamera
{
    public float Distance { get; init; } = 420f;
    public float MinDistance { get; init; } = 10f;
    public float MaxDistance { get; init; } = 1200f;
    public float Pitch { get; init; } = 0.35f;
    public float FieldOfViewDegrees { get; init; } = 65f;
    public float TargetHeight { get; init; } = 30f;
}

public sealed record FieldDayNightKey(float Hour, FieldColor Sun, FieldColor Ambient, FieldColor Sky);

public sealed record FieldDayNight
{
    public IReadOnlyList<FieldDayNightKey> Gradient { get; init; } = [];
    public float DayLengthSeconds { get; init; } = 150f;
    public float StartTimeOfDay { get; init; } = 8.0f;
}

public sealed record FieldDef(
    string Id,
    string Name,
    FieldState State,
    FieldBackdrop Backdrop,
    string StageSmd,
    IReadOnlyList<string> StageObjectSmds)
{
    public int FieldCode { get; init; }
    public FieldPoint Center { get; init; } = FieldPoint.Zero;
    public IReadOnlyList<FieldPoint> StartPoints { get; init; } = [];

    // Where a traveller arrives in the field (an entry, a teleport, a warp, a quest's travel): its first start point,
    // else its centre.
    public FieldPoint Arrival => StartPoints.Count > 0 ? StartPoints[0] : Center;

    // Where a traveller arrives: a random start point of a castle, else Arrival.
    public FieldPoint ArrivalFor(Random random) => State == FieldState.Castle && StartPoints.Count > 0 ? StartPoints[random.Next(StartPoints.Count)] : Arrival;
    public IReadOnlyList<FieldGate> Gates { get; init; } = [];
    public IReadOnlyList<FieldWarpGate> WarpGates { get; init; } = [];
    public IReadOnlyList<FieldAmbient> Ambients { get; init; } = [];

    // The background loop when the field names its own; null keeps its state's.
    public FieldAmbience? Ambience { get; init; }
    public FieldLighting Lighting { get; init; } = new();
    public FieldCamera Camera { get; init; } = new();
    public FieldDayNight DayNight { get; init; } = new();
    public string BackMusicCode { get; init; } = string.Empty;
    public string ServerCode { get; init; } = string.Empty;
    public FieldEventKind FieldEvent { get; init; }

    // Set for an action field.
    public FieldActionCamera ActionCamera { get; init; }

    // How much of the distance haze the field clears (0..1), and the cells of sight it adds before the haze starts.
    public float HazeReduction { get; init; }

    // The morning mist's strength (1 a forest's; null: a forest's for a forest, none elsewhere); the client lays it
    // from 4:30 to 9:00.
    public float? MorningMist { get; init; }

    public float MorningMistStrength => MorningMist ?? (State == FieldState.Forest ? 1f : 0f);

    // The ice fields show the aurora at night.
    public bool ShowsAurora => State == FieldState.Ice;
    // The snow skies bring falling snow at every hour.
    public bool Snows => Backdrop == FieldBackdrops.Snow;
    public int SightCells { get; init; }

    // The level a character needs to walk, warp or be sent into the field (the shipped fields': FieldEntryLevels).
    public int LimitLevel { get; init; }

    // A character of this level saved here logs in at its home town; a Closed field keeps one a game master placed there.
    public bool SendsAway(int level) => LimitLevel > level && LimitLevel != FieldEntryLevels.Closed;

    // Dungeon state disables sky and daylight in favor of stage lighting.
    public bool Underground => State == FieldState.Dungeon;

    public FieldSpawnData Spawns { get; init; } = FieldSpawnData.Empty;
}

public sealed class WorldCatalog
{
    public WorldCatalog()
    {
        AllFields =
        [
            Forest3Field.Definition,
            Forest2Field.Definition,
            Forest1Field.Definition,
            RicartenField.Definition,
            Ruin4Field.Definition,
            Ruin3Field.Definition,
            Ruin2Field.Definition,
            Ruin1Field.Definition,
            Desert1Field.Definition,
            Desert2Field.Definition,
            Desert3Field.Definition,
            Desert4Field.Definition,
            Dungeon1Field.Definition,
            Dungeon2Field.Definition,
            Dungeon3Field.Definition,
            Dungeon4Field.Definition,
            Dungeon5Field.Definition,
            OfficeField.Definition,
            Iron1Field.Definition,
            Iron2Field.Definition,
            Ice1Field.Definition,
            Ice2Field.Definition,
            IceUraField.Definition,
            CastleField.Definition,
            PillaiField.Definition,
            NaviskoField.Definition,
            Sod1Field.Definition,
            GreedyField.Definition,
            ForeverFall4Field.Definition,
            ForeverFall3Field.Definition,
            ForeverFall2Field.Definition,
            ForeverFall1Field.Definition,
            TcaveField.Definition,
            McaveField.Definition,
            DcaveField.Definition,
            Dungeon6Field.Definition,
            Dungeon7Field.Definition,
            Dungeon8Field.Definition,
            Dungeon9Field.Definition,
            Mine1Field.Definition,
            BossField.Definition,
            LostField.Definition,
            LosttempleField.Definition,
            QuestArenaField.Definition,
            Sod2Field.Definition,
            FallGameField.Definition,
            SlabField.Definition,
            SwampField.Definition,
            AncientWeaponsField.Definition,
            SeaOfAbyssField.Definition,
            StempleField.Definition,
            HeartOfFireField.Definition,
            CrystalNestField.Definition,
            LandOfNurwnField.Definition
        ];

        AllFields = AllFields.Select(static field => field with { LimitLevel = FieldEntryLevels.Of(field.FieldCode) }).ToList();
        AllFields = WithGateLevels(AllFields);
        _fieldsById = AllFields.ToDictionary(static field => field.Id);
        _fieldsByCode = AllFields.ToDictionary(static field => field.FieldCode);
    }

    public WorldCatalog(IEnumerable<FieldDef> fields)
    {
        AllFields = WithGateLevels(fields.ToList());
        _fieldsById = AllFields.ToDictionary(static field => field.Id);
        _fieldsByCode = AllFields.ToDictionary(static field => field.FieldCode);
    }

    // Every warp gate opens at its destination's level (0 for a destination the catalog lacks).
    private static List<FieldDef> WithGateLevels(IReadOnlyList<FieldDef> fields)
    {
        var levels = fields.ToDictionary(static field => field.Id, static field => field.LimitLevel);
        return fields.Select(field => field with
        {
            WarpGates = field.WarpGates.Select(gate => gate with { LimitLevel = levels.GetValueOrDefault(gate.DestinationFieldId) }).ToList()
        }).ToList();
    }

    private readonly Dictionary<string, FieldDef> _fieldsById;
    private readonly Dictionary<int, FieldDef> _fieldsByCode;

    public IReadOnlyList<FieldDef> AllFields { get; }

    public FieldDef Ricarten => _fieldsById.GetValueOrDefault(RicartenField.Definition.Id, RicartenField.Definition);

    public FieldDef GetField(string id) => _fieldsById[id];

    public bool TryGetField(string id, out FieldDef field) => _fieldsById.TryGetValue(id, out field);

    public FieldDef GetFieldByCode(int code) => _fieldsByCode[code];

    public bool TryGetFieldByCode(int code, out FieldDef field) => _fieldsByCode.TryGetValue(code, out field);

    public IReadOnlyList<FieldDef> Neighbours(FieldDef field)
    {
        var result = new List<FieldDef>();
        var seen = new HashSet<string> { field.Id };

        foreach (var gate in field.Gates)
            if (_fieldsById.TryGetValue(gate.DestinationFieldId, out var dest) && seen.Add(dest.Id))
                result.Add(dest);

        foreach (var other in AllFields)
            if (other.Id != field.Id)
                foreach (var gate in other.Gates)
                    if (gate.DestinationFieldId == field.Id && seen.Add(other.Id))
                        result.Add(other);

        return result;
    }
}
