// The glTF material of the GLTF variant of Surface.fx: the base surface (Materials/Material.hlsli's Surface) from the
// packed maps and factors, and the layers glTF adds over it, lit by the sun and the sky: clearcoat (GGX with a
// dielectric's Fresnel, F0 0.04, on its own normal), sheen (Charlie's distribution with Estevez and Kulla's visibility;
// its directional albedo in BrdfLut.b) and diffuse transmission (diffuse light through the surface from behind). The
// lamps light the base surface only. Requires MATERIAL_CONSTANTS, GLTF_MATERIAL_CONSTANTS and their resources.
#ifndef MATERIALS_GLTF_MATERIAL_HLSLI
#define MATERIALS_GLTF_MATERIAL_HLSLI

#include "../Core/Color.hlsli"
#include "../Lighting/Brdf.hlsli"

#define GLTF_BASE 0
#define GLTF_NORMALS 1
#define GLTF_SURFACE 2
#define GLTF_EMISSIVE 3
#define GLTF_SHEEN 4
#define GLTF_TRANSMISSION 5

struct GltfLayers
{
    float Occlusion;
    float3 Emission;
    float3 CoatNormal;
    float Coat;
    float CoatRoughness;
    float3 SheenColor;
    float SheenRoughness;
    float3 TransmissionColor;
    float Transmission;
};

bool GltfHas(uint texture)
{
    return GltfTextureReads[texture * 2 + 1].z > -0.5;
}

// One coordinate wrapped by a mode: 0 repeat (the sampler's), 1 clamp, 2 mirror. Clamp and mirror stop inset short of
// the edges, so the repeating sampler's filter never reaches across to the opposite edge.
float GltfWrap(float c, float mode, float inset)
{
    if (mode < 0.5)
    {
        return c;
    }

    if (mode < 1.5)
    {
        return clamp(c, inset, 1.0 - inset);
    }

    float f = frac(c * 0.5) * 2.0;
    return clamp(f > 1.0 ? 2.0 - f : f, inset, 1.0 - inset);
}

// The texture's UVs before wrapping (KHR_texture_transform: scale, rotation, offset), so their gradients stay continuous
// across a clamped or mirrored edge.
float2 GltfTransformedUv(uint texture, float2 uv0, float2 uv1)
{
    float4 m = GltfTextureReads[texture * 2];
    float4 o = GltfTextureReads[texture * 2 + 1];
    float2 uv = o.z > 0.5 ? uv1 : uv0;
    return float2(dot(uv, m.xy), dot(uv, m.zw)) + o.xy;
}

float4 GltfSample(Texture2D<float4> map, SamplerState state, uint texture, float2 uv0, float2 uv1)
{
    float2 uv = GltfTransformedUv(texture, uv0, uv1);
    float2 dx = ddx(uv);
    float2 dy = ddy(uv);
    float wraps = GltfTextureReads[texture * 2 + 1].w;
    float wrapT = floor(wraps / 3.0);
    // Half the filter's footprint: the pixel's extent in UVs, at least a texel of the base level.
    float2 inset = min(0.5 * max(max(abs(dx), abs(dy)), 1.0 / max(GltfTextureSizes[texture].xy, 1.0)), 0.5);
    float2 wrapped = float2(GltfWrap(uv.x, wraps - wrapT * 3.0, inset.x), GltfWrap(uv.y, wrapT, inset.y));
    return map.SampleGrad(state, wrapped, dx, dy);
}

// The base colour's coverage (alpha test of the masked passes and casters).
float GltfCoverage(float2 uv0, float2 uv1, float4 vertexColour)
{
    float4 base = GltfSample(BaseColorMap, BaseColorSampler, GLTF_BASE, uv0, uv1);
    return BaseColorFactor.a * (GltfHas(GLTF_BASE) ? base.a : 1.0) * vertexColour.a;
}

// A tangent-space normal (xy from the map, z rebuilt from them, then xy scaled as glTF's normal scale does) in the frame
// of n and the vertex tangent (w the bitangent's sign).
float3 GltfPerturb(float2 encoded, float scale, float3 n, float4 tangent)
{
    float2 xy = encoded * 2.0 - 1.0;
    float z = sqrt(saturate(1.0 - dot(xy, xy)));
    xy *= scale;
    float3 t = SafeNormalize(tangent.xyz - n * dot(n, tangent.xyz));
    float3 b = cross(n, t) * (tangent.w < 0.0 ? -1.0 : 1.0);
    return SafeNormalize(t * xy.x + b * xy.y + n * z);
}

// A gameplay overlay (GltfOverlayParams) adds over the base colour, as it does over a legacy actor's.
float3 GltfOverlaid(float3 albedo, float2 uv0)
{
    float2 uv = uv0 + float2(GltfOverlayParams.y, 0.0);
    float3 overlay = GltfOverlayMap.SampleGrad(GltfOverlaySampler, uv, ddx(uv), ddy(uv)).rgb;
    return GltfOverlayParams.x > 0.5 ? saturate(albedo + overlay) : albedo;
}

// uv0, uv1 the vertex UV sets; vertexColour linear; normal and tangent interpolated in world space; visibleFace from
// VisibleFaceNormal. Every map is sampled whatever the branch, in uniform control flow.
Surface SampleGltfSurface(float2 uv0, float2 uv1, float4 vertexColour, float3 normal, float4 tangent, float3 visibleFace,
                          out GltfLayers layers)
{
    float4 base = GltfSample(BaseColorMap, BaseColorSampler, GLTF_BASE, uv0, uv1);
    float4 normals = GltfSample(LayerMap, LayerSampler, GLTF_NORMALS, uv0, uv1);
    float4 surface = GltfSample(GltfSurfaceMap, GltfSurfaceSampler, GLTF_SURFACE, uv0, uv1);
    float4 emissive = GltfSample(GltfEmissiveMap, GltfEmissiveSampler, GLTF_EMISSIVE, uv0, uv1);
    float4 sheen = GltfSample(GltfSheenMap, GltfSheenSampler, GLTF_SHEEN, uv0, uv1);
    float4 transmission = GltfSample(GltfTransmissionMap, GltfTransmissionSampler, GLTF_TRANSMISSION, uv0, uv1);

    base = GltfHas(GLTF_BASE) ? base : 1.0;
    surface = GltfHas(GLTF_SURFACE) ? surface : 1.0;
    emissive = GltfHas(GLTF_EMISSIVE) ? emissive : 1.0;
    sheen = GltfHas(GLTF_SHEEN) ? sheen : 1.0;
    transmission = GltfHas(GLTF_TRANSMISSION) ? transmission : 1.0;

    Surface s;
    float4 colour = BaseColorFactor * base * vertexColour;
    s.Albedo = GltfOverlaid(colour.rgb, uv0);
    s.Alpha = colour.a;
    s.Reflection = 0.0;
    float3 n = OrientNormal(normal, visibleFace);
    // A back face turns its whole tangent frame, as glTF's double-sided surfaces do.
    tangent.xyz *= dot(n, normal) < 0.0 ? -1.0 : 1.0;
    s.Normal = GltfHas(GLTF_NORMALS) ? GltfPerturb(normals.rg, GltfNormalScales.x, n, tangent) : n;
    s.Roughness = saturate(Roughness * surface.g);
    s.Metallic = saturate(Metallic * surface.b);
    s.F0 = lerp(0.04.xxx, s.Albedo, s.Metallic);

    layers.Occlusion = lerp(1.0, surface.r, GltfEmission.a);
    layers.Emission = GltfEmission.rgb * emissive.rgb * EmissiveStrength;
    layers.Coat = saturate(GltfNormalScales.z * surface.a);
    layers.CoatRoughness = saturate(GltfNormalScales.w * emissive.a);
    layers.CoatNormal = GltfHas(GLTF_NORMALS) && any(abs(normals.ba - 0.5) > 1.0 / 255.0)
        ? GltfPerturb(normals.ba, GltfNormalScales.y, n, tangent) : n;
    layers.SheenColor = GltfSheen.rgb * sheen.rgb;
    layers.SheenRoughness = saturate(GltfSheen.a * sheen.a);
    // A metal transmits nothing.
    layers.Transmission = saturate(GltfTransmission.a * transmission.a) * (1.0 - s.Metallic);
    layers.TransmissionColor = GltfTransmission.rgb * transmission.rgb;
    return s;
}

float GltfCoatFresnel(float cosine)
{
    return 0.04 + 0.96 * pow(1.0 - saturate(cosine), 5.0);
}

float GltfSheenLambdaFit(float x, float a)
{
    float t = (1.0 - a) * (1.0 - a);
    return lerp(21.5473, 25.3245, t) / (1.0 + lerp(3.82987, 3.32435, t) * pow(x, lerp(0.19823, 0.16801, t)))
        + lerp(-1.97760, -1.27393, t) * x + lerp(-4.32054, -4.85967, t);
}

float GltfSheenLambda(float cosine, float a)
{
    return cosine < 0.5 ? exp(GltfSheenLambdaFit(cosine, a)) : exp(2.0 * GltfSheenLambdaFit(0.5, a) - GltfSheenLambdaFit(1.0 - cosine, a));
}

float GltfSheenAlbedo(GltfLayers layers, float3 n, float3 v)
{
    return BrdfLut.SampleLevel(BrdfLutSampler, float2(saturate(dot(n, v)), layers.SheenRoughness), 0).b;
}

// The share of the base surface's light that leaves through the layers above, seen from v: what the clearcoat reflects
// and the sheen's directional albedo take from it.
float GltfBaseAttenuation(GltfLayers layers, float3 n, float3 v)
{
    float sheen = max(layers.SheenColor.r, max(layers.SheenColor.g, layers.SheenColor.b)) * GltfSheenAlbedo(layers, n, v);
    return (1.0 - layers.Coat * GltfCoatFresnel(dot(layers.CoatNormal, v))) * (1.0 - sheen);
}

// The sky's prefiltered radiance toward r and its irradiance around n, or the hemispherical ambient without an
// environment (Lighting/Shading.hlsli AddAmbient).
float3 GltfSkyRadiance(float3 r, float roughness)
{
    return EnvironmentParams.x > 0.5 ? EnvironmentSpecular(r, roughness) : HemisphereAmbient(r);
}

float3 GltfSkyIrradiance(float3 n)
{
    return EnvironmentParams.x > 0.5 ? EnvironmentIrradiance(n) : HemisphereAmbient(n);
}

// The layers under the sky: the clearcoat's and the sheen's reflections (occluded as the base's, specularVisibility)
// and the diffuse light reaching the back of a transmitting surface (ao).
float3 GltfAmbientLayers(GltfLayers layers, float3 n, float3 v, float ao, float specularVisibility)
{
    float3 c = layers.CoatNormal;
    float3 result = GltfSkyRadiance(reflect(-v, c), layers.CoatRoughness) * (layers.Coat * GltfCoatFresnel(dot(c, v)) * specularVisibility);
    result += layers.SheenColor * GltfSheenAlbedo(layers, n, v) * GltfSkyRadiance(reflect(-v, n), layers.SheenRoughness) * specularVisibility;
    result += layers.TransmissionColor * layers.Transmission * GltfSkyIrradiance(-n) * ao;
    return result;
}

// The clearcoat and sheen lobes of one light of colour `light` from l (radiance units as AddDirectLight).
float3 GltfLayerLight(GltfLayers layers, float3 n, float3 v, float3 l, float3 light)
{
    float3 result = 0.0;
    float3 h = SafeNormalize(v + l);
    [branch]
    if (layers.Coat > 0.0)
    {
        float3 c = layers.CoatNormal;
        float nl = saturate(dot(c, l));
        float a = max(layers.CoatRoughness * layers.CoatRoughness, 0.002);
        result += PI * D_GGX(saturate(dot(c, h)), a) * V_SmithGGX(max(dot(c, v), 1.0e-3), nl, a) * GltfCoatFresnel(dot(v, h))
            * layers.Coat * nl * light;
    }

    [branch]
    if (max(layers.SheenColor.r, max(layers.SheenColor.g, layers.SheenColor.b)) > 0.0)
    {
        float nl = saturate(dot(n, l));
        float nv = max(dot(n, v), 1.0e-3);
        float nh = saturate(dot(n, h));
        float a = max(layers.SheenRoughness * layers.SheenRoughness, 1.0e-3);
        float distribution = (2.0 + 1.0 / a) * pow(sqrt(max(1.0 - nh * nh, 0.0)), 1.0 / a) / (2.0 * PI);
        float visibility = nl > 0.0
            ? saturate(1.0 / ((1.0 + GltfSheenLambda(nv, a) + GltfSheenLambda(nl, a)) * 4.0 * nv * nl)) : 0.0;
        result += PI * layers.SheenColor * distribution * visibility * nl * light;
    }

    return result;
}

// Light through the surface from behind: diffuse, from the side facing away from l.
float3 GltfTransmittedLight(GltfLayers layers, float3 n, float3 l, float3 light)
{
    return layers.TransmissionColor * layers.Transmission * saturate(-dot(n, l)) * light;
}

#endif
