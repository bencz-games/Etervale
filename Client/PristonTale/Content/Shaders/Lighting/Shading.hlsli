// Direct and ambient lighting of a Surface (Materials/Material.hlsli): the sun with its cascaded shadows (PCF or PCSS),
// the screen-space occlusion (ambient occlusion and contact shadows), the local lights and a sky/ground hemispherical
// ambient. Requires FRAME_CONSTANTS, LIGHTING_CONSTANTS and LIGHTING_RESOURCES.
#ifndef LIGHTING_SHADING_HLSLI
#define LIGHTING_SHADING_HLSLI

#include "Brdf.hlsli"
#include "Octahedral.hlsli"
#include "CubeFaces.hlsli"
#include "Environment.hlsli"
#include "SphericalHarmonics.hlsli"

// Share of a cascade's far end over which it hands over to the next one (dithered), and the last one fades out.
static const float CascadeBlendBand = 0.12;
// PCSS: the largest filter radius (texels), and the penumbra radii over which the turned disk takes over from the tent.
static const float PcssMaxRadiusTexels = 9.0;
static const float PcssDiskFrom = 1.5;
static const float PcssDiskTo = 3.0;
// Lamp shadows: a soft source, so a wide kernel and a little bounce light left in them.
// World units a character's own body is ignored by along the sun when it receives the sun's shadow.
static const float ActorShadowBias = 40.0;
static const float LampShadowStrength = 0.8;

static const float2 PoissonDisk[16] =
{
    float2(-0.942016, -0.399062), float2( 0.945586, -0.768907),
    float2(-0.094184, -0.929388), float2( 0.344959,  0.293877),
    float2(-0.915885,  0.457714), float2(-0.815442, -0.879124),
    float2(-0.382775,  0.276768), float2( 0.974844,  0.756484),
    float2( 0.443233, -0.975115), float2( 0.537430, -0.473734),
    float2(-0.264969, -0.418930), float2( 0.791975,  0.190902),
    float2(-0.241888,  0.997065), float2(-0.814100,  0.914376),
    float2( 0.199841,  0.786414), float2( 0.143832, -0.141008)
};

// The pixel's interleaved gradient noise, offset per frame when the temporal anti-aliasing integrates it.
float PixelNoise(float2 pixel)
{
    float2 p = pixel + NoiseParams.x * 5.588238;
    return InterleavedGradientNoise(p);
}

// The four texels around uv and their bilinear weights, in Gather's order (0, 1), (1, 1), (1, 0), (0, 0); the weights sum
// to 1, so a texel's share changes continuously as uv crosses it instead of in whole-texel steps.
float4 ShadowGatherAt(float2 uv, out float4 weights)
{
    float2 position = uv / ShadowTexel.xy - 0.5;
    float2 base = floor(position);
    float2 f = position - base;
    weights = float4((1.0 - f.x) * f.y, f.x * f.y, f.x * (1.0 - f.y), (1.0 - f.x) * (1.0 - f.y));
    return ShadowMap.GatherRed(ShadowSampler, (base + 1.0) * ShadowTexel.xy);
}

// The lit share at uv: the four texels around it compared with the reference and weighted bilinearly.
float ShadowLitAt(float2 uv, float reference)
{
    float4 weights;
    float4 stored = ShadowGatherAt(uv, weights);
    return dot(float4(stored >= reference), weights);
}

// The continuous tent of half-width reach around a point of the atlas (in texels, from the atlas origin): reach 2 is
// the 3x3 tent (four gathers over 4x4 texels, standard deviation 0.82 texel), reach 3 the 5x5 tent (nine gathers over 6x6
// texels, 1.22). Each texel weighs (reach - |dx|)(reach - |dy|) by its distance to the point, reach^4 in all, so the edge
// moves continuously, and the result is the same in every frame, with no noise left for the temporal anti-aliasing (which
// cannot average it where the history is clipped or absent). Also returns the depth and count of the texels in front of
// the reference.
float ShadowTent(Texture2D<float4> map, SamplerState state, float2 texel, float2 grid, float reference, int reach,
                 out float blockerSum, out float blockers)
{
    float2 base = floor(grid);
    float2 f = grid - base;
    float lit = 0.0;
    blockerSum = 0.0;
    blockers = 0.0;
    [unroll]
    for (int y = 1 - reach; y < reach; y += 2)
    {
        [unroll]
        for (int x = 1 - reach; x < reach; x += 2)
        {
            // Gather order: (0, 1), (1, 1), (1, 0), (0, 0) from the texel at (x, y).
            float4 dx = float4(x, x + 1, x + 1, x) - f.x;
            float4 dy = float4(y + 1, y + 1, y, y) - f.y;
            float4 weights = max(reach - abs(dx), 0.0) * max(reach - abs(dy), 0.0);
            float4 stored = map.GatherRed(state, (base + float2(x + 1, y + 1)) * texel);
            float4 lit4 = float4(stored >= reference);
            lit += dot(weights, lit4);
            blockerSum += dot(1.0 - lit4, stored);
            blockers += 4.0 - dot(lit4, 1.0);
        }
    }

    return lit / (reach * reach * reach * reach);
}

// Visibility of one cascade. The atlas tile holds the orthographic light's z (Passes/Surface.fx ShadowCaster), which is
// linear in light-space depth; D3D clip conventions (the Vulkan compiler flips Y), so uv = (x, -y) * 0.5 + 0.5.
float CascadeVisibility(uint c, float3 worldPosition, float3 normal, float nl, float2x2 rotation, float receiverBias)
{
    float texelWorld = CascadeTexelWorld[c];
    float depthRange = CascadeDepthRange[c];
    float normalOffset = texelWorld * (0.8 + 1.6 * (1.0 - nl));
    float4 lightPosition = mul(float4(worldPosition + normal * normalOffset, 1.0), CascadeViewProjection[c]);
    float3 projected = lightPosition.xyz / lightPosition.w;
    float2 local = float2(projected.x, -projected.y) * 0.5 + 0.5;
    float4 tile = CascadeAtlas[c];
    float2 texel = ShadowTexel.xy;
    float2 centre = tile.xy + local * tile.zw;
    float reference = saturate(projected.z) - (texelWorld * (0.5 + 1.5 * (1.0 - nl)) + receiverBias) / depthRange;
    // The tents and the PCSS rings reach six texels from the point: keep them inside the tile.
    float2 grid = clamp(centre, tile.xy + 6.5 * texel, tile.xy + tile.zw - 6.5 * texel) / texel - 0.5;
    float blockerSum;
    float blockers;
    [branch]
    if (ShadowParams.y < 0.5)
    {
        return ShadowTent(ShadowMap, ShadowSampler, texel, grid, reference, 3, blockerSum, blockers);
    }

    // PCSS (Fernando 2005) for a directional light: the penumbra their mean depth gives, (receiver - blocker) *
    // tan(light radius). The 3x3 tent keeps contact shadows as sharp as they are; blockers in two rings of corners
    // beyond it (three and five texels out) widen the penumbra outward as well.
    float lit = ShadowTent(ShadowMap, ShadowSampler, texel, grid, reference, 2, blockerSum, blockers);
    float2 base = floor(grid);
    [unroll]
    for (int corner = 0; corner < 8; corner++)
    {
        float far = corner < 4 ? 3.0 : 5.0;
        float2 offset = float2((corner & 1) != 0 ? far : -far - 1.0, (corner & 2) != 0 ? far : -far - 1.0);
        float4 stored = ShadowMap.GatherRed(ShadowSampler, (base + offset + 1.0) * texel);
        float4 blocking = float4(stored < reference);
        blockerSum += dot(blocking, stored);
        blockers += dot(blocking, 1.0);
    }

    float radius = blockers > 0.0 ? (reference - blockerSum / blockers) * depthRange * ShadowParams.z / texelWorld : 0.0;
    float disk = saturate((radius - PcssDiskFrom) / (PcssDiskTo - PcssDiskFrom));
    // A penumbra up to the tent's width keeps the tent's exact, still result; a wider one (tall casters far above the
    // receiver) blends into a disk of its radius, turned per pixel, whose noise is spread over a soft gradient.
    [branch]
    if (disk > 0.0)
    {
        float2 lo = tile.xy + texel;
        float2 hi = tile.xy + tile.zw - texel;
        float width = min(radius, PcssMaxRadiusTexels);
        float diskLit = 0.0;
        [unroll]
        for (int i = 0; i < 16; i++)
        {
            diskLit += ShadowLitAt(clamp(centre + mul(PoissonDisk[i], rotation) * texel * width, lo, hi), reference);
        }

        lit = lerp(lit, diskLit / 16.0, disk);
    }

    return lit;
}

// Sun visibility in [0, 1]: the cascade by view depth, blended into the next one over the blend band (picking one per
// pixel by noise made the two cascades' edges flicker against each other); the last cascade fades out toward the end of
// the shadow distance.
// receiverBias (world units) keeps a character's own thin, low-polygon body from shadowing itself in faceted triangles;
// buildings and trees farther along the light still shade it.
// contact is the screen-space contact shadow's visibility; the darker of it and the map wins, so a surface both shade is
// not darkened twice.
float SunShadow(float3 worldPosition, float3 normal, float nl, float viewDepth, float2 pixel, float receiverBias, float contact)
{
    uint count = clamp((uint)ShadowParams.x, 1u, (uint)MAX_CASCADES);
    [branch]
    if (ShadowParams.w <= 0.0 || nl <= 0.0 || viewDepth >= CascadeSplits[count - 1])
    {
        return contact;
    }

    uint c = 0;
    [unroll]
    for (uint k = 0; k < MAX_CASCADES - 1; k++)
    {
        c += (k + 1 < count && viewDepth >= CascadeSplits[k]) ? 1 : 0;
    }

    float noise = PixelNoise(pixel);
    float split = CascadeSplits[c];
    float start = c == 0 ? 0.0 : CascadeSplits[c - 1];
    float band = (split - viewDepth) / max((split - start) * CascadeBlendBand, 1.0e-3);
    float next = 0.0;
    float fade = 1.0;
    if (band < 1.0)
    {
        if (c + 1 < count)
        {
            next = 1.0 - saturate(band);
        }
        else
        {
            fade = saturate(band);
        }
    }

    float angle = noise * 2.0 * PI;
    float2 direction = float2(cos(angle), sin(angle));
    float2x2 rotation = float2x2(direction.x, -direction.y, direction.y, direction.x);
    float visibility = CascadeVisibility(c, worldPosition, normal, nl, rotation, receiverBias);
    [branch]
    if (next > 0.0)
    {
        visibility = lerp(visibility, CascadeVisibility(c + 1, worldPosition, normal, nl, rotation, receiverBias), next);
    }

    return min(1.0 - ShadowParams.w * (1.0 - visibility) * fade, contact);
}

// The screen-space occlusion at a pixel (Passes/AmbientOcclusion.fx): x ambient occlusion, y contact shadow visibility.
// The map is half resolution; the four nearest texels are weighted bilinearly and by how close their depth is.
float2 ScreenOcclusion(float2 pixel, float viewDepth)
{
    [branch]
    if (ShadowTexel.w < 0.5)
    {
        return 1.0;
    }

    float2 position = pixel * 0.5 - 0.5;
    float2 base = floor(position);
    float2 f = position - base;
    float2 sum = 0.0;
    float weights = 0.0;
    [unroll]
    for (int y = 0; y < 2; y++)
    {
        [unroll]
        for (int x = 0; x < 2; x++)
        {
            float4 s = OcclusionMap.SampleLevel(OcclusionSampler, (base + float2(x, y) + 0.5) * OcclusionParams.xy, 0);
            float bilinear = (x == 0 ? 1.0 - f.x : f.x) * (y == 0 ? 1.0 - f.y : f.y) + 1.0e-3;
            float relative = abs(s.z - viewDepth) / max(viewDepth, 1.0);
            float w = bilinear / (1.0e-4 + relative * relative);
            sum += s.xy * w;
            weights += w;
        }
    }

    float2 occlusion = sum / weights;
    return float2(lerp(1.0, occlusion.x, OcclusionParams.w), lerp(1.0, occlusion.y, OcclusionParams.z));
}

// The lamps' cube shadows (Lighting/PristonLampShadows.cs): six faces per lamp side by side in its row, each a 90-degree
// view along a major axis (Lighting/CubeFaces.hlsli), holding the distance to the lamp over its radius. The 5x5 tent
// (ShadowTent) softens the edge the same way in every frame; a share of the light stays in the shadow (bounce from the
// lamp-lit surroundings).

float LampShadow(uint row, float3 fromLight, float radius, float nl)
{
    uint face = CubeFace(fromLight);
    float2 local = CubeFaceUv(fromLight, face, 1.0e-3);
    float faceSize = LampShadowParams.z;
    float2 texel = LampShadowParams.xy;
    // The tent reaches three texels from the point: keep it inside the face.
    float2 inner = clamp(local * faceSize, 3.5, faceSize - 3.5);
    float2 origin = float2(face, row) * faceSize;
    float distance = length(fromLight) / radius;
    float reference = distance - (0.004 + 0.012 * (1.0 - nl));
    float blockerSum;
    float blockers;
    float lit = ShadowTent(LampShadowMap, LampShadowSampler, texel, origin + inner - 0.5, reference, 3, blockerSum, blockers);
    return 1.0 - LampShadowStrength * (1.0 - lit);
}

// Specular occlusion from the ambient occlusion (Lagarde and de Rousiers 2014).
float SpecularOcclusion(float nv, float ao, float roughness)
{
    return saturate(pow(nv + ao, exp2(-16.0 * roughness - 1.0)) - 1.0 + ao);
}

// Light reaching a surface, split for the debug views and for the material's own terms.
struct Lighting
{
    float3 Diffuse;
    float3 Specular;
};

static const float LampWrap = 0.25;
// Lamps fall off with the inverse square of the distance (PristonRenderer: the colour is the illuminance at
// LampReferenceDistance), held constant inside LampMinDistance (the lamp's own size), and windowed to zero at the
// radius by (1 - (d / r)^4)^2 (Karis 2013). The reference is one distance for every lamp, so a lamp's intensity is
// comparable whatever its range: a small brazier is set dimmer, not given a shorter reference.
static const float LampReferenceDistance = 150.0;
static const float LampMinDistance = 45.0;

// Local lights keep the legacy radius-limited falloff, (1 - d/r)^2, so the authored lamp ranges still hold; transient
// combat lights stay radial (normal-independent), as the game has always lit them.
// The lights of the pixel's tile (Passes/LightCull.fx), or all of them without tile masks.
uint LocalLightMask(float2 pixel)
{
    uint count = (uint)LocalLightCount;
    uint all = count >= 32 ? 0xffffffffu : (1u << count) - 1u;
    [branch]
    if (TileParams.z < 0.5)
    {
        return all;
    }

    uint2 tile = (uint2)(pixel * TileParams.y);
    return TileLightMasks[tile.y * (uint)TileParams.x + tile.x] & all;
}

void AddLocalLights(float3 worldPosition, float3 n, float3 v, float3 albedo, float3 f0, float metallic, float a,
                    float2 pixel, bool lampShadows, inout Lighting lighting)
{
    uint transients = (uint)TransientLightCount;
    uint mask = LocalLightMask(pixel);
    [loop]
    while (mask != 0)
    {
        uint i = firstbitlow(mask);
        mask &= mask - 1u;

        float3 toLight = LocalLightPosition[i].xyz - worldPosition;
        float radius = LocalLightPosition[i].w;
        float distanceSquared = dot(toLight, toLight);
        [branch]
        if (radius <= 0.0 || distanceSquared >= radius * radius)
        {
            continue;
        }

        if (i < transients)
        {
            float falloff = saturate(1.0 - distanceSquared / (radius * radius));
            lighting.Diffuse += LocalLightColor[i].rgb * falloff * albedo * (1.0 - metallic);
            continue;
        }

        float distanceToLight = sqrt(distanceSquared);
        float3 l = toLight / max(distanceToLight, 1.0e-3);
        float ratio = distanceSquared / (radius * radius);
        float window = saturate(1.0 - ratio * ratio);
        float inverseSquare = LampReferenceDistance * LampReferenceDistance / max(distanceSquared, LampMinDistance * LampMinDistance);
        float3 light = LocalLightColor[i].rgb * (inverseSquare * window * window);
        // The lamps are large, soft sources: a quarter of their light wraps around to surfaces facing away (the legacy
        // lamp light's 0.25 + 0.75 n.l); the specular lobe keeps the exact n.l.
        float nl = saturate(dot(n, l));
        float row = LocalLightColor[i].w - 1.0;
        [branch]
        if (lampShadows && row >= 0.0)
        {
            light *= LampShadow((uint)row, -toLight, radius, nl);
        }

        float3 diffuseLight = light * (LampWrap + (1.0 - LampWrap) * nl);
        float3 unusedDiffuse = 0.0;
        AddDirectLight(l, light, n, v, albedo, f0, metallic, a, unusedDiffuse, lighting.Specular);
        lighting.Diffuse += (1.0 - F_SchlickRough(f0, saturate(dot(v, SafeNormalize(l + v))), sqrt(a))) * (1.0 - metallic) * albedo * diffuseLight;
    }
}

// Irradiance (E / pi) from the sky's spherical harmonics (Passes/Environment.fx), cosine-convolved per band.
float3 EnvironmentIrradiance(float3 n)
{
    float3 c[9];
    [unroll]
    for (uint k = 0; k < 9; k++)
    {
        c[k] = EnvironmentSh[k].rgb;
    }

    return max(EvaluateSh(c, n), 0.0);
}

// Prefiltered radiance toward r for a perceptual roughness, in the light model's units (the atlas's scale,
// EnvironmentSh[9].x).
float3 EnvironmentSpecular(float3 r, float roughness)
{
    return EnvironmentAtlasRadiance(EnvironmentMap, EnvironmentSampler, EnvironmentParams, r, roughness) * EnvironmentSh[9].x;
}

// Image-based lighting: the sky's irradiance and its prefiltered radiance with the split-sum BRDF (Karis 2013), the
// multiple-scattering energy of rough metals restored (Fdez-Aguera 2019), occluded by the ambient occlusion. Without an
// environment, a hemispherical sky/ground ambient.
// ao occludes the sky's light, specularVisibility its reflections (ao and the authored light).
void AddAmbient(float3 n, float3 v, float3 albedo, float3 f0, float metallic, float roughness, float ao, float specularVisibility,
                inout Lighting lighting)
{
    float nvEnvironment = saturate(dot(n, v));
    [branch]
    if (EnvironmentParams.x > 0.5)
    {
        float2 brdf = BrdfLut.SampleLevel(BrdfLutSampler, float2(nvEnvironment, roughness), 0).rg;
        // F = f0 + (F90 - f0) Fc integrates to f0 A + F90 B, with the rough surface's grazing reflectance as F90.
        float3 single = f0 * brdf.x + GrazingReflectance(f0, roughness) * brdf.y;
        float3 energy = 1.0 + f0 * (1.0 / max(brdf.x + brdf.y, 1.0e-3) - 1.0);
        float3 r = reflect(-v, n);
        lighting.Diffuse += EnvironmentIrradiance(n) * albedo * ((1.0 - metallic) * ao);
        lighting.Specular += EnvironmentSpecular(r, roughness) * single * energy * SpecularOcclusion(nvEnvironment, specularVisibility, roughness);
        return;
    }

    float up = n.y * 0.5 + 0.5;
    float3 irradiance = lerp(GroundAmbient, SkyAmbient, up);
    float nv = saturate(dot(n, v));
    float3 fresnel = F_SchlickRough(f0, nv, roughness);
    float3 reflected = reflect(-v, n);
    float3 environment = lerp(GroundAmbient, SkyAmbient, reflected.y * 0.5 + 0.5);
    lighting.Diffuse += irradiance * albedo * ((1.0 - metallic) * ao);
    lighting.Specular += environment * fresnel * SpecularOcclusion(nv, specularVisibility, roughness);
}

#endif
