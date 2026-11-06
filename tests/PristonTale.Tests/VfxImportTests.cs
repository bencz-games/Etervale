using PristonTale.Effects.Vfx;
using PristonTale.Simulation.Skills;
using PristonTale.VfxImport;

namespace PristonTale.Tests;

// The converter of the original effect group scripts and particle scripts (tools/PristonTale.VfxImport).
internal static class VfxImportTests
{
    public static IEnumerable<(string, Func<Task>)> Cases()
    {
        yield return ("effect group scripts convert with the original defaults, axes and accepted calls", GroupScriptAsync);
        yield return ("particle scripts convert with the original tokenizer's quirks and failures", ParticleScriptAsync);
        yield return ("authored definitions kept with the importer validate and cover every skill effect", AuthoredAsync);
    }

    private static Task GroupScriptAsync()
    {
        const string script = """
            -- comment
            Begin("Parent");
            InitPos(0, 50, 0);
            End();
            Begin("Mesh");
            InitMeshName("Res\\Object\\Roar.ASE");
            InitMaxFrame(15);
            InitLoop(1);
            InitPos(0,0,5)
            EventFadeColor(0,0,0,0,0);
            EventFadeSize(0.1,1,1,1);
            EventFadeColor(0.2,80,180,255,255);
            End();
            Begin("ParticleSystem");
            InitTextureName("Res\\Texture\\flare.bmp");
            InitVelocity(-10,10,10,70-10,-10);
            InitSpawnBoundingBox(-5,5,0,1,2,4);
            InitBlendType("Alpha");
            End();
            Begin("BillboardAxial");
            InitTextureName("Res\\Texture\\missing.bmp");
            End();
            """;
        var report = new List<string>();
        var assets = new AssetPaths(["Effect/NewEffect/Res/Object/Roar.smd", "Effect/NewEffect/Res/Texture/flare.bmp"]);
        var definition = EffectScriptConverter.Convert("Test", script, assets, report);
        definition.Validate();
        Assert.True(definition.Name == "script.Test" && definition.Components.Length == 2, "components lost or a missing texture kept");
        var mesh = definition.Components[0];
        Assert.True(mesh.Kind == VfxComponentKind.Model && mesh.Model == "Effect/NewEffect/Res/Object/Roar.smd" && mesh.ModelFrames == 15
            && mesh.Loop == 1 && mesh.Offset[2] == -5f, "model, frames, loop or the forward axis");
        Assert.True(mesh.Events.Length == 2 && mesh.Events.All(static e => e.Kind == VfxComponentEventKind.Color && e.Ramp),
            "a ramped size was keyed on a model");
        var particles = definition.Components[1];
        Assert.True(particles.Loop == 1 && particles.Count == 50 && particles.Rate == 30 && particles.EndTime[0] == 1f && particles.EndTime[1] == 2f
            && particles.SizeMin[0] == 5f && particles.SizeMax[0] == 10f && particles.OffsetTurn == VfxOffsetTurn.Reverse, "particle defaults");
        // "70-10" evaluates to 60 and the missing sixth argument reads as 0; Z bounds negate and swap.
        Assert.True(particles.VelocityMin[1] == 10f && particles.VelocityMax[1] == 60f && particles.VelocityMin[2] == 0f && particles.VelocityMax[2] == 10f,
            "velocity arithmetic, missing argument or Z axis");
        Assert.True(particles.SpawnMin[2] == -4f && particles.SpawnMax[2] == -2f, "spawn box Z axis");
        Assert.True(particles.Blend == VfxBlend.Additive && report.Any(line => line.Contains("blend Alpha", StringComparison.Ordinal))
            && report.Any(line => line.Contains("missing.bmp", StringComparison.Ordinal)), string.Join("; ", report));
        return Task.CompletedTask;
    }

    // Converted names ("script.", "particle.") need the game data and are checked by the game data case.
    private static Task AuthoredAsync()
    {
        var source = FindAuthored();
        Assert.True(source is not null, "the importer's Authored folder was not found");
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var definitions = new List<VfxDefinition>();
        var files = VfxImporter.AuthoredFiles(source!).ToArray();
        Assert.True(files.Length > 0, "no authored definitions");
        foreach (var file in files)
        {
            foreach (var definition in VfxDefinition.ParseFile(File.ReadAllText(file)))
            {
                definition.Validate();
                Assert.True(names.Add(definition.Name), $"{definition.Name} is defined twice");
                definitions.Add(definition);
            }
        }

        // Concentration's and Swift Axe's cast lights hold to the Fighter; Rage of Zecram's burst stays where it began.
        var heldCasts = definitions.Where(static definition => definition.Name is "vfx.fighter.concentration.cast" or "vfx.fighter.swiftaxe.cast").ToArray();
        var zecram = definitions.Single(static definition => definition.Name == "vfx.fighter.zecram.cast");
        Assert.True(heldCasts.Length == 2 && heldCasts.All(static definition =>
            definition.Parts.Length > 0 && definition.Parts.All(static part => part.Anchor == VfxAnchor.Caster && part.Follow))
            && zecram.Parts.All(static part => !part.Follow), "a Fighter cast effect follows its caster otherwise than the original's");

        static bool Converted(string name) => name.StartsWith("script.", StringComparison.Ordinal) || name.StartsWith("particle.", StringComparison.Ordinal);
        var started = definitions
            .SelectMany(static definition => definition.Children.Select(static child => child.Effect)
                .Concat(definition.Parts.SelectMany(static part => new[] { part.EndEffect, part.EveryEffect, part.CarryEffect })))
            .Where(name => name.Length > 0 && !Converted(name) && !names.Contains(name))
            .Distinct()
            .ToArray();
        Assert.True(started.Length == 0, "effects start undefined effects: " + string.Join(", ", started));
        var missing = SkillRuleCatalog.All
            .SelectMany(static rule => new[] { rule }.Concat(rule.Modes).Concat(rule.Charge?.Full is { } full ? [full] : Array.Empty<SkillRule>()))
            .SelectMany(static rule => new[] { rule.CastEffect, rule.ImpactEffect, rule.ProjectileEffect, rule.BuffEffect, rule.CompanionEffect }.Concat(rule.ImpactEffects).Where(static name => name != SkillRule.Silent))
            .Where(name => name.Length > 0 && !Converted(name) && !names.Contains(name))
            .Distinct()
            .ToArray();
        Assert.True(missing.Length == 0, "skills reference undefined effects: " + string.Join(", ", missing));
        return Task.CompletedTask;
    }

    private static string? FindAuthored()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            var candidate = Path.Combine(directory.FullName, "tools", "PristonTale.VfxImport", VfxImporter.AuthoredFolder);
            if (Directory.Exists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }

    private static Task ParticleScriptAsync()
    {
        const string script = """
            particlesystem "Hit" 1.00 {
              position = XYZ(0,100,0)
              eventsequence "spark" {
                sourceblendmode = BLEND_INVSHADOW
                loops = 1
                numparticles = 12
                gravity = XYZ(0,-100,5)
                emitrate = random(30,40)
                emitradius = xyz(0,0,random(1,3))
                lifetime = 0.5
                particletype = TYPE_FOUR
                texture = "effect\\particle\\flare.tga"
                initial size =20
                initial color = rgba(255,200,100,255)
                initial velocity = xyz(random(-50,50),random(20,80),random(-50,10))
                initial partangle = xyz(10,20,30)
                initial localanglex = 45
                at 0.4 eventtimer = 0.1
                fade so final color = rgba(0,0,0,0)
              }
            }
            """;
        var report = new List<string>();
        var assets = new AssetPaths(["Effect/Particle/flare.tga"]);
        var definition = ParticleScriptConverter.Convert("Hit", script, assets, report);
        definition.Validate();
        var sequence = definition.Sequences.Single();
        Assert.True(definition.Name == "particle.Hit" && sequence.Blend == VfxBlend.InvShadow && sequence.Shape == VfxSequenceShape.Trail
            && sequence.Count == 12 && sequence.Loops == 1 && sequence.Rate[0] == 30f && sequence.Rate[1] == 40f && sequence.Texture == "Effect/Particle/flare.tga",
            "sequence properties");
        Assert.True(sequence.GravityMin[2] == -5f && sequence.RadiusMin[2] == -3f && sequence.RadiusMax[2] == -1f, "Z axis of gravity or radius");
        var size = sequence.Events.Single(static e => e.Set == VfxSequenceProperty.Size);
        Assert.True(size.Min[0] == 0f && report.Any(line => line.Contains("unreadable", StringComparison.Ordinal)), "a value glued to '=' must be lost");
        var velocity = sequence.Events.Single(static e => e.Set == VfxSequenceProperty.Velocity);
        Assert.True(velocity.Min[2] == -10f && velocity.Max[2] == 50f, "velocity Z axis");
        var angle = sequence.Events.Single(static e => e.Set == VfxSequenceProperty.Angle);
        Assert.True(angle.Min[0] == -10f && angle.Min[1] == -20f && angle.Min[2] == 30f, "mirrored rotation angles");
        Assert.True(sequence.Events.Any(static e => e.Set == VfxSequenceProperty.OrbitY && e.Min[0] == -45f), "localanglex sets the Y local angle");
        Assert.True(sequence.Events.Any(static e => e.Set == VfxSequenceProperty.Clock && e.T == 0.4f && e.Min[0] == 0.1f), "clock event");
        Assert.True(sequence.Events.Any(static e => e.Set == VfxSequenceProperty.Color && e.Ramp && e.T == 0.5f), "final time is the lifetime");

        report.Clear();
        var rejected = ParticleScriptConverter.Convert("Bad", script.Replace("fade so final color", "fade so color", StringComparison.Ordinal), assets, report);
        Assert.True(rejected.Sequences.Length == 0 && report.Any(line => line.Contains("plays nothing", StringComparison.Ordinal)),
            "a script the original rejects must play nothing");
        return Task.CompletedTask;
    }
}
