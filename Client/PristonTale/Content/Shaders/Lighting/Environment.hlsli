// The prefiltered environment atlas (Lighting/PristonEnvironmentLighting.cs): one roughness level per square, level k
// (base / 2^k) square at x = 2 base (1 - 2^-k), sampled at roughness k / (levels - 1). info: x 1 = bound, y base size,
// z atlas width, w levels.
#ifndef LIGHTING_ENVIRONMENT_HLSLI
#define LIGHTING_ENVIRONMENT_HLSLI

#include "Octahedral.hlsli"

float3 EnvironmentAtlasLevel(Texture2D<float4> atlas, SamplerState atlasSampler, float4 info, float2 octahedral, float level)
{
    float base = info.y;
    float size = base * exp2(-level);
    float offset = 2.0 * base - 2.0 * size;
    float2 pixel = clamp(octahedral * size, 0.5, size - 0.5);
    return atlas.SampleLevel(atlasSampler, (float2(offset, 0.0) + pixel) / float2(info.z, base), 0).rgb;
}

// The atlas's radiance toward r for a perceptual roughness, between its two nearest levels, in the atlas's own scale.
float3 EnvironmentAtlasRadiance(Texture2D<float4> atlas, SamplerState atlasSampler, float4 info, float3 r, float roughness)
{
    float level = saturate(roughness) * (info.w - 1.0);
    float level0 = floor(level);
    float level1 = min(level0 + 1.0, info.w - 1.0);
    float2 octahedral = OctahedralEncode(r);
    return lerp(EnvironmentAtlasLevel(atlas, atlasSampler, info, octahedral, level0),
        EnvironmentAtlasLevel(atlas, atlasSampler, info, octahedral, level1), level - level0);
}

#endif
