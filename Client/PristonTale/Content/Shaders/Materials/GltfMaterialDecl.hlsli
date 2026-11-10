// Declarations of the glTF material (Materials/GltfMaterial.hlsli), the GLTF variant of Surface.fx: GLTF_MATERIAL_CONSTANTS
// inside the pixel cbuffer after MATERIAL_CONSTANTS, GLTF_MATERIAL_RESOURCES at file scope. The maps are packed at load
// time (PristonTale.Engine3D.Rendering.Gltf.GltfMaterialPacking): t0 base colour (MATERIAL_RESOURCES), t1 normals (the
// LayerMap of MATERIAL_RESOURCES: RG the surface's XY, BA the clearcoat's), t11 surface (R occlusion, G roughness,
// B metallic, A clearcoat), t12 emissive (A clearcoat roughness), t13 sheen (A roughness), t14 diffuse transmission
// (A the share). Colour maps are sRGB textures; their alpha is data. t15 holds a gameplay overlay (GltfOverlayParams).
#ifndef MATERIALS_GLTF_MATERIAL_DECL_HLSLI
#define MATERIALS_GLTF_MATERIAL_DECL_HLSLI

// How each packed texture is read, in GltfPackedTexture order (0 base colour, 1 normals, 2 surface, 3 emissive, 4 sheen,
// 5 transmission), two rows per texture: [2k] the UV transform's 2x2 part (m11, m12, m21, m22), [2k + 1]
// its offset (xy), the UV set (z: 0, 1, or -1 when the material has no such texture) and the wraps (w: S + 3 T, each
// 0 repeat, 1 clamp, 2 mirror).
#define GLTF_TEXTURE_COUNT 6
#define GLTF_MATERIAL_CONSTANTS \
    float4 GltfTextureReads[GLTF_TEXTURE_COUNT * 2];     float4 GltfTextureSizes[GLTF_TEXTURE_COUNT]; /* per texture xy its size in texels */ \
    float4 GltfEmission;        /* rgb emissive factor, a occlusion strength */ \
    float4 GltfNormalScales;    /* x normal scale, y clearcoat normal scale, z clearcoat, w clearcoat roughness */ \
    float4 GltfSheen;           /* rgb sheen colour, a sheen roughness */ \
    float4 GltfTransmission;    /* rgb diffuse transmission colour, a diffuse transmission */ \
    float4 GltfOverlayParams;   /* x 1 = the overlay adds over the base colour, y its U scroll on the first UV set */

#define GLTF_MAP(name, slot) \
    Texture2D<float4> name##Map : register(t##slot); \
    SamplerState name##Sampler : register(s##slot) = sampler_state \
    { \
        Texture = (name##Map); MinFilter = Anisotropic; MagFilter = Linear; MipFilter = Linear; MaxAnisotropy = 16; \
        AddressU = Wrap; AddressV = Wrap; \
    };

#define GLTF_MATERIAL_RESOURCES \
    GLTF_MAP(GltfSurface, 11) \
    GLTF_MAP(GltfEmissive, 12) \
    GLTF_MAP(GltfSheen, 13) \
    GLTF_MAP(GltfTransmission, 14) \
    GLTF_MAP(GltfOverlay, 15)

#endif
