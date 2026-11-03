// The first pass of the HDR scene (PristonSceneTargets): it fills every scene target, so no clear is needed.
//  - SkyComposite: the sky dome behind the scene. The dome's layers are display-referred art blended with their original
//    modes (additive glows, alpha clouds) into an 8-bit sRGB target, as they were authored to be composed; this pass
//    decodes that image into the scene at its authored display level (divided by the frame's nominal display scale,
//    which the final pass applies again);
//  - Background: a flat radiance (no sky: underground, tools).
// Both write the sky's motion marker and depth into the motion target when it is bound (discarded otherwise).
#include "../Core/Common.hlsli"
#include "../Core/Color.hlsli"
#include "../Core/Fullscreen.hlsli"

cbuffer SkyCompositeConstants : register(b0)
{
    float4 SkySourceRect;      // the viewport's part of the sky target: xy offset, zw size (uv units)
    float3 BackgroundRadiance; // Background: the pre-exposed radiance written everywhere
    float SkyExposure;
};

Texture2D<float4> SkyColor : register(t0);
SamplerState SkyColorSampler : register(s0) = sampler_state
{
    Texture = (SkyColor); MinFilter = Point; MagFilter = Point; MipFilter = Point; AddressU = Clamp; AddressV = Clamp;
};


struct SceneOut
{
    float4 Color : SV_Target0;
    float4 Motion : SV_Target1;
};

SceneOut Fill(float3 radiance)
{
    SceneOut o;
    o.Color = float4(min(radiance, HdrCap), 1.0);
    o.Motion = float4(SkyMotionMarker, SkyMotionMarker, SkyDepth, SkyDepth);
    return o;
}

SceneOut PS_SkyComposite(FullscreenOut i)
{
    float3 display = SkyColor.SampleLevel(SkyColorSampler, SkySourceRect.xy + i.Uv * SkySourceRect.zw, 0).rgb;
    return Fill(SrgbToLinear(display) / SkyExposure);
}

SceneOut PS_Background(FullscreenOut i)
{
    return Fill(BackgroundRadiance);
}

technique SkyComposite
{
    pass P0 { VertexShader = compile vs_6_0 VS_Fullscreen(); PixelShader = compile ps_6_0 PS_SkyComposite(); }
}

technique Background
{
    pass P0 { VertexShader = compile vs_6_0 VS_Fullscreen(); PixelShader = compile ps_6_0 PS_Background(); }
}
