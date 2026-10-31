// Rain in the HDR scene (PristonRain, Client/PristonTale.Engine3D/Rendering/Weather). Drops: a fixed buffer of streaks,
// each placed by its seed in a box of the world that wraps around the volume centre, falling and drifting with the wind
// by time alone, so no drop moves on the CPU. A streak runs from the drop back along its velocity seen from the moving
// camera, at least StreakPixels wide on screen; a drop thinner than that fades by the ratio instead, so far rain reads
// as a veil. The rain occlusion map (the highest surface of every cell around the camera) hides the part of a streak
// under a roof or the ground. Splashes: the CPU's quads on those surfaces, a ring that spreads and a spray that rises.
// Both test the scene's depth buffer and do not write it.
#include "../Core/Common.hlsli"
#include "../Core/Color.hlsli"

cbuffer RainVertexConstants : register(b0)
{
    float4x4 ViewProjection;
    float4 Volume;           // xyz: centre of the drop boxes; w: seconds since the rain began
    float4 Boxes;            // x: near box width, y: far box width, z: box height, w: streak seconds
    float4 Fall;             // xyz: drop velocity (world units per second), w: unused
    float4 CameraVelocity;   // xyz: the camera's velocity, w: unused
    float4 CameraPosition;   // xyz: the eye, w: focal length in pixels
    float4 Screen;           // xy: target size in pixels, z: least streak width in pixels, w: drop width in world units
};

cbuffer RainPixelConstants : register(b0)
{
    float4 RainColor;        // rgb: radiance of a lit drop (pre-exposed scene units), a: a streak's opacity
    float4 Occlusion;        // xy: centre of the occlusion map, z: cell size, w: 1 while the map holds the field
    float Exposure;          // the scene's exposure (display output only)
    float DisplayOutput;     // 1: write display-referred sRGB into an 8-bit target; 0: HDR radiance
    float OcclusionCells;    // cells along a side of the occlusion map (a power of two)
    float Unused;
};

// R32F heights, toroidal: cell (x, z) of the world at texel (x & (n-1), z & (n-1)); read per pixel, no sampler.
Texture2D<float4> OcclusionMap : register(t0);

static const float NoSurface = -1.0e8;

struct DropIn
{
    float4 Seed : POSITION0;     // x, z: place in the box (0..1), y: height phase (0..1), w: speed and brightness draw
    float4 Corner : TEXCOORD0;   // x: side (-1, 1), y: 0 head, 1 tail, z: layer (0 near, 1 far), w: unused
};

struct RainOut
{
    float4 Position : SV_Position;
    float4 Color : COLOR0;       // rgb scale of RainColor, opacity
    float3 World : TEXCOORD0;
    float3 Shape : TEXCOORD1;    // drop: side, along, 0; splash: uv, kind
    float Age : TEXCOORD2;       // splash: 0..1
};

// The coordinate of a seeded column in a box of the given width around the centre, wrapping as the camera moves.
float Wrap(float seed, float centre, float width)
{
    return centre + (frac((seed - centre) / width + 0.5) - 0.5) * width;
}

RainOut Hidden()
{
    RainOut o = (RainOut)0;
    o.Position = float4(0.0, 0.0, -2.0, 1.0);
    return o;
}

RainOut VS_Drop(DropIn i)
{
    float farLayer = i.Corner.z;
    float width = lerp(Boxes.x, Boxes.y, farLayer);
    float height = Boxes.z;
    float time = Volume.w;
    // Each drop falls 0.8 to 1.2 times the rain's speed.
    float speed = lerp(0.8, 1.2, frac(i.Seed.w * 7.31));
    float3 velocity = Fall.xyz * speed;
    float3 travelled = velocity * time;

    float3 head;
    head.x = Wrap(i.Seed.x * width + travelled.x, Volume.x, width);
    head.z = Wrap(i.Seed.z * width + travelled.z, Volume.z, width);
    head.y = Wrap(i.Seed.y * height + travelled.y, Volume.y, height);

    // Seen from the moving camera the streak runs along the drop's velocity less the camera's.
    float3 streak = (velocity - CameraVelocity.xyz) * Boxes.w;
    float3 tail = head - streak;
    float4 headClip = mul(float4(head, 1.0), ViewProjection);
    float4 tailClip = mul(float4(tail, 1.0), ViewProjection);
    if (headClip.w < 1.0 || tailClip.w < 1.0)
    {
        return Hidden();
    }

    float2 headPixels = headClip.xy / headClip.w * Screen.xy * 0.5;
    float2 tailPixels = tailClip.xy / tailClip.w * Screen.xy * 0.5;
    float2 along = tailPixels - headPixels;
    float lengthPixels = length(along);
    float2 direction = lengthPixels > 1e-3 ? along / lengthPixels : float2(0.0, 1.0);
    float2 across = float2(-direction.y, direction.x);

    // A drop thinner than the least width is drawn at that width, fainter by the ratio.
    float thickness = Screen.w * CameraPosition.w / headClip.w;
    float pixels = max(thickness, Screen.z);
    float4 clip = i.Corner.y < 0.5 ? headClip : tailClip;
    clip.xy += across * (i.Corner.x * pixels * 0.5) / (Screen.xy * 0.5) * clip.w;

    // Faint at the box's sides and next to the eye; brighter drops by their draw.
    float2 fromCentre = abs(float2(head.x - Volume.x, head.z - Volume.z)) / (width * 0.5);
    float edge = saturate((1.0 - max(fromCentre.x, fromCentre.y)) * 5.0);
    float nearFade = saturate((distance(head, CameraPosition.xyz) - 12.0) / 30.0);
    float brightness = lerp(0.6, 1.0, frac(i.Seed.w * 13.7)) * lerp(1.0, 0.7, farLayer);

    RainOut o;
    o.Position = clip;
    o.Color = float4(brightness.xxx, edge * nearFade * saturate(thickness / pixels + 0.08));
    o.World = i.Corner.y < 0.5 ? head : tail;
    o.Shape = float3(i.Corner.x, i.Corner.y, 0.0);
    o.Age = 0.0;
    return o;
}

struct SplashIn
{
    float4 Position : POSITION0;  // xyz: world corner, w: age (0..1)
    float4 Shape : TEXCOORD0;     // xy: corner (-1..1), z: kind (0 ring, 1 spray), w: opacity
};

RainOut VS_Splash(SplashIn i)
{
    RainOut o;
    o.Position = mul(float4(i.Position.xyz, 1.0), ViewProjection);
    o.Color = float4(1.0, 1.0, 1.0, i.Shape.w);
    o.World = i.Position.xyz;
    o.Shape = i.Shape.xyz;
    o.Age = i.Position.w;
    return o;
}

// The highest surface over a point while the map covers it; NoSurface elsewhere.
float SurfaceAt(float3 world)
{
    if (Occlusion.w < 0.5)
    {
        return NoSurface;
    }

    float2 cells = world.xz / Occlusion.z;
    float halfCells = OcclusionCells * 0.5;
    float2 fromCentre = abs(cells - Occlusion.xy / Occlusion.z);
    if (max(fromCentre.x, fromCentre.y) >= halfCells - 1.0)
    {
        return NoSurface;
    }

    int mask = (int)OcclusionCells - 1;
    int2 texel = int2(floor(cells)) & mask;
    return OcclusionMap.Load(int3(texel, 0)).r;
}

float4 Output(float3 rgb, float alpha)
{
    float3 radiance = min(RainColor.rgb * rgb, HdrCap);
    if (DisplayOutput > 0.5)
    {
        return float4(DisplayEncode(radiance, Exposure), alpha);
    }

    return float4(radiance, alpha);
}

float4 PS_Drop(RainOut i) : SV_Target0
{
    if (i.World.y < SurfaceAt(i.World))
    {
        discard;
    }

    // Round across the streak, brightest a little behind the head.
    float across = 1.0 - i.Shape.x * i.Shape.x;
    float along = smoothstep(0.0, 0.15, i.Shape.y) * (1.0 - i.Shape.y * 0.6);
    return Output(i.Color.rgb, RainColor.a * i.Color.a * across * along);
}

float4 PS_Splash(RainOut i) : SV_Target0
{
    float age = i.Age;
    float r = length(i.Shape.xy);
    float alpha;
    if (i.Shape.z < 0.5)
    {
        // A thin ring spreading to the quad's edge.
        float ring = r - lerp(0.15, 0.95, age);
        alpha = exp(-ring * ring * 180.0) * (1.0 - age);
    }
    else
    {
        // A spray rising from the quad's foot and falling back.
        float2 p = float2(i.Shape.x, i.Shape.y * 0.5 + 0.5);
        alpha = saturate(1.0 - length(float2(p.x * 1.6, (p.y - age * 0.7) * 2.2))) * (1.0 - age * age);
    }

    return Output(i.Color.rgb, RainColor.a * i.Color.a * alpha);
}

technique Drops { pass P0 { VertexShader = compile vs_6_0 VS_Drop(); PixelShader = compile ps_6_0 PS_Drop(); } }
technique Splashes { pass P0 { VertexShader = compile vs_6_0 VS_Splash(); PixelShader = compile ps_6_0 PS_Splash(); } }
