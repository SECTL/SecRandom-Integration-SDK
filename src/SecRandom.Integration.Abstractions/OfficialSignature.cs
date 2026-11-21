using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace SecRandom.Integration;

public static class OfficialSignature
{
    public const string OfficialPackageId = "SecRandom.Integration.Client";

    public static string CanonicalProof(OfficialSdkProof proof) =>
        $"secrandom.integration-sdk/v1\n{proof.PackageId}\n{proof.SdkVersion}\n{proof.AssemblySha256.ToLowerInvariant()}";

    public static bool Verify(IntegrationManifest manifest) => Verify(manifest, GetPublicKey());

    public static bool Verify(IntegrationManifest manifest, string? publicKey)
    {
        publicKey ??= GetPublicKey();
        if (string.IsNullOrWhiteSpace(publicKey) || string.IsNullOrWhiteSpace(manifest.PublisherProof))
            return false;

        try
        {
            var proof = JsonSerializer.Deserialize<OfficialSdkProof>(manifest.PublisherProof, IntegrationJson.Options);
            return proof is not null
                   && string.Equals(proof.SdkVersion, manifest.SdkVersion, StringComparison.Ordinal)
                   && Verify(proof, publicKey);
        }
        catch (JsonException)
        {
            return false;
        }
        catch (FormatException)
        {
            return false;
        }
        catch (CryptographicException)
        {
            return false;
        }
    }

    public static bool Verify(OfficialSdkProof proof, string? publicKey)
    {
        if (proof is null || string.IsNullOrWhiteSpace(publicKey)
            || !string.Equals(proof.PackageId, OfficialPackageId, StringComparison.Ordinal)
            || proof.AssemblySha256 is not { Length: 64 }
            || proof.AssemblySha256.Any(x => !Uri.IsHexDigit(x))
            || string.IsNullOrWhiteSpace(proof.SdkVersion)
            || string.IsNullOrWhiteSpace(proof.Signature))
            return false;

        try
        {
            using var ecdsa = ECDsa.Create();
            ecdsa.ImportSubjectPublicKeyInfo(Convert.FromBase64String(publicKey), out _);
            return ecdsa.VerifyData(
                Encoding.UTF8.GetBytes(CanonicalProof(proof)),
                Convert.FromBase64String(proof.Signature),
                HashAlgorithmName.SHA256);
        }
        catch (FormatException)
        {
            return false;
        }
        catch (CryptographicException)
        {
            return false;
        }
    }

    private static string? GetPublicKey() => typeof(OfficialSignature).Assembly
        .GetCustomAttributes<AssemblyMetadataAttribute>()
        .FirstOrDefault(x => x.Key == "SecRandomIntegrationPublicKey")?.Value;
}

