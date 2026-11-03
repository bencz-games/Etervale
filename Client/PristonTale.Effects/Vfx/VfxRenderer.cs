using System.Runtime.InteropServices;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Content;
using Microsoft.Xna.Framework.Graphics;
using PristonTale.Engine3D.Rendering;

namespace PristonTale.Effects.Vfx;

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct VfxVertex : IVertexType
{
    public Vector3 Position;
    public Vector4 Color;
    public Vector2 TexCoord;
    public Vector2 Params;

    public static readonly VertexDeclaration Declaration = new(
        new VertexElement(0, VertexElementFormat.Vector3, VertexElementUsage.Position, 0),
        new VertexElement(12, VertexElementFormat.Vector4, VertexElementUsage.Color, 0),
        new VertexElement(28, VertexElementFormat.Vector2, VertexElementUsage.TextureCoordinate, 0),
        new VertexElement(36, VertexElementFormat.Vector2, VertexElementUsage.TextureCoordinate, 1));

    readonly VertexDeclaration IVertexType.VertexDeclaration => Declaration;
}

// Draws every VFX instance into the bound HDR scene target: quads in one growing vertex array, uploaded once per frame
// to a dynamic buffer with a shared quad index buffer, drawn in runs of (blend, texture). Alpha-blended layers draw
// first, back to front by instance, then the darkening (shadow) layers, then the order-independent additive light.
// Effect models draw before the quads through their own mesh draws. Depth is tested, not written.
public sealed partial class VfxRenderer : IDisposable
{
    private const int MaxQuads = 16384;

    private readonly GraphicsDevice _device;
    private readonly PristonVfxEffect _effect;
    private readonly IndexBuffer _indices;
    private readonly List<Run> _runs = new(64);
    private readonly List<(float Distance, int Index)> _order = new(64);
    private readonly List<GroundQuad> _ground = new(64);
    private readonly List<Billboard> _billboards = new(256);
    private readonly List<LooseModel> _looseModels = new(16);
    private readonly BlendState _shadow = new()
    {
        ColorSourceBlend = Blend.Zero,
        ColorDestinationBlend = Blend.SourceColor,
        AlphaSourceBlend = Blend.Zero,
        AlphaDestinationBlend = Blend.One
    };
    private readonly BlendState _inverseShadow = new()
    {
        ColorSourceBlend = Blend.Zero,
        ColorDestinationBlend = Blend.InverseSourceColor,
        AlphaSourceBlend = Blend.Zero,
        AlphaDestinationBlend = Blend.One
    };
    private readonly BlendState _added = new()
    {
        ColorSourceBlend = Blend.One,
        ColorDestinationBlend = Blend.One,
        AlphaSourceBlend = Blend.Zero,
        AlphaDestinationBlend = Blend.One
    };
    private DynamicVertexBuffer? _vertices;
    private VfxVertex[] _scratch = new VfxVertex[4 * 256];
    private int _quadCount;

    public VfxRenderer(GraphicsDevice device, ContentManager content)
    {
        _device = device ?? throw new ArgumentNullException(nameof(device));
        _effect = new PristonVfxEffect(content);
        var indices = new int[MaxQuads * 6];
        for (var quad = 0; quad < MaxQuads; quad++)
        {
            var vertex = quad * 4;
            var index = quad * 6;
            indices[index] = vertex;
            indices[index + 1] = vertex + 1;
            indices[index + 2] = vertex + 2;
            indices[index + 3] = vertex;
            indices[index + 4] = vertex + 2;
            indices[index + 5] = vertex + 3;
        }

        _indices = new IndexBuffer(device, IndexElementSize.ThirtyTwoBits, indices.Length, BufferUsage.WriteOnly);
        _indices.SetData(indices);
    }

    public bool DisplayOutput
    {
        get => _effect.DisplayOutput;
        set => _effect.DisplayOutput = value;
    }

    public int LastQuadCount { get; private set; }

    public int LastDrawCalls { get; private set; }

    // A flat quad drawn once with the next draw's effects in its blend's pass (blob shadows): its centre, the half extents
    // of its two sides (u along the first, v along the second) and its colour.
    public void AddGroundQuad(Texture2D texture, VfxBlend blend, Vector3 center, Vector3 halfU, Vector3 halfV, Vector4 color) =>
        _ground.Add(new GroundQuad(texture, blend, center, halfU, halfV, color));

    // A camera-facing square drawn once with the next draw's effects in its blend's pass: its centre, half size and
    // colour (0..255 sRGB channels, alpha 0..255).
    public void AddBillboard(Texture2D texture, VfxBlend blend, Vector3 center, float halfSize, Vector4 color) =>
        _billboards.Add(new Billboard(texture, blend, center, halfSize, Decode(color)));

    // A model drawn once with the next draw's effects (a thrown weapon); world places its +Z front.
    public void AddModel(PristonMeshEffect model, Matrix world, VfxBlend blend) => _looseModels.Add(new LooseModel(model, world, blend));

    // Effect models face the opposite way of effect space (their front is +Z): this turns one to face it.
    private static readonly Matrix ModelFront = Matrix.CreateRotationY(MathHelper.Pi);

    // A model's blend; models draw colour-weighted blends additively, quads keep their own (QuadBlend).
    private BlendState ModelBlend(VfxBlend blend) => blend switch
    {
        VfxBlend.Alpha => BlendState.NonPremultiplied,
        VfxBlend.Shadow => _shadow,
        VfxBlend.InvShadow => _inverseShadow,
        _ => BlendState.Additive
    };

    private BlendState QuadBlend(VfxBlend blend) => blend is VfxBlend.Color or VfxBlend.AddColor ? _added : ModelBlend(blend);

    public void Draw(VfxSystem system, Matrix view, Matrix projection, Vector3 cameraPosition, Texture2D? sceneDepth)
    {
        DrawModels(system, view, projection);
        foreach (var loose in _looseModels)
        {
            loose.Model.Draw(0, ModelFront * loose.World, view, projection, Vector4.One, ModelBlend(loose.Blend));
            LastModelCount++;
        }

        _looseModels.Clear();
        _quadCount = 0;
        _runs.Clear();
        var right = new Vector3(view.M11, view.M21, view.M31);
        var up = new Vector3(view.M12, view.M22, view.M32);
        _viewForward = -new Vector3(view.M13, view.M23, view.M33);
        _worldPerPixel = projection.M22 > 0f ? 1f / (VfxScreenSlide.ViewPixels * 0.5f * projection.M22) : 0f;
        var instances = system.Instances;
        _order.Clear();
        for (var index = 0; index < instances.Count; index++)
        {
            _order.Add((Vector3.DistanceSquared(instances[index].CasterPosition, cameraPosition), index));
        }

        _order.Sort(static (a, b) => b.Distance.CompareTo(a.Distance));
        _inverseView = Matrix.Invert(view);
        for (var pass = 0; pass < 3; pass++)
        {
            foreach (var quad in _ground)
            {
                if (PassOf(quad.Blend) == pass)
                {
                    AppendGround(quad);
                }
            }

            foreach (var billboard in _billboards)
            {
                if (PassOf(billboard.Blend) == pass)
                {
                    AppendBillboard(billboard, right, up);
                }
            }

            foreach (var (_, index) in _order)
            {
                AppendInstance(instances[index], pass, right, up, cameraPosition);
            }
        }

        _ground.Clear();
        _billboards.Clear();
        LastQuadCount = _quadCount;
        LastDrawCalls = 0;
        if (_quadCount == 0)
        {
            return;
        }

        if (_vertices is null || _vertices.VertexCount < _quadCount * 4)
        {
            _vertices?.Dispose();
            var capacity = Math.Min(MaxQuads, Math.Max(1024, _quadCount * 2)) * 4;
            _vertices = new DynamicVertexBuffer(_device, VfxVertex.Declaration, capacity, BufferUsage.WriteOnly);
        }

        _vertices.SetData(_scratch, 0, _quadCount * 4, SetDataOptions.Discard);
        _device.SetVertexBuffer(_vertices);
        _device.Indices = _indices;
        _device.DepthStencilState = PristonDepth.Read;
        _device.RasterizerState = RasterizerState.CullNone;
        _effect.ViewProjection = view * projection;
        _effect.Depth = sceneDepth;
        foreach (var run in _runs)
        {
            _device.BlendState = QuadBlend(run.Blend);
            _effect.Mode = run.Blend switch
            {
                VfxBlend.Shadow => PristonVfxEffect.MultiplyMode,
                VfxBlend.InvShadow => PristonVfxEffect.InverseMultiplyMode,
                VfxBlend.Color or VfxBlend.AddColor => PristonVfxEffect.SquaredMode,
                _ => PristonVfxEffect.RadianceMode
            };
            _effect.Texture = run.Texture;
            _effect.Apply();
            _device.DrawIndexedPrimitives(PrimitiveType.TriangleList, 0, run.FirstQuad * 6, run.QuadCount * 2);
            LastDrawCalls++;
        }

        _device.SetVertexBuffer(null);
    }

    // Pass 0: alpha; 1: darkening; 2: additive light.
    internal static int PassOf(VfxBlend blend) => blend switch
    {
        VfxBlend.Alpha => 0,
        VfxBlend.Shadow or VfxBlend.InvShadow => 1,
        _ => 2
    };

    private void AppendInstance(VfxInstance instance, int pass, Vector3 right, Vector3 up, Vector3 camera)
    {
        AppendComponents(instance, pass, right, up, camera);
        AppendSequences(instance, pass, right, up, camera);
        AppendParts(instance, pass, right, up, camera);
        foreach (var state in instance.EmitterStates)
        {
            var emitter = state.Emitter;
            var source = emitter.Source;
            if (PassOf(source.Blend) != pass || state.Count == 0)
            {
                continue;
            }

            var frames = source.FrameColumns * source.FrameRows;
            for (var index = 0; index < state.Count; index++)
            {
                ref readonly var particle = ref state.Particles[index];
                var life = particle.Age / particle.Lifetime;
                var size = emitter.Size.Sample(life).X;
                var color = emitter.Color.Sample(life);
                var tint = new Vector4(color.X * source.Intensity, color.Y * source.Intensity, color.Z * source.Intensity, color.W);
                var frame = frames > 1 ? Math.Min(frames - 1, (int)(life * frames)) : 0;
                AppendParticle(particle, source, emitter.Texture, size, tint, frame, right, up, camera);
            }
        }

        foreach (var trail in instance.TrailStates)
        {
            if (PassOf(trail.Trail.Source.Blend) == pass && trail.Count >= 2)
            {
                AppendTrail(trail, camera);
            }
        }
    }

    private void AppendParticle(in VfxParticle particle, VfxEmitterDefinition source, Texture2D texture, float size, Vector4 tint, int frame, Vector3 right,
        Vector3 up, Vector3 camera)
    {
        Vector3 axisX;
        Vector3 axisY;
        switch (source.Align)
        {
            case VfxAlign.Ground:
                axisX = Vector3.Right;
                axisY = Vector3.Forward;
                break;
            case VfxAlign.Velocity when particle.Velocity.LengthSquared() > 1e-4f:
                var speed = particle.Velocity.Length();
                axisY = particle.Velocity / speed;
                var toCamera = Vector3.Normalize(camera - particle.Position);
                axisX = Vector3.Cross(axisY, toCamera);
                axisX = axisX.LengthSquared() > 1e-6f ? Vector3.Normalize(axisX) : right;
                axisY *= 1f + speed * source.Stretch;
                break;
            default:
                axisX = right;
                axisY = up;
                break;
        }

        if (particle.Rotation != 0f && source.Align != VfxAlign.Velocity)
        {
            var cos = MathF.Cos(particle.Rotation);
            var sin = MathF.Sin(particle.Rotation);
            var rotatedX = axisX * cos + axisY * sin;
            axisY = axisY * cos - axisX * sin;
            axisX = rotatedX;
        }

        var x = axisX * size;
        var y = axisY * size;
        var columns = source.FrameColumns;
        var u0 = (frame % columns) / (float)columns;
        var v0 = (frame / columns) / (float)source.FrameRows;
        var du = 1f / columns;
        var dv = 1f / source.FrameRows;
        var p = particle.Position;
        if (!BeginQuad(texture, source.Blend, out var at))
        {
            return;
        }

        var parameters = new Vector2(source.Softness, 0f);
        _scratch[at] = new VfxVertex { Position = p - x + y, Color = tint, TexCoord = new Vector2(u0, v0), Params = parameters };
        _scratch[at + 1] = new VfxVertex { Position = p + x + y, Color = tint, TexCoord = new Vector2(u0 + du, v0), Params = parameters };
        _scratch[at + 2] = new VfxVertex { Position = p + x - y, Color = tint, TexCoord = new Vector2(u0 + du, v0 + dv), Params = parameters };
        _scratch[at + 3] = new VfxVertex { Position = p - x - y, Color = tint, TexCoord = new Vector2(u0, v0 + dv), Params = parameters };
    }

    private void AppendGround(in GroundQuad quad)
    {
        if (!BeginQuad(quad.Texture, quad.Blend, out var at))
        {
            return;
        }

        var parameters = Vector2.Zero;
        var p = quad.Center;
        _scratch[at] = new VfxVertex { Position = p - quad.HalfU - quad.HalfV, Color = quad.Color, TexCoord = new Vector2(0f, 0f), Params = parameters };
        _scratch[at + 1] = new VfxVertex { Position = p + quad.HalfU - quad.HalfV, Color = quad.Color, TexCoord = new Vector2(1f, 0f), Params = parameters };
        _scratch[at + 2] = new VfxVertex { Position = p + quad.HalfU + quad.HalfV, Color = quad.Color, TexCoord = new Vector2(1f, 1f), Params = parameters };
        _scratch[at + 3] = new VfxVertex { Position = p - quad.HalfU + quad.HalfV, Color = quad.Color, TexCoord = new Vector2(0f, 1f), Params = parameters };
    }

    private void AppendBillboard(in Billboard billboard, Vector3 right, Vector3 up)
    {
        if (!BeginQuad(billboard.Texture, billboard.Blend, out var at))
        {
            return;
        }

        var x = right * billboard.HalfSize;
        var y = up * billboard.HalfSize;
        var p = billboard.Center;
        var parameters = Vector2.Zero;
        _scratch[at] = new VfxVertex { Position = p - x + y, Color = billboard.Color, TexCoord = new Vector2(0f, 0f), Params = parameters };
        _scratch[at + 1] = new VfxVertex { Position = p + x + y, Color = billboard.Color, TexCoord = new Vector2(1f, 0f), Params = parameters };
        _scratch[at + 2] = new VfxVertex { Position = p + x - y, Color = billboard.Color, TexCoord = new Vector2(1f, 1f), Params = parameters };
        _scratch[at + 3] = new VfxVertex { Position = p - x - y, Color = billboard.Color, TexCoord = new Vector2(0f, 1f), Params = parameters };
    }

    private readonly record struct Billboard(Texture2D Texture, VfxBlend Blend, Vector3 Center, float HalfSize, Vector4 Color);

    private readonly record struct LooseModel(PristonMeshEffect Model, Matrix World, VfxBlend Blend);

    private readonly record struct GroundQuad(Texture2D Texture, VfxBlend Blend, Vector3 Center, Vector3 HalfU, Vector3 HalfV, Vector4 Color);

    // A camera-facing ribbon through the trail's points, newest first.
    private void AppendTrail(TrailState trail, Vector3 camera)
    {
        var source = trail.Trail.Source;
        var length = trail.Points.Length;
        for (var segment = 0; segment < trail.Count - 1; segment++)
        {
            var a = trail.Points[(trail.Head - segment + length) % length];
            var b = trail.Points[(trail.Head - segment - 1 + length) % length];
            var along = b - a;
            if (along.LengthSquared() < 1e-4f)
            {
                continue;
            }

            var side = Vector3.Cross(along, camera - a);
            if (side.LengthSquared() < 1e-6f)
            {
                continue;
            }

            side = Vector3.Normalize(side) * (source.Width * 0.5f);
            var t0 = segment / (float)(trail.Count - 1);
            var t1 = (segment + 1) / (float)(trail.Count - 1);
            var c0 = trail.Trail.Color.Sample(t0);
            var c1 = trail.Trail.Color.Sample(t1);
            c0 = new Vector4(c0.X * source.Intensity, c0.Y * source.Intensity, c0.Z * source.Intensity, c0.W);
            c1 = new Vector4(c1.X * source.Intensity, c1.Y * source.Intensity, c1.Z * source.Intensity, c1.W);
            if (!BeginQuad(trail.Trail.Texture, source.Blend, out var at))
            {
                return;
            }

            var parameters = new Vector2(VfxSystem.StripSoftness, 0f);
            _scratch[at] = new VfxVertex { Position = a + side, Color = c0, TexCoord = new Vector2(t0, 0f), Params = parameters };
            _scratch[at + 1] = new VfxVertex { Position = b + side, Color = c1, TexCoord = new Vector2(t1, 0f), Params = parameters };
            _scratch[at + 2] = new VfxVertex { Position = b - side, Color = c1, TexCoord = new Vector2(t1, 1f), Params = parameters };
            _scratch[at + 3] = new VfxVertex { Position = a - side, Color = c0, TexCoord = new Vector2(t0, 1f), Params = parameters };
        }
    }

    private bool BeginQuad(Texture2D texture, VfxBlend blend, out int at)
    {
        at = 0;
        if (_quadCount >= MaxQuads)
        {
            return false;
        }

        if (_scratch.Length < (_quadCount + 1) * 4)
        {
            Array.Resize(ref _scratch, Math.Min(MaxQuads * 4, _scratch.Length * 2));
        }

        if (_runs.Count > 0 && _runs[^1].Texture == texture && _runs[^1].Blend == blend)
        {
            var last = _runs[^1];
            _runs[^1] = last with { QuadCount = last.QuadCount + 1 };
        }
        else
        {
            _runs.Add(new Run(texture, blend, _quadCount, 1));
        }

        at = _quadCount * 4;
        _quadCount++;
        return true;
    }

    public void Dispose()
    {
        _shadow.Dispose();
        _inverseShadow.Dispose();
        _added.Dispose();
        _vertices?.Dispose();
        _indices.Dispose();
        _effect.Dispose();
    }

    private readonly record struct Run(Texture2D Texture, VfxBlend Blend, int FirstQuad, int QuadCount);
}
