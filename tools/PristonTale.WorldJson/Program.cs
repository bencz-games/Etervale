using System.Text.Json;
using System.Text.Json.Serialization;
using PristonTale.Simulation.World;

// Dump the shared WorldCatalog to world.json for external field-streaming tools. The geometry GLBs
// are exported at RAW world coordinates × a uniform scale, so two co-resident fields align automatically;
// the only thing the runtime needs is, per field, WHERE the walk-through gates are (to know WHEN to stream
// a neighbour and WHICH one). Coordinates are PT global units (same space as the stage geometry, pre-scale).

var outPath = args.Length > 0 ? args[0] : "world.json";

var catalog = new WorldCatalog();

static string GlbRel(string stageSmd)
{
    // "Field\Ricarten\village-2.smd" -> "Ricarten/village-2"  (matches 3d/fields/<dir>/<stem>.glb)
    var p = stageSmd.Replace('\\', '/');
    const string prefix = "Field/";
    var i = p.IndexOf(prefix, StringComparison.OrdinalIgnoreCase);
    if (i >= 0) p = p[(i + prefix.Length)..];
    var dot = p.LastIndexOf('.');
    return dot >= 0 ? p[..dot] : p;
}

var fields = catalog.AllFields.Select(f => new
{
    id = f.Id,
    name = f.Name,
    code = f.FieldCode,
    glb = GlbRel(f.StageSmd),
    center = new { x = f.Center.X, z = f.Center.Z },
    start = f.StartPoints.Select(s => new { x = s.X, z = s.Z }).ToArray(),
    gates = f.Gates.Select(g => new { dest = g.DestinationFieldId, x = g.X, z = g.Z, angle = g.Angle }).ToArray(),
    warps = f.WarpGates.Select(w => new
    {
        x = w.X,
        y = w.Y,
        z = w.Z,
        width = w.Width,
        height = w.Height,
        dest = w.DestinationFieldId,
        dx = w.DestinationX,
        dy = w.DestinationY,
        dz = w.DestinationZ,
        moreExits = w.MoreExits.Select(e => new { x = e.X, y = e.Y, z = e.Z }).ToArray(),
        limitLevel = w.LimitLevel,
        special = w.SpecialEffect
    }).ToArray()
}).ToArray();

var world = new
{
    // Streaming radii: the connect view range and the disconnect range (0x320000 squared world units). Multiply by the
    // export scale in the runtime. Gate planar mapping to a Y-up engine: (x, -z).
    streamConnect = (double)ViewRanges.Connect,
    streamDisconnect = Math.Sqrt(0x320000),
    startField = "village-2",
    camera = new { distance = 420.0, pitch = 0.35, fovDeg = 65.0, targetHeight = 30.0 },
    fields
};

var json = JsonSerializer.Serialize(world, new JsonSerializerOptions
{
    WriteIndented = true,
    DefaultIgnoreCondition = JsonIgnoreCondition.Never
});
File.WriteAllText(outPath, json);
Console.WriteLine($"wrote {outPath}  ({fields.Length} fields, {fields.Sum(f => f.gates.Length)} gates, {fields.Sum(f => f.warps.Length)} warps)");
