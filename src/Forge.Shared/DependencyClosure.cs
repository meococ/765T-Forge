using System.Security.Cryptography;
using System.Text;

namespace Forge.Shared;

public sealed record DependencyNode(
    string Path,
    string Kind,
    bool Exists,
    string? ContentHash = null,
    long? Length = null);

public static class DependencyClosure
{
    public static string? TryHashFile(string path)
    {
        try
        {
            if (!File.Exists(path))
            {
                return null;
            }

            using var stream = File.OpenRead(path);
            var hash = SHA256.HashData(stream);
            return Convert.ToHexString(hash)[..16];
        }
        catch
        {
            return null;
        }
    }

    public static DependencyNode FromPath(string path, string kind)
    {
        var full = Path.GetFullPath(path);
        var exists = File.Exists(full);
        return new DependencyNode(
            full,
            kind,
            exists,
            exists ? TryHashFile(full) : null,
            exists ? new FileInfo(full).Length : null);
    }

    public static IReadOnlyList<QaFinding> Evaluate(IEnumerable<DependencyNode> nodes)
    {
        var findings = new List<QaFinding>();
        foreach (var node in nodes)
        {
            if (!node.Exists)
            {
                findings.Add(new QaFinding(
                    "dependency_missing",
                    "error",
                    $"Missing {node.Kind} dependency: {node.Path}",
                    "Restore the file or update pack-and-go / plot style search paths.",
                    "forge_qa_dependency_closure"));
            }
        }

        return findings;
    }
}
