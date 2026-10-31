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
    Castle
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
    int LimitLevel,
    int SpecialEffect);

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
    public IReadOnlyList<FieldGate> Gates { get; init; } = [];
    public IReadOnlyList<FieldWarpGate> WarpGates { get; init; } = [];
    public IReadOnlyList<FieldAmbient> Ambients { get; init; } = [];
    public FieldLighting Lighting { get; init; } = new();
    public FieldCamera Camera { get; init; } = new();
    public FieldDayNight DayNight { get; init; } = new();
    public string BackMusicCode { get; init; } = string.Empty;
    public string ServerCode { get; init; } = string.Empty;
    public FieldEventKind FieldEvent { get; init; }

    // How much of the distance haze the field clears (0..1), and the cells of sight it adds before the haze starts.
    public float HazeReduction { get; init; }

    // The morning mist's strength (1 a forest's; null: a forest's for a forest, none elsewhere); the client lays it
    // from 4:30 to 9:00.
    public float? MorningMist { get; init; }
    public int SightCells { get; init; }
    public int LimitLevel { get; init; }

    // Dungeon state disables sky and daylight in favor of stage lighting.
    public bool Underground => State == FieldState.Dungeon;

    public FieldSpawnData Spawns { get; init; } = FieldSpawnData.Empty;

    public Action<IFieldContext> OnLoaded { get; init; }
    public Action<IFieldContext, float> OnUpdate { get; init; }
}

public interface IFieldContext
{
    float TimeOfDay { get; }
    void SetTimeOfDay(float hours);
    void SetDayLength(float seconds);
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
            Sod2Field.Definition
        ];

        _fieldsById = AllFields.ToDictionary(static field => field.Id);
        _fieldsByCode = AllFields.ToDictionary(static field => field.FieldCode);
    }

    public WorldCatalog(IEnumerable<FieldDef> fields)
    {
        AllFields = fields.ToList();
        _fieldsById = AllFields.ToDictionary(static field => field.Id);
        _fieldsByCode = AllFields.ToDictionary(static field => field.FieldCode);
    }

    private readonly Dictionary<string, FieldDef> _fieldsById;
    private readonly Dictionary<int, FieldDef> _fieldsByCode;

    public IReadOnlyList<FieldDef> AllFields { get; }

    public FieldDef Ricarten => RicartenField.Definition;

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
