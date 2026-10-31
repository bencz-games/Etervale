using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace PristonTale.Gameplay.UI.Rendering;

public sealed class HudCanvas(SpriteBatch batch, SpriteFont font, HudAtlas atlas)
{
    private readonly record struct TextRun(string Value, Vector2 Position, Color Color, float Scale);
    private readonly List<TextRun> _text = new(128);

    public static readonly Color Gold = new(221, 199, 147);
    public static readonly Color Muted = new(173, 188, 191);

    public void Begin(float scale)
    {
        _text.Clear();
        batch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.LinearClamp,
            DepthStencilState.None, RasterizerState.CullNone, transformMatrix: Matrix.CreateScale(scale));
    }

    public void End()
    {
        FlushText();
        batch.End();
    }

    public void FlushText()
    {
        foreach (var run in _text)
        {
            batch.DrawString(font, run.Value, run.Position + Vector2.One, Color.Black * 0.8f,
                0f, Vector2.Zero, run.Scale, SpriteEffects.None, 0f);
            batch.DrawString(font, run.Value, run.Position, run.Color,
                0f, Vector2.Zero, run.Scale, SpriteEffects.None, 0f);
        }

        _text.Clear();
    }

    public void Image(string name, Rectangle destination, Color? tint = null)
    {
        var region = atlas.Region(name);
        batch.Draw(atlas.Texture(region.Texture), destination, region.Bounds, tint ?? Color.White);
    }

    public void Icon(Texture2D texture, Rectangle bounds, Color? tint = null)
    {
        var scale = Math.Min((float)bounds.Width / texture.Width, (float)bounds.Height / texture.Height);
        var width = (int)(texture.Width * scale);
        var height = (int)(texture.Height * scale);
        var destination = new Rectangle(bounds.X + (bounds.Width - width) / 2, bounds.Y + (bounds.Height - height) / 2, width, height);
        batch.Draw(texture, destination, tint ?? Color.White);
    }

    public void Gauge(string name, Rectangle frame, float fraction)
    {
        fraction = float.IsFinite(fraction) ? Math.Clamp(fraction, 0, 1) : 0;
        var gauge = atlas.Gauge(name);
        var region = gauge.Fill;
        var source = region.Bounds;
        var sourceHeight = (int)(source.Height * fraction);
        if (sourceHeight <= 0)
        {
            return;
        }

        var skippedRows = source.Height - sourceHeight;
        var scale = new Vector2(frame.Width * gauge.Size.X / source.Width, frame.Height * gauge.Size.Y / source.Height);
        var position = new Vector2(frame.X + frame.Width * gauge.Offset.X, frame.Y + frame.Height * gauge.Offset.Y + skippedRows * scale.Y);
        source.Y += skippedRows;
        source.Height = sourceHeight;
        // Use the frame's exact transform: independent integer rounding makes the glass jump as HP changes.
        batch.Draw(atlas.Texture(region.Texture), position, source, Color.White, 0, Vector2.Zero, scale, SpriteEffects.None, 0);
    }

    public void Frame(Rectangle destination, string name = "Panel", int border = 20, Color? tint = null)
    {
        var region = atlas.Region(name);
        var source = region.Bounds;
        var inset = region.Border;
        var edge = Math.Min(border, Math.Min(destination.Width, destination.Height) / 2);
        for (var row = 0; row < 3; row++)
        {
            for (var column = 0; column < 3; column++)
            {
                var from = new Rectangle(Start(source.X, source.Width, inset, column), Start(source.Y, source.Height, inset, row),
                    Length(source.Width, inset, column), Length(source.Height, inset, row));
                var to = new Rectangle(Start(destination.X, destination.Width, edge, column), Start(destination.Y, destination.Height, edge, row),
                    Length(destination.Width, edge, column), Length(destination.Height, edge, row));
                if (to.Width > 0 && to.Height > 0 && from.Width > 0 && from.Height > 0)
                {
                    batch.Draw(atlas.Texture(region.Texture), to, from, tint ?? Color.White);
                }
            }
        }
    }

    public void Bar(Rectangle bounds, long value, long maximum, Color color, string label = "")
    {
        Frame(bounds, "Inset", 4);
        var inner = new Rectangle(bounds.X + 3, bounds.Y + 3, bounds.Width - 6, bounds.Height - 6);
        var fraction = maximum > 0 ? Math.Clamp((double)value / maximum, 0, 1) : 0;
        inner.Width = (int)(inner.Width * fraction);
        if (inner.Width > 0 && inner.Height > 0)
        {
            Image("Fill", inner, color);
        }

        if (label.Length > 0)
        {
            Text(label, bounds, Color.White, 15f, centered: true);
        }
    }

    // Text at an em of at most pixels, shrunk to fit the bounds.
    public void Text(string value, Rectangle bounds, Color? color = null, float pixels = 19f, bool centered = false)
    {
        if (string.IsNullOrEmpty(value) || bounds.Width <= 0 || bounds.Height <= 0)
        {
            return;
        }

        var size = font.MeasureString(value);
        var fitted = Math.Min(pixels / HudPainter.BakePixels, Math.Min(bounds.Width / Math.Max(1, size.X), bounds.Height / Math.Max(1, size.Y)));
        var x = centered ? bounds.X + (bounds.Width - size.X * fitted) / 2 : bounds.X;
        var y = bounds.Y + (bounds.Height - size.Y * fitted) / 2;
        _text.Add(new TextRun(value, new Vector2(x, y), color ?? Gold, fitted));
    }

    private static int Start(int position, int length, int edge, int part)
    {
        return part switch
        {
            0 => position,
            1 => position + edge,
            _ => position + length - edge
        };
    }

    private static int Length(int length, int edge, int part) => part == 1 ? length - edge * 2 : edge;
}
