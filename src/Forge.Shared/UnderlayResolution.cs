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

    /// <summary>Loaded underlay definitions whose file does not exist at the candidate path.</summary>
    public static IReadOnlyList<UnderlayFileRef> MissingUnderlays(
        IEnumerable<UnderlayFileRef> loadedUnderlays,
        string? drawingFolder,
        Func<string, bool> fileExists)
    {
        var missing = new List<UnderlayFileRef>();
        foreach (var underlay in loadedUnderlays)
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
