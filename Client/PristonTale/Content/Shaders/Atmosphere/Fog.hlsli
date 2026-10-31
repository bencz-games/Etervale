// Distance fog toward the visible sky. Beyond the start distance (PristonDistanceFog) the opacity
// approaches its cap exponentially, cap * (1 - exp(-density * d)); the colour is the sky capture behind the surface
// (PristonSkyCapture, display-referred and decoded here) plus a Schlick approximation of Henyey-Greenstein forward
// scattering toward the sun. The capture already holds the sky's own glow around the sun, so the forward scattering
// fades out as the opacity reaches its cap: a surface the fog hides takes the sky behind it instead of a brighter
// silhouette against it.
// Requires FRAME_CONSTANTS, FOG_CONSTANTS and FOG_RESOURCES.
#ifndef ATMOSPHERE_FOG_HLSLI
#define ATMOSPHERE_FOG_HLSLI

float FogOpacity(float viewDepth)
{
    float distance = max(viewDepth - FogParams.y, 0.0);
    return FogParams.x * FogParams.w * (1.0 - exp(-distance * FogParams.z));
}

// The capture is a strip of four 90-degree perspective tiles facing -Z, +X, +Z and -X: the view direction picks a tile
// by its major horizontal axis and projects onto it like the capture camera did.
float3 CapturedSky(float3 direction)
{
    float3 absolute = abs(direction);
    float tile;
    float major;
    float side;
    if (absolute.z >= absolute.x)
    {
        tile = direction.z < 0.0 ? 0.0 : 2.0;
        major = absolute.z;
        side = direction.z < 0.0 ? direction.x : -direction.x;
    }
    else
    {
        tile = direction.x > 0.0 ? 1.0 : 3.0;
        major = absolute.x;
        side = direction.x > 0.0 ? direction.z : -direction.z;
    }

    major = max(major, 1.0e-4);
    float elevation = clamp(direction.y / major * FogSkyBand.z, FogSkyBand.x, FogSkyBand.y);
    float u = (tile + side / major * 0.5 + 0.5) * 0.25;
    float v = 1.0 - (elevation - FogSkyBand.w) * FogSkyBand2.x;
    // The capture is the display-referred dome (8-bit sRGB), decoded like Passes/SkyComposite.fx.
    return SrgbToLinear(FogSky.SampleLevel(FogSkySampler, float2(u, v), 0).rgb) / Exposure;
}

float3 FogColour(float3 worldPosition, float opacity)
{
    float3 direction = SafeNormalize(worldPosition - CameraPosition);
    float3 colour = FogSkyBand2.y > 0.5 ? CapturedSky(direction) : FogColor;
    float k = FogPhaseK;
    float denominator = 1.0 - k * dot(direction, SunDirection);
    float phase = (1.0 - k * k) / (4.0 * PI * denominator * denominator);
    float hidden = opacity / max(FogParams.x * FogParams.w, 1.0e-4);
    return colour + FogInScatter * (phase * saturate(1.0 - hidden));
}

float3 ApplyFog(float3 radiance, float3 worldPosition, float viewDepth)
{
    float opacity = FogOpacity(viewDepth);
    [branch]
    if (opacity <= 0.0)
    {
        return radiance;
    }

    return lerp(radiance, FogColour(worldPosition, opacity), opacity);
}

#endif
