// Image-based lighting from the sky dome (PristonEnvironmentLighting), rebuilt when the sky changes:
//  - Octahedral: the six 90-degree faces of the dome (rendered display-referred like the sky behind the scene) to an HDR
//    octahedral map, decoded and divided by the frame's nominal display scale; below the horizon the ground's bounce;
//  - Prefilter: the GGX-prefiltered radiance for one roughness level of the specular atlas (Karis 2013, split sum),
//    importance-sampled from the octahedral map;
//  - Irradiance (compute): the order-2 spherical harmonics of the dome's faces (Ramamoorthi and Hanrahan 2001), scaled so
//    that an upward surface receives the light model's sky light (the dome at most at its visible level, a uniform fill
//    for the rest), plus a uniform ground below; stored convolved with the cosine lobe and divided by pi, the shaders'
//    light unit. Entry 9 holds the scale of the specular atlas (x) and the fill's radiance (yzw), which the octahedral
//    map, built after it, includes.
#include "../Core/Common.hlsli"
#include "../Core/Color.hlsli"
#include "../Core/Fullscreen.hlsli"
#include "../Lighting/Octahedral.hlsli"
#include "../Lighting/CubeFaces.hlsli"
#include "../Lighting/SphericalHarmonics.hlsli"

cbuffer EnvironmentConstants : register(b0)
{
    float4 EnvParams;          // x nominal display scale, y roughness (Prefilter), z octahedral size (px), w sample count
    float3 SkyAmbientTarget;   // the light model's sky light on an upward surface (pre-exposed E / pi)
    float EnvPadding0;
    float3 GroundAmbientTarget; // the ground's bounce on a downward surface
    float EnvPadding1;
};

Texture2D<float4> FaceAtlas : register(t0);     // six faces side by side, 8-bit sRGB display values
Texture2D<float4> Octahedral : register(t1);    // HDR octahedral radiance
StructuredBuffer<float4> LightSh : register(t2);     // the irradiance pass's result (its scale and fill)
SamplerState FaceSampler : register(s0) = sampler_state
{
    Texture = (FaceAtlas); MinFilter = Linear; MagFilter = Linear; MipFilter = Point; AddressU = Clamp; AddressV = Clamp;
};
SamplerState OctahedralSampler : register(s1) = sampler_state
{
    Texture = (Octahedral); MinFilter = Linear; MagFilter = Linear; MipFilter = Point; AddressU = Clamp; AddressV = Clamp;
};

RWStructuredBuffer<float4> Sh : register(u0);

// Lanczos sigma factors of the order-2 bands: sinc(pi l / 3).
static const float ShWindowBand1 = 0.826993;
static const float ShWindowBand2 = 0.413497;


// The dome as seen in direction d, from the cube face of d's major axis (Lighting/CubeFaces.hlsli).
float3 SkyRadiance(float3 d)
{
    uint face = CubeFace(d);
    float2 local = clamp(CubeFaceUv(d, face, 1.0e-4), 0.01, 0.99);
    float3 display = FaceAtlas.SampleLevel(FaceSampler, float2((face + local.x) / 6.0, local.y), 0).rgb;
    return SrgbToLinear(display) / EnvParams.x;
}

float4 PS_Octahedral(FullscreenOut i) : SV_Target0
{
    float3 d = OctahedralDecode(i.Uv);
    float3 sky = SkyRadiance(d);
    // The ground and the sky's fill (Irradiance) in the light model's units, converted to the atlas's (divided by its
    // scale); the fill covers the whole upper hemisphere uniformly.
    float scale = max(LightSh[9].x, 1.0e-4);
    float3 ground = GroundAmbientTarget / scale;
    float3 fill = LightSh[9].yzw / scale;
    float below = smoothstep(-0.02, -0.15, d.y);
    return float4(min(lerp(sky + fill, ground, below), HdrCap), 1.0);
}

float RadicalInverse(uint bits)
{
    bits = (bits << 16u) | (bits >> 16u);
    bits = ((bits & 0x55555555u) << 1u) | ((bits & 0xAAAAAAAAu) >> 1u);
    bits = ((bits & 0x33333333u) << 2u) | ((bits & 0xCCCCCCCCu) >> 2u);
    bits = ((bits & 0x0F0F0F0Fu) << 4u) | ((bits & 0xF0F0F0F0u) >> 4u);
    bits = ((bits & 0x00FF00FFu) << 8u) | ((bits & 0xFF00FF00u) >> 8u);
    return float(bits) * 2.3283064365386963e-10;
}

float3 ImportanceSampleGgx(float2 xi, float a, float3 n)
{
    float phi = 2.0 * PI * xi.x;
    float cosTheta = sqrt((1.0 - xi.y) / (1.0 + (a * a - 1.0) * xi.y));
    float sinTheta = sqrt(1.0 - cosTheta * cosTheta);
    float3 h = float3(sinTheta * cos(phi), sinTheta * sin(phi), cosTheta);
    float3 up = abs(n.y) < 0.999 ? float3(0, 1, 0) : float3(1, 0, 0);
    float3 tangentX = normalize(cross(up, n));
    float3 tangentY = cross(n, tangentX);
    return tangentX * h.x + tangentY * h.y + n * h.z;
}

float4 PS_Prefilter(FullscreenOut i) : SV_Target0
{
    float3 n = OctahedralDecode(i.Uv);
    float roughness = EnvParams.y;
    [branch]
    if (roughness <= 0.0)
    {
        return float4(Octahedral.SampleLevel(OctahedralSampler, i.Uv, 0).rgb, 1.0);
    }

    float a = roughness * roughness;
    uint count = (uint)EnvParams.w;
    float3 sum = 0.0;
    float weight = 0.0;
    [loop]
    for (uint k = 0; k < count; k++)
    {
        float2 xi = float2((k + 0.5) / count, RadicalInverse(k));
        float3 h = ImportanceSampleGgx(xi, a, n);
        float3 l = 2.0 * dot(n, h) * h - n;
        float nl = dot(n, l);
        if (nl > 0.0)
        {
            sum += Octahedral.SampleLevel(OctahedralSampler, OctahedralEncode(l), 0).rgb * nl;
            weight += nl;
        }
    }

    return float4(sum / max(weight, 1.0e-4), 1.0);
}

// Share of the light model's colour in the sky's irradiance.
static const float SkyHueShare = 0.65;

#define SH_THREADS 256
#define SH_GRID 32
groupshared float3 Partial[SH_THREADS];
groupshared float PartialWeight[SH_THREADS];

[numthreads(SH_THREADS, 1, 1)]
void CS_Irradiance(uint index : SV_GroupIndex)
{
    // Every thread integrates four cells of a 32x32 octahedral grid over the dome's faces (the octahedral map also
    // holds the fill). The solid angle of an octahedral cell is proportional to 1 / |d|^3 for the unnormalised
    // direction on the octahedron.
    float3 c[9];
    [unroll]
    for (uint z = 0; z < 9; z++)
    {
        c[z] = 0.0;
    }

    float weight = 0.0;
    [unroll]
    for (uint t = 0; t < 4; t++)
    {
        uint cell = index * 4 + t;
        float2 uv = (float2(cell % SH_GRID, cell / SH_GRID) + 0.5) / SH_GRID;
        float2 p = uv * 2.0 - 1.0;
        float3 raw = float3(p.x, 1.0 - abs(p.x) - abs(p.y), p.y);
        float3 d = OctahedralDecode(uv);
        if (d.y < 0.0)
        {
            continue;
        }

        float length3 = pow(dot(raw, raw), 1.5);
        float w = 1.0 / max(length3, 1.0e-4);
        float3 radiance = SkyRadiance(d);
        float b[9];
        ShBasis(d, b);
        [unroll]
        for (uint k = 0; k < 9; k++)
        {
            c[k] += radiance * (b[k] * w);
        }

        weight += w;
    }

    // The whole sphere's weight is 4 pi: the upper hemisphere's texels carry half of the total octahedral weight.
    PartialWeight[index] = weight;
    GroupMemoryBarrierWithGroupSync();
    for (uint stride = SH_THREADS / 2; stride > 0; stride >>= 1)
    {
        if (index < stride)
        {
            PartialWeight[index] += PartialWeight[index + stride];
        }

        GroupMemoryBarrierWithGroupSync();
    }

    float hemisphereWeight = PartialWeight[0];
    float3 sky[9];
    [unroll]
    for (uint coefficient = 0; coefficient < 9; coefficient++)
    {
        Partial[index] = c[coefficient];
        GroupMemoryBarrierWithGroupSync();
        for (uint s = SH_THREADS / 2; s > 0; s >>= 1)
        {
            if (index < s)
            {
                Partial[index] += Partial[index + s];
            }

            GroupMemoryBarrierWithGroupSync();
        }

        sky[coefficient] = Partial[0] * (2.0 * PI / max(hemisphereWeight, 1.0e-6));
        GroupMemoryBarrierWithGroupSync();
    }

    if (index != 0)
    {
        return;
    }

    // The dome is display art: its reflections are never brighter than the sky behind the scene (a scale of at most 1),
    // and a dome brighter than the light model's sky light is dimmed to it. The rest of the sky light, when the dome is
    // darker (the night is lit for play, about a hundred times its dark dome), is a uniform fill over the upper
    // hemisphere, so neither the dome's horizon glow nor its moon is magnified into the reflections. The scale and the
    // fill come from the unwindowed sky (the reflections are not dimmed or brightened by the window).
    float target = Luminance(SkyAmbientTarget);
    float domeUp = max(Luminance(EvaluateSh(sky, float3(0, 1, 0))), 1.0e-6);
    float specularScale = min(1.0, target / domeUp);

    // The hemispheres' step at the horizon rings in an order-2 expansion (dark undersides and bright bands on curved
    // surfaces): the bands are windowed by Lanczos sigma factors, sinc(l / 3), before the sky is scaled.
    [unroll]
    for (uint w = 1; w < 9; w++)
    {
        sky[w] *= w < 4 ? ShWindowBand1 : ShWindowBand2;
    }

    // The dome keeps its direction and gives an upward surface its share of the sky light. The sky light's colour, in the
    // irradiance and in the fill (so a dark dome's reflected sky has the colour of the light it sheds), is mostly the
    // light model's (SkyHueShare), the rest the dome's (its art hues are strong, a teal night or a purple dusk).
    float3 up = EvaluateSh(sky, float3(0, 1, 0));
    float upLevel = max(Luminance(up), 1.0e-6);
    float scale = specularScale * domeUp / upLevel;
    float3 tint = lerp(1.0, clamp(SkyAmbientTarget / max(up * (target / upLevel), 1.0e-6), 0.25, 4.0), SkyHueShare);
    // A black dome has no colour to keep.
    float3 skyColour = max(up * tint, 0.0);
    skyColour = Luminance(skyColour) > 1.0e-5 * target ? skyColour * (target / Luminance(skyColour)) : SkyAmbientTarget;
    float3 fill = skyColour * (max(target - specularScale * domeUp, 0.0) / max(target, 1.0e-6));
    [unroll]
    for (uint t2 = 0; t2 < 9; t2++)
    {
        sky[t2] *= tint;
    }

    // A uniform ground of radiance G below the horizon and the uniform fill above it, projected on the basis: by symmetry
    // only the constant term and the y term are non-zero (their step windowed like the sky's). An upward surface under a
    // uniform upper hemisphere of radiance F receives F (unwindowed) or F (1 + w1) / 2 (windowed).
    float3 g = GroundAmbientTarget;
    float3 f = fill * (2.0 / (1.0 + ShWindowBand1));
    [unroll]
    for (uint k = 0; k < 9; k++)
    {
        Sh[k] = float4(sky[k] * scale, 0.0);
    }

    Sh[0] = float4(sky[0] * scale + (g + f) * (0.282095 * 2.0 * PI), 0.0);
    Sh[1] = float4(sky[1] * scale + (f - g) * (0.488603 * PI * ShWindowBand1), 0.0);
    Sh[9] = float4(specularScale, fill);
}

technique Octahedral { pass P0 { VertexShader = compile vs_6_0 VS_Fullscreen(); PixelShader = compile ps_6_0 PS_Octahedral(); } }
technique Prefilter { pass P0 { VertexShader = compile vs_6_0 VS_Fullscreen(); PixelShader = compile ps_6_0 PS_Prefilter(); } }
technique Irradiance { pass P0 { ComputeShader = compile cs_6_0 CS_Irradiance(); } }
