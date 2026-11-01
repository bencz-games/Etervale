using Microsoft.Xna.Framework;

namespace PristonTale.Engine3D.Rendering;

public readonly record struct DayNightKey(float Hour, Vector3 Sun, Vector3 Ambient, Vector3 Sky);

public sealed record DayNightSettings
{
    public IReadOnlyList<DayNightKey> Gradient { get; init; } = DefaultGradient;
    public float DayLengthSeconds { get; init; } = 150f;
    public float StartTimeOfDay { get; init; } = 8.0f;

    public static readonly DayNightKey[] DefaultGradient =
    [
        new(0.0f,  new Vector3(0.30f, 0.34f, 0.46f), new Vector3(0.13f, 0.15f, 0.20f), new Vector3(0.030f, 0.040f, 0.075f)),
        new(3.5f,  new Vector3(0.30f, 0.34f, 0.46f), new Vector3(0.13f, 0.15f, 0.20f), new Vector3(0.030f, 0.040f, 0.075f)),
        new(5.0f,  new Vector3(0.85f, 0.55f, 0.34f), new Vector3(0.16f, 0.14f, 0.14f), new Vector3(0.40f,  0.32f,  0.30f)),
        new(8.5f,  new Vector3(1.00f, 0.96f, 0.86f), new Vector3(0.30f, 0.30f, 0.30f), new Vector3(0.42f,  0.58f,  0.82f)),
        new(13.0f, new Vector3(1.05f, 1.02f, 0.95f), new Vector3(0.36f, 0.36f, 0.37f), new Vector3(0.48f,  0.66f,  0.92f)),
        new(18.0f, new Vector3(1.00f, 0.95f, 0.82f), new Vector3(0.32f, 0.31f, 0.30f), new Vector3(0.44f,  0.60f,  0.84f)),
        new(21.25f, new Vector3(0.92f, 0.52f, 0.28f), new Vector3(0.16f, 0.13f, 0.12f), new Vector3(0.42f,  0.30f,  0.26f)),
        new(22.5f, new Vector3(0.42f, 0.40f, 0.48f), new Vector3(0.13f, 0.14f, 0.18f), new Vector3(0.12f,  0.11f,  0.16f)),
        new(23.0f, new Vector3(0.30f, 0.34f, 0.46f), new Vector3(0.13f, 0.15f, 0.20f), new Vector3(0.030f, 0.040f, 0.075f)),
        new(24.0f, new Vector3(0.30f, 0.34f, 0.46f), new Vector3(0.13f, 0.15f, 0.20f), new Vector3(0.030f, 0.040f, 0.075f)),
    ];

    public static readonly DayNightSettings Default = new();
}
