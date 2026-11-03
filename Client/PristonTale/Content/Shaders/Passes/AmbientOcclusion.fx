// Screen-space occlusion at half resolution from the depth pre-pass (PristonScreenOcclusion):
//  - Occlusion: ground-truth ambient occlusion (Jimenez et al. 2016, "Practical Real-Time Strategies for Accurate
//    Indirect Occlusion"; the structure of Intel's XeGTAO): two slices with three horizon steps each way per pixel,
//    rotated and jittered by a 4x4 interleaved pattern; and contact shadows: a short ray march toward the sun through
//    the depth buffer, for the small-scale shadows the cascades are too coarse to resolve (feet on the ground, folds);
//  - Blur: a 4x4 depth-aware box, which covers each cell of the interleaved pattern exactly once.
// Output (RGBA16F): x ambient visibility, y sun visibility of the contact shadows, z view depth. The lit passes upsample
// it with the depth (Lighting/Shading.hlsli ScreenOcclusion).
#include "../Core/Common.hlsli"
#include "../Core/Fullscreen.hlsli"

cbuffer AmbientOcclusionConstants : register(b0)
{
    float4 AoSize;            // xy size of the half-resolution target (px), zw 1 / size
    float4 AoProjection;      // x tan(half fov x), y tan(half fov y), z pixels per world unit at depth 1 (half res)
    float4 AoRadius;          // x world radius, y falloff share of the radius, z max radius (px), w 1 = ambient occlusion on
    float4 ContactParams;     // x march length (world), y thickness (world), z 1 = contact shadows on
    float3 CameraRight;       // world basis of the camera
    float AoPadding0;
    float3 CameraUp;
    float AoPadding1;
    float3 CameraForward;
    float AoPadding2;
    float3 SunDirectionWorld; // toward the sun
    float AoPadding3;
    float4x4 WorldToClip;     // camera-relative world -> clip (the camera at the origin)
};

Texture2D<float4> DepthMap : register(t0);    // R32F view depth (60000 or more: sky)
Texture2D<float4> NormalMap : register(t1);   // RGBA8 world normal * 0.5 + 0.5
Texture2D<float4> AoInput : register(t2);     // the Occlusion pass, for the blur
SamplerState DepthSampler : register(s0) = sampler_state
{
    Texture = (DepthMap); MinFilter = Point; MagFilter = Point; MipFilter = Point; AddressU = Clamp; AddressV = Clamp;
};
SamplerState NormalSampler : register(s1) = sampler_state
{
    Texture = (NormalMap); MinFilter = Point; MagFilter = Point; MipFilter = Point; AddressU = Clamp; AddressV = Clamp;
};
SamplerState AoInputSampler : register(s2) = sampler_state
{
    Texture = (AoInput); MinFilter = Point; MagFilter = Point; MipFilter = Point; AddressU = Clamp; AddressV = Clamp;
};

static const uint SliceCount = 2;
static const uint StepCount = 3;
static const uint ContactSteps = 10;

float3 ViewRay(float2 uv)
{
    return CameraRay(uv, AoProjection.xy, CameraRight, CameraUp, CameraForward);
}

float DepthAt(float2 uv)
{
    return DepthMap.SampleLevel(DepthSampler, uv, 0).r;
}

// Ordered 4x4 Bayer value 0..15.
uint Bayer4(uint2 p)
{
    uint low = 2 * ((p.x ^ p.y) & 1) + (p.y & 1);
    uint high = 2 * (((p.x >> 1) ^ (p.y >> 1)) & 1) + ((p.y >> 1) & 1);
    return low * 4 + high;
}

float Gtao(float2 pixel, float2 uv, float3 p, float3 v, float3 n, float depth)
{
    float radiusPixels = AoRadius.x * AoProjection.z / depth;
    if (radiusPixels < 1.0)
    {
        return 1.0;
    }

    radiusPixels = min(radiusPixels, AoRadius.z);
    float falloffRange = AoRadius.y * AoRadius.x;
    float falloffMul = -1.0 / falloffRange;
    float falloffAdd = (AoRadius.x - falloffRange) / falloffRange + 1.0;
    uint2 cell = (uint2)pixel & 3;
    float sliceJitter = (Bayer4(cell) + 0.5) / 16.0;
    float stepJitter = (Bayer4(uint2(cell.y, cell.x * 3)) + 0.5) / 16.0;
    float visibility = 0.0;
    [unroll]
    for (uint slice = 0; slice < SliceCount; slice++)
    {
        float phi = (slice + sliceJitter) * PI / SliceCount;
        float2 omega = float2(cos(phi), sin(phi));
        float2 stepUv = float2(omega.x, -omega.y);
        float3 direction = CameraRight * omega.x + CameraUp * omega.y;
        float3 orthoDirection = direction - dot(direction, v) * v;
        float3 axis = SafeNormalize(cross(orthoDirection, v));
        float3 projectedNormal = n - axis * dot(n, axis);
        float projectedLength = length(projectedNormal);
        float signN = dot(orthoDirection, projectedNormal) >= 0.0 ? 1.0 : -1.0;
        float cosN = saturate(dot(projectedNormal, v) / max(projectedLength, 1.0e-4));
        float angleN = signN * acos(cosN);
        float low0 = cos(angleN + PI * 0.5);
        float low1 = cos(angleN - PI * 0.5);
        float horizon0 = low0;
        float horizon1 = low1;
        [unroll]
        for (uint s = 0; s < StepCount; s++)
        {
            float t = (s + stepJitter) / StepCount;
            float offset = t * t * radiusPixels + 1.0;
            float2 uv0 = (floor(pixel + stepUv * offset) + 0.5) * AoSize.zw;
            float2 uv1 = (floor(pixel - stepUv * offset) + 0.5) * AoSize.zw;
            float3 d0 = ViewRay(uv0) * DepthAt(uv0) - p;
            float3 d1 = ViewRay(uv1) * DepthAt(uv1) - p;
            float l0 = max(length(d0), 1.0e-4);
            float l1 = max(length(d1), 1.0e-4);
            float c0 = dot(d0, v) / l0;
            float c1 = dot(d1, v) / l1;
            c0 = lerp(low0, c0, saturate(l0 * falloffMul + falloffAdd));
            c1 = lerp(low1, c1, saturate(l1 * falloffMul + falloffAdd));
            horizon0 = max(horizon0, c0);
            horizon1 = max(horizon1, c1);
        }

        // Cosine-weighted visibility of the arc between the two horizons, clamped to the hemisphere.
        float h0 = -acos(clamp(horizon1, -1.0, 1.0));
        float h1 = acos(clamp(horizon0, -1.0, 1.0));
        h0 = angleN + clamp(h0 - angleN, -PI * 0.5, PI * 0.5);
        h1 = angleN + clamp(h1 - angleN, -PI * 0.5, PI * 0.5);
        float sinN = sin(angleN);
        float arc0 = (cosN + 2.0 * h0 * sinN - cos(2.0 * h0 - angleN)) * 0.25;
        float arc1 = (cosN + 2.0 * h1 * sinN - cos(2.0 * h1 - angleN)) * 0.25;
        visibility += projectedLength * (arc0 + arc1);
    }

    return saturate(visibility / SliceCount);
}

// March from the surface toward the sun; a step behind the depth buffer, within the thickness, is in shadow. The
// shadow fades over the last quarter of the march so its end does not show.
float ContactShadow(float2 pixel, float3 p, float3 n)
{
    float3 toSun = SafeNormalize(SunDirectionWorld);
    if (dot(n, toSun) <= 0.0)
    {
        return 1.0;
    }

    // A fixed jitter per pixel, so the occlusion holds still where the temporal history is rejected (a turning camera,
    // moving actors).
    float jitter = InterleavedGradientNoise(pixel);
    float3 start = p + n * (0.002 * length(p));
    float3 stepWorld = toSun * (ContactParams.x / ContactSteps);
    float occlusion = 0.0;
    [loop]
    for (uint k = 0; k < ContactSteps; k++)
    {
        float t = k + jitter;
        float3 position = start + stepWorld * t;
        float4 clip = mul(float4(position, 1.0), WorldToClip);
        if (clip.w <= 0.0)
        {
            break;
        }

        float2 uv = float2(clip.x, -clip.y) / clip.w * 0.5 + 0.5;
        if (any(uv != saturate(uv)))
        {
            break;
        }

        float scene = DepthAt(uv);
        float difference = clip.w - scene;
        if (difference > 0.0 && difference < ContactParams.y)
        {
            occlusion = saturate((ContactSteps - t) / (ContactSteps * 0.25));
            break;
        }
    }

    return 1.0 - occlusion;
}

float4 PS_Occlusion(FullscreenOut i) : SV_Target0
{
    float2 pixel = floor(i.Uv * AoSize.xy);
    float2 uv = (pixel + 0.5) * AoSize.zw;
    float depth = DepthAt(uv);
    if (depth > SkyDepthThreshold)
    {
        return float4(1.0, 1.0, depth, 1.0);
    }

    float3 p = ViewRay(uv) * depth;
    float3 v = SafeNormalize(-p);
    // The prepass already writes the normal of the visible side (Materials/Material.hlsli OrientNormal); flipping it again
    // by the view would reverse smooth normals near silhouettes.
    float3 n = SafeNormalize(NormalMap.SampleLevel(NormalSampler, uv, 0).xyz * 2.0 - 1.0);
    float ao = AoRadius.w > 0.5 ? Gtao(pixel, uv, p, v, n, depth) : 1.0;
    float contact = ContactParams.z > 0.5 ? ContactShadow(pixel, p, n) : 1.0;
    return float4(ao, contact, depth, 1.0);
}

float4 PS_Blur(FullscreenOut i) : SV_Target0
{
    float2 pixel = floor(i.Uv * AoSize.xy);
    float4 centre = AoInput.SampleLevel(AoInputSampler, (pixel + 0.5) * AoSize.zw, 0);
    if (centre.z > SkyDepthThreshold)
    {
        return centre;
    }

    float2 sum = 0.0;
    float weight = 0.0;
    [unroll]
    for (int y = -2; y < 2; y++)
    {
        [unroll]
        for (int x = -2; x < 2; x++)
        {
            float4 s = AoInput.SampleLevel(AoInputSampler, (pixel + float2(x, y) + 0.5) * AoSize.zw, 0);
            float w = saturate(1.0 - abs(s.z - centre.z) / (0.05 * centre.z)) + 1.0e-4;
            sum += s.xy * w;
            weight += w;
        }
    }

    return float4(sum / weight, centre.z, 1.0);
}

technique Occlusion { pass P0 { VertexShader = compile vs_6_0 VS_Fullscreen(); PixelShader = compile ps_6_0 PS_Occlusion(); } }
technique Blur { pass P0 { VertexShader = compile vs_6_0 VS_Fullscreen(); PixelShader = compile ps_6_0 PS_Blur(); } }
