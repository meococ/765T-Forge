namespace Forge.Shared;

public static class XrefWalkStatuses
{
    public const string Ok = "ok";
    public const string Missing = "missing";
    public const string Unreadable = "unreadable";
    public const string Cycle = "cycle";
    public const string DepthCapped = "depth_capped";
    public const string Unloaded = "unloaded";
}

public sealed class XrefClosureNode
{
    public string Name { get; init; } = "";
    public string Path { get; init; } = "";
    public int Depth { get; init; } = 1;
    public string? ParentName { get; init; }
    public string WalkStatus { get; init; } = XrefWalkStatuses.Ok;
    public bool IsOverlay { get; init; }
    public bool IsUnloaded { get; init; }
    public string? Status { get; init; }

    public string PinKey => Depth <= 1 || string.IsNullOrWhiteSpace(ParentName)
        ? Name
        : $"{ParentName}>{Name}";
}

public static class XrefClosureEval
{
    public const int DefaultMaxDepth = 4;
    public const int MinMaxDepth = 1;
    public const int AbsoluteMaxDepth = 8;

    public static int ClampMaxDepth(int? maxDepth)
    {
        var value = maxDepth ?? DefaultMaxDepth;
        return value < MinMaxDepth ? MinMaxDepth : (value > AbsoluteMaxDepth ? AbsoluteMaxDepth : value);
    }

    public static IReadOnlyList<QaFinding> EvaluateFailClosed(IEnumerable<XrefClosureNode> nodes)
    {
        var findings = new List<QaFinding>();
        foreach (var node in nodes)
        {
            if (node.WalkStatus is XrefWalkStatuses.Missing or XrefWalkStatuses.Unreadable or XrefWalkStatuses.Unloaded)
            {
                findings.Add(new QaFinding(
                    "xref_nested_incomplete",
                    "error",
                    $"Xref '{node.PinKey}' walkStatus={node.WalkStatus} path={node.Path}",
                    "Restore nested DWG or repath; pass failClosed=false to report-only.",
                    "forge_xref_closure"));
            }
        }

        return findings;
    }

    public static XrefPinNode ToPinNode(XrefClosureNode node)
    {
        long? length = null;
        DateTimeOffset? mtime = null;
        string? hash = null;
        try
        {
            if (node.WalkStatus == XrefWalkStatuses.Ok && File.Exists(node.Path))
            {
                var info = new FileInfo(node.Path);
                length = info.Length;
                mtime = info.LastWriteTimeUtc;
                hash = DependencyClosure.TryHashFile(node.Path);
            }
        }
        catch
        {
            // Best effort.
        }

        return new XrefPinNode
        {
            Name = node.PinKey,
            Path = node.Path,
            Depth = node.Depth,
            ParentName = node.ParentName,
            Length = length,
            MtimeUtc = mtime,
            ContentHash = hash
        };
    }
}
