using System.Text.Json;

namespace Forge.Shared;

public sealed class XrefPinNode
{
    public string Name { get; init; } = "";
    public string Path { get; init; } = "";
    public int Depth { get; init; } = 1;
    public string? ParentName { get; init; }
    public long? Length { get; init; }
    public DateTimeOffset? MtimeUtc { get; init; }
    public string? ContentHash { get; init; }
}

public sealed class XrefClosurePin
{
    public string PinId { get; init; } = Guid.NewGuid().ToString("N");
    public DateTimeOffset CreatedUtc { get; init; } = DateTimeOffset.UtcNow;
    public string? DocumentPath { get; init; }
    public List<XrefPinNode> Nodes { get; init; } = [];

    public static string DefaultPinDir()
        => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "765T-Forge",
            "xref-pins");

    public static string? TrySave(XrefClosurePin pin, string? directory = null)
    {
        try
        {
            var root = directory ?? DefaultPinDir();
            Directory.CreateDirectory(root);
            var path = Path.Combine(root, $"pin-{pin.PinId}.json");
            File.WriteAllText(path, JsonSerializer.Serialize(pin, ForgeJson.Options));
            return path;
        }
        catch
        {
            return null;
        }
    }

    public static XrefClosurePin? TryLoad(string path)
    {
        try
        {
            return JsonSerializer.Deserialize<XrefClosurePin>(File.ReadAllText(path), ForgeJson.Options);
        }
        catch
        {
            return null;
        }
    }

    public static IReadOnlyList<QaFinding> Compare(IEnumerable<XrefPinNode> current, XrefClosurePin pin)
    {
        var findings = new List<QaFinding>();
        var currentByName = current.ToDictionary(n => n.Name, StringComparer.OrdinalIgnoreCase);
        foreach (var expected in pin.Nodes)
        {
            if (!currentByName.TryGetValue(expected.Name, out var live))
            {
                findings.Add(new QaFinding(
                    "xref_pin_missing_node",
                    "error",
                    $"Pinned xref '{expected.Name}' is missing from the current drawing.",
                    "Restore the xref or re-pin after intentional change.",
                    "forge_xref_pin_verify"));
                continue;
            }

            var pathChanged = !string.Equals(NormalizePath(expected.Path), NormalizePath(live.Path), StringComparison.OrdinalIgnoreCase);
            var hashChanged = !string.IsNullOrWhiteSpace(expected.ContentHash)
                              && !string.Equals(expected.ContentHash, live.ContentHash, StringComparison.OrdinalIgnoreCase);
            var lengthChanged = expected.Length is not null && live.Length is not null && expected.Length != live.Length;
            if (pathChanged || hashChanged || lengthChanged)
            {
                findings.Add(new QaFinding(
                    "xref_pin_mismatch",
                    "error",
                    $"Pinned xref '{expected.Name}' changed (path/hash/length).",
                    "Investigate consultant drop or re-save pin after human accept.",
                    "forge_xref_pin_verify"));
            }
        }

        return findings;
    }

    private static string NormalizePath(string path)
    {
        try
        {
            return Path.GetFullPath(path);
        }
        catch
        {
            return path;
        }
    }
}
