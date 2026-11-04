// Temporal anti-aliasing resolve (PristonTemporalAntiAliasing), on the pre-exposed HDR scene before bloom and exposure.
// After Karis, "High Quality Temporal Supersampling" (SIGGRAPH 2014), Salvi, "An Excursion in Temporal Supersampling"
// (GDC 2016) and Pedersen, "Temporal Reprojection Anti-Aliasing in INSIDE" (GDC 2016):
//  - current sample: Gaussian reconstruction around the unjittered pixel centre from the 3x3 neighbourhood (a third of a
//    pixel wide, so it adds little to the jitter's own blur; the final pass sharpens what the average still softens);
//    the variance box (YCoCg) comes from the whole neighbourhood, so jittered high-frequency texture (stone, gold trims)
//    keeps its history instead of following the jitter;
//  - motion: of the closest surface among the centre and its four diagonals (silhouettes keep their own motion).
//    Surfaces write their motion (Passes/Surface.fx: skinned actors from their previous bone palette, wind and water from
//    the previous animation time, everything from the previous view-projection); the sky marker reprojects the view
//    direction with the camera rotation;
//  - history: Catmull-Rom with five bilinear taps (one tap below 0.05 px of motion), clipped toward the variance box,
//    blended in the reversible tonemapped space c / (1 + Y) so HDR highlights do not dominate; the current weight grows
//    with the speed to limit the blur of resampling;
//  - disocclusion: the history keeps the view depth in alpha; a history depth outside the neighbourhood's depth range
//    (carried to the previous frame by the surface's own depth ratio) is rejected.
// The motion target carries (uv motion, previous view depth, view depth): one fetch gives a neighbour's depth and motion.
// Outputs: SV_Target0 the new history (compressed colour, depth), SV_Target1 the resolved pre-exposed HDR.
#include "../Core/Common.hlsli"
#include "../Core/Fullscreen.hlsli"

cbuffer PostTemporalConstants : register(b0)
{
    float4 TexelSize;                   // xy 1 / size, zw size (px)
    float4 JitterParams;                // xy this frame's jitter (px, x right, y down), z reconstruction sharpness, w history valid
    float4x4 CurrentDirectionToWorld;   // clip (x, y, 1, 1) -> view direction: the unjittered rotation-only inverse
    float4x4 PreviousDirectionToClip;   // view direction (w = 0) -> previous unjittered clip
    float4 BlendParams;                 // x current weight at rest, y when moving, z speed for y (px), w variance gamma
    float4 HistoryParams;               // x pre-exposure ratio (now / previous), y relative depth tolerance
};

Texture2D<float4> Scene : register(t0);
Texture2D<float4> Motion : register(t1);
Texture2D<float4> History : register(t3);
SamplerState SceneSampler : register(s0) = sampler_state
{
    Texture = (Scene); MinFilter = Point; MagFilter = Point; MipFilter = Point; AddressU = Clamp; AddressV = Clamp;
};
SamplerState MotionSampler : register(s1) = sampler_state
{
    Texture = (Motion); MinFilter = Point; MagFilter = Point; MipFilter = Point; AddressU = Clamp; AddressV = Clamp;
};
SamplerState HistorySampler : register(s3) = sampler_state
{
    Texture = (History); MinFilter = Linear; MagFilter = Linear; MipFilter = Point; AddressU = Clamp; AddressV = Clamp;
};

static const float FoliageWeight = 0.3;
static const float FoliageGamma = 0.9;
static const float2 Diagonals[4] = { float2(-1, -1), float2(1, -1), float2(-1, 1), float2(1, 1) };
static const float2 Neighbourhood[9] =
{
    float2(0, 0), float2(-1, 0), float2(1, 0), float2(0, -1), float2(0, 1),
    float2(-1, -1), float2(1, -1), float2(-1, 1), float2(1, 1)
};

float3 Compress(float3 c)
{
    return c / (1.0 + Luminance(c));
}

float3 Expand(float3 c)
{
    return c / max(1.0 - Luminance(c), 1.0e-4);
}

float3 RgbToYCoCg(float3 c)
{
    return float3(dot(c, float3(0.25, 0.5, 0.25)), dot(c, float3(0.5, 0.0, -0.5)), dot(c, float3(-0.25, 0.5, -0.25)));
}

float3 YCoCgToRgb(float3 c)
{
    return float3(c.x + c.y - c.z, c.x + c.z, c.x - c.y - c.z);
}

float3 SceneAt(float2 uv)
{
    return clamp(Scene.SampleLevel(SceneSampler, uv, 0).rgb, 0.0, HdrCap);
}

// w: the view depth, negative on foliage (Passes/Surface.fx MotionOutput); returned positive.
float4 MotionAt(float2 uv, out bool foliage)
{
    float4 m = Motion.SampleLevel(MotionSampler, uv, 0);
    foliage = m.w < 0.0;
    m.w = min(abs(m.w), SkyDepth);
    return m;
}

float4 HistoryAt(float2 uv)
{
    return History.SampleLevel(HistorySampler, uv, 0);
}

// Catmull-Rom with five bilinear taps (the four corner taps of the nine-tap form dropped; Jimenez 2016).
float4 SampleHistory(float2 uv)
{
    float2 samplePos = uv * TexelSize.zw;
    float2 texPos1 = floor(samplePos - 0.5) + 0.5;
    float2 f = samplePos - texPos1;
    float2 w0 = f * (-0.5 + f * (1.0 - 0.5 * f));
    float2 w1 = 1.0 + f * f * (-2.5 + 1.5 * f);
    float2 w2 = f * (0.5 + f * (2.0 - 1.5 * f));
    float2 w3 = f * f * (-0.5 + 0.5 * f);
    float2 w12 = w1 + w2;
    float2 texPos0 = (texPos1 - 1.0) * TexelSize.xy;
    float2 texPos3 = (texPos1 + 2.0) * TexelSize.xy;
    float2 texPos12 = (texPos1 + w2 / w12) * TexelSize.xy;
    float4 centre = HistoryAt(texPos12);
    float3 result = centre.rgb * (w12.x * w12.y);
    result += HistoryAt(float2(texPos12.x, texPos0.y)).rgb * (w12.x * w0.y);
    result += HistoryAt(float2(texPos0.x, texPos12.y)).rgb * (w0.x * w12.y);
    result += HistoryAt(float2(texPos3.x, texPos12.y)).rgb * (w3.x * w12.y);
    result += HistoryAt(float2(texPos12.x, texPos3.y)).rgb * (w12.x * w3.y);
    float weight = w12.x * w12.y + w12.x * w0.y + w0.x * w12.y + w3.x * w12.y + w12.x * w3.y;
    return float4(max(result / weight, 0.0), centre.a);
}

float3 ClipToBox(float3 history, float3 boxMin, float3 boxMax)
{
    float3 centre = 0.5 * (boxMax + boxMin);
    float3 extent = 0.5 * (boxMax - boxMin) + 1.0e-5;
    float3 offset = history - centre;
    float3 units = abs(offset / extent);
    float m = max(units.x, max(units.y, units.z));
    return m > 1.0 ? centre + offset / m : history;
}

// Where the view direction through uv was in the previous frame (the sky, infinitely far).
float2 SkyPreviousUv(float2 uv)
{
    float2 ndc = float2(uv.x * 2.0 - 1.0, 1.0 - uv.y * 2.0);
    float4 direction = mul(float4(ndc, 1.0, 1.0), CurrentDirectionToWorld);
    float4 clip = mul(float4(direction.xyz / direction.w, 0.0), PreviousDirectionToClip);
    return clip.w > 1.0e-6 ? clip.xy / clip.w * float2(0.5, -0.5) + 0.5 : float2(-1.0, -1.0);
}

struct ResolveOut
{
    float4 History : SV_Target0;
    float4 Resolved : SV_Target1;
};

ResolveOut PS_Resolve(FullscreenOut i)
{
    float2 uv = i.Uv;
    float2 texel = TexelSize.xy;

    // 1. Reconstruction and variance box.
    float2 pixel = uv * TexelSize.zw;
    float2 centreTexel = floor(pixel);
    float2 centreUv = (centreTexel + 0.5) * texel;
    float3 filtered = 0.0;
    float weightSum = 0.0;
    float3 m1 = 0.0;
    float3 m2 = 0.0;
    float3 lo = 1.0e9;
    float3 hi = -1.0e9;
    [unroll]
    for (uint k = 0; k < 9; k++)
    {
        float3 raw = SceneAt(centreUv + Neighbourhood[k] * texel);
        float3 ycc = RgbToYCoCg(Compress(raw));
        m1 += ycc;
        m2 += ycc * ycc;
        lo = min(lo, ycc);
        hi = max(hi, ycc);
        float2 d = centreTexel + 0.5 + Neighbourhood[k] - JitterParams.xy - pixel;
        float w = exp(-JitterParams.z * dot(d, d));
        filtered += raw * w;
        weightSum += w;
    }

    filtered = Compress(filtered / weightSum);
    float3 mean = m1 / 9.0;
    float3 sigma = sqrt(max(m2 / 9.0 - mean * mean, 0.0));

    // 2. The closest surface of the centre and its diagonals, and the depth range around the pixel.
    bool foliage;
    float4 motion = MotionAt(uv, foliage);
    float depth = motion.w;
    float closest = depth;
    float nearest = depth;
    float farthest = depth;
    [unroll]
    for (uint n = 0; n < 4; n++)
    {
        bool neighbourFoliage;
        float4 m = MotionAt(uv + Diagonals[n] * texel, neighbourFoliage);
        foliage = foliage || neighbourFoliage;
        nearest = min(nearest, m.w);
        farthest = max(farthest, m.w);
        if (m.w < closest)
        {
            closest = m.w;
            motion = m;
        }
    }

    bool sky = motion.x >= SkyMotionThreshold;
    float2 previousUv = sky ? SkyPreviousUv(uv) : uv - motion.xy;

    // 3. History.
    float speed = length((uv - previousUv) * TexelSize.zw);
    float4 history = speed < 0.05 ? HistoryAt(previousUv) : SampleHistory(previousUv);
    float3 historyColour = Compress(Expand(history.rgb) * HistoryParams.x);
    float currentWeight = lerp(BlendParams.x, BlendParams.y, saturate(speed / BlendParams.z));
    float gamma = BlendParams.w;
    // Foliage: thin cut-outs swaying in the wind over what is behind them; a long history smears leaves into the gaps
    // (a ghosted, see-through canopy), so this frame weighs more and the box is tighter.
    if (foliage)
    {
        currentWeight = max(currentWeight, FoliageWeight);
        gamma = min(gamma, FoliageGamma);
    }
    float3 boxMin = max(lo, mean - gamma * sigma);
    float3 boxMax = min(hi, mean + gamma * sigma);
    float3 clipped = YCoCgToRgb(ClipToBox(RgbToYCoCg(historyColour), boxMin, boxMax));

    bool offScreen = any(previousUv != saturate(previousUv));
    // The neighbourhood's depth range carried to the previous frame by the closest surface's own depth change.
    float ratio = sky ? 1.0 : motion.z / max(closest, 1.0e-3);
    float tolerance = HistoryParams.y;
    bool disoccluded = !sky
        && (history.a < nearest * ratio * (1.0 - tolerance) || history.a > farthest * ratio * (1.0 + tolerance));
    bool reset = JitterParams.w < 0.5 || offScreen || disoccluded;
    float3 result = reset ? filtered : lerp(clipped, filtered, currentWeight);
    result = max(result, 0.0);
    result *= min(1.0, 0.999 / max(Luminance(result), 1.0e-6));

    ResolveOut o;
    o.History = float4(result, depth);
    o.Resolved = float4(Expand(result), 1.0);
    return o;
}

technique Resolve { pass P0 { VertexShader = compile vs_6_0 VS_Fullscreen(); PixelShader = compile ps_6_0 PS_Resolve(); } }
