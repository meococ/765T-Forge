namespace Forge.Shared;

/// <summary>
/// Typed argument surface for forge_dgn_import (docs/cad-job-tools.md C15): convert a
/// MicroStation DGN V8 drawing into a new DWG. The source DGN is never written and the
/// active drawing is never touched — the import runs in a fresh temporary document.
/// </summary>
public sealed record DgnImportArgs
{
    public string? DgnPath { get; init; }
    public string? TargetDwgPath { get; init; }

    /// <summary>Design model inside the DGN; null/empty accepts the DGN's default model.</summary>
    public string? ModelName { get; init; }

    /// <summary>Conversion units for -DGNIMPORT: "master" (default) or "sub".</summary>
    public string? Units { get; init; }

    public bool OverwriteAcknowledged { get; init; }
}

/// <summary>
/// Pure validation/normalization for the DGN import tool so the guards are testable without
/// AutoCAD. File existence, the V7 header sniff and document-context work stay in the plugin.
/// </summary>
public static class DgnImportPolicy
{
    /// <summary>
    /// D14: DGNIMPORTMODE value scoped during the import — "import into the current drawing,
    /// ignore duplicate named objects". It must be forced: the default mode 0 would make
    /// -DGNIMPORT create a second drawing of its own instead of filling the temporary
    /// document (breaking the one-temp-document guarantee), and mode 2 prefixes every named
    /// object with the DGN file name (the client contract keeps level names as layer names).
    /// A fresh template document has no named objects for "ignore duplicates" to swallow.
    /// Note: importing into the current drawing converts DGN references to blocks; importing
    /// into a new drawing converts them to xrefs — a future arg could expose that if a job
    /// needs sibling reference DWGs.
    /// </summary>
    public const int ImportIntoCurrentDrawing = 1;

    /// <summary>
    /// The -DGNIMPORT argument list for Editor.Command/CommandAsync. Prompt order per
    /// Autodesk Help (-DGNIMPORT): filename → design model (empty accepts the DGN default)
    /// → conversion units Master/Sub → mapping setup (empty accepts the default Standard
    /// setup). An empty-string argument acts as Enter. Discrete arguments are passed as
    /// typed command inputs, so paths and model names containing spaces need no quoting.
    /// </summary>
    public static object[] CommandArguments(string dgnPath, string? modelName, string units)
        => ["-DGNIMPORT", dgnPath, modelName ?? "", units, ""];

    public static (bool Allowed, string? Code, string? Message, string? Suggestion) Validate(DgnImportArgs args)
    {
        if (string.IsNullOrWhiteSpace(args.DgnPath) || string.IsNullOrWhiteSpace(args.TargetDwgPath))
        {
            return (false, "missing_dgn_args", "dgnPath and targetDwgPath are required.", null);
        }

        var units = args.Units?.Trim();
        if (!string.IsNullOrEmpty(units)
            && !units.Equals("master", StringComparison.OrdinalIgnoreCase)
            && !units.Equals("sub", StringComparison.OrdinalIgnoreCase))
        {
            return (false, "dgn_units_invalid", $"units must be master or sub (got '{args.Units}').",
                "master keeps the DGN master-unit scale, sub imports at the finer sub-unit scale — the wrong choice rescales the whole drawing.");
        }

        foreach (var (field, value) in new[] { ("dgnPath", args.DgnPath), ("targetDwgPath", args.TargetDwgPath), ("modelName", args.ModelName) })
        {
            if (!string.IsNullOrEmpty(value) && value.IndexOfAny(Newlines) >= 0)
            {
                return (false, "illegal_control_character",
                    $"Parameter '{field}' contains a newline; single-line values are required.",
                    "Remove CR/LF characters from the path or model name.");
            }
        }

        return (true, null, null, null);
    }

    /// <summary>Normalizes the units argument to the exact -DGNIMPORT prompt token.</summary>
    public static string NormalizeUnits(string? units)
    {
        return string.IsNullOrWhiteSpace(units) || units.Trim().Equals("master", StringComparison.OrdinalIgnoreCase)
            ? "Master"
            : "Sub";
    }

    public static string CanonicalPath(string path)
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

    /// <summary>
    /// Intergraph/MicroStation V7 design files open with the 0x0809 (or byte-swapped 0x0908)
    /// TCB word; DGN V8 is a different container. AutoCAD's importer takes V8 only, so a V7
    /// header is refused by name before any command is dispatched. Best-effort sniff: a header
    /// that is neither pattern is assumed V8 and left to the importer plus post-import
    /// verification to judge.
    /// </summary>
    public static bool LooksLikeV7Header(byte[]? header)
    {
        return header is { Length: >= 2 }
            && ((header[0] == 0x08 && header[1] == 0x09) || (header[0] == 0x09 && header[1] == 0x08));
    }

    private static readonly char[] Newlines = { '\r', '\n' };
}
