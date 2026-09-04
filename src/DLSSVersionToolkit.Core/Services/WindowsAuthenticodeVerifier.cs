namespace DLSSVersionToolkit.Core.Services;

using System.Runtime.Versioning;
using System.Security.Cryptography.X509Certificates;

/// <summary>Result of an Authenticode signature check.</summary>
public class AuthenticodeResult
{
    public bool IsValid { get; set; }

    /// <summary>Why it failed, or the accepted signer subject.</summary>
    public string Detail { get; set; } = "";

    public string? Signer { get; set; }
}

/// <summary>
/// Authenticode verification for downloaded payloads. An interface so the OTA download path can
/// be tested on a non-Windows CI runner and so the reject arm can be armed with a bad signature
/// without needing a maliciously-signed fixture.
/// </summary>
public interface IAuthenticodeVerifier
{
    AuthenticodeResult Verify(string filePath);
}

/// <summary>
/// The real check. A payload is accepted only when it carries an Authenticode signature whose
/// chain builds and whose signer is NVIDIA.
///
/// The signer-name test is deliberately part of validity: a file can be perfectly signed and
/// still not be NVIDIA's, and "it had a signature" is not the question being asked when the
/// bytes came from an undocumented endpoint.
/// </summary>
public class WindowsAuthenticodeVerifier : IAuthenticodeVerifier
{
    /// <summary>Accepted signer organizations, matched case-insensitively against the subject.</summary>
    private static readonly string[] AcceptedSigners = { "NVIDIA" };

    public AuthenticodeResult Verify(string filePath)
    {
        if (!OperatingSystem.IsWindows())
        {
            // The download path only runs on Windows. Reaching this on another OS means a test or
            // a port; fail closed rather than silently accepting unverified bytes.
            return new AuthenticodeResult
            {
                IsValid = false,
                Detail = "Authenticode verification is only available on Windows.",
            };
        }

        return VerifyOnWindows(filePath);
    }

    [SupportedOSPlatform("windows")]
    private static AuthenticodeResult VerifyOnWindows(string filePath)
    {
        try
        {
            using var cert = new X509Certificate2(X509Certificate.CreateFromSignedFile(filePath));

            using var chain = new X509Chain();
            chain.ChainPolicy.RevocationMode = X509RevocationMode.Online;
            chain.ChainPolicy.RevocationFlag = X509RevocationFlag.ExcludeRoot;
            // A revocation server that cannot be reached must not silently pass the file.
            chain.ChainPolicy.UrlRetrievalTimeout = TimeSpan.FromSeconds(15);

            if (!chain.Build(cert))
            {
                var reasons = string.Join(", ",
                    chain.ChainStatus.Select(s => s.StatusInformation.Trim())
                        .Where(s => !string.IsNullOrWhiteSpace(s)));
                return new AuthenticodeResult
                {
                    IsValid = false,
                    Detail = $"certificate chain did not build ({reasons}).",
                    Signer = cert.Subject,
                };
            }

            var trusted = AcceptedSigners.Any(s =>
                cert.Subject.Contains(s, StringComparison.OrdinalIgnoreCase));

            return new AuthenticodeResult
            {
                IsValid = trusted,
                Signer = cert.Subject,
                Detail = trusted
                    ? $"signed by {cert.Subject}"
                    : $"signer is not NVIDIA ({cert.Subject}).",
            };
        }
        catch (Exception ex)
        {
            // CreateFromSignedFile throws when the file carries no signature at all.
            return new AuthenticodeResult
            {
                IsValid = false,
                Detail = $"no valid Authenticode signature ({ex.Message}).",
            };
        }
    }
}
