using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace Scdms.Services;

/// <summary>
/// Provides a localhost TLS certificate for standalone deployments where the .NET SDK
/// (and therefore 'dotnet dev-certs') is not available. A self-signed certificate is
/// generated once and cached in the SCDMS user-data folder. Because the certificate is
/// self-signed, browsers show a one-time warning; see docs/usage.md for trust instructions.
/// </summary>
public static class LocalhostCertificateProvider
{
    private const string PfxFileName = "localhost.pfx";
    private const string PfxKeyFileName = "localhost.pfx.key";

    /// <summary>
    /// Returns a valid localhost certificate, generating and persisting one on first use.
    /// Certificates expiring within 7 days are regenerated automatically.
    /// </summary>
    public static X509Certificate2 GetOrCreateCertificate()
    {
        var certificatesDirectory = ScdmsPaths.CertificatesDirectory;
        Directory.CreateDirectory(certificatesDirectory);

        var pfxPath = Path.Combine(certificatesDirectory, PfxFileName);
        var passwordPath = Path.Combine(certificatesDirectory, PfxKeyFileName);

        if (File.Exists(pfxPath) && File.Exists(passwordPath))
        {
            try
            {
                var password = File.ReadAllText(passwordPath).Trim();
                var existing = X509CertificateLoader.LoadPkcs12FromFile(pfxPath, password);
                if (existing.NotAfter > DateTimeOffset.UtcNow.AddDays(7))
                {
                    return existing;
                }
            }
            catch (CryptographicException)
            {
                // Fall through and regenerate.
            }
        }

        return GenerateAndStore(pfxPath, passwordPath);
    }

    private static X509Certificate2 GenerateAndStore(string pfxPath, string passwordPath)
    {
        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest(
            "CN=localhost",
            rsa,
            HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1);

        var subjectAlternativeNames = new SubjectAlternativeNameBuilder();
        subjectAlternativeNames.AddDnsName("localhost");
        subjectAlternativeNames.AddIpAddress(IPAddress.Loopback);
        subjectAlternativeNames.AddIpAddress(IPAddress.IPv6Loopback);
        request.CertificateExtensions.Add(subjectAlternativeNames.Build());
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, false));
        request.CertificateExtensions.Add(
            new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyEncipherment, false));
        request.CertificateExtensions.Add(
            new X509EnhancedKeyUsageExtension(new OidCollection { new("1.3.6.1.5.5.7.3.1") }, false)); // serverAuth

        using var certificate = request.CreateSelfSigned(
            DateTimeOffset.UtcNow.AddDays(-1),
            DateTimeOffset.UtcNow.AddYears(5));

        var password = Convert.ToBase64String(RandomNumberGenerator.GetBytes(24));
        var pfxBytes = certificate.Export(X509ContentType.Pfx, password);

        File.WriteAllBytes(pfxPath, pfxBytes);
        File.WriteAllText(passwordPath, password);
        TryRestrictToCurrentUser(pfxPath);
        TryRestrictToCurrentUser(passwordPath);

        return X509CertificateLoader.LoadPkcs12(pfxBytes, password);
    }

    private static void TryRestrictToCurrentUser(string path)
    {
        try
        {
            if (OperatingSystem.IsWindows())
            {
                // Remove inherited ACLs and grant the current user only (best effort).
                var fileInfo = new FileInfo(path);
                var security = fileInfo.GetAccessControl();
                security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
                var account = System.Security.Principal.WindowsIdentity.GetCurrent().Name;
                security.AddAccessRule(
                    new System.Security.AccessControl.FileSystemAccessRule(
                        account,
                        System.Security.AccessControl.FileSystemRights.FullControl,
                        System.Security.AccessControl.AccessControlType.Allow));
                fileInfo.SetAccessControl(security);
            }
            else
            {
                File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            }
        }
        catch (IOException)
        {
            // Best effort only.
        }
        catch (UnauthorizedAccessException)
        {
            // Best effort only.
        }
        catch (InvalidOperationException)
        {
            // Best effort only (e.g. account resolution failed).
        }
    }
}
