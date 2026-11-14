using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace PristonTale.Engine3D.Rendering;

// M1.5.0: GPU instance data stream. Not wired to any renderer pass until
// the matching instanced glTF shader is built and verified (M1.5.1).
// Four float4 attributes form one row-major world matrix; the glTF vertex
// stream already occupies TEXCOORD0/1, leaving TEXCOORD2..5 for each instance.
// This backend is deliberately independent of actor/skinning and stage state.
internal struct PristonForestInstanceVertex : IVertexType
{
    public static readonly VertexDeclaration Declaration = new(
        new VertexElement(0, VertexElementFormat.Vector4, VertexElementUsage.TextureCoordinate, 2),
        new VertexElement(16, VertexElementFormat.Vector4, VertexElementUsage.TextureCoordinate, 3),
        new VertexElement(32, VertexElementFormat.Vector4, VertexElementUsage.TextureCoordinate, 4),
        new VertexElement(48, VertexElementFormat.Vector4, VertexElementUsage.TextureCoordinate, 5));

    public Vector4 WorldRow0;
    public Vector4 WorldRow1;
    public Vector4 WorldRow2;
    public Vector4 WorldRow3;

    public PristonForestInstanceVertex(Matrix world)
    {
        WorldRow0 = new Vector4(world.M11, world.M12, world.M13, world.M14);
        WorldRow1 = new Vector4(world.M21, world.M22, world.M23, world.M24);
        WorldRow2 = new Vector4(world.M31, world.M32, world.M33, world.M34);
        WorldRow3 = new Vector4(world.M41, world.M42, world.M43, world.M44);
    }

    VertexDeclaration IVertexType.VertexDeclaration => Declaration;
}

// Build/upload a single per-instance stream for one LOD. The draw is one
// indexed instanced invocation, not an actor loop. Instanced EffectPass must
// consume TEXCOORD2..5 and apply the supplied world transform in its VS.
// Caller must restore/invalidate renderer's vertex-binding state cache after
// use; M1.5.0 does not invoke Draw or change any game rendering.
internal sealed class PristonForestInstanceStream : IDisposable
{
    private readonly GraphicsDevice _device;
    private DynamicVertexBuffer _instances;
    private PristonForestInstanceVertex[] _staging = Array.Empty<PristonForestInstanceVertex>();
    private int _capacity;
    private bool _disposed;

    public PristonForestInstanceStream(GraphicsDevice device)
    {
        _device = device ?? throw new ArgumentNullException(nameof(device));
    }

    public int Count { get; private set; }

    public void Upload(ReadOnlySpan<Matrix> worlds)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        Count = 0;
        if (worlds.IsEmpty)
            return;

        if (worlds.Length > _capacity)
        {
            var newCapacity = Math.Max(16, _capacity);
            while (newCapacity < worlds.Length)
                newCapacity = checked(newCapacity * 2);
            var replacement = new DynamicVertexBuffer(
                _device, PristonForestInstanceVertex.Declaration, newCapacity, BufferUsage.WriteOnly);
            _instances?.Dispose();
            _instances = replacement;
            _staging = new PristonForestInstanceVertex[newCapacity];
            _capacity = newCapacity;
        }

        for (var i = 0; i < worlds.Length; i++)
            _staging[i] = new PristonForestInstanceVertex(worlds[i]);
        _instances.SetData(_staging, 0, worlds.Length, SetDataOptions.Discard);
        Count = worlds.Length;
    }

    // This method is an API/compilation proof only in M1.5.0. M1.5.1 will
    // provide the EffectPass and submit the draw at the correct scene phase.
    public bool Draw(VertexBuffer mesh, IndexBuffer indices, EffectPass instancedPass, int primitiveCount)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (Count == 0)
            return false;
        ArgumentNullException.ThrowIfNull(mesh);
        ArgumentNullException.ThrowIfNull(indices);
        ArgumentNullException.ThrowIfNull(instancedPass);
        if (primitiveCount <= 0)
            throw new ArgumentOutOfRangeException(nameof(primitiveCount));
        _device.SetVertexBuffers(
            new VertexBufferBinding(mesh, 0, 0),
            new VertexBufferBinding(_instances, 0, 1));
        _device.Indices = indices;
        instancedPass.Apply();
        _device.DrawInstancedPrimitives(PrimitiveType.TriangleList, 0, 0, primitiveCount, Count);
        return true;
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        Count = 0;
        _instances?.Dispose();
        _instances = null;
        _staging = Array.Empty<PristonForestInstanceVertex>();
        _capacity = 0;
    }
}
