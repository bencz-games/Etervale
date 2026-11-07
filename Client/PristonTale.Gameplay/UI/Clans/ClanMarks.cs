using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace PristonTale.Gameplay.UI.Clans;

// A clan's mark as name boxes and windows draw it, 16x16. Every clan shows the default crest: the server keeps no
// uploaded marks (IClanMarkStore), so mark versions stay 0.
public static class ClanMarks
{
    public const int Size = 16;

    // A clan line is the mark and, 2 px after it, the clan's name.
    public const int Span = Size + 2;

    // o outline, g gold rim, r field, h field light, w emblem; '.' is transparent.
    private static readonly string[] Crest =
    [
        "................",
        ".oooooooooooooo.",
        ".oggggggggggggo.",
        ".oghhhhhhhhhhgo.",
        ".oghhhhwwhhhhgo.",
        ".ogrrrwwwwrrrgo.",
        ".ogrrrrwwrrrrgo.",
        ".ogrwwwwwwwwrgo.",
        ".ogrwwwwwwwwrgo.",
        ".ogrrrrwwrrrrgo.",
        "..ogrrrwwrrrgo..",
        "..ogrrrwwrrrgo..",
        "...ogrrwwrrgo...",
        "....ogrrrrgo....",
        ".....oggggo.....",
        "......oooo......",
    ];

    public static Texture2D Default(GraphicsDevice device)
    {
        var pixels = new Color[Size * Size];
        for (var y = 0; y < Size; y++)
        {
            for (var x = 0; x < Size; x++)
            {
                pixels[y * Size + x] = Crest[y][x] switch
                {
                    'o' => new Color(40, 24, 10),
                    'g' => new Color(226, 186, 84),
                    'h' => new Color(178, 44, 40),
                    'r' => new Color(140, 28, 30),
                    'w' => new Color(244, 234, 206),
                    _ => Color.Transparent,
                };
            }
        }

        var texture = new Texture2D(device, Size, Size, false, SurfaceFormat.Color);
        texture.SetData(pixels);
        return texture;
    }
}
