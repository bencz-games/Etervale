using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Content;
using Microsoft.Xna.Framework.Graphics;
using PristonTale.Engine3D.Diagnostics;

namespace PristonTale.Engine3D.Rendering;

internal sealed class GlowRenderer : IDisposable
{
    // Corona size is per-light, scaled from the light's own radius (a torch with a small radius gets a small glow, a big
    // street lamp a larger one) and clamped, so dense torch-lit dungeons read as flames, not overlapping blobs.
    private const float GlowRadiusFactor = 0.30f;
    private const float GlowMinSize = 34f;
    private const float GlowMaxSize = 130f;
    // Additive corona alpha (× lamp factor): below 1 so a cluster of torches doesn't blow out to white.
    private const float GlowAlpha = 0.7f;

    private readonly GraphicsDevice _graphicsDevice;
    private readonly PristonUnlitEffect _effect;
    private readonly Texture2D _texture;
    private VertexPositionColorTexture[] _vertices = [];

    public GlowRenderer(GraphicsDevice graphicsDevice, ContentManager content, IClientLog log)
    {
        _graphicsDevice = graphicsDevice;
        _texture = CreateGlowTexture(graphicsDevice);
        _effect = new PristonUnlitEffect(content, log) { Texture = _texture };
    }

    public void Draw(PristonFrameContext frame, IReadOnlyList<PristonPointLight> glowLights, float lampFactor)
    {
        if (!PristonDayNightCycle.LampsLit(lampFactor) || glowLights.Count == 0)
            return;

        var view = frame.View;
        var rightDir = Vector3.Normalize(new Vector3(view.M11, view.M21, view.M31));
        var upDir = Vector3.Normalize(new Vector3(view.M12, view.M22, view.M32));

        var needed = glowLights.Count * 6;
        if (_vertices.Length < needed)
            _vertices = new VertexPositionColorTexture[needed];

        var baseAlpha = lampFactor * GlowAlpha;
        var v = 0;
        for (var i = 0; i < glowLights.Count; i++)
        {
            var l = glowLights[i];
            var size = MathHelper.Clamp(l.Radius * GlowRadiusFactor, GlowMinSize, GlowMaxSize);
            var right = rightDir * size;
            var up = upDir * size;
            var alpha = MathHelper.Clamp(baseAlpha * l.Intensity, 0f, 1f);
            var col = new Color(l.Color.X, l.Color.Y, l.Color.Z, alpha);
            var p = l.Position;
            var tl = new VertexPositionColorTexture(p - right + up, col, new Vector2(0f, 0f));
            var tr = new VertexPositionColorTexture(p + right + up, col, new Vector2(1f, 0f));
            var br = new VertexPositionColorTexture(p + right - up, col, new Vector2(1f, 1f));
            var bl = new VertexPositionColorTexture(p - right - up, col, new Vector2(0f, 1f));
            _vertices[v++] = tl; _vertices[v++] = tr; _vertices[v++] = br;
            _vertices[v++] = tl; _vertices[v++] = br; _vertices[v++] = bl;
        }

        _effect.View = frame.View;
        _effect.Projection = frame.Projection;

        _graphicsDevice.BlendState = BlendState.Additive;
        // Depth-tested, so walls and trees in front of a lamp hide its glow, without depth writes: an additive billboard.
        _graphicsDevice.DepthStencilState = PristonDepth.Read;
        _graphicsDevice.RasterizerState = RasterizerState.CullNone;

        _effect.Apply();
        _graphicsDevice.DrawUserPrimitives(PrimitiveType.TriangleList, _vertices, 0, glowLights.Count * 2);
    }

    private static Texture2D CreateGlowTexture(GraphicsDevice graphicsDevice)
    {
        const int size = 64;
        var data = new Color[size * size];
        var c = (size - 1) * 0.5f;
        for (var y = 0; y < size; y++)
            for (var x = 0; x < size; x++)
            {
                var d = new Vector2((x - c) / c, (y - c) / c).Length();
                var a = MathHelper.Clamp(1f - d, 0f, 1f);
                a = a * a;
                data[y * size + x] = new Color(1f, 1f, 1f, a);
            }
        var texture = new Texture2D(graphicsDevice, size, size, false, SurfaceFormat.ColorSRgb);
        texture.SetData(data);
        return texture;
    }

    public void Dispose()
    {
        _effect.Dispose();
        _texture.Dispose();
    }
}
