using Microsoft.Xna.Framework;
using PristonTale.Contracts.Units;

namespace PristonTale.Engine3D.Rendering.Lighting;

public sealed class PristonTransientLights
{
    public const int Capacity = 80;
    public const int ShaderBudget = 8;
    private readonly Entry[] _entries = new Entry[Capacity];
    public int Count { get; private set; }

    public bool Add(Vector3 position, Color color, int power, int decay)
    {
        if (!float.IsFinite(position.X) || !float.IsFinite(position.Y) || !float.IsFinite(position.Z))
        {
            throw new ArgumentOutOfRangeException(nameof(position));
        }

        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(power);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(decay);
        if (Count == Capacity)
        {
            return false;
        }

        _entries[Count++] = new Entry(position, color, power, decay, 0);
        return true;
    }

    public void Update(float seconds)
    {
        if (!float.IsFinite(seconds) || seconds < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(seconds));
        }

        int index = 0;
        while (index < Count)
        {
            var entry = _entries[index];
            entry = entry with { Age = entry.Age + seconds };
            if (entry.Age * GameUnits.TicksPerSecond >= Math.Ceiling(entry.Power / (double)entry.Decay))
            {
                _entries[index] = _entries[--Count];
                continue;
            }

            _entries[index++] = entry;
        }
    }

    public void Clear() => Count = 0;

    public void Select(Vector3 focus, List<PristonPointLight> destination)
    {
        destination.Clear();
        Span<float> distances = stackalloc float[ShaderBudget];
        for (int index = 0; index < Count; index++)
        {
            var entry = _entries[index];
            float distance = Vector3.DistanceSquared(entry.Position, focus);
            int slot = 0;
            while (slot < destination.Count && distances[slot] <= distance)
            {
                slot++;
            }

            if (slot == ShaderBudget)
            {
                continue;
            }

            if (destination.Count == ShaderBudget)
            {
                destination.RemoveAt(ShaderBudget - 1);
            }

            for (int move = destination.Count; move > slot; move--)
            {
                distances[move] = distances[move - 1];
            }

            distances[slot] = distance;
            int ticks = (int)Math.Floor(entry.Age * 70 + 0.000001);
            int power = Math.Max(0, entry.Power - ticks * entry.Decay);
            var color = new Vector3(
                (float)((long)power * entry.Color.R / 256) / 255f,
                (float)((long)power * entry.Color.G / 256) / 255f,
                (float)((long)power * entry.Color.B / 256) / 255f);
            destination.Insert(slot, new PristonPointLight(entry.Position, Math.Max(64, power >> 1), color, 1));
        }
    }

    private readonly record struct Entry(Vector3 Position, Color Color, int Power, int Decay, double Age);
}
