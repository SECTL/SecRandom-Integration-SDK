using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using SecRandom.Integration;

namespace SecRandom.Integration.Client;

public sealed class IntegrationDiscovery
{
    public IntegrationDiscovery(string? directory = null, string? publicKey = null)
    {
        DirectoryPath = Path.GetFullPath(directory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "SecRandom", "integrations"));
        PublicKey = publicKey;
    }

    public string DirectoryPath { get; }
    public string? PublicKey { get; }

    public string Publish(IntegrationManifest manifest)
    {
        if (string.IsNullOrWhiteSpace(manifest.AppId))
            throw new ArgumentException("An application id is required.", nameof(manifest));

        Directory.CreateDirectory(DirectoryPath);
        var fileName = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(manifest.AppId))).ToLowerInvariant();
        var path = Path.Combine(DirectoryPath, fileName + ".json");
        var temp = path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(manifest, IntegrationJson.Options));
        File.Move(temp, path, true);
        return path;
    }

    public IReadOnlyList<IntegrationManifest> Discover()
    {
        if (!Directory.Exists(DirectoryPath))
            return [];
        var result = new List<IntegrationManifest>();
        foreach (var file in Directory.EnumerateFiles(DirectoryPath, "*.json"))
        {
            try
            {
                var manifest = JsonSerializer.Deserialize<IntegrationManifest>(File.ReadAllText(file), IntegrationJson.Options);
                if (manifest is not null && IsProcessAlive(manifest.ProcessId)
                    && OfficialSignature.Verify(manifest, PublicKey)
                    && VerifyLoadedSdkAssembly(manifest.ProcessId, manifest.PublisherProof))
                    result.Add(manifest);
            }
            catch (JsonException) { }
            catch (IOException) { }
        }
        return result;
    }

    private bool VerifyLoadedSdkAssembly(int processId, string? serializedProof)
    {
        try
        {
            var proof = JsonSerializer.Deserialize<OfficialSdkProof>(serializedProof!, IntegrationJson.Options);
            if (proof is null || !OfficialSignature.Verify(proof, PublicKey))
                return false;

            using var process = Process.GetProcessById(processId);
            foreach (ProcessModule module in process.Modules)
            {
                try
                {
                    if (string.Equals(
                            Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(module.FileName))).ToLowerInvariant(),
                            proof.AssemblySha256,
                            StringComparison.Ordinal))
                        return true;
                }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
        }
        catch (JsonException) { }
        catch (ArgumentException) { }
        catch (InvalidOperationException) { }
        catch (System.ComponentModel.Win32Exception) { }
        catch (NotSupportedException) { }

        return false;
    }

    public static bool IsProcessAlive(int processId)
    {
        try { return Process.GetProcessById(processId) is { HasExited: false }; }
        catch (ArgumentException) { return false; }
        catch (InvalidOperationException) { return false; }
    }
}

