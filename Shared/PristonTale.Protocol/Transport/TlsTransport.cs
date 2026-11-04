using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace PristonTale.Protocol.Transport;

// Every connection runs inside TLS 1.2 or 1.3: the server presents its certificate and the client checks it before the
// first frame. The handshake has a deadline, so a silent peer cannot hold an accepted socket.
public static class TlsTransport
{
    public const SslProtocols Protocols = SslProtocols.Tls12 | SslProtocols.Tls13;
    public static readonly TimeSpan DefaultHandshakeTimeout = TimeSpan.FromSeconds(10);

    // The game server's port, and the address of a server on this machine that the client and the tools default to.
    public const int DefaultPort = 10009;
    public static readonly string LocalAddress = $"tcp://localhost:{DefaultPort}";

    // A server address: "host", "host:port" or "tcp://host:port", with DefaultPort when the port is left out, and
    // optionally "?cert=<sha-256 hex>" to pin the server certificate (checked here, so a bad pin is refused up front).
    public static Uri ParseAddress(string text)
    {
        var trimmed = text.Trim();
        var absolute = trimmed.Contains("://", StringComparison.Ordinal) ? trimmed : "tcp://" + trimmed;
        if (!Uri.TryCreate(absolute, UriKind.Absolute, out var address) || address.Scheme != "tcp" || address.Host.Length == 0 || address.Port == 0)
        {
            throw new ArgumentException($"\"{text}\" is not a server address: expected host, host:port or tcp://host:port.", nameof(text));
        }

        if (address.IsDefaultPort)
        {
            address = new UriBuilder(address) { Port = DefaultPort }.Uri;
        }

        _ = ServerCertificatePolicy.FromAddress(address);
        return address;
    }

    public static async Task<FrameConnection> AcceptAsync(Socket socket, SslStreamCertificateContext certificate, TimeSpan timeout,
        CancellationToken cancellationToken, FrameConnectionOptions? options = null)
    {
        var tls = new SslStream(new NetworkStream(socket, ownsSocket: false), leaveInnerStreamOpen: false);
        try
        {
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            deadline.CancelAfter(timeout);
            await tls.AuthenticateAsServerAsync(new SslServerAuthenticationOptions
            {
                ServerCertificateContext = certificate,
                EnabledSslProtocols = Protocols,
                ClientCertificateRequired = false,
                CertificateRevocationCheckMode = X509RevocationMode.NoCheck
            }, deadline.Token);
            return new FrameConnection(socket, tls, options);
        }
        catch
        {
            await tls.DisposeAsync();
            throw;
        }
    }

    public static async Task<FrameConnection> ConnectAsync(string host, int port, ServerCertificatePolicy policy, CancellationToken cancellationToken,
        FrameConnectionOptions? options = null)
    {
        var socket = new Socket(SocketType.Stream, ProtocolType.Tcp);
        SslStream? tls = null;
        try
        {
            await socket.ConnectAsync(host, port, cancellationToken);
            tls = new SslStream(new NetworkStream(socket, ownsSocket: false), leaveInnerStreamOpen: false);
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            deadline.CancelAfter(DefaultHandshakeTimeout);
            await tls.AuthenticateAsClientAsync(new SslClientAuthenticationOptions
            {
                TargetHost = host,
                EnabledSslProtocols = Protocols,
                CertificateRevocationCheckMode = X509RevocationMode.NoCheck,
                RemoteCertificateValidationCallback = (_, certificate, _, errors) => policy.Accepts(certificate, errors)
            }, deadline.Token);
            return new FrameConnection(socket, tls, options);
        }
        catch
        {
            if (tls is not null)
            {
                await tls.DisposeAsync();
            }

            socket.Dispose();
            throw;
        }
    }
}

// How the client trusts the server: a certificate the system trusts for the host name, a certificate whose SHA-256
// matches the pinned one, or any certificate when the server runs on this machine (the self-signed development one).
public sealed record ServerCertificatePolicy(string Host, byte[]? PinnedSha256 = null)
{
    public bool Accepts(X509Certificate? certificate, SslPolicyErrors errors)
    {
        if (certificate is null)
        {
            return false;
        }

        if (PinnedSha256 is { } pin)
        {
            return CryptographicOperations.FixedTimeEquals(SHA256.HashData(certificate.GetRawCertData()), pin);
        }

        return errors == SslPolicyErrors.None || IsLoopback(Host);
    }

    // "cert=<hex>" in the address query pins the certificate; colons and spaces between hex pairs are ignored.
    public static ServerCertificatePolicy FromAddress(Uri address)
    {
        foreach (var part in address.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var pair = part.Split('=', 2);
            if (pair.Length == 2 && pair[0].Equals("cert", StringComparison.OrdinalIgnoreCase))
            {
                var hex = Uri.UnescapeDataString(pair[1]).Replace(":", "", StringComparison.Ordinal).Replace(" ", "", StringComparison.Ordinal);
                var pin = IsHex(hex) ? Convert.FromHexString(hex) : [];
                if (pin.Length != SHA256.HashSizeInBytes)
                {
                    throw new ArgumentException("The cert pin must be a SHA-256 hash (64 hex digits).", nameof(address));
                }

                return new ServerCertificatePolicy(address.Host, pin);
            }
        }

        return new ServerCertificatePolicy(address.Host);
    }

    private static bool IsHex(string text) => text.Length % 2 == 0 && text.All(char.IsAsciiHexDigit);

    private static bool IsLoopback(string host) =>
        host.Equals("localhost", StringComparison.OrdinalIgnoreCase) || IPAddress.TryParse(host.Trim('[', ']'), out var address) && IPAddress.IsLoopback(address);
}
