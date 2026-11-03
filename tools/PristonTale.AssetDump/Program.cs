using System.Text;
using Microsoft.Xna.Framework;
using PristonTale.Assets;
using PristonTale.Engine3D.Formats;
using PristonTale.Engine3D.Rendering;
using PristonTale.AssetDump;
using PristonTale.Contracts.Motion;
using PristonTale.Contracts.Units;

if (args.Length == 4 && args[0] == "--export-field-textures")
{
    try
    {
        FieldTextureManifest.Export(args[1], args[2], args[3]);
        return 0;
    }
    catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException or InvalidOperationException)
    {
        Console.Error.WriteLine($"error exporting field textures: {exception.Message}");
        return 1;
    }
}

// Headless inspector for PristonTale binary assets (.smd stage, .smd/.smb model, .inx index).
// Prints the decoded structure so material/geometry/light decisions are grounded in real bytes
// instead of assumption (see docs/rendering.md for the material and render conventions and docs/tools.md for
// the options). Reuses the real Engine3D readers — no GraphicsDevice, no wine prefix.
//
// Usage:  ptdump <asset-path> [--materials] [--lights] [--objects] [--normals] [--motions] [--matbounds] [--physique] [--full]
//   A .bmp/.tga path instead decodes the texture and prints content stats (size/alpha/grayscale%).
//   --matbounds : per-material face count + XYZ bounds (locate geometry by material).
//   --physique  : distinct skinning bone names referenced by vertices.
//   default          summary + aggregates (counts, bounds, blend-type histogram)
//   --materials      one line per material (blend / transparency / flags / textures)
//   --lights         every stage light (type / position / range / colour)
//   --objects        every model object (bones/meshes, vertex+face counts, animated?)
//   --normals        per-object authored vertex-normal stats (zero count / avg length) + samples
//   --motions        usable .inx motion slots (state / frame range)
//   --full           everything above

if (args.Length == 0 || args[0] is "-h" or "--help")
{
    Console.WriteLine(
        "ptdump <asset-path> [--materials] [--lights] [--objects] [--normals] [--motions] [--matbounds] [--physique] [--full]\n" +
        "ptdump --export-field-textures <asset-root> <field-id> <output-directory>\n" +
        "  Inspects a PristonTale .smd (stage or model), .smb (model) or .inx (index).\n" +
        "  A .bmp/.tga path decodes the texture and prints content stats (size/alpha/grayscale%).");
    return args.Length == 0 ? 1 : 0;
}

var path = args[0];
var flags = new HashSet<string>(args.Skip(1), StringComparer.OrdinalIgnoreCase);
bool Want(string f) => flags.Contains("--full") || flags.Contains(f);

if (!File.Exists(path))
{
    Console.Error.WriteLine($"error: file not found: {path}");
    return 1;
}

try
{
    var ext = Path.GetExtension(path).ToLowerInvariant();
    if (ext == ".inx")
    {
        DumpInx(new PristonInxReader().Read(path));
    }
    else if (ext is ".bmp" or ".tga")
    {
        DumpTextureStats(path);
    }
    else
    {
        var kind = DetectSmdKind(path); // "stage" | "model" | null
        var reader = new PristonSmdReader();
        if (kind == "stage")
            DumpStage(reader.ReadStage(path));
        else if (kind == "model")
            DumpModel(reader.ReadModel(path));
        else
        {
            Console.Error.WriteLine(
                "error: unrecognized header (not 'SMD Stage data' / 'SMD Model data'). " +
                "For .inx files keep the .inx extension.");
            return 1;
        }
    }
    return 0;
}
catch (Exception ex)
{
    Console.Error.WriteLine($"error parsing {path}: {ex.Message}");
    return 1;
}

// ---- detection ----------------------------------------------------------------------------

// The SMD header opens with a 24-byte name string (PristonSmdReader.ReadHeader). We only need
// the leading bytes to tell a stage from a model; the reader itself validates the full prefix.
static string? DetectSmdKind(string path)
{
    Span<byte> head = stackalloc byte[SmdLayout.SignatureSize];
    using var fs = File.OpenRead(path);
    var read = fs.Read(head);
    var name = Encoding.ASCII.GetString(head[..read]);
    if (name.StartsWith(SmdLayout.StagePrefix, StringComparison.Ordinal)) return "stage";
    if (name.StartsWith(SmdLayout.ModelPrefix, StringComparison.Ordinal)) return "model";
    return null;
}

// ---- dumpers ------------------------------------------------------------------------------

void DumpStage(PristonStage stage)
{
    Header($"STAGE  {stage.Path}");
    Console.WriteLine($"  materials : {stage.Materials.Count}");
    Console.WriteLine($"  vertices  : {stage.Vertices.Count}");
    Console.WriteLine($"  faces     : {stage.Faces.Count}");
    Console.WriteLine($"  texLinks  : {stage.TexLinks.Count}");
    int lNight = 0, lLens = 0, lDyn = 0;
    foreach (var l in stage.Lights)
    {
        if (l.Night) lNight++;
        if (l.Lens) lLens++;
        if (l.Dynamic) lDyn++;
    }
    Console.WriteLine($"  lights    : {stage.Lights.Count}  (night={lNight} lens={lLens} dynamic={lDyn})");
    int chained = 0; foreach (var tl in stage.TexLinks) if (tl.NextPointer != 0) chained++;
    Console.WriteLine($"  chained texture links (2nd UV set): {chained} of {stage.TexLinks.Count}");
    // Per MULTIMIX material: do ITS faces' texlinks carry a next pointer (genuine 2nd UV set) or not?
    for (var mi = 0; mi < stage.Materials.Count; mi++)
    {
        if (!stage.Materials[mi].HasSecondTexture) continue;
        int faces = 0, withNext = 0;
        foreach (var f in stage.Faces)
        {
            if (f.MaterialIndex != mi) continue;
            faces++;
            if (f.TexLinkIndex >= 0 && f.TexLinkIndex < stage.TexLinks.Count && stage.TexLinks[f.TexLinkIndex].NextPointer != 0) withNext++;
        }
        // MULTIMIX slot1 uses a 2nd UV set via the texlink NextPointer chain (D3DTSS_TEXCOORDINDEX=1). For
        // cSelect/Login.smd this 2nd set is a verbatim copy of UV0 — the slot1 effect comes from its
        // scroll (TextureFormStates[1]), not a different static mapping — so the renderer reuses UV0.
        Console.WriteLine($"  MULTIMIX mat {mi} ({stage.Materials[mi].TextureName}+{stage.Materials[mi].SecondTextureName}): faces={faces} faceTexlinks-with-pNext={withNext}");
    }
    Console.WriteLine($"  bounds    : {FmtBox(stage.Bounds)}");

    DumpMaterialAggregates(stage.Materials);

    if (Want("--lights") && stage.Lights.Count > 0)
    {
        Section("LIGHTS");
        Console.WriteLine($"  {"#",-4} {"type",-10} {"range",10}  position                       color");
        for (var i = 0; i < stage.Lights.Count; i++)
        {
            var l = stage.Lights[i];
            Console.WriteLine($"  {i,-4} 0x{l.Type:X8} {l.Range,10:0.0}  {FmtVec(l.Position),-30} {FmtVec(l.Color)}");
        }
    }

    if (Want("--materials"))
        DumpMaterialTable(stage.Materials);

    if (Want("--matbounds"))
    {
        Section("PER-MATERIAL FACE BOUNDS (locate geometry by material)");
        Console.WriteLine($"  {"#",-4} {"blend",-10} {"faces",6}  centre                         size                            texture");
        for (var mi = 0; mi < stage.Materials.Count; mi++)
        {
            int fc = 0; var min = new Vector3(float.MaxValue); var max = new Vector3(float.MinValue);
            foreach (var f in stage.Faces)
            {
                if (f.MaterialIndex != mi) continue;
                fc++;
                foreach (var vi in new[] { f.A, f.B, f.C })
                {
                    if (vi < 0 || vi >= stage.Vertices.Count) continue;
                    var p = stage.Vertices[vi].Position; min = Vector3.Min(min, p); max = Vector3.Max(max, p);
                }
            }
            if (fc == 0) continue;
            var c = (min + max) * 0.5f; var s = max - min;
            Console.WriteLine($"  {mi,-4} {BlendName(stage.Materials[mi].BlendType),-10} {fc,6}  {FmtVec(c),-30} {FmtVec(s),-30}  {stage.Materials[mi].TextureName}");
        }
    }
}

void DumpModel(PristonModel model)
{
    Header($"MODEL  {model.Path}");
    Console.WriteLine($"  objects   : {model.Objects.Count}");
    Console.WriteLine($"  materials : {model.Materials.Count}");
    Console.WriteLine($"  animated  : {model.IsAnimated}");
    Console.WriteLine($"  lastFrame : {model.LastFrame}");
    Console.WriteLine($"  bounds    : {FmtBox(model.Bounds)}");

    var verts = model.Objects.Sum(o => o.Vertices.Count);
    var faces = model.Objects.Sum(o => o.Faces.Count);
    var bones = model.Objects.Count(o => o.Vertices.Count == 0 || o.Name.StartsWith("Bip", StringComparison.OrdinalIgnoreCase));
    Console.WriteLine($"  totals    : {verts} verts, {faces} faces across {model.Objects.Count} objects");

    DumpMaterialAggregates(model.Materials);

    if (Want("--objects"))
    {
        Section("OBJECTS");
        Console.WriteLine($"  {"#",-4} {"name",-20} {"parent",-16} verts faces anim   basePosition                  posKey0");
        for (var i = 0; i < model.Objects.Count; i++)
        {
            var o = model.Objects[i];
            var bp = $"({o.BasePosition.X:0.0},{o.BasePosition.Y:0.0},{o.BasePosition.Z:0.0})";
            var tt = o.Transform.Translation;
            var tm = $"({tt.X:0.0},{tt.Y:0.0},{tt.Z:0.0})";
            Console.WriteLine($"  {i,-4} {Trunc(o.Name, 18),-18} {o.Vertices.Count,5} {o.Faces.Count,5} {o.IsAnimated,-5} base={bp,-22} TM={tm}");
        }
    }

    if (Want("--physique"))
    {
        Section("PHYSIQUE BONES (distinct skinning bones referenced by vertices)");
        var distinct = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var o in model.Objects)
            foreach (var bn in o.PhysiqueBoneNames)
                if (!string.IsNullOrWhiteSpace(bn))
                    distinct.Add(bn);
        Console.WriteLine($"  distinct physique bones used : {distinct.Count}");
        foreach (var bn in distinct)
            Console.WriteLine($"    {bn}");
    }

    if (Want("--normals"))
    {
        Section("VERTEX NORMALS (authored, as read)");
        for (var i = 0; i < model.Objects.Count; i++)
        {
            var o = model.Objects[i];
            if (o.Vertices.Count == 0) continue;
            var zero = o.Vertices.Count(v => v.Normal.LengthSquared() < 0.0001f);
            var avgLen = o.Vertices.Average(v => v.Normal.Length());
            Console.WriteLine($"  obj {i,-3} {Trunc(o.Name, 18),-18} verts={o.Vertices.Count,5}  zeroNormals={zero,4}  avgLen={avgLen:F3}");
            for (var v = 0; v < Math.Min(4, o.Vertices.Count); v++)
            {
                var n = o.Vertices[v].Normal;
                Console.WriteLine($"      v{v}: N=({n.X,7:F3},{n.Y,7:F3},{n.Z,7:F3})");
            }
        }
    }

    if (Want("--materials"))
        DumpMaterialTable(model.Materials);
}

void DumpInx(PristonModelInfo info)
{
    Header($"INX  {info.Path}");
    Console.WriteLine($"  modelFile    : {Show(info.ModelFile)}");
    Console.WriteLine($"  motionFile   : {Show(info.MotionFile)}");
    Console.WriteLine($"  subModelFile : {Show(info.SubModelFile)}");
    Console.WriteLine($"  linkFile     : {Show(info.LinkFile)}");
    Console.WriteLine($"  talkLinkFile : {Show(info.TalkLinkFile)}");
    Console.WriteLine($"  talkMotion   : {Show(info.TalkMotionFile)}");
    Console.WriteLine($"  LOD groups   : high={info.HighModel.ModelNameCount} default={info.DefaultModel.ModelNameCount} low={info.LowModel.ModelNameCount}");
    Console.WriteLine($"    high    : [{string.Join(", ", info.HighModel.ModelNames)}]");
    Console.WriteLine($"    default : [{string.Join(", ", info.DefaultModel.ModelNames)}]");
    Console.WriteLine($"    low     : [{string.Join(", ", info.LowModel.ModelNames)}]");
    Console.WriteLine($"  motionCount  : {info.MotionCount}  (array {info.Motions.Count})");
    Console.WriteLine($"  talkMotions  : {info.TalkMotionCount}  (array {info.TalkMotions.Count})");

    var usable = info.UsableMotions().ToList();
    Console.WriteLine($"  usable motions: {usable.Count}");

    // JobCodeBit gates which classes may wear this armour: bit (class - 1) of PlayerClass (0x1 Fighter, 0x2
    // Mechanician, ...). Reported as the OR of every motion's bits so a body armour's class set is visible.
    uint jobBits = 0;
    foreach (var m in info.Motions) jobBits |= m.JobCodeBit;
    Console.WriteLine($"  jobCodeBits  : 0x{jobBits:X}");

    if (Want("--motions") && usable.Count > 0)
    {
        Section("USABLE MOTIONS");
        // EventFrames are the raw INX values: (authoredFrame − StartFrame) × 160 offsets from the clip
        // start — printed raw + as the authored frame they resolve to, so
        // motion-timing work can be spot-checked against the bytes.
        Console.WriteLine($"  {"slot",-5} {"state",-10} {"start",8} {"end",8} {"repeat",7}  event frames (raw ×160 → authored)");
        foreach (var (slot, m) in usable)
        {
            var name = Enum.IsDefined((MotionCode)m.State) ? ((MotionCode)m.State).ToString() : "";
            var events = string.Join(", ", m.EventFrames
                .Where(e => e != 0)
                .Select(e => $"{e} → f{m.StartFrame + (e / GameUnits.FrameTicks)}"));
            Console.WriteLine($"  {slot,-5} 0x{m.State:X4} {m.StartFrame,8} {m.EndFrame,8} {m.Repeat,7}  [{events}]  {name}");
        }

        if (info.NpcMotionRates.Count > PristonFormatConstants.CharacterMotionBase && info.NpcMotionRates[PristonFormatConstants.CharacterMotionBase] != 0)
        {
            Section("NPC MOTION RATES");
            foreach (var (slot, _) in usable)
            {
                var rate = slot < info.NpcMotionRates.Count ? info.NpcMotionRates[slot] : 0;
                Console.WriteLine($"  slot {slot,-5} rate {rate,3}%");
            }

            var selected = info.NpcMotionSelection
                .GroupBy(static slot => slot)
                .OrderBy(static group => group.Key)
                .Select(static group => $"{group.Key}:{group.Count()}");
            Console.WriteLine($"  expanded     : {string.Join(", ", selected)}");
        }
    }
}

// ---- material helpers ---------------------------------------------------------------------

static void DumpMaterialAggregates(IReadOnlyList<PristonMaterial> mats)
{
    Section("MATERIAL AGGREGATES");
    var blendHist = new SortedDictionary<uint, int>();
    var formStateHist = new SortedDictionary<uint, int>();
    int lateBlend = 0, invisible = 0, water = 0, blinking = 0, selfIllum = 0,
        renderLast = 0, twoSided = 0, alphaMaybe = 0, checkFace = 0, animTex = 0, wind = 0;
    foreach (var m in mats)
    {
        blendHist[m.BlendType] = blendHist.GetValueOrDefault(m.BlendType) + 1;
        uint fs = 0;
        for (var k = 0; k < m.TextureFormStates.Count; k++) if (m.TextureFormStates[k] != 0) { fs = m.TextureFormStates[k]; break; }
        if (fs != 0) formStateHist[fs] = formStateHist.GetValueOrDefault(fs) + 1;
        // Move (0x1) = wind Z1; MoveX/Z bits = explicit wind axes (see PristonMaterialRuntimeFactory).
        if ((m.SurfaceFlags & (PristonMaterialSurfaceFlags.Move | PristonMaterialSurfaceFlags.MoveX1 | PristonMaterialSurfaceFlags.MoveX2 | PristonMaterialSurfaceFlags.MoveZ1 | PristonMaterialSurfaceFlags.MoveZ2)) != 0) wind++;
        if (PristonBlendType.IsLate(m.BlendType)) lateBlend++;
        if (m.IsInvisible) invisible++;
        if (m.IsWater) water++;
        if (m.IsBlinking) blinking++;
        if (m.IsSelfIllum) selfIllum++;
        if (m.RenderLast) renderLast++;
        if (m.TwoSided) twoSided++;
        if (!string.IsNullOrEmpty(m.OpacityName) || m.TextureName.EndsWith(".tga", StringComparison.OrdinalIgnoreCase)) alphaMaybe++;
        if ((m.MeshState & StageCollisionReader.CheckFace) != 0) checkFace++;
        if (m.HasAnimatedTexture) animTex++;
    }
    var hist = string.Join(", ", blendHist.Select(kv => $"{BlendName(kv.Key)}({kv.Key})={kv.Value}"));
    var fsHist = formStateHist.Count == 0 ? "(none)" : string.Join(", ", formStateHist.Select(kv => $"{kv.Key}={kv.Value}"));
    Console.WriteLine($"  blend histogram : {hist}");
    Console.WriteLine($"  formstate hist  : {fsHist}   (5 = FS_REFLEX, 4/6..18 = scroll)");
    Console.WriteLine($"  late-blend(2-6) : {lateBlend}   (NOT shadow casters / late pass)");
    Console.WriteLine($"  alpha-test'ish  : {alphaMaybe}   (.tga or opacity map)");
    Console.WriteLine($"  collision faces : {checkFace}   (MeshState & check face)");
    Console.WriteLine($"  flags           : invisible={invisible} water={water} blinking={blinking} " +
                      $"selfIllum={selfIllum} renderLast={renderLast} twoSided={twoSided} animTex={animTex} wind={wind}");
}

static void DumpMaterialTable(IReadOnlyList<PristonMaterial> mats)
{
    Section("MATERIALS");
    Console.WriteLine($"  {"#",-4} {"blend",-12} {"transp",7} {"2s",-3} {"mesh",-10} {"flags",-22} texture");
    for (var i = 0; i < mats.Count; i++)
    {
        var m = mats[i];
        var sf = m.SurfaceFlags == PristonMaterialSurfaceFlags.None ? "-" : m.SurfaceFlags.ToString();
        var tex = m.TextureName;
        if (!string.IsNullOrEmpty(m.OpacityName)) tex += $"  |op:{m.OpacityName}";
        // MULTIMIX slot1 (a second texture combined over slot0, e.g. a scrolling light layer). Show the
        // slot index whose form-state drives its scroll so the combine + scroll can be eyeballed.
        if (m.HasSecondTexture)
        {
            var fs1 = m.TextureFormStates.Count > 1 ? m.TextureFormStates[1] : 0u;
            tex += $"  |+slot1:{m.SecondTextureName}";
            if (!string.IsNullOrEmpty(m.SecondOpacityName)) tex += $"(op:{m.SecondOpacityName})";
            tex += $" fs1={fs1}";
        }
        Console.WriteLine($"  {i,-4} {BlendName(m.BlendType) + "(" + m.BlendType + ")",-12} {m.Transparency,7:0.000} " +
                          $"{(m.TwoSided ? "y" : "-"),-3} 0x{m.MeshState:X8} {Trunc(sf, 22),-22} {tex}");
    }
}

static string BlendName(uint b) => b switch
{
    0 => "NONE",
    PristonBlendType.Alpha => "ALPHA",
    PristonBlendType.Color => "COLOR",
    PristonBlendType.Shadow => "SHADOW",
    PristonBlendType.Lamp => "LAMP",
    PristonBlendType.AddColor => "ADDCOLOR",
    PristonBlendType.InverseShadow => "INVSHADOW",
    _ => "?"
};

// ---- texture stats (decoded by the engine; content that tells a mask from a colour map) ----

static void DumpTextureStats(string path)
{
    var image = PristonImageDecoder.Decode(File.ReadAllBytes(path), Path.GetExtension(path));
    var (w, h, px) = (image.Width, image.Height, image.Pixels);

    Header($"TEXTURE  {path}");
    Console.WriteLine($"  size   : {w} x {h}");
    if (!image.IsValid) { Console.WriteLine("  (could not decode pixels)"); return; }

    long sr = 0, sg = 0, sb = 0; int minA = 255, maxA = 0; int gray = 0; int greenDom = 0; int nearBlack = 0;
    foreach (var p in px)
    {
        sr += p.R; sg += p.G; sb += p.B;
        if (p.A < minA) minA = p.A; if (p.A > maxA) maxA = p.A;
        var mx = Math.Max(p.R, Math.Max(p.G, p.B)); var mn = Math.Min(p.R, Math.Min(p.G, p.B));
        if (mx - mn <= 12) gray++;                         // ~achromatic pixel
        if (p.G > p.R + 20 && p.G > p.B + 20) greenDom++;   // green-dominant
        if (mx <= 16) nearBlack++;
    }
    int n = px.Length;
    Console.WriteLine($"  avg RGB: ({sr / n}, {sg / n}, {sb / n})");
    Console.WriteLine($"  alpha  : min={minA} max={maxA}  ({(image.HasSourceAlpha ? "alpha from the file" : "no alpha in the file")})");
    Console.WriteLine($"  achromatic(gray) pixels : {gray * 100L / n}%   (high % => a grayscale MASK)");
    Console.WriteLine($"  green-dominant pixels   : {greenDom * 100L / n}%");
    Console.WriteLine($"  near-black pixels       : {nearBlack * 100L / n}%   (typical additive-glow background)");
    // A few samples: corners + centre.
    void Sample(string label, int x, int y) { var p = px[Math.Clamp(y, 0, h - 1) * w + Math.Clamp(x, 0, w - 1)]; Console.WriteLine($"    {label,-8} = rgba({p.R},{p.G},{p.B},{p.A})"); }
    Console.WriteLine("  samples:");
    Sample("centre", w / 2, h / 2); Sample("tl", w / 8, h / 8); Sample("br", w - w / 8, h - h / 8); Sample("midL", 2, h / 2);
}

// ---- formatting ---------------------------------------------------------------------------

static void Header(string s) { Console.WriteLine(); Console.WriteLine(s); Console.WriteLine(new string('=', Math.Min(s.Length, 78))); }
static void Section(string s) { Console.WriteLine(); Console.WriteLine($"-- {s} --"); }
static string FmtVec(Vector3 v) => $"({v.X:0.0}, {v.Y:0.0}, {v.Z:0.0})";
static string FmtBox(BoundingBox b) => $"{FmtVec(b.Min)} .. {FmtVec(b.Max)}";
static string Show(string s) => string.IsNullOrWhiteSpace(s) ? "(empty)" : s;
static string Trunc(string s, int n) => string.IsNullOrEmpty(s) ? "" : (s.Length <= n ? s : s[..(n - 1)] + "…");
