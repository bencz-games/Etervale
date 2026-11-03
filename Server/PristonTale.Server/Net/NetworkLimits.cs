using System.Net;
using PristonTale.Protocol.Transport;

namespace PristonTale.Server.Net;

// Connection hardening read from the "Network" configuration section; a value of 0 disables that limit.
internal sealed record NetworkLimits
{
    // The original server disconnected a client that sent more than 251 packets in 5 s.
    public int PacketLimit { get; init; } = 251;
    public int PacketWindowMs { get; init; } = 5000;
    public int MaxConnectionsPerIp { get; init; } = 8;
    // Every open socket, from its accept to its end, and the TLS handshakes in progress, in all and per address: past
    // any of them a new socket is closed before its handshake costs anything. The per-address cap keeps a few hosts of
    // silent sockets from holding every handshake.
    public int MaxConnections { get; init; } = 6000;
    public int MaxHandshakes { get; init; } = 128;
    public int MaxHandshakesPerIp { get; init; } = 4;
    public int LoginTimeoutMs { get; init; } = 30_000;
    // The deadline of an accepted socket's TLS handshake; it cannot be disabled.
    public int HandshakeTimeoutMs { get; init; } = (int)TlsTransport.DefaultHandshakeTimeout.TotalMilliseconds;
    public int LoginFailuresPerAccount { get; init; } = 5;
    public int LoginFailuresPerIp { get; init; } = 20;
    public int LoginFailureWindowMs { get; init; } = 300_000;
    public int PlayerInfoBurst { get; init; } = 512;
    public int PlayerInfoPerSecond { get; init; } = 128;
    // Requests that copy the character on the tick (inventory, progression, NPC services, item use, quests, restarts,
    // wing gates) cost more than a packet: past this bucket they are refused as busy. The client sends one inventory
    // request at a time, so a player drinking potions and looting stays well inside it.
    public int StoredOperationBurst { get; init; } = 40;
    public int StoredOperationsPerSecond { get; init; } = 20;

    public static NetworkLimits From(IConfiguration configuration)
    {
        var defaults = new NetworkLimits();
        return new NetworkLimits
        {
            PacketLimit = Read(configuration, "PacketLimit", defaults.PacketLimit),
            PacketWindowMs = Read(configuration, "PacketWindowSeconds", defaults.PacketWindowMs / 1000) * 1000,
            MaxConnectionsPerIp = Read(configuration, "MaxConnectionsPerIp", defaults.MaxConnectionsPerIp),
            MaxConnections = Read(configuration, "MaxConnections", defaults.MaxConnections),
            MaxHandshakes = Read(configuration, "MaxHandshakes", defaults.MaxHandshakes),
            MaxHandshakesPerIp = Read(configuration, "MaxHandshakesPerIp", defaults.MaxHandshakesPerIp),
            LoginTimeoutMs = Read(configuration, "LoginTimeoutSeconds", defaults.LoginTimeoutMs / 1000) * 1000,
            HandshakeTimeoutMs = ReadPositive(configuration, "HandshakeTimeoutSeconds", defaults.HandshakeTimeoutMs / 1000) * 1000,
            LoginFailuresPerAccount = Read(configuration, "LoginFailuresPerAccount", defaults.LoginFailuresPerAccount),
            LoginFailuresPerIp = Read(configuration, "LoginFailuresPerIp", defaults.LoginFailuresPerIp),
            LoginFailureWindowMs = Read(configuration, "LoginFailureWindowSeconds", defaults.LoginFailureWindowMs / 1000) * 1000,
            PlayerInfoBurst = Read(configuration, "PlayerInfoBurst", defaults.PlayerInfoBurst),
            PlayerInfoPerSecond = Read(configuration, "PlayerInfoPerSecond", defaults.PlayerInfoPerSecond),
            StoredOperationBurst = Read(configuration, "StoredOperationBurst", defaults.StoredOperationBurst),
            StoredOperationsPerSecond = Read(configuration, "StoredOperationsPerSecond", defaults.StoredOperationsPerSecond)
        };
    }

    private static int ReadPositive(IConfiguration configuration, string key, int fallback)
    {
        var value = Read(configuration, key, fallback);
        if (value < 1)
        {
            throw new InvalidOperationException($"Network:{key} must be at least 1.");
        }

        return value;
    }

    private static int Read(IConfiguration configuration, string key, int fallback)
    {
        var value = configuration.GetValue("Network:" + key, fallback);
        if (value < 0)
        {
            throw new InvalidOperationException($"Network:{key} must not be negative.");
        }

        return value;
    }

    public static IPAddress? AddressOf(EndPoint? endpoint)
    {
        if (endpoint is not IPEndPoint ip)
        {
            return null;
        }

        return ip.Address.IsIPv4MappedToIPv6 ? ip.Address.MapToIPv4() : ip.Address;
    }
}
