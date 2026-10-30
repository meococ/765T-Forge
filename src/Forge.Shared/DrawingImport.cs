using System.Globalization;

namespace Forge.Shared;

/// <summary>How forge_doc_import brings an external DXF/DWG into the active drawing.</summary>
public enum DrawingImportMode
{
    /// <summary>One block definition + (optionally) one INSERT — the drawing stays a single selectable object.</summary>
    Block,

    /// <summary>The source model space is inserted and exploded into the active model space (one native Database.Insert).</summary>
    Merge,

    /// <summary>The source DWG is attached as an external reference (a DXF is first saved to a DWG path).</summary>
    Xref
}

/// <summary>One source layer and the name it is given in the active drawing.</summary>
public sealed record LayerRename(string Source, string Target);

/// <summary>
/// Result of planning a layer map/prefix against the source drawing's layer table. Renames are
/// one-source-to-a-free-name and are done by renaming the layer record (keeps colour/linetype);
/// merges are many-to-one or onto a name the source already uses and are done by moving entities.
/// </summary>
public sealed record LayerMapPlan(
    IReadOnlyList<LayerRename> Renames,
    IReadOnlyList<LayerRename> Merges,
    IReadOnlyList<string> Protected,
    IReadOnlyList<string> Invalid)
{
    public int Changed => Renames.Count + Merges.Count;

    public IReadOnlyDictionary<string, string> AsDictionary()
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var r in Renames.Concat(Merges))
        {
            map[r.Source] = r.Target;
        }

        return map;
    }
}

/// <summary>
/// Pure planning for forge_doc_import: mode/extension parsing, INSUNITS scaling, layer map and
/// prefix resolution, block-name sanitising and the merge transform. The plugin owns the AutoCAD
/// calls (ReadDwgFile/DxfIn, Database.Insert, AttachXref); everything decidable without AutoCAD
/// lives here so it is unit-tested.
/// </summary>
public static class DrawingImport
{
    public const int MaxReturnedHandles = 5000;

    /// <summary>D11 failure code: the import ran but the read-back could not prove it.</summary>
    public const string ImportVerificationFailedCode = "import_verification_failed";

    /// <summary>
    /// smoke-r2 D11 result rule: ok derives from verification, and the created count is what
    /// the read-back actually counted — never "the call returned without throwing".
    /// - merge: only entities read back in MODEL SPACE count (the bug let them land on a
    ///   paper layout while model space stayed empty — verified=false).
    /// - block: entities in the new definition, or a produced INSERT handle.
    /// - xref:  the attach produced a definition (block handle); an INSERT is optional.
    /// </summary>
    public static bool ImportVerified(DrawingImportMode mode, int created, bool hasInsert, bool xrefAttached)
        => mode switch
        {
            DrawingImportMode.Merge => created > 0,
            DrawingImportMode.Xref => xrefAttached,
            _ => created > 0 || hasInsert
        };

    /// <summary>
    /// review-wp2-r1 finding 10 (D11): a partial import is success-with-warning, never silent.
    /// When fewer entities land than the source model space held, or clones were skipped or
    /// left untransformed, the warning names BOTH counts (created of sourceCount). An xref is
    /// exempt: it copies nothing into the drawing by design.
    /// </summary>
    public static string? PartialImportWarning(DrawingImportMode mode, int created, int sourceCount, int skippedOrUntransformed)
    {
        if (mode == DrawingImportMode.Xref || (created >= sourceCount && skippedOrUntransformed <= 0))
        {
            return null;
        }

        var detail = skippedOrUntransformed > 0
            ? $" ({skippedOrUntransformed} clone(s) skipped or left untransformed — proxied or non-entity content?)"
            : "";
        return $"Partial import: {created} of {sourceCount} source model-space entities landed in the target{detail}. Inspect the drawing before relying on the import.";
    }

    /// <summary>
    /// D11 result composition: success only when verification passed; otherwise
    /// <see cref="ImportVerificationFailedCode"/> carrying the same data and verification,
    /// so callers see the real read-back instead of a silent ok=true.
    /// </summary>
    public static ForgeResult ImportResult(
        string commandId,
        bool verified,
        object? data,
        ForgeVerification verification,
        string failureMessage,
        string? suggestion = null)
        => verified
            ? ForgeResult.Success(commandId, data, verification: verification)
            : ForgeResult.Failure(commandId, ImportVerificationFailedCode, failureMessage, suggestion, data: data, verification: verification);

    /// <summary>Layers that can never be renamed or merged away.</summary>
    public static readonly string[] ProtectedLayers = ["0", "Defpoints"];

    private const string InvalidSymbolChars = "<>/\\\":;?*|=`";

    public static bool TryParseMode(string? value, out DrawingImportMode mode, out string? error)
    {
        error = null;
        switch (value?.Trim().ToLowerInvariant())
        {
            case null or "" or "block" or "insert" or "insert_as_block" or "insert-as-block" or "asblock":
                mode = DrawingImportMode.Block;
                return true;
            case "merge" or "explode" or "insert_explode" or "insert-explode":
                mode = DrawingImportMode.Merge;
                return true;
            case "xref" or "attach" or "attach_xref" or "attach-xref":
                mode = DrawingImportMode.Xref;
                return true;
            default:
                mode = DrawingImportMode.Block;
                error = $"Unknown import mode '{value}'. Use block, merge or xref.";
                return false;
        }
    }

    /// <summary>"dxf" or "dwg" for a supported source; null otherwise. DWT/DWS read like DWG.</summary>
    public static string? SourceKind(string? path)
    {
        var ext = Path.GetExtension(path ?? "").ToLowerInvariant();
        return ext switch
        {
            ".dxf" => "dxf",
            ".dwg" or ".dwt" or ".dws" => "dwg",
            _ => null
        };
    }

    /// <summary>Metres per drawing unit for an INSUNITS code; null for unitless (0) or unknown codes.</summary>
    public static double? MetersPerUnit(int insunits) => insunits switch
    {
        1 => 0.0254,
        2 => 0.3048,
        3 => 1609.344,
        4 => 0.001,
        5 => 0.01,
        6 => 1.0,
        7 => 1000.0,
        8 => 0.0254e-6,
        9 => 0.0254e-3,
        10 => 0.9144,
        11 => 1e-10,
        12 => 1e-9,
        13 => 1e-6,
        14 => 0.1,
        15 => 10.0,
        16 => 100.0,
        17 => 1e9,
        18 => 1.495978707e11,
        19 => 9.4607304725808e15,
        20 => 3.0856775814913673e16,
        21 => 1200.0 / 3937.0,
        22 => 100.0 / 3937.0,
        23 => 3600.0 / 3937.0,
        24 => 6336000.0 / 3937.0,
        _ => null
    };

    public static string UnitName(int insunits) => insunits switch
    {
        0 => "unitless",
        1 => "inches",
        2 => "feet",
        3 => "miles",
        4 => "millimeters",
        5 => "centimeters",
        6 => "meters",
        7 => "kilometers",
        8 => "microinches",
        9 => "mils",
        10 => "yards",
        11 => "angstroms",
        12 => "nanometers",
        13 => "microns",
        14 => "decimeters",
        15 => "decameters",
        16 => "hectometers",
        17 => "gigameters",
        18 => "astronomical units",
        19 => "light years",
        20 => "parsecs",
        21 => "US survey feet",
        22 => "US survey inches",
        23 => "US survey yards",
        24 => "US survey miles",
        _ => "unknown(" + insunits.ToString(CultureInfo.InvariantCulture) + ")"
    };

    /// <summary>
    /// Scale that converts source drawing units into target drawing units (INSERT's INSUNITS rule).
    /// Unitless or unknown on either side → 1.0 plus a warning when the codes differ.
    /// </summary>
    public static double UnitScale(int sourceInsunits, int targetInsunits, out string? warning)
    {
        warning = null;
        if (sourceInsunits == targetInsunits)
        {
            return 1.0;
        }

        var source = MetersPerUnit(sourceInsunits);
        var target = MetersPerUnit(targetInsunits);
        if (source is null || target is null)
        {
            warning = $"Unit scaling skipped: source is {UnitName(sourceInsunits)}, target is {UnitName(targetInsunits)}; geometry is imported 1:1. Pass scale explicitly if the units differ.";
            return 1.0;
        }

        return source.Value / target.Value;
    }

    /// <summary>AutoCAD symbol-name rule used for layers and blocks (no reserved characters, ≤255, no edge spaces).</summary>
    public static bool IsValidSymbolName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Length > 255 || name != name.Trim())
        {
            return false;
        }

        return name.IndexOfAny(InvalidSymbolChars.ToCharArray()) < 0 && !name.Any(char.IsControl);
    }

    /// <summary>Block name derived from a file name: reserved characters become '_', never empty.</summary>
    public static string SanitizeBlockName(string? raw)
    {
        var name = (raw ?? "").Trim();
        if (name.Length == 0)
        {
            return "IMPORT";
        }

        var chars = name.Select(c => InvalidSymbolChars.IndexOf(c) >= 0 || char.IsControl(c) ? '_' : c).ToArray();
        var clean = new string(chars).Trim();
        if (clean.Length > 255)
        {
            clean = clean[..255].TrimEnd();
        }

        // Names starting with '*' are anonymous; the sanitiser already replaced '*', so a clean name
        // that is still empty after trimming falls back to IMPORT.
        return clean.Length == 0 ? "IMPORT" : clean;
    }

    /// <summary>
    /// Resolves the target name of one source layer. Exact map keys win over wildcard keys; among
    /// wildcard keys the longest (most specific) pattern wins, ties broken ordinally so the result
    /// never depends on JSON key order. A '*' in the target is replaced by what the key's single '*'
    /// matched ("A-*" -> "M-*" maps A-WALL to M-WALL; "*" -> "X-*" maps WALL to X-WALL); for exact keys
    /// and keys with several wildcards or '?' it is replaced by the whole source name. The
    /// prefix applies only to layers the map did not touch. Protected layers return unchanged.
    /// </summary>
    public static string MapLayer(string source, IReadOnlyDictionary<string, string>? map, string? prefix)
    {
        if (IsProtectedLayer(source))
        {
            return source;
        }

        if (map is { Count: > 0 })
        {
            foreach (var pair in map)
            {
                if (!WildcardSet.IsWildcard(pair.Key) && string.Equals(pair.Key, source, StringComparison.OrdinalIgnoreCase))
                {
                    return ApplyTarget(pair.Value, source);
                }
            }

            var best = map
                .Where(pair => WildcardSet.IsWildcard(pair.Key) && WildcardSet.MatchOne(pair.Key, source))
                .OrderByDescending(pair => pair.Key.Replace("*", "", StringComparison.Ordinal).Length)
                .ThenBy(pair => pair.Key, StringComparer.Ordinal)
                .Select(pair => (KeyValuePair<string, string>?)pair)
                .FirstOrDefault();
            if (best is { } hit)
            {
                return ApplyTarget(hit.Value, WildcardCapture(hit.Key, source));
            }
        }

        return string.IsNullOrEmpty(prefix) ? source : prefix + source;
    }

    public static bool IsProtectedLayer(string name) =>
        ProtectedLayers.Any(p => string.Equals(p, name, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Plans the layer renames/merges for a source layer table. A target that is already the name
    /// of another source layer, or that several sources map onto, becomes a merge (entities move);
    /// otherwise the layer record is renamed. Invalid target names are reported, never applied.
    /// </summary>
    public static LayerMapPlan PlanLayerMap(IEnumerable<string> sourceLayers, IReadOnlyDictionary<string, string>? map, string? prefix)
    {
        var sources = sourceLayers.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var existing = new HashSet<string>(sources, StringComparer.OrdinalIgnoreCase);
        var protectedHits = new List<string>();
        var invalid = new List<string>();
        var wanted = new List<LayerRename>();
        foreach (var source in sources)
        {
            if (IsProtectedLayer(source))
            {
                if (map is not null && map.Keys.Any(k => !WildcardSet.IsWildcard(k) && string.Equals(k, source, StringComparison.OrdinalIgnoreCase)))
                {
                    protectedHits.Add(source);
                }

                continue;
            }

            var target = MapLayer(source, map, prefix);
            if (string.Equals(target, source, StringComparison.Ordinal))
            {
                continue;
            }

            if (!IsValidSymbolName(target))
            {
                invalid.Add($"{source} -> {target}");
                continue;
            }

            wanted.Add(new LayerRename(source, target));
        }

        var renames = new List<LayerRename>();
        var merges = new List<LayerRename>();
        foreach (var group in wanted.GroupBy(w => w.Target, StringComparer.OrdinalIgnoreCase))
        {
            var members = group.ToList();
            var target = members[0].Target;

            // A case-only rename of a layer onto itself ("wall" -> "WALL") is a plain rename.
            var targetIsOtherSource = existing.Contains(target) &&
                                      !(members.Count == 1 && string.Equals(members[0].Source, target, StringComparison.OrdinalIgnoreCase));
            if (members.Count == 1 && !targetIsOtherSource)
            {
                renames.Add(members[0]);
                continue;
            }

            if (targetIsOtherSource)
            {
                merges.AddRange(members);
                continue;
            }

            // Several sources onto a free name: rename the first (keeps its properties), move the rest.
            var ordered = members.OrderBy(m => m.Source, StringComparer.OrdinalIgnoreCase).ToList();
            renames.Add(ordered[0]);
            merges.AddRange(ordered.Skip(1));
        }

        return new LayerMapPlan(renames, merges, protectedHits, invalid);
    }

    /// <summary>
    /// Row-major 4x4 (16 values, Matrix3d layout) for merge mode:
    /// T(position) · Rz(rotation) · S(scale) · T(-basePoint). Source point basePoint lands on position.
    /// </summary>
    public static double[] MergeTransform(double[] position, double scale, double rotationRad, double[]? basePoint)
    {
        var px = position.Length > 0 ? position[0] : 0;
        var py = position.Length > 1 ? position[1] : 0;
        var pz = position.Length > 2 ? position[2] : 0;
        var bx = basePoint is { Length: > 0 } ? basePoint[0] : 0;
        var by = basePoint is { Length: > 1 } ? basePoint[1] : 0;
        var bz = basePoint is { Length: > 2 } ? basePoint[2] : 0;
        var c = Math.Cos(rotationRad);
        var s = Math.Sin(rotationRad);
        var a = c * scale;
        var b = -s * scale;
        var d = s * scale;
        var e = c * scale;
        return
        [
            a, b, 0, px - (a * bx + b * by),
            d, e, 0, py - (d * bx + e * by),
            0, 0, scale, pz - scale * bz,
            0, 0, 0, 1
        ];
    }

    /// <summary>Union of [minX,minY,minZ,maxX,maxY,maxZ] boxes; null when none.</summary>
    public static double[]? UnionExtents(double[]? acc, double[]? box)
    {
        if (box is not { Length: 6 } || box.Any(v => !double.IsFinite(v)))
        {
            return acc;
        }

        if (acc is null)
        {
            return (double[])box.Clone();
        }

        return
        [
            Math.Min(acc[0], box[0]), Math.Min(acc[1], box[1]), Math.Min(acc[2], box[2]),
            Math.Max(acc[3], box[3]), Math.Max(acc[4], box[4]), Math.Max(acc[5], box[5])
        ];
    }

    /// <summary>Increments a counter in a case-insensitive tally.</summary>
    public static void Tally(IDictionary<string, int> counts, string key)
    {
        counts[key] = counts.TryGetValue(key, out var n) ? n + 1 : 1;
    }

    /// <summary>Validates a [x,y] or [x,y,z] point; error names the parameter.</summary>
    public static bool TryPoint(double[]? value, string name, out double[] point, out string? error)
    {
        error = null;
        point = [0, 0, 0];
        if (value is null)
        {
            return true;
        }

        if (value.Length is < 2 or > 3 || value.Any(v => !double.IsFinite(v)))
        {
            error = $"{name} must be [x, y] or [x, y, z] with finite values.";
            return false;
        }

        point = [value[0], value[1], value.Length > 2 ? value[2] : 0];
        return true;
    }

    /// <summary>
    /// The text matched by the only '*' of <paramref name="pattern"/>; the whole name when the
    /// pattern has no '*', several of them, or a '?' (no single unambiguous capture).
    /// </summary>
    public static string WildcardCapture(string pattern, string source)
    {
        var star = pattern.IndexOf('*', StringComparison.Ordinal);
        if (star < 0 || pattern.IndexOf('*', star + 1) >= 0 || pattern.Contains('?', StringComparison.Ordinal))
        {
            return source;
        }

        var fixedLength = pattern.Length - 1;
        return source.Length < fixedLength ? source : source.Substring(star, source.Length - fixedLength);
    }

    private static string ApplyTarget(string target, string source) =>
        target.Contains('*', StringComparison.Ordinal) ? target.Replace("*", source, StringComparison.Ordinal) : target;
}
