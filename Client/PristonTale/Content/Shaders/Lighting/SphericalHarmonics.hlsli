// Real spherical harmonics, bands 0-2, and the irradiance they give once cosine-convolved.
#ifndef LIGHTING_SPHERICAL_HARMONICS_HLSLI
#define LIGHTING_SPHERICAL_HARMONICS_HLSLI

void ShBasis(float3 d, out float b[9])
{
    b[0] = 0.282095;
    b[1] = 0.488603 * d.y;
    b[2] = 0.488603 * d.z;
    b[3] = 0.488603 * d.x;
    b[4] = 1.092548 * d.x * d.y;
    b[5] = 1.092548 * d.y * d.z;
    b[6] = 0.315392 * (3.0 * d.z * d.z - 1.0);
    b[7] = 1.092548 * d.x * d.z;
    b[8] = 0.546274 * (d.x * d.x - d.y * d.y);
}

// Cosine-lobe convolution per band, divided by pi (A0 = pi, A1 = 2 pi / 3, A2 = pi / 4).
static const float BandScale[9] = { 1.0, 2.0 / 3.0, 2.0 / 3.0, 2.0 / 3.0, 0.25, 0.25, 0.25, 0.25, 0.25 };

// Irradiance over pi toward n from radiance coefficients c.
float3 EvaluateSh(float3 c[9], float3 n)
{
    float b[9];
    ShBasis(n, b);
    float3 e = 0.0;
    [unroll]
    for (uint k = 0; k < 9; k++)
    {
        e += c[k] * (b[k] * BandScale[k]);
    }

    return e;
}

#endif
