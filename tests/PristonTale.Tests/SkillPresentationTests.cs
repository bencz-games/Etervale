using Microsoft.Xna.Framework;
using PristonTale.Client.Net.Presence;
using PristonTale.Effects.Vfx;
using PristonTale.Engine3D.IO;
using PristonTale.Gameplay.Effects;
using PristonTale.Gameplay.UI.Widgets;
using PristonTale.Protocol.Packets;
using PristonTale.Simulation.Characters;
using PristonTale.Simulation.Skills;
using PristonTale.Contracts.Skills;
using PristonTale.Assets;
using PristonTale.Contracts.Characters;

namespace PristonTale.Tests;

internal static class SkillPresentationTests
{
    public static IEnumerable<(string, Func<Task>)> Cases()
    {
        yield return ("effect definitions validate every layer and reject unknown builtin textures", DefinitionsAsync);
        yield return ("the effect catalog reads Effect/Vfx recursively, skips broken files and reports replaced names", CatalogAsync);
        yield return ("a numbered effect frame puts its number before the last dot", FrameNamesAsync);
        yield return ("game data effects cover every effect, child, texture, model and sound the skill rules use", GameDataAsync);
        yield return ("vfx system emits, travels, pools instances and keeps its budget", SystemAsync);
        yield return ("cooldown and status books follow the server's messages", BooksAsync);
        yield return ("skill sockets offer learned active skills and drop lost ones", BindingsAsync);
        yield return ("floating combat text keeps a bounded ring", FloatingTextAsync);
    }

    private static Task FrameNamesAsync()
    {
        Assert.True(VfxCatalog.FrameName("Effect/Fire.bmp", 3) == "Effect/Fire3.bmp", "frame number goes before the extension");
        Assert.True(VfxCatalog.FrameName("Effect/v1.2/Ice.light.tga", 12) == "Effect/v1.2/Ice.light12.tga", "only the last dot starts the extension");
        Assert.True(VfxCatalog.FrameName("Glow", 2) == "Glow2", "a name without extension ends with the number");
        Assert.True(VfxCatalog.Frames("Spark.bmp", 1).SequenceEqual(["Spark.bmp"])
            && VfxCatalog.Frames("Spark.bmp", 2).SequenceEqual(["Spark1.bmp", "Spark2.bmp"]), "a single frame keeps the plain name");
        return Task.CompletedTask;
    }

    private static Task DefinitionsAsync()
    {
        const string effect = """
            [{ "name": "vfx.test", "duration": 1, "emitters": [{ "texture": "builtin:glw", "burst": 1, "lifetime": [0.1, 0.2] }] }]
            """;
        Rejected(effect);
        Assert.True(VfxDefinition.ParseFile(effect.Replace("builtin:glw", "builtin:glow", StringComparison.Ordinal)).Length == 1,
            "a valid builtin texture was refused");
        Rejected("""[{ "name": "bad", "duration": 0 }]""");
        Rejected("""[{ "name": "bad", "duration": 601 }]""");
        Rejected("""[{ "name": "bad", "emitters": [{ "size": [{ "t": 0.5, "v": [1] }, { "t": 0.2, "v": [2] }] }] }]""");
        Rejected("""[{ "name": "bad", "components": [{ "kind": "Model" }] }]""");
        Rejected("""[{ "name": "bad", "components": [{ "kind": "Billboard", "texture": "a.tga", "events": [{ "t": 0, "kind": "Color", "value": [1, 2] }] }] }]""");
        Rejected("""[{ "name": "bad", "sequences": [{ "texture": "a.tga", "count": 0, "rate": [1, 1], "lifetime": [1, 1] }] }]""");
        Rejected("""[{ "name": "bad", "sequences": [{ "texture": "a.tga", "rate": [2, 1], "lifetime": [1, 1] }] }]""");
        Rejected("""[{ "name": "bad", "children": [{ "effect": "bad" }] }]""");
        var parsed = VfxDefinition.ParseFile("""
            [{ "name": "good", "duration": 2,
               "components": [{ "kind": "Ground", "texture": "Effect/x.bmp", "events": [{ "t": 0.2, "kind": "Size", "ramp": true, "value": [3, 3, 0] }] }],
               "sequences": [{ "texture": "Effect/Particle/x.bmp", "count": 4, "loops": 1, "rate": [10, 10], "lifetime": [0.5, 0.5],
                 "events": [{ "t": 0, "set": "Color", "min": [255, 255, 255, 255], "max": [255, 255, 255, 255] }] }],
               "children": [{ "effect": "other", "follow": true }] }]
            """).Single();
        Assert.True(parsed.Components.Single().Kind == VfxComponentKind.Ground && parsed.Sequences.Single().Count == 4
            && parsed.Children.Single().Follow, "component, sequence or child layer lost");
        return Task.CompletedTask;
    }

    private static void Rejected(string json)
    {
        var threw = false;
        try
        {
            VfxDefinition.ParseFile(json);
        }
        catch (InvalidDataException)
        {
            threw = true;
        }

        Assert.True(threw, $"an invalid definition was accepted: {json}");
    }

    private static Task CatalogAsync()
    {
        const string effect = """[{ "name": "vfx.test", "duration": 1, "emitters": [{ "texture": "builtin:glow", "burst": 1, "lifetime": [0.1, 0.2] }] }]""";
        var root = Path.Combine(Path.GetTempPath(), $"priston-vfx-{Guid.NewGuid():N}");
        var folder = VfxCatalog.FolderOf(root);
        Directory.CreateDirectory(Path.Combine(folder, "Classes", "Knight"));
        try
        {
            File.WriteAllText(Path.Combine(folder, "a.vfx.json"), effect);
            File.WriteAllText(Path.Combine(folder, "broken.vfx.json"), "[{ \"name\": ");
            File.WriteAllText(Path.Combine(folder, "Classes", "Knight", "Knight.vfx.json"),
                effect.Replace("\"duration\": 1", "\"duration\": 2", StringComparison.Ordinal));
            File.WriteAllText(Path.Combine(folder, "notes.json"), effect);
            var catalog = VfxCatalog.Load(root);
            Assert.True(catalog.Files == 3 && catalog.Definitions.Count == 2, $"catalog read {catalog.Files} files, {catalog.Definitions.Count} definitions");
            Assert.True(catalog.Errors.Count == 2 && catalog.Errors.Any(error => error.Contains("broken", StringComparison.Ordinal))
                && catalog.Errors.Any(error => error.Contains("replaces", StringComparison.Ordinal)), string.Join("; ", catalog.Errors));
            // Files load in ordinal order of their relative path, so "a.vfx.json" (after "Classes/...") wins.
            var library = new VfxLibrary(_ => null, null!);
            foreach (var definition in catalog.Definitions)
            {
                library.Register(definition);
            }

            Assert.True(library.TryGet("VFX.TEST", out var compiled) && compiled.Definition.Duration == 1f, "the later file did not win");
            Assert.True(VfxCatalog.Load(Path.Combine(root, "missing")).Files == 0, "a missing folder did not read as empty");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }

        return Task.CompletedTask;
    }

    // Every effect a rule, its modes or its charged resolution names.
    private static IEnumerable<string> Effects(SkillRule rule)
    {
        var rules = new[] { rule }.Concat(rule.Modes).Concat(rule.Charge?.Full is { } full ? [full] : Array.Empty<SkillRule>());
        return rules.SelectMany(static item => new[] { item.CastEffect, item.ImpactEffect, item.ProjectileEffect, item.BuffEffect, item.CompanionEffect, item.TargetEffect, item.DefenseEffect, item.ShotEffect }.Concat(item.ImpactEffects)).Where(static name => name != SkillRule.Silent);
    }

    private static Task GameDataAsync()
    {
        var root = FindAssets();
        if (root is null || !Directory.Exists(Path.Combine(root, "Effect", "Vfx")))
        {
            Console.WriteLine("  (skipped: no assets/Effect/Vfx next to the repository)");
            return Task.CompletedTask;
        }

        var catalog = VfxCatalog.Load(root);
        Assert.True(catalog.Errors.Count == 0, string.Join("; ", catalog.Errors.Take(10)));
        var names = catalog.Definitions.Select(definition => definition.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        Assert.True(names.Contains(SkillPresentation.ReviveEffect) && names.Contains(SkillPresentation.RestartEffect), "the revive or restart effect is missing");
        var problems = new List<string>();
        foreach (var hit in new[] { HitEffects.Critical, HitEffects.Normal, HitEffects.Bare, HitEffects.Blood }.Where(name => !names.Contains(name)))
        {
            problems.Add($"the hit effect {hit} is missing");
        }

        foreach (var rule in SkillRuleCatalog.All)
        {
            // A hit effect is shown in the element the status drew: each of its choices needs its variant or the base.
            var elements = (rule.Buff?.Enchant?.Choices.Select(static choice => choice.Element) ?? rule.Buff?.Companion?.Choices.Select(static choice => choice.Element))?.ToArray();
            var hitNames = rule.HitEffect.Length == 0 ? [] : elements is { Length: > 1 }
                ? elements.Select(element => rule.HitEffect + "." + element.ToString().ToLowerInvariant()).ToArray()
                : new[] { rule.HitEffect };
            foreach (var hit in hitNames.Where(name => !names.Contains(name) && !names.Contains(rule.HitEffect)))
            {
                problems.Add($"{rule.Name} references missing hit effect {hit}");
            }

            foreach (var effect in Effects(rule))
            {
                if (effect.Length > 0 && !names.Contains(effect))
                {
                    problems.Add($"{rule.Name} references missing effect {effect}");
                }
            }

            foreach (var sound in new[] { rule.CastSound, rule.ImpactSound, rule.CompanionSound, rule.ShieldModel }.Concat(rule.ImpactSounds).Where(static sound => sound != SkillRule.Silent))
            {
                if (sound.Length > 0 && !AssetFiles.Exists(Path.Combine(root, sound)))
                {
                    problems.Add($"{rule.Name} sound {sound} is missing");
                }
            }
        }

        foreach (var definition in catalog.Definitions)
        {
            foreach (var child in definition.Children)
            {
                if (!names.Contains(child.Effect))
                {
                    problems.Add($"{definition.Name} starts missing effect {child.Effect}");
                }
            }

            foreach (var texture in VfxCatalog.Textures(definition))
            {
                if (texture.Length > 0 && !texture.StartsWith(VfxBuiltinTextures.Prefix, StringComparison.Ordinal)
                    && !AssetFiles.Exists(Path.Combine(root, texture)))
                {
                    problems.Add($"{definition.Name} texture {texture} is missing");
                }
            }

            foreach (var part in definition.Parts)
            {
                if (part.Animation.Length > 0 && !AssetFiles.Exists(Path.Combine(root, "Effect", "AnimationData", part.Animation)))
                {
                    problems.Add($"{definition.Name} animation {part.Animation} is missing");
                }

                if (part.Model.Length > 0 && !AssetFiles.Exists(Path.Combine(root, part.Model)))
                {
                    problems.Add($"{definition.Name} model {part.Model} is missing");
                }

                foreach (var started in new[] { part.EndEffect, part.EveryEffect, part.CarryEffect })
                {
                    if (started.Length > 0 && !names.Contains(started))
                    {
                        problems.Add($"{definition.Name} starts missing effect {started}");
                    }
                }
            }

            foreach (var sound in definition.Sounds.SelectMany(static sound => sound.AllPaths))
            {
                if (!AssetFiles.Exists(Path.Combine(root, sound)))
                {
                    problems.Add($"{definition.Name} sound {sound} is missing");
                }
            }

            foreach (var component in definition.Components.Where(static component => component.Kind == VfxComponentKind.Model))
            {
                if (!AssetFiles.Exists(Path.Combine(root, component.Model)))
                {
                    problems.Add($"{definition.Name} model {component.Model} is missing");
                }
            }
        }

        Assert.True(problems.Count == 0, string.Join("; ", problems.Distinct().Take(20)));
        return Task.CompletedTask;
    }

    private static Task SystemAsync()
    {
        var library = new VfxLibrary(_ => null, null!);
        var burst = library.Register(new VfxDefinition
        {
            Name = "test.burst",
            Duration = 0.5f,
            Emitters = [new VfxEmitterDefinition { Burst = 10, MaxParticles = 10, Lifetime = [0.2f, 0.2f], Speed = [10f, 10f], Spread = 180 }]
        });
        var stream = library.Register(new VfxDefinition
        {
            Name = "test.stream",
            Duration = 1f,
            Emitters = [new VfxEmitterDefinition { Rate = 100, MaxParticles = 200, Lifetime = [0.1f, 0.1f] }],
            Trails = [new VfxTrailDefinition { Lifetime = 0.2f }]
        });
        var anchors = new Anchors();
        var system = new VfxSystem(anchors);
        var instance = system.Spawn(burst, new VfxSpawn(0, 0, Vector3.Zero, 0f)).Instance!;
        system.Update(0.01f);
        Assert.True(system.ParticleCount == 10, $"burst emitted {system.ParticleCount}");
        for (var frame = 0; frame < 120; frame++)
        {
            system.Update(1f / 60f);
        }

        Assert.True(system.Instances.Count == 0 && !instance.Alive, "finished effect stayed alive");
        var moving = system.Spawn(stream, new VfxSpawn(0, 0, Vector3.Zero, 0f)).Instance!;
        moving.Travel(Vector3.Zero, new Vector3(300, 0, 0), 0.5f);
        var peak = 0;
        for (var frame = 0; frame < 30; frame++)
        {
            system.Update(1f / 60f);
            peak = Math.Max(peak, system.ParticleCount);
        }

        Assert.True(moving.Spawn.Point.X >= 299f && !moving.Travelling, "projectile did not arrive");
        Assert.True(peak is > 5 and <= 200, $"stream particle count {peak}");
        for (var frame = 0; frame < 60; frame++)
        {
            system.Update(1f / 60f);
        }

        Assert.True(system.Instances.Count == 0, "arrived projectile kept emitting");
        for (var index = 0; index < VfxSystem.MaxInstances + 10; index++)
        {
            system.Spawn(burst, new VfxSpawn(0, 0, Vector3.Zero, 0f));
        }

        Assert.True(system.Instances.Count == VfxSystem.MaxInstances, "instance budget exceeded");
        var allocated = GC.GetAllocatedBytesForCurrentThread();
        for (var frame = 0; frame < 20; frame++)
        {
            system.Update(1f / 60f);
        }

        Assert.True(GC.GetAllocatedBytesForCurrentThread() - allocated < 4096, "vfx update allocated per frame");
        return Task.CompletedTask;
    }

    private static Task BooksAsync()
    {
        var cooldowns = new SkillCooldownBook();
        cooldowns.Apply(new SkillCastResultInfo(1, 0x08010002, SkillCastVerdict.Accepted, 1, 10000, 10000, 0, 0), 1000);
        Assert.True(!cooldowns.Ready(0x08010002, 5000) && Math.Abs(cooldowns.Fraction(0x08010002, 6000) - 0.5f) < 0.001f, "cooldown sweep");
        cooldowns.Apply(new SkillCooldownSnapshot(0, []), 6000);
        Assert.True(cooldowns.Ready(0x08010002, 6000), "snapshot without the skill did not clear it");
        var statuses = new StatusBook();
        var added = 0;
        var removed = 0;
        statuses.Added += (_, _) => added++;
        statuses.Removed += (_, _) => removed++;
        statuses.Apply(new StatusEffectInfo(1, 1, 0x05010002, StatusEffectOp.Add, 1, 2, 0, 0, 30000, 30000, 8), 0);
        statuses.Apply(new StatusEffectInfo(1, 1, 0x05010002, StatusEffectOp.Refresh, 1, 2, 0, 0, 30000, 30000, 8), 5000);
        Assert.True(added == 1 && statuses.For(1).Single().RemainingMs(10000) == 25000, "refresh restarted the timer incorrectly");
        statuses.Apply(new StatusEffectInfo(1, 1, 0x05010002, StatusEffectOp.Remove, 1, 2, 0, 0, 30000, 0, 8), 6000);
        Assert.True(removed == 1 && statuses.For(1).Count == 0, "remove ignored");
        return Task.CompletedTask;
    }

    private static Task BindingsAsync()
    {
        var bindings = new HudSkillBindings();
        var skills = new CharacterClassRegistry().Get(PlayerClass.Magician).Skills;
        bindings.SyncLearned(skills, [new OwnedSkillView(0x08010002, 2), new OwnedSkillView(0x08010001, 1)]);
        Assert.True(bindings.Select(0x08010002, SkillMouseUse.Left) && bindings.Select(0x08010001, SkillMouseUse.Right), "learned skill not selectable");
        Assert.True(!bindings.Select(0x08010001, SkillMouseUse.Left), "right-only skill placed on the left socket");
        Assert.True(bindings.Assign(0, 0x08010002, SkillMouseUse.Left), "short key binding refused");
        bindings.SyncLearned(skills, [new OwnedSkillView(0x08010002, 2)]);
        Assert.True(bindings.RightSkillId == HudSkillBindings.BasicAttackId && bindings.LeftSkillId == 0x08010002, "lost skill kept its socket");
        return Task.CompletedTask;
    }

    private static Task FloatingTextAsync()
    {
        var text = new FloatingCombatText();
        for (var index = 0; index < FloatingCombatText.Capacity * 2; index++)
        {
            text.Add(Vector3.Zero, index, FloatingTextKind.DamageDealt);
        }

        Assert.True(text.Count == FloatingCombatText.Capacity, "floating text grew past its ring");
        text.Update(5f);
        Assert.True(text.Count == 0, "floating text did not expire");
        return Task.CompletedTask;
    }

    private static string? FindAssets()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            var candidate = Path.Combine(directory.FullName, "assets");
            if (Directory.Exists(Path.Combine(candidate, "Effect")))
            {
                return candidate;
            }
        }

        return null;
    }

    private sealed class Anchors : IVfxAnchors
    {
        public bool TryGetPosition(ulong entityId, out Vector3 position)
        {
            position = Vector3.Zero;
            return false;
        }

        public bool TryGetBone(ulong entityId, string bone, out Vector3 position)
        {
            position = Vector3.Zero;
            return false;
        }
    }
}
