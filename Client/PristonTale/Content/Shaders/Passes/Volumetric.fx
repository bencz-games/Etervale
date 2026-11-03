// Volumetric sunlight through height fog (PristonVolumetricLight), after the scene's surfaces:
//  - March (quarter resolution): along each view ray to the nearest surface of its 4x4 block (the pre-pass's linear
//    depth, at most the march distance), sixteen segments, each lit by the sun through the cascaded shadow map at one
//    point jittered per pixel and per frame (interleaved gradient noise), with a Henyey-Greenstein phase. A segment
//    scatters the light its fog takes out of the beam, T(start) - T(end), from the exact optical depth of the
//    exponential height fog, so the integral is exact wherever the sun is unshadowed and the noise is the shadows' only;
//  - Accumulate (quarter resolution): the march blended into its reprojected history (HistoryWeight per frame), the
//    history clamped to the march's 3x3 neighbourhood and dropped where its depth disagrees (disocclusion), so the
//    jitter of successive frames integrates to a smooth estimate with or without the temporal anti-aliasing;
//  - Composite: the accumulated in-scatter upsampled by a joint bilateral filter (4x4 texels, Gaussian in distance,
//    steep in depth: at an edge the texels of the pixel's own surface win) and the fog's transmittance in front of the
//    pixel; the blend multiplies the scene by the transmittance and adds the in-scatter. The sky dome already holds the
//    air's own haze (SkyHaze.x of the medium): in front of it that share only darkens it where shadowed (the shafts),
//    and a mist the air lacks (the rest of the medium) veils and dims it like any surface, so a misty sky and what has
//    no depth before it (water) take the haze the surfaces do.
// The distance fog of the lit passes covers the medium beyond the march distance (its start distance).
#include "../Core/Common.hlsli"
#include "../Core/Fullscreen.hlsli"

#define MAX_CASCADES 4

cbuffer VolumetricConstants : register(b0)
{
    float4 VolumeSize;          // xy quarter-resolution size (px), zw full-resolution size (px)
    float4 VolumeProjection;    // x tan(half fov x), y tan(half fov y), z march distance (world), w frame noise offset
    float3 CameraRight;
    float FogDensity;           // extinction per world unit at the reference height
    float3 CameraUp;
    float FogFalloff;           // 1 / scale height (per world unit)
    float3 CameraForward;
    float FogReferenceHeight;   // world height where the density is FogDensity
    float3 CameraPosition;
    float PhaseG;               // Henyey-Greenstein anisotropy
    float3 SunDirection;        // toward the sun
    float CascadeCount;
    float3 SunRadiance;         // pre-exposed radiance of a white Lambertian surface facing the sun
    float HistoryWeight;        // share of this frame's march in the accumulation; 1 = no history
    float4x4 CascadeViewProjection[MAX_CASCADES];
    float4 CascadeAtlas[MAX_CASCADES];
    float4 CascadeSplits;
    float4x4 PreviousViewProjection;
    float4 SkyHaze;             // x the share of the medium the sky dome already holds; the rest is a mist it lacks
};

Texture2D<float4> DepthMap : register(t0);
Texture2D<float4> ShadowMap : register(t1);
Texture2D<float4> Inscatter : register(t2);
Texture2D<float4> History : register(t3);
SamplerState DepthSampler : register(s0) = sampler_state
{
    Texture = (DepthMap); MinFilter = Point; MagFilter = Point; MipFilter = Point; AddressU = Clamp; AddressV = Clamp;
};
SamplerState ShadowSampler : register(s1) = sampler_state
{
    Texture = (ShadowMap); MinFilter = Point; MagFilter = Point; MipFilter = Point; AddressU = Clamp; AddressV = Clamp;
};
SamplerState InscatterSampler : register(s2) = sampler_state
{
    Texture = (Inscatter); MinFilter = Point; MagFilter = Point; MipFilter = Point; AddressU = Clamp; AddressV = Clamp;
};
SamplerState HistorySampler : register(s3) = sampler_state
{
    Texture = (History); MinFilter = Linear; MagFilter = Linear; MipFilter = Point; AddressU = Clamp; AddressV = Clamp;
};

static const uint Steps = 16;

float3 ViewRay(float2 uv)
{
    return CameraRay(uv, VolumeProjection.xy, CameraRight, CameraUp, CameraForward);
}

// Optical depth from the camera to distance t along a unit direction through a density
// FogDensity * exp(-(height - FogReferenceHeight) * FogFalloff): the integral of the exponential along the ray.
float OpticalDepth(float3 direction, float t)
{
    float k = direction.y * FogFalloff;
    float x = t * k;
    float along = abs(x) < 1.0e-3 ? t * (1.0 - 0.5 * x) : (1.0 - exp(-x)) / k;
    return FogDensity * exp(min(-(CameraPosition.y - FogReferenceHeight) * FogFalloff, 30.0)) * along;
}

// The distance a view ray marches: to the surface (view depth to distance along the ray), at most the march distance.
float MarchDistance(float depth, float rayLength)
{
    return min(depth >= SkyDepthThreshold ? VolumeProjection.z : depth * rayLength, VolumeProjection.z);
}

float SunVisibility(float3 position, float viewDepth)
{
    uint count = clamp((uint)CascadeCount, 1u, (uint)MAX_CASCADES);
    if (viewDepth >= CascadeSplits[count - 1])
    {
        return 1.0;
    }

    uint c = 0;
    [unroll]
    for (uint k = 0; k < MAX_CASCADES - 1; k++)
    {
        c += (k + 1 < count && viewDepth >= CascadeSplits[k]) ? 1 : 0;
    }

    float4 light = mul(float4(position, 1.0), CascadeViewProjection[c]);
    float3 projected = light.xyz / light.w;
    float2 local = saturate(float2(projected.x, -projected.y) * 0.5 + 0.5);
    float4 tile = CascadeAtlas[c];
    float stored = ShadowMap.SampleLevel(ShadowSampler, tile.xy + local * tile.zw, 0).r;
    return saturate(projected.z) > stored + 1.0e-4 ? 0.0 : 1.0;
}

float4 PS_March(FullscreenOut i) : SV_Target0
{
    float2 pixel = floor(i.Uv * VolumeSize.xy);
    float2 uv = (pixel + 0.5) / VolumeSize.xy;
    // The nearest of four depths in the 4x4 block: thin foreground (actors, branches) keeps texels of its own, so the
    // upsampling never has to paint it with the fog behind it.
    float2 quarter = 1.0 / VolumeSize.zw;
    float depth = DepthMap.SampleLevel(DepthSampler, uv + quarter * float2(-1.0, -1.0), 0).r;
    depth = min(depth, DepthMap.SampleLevel(DepthSampler, uv + quarter * float2(1.0, -1.0), 0).r);
    depth = min(depth, DepthMap.SampleLevel(DepthSampler, uv + quarter * float2(-1.0, 1.0), 0).r);
    depth = min(depth, DepthMap.SampleLevel(DepthSampler, uv + quarter * float2(1.0, 1.0), 0).r);
    float3 ray = ViewRay(uv);
    float rayLength = length(ray);
    float3 direction = ray / rayLength;
    float distance = MarchDistance(depth, rayLength);
    float2 q = pixel + VolumeProjection.w * 5.588238;
    float jitter = InterleavedGradientNoise(q);
    float cosTheta = dot(direction, SunDirection);
    float phase = HenyeyGreenstein(cosTheta, PhaseG);
    float stepLength = distance / Steps;
    float scattered = 0.0;
    float entering = 1.0;
    [loop]
    for (uint k = 0; k < Steps; k++)
    {
        float leaving = exp(-OpticalDepth(direction, (k + 1) * stepLength));
        float t = (k + jitter) * stepLength;
        scattered += SunVisibility(CameraPosition + direction * t, t / rayLength) * (entering - leaving);
        entering = leaving;
    }

    // The sky dome already holds the light its share of the medium scatters unshadowed: that much is taken out, so the
    // shadows darken it (crepuscular rays) and only a mist's extra haze adds to it.
    if (depth >= SkyDepthThreshold)
    {
        scattered -= 1.0 - pow(entering, SkyHaze.x);
    }

    // The sun's radiance is that of a white surface facing it (E / pi): the in-scattered radiance is E * phase per unit
    // of scattered share (single scattering, albedo 1).
    return float4(clamp(SunRadiance * PI * phase * scattered, -HdrCap, HdrCap), depth);
}

float4 PS_Accumulate(FullscreenOut i) : SV_Target0
{
    float2 texel = 1.0 / VolumeSize.xy;
    float2 uv = (floor(i.Uv * VolumeSize.xy) + 0.5) * texel;
    float4 current = Inscatter.SampleLevel(InscatterSampler, uv, 0);
    [branch]
    if (HistoryWeight >= 1.0)
    {
        return current;
    }

    float3 lo = current.rgb;
    float3 hi = current.rgb;
    [unroll]
    for (int y = -1; y <= 1; y++)
    {
        [unroll]
        for (int x = -1; x <= 1; x++)
        {
            float3 s = Inscatter.SampleLevel(InscatterSampler, uv + float2(x, y) * texel, 0).rgb;
            lo = min(lo, s);
            hi = max(hi, s);
        }
    }

    float viewDepth = min(current.w, SkyDepthThreshold);
    float3 position = CameraPosition + ViewRay(uv) * viewDepth;
    float4 previousClip = mul(float4(position, 1.0), PreviousViewProjection);
    float2 previousUv = float2(previousClip.x, -previousClip.y) / max(previousClip.w, 1.0e-4) * 0.5 + 0.5;
    [branch]
    if (previousClip.w <= 1.0 || any(previousUv != saturate(previousUv)))
    {
        return current;
    }

    float4 history = History.SampleLevel(HistorySampler, previousUv, 0);
    float expected = min(previousClip.w, SkyDepthThreshold);
    if (abs(min(history.w, SkyDepthThreshold) - expected) > 0.05 * expected + 10.0)
    {
        return current;
    }

    return float4(lerp(clamp(history.rgb, lo, hi), current.rgb, HistoryWeight), current.w);
}

float4 PS_Composite(FullscreenOut i) : SV_Target0
{
    float depth = DepthMap.SampleLevel(DepthSampler, i.Uv, 0).r;
    float2 lowPixel = i.Uv * VolumeSize.xy - 0.5;
    float2 base = floor(lowPixel) - 1.0;
    float3 sum = 0.0;
    float weights = 0.0;
    [unroll]
    for (int y = 0; y < 4; y++)
    {
        [unroll]
        for (int x = 0; x < 4; x++)
        {
            float2 p = base + float2(x, y);
            float2 offset = p - lowPixel;
            float4 s = Inscatter.SampleLevel(InscatterSampler, (p + 0.5) / VolumeSize.xy, 0);
            float spatial = exp(-0.5 * dot(offset, offset)) + 1.0e-3;
            // Depth weight steep enough to pick the nearest-depth texels at an edge (no fog bled onto a silhouette).
            float relative = abs(min(s.w, SkyDepthThreshold) - min(depth, SkyDepthThreshold)) / max(min(depth, SkyDepthThreshold), 1.0);
            float w = spatial / (1.0e-4 + relative * relative);
            sum += s.rgb * w;
            weights += w;
        }
    }

    float3 ray = ViewRay(i.Uv);
    float rayLength = length(ray);
    float opticalDepth = OpticalDepth(ray / rayLength, MarchDistance(depth, rayLength));
    float transmittance = exp(-opticalDepth * (depth >= SkyDepthThreshold ? 1.0 - SkyHaze.x : 1.0));
    return float4(sum / max(weights, 1.0e-6), transmittance);
}

technique March { pass P0 { VertexShader = compile vs_6_0 VS_Fullscreen(); PixelShader = compile ps_6_0 PS_March(); } }
technique Accumulate { pass P0 { VertexShader = compile vs_6_0 VS_Fullscreen(); PixelShader = compile ps_6_0 PS_Accumulate(); } }
technique Composite { pass P0 { VertexShader = compile vs_6_0 VS_Fullscreen(); PixelShader = compile ps_6_0 PS_Composite(); } }
