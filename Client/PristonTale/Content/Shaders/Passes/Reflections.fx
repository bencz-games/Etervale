// Screen-space reflections (PristonScreenReflections) of smooth surfaces (water, ice, polished stone), after the opaque
// passes and before the blended ones:
//  - Copy: the opaque scene into a copy the rays read;
//  - Reflect: a ray along the reflected view direction, marched in screen space through the pre-pass's linear depth
//    (24 jittered steps, then a binary refinement), replaces the image-based reflection the lit pass added with the
//    scene it hits: the pass adds Fresnel * confidence * (hit - environment), so a miss keeps the sky's reflection.
// The pre-pass's normal target carries the smoothness (1 - roughness) in alpha.
#include "../Core/Common.hlsli"
#include "../Core/Fullscreen.hlsli"
#include "../Lighting/Environment.hlsli"

cbuffer ReflectionConstants : register(b0)
{
    float4 ReflectSize;        // xy target size (px), zw 1 / size
    float4 ReflectProjection;  // x tan(half fov x), y tan(half fov y), z max ray length (world), w frame noise offset
    float3 CameraRight;
    float ReflectPadding0;
    float3 CameraUp;
    float ReflectPadding1;
    float3 CameraForward;
    float ReflectPadding2;
    float4x4 WorldToClip;      // camera-relative world -> clip
    float4 EnvironmentInfo;    // x 1 = environment bound, y base size, z atlas width, w levels
};

Texture2D<float4> SceneCopy : register(t0);
Texture2D<float4> DepthMap : register(t1);
Texture2D<float4> NormalMap : register(t2);
Texture2D<float4> EnvironmentMap : register(t3);
StructuredBuffer<float4> EnvironmentSh : register(t4);
SamplerState SceneSampler : register(s0) = sampler_state
{
    Texture = (SceneCopy); MinFilter = Linear; MagFilter = Linear; MipFilter = Point; AddressU = Clamp; AddressV = Clamp;
};
SamplerState DepthSampler : register(s1) = sampler_state
{
    Texture = (DepthMap); MinFilter = Point; MagFilter = Point; MipFilter = Point; AddressU = Clamp; AddressV = Clamp;
};
SamplerState NormalSampler : register(s2) = sampler_state
{
    Texture = (NormalMap); MinFilter = Point; MagFilter = Point; MipFilter = Point; AddressU = Clamp; AddressV = Clamp;
};
SamplerState EnvironmentSampler : register(s3) = sampler_state
{
    Texture = (EnvironmentMap); MinFilter = Linear; MagFilter = Linear; MipFilter = Point; AddressU = Clamp; AddressV = Clamp;
};

static const float MinSmoothness = 0.55;
static const uint Steps = 24;
static const uint RefineSteps = 4;

float4 PS_Copy(FullscreenOut i) : SV_Target0
{
    return float4(SceneCopy.SampleLevel(SceneSampler, i.Uv, 0).rgb, 1.0);
}

float3 ViewRay(float2 uv)
{
    return CameraRay(uv, ReflectProjection.xy, CameraRight, CameraUp, CameraForward);
}

float DepthAt(float2 uv)
{
    return DepthMap.SampleLevel(DepthSampler, uv, 0).r;
}

// The reflection the lit pass already added (Lighting/Shading.hlsli EnvironmentSpecular).
float3 EnvironmentReflection(float3 r, float roughness)
{
    if (EnvironmentInfo.x < 0.5)
    {
        return 0.0;
    }

    return EnvironmentAtlasRadiance(EnvironmentMap, EnvironmentSampler, EnvironmentInfo, r, roughness) * EnvironmentSh[9].x;
}

float2 ClipToUv(float4 clip)
{
    return float2(clip.x, -clip.y) / clip.w * 0.5 + 0.5;
}

float4 PS_Reflect(FullscreenOut i) : SV_Target0
{
    float4 normalSample = NormalMap.SampleLevel(NormalSampler, i.Uv, 0);
    float smoothness = normalSample.a;
    float depth = DepthAt(i.Uv);
    if (smoothness < MinSmoothness || depth > SkyDepthThreshold)
    {
        discard;
    }

    float3 n = SafeNormalize(normalSample.xyz * 2.0 - 1.0);
    float3 p = ViewRay(i.Uv) * depth;
    float3 v = SafeNormalize(-p);
    float3 r = reflect(-v, n);
    float roughness = 1.0 - smoothness;
    float nv = saturate(dot(n, v));
    float fresnel = 0.04 + 0.96 * pow(1.0 - nv, 5.0) * (1.0 - roughness);
    float2 q = i.Position.xy + ReflectProjection.w * 5.588238;
    float jitter = InterleavedGradientNoise(q);

    float3 start = p + n * (0.003 * depth);
    float rayLength = ReflectProjection.z;
    float4 clipStart = mul(float4(start, 1.0), WorldToClip);
    float4 clipEnd = mul(float4(start + r * rayLength, 1.0), WorldToClip);
    // Keep the ray in front of the camera.
    if (clipEnd.w < 1.0)
    {
        float t = (clipStart.w - 1.0) / max(clipStart.w - clipEnd.w, 1.0e-4);
        clipEnd = lerp(clipStart, clipEnd, t);
    }

    float thickness = max(25.0, 0.03 * depth);
    float hitT = -1.0;
    float previousT = 0.0;
    [loop]
    for (uint k = 1; k <= Steps; k++)
    {
        float t = (k - 1.0 + jitter) / Steps;
        t *= t;
        float4 clip = lerp(clipStart, clipEnd, t);
        float2 uv = ClipToUv(clip);
        if (any(uv != saturate(uv)))
        {
            break;
        }

        float difference = clip.w - DepthAt(uv);
        if (difference > 0.0 && difference < thickness)
        {
            hitT = t;
            break;
        }

        previousT = t;
    }

    if (hitT < 0.0)
    {
        discard;
    }

    float low = previousT;
    float high = hitT;
    [unroll]
    for (uint b = 0; b < RefineSteps; b++)
    {
        float middle = 0.5 * (low + high);
        float4 clip = lerp(clipStart, clipEnd, middle);
        if (clip.w - DepthAt(ClipToUv(clip)) > 0.0)
        {
            high = middle;
        }
        else
        {
            low = middle;
        }
    }

    float2 hitUv = ClipToUv(lerp(clipStart, clipEnd, high));
    float2 edge = min(hitUv, 1.0 - hitUv);
    float confidence = saturate(min(edge.x, edge.y) * 10.0) * saturate((1.0 - high) * 4.0)
        * saturate((smoothness - MinSmoothness) / 0.2);
    float3 hit = SceneCopy.SampleLevel(SceneSampler, hitUv, 0).rgb;
    float3 environment = EnvironmentReflection(r, roughness);
    return float4(clamp((hit - environment) * (fresnel * confidence), -HdrCap, HdrCap), 0.0);
}

technique Copy { pass P0 { VertexShader = compile vs_6_0 VS_Fullscreen(); PixelShader = compile ps_6_0 PS_Copy(); } }
technique Reflect { pass P0 { VertexShader = compile vs_6_0 VS_Fullscreen(); PixelShader = compile ps_6_0 PS_Reflect(); } }
