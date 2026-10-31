using System.Text.Json;

namespace Forge.Shared;

/// <summary>
/// TOOL-GAP #28: sheet bookmarks on the merged singlePdf output. AutoCAD's DSD publish
/// writes one multi-page PDF but no outline, so before the probe the plugin shells
/// out to pypdf (BSD-3-Clause — see NOTICE.md) to add one outline item per layout. The
/// page order already matches the requested layout order (DSD emits sheets in list order),
/// so entry i titles page i. This file holds the pure/testable parts: the script text,
/// the titles JSON and the stdout contract. The plugin owns process spawning.
/// </summary>
public static class PdfBookmarks
{
    /// <summary>Exit code the script uses for a page/title count mismatch (fail-closed).</summary>
    public const int PageMismatchExit = 3;

    /// <summary>
    /// The pypdf post-pass. Contract: argv[1]=source pdf, argv[2]=dest pdf,
    /// argv[3]=titles JSON array file. Prints one JSON line {"ok","bookmarks","pages","error"?}.
    /// Refuses when the page count is shorter than the title count — a wrong-page
    /// bookmark is worse than none. Requires pypdf >= 3 (PdfWriter.append/add_outline_item).
    /// </summary>
    public const string Script = """
import json, sys
from pypdf import PdfReader, PdfWriter

src, dst, titles_path = sys.argv[1], sys.argv[2], sys.argv[3]
with open(titles_path, encoding="utf-8") as fh:
    titles = json.load(fh)

reader = PdfReader(src)
if len(reader.pages) < len(titles):
    print(json.dumps({"ok": False, "error": "page_mismatch", "pages": len(reader.pages), "expected": len(titles)}))
    sys.exit(3)

writer = PdfWriter()
writer.append(reader, import_outline=False)
for i, title in enumerate(titles):
    writer.add_outline_item(str(title), i)
with open(dst, "wb") as fh:
    writer.write(fh)
print(json.dumps({"ok": True, "bookmarks": len(titles), "pages": len(writer.pages)}))
""";

    /// <summary>Python executables tried in order; the first that launches wins.</summary>
    public static readonly string[] PythonCandidates = ["python", "py"];

    /// <summary>Extra arguments per candidate — "py" needs the interpreter flag.</summary>
    public static string[] ArgumentsFor(string candidate) =>
        candidate.EndsWith("py", StringComparison.OrdinalIgnoreCase) && !candidate.EndsWith("python", StringComparison.OrdinalIgnoreCase)
            ? ["-3"]
            : [];

    /// <summary>Titles file contents: a JSON array of strings, UTF-8.</summary>
    public static string TitlesJson(IReadOnlyList<string> titles) =>
        JsonSerializer.Serialize(titles);

    /// <summary>Parses the script's single-line stdout JSON.</summary>
    public static bool TryParseResult(string stdout, out int bookmarks, out int pages, out string? error)
    {
        bookmarks = 0;
        pages = 0;
        error = null;
        try
        {
            var line = stdout.Split('\n').Select(l => l.Trim()).LastOrDefault(l => l.StartsWith('{'));
            if (line is null)
            {
                error = "no JSON result on stdout";
                return false;
            }

            using var doc = JsonDocument.Parse(line);
            var root = doc.RootElement;
            var ok = root.TryGetProperty("ok", out var okEl) && okEl.ValueKind == JsonValueKind.True;
            if (root.TryGetProperty("bookmarks", out var b) && b.ValueKind == JsonValueKind.Number && b.TryGetInt32(out var bi))
            {
                bookmarks = bi;
            }

            if (root.TryGetProperty("pages", out var p) && p.ValueKind == JsonValueKind.Number && p.TryGetInt32(out var pi))
            {
                pages = pi;
            }

            if (root.TryGetProperty("error", out var e) && e.ValueKind == JsonValueKind.String)
            {
                error = e.GetString();
            }

            return ok;
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or FormatException or OverflowException)
        {
            error = $"unparseable script output: {ex.Message}";
            return false;
        }
    }
}
