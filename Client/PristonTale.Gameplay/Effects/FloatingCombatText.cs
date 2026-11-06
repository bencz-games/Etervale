using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using PristonTale.Gameplay.Localization;

namespace PristonTale.Gameplay.Effects;

public enum FloatingTextKind
{
    DamageDealt,
    Critical,
    DamageTaken,
    Heal,
    Miss,
    Evade
}

// Damage and heal numbers above the entities they hit. Entries live in a fixed ring; their text is formatted once when
// they are added, so drawing allocates nothing.
public sealed class FloatingCombatText
{
    public const int Capacity = 64;
    private const float LifetimeSeconds = 1.25f;
    private const float RiseUnits = 34f;

    private readonly Entry[] _entries = new Entry[Capacity];
    private int _next;

    public int Count
    {
        get
        {
            var count = 0;
            foreach (var entry in _entries)
            {
                count += entry.Age < LifetimeSeconds && entry.Text is not null ? 1 : 0;
            }

            return count;
        }
    }

    public void Add(Vector3 worldPosition, int amount, FloatingTextKind kind)
    {
        var text = kind switch
        {
            FloatingTextKind.Heal => "+" + amount,
            FloatingTextKind.Miss => ClientText.Get("Combat.Miss"),
            FloatingTextKind.Evade => ClientText.Get("Combat.Evade"),
            FloatingTextKind.Critical => amount + "!",
            _ => amount.ToString(System.Globalization.CultureInfo.InvariantCulture)
        };
        var jitter = ((_next * 37) % 21 - 10) * 1.5f;
        _entries[_next] = new Entry(worldPosition + new Vector3(jitter, 0f, 0f), text, kind, 0f);
        _next = (_next + 1) % Capacity;
    }

    public void Update(float seconds)
    {
        for (var index = 0; index < _entries.Length; index++)
        {
            if (_entries[index].Text is not null && _entries[index].Age < LifetimeSeconds)
            {
                _entries[index].Age += seconds;
            }
        }
    }

    public void Draw(SpriteBatch batch, SpriteFont font, Viewport viewport, Matrix view, Matrix projection)
    {
        var begun = false;
        for (var index = 0; index < _entries.Length; index++)
        {
            ref readonly var entry = ref _entries[index];
            if (entry.Text is null || entry.Age >= LifetimeSeconds)
            {
                continue;
            }

            var life = entry.Age / LifetimeSeconds;
            var world = entry.Position + new Vector3(0f, RiseUnits * (1f - (1f - life) * (1f - life)), 0f);
            var projected = viewport.Project(world, projection, view, Matrix.Identity);
            if (projected.Z is < 0f or > 1f)
            {
                continue;
            }

            if (!begun)
            {
                batch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.LinearClamp);
                begun = true;
            }

            var (color, scale) = Style(entry.Kind);
            var alpha = life < 0.7f ? 1f : 1f - (life - 0.7f) / 0.3f;
            var pop = entry.Kind == FloatingTextKind.Critical ? 1f + 0.35f * MathF.Max(0f, 1f - entry.Age * 6f) : 1f;
            var size = font.MeasureString(entry.Text) * scale * pop;
            var at = new Vector2(projected.X - size.X / 2f, projected.Y - size.Y);
            batch.DrawString(font, entry.Text, at + new Vector2(1.5f, 1.5f), Color.Black * (0.75f * alpha), 0f, Vector2.Zero, scale * pop, SpriteEffects.None, 0f);
            batch.DrawString(font, entry.Text, at, color * alpha, 0f, Vector2.Zero, scale * pop, SpriteEffects.None, 0f);
        }

        if (begun)
        {
            batch.End();
        }
    }

    private static (Color Color, float Scale) Style(FloatingTextKind kind) => kind switch
    {
        FloatingTextKind.Critical => (new Color(255, 196, 64), 1.25f),
        FloatingTextKind.DamageTaken => (new Color(255, 90, 80), 1f),
        FloatingTextKind.Heal => (new Color(120, 255, 140), 1.05f),
        FloatingTextKind.Miss => (new Color(200, 200, 200), 0.9f),
        _ => (new Color(255, 255, 235), 1f)
    };

    private struct Entry(Vector3 position, string text, FloatingTextKind kind, float age)
    {
        public Vector3 Position = position;
        public string Text = text;
        public FloatingTextKind Kind = kind;
        public float Age = age;
    }
}
