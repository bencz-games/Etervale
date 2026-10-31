using Microsoft.Xna.Framework.Graphics;

namespace PristonTale.Engine3D.Rendering;

// The render targets bound before a pass takes the device, put back after it without allocating. The device copies
// exactly as many bindings as are bound, so one array is kept for every count.
public sealed class PristonBoundTargets
{
    private const int MostTargets = 8;
    private readonly RenderTargetBinding[][] _byCount;
    private RenderTargetBinding[] _saved = [];

    public PristonBoundTargets()
    {
        _byCount = new RenderTargetBinding[MostTargets + 1][];
        for (var count = 0; count < _byCount.Length; count++)
        {
            _byCount[count] = new RenderTargetBinding[count];
        }
    }

    // The first saved target, or null for the back buffer.
    public Texture2D First => _saved.Length > 0 ? _saved[0].RenderTarget as Texture2D : null;

    public void Save(GraphicsDevice device)
    {
        _saved = _byCount[device.RenderTargetCount];
        if (_saved.Length > 0)
        {
            device.GetRenderTargets(_saved);
        }
    }

    // Binds the saved targets again (the back buffer if none); may be called more than once per Save.
    public void Restore(GraphicsDevice device)
    {
        if (_saved.Length == 0)
        {
            device.SetRenderTarget(null);
        }
        else
        {
            device.SetRenderTargets(_saved);
        }
    }

    // Drops the saved targets so they are not kept alive.
    public void Forget()
    {
        Array.Clear(_saved);
        _saved = _byCount[0];
    }
}
