using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using PristonTale.Contracts.Units;

namespace PristonTale.Gameplay.UI.Bellatra;

// The effects of Bellatra's table, by their index in it.
public enum BellatraEffect
{
    Loading = 1,
    CloseScreen = 2,
    OpenScreen = 3,
    Round = 4,
    Count1 = 5,
    Start = 13,
    Stage = 14,
    Complete = 15,
    Fail = 16,
    Congratulations = 17,
    QuakeSeal = 18,
    QuakeSealName = 25
}

// The running Bellatra effects: each effect once at a time, its tracks stepped at 70 frames a second and drawn over the
// screen in the original's 800x600 space, scaled evenly and centred over black bars when a screen image as large as that
// space shows (its black is see-through). An effect with a text (TextOf) shows it over its tracks' images, while a key
// has one. An effect ends when all its tracks have, or when it is stopped.
public sealed class BellatraEffects
{
    private const int DesignWidth = HudLayout.DesignWidth;
    private const int DesignHeight = HudLayout.DesignHeight;
    private const string Directory = "image/Bellatra/";

    // The table's screen images and textures by their index in its keys; no effect draws screen image 1.
    private static readonly string[] ScreenImages =
    [
        .. new[]
        {
            "BL_loding.bmp", null, "BL_loding_00.bmp", "BL_loding_01.bmp", "BL_loding_02.bmp", "BL_loding_03.bmp", "BL_loding_04.bmp",
            "F_change01.bmp", "F_change02.bmp", "F_change03.bmp"
        }.Select(static name => name is null ? null : Directory + name)
    ];

    // The textures end at the seals' emblems: the name effects of the quake, stun, freeze and rabbit seals name textures
    // 21 to 24, but the images in those places (effect_item08 to 11: a sword, dark glasses, a figure, armour) are not those
    // seals' art and would cover their emblems, so those names show as text like the others.
    private static readonly string[] Textures =
    [
        .. new[]
        {
            "T_Round_316_65.tga", "T_1_37_64.tga", "T_2_54_64.tga", "T_3_53_64.tga", "T_4_56_64.tga", "T_5_55_64.tga", "T_6_55_64.tga",
            "T_7_51_64.tga", "T_Start_278_65.tga", "T_S-Stage_280_65.tga", "T_S-complete_440_65.tga", "T_S-fail_180_65.tga", "T_Con_754_65.tga",
            "T_8_56_64.tga", "effect_item01.tga", "effect_item02.tga", "effect_item03.tga", "effect_item04.tga", "effect_item05.tga",
            "effect_item06.tga", "effect_item07.tga"
        }.Select(static name => Directory + name)
    ];

    public static readonly string[] Art = [.. ScreenImages.Where(static path => path is not null), .. Textures];

    private sealed class Playing(BellatraEffect effect, BellatraLayer layer, BellatraTrack track)
    {
        public BellatraEffect Effect { get; } = effect;
        public BellatraLayer Layer { get; } = layer;
        public BellatraTrack Track { get; } = track;
        public int Key { get; set; }
        public int Time { get; set; }
        public Vector2 Offset { get; set; }
        public Vector2 OffsetStep { get; set; }
        public Vector2 Growth { get; set; }
        public Vector2 GrowthStep { get; set; }
        public float Angle { get; set; }
        public float AngleStep { get; set; }
        public float Alpha { get; set; }
        public float AlphaStep { get; set; }
        public bool Holding => Track.Keys[Key].Delay == BellatraKey.Hold;
    }

    private readonly BellatraEffectTable _table;
    private readonly List<Playing> _running = [];
    private double _carryMs;

    public BellatraEffects(BellatraEffectTable table) => _table = table;

    // The text an effect shows over its tracks; set by the HUD.
    public Func<BellatraEffect, string> TextOf { get; set; }

    public bool Running => _running.Count > 0;

    public bool IsRunning(BellatraEffect effect)
    {
        foreach (var running in _running)
        {
            if (running.Effect == effect)
            {
                return true;
            }
        }

        return false;
    }

    // Whether every track of the effect waits on a held key.
    public bool IsHolding(BellatraEffect effect)
    {
        var any = false;
        foreach (var running in _running)
        {
            if (running.Effect == effect)
            {
                if (!running.Holding)
                {
                    return false;
                }

                any = true;
            }
        }

        return any;
    }

    // Starts an effect not already running (false for a running one or one the table lacks).
    public bool Start(BellatraEffect effect)
    {
        var layers = _table.Layers(effect);
        if (layers.Count == 0 || IsRunning(effect))
        {
            return false;
        }

        foreach (var layer in layers)
        {
            var running = new Playing(effect, layer, _table.Track(layer.Track));
            Begin(running);
            _running.Add(running);
        }

        return true;
    }

    public void Stop(BellatraEffect effect) => _running.RemoveAll(running => running.Effect == effect);

    public void Clear() => _running.Clear();

    public void Advance(double elapsedSeconds)
    {
        _carryMs += elapsedSeconds * 1000;
        while (_carryMs >= GameUnits.TickMs)
        {
            _carryMs -= GameUnits.TickMs;
            for (var index = _running.Count - 1; index >= 0; index--)
            {
                if (!Step(_running[index]))
                {
                    _running.RemoveAt(index);
                }
            }
        }

        if (_running.Count == 0)
        {
            _carryMs = 0;
        }
    }

    // The original's 800x600 space on the screen: scaled evenly and centred.
    public static (float Scale, Vector2 Offset) Space(Viewport viewport)
    {
        var scale = MathF.Min(viewport.Width / (float)DesignWidth, viewport.Height / (float)DesignHeight);
        return (scale, new Vector2((viewport.Width - DesignWidth * scale) / 2f, (viewport.Height - DesignHeight * scale) / 2f));
    }

    public void Draw(SpriteBatch batch, Func<string, Texture2D> texture, Texture2D pixel, SpriteFont font, float textScale)
    {
        if (_running.Count == 0)
        {
            return;
        }

        var viewport = batch.GraphicsDevice.Viewport;
        var (scale, offset) = Space(viewport);
        foreach (var running in _running)
        {
            if (running.Track.Kind == BellatraImageKind.Screen && running.Layer.Size.X >= DesignWidth && running.Layer.Size.Y >= DesignHeight)
            {
                batch.Draw(pixel, viewport.Bounds with { X = 0, Y = 0 }, Color.Black);
                break;
            }
        }

        for (var layer = 2; layer >= 0; layer--)
        {
            foreach (var running in _running)
            {
                if (running.Layer.Layer == layer)
                {
                    DrawTrack(batch, running, texture, font, textScale, scale, offset);
                }
            }
        }
    }

    private void DrawTrack(SpriteBatch batch, Playing running, Func<string, Texture2D> texture, SpriteFont font, float textScale, float scale, Vector2 offset)
    {
        var image = running.Track.Keys[running.Key].Image;
        var at = offset + (new Vector2(running.Layer.Origin.X, running.Layer.Origin.Y) + new Vector2(MathF.Truncate(running.Offset.X), MathF.Truncate(running.Offset.Y))) * scale;
        switch (running.Track.Kind)
        {
            case BellatraImageKind.Screen when image >= 0 && image < ScreenImages.Length && ScreenImages[image] is { } path && texture(path) is { } screen:
            {
                var source = new Rectangle(0, 0, Math.Min(running.Layer.Size.X, screen.Width), Math.Min(running.Layer.Size.Y, screen.Height));
                batch.Draw(screen, at, source, Color.White, 0f, Vector2.Zero, scale, SpriteEffects.None, 0f);
                break;
            }
            case BellatraImageKind.Texture when image >= 0 && image < Textures.Length && texture(Textures[image]) is { } art:
            {
                var half = new Vector2(running.Layer.Size.X, running.Layer.Size.Y) + running.Growth;
                if (half.X <= 0 || half.Y <= 0)
                {
                    break;
                }

                var alpha = Math.Clamp(running.Alpha / 255f, 0f, 1f);
                batch.Draw(art, at, null, Color.White * alpha, MathHelper.ToRadians(running.Angle), new Vector2(art.Width / 2f, art.Height / 2f),
                    new Vector2(2f * half.X / art.Width, 2f * half.Y / art.Height) * scale, SpriteEffects.None, 0f);
                break;
            }
        }

        if (TextOf?.Invoke(running.Effect) is { Length: > 0 } text && (running.Track.Kind == BellatraImageKind.Text || image >= 0))
        {
            DrawText(batch, text, running.Alpha, font, textScale, scale, offset);
        }
    }

    // An effect's text: centred at TextTop in the original's yellow, twice the HUD's size.
    private static void DrawText(SpriteBatch batch, string text, float trackAlpha, SpriteFont font, float textScale, float scale, Vector2 offset)
    {
        var alpha = Math.Clamp(trackAlpha / 255f, 0f, 1f);
        var size = textScale * TextSize;
        var width = font.MeasureString(text).X * size;
        var position = offset + new Vector2(DesignWidth / 2f - width / 2f, TextTop) * scale;
        batch.DrawString(font, text, position + new Vector2(2, 2) * scale, Color.Black * alpha, 0f, Vector2.Zero, size * scale, SpriteEffects.None, 0f);
        batch.DrawString(font, text, position, TextColor * alpha, 0f, Vector2.Zero, size * scale, SpriteEffects.None, 0f);
    }

    private const float TextTop = 350f;
    private const float TextSize = 2f;
    private static readonly Color TextColor = new(255, 230, 0);

    private static void Begin(Playing running)
    {
        running.Key = 0;
        running.Time = 0;
        running.Offset = Vector2.Zero;
        running.Growth = Vector2.Zero;
        running.Angle = 0f;
        var first = running.Track.Keys[0];
        running.Alpha = first.Alpha;
        running.AlphaStep = 0f;
        if (first.Delay > 0)
        {
            running.OffsetStep = first.Offset / first.Delay;
            running.GrowthStep = first.Growth / first.Delay;
            running.AngleStep = first.Angle / first.Delay;
        }
        else
        {
            running.OffsetStep = running.GrowthStep = Vector2.Zero;
            running.AngleStep = 0f;
        }
    }

    // One frame of a track; false once it has passed its last key.
    private static bool Step(Playing running)
    {
        var keys = running.Track.Keys;
        running.Time++;
        var delay = keys[running.Key].Delay;
        if (running.Time <= delay)
        {
            running.Offset += running.OffsetStep;
            running.Growth += running.GrowthStep;
            running.Angle += running.AngleStep;
            running.Alpha += running.AlphaStep;
            return true;
        }

        if (delay == BellatraKey.Hold)
        {
            running.Time--;
            return true;
        }

        if (delay == BellatraKey.Loop)
        {
            Begin(running);
            return true;
        }

        running.Key++;
        if (running.Key >= keys.Length)
        {
            return false;
        }

        running.Time = 0;
        var from = keys[running.Key - 1];
        var to = keys[running.Key];
        (running.Offset, running.Growth, running.Angle, running.Alpha) = (from.Offset, from.Growth, from.Angle, from.Alpha);
        if (to.Delay > 0)
        {
            running.OffsetStep = (to.Offset - from.Offset) / to.Delay;
            running.GrowthStep = (to.Growth - from.Growth) / to.Delay;
            running.AngleStep = (to.Angle - from.Angle) / to.Delay;
            running.AlphaStep = (to.Alpha - from.Alpha) / (float)to.Delay;
        }
        else
        {
            running.OffsetStep = running.GrowthStep = Vector2.Zero;
            running.AngleStep = running.AlphaStep = 0f;
        }

        return true;
    }
}
