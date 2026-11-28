using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using SecRandom.Integration;
using SecRandom.Integration.Client;
using SecRandom.DrawKit;

if (args.Length != 0) throw new ArgumentException("No arguments expected.");
var assembly = typeof(AutomationClient).Assembly;
var key = assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
    .First(attribute => attribute.Key == "SecRandomIntegrationPublicKey").Value;
var proofPath = Path.Combine(AppContext.BaseDirectory, "SecRandom.Integration.Client.official-proof.json");
var proof = JsonSerializer.Deserialize<OfficialSdkProof>(File.ReadAllText(proofPath), IntegrationJson.Options)
    ?? throw new InvalidDataException("Invalid proof.");
var hash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(assembly.Location))).ToLowerInvariant();
if (!OfficialSignature.Verify(proof, key) || proof.AssemblySha256 != hash
    || proof.SdkVersion != assembly.GetName().Version?.ToString())
    throw new InvalidDataException("The official proof does not match the installed client package.");
Console.WriteLine(new AutomationClient().PipeName);
var database = Path.Combine(Path.GetTempPath(), "secrandom-package-smoke-" + Guid.NewGuid().ToString("N") + ".sqlite");
try
{
    var local = new LocalDrawStore(database);
    local.SaveRoster(new Roster("class", "Class", RosterKind.Students, [new RosterMember("1", "Alice")]));
    if (local.Draw(new DrawRequest("class", 1, DrawMode.Fair)).Members.Single().Id != "1")
        throw new InvalidDataException("The local draw failed.");
    Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
}
finally { File.Delete(database); }
