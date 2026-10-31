namespace Forge.Shared;

/// <summary>
/// TOOL-GAP #26: an underlay whose stored path does not resolve plots silently — the sheet comes
/// out without the underlay and the probe still passes (a relative PDF path left V-101 blank).
/// Path rules are pure and unit-tested; the plugin feeds real file/dictionary reads through them.
/// </summary>
public static class UnderlayResolution
{
    /// <summary>
    /// Absolute candidate for a stored underlay path: drive/UNC-rooted paths pass through
    /// (normalised); relative paths join the drawing folder. '.', '..', '/' and '\' resolve the
    /// way AutoCAD resolves them. Null/blank input or no drawing folder → null (unresolvable).
    /// </summary>
    public static string? ResolveCandidatePath(string? storedPath, string? drawingFolder)
    {
        if (string.IsNullOrWhiteSpace(storedPath))
        {
            return null;
        }

        var raw = storedPath.Trim().Replace('/', '\\');
        try
        {
            // Drive-rooted (C:\...) and UNC (\\server\share\...) pass through.
            if (Path.IsPathFullyQualified(raw))
            {
                return Path.GetFullPath(raw);
            }

            // Rooted-but-not-qualified ('\dir\file.pdf'): resolves on the drawing's drive, like AutoCAD.
            if (raw.StartsWith('\\'))
            {
                var drive = drawingFolder is { Length: > 0 } ? Path.GetPathRoot(drawingFolder) : null;
                return drive is { Length: > 0 }
                    ? Path.GetFullPath(drive + raw[1..])
                    : Path.GetFullPath(raw);
            }

            if (string.IsNullOrWhiteSpace(drawingFolder))
            {
                return null;
            }

            return Path.GetFullPath(Path.Combine(drawingFolder, raw));
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>
    /// Status of one underlay definition, given whether its file resolved and AutoCAD's
    /// IsLoaded flag. Resolution is checked FIRST: AutoCAD reports IsLoaded=false for a
    /// definition whose file is missing at doc open, so a missing file must classify
    /// "missing" (a plot-blocking failure) whatever the load flag says — live window r5
    /// caught a missing underlay sailing through as "unloaded" and plotting a border-only
    /// PDF. "unloaded" applies only when the file resolves AND IsLoaded=false (a deliberate
    /// user unload — nothing paints, nothing is missing).
    /// </summary>
    public static string Classify(string? resolvedPath, bool isLoaded) =>
        resolvedPath is null ? "missing" : isLoaded ? "resolved" : "unloaded";

    /// <summary>
    /// Definitions whose file does not exist at the candidate path. The list is filtered by
    /// file existence only — do NOT pre-filter by IsLoaded: AutoCAD reports IsLoaded=false
    /// for a missing file's definition, so pre-filtering on the flag re-opens the r5 hole.
    /// </summary>
    public static IReadOnlyList<UnderlayFileRef> MissingUnderlays(
        IEnumerable<UnderlayFileRef> underlayRefs,
        string? drawingFolder,
        Func<string, bool> fileExists)
    {
        var missing = new List<UnderlayFileRef>();
        foreach (var underlay in underlayRefs)
        {
            var candidate = ResolveCandidatePath(underlay.Path, drawingFolder);
            if (candidate is null || !fileExists(candidate))
            {
                missing.Add(underlay);
            }
        }

        return missing;
    }
}

/// <summary>One underlay definition as a caller knows it (kind "pdf"|"dwf"|"dgn"|"image").</summary>
public sealed record UnderlayFileRef(string Kind, string Name, string Path);
