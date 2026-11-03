using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using PristonTale.Simulation.Characters;
using PristonTale.Gameplay.UI;

namespace PristonTale.Gameplay.Screens.CharacterSelect;

internal static class CharSelectLayout
{
    public const int FacesPerJob = CharacterFace.FaceCount;

    internal enum ButtonKind
    {
        Select,
        Back,
        NewCharacter,
        Exit,
        Delete
    }

    public static int ButtonRowY(Viewport vp) => vp.Height - 40;
    public static int TwoButtonPrimaryX(Viewport vp) => vp.Width / 2 - 120;
    public static int TwoButtonQuitX(Viewport vp) => vp.Width / 2 - 120 + 110;
    public const int TwoButtonDeleteX = 0;
    public static int ThreeButtonOkX(Viewport vp) => vp.Width / 2 - 150;
    public static int ThreeButtonBackX(Viewport vp) => vp.Width / 2 - 150 + 100;
    public static int ThreeButtonQuitX(Viewport vp) => vp.Width / 2 - 150 + 200;

    public static Rectangle NewCharPrompt(Viewport vp) => new(vp.Width / 2 - 128, 150, 256, 64);
    public static Rectangle NewCharHotspot(Viewport vp) => new(vp.Width / 2 - 128, vp.Height / 4 - 80, 256, 210);

    public static Rectangle TempscronMark(Viewport vp) => new(vp.Width / 2 - 140, vp.Height / 5, 128, 128);
    public static Rectangle MorionMark(Viewport vp) => new(vp.Width / 2 + 12, vp.Height / 5, 128, 128);

    public static Rectangle FaceBackdropTop(Viewport vp)
    {
        var canvas = FaceCanvas(vp);
        return ScaledRectangle(canvas, 272f, 82f, 256f, 256f);
    }

    public static Rectangle FaceBackdropBottom(Viewport vp)
    {
        var canvas = FaceCanvas(vp);
        return ScaledRectangle(canvas, 272f, 338f, 256f, 128f);
    }

    public static Rectangle FacePortrait(Viewport vp)
    {
        var canvas = FaceCanvas(vp);
        return ScaledRectangle(canvas, 288f, 102f, 224f, 286f);
    }

    public static Vector2 FaceInputPosition(Viewport vp)
    {
        var canvas = FaceCanvas(vp);
        return new Vector2(
            canvas.OffsetX + 358f * canvas.Scale,
            canvas.OffsetY + 398f * canvas.Scale);
    }

    public static int FaceNameDecorationY(Viewport vp) => vp.Height - 66;

    public static Rectangle FaceThumbHit(Viewport vp, int index)
    {
        const int boxWidth = 128;
        var startX = vp.Width / 2 - 192;
        var y = vp.Height - (70 + 128);
        return new Rectangle(startX + index * boxWidth, y, boxWidth, boxWidth);
    }

    public static Rectangle FaceThumbDraw(Viewport vp, int index)
    {
        var hit = FaceThumbHit(vp, index);
        return new Rectangle(hit.X, hit.Y + 20, hit.Width, hit.Height);
    }

    public static Rectangle ButtonHit(Texture2D texture, int x, int y, ButtonKind kind)
    {
        var (left, right) = kind switch
        {
            ButtonKind.Select => (10, 110),
            ButtonKind.Back => (20, 103),
            ButtonKind.NewCharacter => (10, 110),
            ButtonKind.Exit => (15, 108),
            ButtonKind.Delete => (10, 108),
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null)
        };
        return new Rectangle(x + left, y, right - left, texture.Height);
    }

    private static (float Scale, float OffsetX, float OffsetY) FaceCanvas(Viewport vp)
    {
        var scale = MathF.Min(vp.Width / (float)HudLayout.DesignWidth, vp.Height / (float)HudLayout.DesignHeight);
        return (
            scale,
            (vp.Width - HudLayout.DesignWidth * scale) * 0.5f,
            (vp.Height - HudLayout.DesignHeight * scale) * 0.5f);
    }

    private static Rectangle ScaledRectangle(
        (float Scale, float OffsetX, float OffsetY) canvas,
        float x,
        float y,
        float width,
        float height)
    {
        return new Rectangle(
            (int)MathF.Round(canvas.OffsetX + x * canvas.Scale),
            (int)MathF.Round(canvas.OffsetY + y * canvas.Scale),
            (int)MathF.Round(width * canvas.Scale),
            (int)MathF.Round(height * canvas.Scale));
    }
}
