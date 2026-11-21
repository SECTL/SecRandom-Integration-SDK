using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using SecRandom.Integration;

if (args.Length != 2)
{
    Console.Error.WriteLine("Usage: SecRandom.Integration.Signing <client-assembly> <proof-output>");
    return 2;
}

var privateKey = Environment.GetEnvironmentVariable("SECRANDOM_INTEGRATION_SIGNING_PRIVATE_KEY_PKCS8_BASE64");
var publicKey = Environment.GetEnvironmentVariable("SECRANDOM_INTEGRATION_PUBLIC_KEY_SPKI_BASE64");
if (string.IsNullOrWhiteSpace(privateKey) || string.IsNullOrWhiteSpace(publicKey))
{
    Console.Error.WriteLine("Signing and verification key environment variables are required.");
    return 3;
}

var assemblyPath = Path.GetFullPath(args[0]);
var version = System.Reflection.AssemblyName.GetAssemblyName(assemblyPath).Version?.ToString();
if (string.IsNullOrWhiteSpace(version))
{
    Console.Error.WriteLine("The SDK assembly has no assembly version.");
    return 5;
}
var assemblyHash = Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(assemblyPath))).ToLowerInvariant();
var unsigned = new OfficialSdkProof(OfficialSignature.OfficialPackageId, version, assemblyHash, string.Empty);
using var signer = ECDsa.Create();
signer.ImportPkcs8PrivateKey(Convert.FromBase64String(privateKey), out _);
var signature = signer.SignData(Encoding.UTF8.GetBytes(OfficialSignature.CanonicalProof(unsigned)), HashAlgorithmName.SHA256);
var proof = unsigned with { Signature = Convert.ToBase64String(signature) };

using var verifier = ECDsa.Create();
verifier.ImportSubjectPublicKeyInfo(Convert.FromBase64String(publicKey), out _);
if (!verifier.VerifyData(Encoding.UTF8.GetBytes(OfficialSignature.CanonicalProof(proof)), signature, HashAlgorithmName.SHA256))
{
    Console.Error.WriteLine("The signing key does not match the configured public verification key.");
    return 4;
}

var outputPath = Path.GetFullPath(args[1]);
Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
await File.WriteAllTextAsync(outputPath, JsonSerializer.Serialize(proof, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
return 0;

