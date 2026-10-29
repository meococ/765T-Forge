namespace Forge.Shared;

/// <summary>Optional multileader tag drawn with one bulk-insert item.</summary>
public sealed record BulkLeaderSpec
{
    /// <summary>Arrow tip first, landing last. With only a landing point the arrow tip is the insert position.</summary>
    public double[][]? Points { get; init; }

    public string? Text { get; init; }
    public double? TextHeight { get; init; }
    public string? Layer { get; init; }
    public string? Style { get; init; }
    public bool? BackgroundMask { get; init; }
}

/// <summary>One block reference to create (wire shape of forge_block_insert_bulk items[]).</summary>
public sealed record BulkInsertItem
{
    public string? Name { get; init; }
    public double[]? Position { get; init; }
    public double? RotationDeg { get; init; }
    public double? Scale { get; init; }
    public string? Layer { get; init; }
    public int? ColorIndex { get; init; }
    public Dictionary<string, string>? Attributes { get; init; }
    public BulkLeaderSpec? Leader { get; init; }
}

/// <summary>An item after defaults were merged and every number validated.</summary>
public sealed record ResolvedInsert(
    int Index,
    string Name,
    double[] Position,
    double RotationRad,
    double Scale,
    string? Layer,
    int? ColorIndex,
    IReadOnlyDictionary<string, string> Attributes,
    ResolvedLeader? Leader);

public sealed record ResolvedLeader(double[][] Points, string? Text, double? TextHeight, string? Layer, string? Style, bool BackgroundMask);

public sealed record BulkInsertError(int Index, string Code, string Message);

/// <summary>
/// Pure validation + default merging for forge_block_insert_bulk. All-or-nothing: the plugin
/// creates nothing unless every item resolves, so one call either lands the whole set or none.
/// </summary>
public static class BlockBulkInsert
{
    public const int MaxItems = 2000;

    public static IReadOnlyList<ResolvedInsert> Resolve(
        IReadOnlyList<BulkInsertItem?>? items,
        string? defaultName,
        double? defaultScale,
        double? defaultRotationDeg,
        string? defaultLayer,
        string? defaultLeaderLayer,
        string? defaultLeaderStyle,
        double? defaultLeaderTextHeight,
        out List<BulkInsertError> errors)
    {
        errors = [];
        var resolved = new List<ResolvedInsert>();
        if (items is null || items.Count == 0)
        {
            errors.Add(new BulkInsertError(-1, "missing_items", "items is required and must not be empty."));
            return resolved;
        }

        if (items.Count > MaxItems)
        {
            errors.Add(new BulkInsertError(-1, "batch_too_large", $"forge_block_insert_bulk accepts at most {MaxItems} items; split the set."));
            return resolved;
        }

        for (var index = 0; index < items.Count; index++)
        {
            var item = items[index] ?? new BulkInsertItem();
            var name = string.IsNullOrWhiteSpace(item.Name) ? defaultName?.Trim() : item.Name.Trim();
            if (string.IsNullOrWhiteSpace(name))
            {
                errors.Add(new BulkInsertError(index, "missing_block_name", "Item has no name and no default blockName was given."));
                continue;
            }

            if (!DrawingImport.IsValidSymbolName(name))
            {
                errors.Add(new BulkInsertError(index, "invalid_block_name", $"Invalid block name '{name}'."));
                continue;
            }

            if (!TryPoint(item.Position, out var position))
            {
                errors.Add(new BulkInsertError(index, "invalid_position", "position must be [x, y] or [x, y, z] with finite values."));
                continue;
            }

            var scale = item.Scale ?? defaultScale ?? 1.0;
            if (!(scale > 0) || !double.IsFinite(scale))
            {
                errors.Add(new BulkInsertError(index, "invalid_scale", "scale must be a finite number > 0."));
                continue;
            }

            var rotationDeg = item.RotationDeg ?? defaultRotationDeg ?? 0;
            if (!double.IsFinite(rotationDeg))
            {
                errors.Add(new BulkInsertError(index, "invalid_rotation", "rotationDeg must be finite."));
                continue;
            }

            if (item.ColorIndex is < 0 or > 256)
            {
                errors.Add(new BulkInsertError(index, "invalid_args", "colorIndex must be 0 (ByBlock), 1–255, or 256 (ByLayer)."));
                continue;
            }

            var layer = string.IsNullOrWhiteSpace(item.Layer) ? defaultLayer : item.Layer;
            if (layer is not null && !DrawingImport.IsValidSymbolName(layer))
            {
                errors.Add(new BulkInsertError(index, "invalid_layer", $"Invalid layer name '{layer}'."));
                continue;
            }

            ResolvedLeader? leader = null;
            if (item.Leader is { } spec)
            {
                if (!TryLeader(spec, position, defaultLeaderLayer ?? layer, defaultLeaderStyle, defaultLeaderTextHeight, out leader, out var leaderError))
                {
                    errors.Add(new BulkInsertError(index, "invalid_leader", leaderError!));
                    continue;
                }
            }

            var attributes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (item.Attributes is { Count: > 0 })
            {
                foreach (var pair in item.Attributes)
                {
                    attributes[pair.Key] = pair.Value ?? "";
                }
            }

            resolved.Add(new ResolvedInsert(index, name!, position, rotationDeg * Math.PI / 180.0, scale, layer, item.ColorIndex, attributes, leader));
        }

        return resolved;
    }

    /// <summary>Distinct block names the resolved set needs, in first-use order.</summary>
    public static IReadOnlyList<string> DistinctNames(IEnumerable<ResolvedInsert> items) =>
        items.Select(i => i.Name).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

    private static bool TryLeader(BulkLeaderSpec spec, double[] position, string? layer, string? style, double? height, out ResolvedLeader? leader, out string? error)
    {
        leader = null;
        error = null;
        var raw = spec.Points ?? [];
        var points = new List<double[]>();
        foreach (var p in raw)
        {
            if (!TryPoint(p, out var point))
            {
                error = "leader.points must be [x, y] or [x, y, z] points with finite values.";
                return false;
            }

            points.Add(point);
        }

        if (points.Count == 1)
        {
            // A single point is the landing; the arrow tip is the block's insertion point.
            points.Insert(0, position);
        }

        if (points.Count < 2)
        {
            error = "leader.points needs a landing point (arrow tip defaults to the insert position) or at least 2 points.";
            return false;
        }

        var textHeight = spec.TextHeight ?? height;
        if (textHeight is { } h && !(h > 0 && double.IsFinite(h)))
        {
            error = "leader.textHeight must be > 0.";
            return false;
        }

        leader = new ResolvedLeader(
            points.ToArray(),
            spec.Text,
            textHeight,
            string.IsNullOrWhiteSpace(spec.Layer) ? layer : spec.Layer,
            string.IsNullOrWhiteSpace(spec.Style) ? style : spec.Style,
            spec.BackgroundMask ?? false);
        return true;
    }

    private static bool TryPoint(double[]? value, out double[] point)
    {
        point = [0, 0, 0];
        if (value is null || value.Length is < 2 or > 3 || value.Any(v => !double.IsFinite(v)))
        {
            return false;
        }

        point = [value[0], value[1], value.Length > 2 ? value[2] : 0];
        return true;
    }
}
