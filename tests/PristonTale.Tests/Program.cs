using PristonTale.Tests;

if (args.Length == 2 && args[0] == "--benchmark-parsing")
{
    BinaryReadBenchmark.Run(args[1]);
    return 0;
}

if (args.Length == 2 && args[0] == "--verify-shaders")
{
    using var probe = new ShaderValidationProbe(args[1]);
    probe.Run();
    return 0;
}

if (args.Length == 2 && args[0] == "--validate-binary-assets")
{
    BinaryAssetValidationProbe.Run(args[1]);
    return 0;
}

if (args.Length is 6 or 10 or 11 or 12 or 14 or 15 or 16 && args[0] == "--benchmark-render")
{
    var hasOptions = args.Length >= 10;
    var duration = hasOptions ? double.Parse(args[6], System.Globalization.CultureInfo.InvariantCulture) : 60;
    var repeat = hasOptions ? int.Parse(args[7]) : 5;
    var hour = hasOptions ? int.Parse(args[8]) : 12;
    var orbit = hasOptions && bool.Parse(args[9]);
    var cameraDistance = args.Length >= 11
        ? float.Parse(args[10], System.Globalization.CultureInfo.InvariantCulture)
        : 0f;
    var cameraPitch = args.Length is 12 or 14 or 15 or 16
        ? float.Parse(args[11], System.Globalization.CultureInfo.InvariantCulture)
        : float.NaN;
    var focusX = args.Length >= 14
        ? float.Parse(args[12], System.Globalization.CultureInfo.InvariantCulture)
        : float.NaN;
    var focusZ = args.Length >= 14
        ? float.Parse(args[13], System.Globalization.CultureInfo.InvariantCulture)
        : float.NaN;
    var cameraYaw = args.Length >= 15
        ? float.Parse(args[14], System.Globalization.CultureInfo.InvariantCulture)
        : 0f;
    var crowd = args.Length == 16 ? int.Parse(args[15]) : 0;
    using var probe = new RenderBenchmarkProbe(
        args[1],
        args[2],
        args[3],
        args[4],
        args[5],
        duration,
        repeat,
        hour,
        orbit,
        cameraDistance,
        cameraPitch,
        focusX,
        focusZ,
        cameraYaw,
        crowd);
    probe.Run();
    probe.WriteReport();
    return 0;
}

if (args.Length == 4 && args[0] == "--capture-ranged-equipment")
{
    using var probe = new EquipmentCaptureProbe(args[1], args[2], args[3], ranged: true);
    probe.Run();
    return 0;
}

if (args.Length == 2 && args[0] == "--verify-vfx-lifetimes")
{
    return VfxLingerProbe.Run(args[1]);
}

if (args.Length == 4 && args[0] == "--capture-ground-loot")
{
    using var probe = new GroundLootCaptureProbe(args[1], args[2], args[3]);
    probe.Run();
    return 0;
}

if (args.Length == 3 && args[0] == "--verify-world-rewards")
{
    await WorldRewardsProbe.RunAsync(args[1], args[2]);
    return 0;
}

if (args.Length == 3 && args[0] == "--export-equipment-timings")
{
    EquipmentAssetProbe.ExportTimings(args[1], args[2]);
    return 0;
}

if (args.Length == 2 && args[0] == "--verify-equipment")
{
    EquipmentAssetProbe.Run(args[1]);
    return 0;
}

if (args.Length == 4 && args[0] == "--capture-equipment")
{
    using var probe = new EquipmentCaptureProbe(args[1], args[2], args[3]);
    probe.Run();
    return 0;
}

if (args.Length == 4 && args[0] == "--capture-combat-effects")
{
    using var probe = new CombatEffectsCaptureProbe(args[1], args[2], args[3]);
    probe.Run();
    return 0;
}

if (args.Length == 2 && args[0] == "--verify-monster-skills")
{
    MonsterSkillTimingProbe.Run(args[1]);
    return 0;
}

if (args.Length is 4 or 5 && args[0] == "--capture-skills")
{
    var codes = args.Length == 5 ? args[4].Split(',').Select(code => Convert.ToUInt32(code, 16)).ToArray() : null;
    using var probe = new SkillCaptureProbe(args[1], args[2], args[3], codes);
    probe.Run();
    return 0;
}

if (args.Length is 4 or 5 && args[0] == "--capture-monster-skills")
{
    var names = args.Length == 5 ? args[4].Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries) : null;
    using var probe = new MonsterSkillCaptureProbe(args[1], args[2], args[3], names);
    probe.Run();
    return 0;
}

if (args.Length is 4 or 5 && args[0] == "--capture-vfx")
{
    var names = args.Length == 5 ? args[4].Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries) : null;
    using var probe = new VfxCaptureProbe(args[1], args[2], args[3], names);
    probe.Run();
    return 0;
}

if (args.Length == 2 && args[0] == "--verify-skill-motions")
{
    SkillMotionProbe.Run(args[1]);
    return 0;
}

if (args.Length == 2 && args[0] == "--verify-player-timing")
{
    PlayerTimingProbe.Run(args[1]);
    return 0;
}


if (args.Length == 4 && args[0] == "--capture-potions")
{
    using var probe = new PotionCaptureProbe(args[1], args[2], args[3]);
    probe.Run();
    return 0;
}


if (args.Length == 4 && args[0] == "--capture-sky")
{
    using var probe = new SkyCaptureProbe(args[1], args[2], args[3]);
    probe.Run();
    return 0;
}


if (args.Length == 2 && args[0] == "--inspect-ricarten-ground")
{
    GroundTraversalTests.InspectRicarten(args[1]);
    return 0;
}

if (args.Length == 5 && args[0] == "--capture-login")
{
    using var probe = new LoginCaptureProbe(args[1], args[2], args[3], args[4]);
    probe.Run();
    return 0;
}

if (args.Length == 5 && args[0] == "--check-look" && args[1] is "charselect" or "actor" or "opening" or "scenery" or "shade")
{
    using var probe = new SceneLookProbe(args[1], args[2], args[3], args[4]);
    probe.Run();
    return 0;
}

if (args.Length == 4 && args[0] == "--capture-npcs")
{
    using var probe = new ActorPresentationProbe(args[1], args[2], args[3]);
    probe.Run();
    return 0;
}

if (args.Length == 4 && args[0] == "--capture-job-heads")
{
    using var probe = new ActorPresentationProbe(args[1], args[2], args[3], jobHeads: true);
    probe.Run();
    return 0;
}


if (args.Length == 3 && args[0] == "--inspect-actor")
{
    ActorAssetProbe.Run(args[1], args[2]);
    return 0;
}

if (args.Length == 4 && args[0] == "--benchmark-presence")
{
    PresenceLoadBenchmark.Run(args[1].Split(',').Select(int.Parse).ToArray(), args[2].Split(','), int.Parse(args[3]));
    return 0;
}

if (args.Contains("--benchmark-world"))
{
    WorldLoadBenchmark.Run();
    return 0;
}

if (args.Length == 4 && args[0] == "--capture-hud-target")
{
    using var probe = new HudTargetCaptureProbe(args[1], args[2], args[3]);
    probe.Run();
    return 0;
}

if (args.Length == 4 && args[0] == "--capture-shadows")
{
    using var probe = new ShadowCaptureProbe(args[1], args[2], args[3]);
    probe.Run();
    return 0;
}

if (args.Length == 4 && args[0] == "--capture-weather")
{
    using var probe = new WeatherCaptureProbe(args[1], args[2], args[3]);
    probe.Run();
    return 0;
}

if (args.Length == 4 && args[0] == "--capture-item-effects")
{
    using var probe = new ItemEffectsCaptureProbe(args[1], args[2], args[3]);
    probe.Run();
    return 0;
}

if (args.Length == 4 && args[0] == "--capture-item-hud")
{
    using var probe = new ItemHudCaptureProbe(args[1], args[2], args[3]);
    probe.Run();
    return 0;
}

if (args.Length is 4 or 5 && args[0] == "--capture-chat")
{
    using var probe = new ChatCaptureProbe(args[1], args[2], args[3], args.Length == 5 ? args[4] : null);
    probe.Run();
    return 0;
}

if (args.Length is 4 or 5 && args[0] == "--capture-hud")
{
    using var probe = new HudCaptureProbe(args[1], args[2], args[3], args.Length == 5 ? args[4] : null);
    probe.Run();
    return 0;
}

if (args.Length == 2 && args[0] == "--profile-ricarten")
{
    await FieldLoadBenchmark.RunAsync(args[1]);
    return 0;
}

if (args.Length == 3 && args[0] is "--profile-stream" or "--profile-stream-baseline")
{
    using var probe = new FieldStreamingProbe(args[1], args[2], args[0] == "--profile-stream-baseline");
    probe.Run();
    return 0;
}

// Every probe above matches its exact argument count; anything else left here is a mistyped probe, not the suite.
for (var index = 0; index < args.Length; index++)
{
    if (args[index] == "--filter" && index + 1 < args.Length)
    {
        index++;
    }
    else if (args[index] != "--benchmark")
    {
        Console.Error.WriteLine($"Unknown option or wrong argument count: {string.Join(' ', args)} (see docs/testing.md).");
        return 2;
    }
}

// Every class's static Cases(), in class-name order: a new area file runs without being listed here.
var specifications = typeof(Assert).Assembly.GetTypes()
    .Select(static type => type.GetMethod("Cases", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static, Type.EmptyTypes))
    .Where(static method => method?.ReturnType == typeof(IEnumerable<(string, Func<Task>)>))
    .OrderBy(static method => method!.DeclaringType!.FullName, StringComparer.Ordinal)
    .SelectMany(static method => (IEnumerable<(string Name, Func<Task> Run)>)method!.Invoke(null, null)!)
    .ToList();
// --filter <text> runs only the cases whose name contains the text.
var filterIndex = Array.IndexOf(args, "--filter");
if (filterIndex >= 0 && filterIndex + 1 < args.Length)
{
    specifications.RemoveAll(spec => !spec.Item1.Contains(args[filterIndex + 1], StringComparison.OrdinalIgnoreCase));
}

var failed = 0;
foreach (var (name, run) in specifications)
{
    Task? task = null;
    try
    {
        task = run();
        await task.WaitAsync(TimeSpan.FromSeconds(15));
        Console.WriteLine($"PASS {name}");
    }
    catch (Exception ex)
    {
        failed++;
        Console.Error.WriteLine($"FAIL {name}: {ex}");
        // A timed-out case keeps running and would share the process with the cases after it.
        if (task is { IsCompleted: false })
        {
            Console.Error.WriteLine("Stopping: the timed-out case is still running.");
            break;
        }
    }
}

Console.WriteLine($"{specifications.Count - failed}/{specifications.Count} passed");
if (args.Contains("--benchmark"))
{
    await DomainTests.BenchmarkAsync();
}

return failed == 0 ? 0 : 1;
