// Skinning functions over BonePalette and PreviousBonePalette (Vertex/SkinningDecl.hlsli): rigid, one bone per vertex
// (the legacy physique), and weighted, four bones blended by their weights (glTF).
#ifndef VERTEX_SKINNING_HLSLI
#define VERTEX_SKINNING_HLSLI

// Row dot products reconstruct (position * boneMatrix).xyz including the translation.
float3 SkinPosition(float4 position, uint bone)
{
    uint b = bone * 3;
    return float3(dot(BonePalette[b], position), dot(BonePalette[b + 1], position), dot(BonePalette[b + 2], position));
}

float3 PreviousSkinPosition(float4 position, uint bone)
{
    uint b = bone * 3;
    return float3(dot(PreviousBonePalette[b], position), dot(PreviousBonePalette[b + 1], position),
                  dot(PreviousBonePalette[b + 2], position));
}

// The rotation part only: the legacy bones have uniform scale, so no inverse transpose is needed.
float3 SkinNormal(float3 normal, uint bone)
{
    uint b = bone * 3;
    return float3(dot(BonePalette[b].xyz, normal), dot(BonePalette[b + 1].xyz, normal), dot(BonePalette[b + 2].xyz, normal));
}

float3 SkinPositionWeighted(float4 position, uint4 bones, float4 weights)
{
    return SkinPosition(position, bones.x) * weights.x + SkinPosition(position, bones.y) * weights.y
        + SkinPosition(position, bones.z) * weights.z + SkinPosition(position, bones.w) * weights.w;
}

float3 PreviousSkinPositionWeighted(float4 position, uint4 bones, float4 weights)
{
    return PreviousSkinPosition(position, bones.x) * weights.x + PreviousSkinPosition(position, bones.y) * weights.y
        + PreviousSkinPosition(position, bones.z) * weights.z + PreviousSkinPosition(position, bones.w) * weights.w;
}

// The blended rotation of a direction (a normal or tangent); the caller renormalizes. Non-uniform bone scale is not
// corrected (no inverse transpose).
float3 SkinNormalWeighted(float3 direction, uint4 bones, float4 weights)
{
    return SkinNormal(direction, bones.x) * weights.x + SkinNormal(direction, bones.y) * weights.y
        + SkinNormal(direction, bones.z) * weights.z + SkinNormal(direction, bones.w) * weights.w;
}

#endif
