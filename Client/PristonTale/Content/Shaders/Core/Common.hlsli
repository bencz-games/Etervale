// Shared constants and helpers of the SM 6.0 library (Content/Shaders, see docs/shaders.md). Conventions of every shader:
//  - world space is Y-up, in Priston units; matrices are row-vector XNA matrices (mul(v, M));
//  - colours are linear Rec.709. Scene passes write pre-exposed HDR radiance into the HalfVector4 scene target and never tonemap:
//    exposure, the display transform and the sRGB encoding happen once, in Passes/PostFinal.fx;
//  - light colours are given as the radiance of a white Lambertian surface facing the light (illuminance / pi), so a
//    lit diffuse surface is albedo * colour * n.l and the specular lobe carries the matching factor of pi;
//  - each effect has exactly one cbuffer per stage (MonoGame's Vulkan effects), and every texture and sampler has an
//    explicit register, the sampler on the same index as its texture.
#ifndef CORE_COMMON_HLSLI
#define CORE_COMMON_HLSLI

static const float PI = 3.14159265358979;

// The cap HDR outputs stay below, under the largest finite half float (65504), so blending never produces infinities.
static const float HdrCap = 60000.0;

// The view depth written for sky pixels (within half-float range), and the view depth beyond which a pixel is sky.
static const float SkyDepth = 60000.0;
static const float SkyDepthThreshold = 50000.0;

// The motion x a sky pixel carries (Passes/SkyComposite.fx), and the value at or above which the temporal pass reads a
// pixel as sky, below the marker so filtering never loses it.
static const float SkyMotionMarker = 4.0;
static const float SkyMotionThreshold = 3.0;

// Interleaved gradient noise (Jimenez 2014) in [0, 1) at a pixel position: a cheap jitter the temporal pass integrates.
float InterleavedGradientNoise(float2 pixel)
{
    return frac(52.9829189 * frac(dot(pixel, float2(0.06711056, 0.00583715))));
}

static const float3 LuminanceWeights = float3(0.2126, 0.7152, 0.0722);

float Luminance(float3 c)
{
    return dot(c, LuminanceWeights);
}

// Opposing interpolated normals and degenerate legacy transforms can produce a zero vector; keep it finite.
float3 SafeNormalize(float3 v)
{
    return v * rsqrt(max(dot(v, v), 1.0e-12));
}

// The camera ray through a screen uv ((0, 0) top left), not normalised: its length is one unit of view depth.
// tanHalfFov holds tan(half fov) along x and y.
float3 CameraRay(float2 uv, float2 tanHalfFov, float3 right, float3 up, float3 forward)
{
    return forward + (uv.x * 2.0 - 1.0) * tanHalfFov.x * right + (1.0 - uv.y * 2.0) * tanHalfFov.y * up;
}

// The Henyey-Greenstein phase function: the share of scattered light per steradian at this cosine to the light's path.
float HenyeyGreenstein(float cosine, float g)
{
    float g2 = g * g;
    return (1.0 - g2) / (4.0 * PI * pow(max(1.0 + g2 - 2.0 * g * cosine, 1.0e-4), 1.5));
}

#endif
