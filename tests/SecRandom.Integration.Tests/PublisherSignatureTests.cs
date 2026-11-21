using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using SecRandom.Integration;

namespace SecRandom.Integration.Tests;

public sealed class PublisherSignatureTests
{
    [Fact]
    public void SignedOfficialSdkProof_IsAcceptedAndTamperingIsRejected()
    {
        using var signer = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var unsigned = new OfficialSdkProof(
            OfficialSignature.OfficialPackageId,
            "1.0.0",
            new string('a', 64),
            string.Empty);
        var bytes = Encoding.UTF8.GetBytes(OfficialSignature.CanonicalProof(unsigned));
        var proof = unsigned with
        {
            Signature = Convert.ToBase64String(signer.SignData(bytes, HashAlgorithmName.SHA256))
        };
        var manifest = new IntegrationManifest("example.app", "Example", proof.SdkVersion,
            IntegrationProtocol.ProtocolName, "pipe", Environment.ProcessId, "instance", [],
            DateTimeOffset.UtcNow, JsonSerializer.Serialize(proof, IntegrationJson.Options));

        Assert.True(OfficialSignature.Verify(manifest, Convert.ToBase64String(signer.ExportSubjectPublicKeyInfo())));
        Assert.False(OfficialSignature.Verify(manifest with { SdkVersion = "9.9.9" },
            Convert.ToBase64String(signer.ExportSubjectPublicKeyInfo())));
        Assert.False(OfficialSignature.Verify(manifest with { PublisherProof = "{}" },
            Convert.ToBase64String(signer.ExportSubjectPublicKeyInfo())));
    }
}

