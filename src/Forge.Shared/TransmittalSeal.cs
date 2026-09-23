using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Forge.Shared;

public sealed class TransmittalSeal
{
    public string SealId { get; init; } = Guid.NewGuid().ToString("N");
    public DateTimeOffset CreatedUtc { get; init; } = DateTimeOffset.UtcNow;
    public string? ReceiptId { get; init; }
    public string[] Inputs { get; init; } = [];
    public string Algorithm { get; init; } = "SHA256";
    public string Digest { get; init; } = "";
    public string? ArtifactPath { get; set; }

    public static TransmittalSeal Create(
        IEnumerable<string> paths,
        string? receiptId = null,
        string? hmacKey = null)
    {
        var normalized = paths
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .Select(p => Path.GetFullPath(p))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var material = new StringBuilder();
        material.Append(receiptId ?? "");
        foreach (var path in normalized)
        {
            material.Append('|').Append(path);
            if (File.Exists(path))
            {
                material.Append(':').Append(new FileInfo(path).Length);
                material.Append(':').Append(DependencyClosure.TryHashFile(path) ?? "");
            }
            else
            {
                material.Append(":missing");
            }
        }

        string digest;
        string algorithm;
        var bytes = Encoding.UTF8.GetBytes(material.ToString());
        if (!string.IsNullOrEmpty(hmacKey))
        {
            algorithm = "HMACSHA256";
            using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(hmacKey!));
            digest = Hex.Encode(hmac.ComputeHash(bytes));
        }
        else
        {
            algorithm = "SHA256";
            using var sha = SHA256.Create();
            digest = Hex.Encode(sha.ComputeHash(bytes));
        }

        return new TransmittalSeal
        {
            ReceiptId = receiptId,
            Inputs = normalized,
            Algorithm = algorithm,
            Digest = digest
        };
    }

    public static bool Verify(TransmittalSeal seal, string? hmacKey = null)
    {
        var recomputed = Create(seal.Inputs, seal.ReceiptId, hmacKey);
        return string.Equals(recomputed.Digest, seal.Digest, StringComparison.OrdinalIgnoreCase)
               && string.Equals(recomputed.Algorithm, seal.Algorithm, StringComparison.OrdinalIgnoreCase);
    }

    public static string? TryWrite(TransmittalSeal seal, string? directory = null)
    {
        try
        {
            var root = directory
                       ?? Path.Combine(
                           Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                           "765T-Forge",
                           "seals");
            Directory.CreateDirectory(root);
            var path = Path.Combine(root, $"seal-{seal.SealId}.json");
            AtomicFile.WriteAllText(path, JsonSerializer.Serialize(seal, ForgeJson.Options));
            seal.ArtifactPath = path;
            return path;
        }
        catch
        {
            return null;
        }
    }
}
