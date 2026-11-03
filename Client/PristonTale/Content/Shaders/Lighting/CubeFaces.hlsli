// The six faces of a cube view as Lighting/PristonCubeFaces.cs builds them (Matrix.CreateLookAt along FaceForward with
// up FaceUp, a 90-degree square perspective): +X, -X, +Y, -Y, +Z, -Z. Needs SafeNormalize (Core/Common.hlsli).
#ifndef LIGHTING_CUBE_FACES_HLSLI
#define LIGHTING_CUBE_FACES_HLSLI

static const float3 FaceForward[6] = { float3(1, 0, 0), float3(-1, 0, 0), float3(0, 1, 0), float3(0, -1, 0), float3(0, 0, 1), float3(0, 0, -1) };
static const float3 FaceUp[6] = { float3(0, 1, 0), float3(0, 1, 0), float3(0, 0, -1), float3(0, 0, 1), float3(0, 1, 0), float3(0, 1, 0) };

// The face of d's major axis.
uint CubeFace(float3 d)
{
    float3 a = abs(d);
    return a.x >= a.y && a.x >= a.z ? (d.x >= 0.0 ? 0 : 1) : a.y >= a.z ? (d.y >= 0.0 ? 2 : 3) : (d.z >= 0.0 ? 4 : 5);
}

// Where d lands on that face, (0, 0) at its top left and (1, 1) at its bottom right; the distance along the face's axis
// is kept at minAlong or more.
float2 CubeFaceUv(float3 d, uint face, float minAlong)
{
    float3 forward = FaceForward[face];
    float3 back = -forward;
    float3 right = SafeNormalize(cross(FaceUp[face], back));
    float3 up = cross(back, right);
    float along = max(dot(forward, d), minAlong);
    float2 ndc = float2(dot(right, d), dot(up, d)) / along;
    return float2(ndc.x, -ndc.y) * 0.5 + 0.5;
}

#endif
