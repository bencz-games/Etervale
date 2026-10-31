using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.CompilerServices;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Content;
using Microsoft.Xna.Framework.Graphics;
using PristonTale.Engine3D.Diagnostics;
using PristonTale.Engine3D.Formats;
using PristonTale.Engine3D.Rendering.Lighting;
using PristonTale.Engine3D.Rendering.Post;
using PristonTale.Contracts.Motion;
using PristonTale.Contracts.Units;

namespace PristonTale.Engine3D.Rendering;

// Frame lighting: glow tuning, emitter colours and the lights chosen for the frame.
public sealed partial class PristonRenderer
{
    private static PristonPointLight[] ApplyGlowTuning(PristonPointLight[] lights, float intensity, float scale)
    {
        var tuned = new PristonPointLight[lights.Length];
        for (var i = 0; i < lights.Length; i++)
            tuned[i] = lights[i] with { Intensity = intensity, Radius = lights[i].Radius * scale };
        return tuned;
    }

    private static void AssignEmitterGlowColors(RenderStageHandle handle, PristonStage stage)
    {
        foreach (var prop in handle.AnimatedModels)
        {
            if (!prop.IsGlowEmitter)
                continue;

            var center = prop.WorldCenter();
            var best = float.MaxValue;
            var color = Vector3.One;
            foreach (var light in stage.Lights)
            {
                var d = Vector3.DistanceSquared(light.Position, center);
                if (d < best) { best = d; color = light.Color; }
            }
            prop.GlowColor = color;
        }
    }

    private static (PristonPointLight[] ground, PristonPointLight[] glow) BuildStageLights(PristonStage stage, IClientLog log)
    {
        var ground = new List<PristonPointLight>();
        var glow = new List<PristonPointLight>();
        int nNight = 0, nLens = 0, nDynamic = 0;

        foreach (var l in stage.Lights)
        {
            var radius = l.Range > 1f ? l.Range : 600f;
            var light = new PristonPointLight(l.Position, radius, l.Color, 1f);
            var isLens = (l.Type & LightLens) != 0;
            if ((l.Type & LightNight) != 0) nNight++;
            if (isLens) nLens++;
            if ((l.Type & LightDynamic) != 0) nDynamic++;

            if (isLens) glow.Add(light);
            if (!isLens || (l.Type & LightDynamic) != 0) ground.Add(light);
        }

        // Only LENS lights receive coronas; dungeon flames use their animated emitters.
        var glowLights = glow.ToArray();
        var groundLights = ground.ToArray();

        log.Info(
            $"[PristonRenderer] {stage.Lights.Count} stage light(s) from {Path.GetFileName(stage.Path)}: " +
            $"{groundLights.Length} ground, {glowLights.Length} glow " +
            $"(night={nNight}, lens={nLens}, dynamic={nDynamic}).");
        return (groundLights, glowLights);
    }

    // The frame's light in photometric units (PristonDayNightCycle, Lighting/PristonPhotometry), pre-exposed by the
    // incident reading: a white surface under the metered light comes out near 1 at every hour.
    private PristonLighting BuildFrameLighting(Vector3 focusPoint)
    {
        var sky = _dayNight.Evaluate();
        var sunIlluminance = sky.SunIlluminance;
        var skyIlluminance = sky.SkyIlluminance;
        var lampFactor = sky.LampFactor;
        var skyColor = sky.SkyColor;
        var sunDirection = sky.SunDirection;
        var horizontal = sky.HorizontalIlluminance;
        var metered = horizontal;
        if (Underground)
        {
            sunIlluminance = Vector3.Zero;
            skyIlluminance = PristonPhotometry.LevelToIlluminance(UndergroundAmbient);
            lampFactor = 1f;
            skyColor = new Vector3(0.02f, 0.02f, 0.03f);
            horizontal = metered = PristonPhotometry.Luminance(skyIlluminance);
        }
        else
        {
            (sunIlluminance, skyIlluminance, horizontal, metered, lampFactor) = WeatherLight(sunIlluminance, skyIlluminance, horizontal, lampFactor);
        }

        SkyColor = new Color(skyColor);
        _lampFactor = lampFactor;
        _towardSun = sky.TowardSun;
        _sunElevation = Underground ? -1f : sky.SunElevation;
        _exposure = PristonFrameExposure.ForIlluminance(metered, Underground, ExposureCompensation, EmissiveLuminance);
        var radianceScale = _exposure.PreExposure / MathF.PI;
        var sunColor = sunIlluminance * radianceScale;
        var ambient = skyIlluminance * radianceScale;
        var ground = PristonPhotometry.Hue(Vector3.Lerp(sunIlluminance, skyIlluminance, 0.5f))
            * (horizontal * PristonPhotometry.GroundAlbedo * radianceScale);
        _fogSunColour = sunColor;
        RainRadiance = Vector3.Max(ambient + sunColor * RainSunShare, new Vector3(RainLeastLight));
        var sun = new PristonDirectionalLight(Vector3.Normalize(sunDirection), sunColor);
        // Transient combat lights come first, then the stage lamps nearest the focus, up to the shader's light count.
        _activeLights.Clear();
        TransientLights.Select(focusPoint, _activeLights);
        var transientCount = _activeLights.Count;
        SelectActiveLights(focusPoint, lampFactor);
        // Combat lights are flashes of feedback: a share of the daylight, so they still read at noon.
        var transientScale = MathF.Max(1f, TransientDaylightShare * horizontal / PristonPhotometry.NightReferenceIlluminance);
        for (var index = 0; index < _activeLights.Count; index++)
        {
            var light = _activeLights[index];
            var scale = index < transientCount ? transientScale : LampLevelScale;
            var illuminance = PristonPhotometry.LevelToIlluminance(light.Color * light.Intensity) * scale;
            _activeLights[index] = light with { Color = illuminance * radianceScale, Intensity = 1f };
        }

        return new PristonLighting(ambient, sun, _activeLights, transientCount, ground);
    }

    private void SelectActiveLights(Vector3 focusPoint, float lampFactor)
    {
        if (lampFactor <= 0.01f)
            return;

        _tunedCandidates.Clear();
        foreach (var stage in _stages)
            foreach (var light in stage.StageLights)
                _tunedCandidates.Add(TuneLight(light, stage, lampFactor));

        if (_tunedCandidates.Count == 0)
            return;

        // Transient lights already hold the first slots; the lamps share the rest of the limit.
        var limit = Math.Max(_maxLocalLights - _activeLights.Count, 0);
        if (limit == 0)
            return;

        if (_tunedCandidates.Count <= limit)
        {
            // Nearest first, as the lamp shadows take the first lamps.
            var first = _activeLights.Count;
            _activeLights.AddRange(_tunedCandidates);
            for (var i = first + 1; i < _activeLights.Count; i++)
            {
                var light = _activeLights[i];
                var distance = Vector3.DistanceSquared(light.Position, focusPoint);
                var j = i - 1;
                while (j >= first && Vector3.DistanceSquared(_activeLights[j].Position, focusPoint) > distance)
                {
                    _activeLights[j + 1] = _activeLights[j];
                    j--;
                }

                _activeLights[j + 1] = light;
            }

            return;
        }

        Span<int> best = stackalloc int[limit];
        Span<float> bestDist = stackalloc float[limit];
        for (var i = 0; i < limit; i++) { best[i] = -1; bestDist[i] = float.MaxValue; }

        for (var i = 0; i < _tunedCandidates.Count; i++)
        {
            var d = Vector3.DistanceSquared(_tunedCandidates[i].Position, focusPoint);
            for (var slot = 0; slot < limit; slot++)
            {
                if (d < bestDist[slot])
                {
                    for (var k = limit - 1; k > slot; k--)
                    {
                        bestDist[k] = bestDist[k - 1];
                        best[k] = best[k - 1];
                    }
                    bestDist[slot] = d;
                    best[slot] = i;
                    break;
                }
            }
        }

        for (var i = 0; i < limit; i++)
            if (best[i] >= 0)
                _activeLights.Add(_tunedCandidates[best[i]]);
    }

    private static PristonPointLight TuneLight(PristonPointLight light, RenderStageHandle stage, float lampFactor) =>
        light with
        {
            Radius = light.Radius * stage.LampRangeScale * LampRangeStretch,
            Intensity = light.Intensity * stage.LampIntensity * lampFactor
        };
}
