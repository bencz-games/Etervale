using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace PristonTale.Gameplay.UI.Rendering;

// Draws original sprites in 800x600 logical units at an integer scale with point sampling.
// Text is queued and drawn in a linear-filtered pass because the UI font is a scaled vector bake.
public sealed class HudPainter : IDisposable
{
    // The original used 14-16 px Comic Sans through GDI TextOut, whose y is the top of a cell with ~2 px leading.
    public const float TextScale = 0.7f;
    public const int LineHeight = 16;
    public const float CapTop = 2f;

    private readonly record struct TextRun(string Value, Vector2 Position, Color Color, bool Shadow, Vector2 ShadowOffset, float Scale);

    private readonly SpriteBatch _batch;
    private readonly SpriteFont _font;
    private HashSet<char> _glyphs;
    private readonly HudSprites _sprites;
    private readonly Texture2D _white;
    private readonly Texture2D _nameBox;
    private readonly Texture2D _nameBoxHighlighted;
    private readonly List<TextRun> _text = new(128);
    private readonly float _textShift;
    private readonly float _capCropping;
    private (float Capitals, float Descent)? _letterHeights;
    private Matrix _transform = Matrix.Identity;
    private bool _active;

    public HudPainter(SpriteBatch batch, SpriteFont font, HudSprites sprites)
    {
        _batch = batch;
        _font = font;
        _sprites = sprites;
        _white = new Texture2D(batch.GraphicsDevice, 1, 1);
        _white.SetData([Color.White]);
        _nameBox = NameBoxTexture(batch.GraphicsDevice, new Color(170, 136, 65), new Color(98, 84, 48));
        _nameBoxHighlighted = NameBoxTexture(batch.GraphicsDevice, new Color(236, 200, 110), new Color(150, 120, 60));
        _capCropping = font.GetGlyphs().TryGetValue('M', out var glyph) ? glyph.Cropping.Y : CapTop / TextScale;
        _textShift = MathF.Round(CapTop - _capCropping * TextScale);
    }

    public HudSprites Sprites => _sprites;

    public void Begin(HudLayout layout)
    {
        _text.Clear();
        _transform = Matrix.CreateScale(Math.Max(1, layout.Scale));
        BeginSprites();
    }

    public void End()
    {
        if (!_active)
        {
            return;
        }

        Flush();
        _batch.End();
        _active = false;
    }

    public void Flush()
    {
        if (_text.Count == 0 || !_active)
        {
            return;
        }

        _batch.End();
        _batch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.LinearClamp, DepthStencilState.None,
            RasterizerState.CullNone, transformMatrix: _transform);
        foreach (var run in _text)
        {
            if (run.Shadow)
            {
                _batch.DrawString(_font, run.Value, run.Position + run.ShadowOffset, Color.Black, 0f, Vector2.Zero, run.Scale, SpriteEffects.None, 0f);
            }

            _batch.DrawString(_font, run.Value, run.Position, run.Color, 0f, Vector2.Zero, run.Scale, SpriteEffects.None, 0f);
        }

        _text.Clear();
        _batch.End();
        BeginSprites();
    }

    public Texture2D Texture(string path) => _sprites.Get(path);

    public void Sprite(string path, int x, int y, Color? tint = null)
    {
        var texture = _sprites.Get(path);
        if (texture is not null)
        {
            _batch.Draw(texture, new Vector2(x, y), tint ?? Color.White);
        }
    }

    public void Sprite(string path, int x, int y, Rectangle source, Color? tint = null)
    {
        var texture = _sprites.Get(path);
        if (texture is null)
        {
            return;
        }

        source = Rectangle.Intersect(source, texture.Bounds);
        if (source.Width > 0 && source.Height > 0)
        {
            _batch.Draw(texture, new Vector2(x, y), source, tint ?? Color.White);
        }
    }

    public void Stretch(string path, Rectangle destination, Rectangle? source = null, Color? tint = null)
    {
        var texture = _sprites.Get(path);
        if (texture is not null && destination.Width > 0 && destination.Height > 0)
        {
            _batch.Draw(texture, destination, source, tint ?? Color.White);
        }
    }

    public void Rotated(string path, Vector2 center, float rotation, Color? tint = null)
    {
        var texture = _sprites.Get(path);
        if (texture is not null)
        {
            var origin = new Vector2(texture.Width / 2f, texture.Height / 2f);
            _batch.Draw(texture, center, null, tint ?? Color.White, rotation, origin, 0.5f, SpriteEffects.None, 0f);
        }
    }

    // Premultiplied alpha blending with a zero alpha tint adds the sprite's colour: the original specular highlight.
    public void Highlight(string path, Rectangle destination, int amount)
    {
        if (amount > 0)
        {
            Stretch(path, destination, null, new Color(amount, amount, amount, 0));
        }
    }

    public void Image(Texture2D texture, Rectangle destination, Color? tint = null)
    {
        if (texture is not null && destination.Width > 0 && destination.Height > 0)
        {
            _batch.Draw(texture, destination, tint ?? Color.White);
        }
    }

    public void Fill(Rectangle bounds, Color premultiplied)
    {
        if (bounds.Width > 0 && bounds.Height > 0)
        {
            _batch.Draw(_white, bounds, premultiplied);
        }
    }

    // Original colour box fills are straight RGBA.
    public void ColorBox(Rectangle bounds, int r, int g, int b, int a) => Fill(bounds, Color.FromNonPremultiplied(r, g, b, a));

    public void Text(string value, int x, int y, Color? color = null, bool shadow = true) => Text(value, x, y, color ?? Color.White, shadow, Point.Zero);

    // A zero shadow offset is the usual one pixel down and right.
    public void Text(string value, int x, int y, Color color, bool shadow, Point shadowOffset)
    {
        if (!string.IsNullOrEmpty(value))
        {
            var offset = shadowOffset == Point.Zero ? Vector2.One : shadowOffset.ToVector2();
            _text.Add(new TextRun(value, new Vector2(x, y + _textShift), color, shadow, offset, TextScale));
        }
    }

    // Text at its own scale of the UI font (smaller labels over the scene), its y the top of the capitals as for Text.
    public void Text(string value, int x, int y, Color color, float scale)
    {
        if (!string.IsNullOrEmpty(value))
        {
            _text.Add(new TextRun(value, new Vector2(x, y + MathF.Round(CapTop - _capCropping * scale)), color, false, Vector2.One, scale));
        }
    }

    public void TextCentered(string value, int left, int right, int y, Color? color = null, bool shadow = true) =>
        TextCentered(value, left, right, y, color ?? Color.White, shadow, Point.Zero);

    public void TextCentered(string value, int left, int right, int y, Color color, bool shadow, Point shadowOffset)
    {
        if (!string.IsNullOrEmpty(value))
        {
            Text(value, left + (right - left - Measure(value)) / 2, y, color, shadow, shadowOffset);
        }
    }

    // The height of one line of HUD text.
    public int TextHeight => (int)MathF.Ceiling(_font.LineSpacing * TextScale);

    public bool CanDraw(char value) => (_glyphs ??= [.. _font.Characters]).Contains(value);

    public int Measure(string value) => MeasureAt(value, TextScale);

    public int MeasureAt(string value, float scale) => string.IsNullOrEmpty(value) ? 0 : (int)MathF.Ceiling(_font.MeasureString(value).X * scale);

    public int TextHeightAt(float scale) => (int)MathF.Ceiling(_font.LineSpacing * scale);

    // The capitals' height and the descenders' depth below the baseline at a scale, in pixels: text drawn at y has the top
    // of its capitals at y + CapTop. The line spacing also holds the gap to the next line, so a box sized by it leaves
    // the text high.
    public (float Capitals, float Descent) LetterHeightsAt(float scale)
    {
        if (_letterHeights is null)
        {
            var glyphs = _font.GetGlyphs();
            var baseline = glyphs.TryGetValue('M', out var capital) ? capital.Cropping.Y + capital.BoundsInTexture.Height : _font.LineSpacing;
            var descent = 0f;
            foreach (var character in "gjpqy")
            {
                if (glyphs.TryGetValue(character, out var glyph))
                {
                    descent = MathF.Max(descent, glyph.Cropping.Y + glyph.BoundsInTexture.Height - baseline);
                }
            }

            _letterHeights = (baseline - _capCropping, descent);
        }

        return (_letterHeights.Value.Capitals * scale, _letterHeights.Value.Descent * scale);
    }

    // One line of a name box at a scale: the capitals centred, with room for descenders plus padY on both sides. Pad is
    // the room above the capitals; text for the line goes at its top + Pad - CapTop.
    public (int Line, int Pad) NameBoxLineAt(float scale, int padY)
    {
        var (capitals, descent) = LetterHeightsAt(scale);
        var pad = (int)MathF.Ceiling(descent) + padY;
        return ((int)MathF.Ceiling(capitals) + pad * 2, pad);
    }

    public string Fit(string value, int width)
    {
        if (Measure(value) <= width)
        {
            return value;
        }

        // The longest prefix that fits with the ellipsis; a prefix's width grows with its length, so bisect it.
        var fitted = string.Empty;
        for (int low = 1, high = value.Length - 1; low <= high;)
        {
            var length = (low + high) / 2;
            var candidate = value[..length] + "...";
            if (Measure(candidate) <= width)
            {
                fitted = candidate;
                low = length + 1;
            }
            else
            {
                high = length - 1;
            }
        }

        return fitted;
    }

    // Item info box: nine 16x16 tiles repeated over a box measured in whole tiles.
    public void InfoBox(Rectangle bounds)
    {
        var columns = Math.Max(2, (bounds.Width + 15) / 16);
        var rows = Math.Max(2, (bounds.Height + 15) / 16);
        for (var column = 0; column < columns; column++)
        {
            for (var row = 0; row < rows; row++)
            {
                var horizontal = column == 0 ? 0 : column == columns - 1 ? 2 : 1;
                var vertical = row == 0 ? 0 : row == rows - 1 ? 2 : 1;
                Sprite(HudArt.InfoBox[vertical * 3 + horizontal], bounds.X + column * 16, bounds.Y + row * 16);
            }
        }
    }

    public static Rectangle InfoBoxBounds(int x, int y, int width, int height) =>
        new(x, y, Math.Max(2, (width + 15) / 16) * 16, Math.Max(2, (height + 15) / 16) * 16);

    // Help box: 64-pixel corners/sides, 32-pixel repeated edges and centre, Box_Line separator.
    public void HelpBox(int x, int y, int sizeW, int sizeH, bool separator = true)
    {
        sizeW = Math.Max(2, sizeW);
        sizeH = Math.Max(2, sizeH);
        for (var j = 0; j < sizeW - 2; j++)
        {
            for (var k = 0; k < sizeH - 2; k++)
            {
                Sprite(HudArt.HelpCenter, x + 64 + 32 * j, y + 32 + 32 * k);
            }
        }

        var right = x + (sizeW - 1) * 32 + 32;
        var bottom = y + (sizeH - 1) * 32;
        Sprite(HudArt.HelpTopLeft, x, y);
        Sprite(HudArt.HelpTopRight, right, y);
        Sprite(HudArt.HelpBottomLeft, x, bottom);
        Sprite(HudArt.HelpBottomRight, right, bottom);
        for (var j = 1; j < sizeW - 1; j++)
        {
            Sprite(HudArt.HelpTop, x + j * 32 + 32, y);
            Sprite(HudArt.HelpBottom, x + j * 32 + 32, bottom);
        }

        for (var k = 1; k < sizeH - 1; k++)
        {
            Sprite(HudArt.HelpLeft, x, y + k * 32);
            Sprite(HudArt.HelpRight, right, y + k * 32);
        }

        if (separator)
        {
            Stretch(HudArt.HelpLine, new Rectangle(x + 8, y + 48, sizeW * 32 + 50, 10));
        }
    }

    // The help boxes' scroll bar: an 8-pixel bar of the given length and its knob, which travels the length less
    // ScrollKnobSlack as position goes from 0 to range.
    public const int ScrollKnobSlack = 20;

    public void ScrollBar(int x, int y, int length, int position, int range)
    {
        Stretch(HudArt.ScrollBar, new Rectangle(x, y, 8, length));
        Sprite(HudArt.ScrollButton, x - 4, y + (length - ScrollKnobSlack) * position / Math.Max(1, range));
    }

    // The position a click at pointerY on a scroll bar from y of the given length asks for.
    public static int ScrollPosition(int y, int length, int pointerY, int range) =>
        Math.Clamp((int)Math.Round((pointerY - y) * range / (double)(length - ScrollKnobSlack)), 0, Math.Max(0, range));

    // Guide box (the helper pet's and the level guide's): 32-pixel corners, 16-pixel edge and fill tiles, columns of 16
    // pixels between the corners and rows 13 pixels apart (the tiles overlap).
    public const int GuideCorner = 32;
    public const int GuideTile = 16;
    public const int GuideRowStep = 13;

    public void GuideBox(int x, int y, int columns, int rows)
    {
        var right = GuideBoxRight(x, columns);
        var bottom = GuideBoxBottom(y, rows);
        Stretch(HudArt.GuideBox[0], new Rectangle(x, y, GuideCorner, GuideCorner));
        Stretch(HudArt.GuideBox[2], new Rectangle(right, y, GuideCorner, GuideCorner));
        for (var row = 0; row < rows; row++)
        {
            var rowY = y + GuideCorner + row * GuideRowStep;
            Stretch(HudArt.GuideBox[3], new Rectangle(x, rowY, GuideCorner, GuideTile));
            Stretch(HudArt.GuideBox[5], new Rectangle(right, rowY, GuideCorner, GuideTile));
            for (var column = 0; column < columns; column++)
            {
                Stretch(HudArt.GuideBox[4], new Rectangle(x + GuideCorner + column * GuideTile, rowY, GuideTile, GuideTile));
            }
        }

        Stretch(HudArt.GuideBox[6], new Rectangle(x, bottom, GuideCorner, GuideCorner));
        Stretch(HudArt.GuideBox[8], new Rectangle(right, bottom, GuideCorner, GuideCorner));
        for (var column = 0; column < columns; column++)
        {
            Stretch(HudArt.GuideBox[1], new Rectangle(x + GuideCorner + column * GuideTile, y, GuideTile, GuideCorner));
            Stretch(HudArt.GuideBox[7], new Rectangle(x + GuideCorner + column * GuideTile, bottom, GuideTile, GuideCorner));
        }
    }

    // The left of the guide box's right corners, and the top of its bottom edge.
    public static int GuideBoxRight(int x, int columns) => x + GuideCorner + GuideTile * columns;

    public static int GuideBoxBottom(int y, int rows) => y + GuideCorner - 2 + rows * GuideRowStep;

    public static Rectangle HelpBoxBounds(int x, int y, int sizeW, int sizeH) => new(x, y, Math.Max(2, sizeW) * 32 + 64, Math.Max(2, sizeH) * 32 + 32);

    // The box behind a name over the scene (monsters, NPCs, drops): the speech bubble's dark fill and gold rim with
    // rounded corners. Nine slices of a 20-pixel texture: fixed corners, stretched edges and centre.
    public void NameBox(Rectangle bounds, bool highlighted = false)
    {
        const int corner = NameBoxCorner;
        if (bounds.Width < corner * 2 || bounds.Height < corner * 2)
        {
            return;
        }

        var texture = highlighted ? _nameBoxHighlighted : _nameBox;
        Span<int> destinationX = [bounds.X, bounds.X + corner, bounds.Right - corner, bounds.Right];
        Span<int> destinationY = [bounds.Y, bounds.Y + corner, bounds.Bottom - corner, bounds.Bottom];
        Span<int> sourceX = [0, corner, NameBoxSize - corner, NameBoxSize];
        for (var row = 0; row < 3; row++)
        {
            for (var column = 0; column < 3; column++)
            {
                var destination = new Rectangle(destinationX[column], destinationY[row],
                    destinationX[column + 1] - destinationX[column], destinationY[row + 1] - destinationY[row]);
                var source = new Rectangle(sourceX[column], sourceX[row], sourceX[column + 1] - sourceX[column], sourceX[row + 1] - sourceX[row]);
                if (destination.Width > 0 && destination.Height > 0)
                {
                    _batch.Draw(texture, destination, source, Color.White);
                }
            }
        }
    }

    // The name box's content sits under a 4-pixel top border and over a 2-pixel bottom one.
    public const int NameBoxTop = 4;
    public const int NameBoxBorders = 6;
    private const int NameBoxSize = 20;
    private const int NameBoxCorner = 8;
    private const float NameBoxRadius = 7f;

    // A rounded rectangle filling the texture: a soft dark shadow outside the rim, the rim's light and dark gold, then
    // the translucent fill, measured from the edge with a signed distance so the corners are smooth at any scale step.
    private static Texture2D NameBoxTexture(GraphicsDevice device, Color rim, Color innerRim)
    {
        var shadow = new Color(0, 0, 0, 150);
        var fill = new Color(11, 11, 11, 190);
        var pixels = new Color[NameBoxSize * NameBoxSize];
        var half = NameBoxSize / 2f;
        for (var y = 0; y < NameBoxSize; y++)
        {
            for (var x = 0; x < NameBoxSize; x++)
            {
                var qx = MathF.Max(MathF.Abs(x + 0.5f - half) - (half - NameBoxRadius), 0f);
                var qy = MathF.Max(MathF.Abs(y + 0.5f - half) - (half - NameBoxRadius), 0f);
                var depth = NameBoxRadius - MathF.Sqrt(qx * qx + qy * qy);
                var color = depth < 1f ? shadow : depth < 2f ? rim : depth < 3f ? innerRim : fill;
                var coverage = Math.Clamp(depth + 0.5f, 0f, 1f);
                pixels[y * NameBoxSize + x] = Color.FromNonPremultiplied(color.R, color.G, color.B, (int)(color.A * coverage));
            }
        }

        var texture = new Texture2D(device, NameBoxSize, NameBoxSize, false, SurfaceFormat.Color);
        texture.SetData(pixels);
        return texture;
    }

    private void BeginSprites()
    {
        _batch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.PointClamp, DepthStencilState.None,
            RasterizerState.CullNone, transformMatrix: _transform);
        _active = true;
    }

    public void Dispose()
    {
        _white.Dispose();
        _nameBox.Dispose();
        _nameBoxHighlighted.Dispose();
    }
}
