using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using PristonTale.Persistence.Sqlite;
using PristonTale.Assets;

namespace PristonTale.Server.Net;

// The TLS certificate the server presents: a PKCS#12 file from Network:Certificate:Path (relative paths resolve from the
// solution root, like the SQLite database) with Network:Certificate:Password. When the file does not exist a self-signed
// ECDSA P-256 certificate is created there; clients on this machine accept it, others pin its SHA-256.
internal static class ServerCertificate
{
    public static X509Certificate2 Load(IConfiguration configuration, ILogger logger)
    {
        var path = DataPath.Resolve(configuration["Network:Certificate:Path"] ?? Path.Combine("data", "server-certificate.pfx"));
        var password = configuration["Network:Certificate:Password"];
        if (!File.Exists(path))
        {
            Create(path);
            logger.LogWarning("Created a self-signed TLS certificate at {Path}; remote clients must pin it or use a trusted certificate", path);
        }

        var certificate = X509CertificateLoader.LoadPkcs12FromFile(path, password);
        if (!certificate.HasPrivateKey)
        {
            throw new InvalidOperationException($"The TLS certificate {path} has no private key.");
        }

        logger.LogInformation("TLS certificate {Subject}, SHA-256 {Sha256}, valid until {Expiry:yyyy-MM-dd}", certificate.Subject,
            Convert.ToHexString(SHA256.HashData(certificate.RawData)), certificate.NotAfter);
        return certificate;
    }

    private static void Create(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        File.WriteAllBytes(temporary, CreateSelfSignedPfx());
        try
        {
            // Another server instance (an in-process test) may have created it first; keep that one.
            File.Move(temporary, path, overwrite: false);
        }
        catch (IOException) when (File.Exists(path))
        {
            File.Delete(temporary);
        }
    }

    // A self-signed ECDSA P-256 server certificate for localhost and this machine, valid two years, as PKCS#12 bytes.
    internal static byte[] CreateSelfSignedPfx()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var request = new CertificateRequest("CN=Etervale", key, HashAlgorithmName.SHA256);
        var names = new SubjectAlternativeNameBuilder();
        names.AddDnsName("localhost");
        names.AddDnsName(Environment.MachineName);
        names.AddIpAddress(IPAddress.Loopback);
        names.AddIpAddress(IPAddress.IPv6Loopback);
        request.CertificateExtensions.Add(names.Build());
        request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature, critical: true));
        request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension([new Oid("1.3.6.1.5.5.7.3.1")], critical: false));
        using var certificate = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddYears(2));
        return certificate.Export(X509ContentType.Pfx);
    }
}
