using PristonTale.Engine3D.Formats;
using PristonTale.Engine3D.Rendering;
using PristonTale.Gameplay.World.Remote;
using PristonTale.Simulation.Characters;
using PristonTale.Contracts.Motion;
using PristonTale.Contracts.Characters;

namespace PristonTale.Tests;

internal static class EquipmentAssetProbe
{
    public static void ExportTimings(string root, string output)
    {
        var registry = new CharacterClassRegistry();
        var loader = new PristonActorLoader();
        var lines = new List<string>();
        for (int job = 1; job <= (int)PlayerClass.MartialArtist; job++)
        {
            var definition = registry.Get((PlayerClass)job).Definition;
            var actor = loader.Load(root, Path.Combine(root, definition.DefaultVisuals.Body.InxExtensionPath.Replace('\\', '/')));
            var groups = new Dictionary<(uint Start, uint End, uint Hit), List<string>>();
            foreach (string key in EquipmentVisuals.CatalogKeys.Order(StringComparer.Ordinal))
            {
                if (!key.StartsWith('W'))
                {
                    continue;
                }

                var clip = PristonMotionSelection.Select(actor.Info, MotionCode.Attack,
                    new(EquipmentVisuals.MotionIndex(key), definition.MotionJobMask, 2));
                var timing = (clip.StartFrame, clip.EndFrame, clip.EventFrames[0]);
                if (!groups.TryGetValue(timing, out var keys))
                {
                    keys = [];
                    groups.Add(timing, keys);
                }

                keys.Add(key);
            }

            foreach (var (timing, keys) in groups)
            {
                lines.Add($"  {{\"class\": \"{(PlayerClass)job}\", \"start\": {timing.Start}, \"end\": {timing.End}, \"hit\": {timing.Hit}, "
                    + $"\"keys\": [{string.Join(", ", keys.Select(static key => $"\"{key}\""))}]}}");
            }
        }

        File.WriteAllText(output, "[\n" + string.Join(",\n", lines) + "\n]\n");
    }

    public static void Run(string root)
    {
        var registry = new CharacterClassRegistry();
        var loader = new PristonActorLoader();
        string[] weapons = ["WA101", "WC101", "WS101", "WP101", "WT101", "WS201", "WM101", "WM101", "WD101", "WN101", "WV101"];
        for (int index = 0; index < weapons.Length; index++)
        {
            var definition = registry.Get((PlayerClass)(index + 1)).Definition;
            var path = Path.Combine(root, definition.DefaultVisuals.Body.InxExtensionPath.Replace('\\', '/'));
            var actor = loader.Load(root, path);
            int item = EquipmentVisuals.MotionIndex(weapons[index]);
            var armed = PristonMotionSelection.Select(actor.Info, MotionCode.Attack, new(item, definition.MotionJobMask, 2));
            var bare = PristonMotionSelection.Select(actor.Info, MotionCode.Attack, new(-1, definition.MotionJobMask, 2));
            if (armed is null || bare is null)
            {
                string candidates = string.Join(", ", actor.Info.Motions.Where(motion => motion.State == (uint)MotionCode.Attack)
                    .Select(motion => $"{motion.StartFrame}-{motion.EndFrame}: job={motion.JobCodeBit:X}, map={motion.MapPosition}"));
                throw new InvalidDataException($"No attack for {definition.Name}, job={definition.MotionJobMask:X}: {candidates}");
            }

            Console.WriteLine($"{definition.Name} {weapons[index]} index={item}: armed={armed.StartFrame}-{armed.EndFrame} hit={armed.EventFrames[0]}; bare={bare.StartFrame}-{bare.EndFrame} hit={bare.EventFrames[0]}");
            foreach (string key in EquipmentVisuals.CatalogKeys.Where(key => key.StartsWith('W')))
            {
                var clip = PristonMotionSelection.Select(actor.Info, MotionCode.Attack,
                    new(EquipmentVisuals.MotionIndex(key), definition.MotionJobMask, 2));
                Assert.True(PristonTale.Simulation.World.Motion.PlayerEquipmentTiming.TryGet((PlayerClass)(index + 1), key, out var shared), "missing authoritative equipped timing");
                Assert.True(shared.Frames == clip.EndFrame - clip.StartFrame && shared.Event0 == clip.EventFrames[0], "authoritative timing differs from rendered INX");
            }
        }
    }
}
