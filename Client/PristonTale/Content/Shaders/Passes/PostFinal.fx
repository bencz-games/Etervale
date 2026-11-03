// The display transform of the frame (PristonPostProcessor) and the anti-aliasing filters on the display image.
//  Tonemap: [contrast-adaptive sharpening] -> (scene + bloom) -> mesopic vision -> x the exposure multiplier (the
//           automatic exposure's State[0].y, Passes/PostExposure.fx, or the fixed multiplier) -> display transform
//           (Khronos PBR Neutral, AgX or ACES) -> exact sRGB -> colour grade (3D LUT) -> +-1 LSB triangular dither; luma
//           in alpha for FXAA.
//           The scene is pre-exposed radiance (Core/Frame.hlsli); the CPU reference of the display transforms is
//           PristonDisplayTransform.cs.
//  Fxaa:    FXAA 3.11-style edge search on the 8-bit image (SMAA: Passes/PostSmaa.fx).
// The UI is drawn after this pass, in sRGB.
#include "../Core/Common.hlsli"
#include "../Core/Color.hlsli"
#include "../Core/Fullscreen.hlsli"

cbuffer PostFinalConstants : register(b0)
{
    float4 SourceRect;       // the viewport's part of the scene: xy offset, zw size (uv units)
    float4 ScreenParams;     // xy scene size (px), z frame index (dither), w sharpness 0..1
    float4 ToneParams;       // x tonemapper (0 PBR Neutral, 1 AgX, 2 ACES), y grade strength, z bloom strength, w fixed multiplier (> 0)
    float4 DebugParams;      // x 1 = EV100 false colour, y 1 / pre-exposure
    float4 MesopicParams;    // xyz the hue of the scotopic grey (unit luminance)
};

Texture2D<float4> Scene : register(t0);
Texture2D<float4> Bloom : register(t1);
Texture2D<float4> Lut : register(t2);
Texture2D<float4> Ldr : register(t3);
StructuredBuffer<float4> ExposureState : register(t4);
SamplerState SceneSampler : register(s0) = sampler_state
{
    Texture = (Scene); MinFilter = Point; MagFilter = Point; MipFilter = Point; AddressU = Clamp; AddressV = Clamp;
};
SamplerState BloomSampler : register(s1) = sampler_state
{
    Texture = (Bloom); MinFilter = Linear; MagFilter = Linear; MipFilter = Point; AddressU = Clamp; AddressV = Clamp;
};
SamplerState LutSampler : register(s2) = sampler_state
{
    Texture = (Lut); MinFilter = Linear; MagFilter = Linear; MipFilter = Point; AddressU = Clamp; AddressV = Clamp;
};
SamplerState LdrSampler : register(s3) = sampler_state
{
    Texture = (Ldr); MinFilter = Linear; MagFilter = Linear; MipFilter = Point; AddressU = Clamp; AddressV = Clamp;
};

// ---- Display transforms -------------------------------------------------------------------------------------------
// Minimal AgX (Sobotka; fit by Wrensch 2023). The GLSL matrices are column-major: the same literals with mul(v, M).
static const float3x3 AgxIn = float3x3(
    0.842479062253094, 0.0423282422610123, 0.0423756549057051,
    0.0784335999999992, 0.878468636469772, 0.0784336,
    0.0792237451477643, 0.0791661274605434, 0.879142973793104);
static const float3x3 AgxOut = float3x3(
    1.19687900512017, -0.0528968517574562, -0.0529716355144438,
    -0.0980208811401368, 1.15190312990417, -0.0980434501171241,
    -0.0990297440797205, -0.0989611768448433, 1.15107367264116);

float3 AgxContrast(float3 x)
{
    float3 x2 = x * x;
    float3 x4 = x2 * x2;
    return 15.5 * x4 * x2 - 40.14 * x4 * x + 31.96 * x4 - 6.868 * x2 * x + 0.4298 * x2 + 0.1191 * x - 0.00232;
}

// Linear display value (the AgX display encoding is about a 2.2 gamma; decoding it lets the frame end with exact sRGB).
float3 Agx(float3 c)
{
    float3 v = mul(max(c, 1.0e-6), AgxIn);
    v = (clamp(log2(v), -12.47393, 4.026069) + 12.47393) / 16.499999;
    v = AgxContrast(v);
    v = saturate(mul(v, AgxOut));
    return pow(v, 2.2);
}

// ACES filmic, Hill's fit of the RRT and the sRGB ODT (2016), with mul(M, v) as published. The fit puts scene grey
// 0.18 near 0.1; AcesExposure lifts it to the other transforms' level.
static const float AcesExposure = 1.6;
static const float3x3 AcesIn = float3x3(
    0.59719, 0.35458, 0.04823,
    0.07600, 0.90834, 0.01566,
    0.02840, 0.13383, 0.83777);
static const float3x3 AcesOut = float3x3(
    1.60475, -0.53108, -0.07367,
    -0.10208, 1.10813, -0.00605,
    -0.00327, -0.07276, 1.07602);

float3 Aces(float3 c)
{
    float3 v = mul(AcesIn, max(c, 0.0) * AcesExposure);
    float3 a = v * (v + 0.0245786) - 0.000090537;
    float3 b = v * (0.983729 * v + 0.4329510) + 0.238081;
    return saturate(mul(AcesOut, a / b));
}

float3 DisplayTransform(float3 c)
{
    uint mode = (uint)(ToneParams.x + 0.5);
    if (mode == 1)
    {
        return Agx(c);
    }

    if (mode == 2)
    {
        return Aces(c);
    }

    return saturate(PbrNeutral(max(c, 0.0)));
}

// ---- Mesopic vision -----------------------------------------------------------------------------------------------
// Below a few cd/m^2 the rods take over: they see no colour and favour blue (the Purkinje shift). Each pixel blends
// toward its scotopic luminance (Larson, Rushmeier and Piatko 1997, normalised to 1 for D65 white) in a blue-shifted
// grey, by 0.04 / (0.04 + Y) of its absolute luminance Y in cd/m^2 (Krawczyk, Myszkowski and Seidel 2005). The grey takes
// the hue of the night light the game was authored with (PristonDayNightCycle.NightHue): the night's blue is seen here
// rather than lit, so the diffuse and the specular light of a surface keep the colour of the light that makes them.
// CPU reference: PristonDisplayTransform.Mesopic.
static const float ScotopicWhite = 2.5731;
static const float MesopicHalf = 0.04;

float3 MesopicVision(float3 c, float inversePreExposure, float3 tint)
{
    float x = dot(c, float3(0.4124, 0.3576, 0.1805));
    float y = Luminance(c);
    float z = dot(c, float3(0.0193, 0.1192, 0.9505));
    float scotopic = max(y * (1.33 * (1.0 + (y + z) / max(x, 1.0e-6)) - 1.68), 0.0) / ScotopicWhite;
    float rods = MesopicHalf / (MesopicHalf + y * inversePreExposure);
    return lerp(c, tint * scotopic, rods);
}

// ---- Colour grade -------------------------------------------------------------------------------------------------
// A 32^3 LUT stored as 32 slices of 32x32 side by side (1024x32), indexed and returning sRGB-encoded colour
// (PristonColorGrading).
static const float LutSize = 32.0;

float3 Grade(float3 encoded)
{
    float3 c = saturate(encoded);
    float slice = c.b * (LutSize - 1.0);
    float slice0 = floor(slice);
    float slice1 = min(slice0 + 1.0, LutSize - 1.0);
    float t = slice - slice0;
    float2 inner = c.rg * ((LutSize - 1.0) / LutSize) + 0.5 / LutSize;
    float2 uv0 = float2((slice0 + inner.x) / LutSize, inner.y);
    float2 uv1 = float2((slice1 + inner.x) / LutSize, inner.y);
    float3 graded = lerp(Lut.SampleLevel(LutSampler, uv0, 0).rgb, Lut.SampleLevel(LutSampler, uv1, 0).rgb, t);
    return lerp(encoded, graded, ToneParams.y);
}

// ---- Helpers ------------------------------------------------------------------------------------------------------
// Triangular +-1 LSB noise from two interleaved-gradient-noise samples (Jimenez 2014).
float3 Dither8(float3 c, float2 pixel)
{
    float2 p = pixel + ScreenParams.z * 5.588238;
    float n1 = InterleavedGradientNoise(p);
    float n2 = InterleavedGradientNoise(p + 17.0);
    return c + (n1 - n2) / 255.0;
}

// One hue per stop: blue (dark) -> green -> yellow -> red (bright), white from EV 16; a dark line at every whole stop.
float3 FalseColour(float ev)
{
    float t = saturate((ev + 4.0) / 20.0);
    float band = frac(ev) < 0.06 ? 0.0 : 1.0;
    float3 c = saturate(float3(abs(t * 6.0 - 3.0) - 1.0, 2.0 - abs(t * 6.0 - 2.0), 2.0 - abs(t * 6.0 - 4.0)));
    return (ev >= 16.0 ? 1.0 : c) * band;
}

float3 SceneAt(float2 uv)
{
    return clamp(Scene.SampleLevel(SceneSampler, uv, 0).rgb, 0.0, HdrCap);
}

// Contrast-adaptive sharpening (after AMD FidelityFX CAS, Lottes 2019) on the cross neighbourhood, per channel in a
// Reinhard space of the exposed image, so the adaptive weight sees display-like contrast and highlights cannot ring.
float3 SharpenedScene(float2 uv, float exposure)
{
    float3 c = SceneAt(uv);
    [branch]
    if (ScreenParams.w <= 0.0)
    {
        return c;
    }

    float2 t = 1.0 / ScreenParams.xy;
    float3 up = SceneAt(uv - float2(0, t.y)) * exposure;
    float3 left = SceneAt(uv - float2(t.x, 0)) * exposure;
    float3 right = SceneAt(uv + float2(t.x, 0)) * exposure;
    float3 down = SceneAt(uv + float2(0, t.y)) * exposure;
    float3 centre = c * exposure;
    up /= 1.0 + up;
    left /= 1.0 + left;
    right /= 1.0 + right;
    down /= 1.0 + down;
    centre /= 1.0 + centre;
    float3 lo = min(centre, min(min(up, left), min(right, down)));
    float3 hi = max(centre, max(max(up, left), max(right, down)));
    float3 amplitude = sqrt(saturate(min(lo, 1.0 - hi) / max(hi, 1.0e-5)));
    // CAS's peak weight runs from -1/8 to -1/5 over the sharpness; 0 turns the pass off above.
    float3 w = amplitude * lerp(-0.125, -0.2, saturate(ScreenParams.w));
    float3 r = clamp((centre + (up + left + right + down) * w) / (1.0 + 4.0 * w), 0.0, 0.999);
    return r / (1.0 - r) / max(exposure, 1.0e-6);
}

float4 PS_Tonemap(FullscreenOut i) : SV_Target0
{
    float exposure = ToneParams.w > 0.0 ? ToneParams.w : ExposureState[0].y;
    float2 uv = SourceRect.xy + i.Uv * SourceRect.zw;
    float3 scene = SharpenedScene(uv, exposure);
    float3 bloom = min(Bloom.SampleLevel(BloomSampler, uv, 0).rgb, HdrCap);
    float3 hdr = lerp(scene, bloom, ToneParams.z);
    float3 display;
    [branch]
    if (DebugParams.x > 0.5)
    {
        float luminance = Luminance(scene) * DebugParams.y;
        display = FalseColour(log2(max(luminance, 1.0e-6) * 100.0 / 12.5));
    }
    else
    {
        display = Grade(LinearToSrgb(DisplayTransform(MesopicVision(hdr, DebugParams.y, MesopicParams.xyz) * exposure)));
    }

    display = Dither8(display, i.Uv * ScreenParams.xy);
    return float4(display, dot(saturate(display), float3(0.299, 0.587, 0.114)));
}

// ---- FXAA ---------------------------------------------------------------------------------------------------------
// After Lottes, "FXAA 3.11" (NVIDIA 2011), with a short six-step edge search; luma in alpha.
static const float EdgeThresholdMin = 0.0312;
static const float EdgeThresholdMax = 0.125;
static const float SubpixelQuality = 0.75;
static const float SearchSteps[4] = { 1.5, 2.0, 4.0, 8.0 };

float LumaAt(float2 uv)
{
    return Ldr.SampleLevel(LdrSampler, uv, 0).a;
}

float4 PS_Fxaa(FullscreenOut i) : SV_Target0
{
    float2 texel = 1.0 / ScreenParams.xy;
    float2 uv = i.Uv;
    float4 centre = Ldr.SampleLevel(LdrSampler, uv, 0);
    float lumaC = centre.a;
    float lumaD = LumaAt(uv + float2(0, texel.y));
    float lumaU = LumaAt(uv - float2(0, texel.y));
    float lumaL = LumaAt(uv - float2(texel.x, 0));
    float lumaR = LumaAt(uv + float2(texel.x, 0));
    float lumaMin = min(lumaC, min(min(lumaD, lumaU), min(lumaL, lumaR)));
    float lumaMax = max(lumaC, max(max(lumaD, lumaU), max(lumaL, lumaR)));
    float range = lumaMax - lumaMin;
    if (range < max(EdgeThresholdMin, lumaMax * EdgeThresholdMax))
    {
        return float4(centre.rgb, 1.0);
    }

    float lumaDL = LumaAt(uv + float2(-texel.x, texel.y));
    float lumaUR = LumaAt(uv + float2(texel.x, -texel.y));
    float lumaUL = LumaAt(uv - texel);
    float lumaDR = LumaAt(uv + texel);
    float lumaDownUp = lumaD + lumaU;
    float lumaLeftRight = lumaL + lumaR;
    float lumaLeftCorners = lumaDL + lumaUL;
    float lumaDownCorners = lumaDL + lumaDR;
    float lumaRightCorners = lumaDR + lumaUR;
    float lumaUpCorners = lumaUR + lumaUL;
    float edgeHorizontal = abs(-2.0 * lumaL + lumaLeftCorners) + abs(-2.0 * lumaC + lumaDownUp) * 2.0
        + abs(-2.0 * lumaR + lumaRightCorners);
    float edgeVertical = abs(-2.0 * lumaU + lumaUpCorners) + abs(-2.0 * lumaC + lumaLeftRight) * 2.0
        + abs(-2.0 * lumaD + lumaDownCorners);
    bool isHorizontal = edgeHorizontal >= edgeVertical;

    float luma1 = isHorizontal ? lumaU : lumaL;
    float luma2 = isHorizontal ? lumaD : lumaR;
    float gradient1 = luma1 - lumaC;
    float gradient2 = luma2 - lumaC;
    bool is1Steepest = abs(gradient1) >= abs(gradient2);
    float gradientScaled = 0.25 * max(abs(gradient1), abs(gradient2));
    float stepLength = isHorizontal ? texel.y : texel.x;
    float lumaLocalAverage;
    if (is1Steepest)
    {
        stepLength = -stepLength;
        lumaLocalAverage = 0.5 * (luma1 + lumaC);
    }
    else
    {
        lumaLocalAverage = 0.5 * (luma2 + lumaC);
    }

    float2 currentUv = uv;
    if (isHorizontal)
    {
        currentUv.y += stepLength * 0.5;
    }
    else
    {
        currentUv.x += stepLength * 0.5;
    }

    float2 offset = isHorizontal ? float2(texel.x, 0.0) : float2(0.0, texel.y);
    float2 uv1 = currentUv - offset;
    float2 uv2 = currentUv + offset;
    float lumaEnd1 = LumaAt(uv1) - lumaLocalAverage;
    float lumaEnd2 = LumaAt(uv2) - lumaLocalAverage;
    bool reached1 = abs(lumaEnd1) >= gradientScaled;
    bool reached2 = abs(lumaEnd2) >= gradientScaled;
    if (!reached1)
    {
        uv1 -= offset;
    }

    if (!reached2)
    {
        uv2 += offset;
    }

    [unroll]
    for (uint k = 0; k < 4; k++)
    {
        if (!reached1)
        {
            lumaEnd1 = LumaAt(uv1) - lumaLocalAverage;
            reached1 = abs(lumaEnd1) >= gradientScaled;
            if (!reached1)
            {
                uv1 -= offset * SearchSteps[k];
            }
        }

        if (!reached2)
        {
            lumaEnd2 = LumaAt(uv2) - lumaLocalAverage;
            reached2 = abs(lumaEnd2) >= gradientScaled;
            if (!reached2)
            {
                uv2 += offset * SearchSteps[k];
            }
        }
    }

    float distance1 = isHorizontal ? (uv.x - uv1.x) : (uv.y - uv1.y);
    float distance2 = isHorizontal ? (uv2.x - uv.x) : (uv2.y - uv.y);
    bool isDirection1 = distance1 < distance2;
    float distanceFinal = min(distance1, distance2);
    float edgeThickness = distance1 + distance2;
    float pixelOffset = -distanceFinal / edgeThickness + 0.5;
    bool isLumaCenterSmaller = lumaC < lumaLocalAverage;
    bool correctVariation = ((isDirection1 ? lumaEnd1 : lumaEnd2) < 0.0) != isLumaCenterSmaller;
    float finalOffset = correctVariation ? pixelOffset : 0.0;

    float lumaAverage = (1.0 / 12.0) * (2.0 * (lumaDownUp + lumaLeftRight) + lumaLeftCorners + lumaRightCorners);
    float subPixelOffset1 = saturate(abs(lumaAverage - lumaC) / range);
    float subPixelOffset2 = (-2.0 * subPixelOffset1 + 3.0) * subPixelOffset1 * subPixelOffset1;
    float subPixelOffsetFinal = subPixelOffset2 * subPixelOffset2 * SubpixelQuality;
    finalOffset = max(finalOffset, subPixelOffsetFinal);

    float2 finalUv = uv;
    if (isHorizontal)
    {
        finalUv.y += finalOffset * stepLength;
    }
    else
    {
        finalUv.x += finalOffset * stepLength;
    }

    return float4(Ldr.SampleLevel(LdrSampler, finalUv, 0).rgb, 1.0);
}

technique Tonemap { pass P0 { VertexShader = compile vs_6_0 VS_Fullscreen(); PixelShader = compile ps_6_0 PS_Tonemap(); } }
technique Fxaa { pass P0 { VertexShader = compile vs_6_0 VS_Fullscreen(); PixelShader = compile ps_6_0 PS_Fxaa(); } }
