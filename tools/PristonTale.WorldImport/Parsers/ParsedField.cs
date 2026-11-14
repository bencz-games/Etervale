namespace PristonTale.WorldImport.Parsers;

// Importer DTO aggregating the three spawn files of one stage (the ASE base name): the .spm text config (budget knobs,
// weighted monster table, bosses), the .spp binary spawn points and the .spc binary fixed NPCs. Monsters are named by
// their authored identity and NPCs by their definition file, both resolved by the Importer. FieldCode is the world
// catalog field that uses the stage, null for a stage no field uses.
public sealed class ParsedField
{
    public required string AseBase { get; init; }
    public int? FieldCode { get; init; }

    // .spm budget knobs. Defaults match the original (10 concurrent monsters, 3 per wave).
    public int MaxConcurrentMonsters { get; set; } = 10;
    public int MaxPerSpawnPoint { get; set; } = 3;
    public int SpawnInterval { get; set; }
    public int PerPointCooldownMs { get; set; }

    public List<(string MonsterName, int Weight)> MonsterSpawns { get; } = [];
    public List<ParsedBoss> Bosses { get; } = [];

    // .spp spawn points: (index, state, x, z).
    public List<(int Index, int State, int X, int Z)> SpawnPoints { get; } = [];

    // .spc fixed NPCs.
    public List<ParsedNpcPlacement> FixedNpcs { get; } = [];
}

// A placed NPC: its record index in the .spc, the NPC definition file it names, its position and heading, and the
// class and shadow size of the placement's character record (NpcParser.TakePlacement).
// Preserve the exact authored path as well as the basename used to resolve real NPC definitions.
public sealed record ParsedNpcPlacement(int Index, string SourceFile, int X, int Y, int Z, int Angle, int Class, int SizeLevel,
    string RawSourcePath = "");

// A *출연자두목 boss spawn: master + optional escort/slave + slave count + spawn hours.
public sealed record ParsedBoss(string MasterName, string? SlaveName, int SlaveCount, IReadOnlyList<int> Hours);
