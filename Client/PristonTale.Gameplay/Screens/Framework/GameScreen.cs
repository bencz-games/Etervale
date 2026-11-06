using Microsoft.Xna.Framework;
using PristonTale.Assets;
using PristonTale.Gameplay.UI.Rendering;

namespace PristonTale.Gameplay.Screens.Framework;

public abstract class GameScreen : IDisposable
{
    protected GameScreen(ScreenContext context) => Context = context;

    protected ScreenContext Context { get; }

    public virtual string StatusText => string.Empty;

    // The pointer this screen shows after its update.
    public virtual HudCursorKind Cursor => HudCursorKind.Default;

    public virtual void Enter() { }

    public abstract void Update(GameTime gameTime);

    public abstract void Draw();

    public virtual bool HandleEscape() => false;

    public virtual void Dispose() { }

    // Work Dispose started that still needs the connection (leaving the world); shutdown waits for it.
    public virtual Task Closing => Task.CompletedTask;

    protected string AssetPath(string relativePath) => AssetFiles.Combine(Context.AssetRoot, relativePath);
}
