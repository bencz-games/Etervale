namespace PristonTale.Application.Clans;

// Where clan marks are kept: the chief's upload (ClanWire.MarkSize square RGBA pixels) as the clan's mark of that
// version. A store answers false when it did not keep it, and never throws.
public interface IClanMarkStore
{
    Task<bool> SaveAsync(long clanId, int version, ReadOnlyMemory<byte> pixels, CancellationToken ct = default);
}

// The server keeps no marks yet: every upload is refused and clans show the default mark.
public sealed class NoClanMarkStore : IClanMarkStore
{
    public static readonly NoClanMarkStore Instance = new();

    public Task<bool> SaveAsync(long clanId, int version, ReadOnlyMemory<byte> pixels, CancellationToken ct = default) => Task.FromResult(false);
}
