// Stage, scenery, sky and actor geometry (PristonShaderLibrary). Content.mgcb builds it twice: StaticSurface and
// SkinnedSurface (SKINNED). Techniques:
//  - Prepass / PrepassMasked: the depth pre-pass (depth, normal, motion);
//  - Opaque / Masked: the lit HDR forward pass (Masked adds the alpha test). Besides the pre-exposed radiance they write
//    SV_Target1 for the temporal anti-aliasing: the screen motion (uv now minus uv in the previous frame), the previous
//    and the current view depth; without that target bound the write is discarded;
//  - Sky (static only): the sky dome layers, display-referred colour with the layer opacity, blended into the 8-bit
//    sky target with their original blend modes (Passes/SkyComposite.fx brings the result into the scene);
//  - ShadowCaster / ShadowCasterMasked: the sun's cascades, the light's projected z into the R32F atlas;
//  - ShadowClear (static only): clears one cascade's tile of the atlas;
//  - Outline / OutlineMasked (skinned only): the hover outline, the actor redrawn flat in one colour, shifted on screen
//    and pushed back in depth so only a rim shows around the lit actor (PristonRenderer.DrawOutlines).
#include "../Core/Common.hlsli"
#include "../Core/Color.hlsli"
#include "../Core/Frame.hlsli"
#include "../Atmosphere/FogDecl.hlsli"
#include "../Lighting/LightingDecl.hlsli"
#include "../Materials/MaterialDecl.hlsli"
#ifdef SKINNED
#include "../Vertex/SkinningDecl.hlsli"
#else
#include "../Vertex/VertexAnimationDecl.hlsli"
#endif

cbuffer SurfaceVertexConstants : register(b0)
{
    float4x4 ViewProjection;
    float4x4 View;                    // camera space for the sphere-mapped (reflection) layers
    float4x4 CasterViewProjection;    // the shadow casters' light view-projection
    float4 CasterLight;               // lamp shadow casters: xyz the lamp, w 1 / its radius (0: the sun's cascades)
    float4 UvOffsets;                 // xy base layer scroll, zw second layer scroll
    float2 ReflexLayers;              // x base layer, y second layer: 1 = sphere map from the reflected eye vector
    float DrawOpacity;                // sky layer cross-fades and fading props
    float DrawLight;                  // skinned: an actor's floor light (PristonStageLightField), 1 otherwise
    float4x4 PreviousViewProjection;  // the previous frame's unjittered view-projection
    float4 MotionParams;              // xy this frame's jitter in clip units, z previous animation time, w 1 = motion on
    float4 ActorParams;               // skinned: x wobble amplitude, y wobble phase (radians), z 1 = the layer is an overlay on uv0
    float4 OutlineParams;             // skinned outline: xy screen shift in clip units per w; z -M43 times the push, w the push in world units
#ifdef SKINNED
    SKINNING_CONSTANTS
#else
    VERTEX_ANIMATION_CONSTANTS
#endif
};

cbuffer SurfacePixelConstants : register(b0)
{
    FRAME_CONSTANTS
    FOG_CONSTANTS
    LIGHTING_CONSTANTS
    MATERIAL_CONSTANTS
    float4 OutlineColor;              // skinned outline: display-referred colour, a its strength
    float4 CloudShadowParams;         // x one over the shadow's extent (world), yz its centre, w strength (0: none)
};

MATERIAL_RESOURCES
LIGHTING_RESOURCES(2, 4, 5, 6, 7, 8, 9)
FOG_RESOURCES(3)
// The clouds' shadow (Passes/Atmosphere.fx CloudShadow): their transmittance toward the sun around the camera.
Texture2D<float4> CloudShadowMap : register(t10);
SamplerState CloudShadowSampler : register(s10) = sampler_state
{
    Texture = (CloudShadowMap); MinFilter = Linear; MagFilter = Linear; MipFilter = Point; AddressU = Clamp; AddressV = Clamp;
};

float CloudShadowAt(float3 world)
{
    if (CloudShadowParams.w <= 0.0)
    {
        return 1.0;
    }

    float2 uv = (world.xz - CloudShadowParams.yz) * CloudShadowParams.x + 0.5;
    return lerp(1.0, CloudShadowMap.SampleLevel(CloudShadowSampler, uv, 0).r, CloudShadowParams.w);
}

#include "../Atmosphere/Fog.hlsli"
#include "../Lighting/Shading.hlsli"
#include "../Materials/Material.hlsli"
#ifdef SKINNED
#include "../Vertex/Skinning.hlsli"
#else
#include "../Vertex/VertexAnimation.hlsli"
#endif

struct VSIn
{
    float4 Position : POSITION0;
    float3 Normal : NORMAL0;
    float4 Color : COLOR0;
    float2 Uv0 : TEXCOORD0;
    float2 Uv1 : TEXCOORD1;
#ifdef SKINNED
    float BoneIndex : BLENDINDICES0;
#endif
};

struct VSOut
{
    float4 Position : SV_Position;
    float3 WorldPosition : TEXCOORD0;
    float3 Normal : TEXCOORD1;
    float4 Uv : TEXCOORD2;            // xy base layer, zw second layer
    float4 Color : COLOR0;            // linear vertex colour and opacity
    float ViewDepth : TEXCOORD3;
    float3 ClipNow : TEXCOORD4;       // unjittered clip x, y, w of this frame
    float3 ClipPrevious : TEXCOORD5;  // clip x, y, w of the same point in the previous frame
};

struct SurfaceOut
{
    float4 Color : SV_Target0;
    float4 Motion : SV_Target1;
};

struct CasterIn
{
    float4 Position : POSITION0;
    float4 Color : COLOR0;
    float2 Uv0 : TEXCOORD0;
#ifdef SKINNED
    float BoneIndex : BLENDINDICES0;
#endif
};

struct CasterOut
{
    float4 Position : SV_Position;
    float2 Depth : TEXCOORD0;         // clip z and w, divided per pixel
    float4 LampOffset : TEXCOORD3;    // lamp casters: (position - lamp) / radius, w 1
    float2 Uv : TEXCOORD1;
    float Alpha : TEXCOORD2;
};

// The original reflection sphere map: the eye vector reflected about the normal, both in camera space, so the highlight
// follows the camera.
float2 ReflexUv(float3 worldPosition, float3 worldNormal)
{
    float3 eye = SafeNormalize(mul(float4(worldPosition, 1.0), View).xyz);
    float3 normal = SafeNormalize(mul(worldNormal, (float3x3)View));
    float3 reflected = eye - 2.0 * dot(eye, normal) * normal;
    return float2(reflected.x * 0.5 + 0.5, -reflected.y * 0.5 + 0.5);
}

void PlaceVertex(float4 position, float3 normal, float bone, out float3 worldPosition, out float3 worldNormal,
                 out float3 previousPosition)
{
#ifdef SKINNED
    uint index = (uint)bone;
    worldPosition = SkinPosition(position, index);
    worldNormal = SafeNormalize(SkinNormal(normal, index));
    previousPosition = PreviousSkinPosition(position, index);
#else
    // Static batches are built in world space (PristonGpuMeshBuilder).
    worldPosition = position.xyz + VertexAnimOffset(position.xyz);
    worldNormal = SafeNormalize(normal);
    previousPosition = position.xyz + VertexAnimOffsetAt(position.xyz, MotionParams.z);
#endif
}

VSOut VS_Surface(VSIn i)
{
    VSOut o;
#ifdef SKINNED
    float bone = i.BoneIndex;
    // Animated scenery carries the stage's authored light for its texture (PristonStageBakedLight); actors are white and
    // take the authored light of the floor they stand on, so they darken in a tree's shade with the floor.
    float4 colour = float4(SrgbToLinear(i.Color.rgb) * DrawLight, 1.0);
#else
    float bone = 0.0;
    // The authored stage colour (the original per-vertex light) tints the surface; its alpha is the opacity.
    float4 colour = float4(SrgbToLinear(i.Color.rgb), i.Color.a);
#endif
    float3 worldPosition;
    float3 worldNormal;
    float3 previousPosition;
    PlaceVertex(i.Position, i.Normal, bone, worldPosition, worldNormal, previousPosition);
#ifdef SKINNED
    // A distortion wobbles every vertex up to ActorParams.x along a sine of its own coordinate (32-unit waves).
    float3 phase = worldPosition * (6.2831853 / 32.0) + ActorParams.y;
    float3 wobble = float3(sin(phase.x), sin(phase.y), cos(phase.z)) * ActorParams.x;
    worldPosition += wobble;
    previousPosition += wobble;
#endif
    o.Position = mul(float4(worldPosition, 1.0), ViewProjection);
    o.ClipNow = float3(o.Position.xy - MotionParams.xy * o.Position.w, o.Position.w);
    float4 previous = mul(float4(previousPosition, 1.0), PreviousViewProjection);
    // Without a motion target the surface reports no motion.
    o.ClipPrevious = MotionParams.w > 0.5 ? previous.xyw : o.ClipNow;
    o.WorldPosition = worldPosition;
    o.Normal = worldNormal;
#ifdef SKINNED
    float2 layerUv = ActorParams.z > 0.5 ? i.Uv0 : i.Uv1;
#else
    float2 layerUv = i.Uv1;
#endif
    o.Uv = float4(i.Uv0 + UvOffsets.xy, layerUv + UvOffsets.zw);
    [branch]
    if (ReflexLayers.x + ReflexLayers.y > 0.0)
    {
        float2 reflex = ReflexUv(worldPosition, worldNormal);
        o.Uv.xy = ReflexLayers.x > 0.5 ? reflex : o.Uv.xy;
        o.Uv.zw = ReflexLayers.y > 0.5 ? reflex : o.Uv.zw;
    }

    o.Color = float4(colour.rgb, colour.a * DrawOpacity);
    o.ViewDepth = o.Position.w;
    return o;
}

// Development views (PristonDebugView): 1 albedo, 2 vertex colour, 3 normal, 4 lighting on white, 5 overdraw,
// 7 roughness, 8 metallic, 9 ambient occlusion and contact shadows (red, green), 10 the specular radiance alone;
// 6 (exposure) is the final pass's.
float4 DebugOutput(Surface s, float3 vertexColour, float3 lighting, float3 specular, float2 pixel, float viewDepth)
{
    if (DebugView < 1.5)
        return float4(s.Albedo, s.Alpha);
    if (DebugView < 2.5)
        return float4(vertexColour, s.Alpha);
    if (DebugView < 3.5)
        return float4(s.Normal * 0.5 + 0.5, s.Alpha);
    if (DebugView < 4.5)
        return float4(lighting, s.Alpha);
    if (DebugView < 5.5)
        return float4(0.08, 0.04, 0.02, 1.0);
    if (DebugView < 7.5)
        return float4(s.Roughness.xxx, s.Alpha);
    if (DebugView < 8.5)
        return float4(s.Metallic.xxx, s.Alpha);
    if (DebugView < 9.5)
        return float4(ScreenOcclusion(pixel, viewDepth), 0.0, s.Alpha);
    return float4(specular, s.Alpha);
}

float4 FinishOutput(float3 radiance, float alpha)
{
    radiance = min(radiance, HdrCap);
    if (DisplayOutput > 0.5)
    {
        return float4(DisplayEncode(radiance, Exposure), alpha);
    }

    return float4(radiance, alpha);
}

// Screen motion in uv (y down), the previous and the current view depth; the depth is negative on cut-outs (foliage,
// fences), which the temporal resolve treats as reactive (Passes/PostTemporal.fx).
float4 MotionOutput(VSOut i, bool masked)
{
    float2 now = i.ClipNow.xy / i.ClipNow.z;
    float2 previous = i.ClipPrevious.xy / max(i.ClipPrevious.z, 1.0e-4);
    float depth = masked || Translucency > 0.0 ? -i.ViewDepth : i.ViewDepth;
    return float4((now - previous) * float2(0.5, -0.5), i.ClipPrevious.z, depth);
}

SurfaceOut Output(VSOut i, float4 colour, bool masked)
{
    SurfaceOut o;
    o.Color = colour;
    o.Motion = MotionOutput(i, masked);
    return o;
}

float4 ShadeSurface(VSOut i, bool masked)
{
    float3 v = SafeNormalize(CameraPosition - i.WorldPosition);
    Surface s = SampleSurface(i.Uv.xy, i.Uv.zw, i.Color, i.Normal, VisibleFaceNormal(i.WorldPosition, v));
    if (masked)
    {
        AlphaTest(s.Alpha);
    }

    float a = SpecularAntiAliasedAlpha(s.Normal, max(s.Roughness * s.Roughness, 0.002));
    // Emission: the original display-referred colour at its authored level by day (EmissionScale >= 1 / Exposure), a
    // brighter light source at night.
    float emission = (MaterialBlink > 0.5 ? BlinkPulse : 1.0) * EmissionScale;
    // Characters and monsters are not light sources: their self-illuminated parts show the texture at its display level
    // by day and by night, unlit, as the original drew them.
    if (ActorReceiver > 0.5)
    {
        emission = (MaterialBlink > 0.5 ? BlinkPulse : 1.0) / Exposure;
    }
    float rim = pow(1.0 - saturate(dot(s.Normal, v)), 3.0);
    float3 radiance;
    Lighting lighting;
    lighting.Diffuse = 0.0;
    lighting.Specular = 0.0;
    [branch]
    if (ShadingModel > 1.5)
    {
        // Multiplicative blends: the colour is the blend factor, never radiance.
        return float4(s.Albedo, s.Alpha);
    }
    else if (ShadingModel > 0.5)
    {
        radiance = s.Albedo * (EmissiveStrength * emission);
    }
    else
    {
        float nl = saturate(dot(s.Normal, SunDirection));
        float back = saturate(-dot(s.Normal, SunDirection));
        float2 occlusion = ScreenOcclusion(i.Position.xy, i.ViewDepth);
        // A translucent surface lit from behind looks up its shadow on the sun's side.
        bool transmits = Translucency > 0.0 && back > nl;
        float3 shadowNormal = transmits ? -s.Normal : s.Normal;
        // Actors take no contact shadows: the march across their own thin bodies left dark patches that changed with the
        // view and the pose.
        bool actor = ActorReceiver > 0.5;
        float shadow = SunShadow(i.WorldPosition, shadowNormal, max(nl, transmits ? back : 0.0), i.ViewDepth, i.Position.xy,
            actor ? ActorShadowBias : 0.0, actor ? 1.0 : occlusion.y) * CloudShadowAt(i.WorldPosition);
        AddDirectLight(SunDirection, SunColor * shadow, s.Normal, v, s.Albedo, s.F0, s.Metallic, a, lighting.Diffuse, lighting.Specular);
        // Transmission through leaves: diffuse through the blade, stronger looking toward the sun (forward scattering).
        float forward = pow(saturate(dot(-v, SunDirection)), 4.0);
        lighting.Diffuse += s.Albedo * SunColor * (shadow * Translucency * back * (0.35 + 0.65 * forward));
        AddLocalLights(i.WorldPosition, s.Normal, v, s.Albedo, s.F0, s.Metallic, a, i.Position.xy, ActorReceiver < 0.5, lighting);
        // The authored light (vertex colour) is the art's sky visibility: it occludes the sky's reflections too.
        float visibility = occlusion.x * saturate(Luminance(i.Color.rgb));
        AddAmbient(s.Normal, v, s.Albedo, s.F0, s.Metallic, s.Roughness, occlusion.x, visibility, lighting);
        // The original shine (a sphere-mapped reflection texture on armour and weapons) reflects the sky and a quarter of
        // the sun, occluded and shadowed like the surface itself: it does not glow in the shade.
        float3 environment = lerp(GroundAmbient, SkyAmbient, 0.75) * visibility + SunColor * (0.25 * shadow);
        lighting.Specular += s.Reflection * environment;
        radiance = lighting.Diffuse + lighting.Specular;
        radiance += s.Albedo * (EmissiveStrength * emission);
    }

    radiance += RimColor * (rim / Exposure);
    // Gameplay tints add (or take away) light in the original display units, like the original colour adjustment.
    radiance = max(radiance + s.Albedo * (ActorTint.rgb / Exposure), 0.0);
    [branch]
    if (DebugView > 0.5)
    {
        float3 irradiance = lighting.Diffuse / max(s.Albedo, 1.0e-4);
        return DebugOutput(s, i.Color.rgb, irradiance + lighting.Specular, lighting.Specular, i.Position.xy, i.ViewDepth);
    }

    radiance = ApplyFog(radiance, i.WorldPosition, i.ViewDepth);
    return FinishOutput(radiance, s.Alpha * ActorTint.a);
}

SurfaceOut PS_Opaque(VSOut i)
{
    return Output(i, ShadeSurface(i, false), false);
}

SurfaceOut PS_Masked(VSOut i)
{
    return Output(i, ShadeSurface(i, true), true);
}

// Sky dome layers: the sRGB texel times the layer's tint, as the dome was authored to be composed (display-referred,
// blended in sRGB); DrawOpacity cross-fades two profiles. The vertex stage decodes the tint, so it is encoded back.
float4 PS_Sky(VSOut i) : SV_Target0
{
    float4 texel = BaseColorMap.Sample(BaseColorSampler, i.Uv.xy);
    return float4(texel.rgb * LinearToSrgb(i.Color.rgb), texel.a * i.Color.a);
}

CasterOut VS_Caster(CasterIn i)
{
    CasterOut o;
#ifdef SKINNED
    float3 worldPosition = SkinPosition(i.Position, (uint)i.BoneIndex);
    o.Alpha = 1.0;
#else
    float3 worldPosition = i.Position.xyz + VertexAnimOffset(i.Position.xyz);
    o.Alpha = i.Color.a;
#endif
    o.Position = mul(float4(worldPosition, 1.0), CasterViewProjection);
    // Interpolate z and w and divide per pixel.
    o.Depth = o.Position.zw;
    o.LampOffset = float4((worldPosition - CasterLight.xyz) * CasterLight.w, CasterLight.w > 0.0 ? 1.0 : 0.0);
    o.Uv = i.Uv0 + UvOffsets.xy;
    return o;
}

// The sun's cascades keep the orthographic depth; the lamps' cube faces the distance to the lamp over its radius.
float4 CasterDepth(CasterOut i)
{
    float depth = i.LampOffset.w > 0.5 ? length(i.LampOffset.xyz) : saturate(i.Depth.x / i.Depth.y);
    return float4(depth, 0.0, 0.0, 1.0);
}

float4 PS_Caster(CasterOut i) : SV_Target0
{
    return CasterDepth(i);
}

// Same coverage as the lit Masked pass (base texture alpha times vertex opacity), so cutout silhouettes and their
// shadows agree.
float4 PS_CasterMasked(CasterOut i) : SV_Target0
{
    clip(BaseColorMap.Sample(BaseColorSampler, i.Uv).a * i.Alpha - AlphaCutoff);
    return CasterDepth(i);
}

// The depth pre-pass of the screen-space occlusion and of the temporal anti-aliasing (PristonRenderer.DrawPrepass): the
// opaque surfaces' depth (the scene's depth buffer, the colour write masked off), linear view depth, world normal and
// motion; the lit pass then shades each pixel once (depth test less-equal, no depth write).
struct PrepassOut
{
    float4 Color : SV_Target0;
    float Depth : SV_Target1;
    float4 Normal : SV_Target2;
    float4 Motion : SV_Target3;
};

PrepassOut Prepass(VSOut i, bool masked)
{
    float3 v = SafeNormalize(CameraPosition - i.WorldPosition);
    float3 n = OrientNormal(i.Normal, VisibleFaceNormal(i.WorldPosition, v));
    if (masked)
    {
        AlphaTest(SampleBaseColor(i.Uv.xy, i.Color).a);
    }

    PrepassOut o;
    o.Color = 0.0;
    o.Depth = i.ViewDepth;
    // Alpha: the smoothness the screen-space reflections read (Passes/Reflections.fx).
    o.Normal = float4(n * 0.5 + 0.5, 1.0 - Roughness);
    o.Motion = MotionOutput(i, masked);
    return o;
}

PrepassOut PS_Prepass(VSOut i)
{
    return Prepass(i, false);
}

PrepassOut PS_PrepassMasked(VSOut i)
{
    return Prepass(i, true);
}

#ifdef SKINNED
VSOut VS_Outline(VSIn i)
{
    VSOut o = VS_Surface(i);
    o.Position.xy += OutlineParams.xy * o.Position.w;
    // The view depth pushed back by OutlineParams.w world units, keeping w: z = -M33 w + M43 becomes -M33 w + M43 w / (w + push).
    o.Position.z += OutlineParams.z / (o.Position.w + OutlineParams.w);
    return o;
}

float4 Outline(VSOut i, bool masked)
{
    if (masked)
    {
        AlphaTest(SampleBaseColor(i.Uv.xy, i.Color).a);
    }

    return float4(SrgbToLinear(OutlineColor.rgb) * (OutlineColor.a / Exposure), 1.0);
}

float4 PS_Outline(VSOut i) : SV_Target0
{
    return Outline(i, false);
}

float4 PS_OutlineMasked(VSOut i) : SV_Target0
{
    return Outline(i, true);
}
#endif

// Clears a cascade's tile of the shadow atlas: far depth and the far value, drawn over the tile's viewport with the
// full-screen triangle (Post/PristonFullscreenTriangle), so the other cascades keep their contents.
struct ClearIn
{
    float4 Position : POSITION0;
};

float4 VS_ShadowClear(ClearIn i) : SV_Position
{
    return float4(i.Position.xy, 1.0, 1.0);
}

float4 PS_ShadowClear() : SV_Target0
{
    return 1.0;
}

technique Opaque { pass P0 { VertexShader = compile vs_6_0 VS_Surface(); PixelShader = compile ps_6_0 PS_Opaque(); } }
technique Masked { pass P0 { VertexShader = compile vs_6_0 VS_Surface(); PixelShader = compile ps_6_0 PS_Masked(); } }
technique Prepass { pass P0 { VertexShader = compile vs_6_0 VS_Surface(); PixelShader = compile ps_6_0 PS_Prepass(); } }
technique PrepassMasked { pass P0 { VertexShader = compile vs_6_0 VS_Surface(); PixelShader = compile ps_6_0 PS_PrepassMasked(); } }
#ifdef SKINNED
technique Outline { pass P0 { VertexShader = compile vs_6_0 VS_Outline(); PixelShader = compile ps_6_0 PS_Outline(); } }
technique OutlineMasked { pass P0 { VertexShader = compile vs_6_0 VS_Outline(); PixelShader = compile ps_6_0 PS_OutlineMasked(); } }
#endif
#ifndef SKINNED
technique ShadowClear { pass P0 { VertexShader = compile vs_6_0 VS_ShadowClear(); PixelShader = compile ps_6_0 PS_ShadowClear(); } }
technique Sky { pass P0 { VertexShader = compile vs_6_0 VS_Surface(); PixelShader = compile ps_6_0 PS_Sky(); } }
#endif
technique ShadowCaster { pass P0 { VertexShader = compile vs_6_0 VS_Caster(); PixelShader = compile ps_6_0 PS_Caster(); } }
technique ShadowCasterMasked { pass P0 { VertexShader = compile vs_6_0 VS_Caster(); PixelShader = compile ps_6_0 PS_CasterMasked(); } }
