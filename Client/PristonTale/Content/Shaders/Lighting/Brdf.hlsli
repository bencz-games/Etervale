// GGX microfacet specular with height-correlated Smith visibility and Schlick Fresnel, Lambert diffuse, and geometric specular
// anti-aliasing (the lobe widened by the screen-space variance of the normal).
#ifndef LIGHTING_BRDF_HLSLI
#define LIGHTING_BRDF_HLSLI

#include "../Core/Common.hlsli"

float D_GGX(float nh, float a)
{
    float a2 = a * a;
    float d = nh * nh * (a2 - 1.0) + 1.0;
    return a2 / (PI * d * d);
}

// Height-correlated Smith visibility, G2 / (4 n.l n.v) (Heitz 2014).
float V_SmithGGX(float nv, float nl, float a)
{
    float a2 = a * a;
    float lambdaV = nl * sqrt(nv * nv * (1.0 - a2) + a2);
    float lambdaL = nv * sqrt(nl * nl * (1.0 - a2) + a2);
    return 0.5 / max(lambdaV + lambdaL, 1.0e-5);
}

float3 F_Schlick(float3 f0, float vh)
{
    return f0 + (1.0 - f0) * pow(1.0 - vh, 5.0);
}

// The grazing reflectance of a rough surface: microfacet shadowing keeps a rough dielectric (skin, cloth, stone) from
// turning into a mirror at grazing angles, so its Fresnel rises only to max(1 - roughness, F0) (Lagarde 2014), and dark
// albedos keep no grey, metallic rim from the sky and the sun behind them.
float3 GrazingReflectance(float3 f0, float perceptualRoughness)
{
    return max(1.0 - perceptualRoughness, f0);
}

float3 F_SchlickRough(float3 f0, float vh, float perceptualRoughness)
{
    return f0 + (GrazingReflectance(f0, perceptualRoughness) - f0) * pow(1.0 - vh, 5.0);
}

// Kaplanyan 2016; Tokuyoshi & Kaplanyan 2019. Needs uniform control flow (derivatives).
float SpecularAntiAliasedAlpha(float3 n, float a)
{
    float3 dndu = ddx(n);
    float3 dndv = ddy(n);
    float variance = 0.25 * (dot(dndu, dndu) + dot(dndv, dndv));
    float kernel = min(2.0 * variance, 0.18);
    return sqrt(saturate(a * a + kernel));
}

// One light of radiance-equivalent colour `light` (Core/Common.hlsli) from direction l.
void AddDirectLight(float3 l, float3 light, float3 n, float3 v, float3 albedo, float3 f0, float metallic, float a,
                    inout float3 diffuse, inout float3 specular)
{
    float nl = saturate(dot(n, l));
    float3 h = SafeNormalize(l + v);
    float nv = max(dot(n, v), 1.0e-3);
    float nh = saturate(dot(n, h));
    float vh = saturate(dot(v, h));
    // a is the (specular-anti-aliased) GGX alpha: its square root is the perceptual roughness.
    float3 F = F_SchlickRough(f0, vh, sqrt(a));
    specular += PI * D_GGX(nh, a) * V_SmithGGX(nv, nl, a) * F * light * nl;
    diffuse += (1.0 - F) * (1.0 - metallic) * albedo * light * nl;
}

#endif
