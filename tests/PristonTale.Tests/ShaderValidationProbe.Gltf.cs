using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Graphics.PackedVector;
using PristonTale.Engine3D.Formats.Gltf;
using PristonTale.Engine3D.Rendering;
using PristonTale.Engine3D.Rendering.Gltf;

namespace PristonTale.Tests;

// The GLTF variant of Surface.fx: four weighted bones, a base colour read through KHR_texture_transform, its wraps and
// either UV set, and the lit base surface with a flat normal map matching the legacy lit reference.
internal sealed partial class ShaderValidationProbe
{
    private void VerifyGltf()
    {
        var palette = new PristonSkinPalette(2);
        palette.Matrices[0] = Matrix.CreateTranslation(5f, 0f, 0f);
        palette.Matrices[1] = Matrix.CreateTranslation(-5f, 0f, 0f);
        palette.Pack();
        _shaders.ApplyFrame(Frame(ambient: 0f, sun: 0f));
        using (var white = GltfMaterial(new GltfMaterial("White") { Unlit = true }, []))
        {
            DrawGltf(white, palette, new Vector4(0.5f, 0.5f, 0f, 0f), Vector2.Zero, Vector2.Zero);
            Read(Vector3.One, "glTF weighted skinning blends two bones");
            DrawGltf(white, palette, Vector4.UnitX, Vector2.Zero, Vector2.Zero);
            Read(new Vector3(ClearValue), "glTF skinning follows a single weighted bone");
        }

        // White on the left of the texture, black on the right; texel centres at u 0.25 and 0.75.
        DecodedPristonImage[] halves = [new(2, 1, [Color.White, Color.Black], false)];
        var cases = new (string Scenario, GltfTextureRef Read, Vector2 Uv0, Vector2 Uv1, float Expected)[]
        {
            ("glTF base colour at its UVs", Map(), new(0.25f, 0.5f), Vector2.Zero, 1f),
            ("glTF texture offset", Map() with { Offset = new Vector2(0.5f, 0f) }, new(0.25f, 0.5f), Vector2.Zero, 0f),
            ("glTF texture rotation turns (1, 0) towards (0, -1)", Map() with { Rotation = MathHelper.PiOver2 }, new(0.1f, 0.25f), Vector2.Zero, 1f),
            ("glTF clamp wrap holds the edge texel", Map() with { WrapS = GltfWrap.Clamp }, new(-0.4f, 0.5f), Vector2.Zero, 1f),
            ("glTF mirror wrap folds back", Map() with { WrapS = GltfWrap.Mirror }, new(1.25f, 0.5f), Vector2.Zero, 0f),
            ("glTF second UV set", Map() with { TexCoord = 1 }, new(0.75f, 0.5f), new(0.25f, 0.5f), 1f)
        };
        var still = new PristonSkinPalette(1);
        still.Matrices[0] = Matrix.Identity;
        still.Pack();
        foreach (var (scenario, read, uv0, uv1, expected) in cases)
        {
            using var material = GltfMaterial(new GltfMaterial("Halves") { Unlit = true, BaseColorTexture = read }, halves);
            DrawGltf(material, still, Vector4.UnitX, uv0, uv1);
            Read(new Vector3(expected), scenario);
        }

        DecodedPristonImage[] flat = [new(1, 1, [new Color(128, 128, 255)], false)];
        foreach (var (ambient, sun) in new[] { (0.5f, 0f), (0f, 1f), (0.3f, 0.8f) })
        {
            _shaders.ApplyFrame(Frame(ambient, sun));
            using var material = GltfMaterial(new GltfMaterial("Lit")
            {
                BaseColor = new Vector4(Grey, Grey, Grey, 1f),
                Metallic = 0f,
                Roughness = 0.6f,
                NormalTexture = Map()
            }, flat);
            DrawGltf(material, still, Vector4.UnitX, new Vector2(0.5f, 0.5f), Vector2.Zero);
            Read(new Vector3(LitReference(Grey, ambient, sun, sunVisibility: 1f, roughness: 0.6f)),
                $"glTF lit surface with a flat normal map ambient={ambient} sun={sun}");
        }

        static GltfTextureRef Map() => GltfTextureRef.None with { Image = 0 };
    }

    private GltfMaterialRuntime GltfMaterial(GltfMaterial material, DecodedPristonImage[] images) =>
        GltfMaterialRuntime.Create(GraphicsDevice, GltfMaterialPacking.Pack(material, index => images[index]), _white);

    // The probe's quad (clip space under the identity frame), every vertex on bones 0 and 1 with the same UVs.
    private void DrawGltf(GltfMaterialRuntime material, PristonSkinPalette palette, Vector4 weights, Vector2 uv0, Vector2 uv1)
    {
        var effect = _shaders.SelectGltfSurface(material, palette, Vector3.Zero);
        GraphicsDevice.SetRenderTarget(_target);
        GraphicsDevice.Clear(new Color(new Vector4(ClearValue)));
        GraphicsDevice.BlendState = BlendState.Opaque;
        GraphicsDevice.DepthStencilState = DepthStencilState.None;
        GraphicsDevice.RasterizerState = RasterizerState.CullNone;
        effect.CurrentTechnique.Passes[0].Apply();
        var vertices = Quad.Select(position => new GltfGpuVertex
        {
            Position = position,
            Normal = Vector3.Up,
            Color = Vector4.One,
            Uv0 = uv0,
            Uv1 = uv1,
            Tangent = new Vector4(1f, 0f, 0f, 1f),
            Bones = new Byte4(0, 1, 0, 0),
            Weights = weights
        }).ToArray();
        GraphicsDevice.DrawUserPrimitives(PrimitiveType.TriangleList, vertices, 0, vertices.Length / 3);
    }
}
