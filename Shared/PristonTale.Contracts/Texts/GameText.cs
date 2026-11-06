using System.Collections.Immutable;

namespace PristonTale.Contracts.Texts;

// A line the server or the simulation has for the player, worded by the client in its own language: Id names the
// phrase, Args fill its placeholders in order with values already worded (names, numbers). None is no line.
public readonly struct GameText : IEquatable<GameText>
{
    private readonly ImmutableArray<string> _args;

    public GameText(GameTextId id, ImmutableArray<string> args)
    {
        Id = id;
        _args = args;
    }

    public static GameText None => default;

    public GameTextId Id { get; }

    public ImmutableArray<string> Args => _args.IsDefault ? [] : _args;

    public bool IsNone => Id == GameTextId.None;

    public static GameText Of(GameTextId id) => new(id, []);

    public static GameText Of(GameTextId id, params ReadOnlySpan<string> args) => new(id, [.. args]);

    public static GameText Of(GameTextId id, long number) => new(id, [number.ToString(System.Globalization.CultureInfo.InvariantCulture)]);

    public bool Equals(GameText other) => Id == other.Id && Args.AsSpan().SequenceEqual(other.Args.AsSpan());

    public override bool Equals(object? obj) => obj is GameText other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(Id, Args.Length);

    public static bool operator ==(GameText left, GameText right) => left.Equals(right);

    public static bool operator !=(GameText left, GameText right) => !left.Equals(right);

    public override string ToString() => Args.IsEmpty ? Id.ToString() : $"{Id}({string.Join(", ", Args)})";
}
