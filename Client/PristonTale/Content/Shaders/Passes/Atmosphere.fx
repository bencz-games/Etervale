// The physical sky (PristonPhysicalSky, Client/PristonTale.Engine3D/Rendering/Atmosphere), after Hillaire 2020 ("A
// Scalable and Production Ready Sky and Atmosphere Rendering Technique"): lookup tables of the transmittance to space,
// of the light every scattering order beyond the first adds and of the sky's radiance around the viewer, then the
// volumetric clouds over the upper hemisphere and their shadow on the ground, and the sky itself: the sky view, the
// sun's and the moon's discs, the stars and the Milky Way, the aurora, high cirrus, the clouds and a rainbow. Lengths in
// km with the planet's centre at the origin and y up. The sky pass writes display-referred sRGB into the 8-bit sky
// targets the painted domes use (decoded by the scene with the same exposure), so the fog's capture and the
// image-based lighting take the physical sky as they took the domes.
#include "../Core/Common.hlsli"
#include "../Core/Color.hlsli"
#include "../Core/Fullscreen.hlsli"

cbuffer AtmosphereConstants : register(b0)
{
    float4x4 InverseViewProjection; // sky: clip -> world of a view without translation
    float4 ToSun;           // xyz toward the sun, w its illuminance outside the atmosphere (pre-exposed radiance units)
    float4 ToMoon;          // xyz toward the moon, w its light outside the atmosphere (same units)
    float4 MoonFace;        // xyz toward the light on the moon's face (its phase), w the moon disc's radiance
    float4 Night;           // x stars (0..1), y the sky's turn (radians), z airglow radiance, w aurora (0..1)
    float4 Effects;         // x rainbow (0..1), y seconds, z cirrus cover (0..1), w display scale
    float4 Clouds;          // x coverage, y density scale, z history weight (1 refreshes), w frame
    float4 CloudOffset;     // xy km of wind and camera (wrapped), zw unused
    float4 Flash;           // xyz toward the lightning, w its radiance
    float4 Target;          // xy target size, zw its inverse
    float4 CloudShadowArea; // x extent (km), y overcast grey without clouds (0..1), z unused, w 1 while clouds draw
};

Texture2D<float4> TransmittanceLut : register(t0);
SamplerState TransmittanceSampler : register(s0) = sampler_state
{
    Texture = (TransmittanceLut); MinFilter = Linear; MagFilter = Linear; MipFilter = Point; AddressU = Clamp; AddressV = Clamp;
};
Texture2D<float4> MultipleLut : register(t1);
SamplerState MultipleSampler : register(s1) = sampler_state
{
    Texture = (MultipleLut); MinFilter = Linear; MagFilter = Linear; MipFilter = Point; AddressU = Clamp; AddressV = Clamp;
};
Texture2D<float4> SkyViewLut : register(t2);
SamplerState SkyViewSampler : register(s2) = sampler_state
{
    Texture = (SkyViewLut); MinFilter = Linear; MagFilter = Linear; MipFilter = Point; AddressU = Wrap; AddressV = Clamp;
};
Texture3D<float4> CloudNoise : register(t3);
SamplerState CloudNoiseSampler : register(s3) = sampler_state
{
    Texture = (CloudNoise); MinFilter = Linear; MagFilter = Linear; MipFilter = Point; AddressU = Wrap; AddressV = Wrap; AddressW = Wrap;
};
Texture2D<float4> CloudMap : register(t4);
SamplerState CloudMapSampler : register(s4) = sampler_state
{
    Texture = (CloudMap); MinFilter = Linear; MagFilter = Linear; MipFilter = Point; AddressU = Wrap; AddressV = Clamp;
};

// Earth's atmosphere (clear day).
static const float BottomRadius = 6360.0;
static const float TopRadius = 6460.0;
static const float ObserverAltitude = 0.2;
static const float3 RayleighScattering = float3(5.802e-3, 13.558e-3, 33.1e-3);
static const float RayleighHeight = 8.0;
static const float MieScattering = 3.996e-3;
static const float MieExtinction = 4.40e-3;
static const float MieHeight = 1.2;
static const float MieG = 0.8;
static const float3 OzoneAbsorption = float3(0.650e-3, 1.881e-3, 0.085e-3);
static const float OzoneCentre = 25.0;
static const float OzoneHalfWidth = 15.0;
static const float3 GroundAlbedo = float3(0.3, 0.3, 0.3);

// The cloud layer: its base and thickness (km), the noise's tiles (km), the densest cloud's extinction (per km), and
// how far a ray marches through it before the haze takes the rest.
static const float CloudBottom = 1.5;
static const float CloudThickness = 2.5;
static const float CloudNoiseTile = 12.0;
static const float CloudCoverageTile = 60.0;
static const float CloudExtinction = 40.0;
static const float CloudFarthest = 60.0;
static const int CloudSteps = 48;
static const int CloudLightSteps = 5;
// Cirrus at this altitude (km), a sheet the sky pass draws over the clouds' hemisphere.
static const float CirrusAltitude = 8.0;

// Angular radii (radians) of the sun's disc (a little larger than true, to read at the game's view) and the moon's.
static const float SunRadius = 0.008;
static const float MoonRadius = 0.013;
// The sun disc's radiance per unit illuminance: bright enough to clip white.
static const float SunDiscRadiance = 2.0e4;

float3 ObserverPosition()
{
    return float3(0.0, BottomRadius + ObserverAltitude, 0.0);
}

// Distance to the far intersection with a sphere around the origin, or -1.
float SphereExit(float3 origin, float3 direction, float radius)
{
    float b = dot(origin, direction);
    float c = dot(origin, origin) - radius * radius;
    float d = b * b - c;
    return d < 0.0 ? -1.0 : -b + sqrt(d);
}

// Distance to the near intersection ahead of an origin, or -1.
float SphereEnter(float3 origin, float3 direction, float radius)
{
    float b = dot(origin, direction);
    float c = dot(origin, origin) - radius * radius;
    float d = b * b - c;
    if (d < 0.0)
    {
        return -1.0;
    }

    float t = -b - sqrt(d);
    return t >= 0.0 ? t : (-b + sqrt(d) >= 0.0 ? 0.0 : -1.0);
}

struct Medium
{
    float3 Scattering;
    float3 Extinction;
    float3 Rayleigh;
    float Mie;
};

Medium MediumAt(float altitude)
{
    float rayleigh = exp(-altitude / RayleighHeight);
    float mie = exp(-altitude / MieHeight);
    float ozone = max(0.0, 1.0 - abs(altitude - OzoneCentre) / OzoneHalfWidth);
    Medium m;
    m.Rayleigh = RayleighScattering * rayleigh;
    m.Mie = MieScattering * mie;
    m.Scattering = m.Rayleigh + m.Mie;
    m.Extinction = m.Rayleigh + MieExtinction * mie + OzoneAbsorption * ozone;
    return m;
}

// Bruneton's transmittance parameterization: the distance to the top between its extremes, and the radius.
float2 TransmittanceUv(float r, float mu)
{
    float h = sqrt(TopRadius * TopRadius - BottomRadius * BottomRadius);
    float rho = sqrt(max(r * r - BottomRadius * BottomRadius, 0.0));
    float discriminant = r * r * (mu * mu - 1.0) + TopRadius * TopRadius;
    float d = max(0.0, -r * mu + sqrt(max(discriminant, 0.0)));
    float dMin = TopRadius - r;
    float dMax = rho + h;
    return float2((d - dMin) / (dMax - dMin), rho / h);
}

// Light reaching a point from a direction outside the atmosphere: none where the planet is in the way.
float3 TransmittanceToSpace(float3 position, float3 direction)
{
    float r = length(position);
    float mu = dot(position / r, direction);
    if (SphereEnter(position, direction, BottomRadius) > 0.0)
    {
        return 0.0;
    }

    return TransmittanceLut.SampleLevel(TransmittanceSampler, TransmittanceUv(r, mu), 0).rgb;
}

float RayleighPhase(float cosine)
{
    return 3.0 / (16.0 * PI) * (1.0 + cosine * cosine);
}

// Cornette-Shanks.
float MiePhase(float cosine)
{
    float g = MieG;
    float k = 3.0 / (8.0 * PI) * (1.0 - g * g) / (2.0 + g * g);
    return k * (1.0 + cosine * cosine) / pow(max(1.0 + g * g - 2.0 * g * cosine, 1e-4), 1.5);
}

float3 MultipleScattering(float3 position, float3 toLight)
{
    float r = length(position);
    float cosine = dot(position / r, toLight);
    float2 uv = float2(cosine * 0.5 + 0.5, saturate((r - BottomRadius) / (TopRadius - BottomRadius)));
    return MultipleLut.SampleLevel(MultipleSampler, uv, 0).rgb;
}

// Transmittance table: one texel per (radius, view cosine); 40 steps to the top of the atmosphere.
float4 PS_Transmittance(FullscreenOut i) : SV_Target0
{
    float h = sqrt(TopRadius * TopRadius - BottomRadius * BottomRadius);
    float rho = h * i.Uv.y;
    float r = sqrt(rho * rho + BottomRadius * BottomRadius);
    float dMin = TopRadius - r;
    float dMax = rho + h;
    float d = dMin + i.Uv.x * (dMax - dMin);
    float mu = d == 0.0 ? 1.0 : clamp((h * h - rho * rho - d * d) / (2.0 * r * d), -1.0, 1.0);
    float3 origin = float3(0.0, r, 0.0);
    float3 direction = float3(sqrt(1.0 - mu * mu), mu, 0.0);
    float pathLength = SphereExit(origin, direction, TopRadius);
    float3 depth = 0.0;
    const int steps = 40;
    [loop]
    for (int s = 0; s < steps; s++)
    {
        float3 p = origin + direction * ((s + 0.5) / steps * pathLength);
        depth += MediumAt(length(p) - BottomRadius).Extinction * (pathLength / steps);
    }

    return float4(exp(-depth), 1.0);
}

// The light every scattering order beyond the first adds at an altitude for a sun angle (Hillaire's isotropic
// approximation): the second order over 64 directions, then the geometric series of the share each order scatters
// again; the ground reflects with its albedo.
float4 PS_Multiple(FullscreenOut i) : SV_Target0
{
    float cosSun = i.Uv.x * 2.0 - 1.0;
    float altitude = i.Uv.y * (TopRadius - BottomRadius);
    float3 origin = float3(0.0, BottomRadius + max(altitude, 0.01), 0.0);
    float3 toSun = float3(sqrt(saturate(1.0 - cosSun * cosSun)), cosSun, 0.0);
    float3 second = 0.0;
    float3 transfer = 0.0;
    const int directions = 8;
    const int steps = 20;
    [loop]
    for (int a = 0; a < directions; a++)
    {
        [loop]
        for (int b = 0; b < directions; b++)
        {
            float theta = acos(1.0 - 2.0 * (a + 0.5) / directions);
            float phi = 2.0 * PI * (b + 0.5) / directions;
            float3 direction = float3(sin(theta) * cos(phi), cos(theta), sin(theta) * sin(phi));
            float ground = SphereEnter(origin, direction, BottomRadius);
            float pathLength = ground > 0.0 ? ground : SphereExit(origin, direction, TopRadius);
            float dt = pathLength / steps;
            float3 throughput = 1.0;
            float3 light = 0.0;
            float3 share = 0.0;
            [loop]
            for (int s = 0; s < steps; s++)
            {
                float3 p = origin + direction * ((s + 0.5) * dt);
                Medium m = MediumAt(length(p) - BottomRadius);
                float3 stepTransmittance = exp(-m.Extinction * dt);
                float3 integral = (1.0 - stepTransmittance) / max(m.Extinction, 1e-6);
                float3 sunLight = TransmittanceToSpace(p, toSun) * saturate(dot(normalize(p), toSun) * 4.0 + 0.5);
                light += throughput * m.Scattering * sunLight / (4.0 * PI) * integral;
                share += throughput * m.Scattering * integral;
                throughput *= stepTransmittance;
            }

            if (ground > 0.0)
            {
                float3 p = origin + direction * ground;
                light += throughput * TransmittanceToSpace(p, toSun) * saturate(dot(normalize(p), toSun)) * GroundAlbedo / PI;
            }

            second += light;
            transfer += share;
        }
    }

    float count = directions * directions;
    second /= count;
    transfer /= count;
    return float4(second / max(1.0 - transfer, 1e-3), 1.0);
}

// Sky-view coordinates: u the world azimuth, v the elevation with resolution crowded at the horizon.
float2 SkyViewUv(float3 direction)
{
    float elevation = asin(clamp(direction.y, -1.0, 1.0));
    float v = 0.5 + 0.5 * sign(elevation) * sqrt(abs(elevation) / (PI * 0.5));
    return float2(atan2(direction.x, direction.z) / (2.0 * PI) + 0.5, 1.0 - v);
}

float3 SkyViewDirection(float2 uv)
{
    float v = 1.0 - uv.y;
    float t = (v - 0.5) * 2.0;
    float elevation = sign(t) * t * t * (PI * 0.5);
    float azimuth = (uv.x - 0.5) * 2.0 * PI;
    return float3(sin(azimuth) * cos(elevation), sin(elevation), cos(azimuth) * cos(elevation));
}

// Single and multiple scattering of one light at a point, per unit light.
float3 LightScattering(Medium m, float3 position, float3 direction, float3 toLight)
{
    float cosine = dot(direction, toLight);
    float3 single = m.Rayleigh * RayleighPhase(cosine) + m.Mie * MiePhase(cosine);
    float3 sunLight = TransmittanceToSpace(position, toLight);
    return single * sunLight + m.Scattering * MultipleScattering(position, toLight);
}

// The sky's radiance around the viewer for the sun and the moon at their light.
float4 PS_SkyView(FullscreenOut i) : SV_Target0
{
    float3 direction = SkyViewDirection(i.Uv);
    float3 origin = ObserverPosition();
    float ground = SphereEnter(origin, direction, BottomRadius);
    float pathLength = ground > 0.0 ? ground : SphereExit(origin, direction, TopRadius);
    float3 throughput = 1.0;
    float3 radiance = 0.0;
    const int steps = 32;
    float previous = 0.0;
    [loop]
    for (int s = 0; s < steps; s++)
    {
        // Samples crowd toward the viewer, where the air is densest.
        float t = pathLength * pow((s + 1.0) / steps, 2.0);
        float dt = t - previous;
        float3 p = origin + direction * (previous + 0.5 * dt);
        previous = t;
        Medium m = MediumAt(length(p) - BottomRadius);
        float3 stepTransmittance = exp(-m.Extinction * dt);
        float3 integral = (1.0 - stepTransmittance) / max(m.Extinction, 1e-6);
        float3 light = LightScattering(m, p, direction, ToSun.xyz) * ToSun.w + LightScattering(m, p, direction, ToMoon.xyz) * ToMoon.w;
        radiance += throughput * light * integral;
        throughput *= stepTransmittance;
    }

    return float4(radiance, 1.0);
}

// Cells along each side of the noise volume (PristonCloudNoise.Size).
static const float CloudNoiseCells = 64.0;

// The noise volume read with its fraction eased between texels (one bilinear fetch): plain linear interpolation bends
// at every texel, and the coverage threshold that stretches the density turns those bends into contour lines.
float4 SmoothNoise(float3 uvw)
{
    float3 p = uvw * CloudNoiseCells + 0.5;
    float3 cell = floor(p);
    float3 f = p - cell;
    f = f * f * (3.0 - 2.0 * f);
    return CloudNoise.SampleLevel(CloudNoiseSampler, (cell + f - 0.5) / CloudNoiseCells, 0);
}

float CloudRemap(float value, float low, float high, float newLow, float newHigh)
{
    return newLow + (value - low) * (newHigh - newLow) / max(high - low, 1e-5);
}

// Coverage at a horizontal position: the weather's, varied by a broad pattern so clear and clouded regions alternate.
float CoverageAt(float2 position)
{
    float pattern = SmoothNoise(float3(position.x, 0.37 * CloudCoverageTile, position.y) / CloudCoverageTile).r;
    return saturate(Clouds.x + (pattern - 0.5) * 0.7);
}

// Density (0..1) at a position whose height runs 0..1 through the layer: cumulus rounded below and rising with the
// coverage, eroded by detail; a closing sky lays a stratus deck in the lower half of the layer.
float CloudDensityAt(float3 position, float height, bool detail)
{
    if (height <= 0.0 || height >= 1.0)
    {
        return 0.0;
    }

    float local = CoverageAt(position.xz);
    float stratus = saturate((Clouds.x - 0.7) / 0.3) * saturate(height * 8.0) * saturate((0.6 - height) * 4.0) * 0.35;
    if (local <= 0.0)
    {
        return stratus;
    }

    float profile = saturate(height * 5.0) * saturate((1.0 - height) * (1.0 + 2.0 * (1.0 - local)));
    float4 shapeNoise = SmoothNoise(position / CloudNoiseTile);
    float fbm = shapeNoise.g * 0.625 + shapeNoise.b * 0.25 + shapeNoise.a * 0.125;
    float shape = CloudRemap(shapeNoise.r, fbm - 1.0, 1.0, 0.0, 1.0) * profile;
    float density = saturate(CloudRemap(shape, 1.0 - local, 1.0, 0.0, 1.0)) * local;
    if (detail && density > 0.0)
    {
        float4 detailNoise = SmoothNoise(position / (CloudNoiseTile * 0.18));
        float erosion = detailNoise.g * 0.625 + detailNoise.b * 0.25 + detailNoise.a * 0.125;
        erosion = lerp(erosion, 1.0 - erosion, saturate(height * 4.0));
        density = saturate(CloudRemap(density, erosion * 0.35, 1.0, 0.0, 1.0));
    }

    return max(density, stratus);
}

// The cloud position of a point: the wind's and the camera's offset added, km.
float3 CloudPosition(float3 p)
{
    return float3(p.x + CloudOffset.x, length(p) - BottomRadius - CloudBottom, p.z + CloudOffset.y);
}

float HenyeyGreenstein(float cosine, float g)
{
    float g2 = g * g;
    return (1.0 - g2) / (4.0 * PI * pow(max(1.0 + g2 - 2.0 * g * cosine, 1e-4), 1.5));
}

// Strong forward scattering with a weaker backward lobe.
float CloudPhase(float cosine)
{
    return lerp(HenyeyGreenstein(cosine, 0.8), HenyeyGreenstein(cosine, -0.3), 0.3);
}

// The clouds' hemisphere by azimuth (u, wrapping) and elevation (v: the square root of its share of a right angle, 0 at
// the horizon), so the low sky the camera looks at holds most of the texels.
float2 CloudUv(float3 direction)
{
    float elevation = asin(saturate(direction.y));
    return float2(atan2(direction.x, direction.z) / (2.0 * PI) + 0.5, sqrt(elevation / (PI * 0.5)));
}

float3 CloudDirection(float2 uv)
{
    float elevation = uv.y * uv.y * (PI * 0.5);
    float azimuth = (uv.x - 0.5) * 2.0 * PI;
    return float3(sin(azimuth) * cos(elevation), sin(elevation), cos(azimuth) * cos(elevation));
}

// Optical depth from a point toward a light through the layer, in lengthening steps without detail.
float CloudLightDepth(float3 position, float3 toLight)
{
    float depth = 0.0;
    float step = 0.12;
    float travelled = 0.0;
    [loop]
    for (int s = 0; s < CloudLightSteps; s++)
    {
        travelled += step;
        float3 p = position + toLight * (travelled - step * 0.5);
        float3 c = CloudPosition(p);
        depth += CloudDensityAt(c, c.y / CloudThickness, false) * step;
        step *= 1.8;
    }

    return depth * CloudExtinction * Clouds.y;
}

float3 SkyViewAt(float3 direction)
{
    return SkyViewLut.SampleLevel(SkyViewSampler, SkyViewUv(normalize(float3(direction.x, max(direction.y, 0.01), direction.z))), 0).rgb;
}

// One texel of the clouds' hemisphere: rgb the light they send toward the viewer, a the share of the sky behind they
// let through; blended with the last frame's by Clouds.z, the march jittered per frame and per texel so the blend
// averages it out. Every texel renders every frame, so no texel lags its neighbours.
float4 PS_Clouds(FullscreenOut i) : SV_Target0
{
    float3 direction = CloudDirection(i.Uv);
    if (direction.y < 0.003 || Clouds.x <= 0.0)
    {
        return float4(0.0, 0.0, 0.0, 1.0);
    }

    float3 origin = ObserverPosition();
    float start = SphereExit(origin, direction, BottomRadius + CloudBottom);
    float end = min(SphereExit(origin, direction, BottomRadius + CloudBottom + CloudThickness), start + CloudFarthest);
    float noise = frac(InterleavedGradientNoise(i.Position.xy) + Clouds.w * 0.618034);
    float dt = (end - start) / CloudSteps;
    // The dominant light of the hour lights the clouds; the sky's zenith and horizon give their ambient light.
    bool moonLights = ToMoon.w * 20.0 > ToSun.w;
    float3 toLight = moonLights ? ToMoon.xyz : ToSun.xyz;
    float lightLevel = moonLights ? ToMoon.w : ToSun.w;
    float phase = CloudPhase(dot(direction, toLight));
    float3 zenith = SkyViewAt(float3(0.0, 1.0, 0.0));
    float3 horizon = SkyViewAt(float3(direction.x, 0.05, direction.z));
    float throughput = 1.0;
    float3 radiance = 0.0;
    [loop]
    for (int s = 0; s < CloudSteps && throughput > 0.01; s++)
    {
        float3 p = origin + direction * (start + (s + noise) * dt);
        float3 c = CloudPosition(p);
        float height = c.y / CloudThickness;
        float density = CloudDensityAt(c, height, (s + noise) * dt < 25.0) * Clouds.y;
        if (density <= 0.0)
        {
            continue;
        }

        float extinction = density * CloudExtinction;
        float depth = CloudLightDepth(p, toLight);
        // Single scattering by Beer's law with the dark sunward edges of a dense cloud (powder), and the light that
        // scatters many times and diffuses through thick cloud (two-stream transmittance, white and nearly isotropic),
        // which keeps an overcast deck's base grey instead of the sky's blue.
        float single = exp(-depth) * lerp(1.0, 1.0 - exp(-depth * 2.0 - 0.1), 0.5) * phase;
        float diffuse = 0.25 / (1.0 + 0.11 * depth);
        float3 sunLight = TransmittanceToSpace(p, toLight) * lightLevel;
        // A rain cloud's denser body keeps the sky's light from its base.
        float3 ambient = lerp(horizon, zenith, height) * (0.5 + 0.5 * height) / Clouds.y;
        // Under a closing sky the clouds light each other: their ambient light turns grey.
        ambient = lerp(ambient, Luminance(ambient).xxx, saturate(Clouds.x));
        float3 scattered = sunLight * (single + diffuse * (0.4 + 0.6 * height) / Clouds.y) + ambient;
        float stepTransmittance = exp(-extinction * dt);
        radiance += throughput * scattered * (1.0 - stepTransmittance);
        throughput *= stepTransmittance;
    }

    // Far clouds fade into the haze between them and the viewer.
    float haze = exp(-start / 45.0);
    float4 current = float4(radiance * haze, lerp(1.0, throughput, haze));
    float4 history = CloudMap.SampleLevel(CloudMapSampler, i.Uv, 0);
    return lerp(history, current, Clouds.z);
}

// The clouds' shadow on the ground: the transmittance toward the light of every texel of CloudShadowArea.x km around
// the camera.
float4 PS_CloudShadow(FullscreenOut i) : SV_Target0
{
    float2 ground = (i.Uv - 0.5) * CloudShadowArea.x;
    float3 p = float3(ground.x, BottomRadius, ground.y);
    float3 toLight = ToSun.xyz.y > 0.05 ? ToSun.xyz : float3(0.0, 1.0, 0.0);
    float enter = SphereExit(p, toLight, BottomRadius + CloudBottom);
    float exit = SphereExit(p, toLight, BottomRadius + CloudBottom + CloudThickness);
    float depth = 0.0;
    const int steps = 10;
    float dt = (exit - enter) / steps;
    [loop]
    for (int s = 0; s < steps; s++)
    {
        float3 q = p + toLight * (enter + (s + 0.5) * dt);
        float3 c = CloudPosition(q);
        depth += CloudDensityAt(c, c.y / CloudThickness, false) * dt;
    }

    return float4(exp(-depth * CloudExtinction * Clouds.y * 0.5), 0.0, 0.0, 1.0);
}

float Hash13(float3 p)
{
    p = frac(p * 0.1031);
    p += dot(p, p.zyx + 31.32);
    return frac((p.x + p.y) * p.z);
}

// Stars: one in a share of the cells of the turning sky, twinkling, brighter few; the Milky Way a faint band.
float3 Stars(float3 direction)
{
    float turn = Night.y;
    float3 d = float3(direction.x * cos(turn) - direction.z * sin(turn), direction.y, direction.x * sin(turn) + direction.z * cos(turn));
    float3 scaled = d * 180.0;
    float3 cell = floor(scaled);
    float h = Hash13(cell);
    float3 stars = 0.0;
    if (h > 0.92)
    {
        float3 centre = cell + float3(Hash13(cell + 1.7), Hash13(cell + 3.1), Hash13(cell + 5.3));
        float distance = length(scaled - centre);
        float brightness = pow((h - 0.92) / 0.08, 6.0) * 6.0 + 0.4;
        float twinkle = 0.75 + 0.25 * sin(Effects.y * (2.0 + h * 5.0) + h * 40.0);
        float3 tint = lerp(float3(0.75, 0.82, 1.0), float3(1.0, 0.88, 0.7), Hash13(cell + 7.9));
        stars = tint * brightness * twinkle * saturate(1.0 - distance * 2.2);
    }

    // The Milky Way along a tilted great circle, mottled by the cloud noise.
    float band = exp(-pow(dot(d, normalize(float3(0.3, 0.45, 0.84))), 2.0) * 40.0);
    float mottle = CloudNoise.SampleLevel(CloudNoiseSampler, d * 1.5, 0).r;
    return (stars + float3(0.55, 0.6, 0.8) * band * mottle * 0.35) * Night.x;
}

// The aurora: thin folded curtains across the sky between the horizon and the zenith, green below and violet at
// their tops, waving slowly. The layers sample the same curtains from a little higher each, so they stand in vertical
// rays.
float3 Aurora(float3 direction)
{
    if (Night.w <= 0.0 || direction.y < 0.05)
    {
        return 0.0;
    }

    float3 colour = 0.0;
    const int layers = 20;
    float t = Effects.y * 0.02;
    [loop]
    for (int l = 0; l < layers; l++)
    {
        float rise = (float)l / layers;
        float2 at = direction.xz / direction.y * (1.0 + rise * 0.15);
        float warp = CloudNoise.SampleLevel(CloudNoiseSampler, float3(at.x * 0.03 + t, 0.21, at.y * 0.03 - t * 0.3), 0).r;
        float stripe = sin(at.x * 8.0 + at.y * 2.5 + warp * 10.0);
        float rays = CloudNoise.SampleLevel(CloudNoiseSampler, float3(at.x * 1.5, 0.45, at.y * 0.2), 0).b;
        float curtain = pow(saturate(stripe), 12.0) * (0.25 + 0.75 * rays);
        float3 hue = lerp(float3(0.15, 1.0, 0.45), float3(0.65, 0.2, 1.0), rise * rise);
        colour += hue * curtain * (1.0 - rise * 0.7);
    }

    float band = smoothstep(0.05, 0.25, direction.y) * smoothstep(0.95, 0.6, direction.y);
    return colour * (3.0 / layers) * Night.w * band;
}

// The rainbow opposite the sun: the primary bow from 40.6 to 42.5 degrees (red outside), the fainter secondary from
// 50.5 to 53.5 (red inside).
float3 Spectrum(float t)
{
    float hue = (1.0 - t) * 0.8;
    return saturate(abs(frac(hue + float3(0.0, 2.0 / 3.0, 1.0 / 3.0)) * 6.0 - 3.0) - 1.0) * sin(PI * saturate(t));
}

float3 Rainbow(float3 direction)
{
    if (Effects.x <= 0.0 || direction.y < 0.0)
    {
        return 0.0;
    }

    float angle = degrees(acos(clamp(dot(direction, -ToSun.xyz), -1.0, 1.0)));
    float3 bow = 0.0;
    if (angle > 40.6 && angle < 42.5)
    {
        bow += Spectrum((angle - 40.6) / 1.9);
    }

    if (angle > 50.5 && angle < 53.5)
    {
        bow += 0.35 * Spectrum(1.0 - (angle - 50.5) / 3.0);
    }

    return bow * Effects.x * saturate(direction.y * 8.0);
}

// Cirrus high above the cumulus: a few patches of fine fibres combed along the wind. A broad pattern places the
// patches and leaves most of the sky clear; fine stretched noise draws the fibres inside them, so they read as hair-thin
// wisps instead of smears. Lit by the hour's light, faint against the sky.
float4 Cirrus(float3 direction, float3 sky)
{
    if (Effects.z <= 0.0 || direction.y < 0.03)
    {
        return float4(0.0, 0.0, 0.0, 1.0);
    }

    float2 at = direction.xz / direction.y * CirrusAltitude + CloudOffset.xy * 2.0;
    float patch = CloudNoise.SampleLevel(CloudNoiseSampler, float3(at.x * 0.012, 0.73, at.y * 0.012), 0).r;
    float placed = saturate((patch - (1.0 - Effects.z * 0.5)) * 4.0);
    if (placed <= 0.0)
    {
        return float4(0.0, 0.0, 0.0, 1.0);
    }

    float fibres = CloudNoise.SampleLevel(CloudNoiseSampler, float3(at.x * 0.03, 0.31, at.y * 0.6), 0).g;
    float fine = CloudNoise.SampleLevel(CloudNoiseSampler, float3(at.x * 0.08, 0.57, at.y * 1.7), 0).b;
    float wisp = placed * saturate((fibres * 0.6 + fine * 0.4 - 0.55) * 5.0) * saturate((direction.y - 0.03) * 8.0);
    float3 light = TransmittanceToSpace(ObserverPosition() + float3(0.0, CirrusAltitude, 0.0), ToSun.xyz) * ToSun.w * 0.04 + sky * 1.2;
    float opacity = wisp * 0.3;
    return float4(light * opacity, 1.0 - opacity);
}

float3 Moon(float3 direction)
{
    float cosine = dot(direction, ToMoon.xyz);
    float edge = cos(MoonRadius);
    if (cosine < edge || MoonFace.w <= 0.0)
    {
        return 0.0;
    }

    // The disc's point as a sphere seen from the viewer, lit by the light of its phase; faint earthshine on the rest.
    float3 up = abs(ToMoon.y) < 0.99 ? float3(0.0, 1.0, 0.0) : float3(1.0, 0.0, 0.0);
    float3 right = normalize(cross(up, ToMoon.xyz));
    float3 top = cross(ToMoon.xyz, right);
    float2 disc = float2(dot(direction, right), dot(direction, top)) / sin(MoonRadius);
    // Local frame: x right, y up, z from the moon toward the viewer.
    float3 normal = float3(disc, sqrt(saturate(1.0 - dot(disc, disc))));
    float3 light = float3(dot(MoonFace.xyz, right), dot(MoonFace.xyz, top), -dot(MoonFace.xyz, ToMoon.xyz));
    float maria = CloudNoise.SampleLevel(CloudNoiseSampler, float3(disc * 0.35, 0.5), 0).g;
    float albedo = lerp(0.55, 1.0, smoothstep(0.35, 0.65, maria));
    float lit = saturate(dot(normal, light)) * albedo + 0.012;
    float rim = saturate((cosine - edge) / (1.0 - edge) * 40.0);
    return MoonFace.w * lit * rim * float3(1.0, 0.97, 0.92);
}

float4 PS_Sky(FullscreenOut i) : SV_Target0
{
    float2 ndc = float2(i.Uv.x * 2.0 - 1.0, 1.0 - i.Uv.y * 2.0);
    float4 world = mul(float4(ndc, 0.5, 1.0), InverseViewProjection);
    float3 direction = normalize(world.xyz / world.w);
    float3 sky = SkyViewAt(direction);
    float above = saturate(direction.y * 30.0 + 0.5);

    float4 clouds = float4(0.0, 0.0, 0.0, 1.0);
    if (CloudShadowArea.w > 0.5 && direction.y > 0.0)
    {
        clouds = CloudMap.SampleLevel(CloudMapSampler, CloudUv(direction), 0);
    }

    float4 cirrus = Cirrus(direction, sky);

    // Behind the clouds: the stars and the Milky Way, the aurora, the moon and the sun's disc. The disc outshines the
    // sky thousands of times, so the share of it a cloud lets through would still clip white: any cloud with a body
    // (and an overcast sky without drawn clouds) hides it, and the light it sends through the cloud is the cloud's own
    // forward scattering.
    float3 space = Stars(direction) + Night.z * (1.0 + 3.0 * pow(1.0 - saturate(direction.y), 4.0)) + Aurora(direction) + Moon(direction);
    float sunCosine = dot(direction, ToSun.xyz);
    if (sunCosine > cos(SunRadius))
    {
        float centre = saturate(1.0 - acos(min(sunCosine, 1.0)) / SunRadius);
        float veiled = smoothstep(0.6, 0.97, clouds.a) * cirrus.a * saturate(1.0 - CloudShadowArea.y * 1.5);
        // Limb darkening.
        space += SunDiscRadiance * ToSun.w * veiled * (0.4 + 0.6 * sqrt(centre)) * TransmittanceToSpace(ObserverPosition(), ToSun.xyz);
    }

    float3 colour = sky + space * TransmittanceToSpace(ObserverPosition(), direction) * above;
    colour = colour * cirrus.a + cirrus.rgb;
    if (CloudShadowArea.w > 0.5 && direction.y > 0.0)
    {
        // A strike lights the clouds from inside, on its side of the sky; it flashes too briefly for their history.
        float flash = exp(-(1.0 - dot(direction, Flash.xyz)) * 6.0) * Flash.w;
        colour = colour * clouds.a + clouds.rgb + flash * (1.0 - clouds.a) * float3(0.85, 0.9, 1.0);
    }

    // Without drawn clouds an overcast sky turns a flat grey of its own light.
    colour = lerp(colour, Luminance(sky).xxx * 0.8, CloudShadowArea.y * above);
    colour += Rainbow(direction) * sky * 1.6;
    // A soft knee above 0.6 keeps the hue and the shading of a bright sky and of sunlit clouds instead of clipping
    // each channel or flattening them into plateaus.
    float3 shown = colour * Effects.w;
    float peak = max(max(shown.r, shown.g), shown.b);
    if (peak > 0.6)
    {
        shown *= (0.6 + 0.4 * (1.0 - exp(-(peak - 0.6) * 1.5))) / peak;
    }

    float3 display = LinearToSrgb(saturate(shown));
    // Dither the 8-bit gradients.
    display += (InterleavedGradientNoise(i.Position.xy) - 0.5) / 255.0;
    return float4(display, 1.0);
}

technique Transmittance { pass P0 { VertexShader = compile vs_6_0 VS_Fullscreen(); PixelShader = compile ps_6_0 PS_Transmittance(); } }
technique Multiple { pass P0 { VertexShader = compile vs_6_0 VS_Fullscreen(); PixelShader = compile ps_6_0 PS_Multiple(); } }
technique SkyView { pass P0 { VertexShader = compile vs_6_0 VS_Fullscreen(); PixelShader = compile ps_6_0 PS_SkyView(); } }
technique Clouds { pass P0 { VertexShader = compile vs_6_0 VS_Fullscreen(); PixelShader = compile ps_6_0 PS_Clouds(); } }
technique CloudShadow { pass P0 { VertexShader = compile vs_6_0 VS_Fullscreen(); PixelShader = compile ps_6_0 PS_CloudShadow(); } }
technique Sky { pass P0 { VertexShader = compile vs_6_0 VS_Fullscreen(); PixelShader = compile ps_6_0 PS_Sky(); } }
