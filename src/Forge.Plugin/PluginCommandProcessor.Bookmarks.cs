using System.Diagnostics;
using System.Text;
using Forge.Shared;

namespace Forge.Plugin;

// TOOL-GAP #28: pypdf bookmark post-pass for forge_plot_publish(singlePdf=true). The DSD
// merge writes one multi-page PDF with no outline; a bounded python child process adds one
// outline item per layout in page order. Never fatal — a missing interpreter or a script
// error reports bookmarksApplied:false and the publish still succeeds.
public sealed partial class PluginCommandProcessor
{
    private sealed record BookmarkPassResult(bool Applied, int Count, string? Engine, string? Error)
    {
        public object ToPayload() => new { applied = Applied, count = Count, engine = Engine, error = Error };
    }

    /// <summary>Writes one outline item per layout title onto <paramref name="pdfPath"/> via pypdf.</summary>
    private static BookmarkPassResult RunBookmarkPass(string pdfPath, IReadOnlyList<string> titles)
    {
        if (titles.Count == 0)
        {
            return new BookmarkPassResult(false, 0, null, "no layouts to bookmark");
        }

        var tempDir = Path.Combine(Path.GetTempPath(), $"forge-bookmark-{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(tempDir);
            var scriptPath = Path.Combine(tempDir, "forge_bookmark.py");
            var titlesPath = Path.Combine(tempDir, "titles.json");
            var outPath = Path.Combine(tempDir, "bookmarked.pdf");
            File.WriteAllText(scriptPath, PdfBookmarks.Script, new UTF8Encoding(false));
            File.WriteAllText(titlesPath, PdfBookmarks.TitlesJson(titles), new UTF8Encoding(false));

            string? lastError = null;
            foreach (var candidate in PdfBookmarks.PythonCandidates)
            {
                var psi = new ProcessStartInfo
                {
                    FileName = candidate,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                };
                foreach (var a in PdfBookmarks.ArgumentsFor(candidate))
                {
                    psi.ArgumentList.Add(a);
                }

                psi.ArgumentList.Add(scriptPath);
                psi.ArgumentList.Add(pdfPath);
                psi.ArgumentList.Add(outPath);
                psi.ArgumentList.Add(titlesPath);

                string? stdout;
                string? stderrTail;
                int exitCode;
                try
                {
                    using var proc = System.Diagnostics.Process.Start(psi);
                    if (proc is null)
                    {
                        lastError = $"{candidate} did not start";
                        continue;
                    }

                    // Read both pipes async before waiting — a full stderr buffer otherwise
                    // deadlocks the child (the accoreconsole stall class). The drain itself
                    // is bounded too: a handle-inheriting survivor could otherwise stall it.
                    var outTask = proc.StandardOutput.ReadToEndAsync();
                    var errTask = proc.StandardError.ReadToEndAsync();
                    if (!proc.WaitForExit(60_000))
                    {
                        try { proc.Kill(); } catch { /* best-effort */ }
                        return new BookmarkPassResult(false, 0, "pypdf", $"bookmark pass timed out after 60 s ({candidate})");
                    }

                    exitCode = proc.ExitCode;
                    var drained = Task.WhenAll(outTask, errTask);
                    drained.Wait(10_000);
                    stdout = outTask.IsCompleted ? outTask.Result : null;
                    stderrTail = errTask.IsCompleted ? errTask.Result : null;
                }
                catch (System.ComponentModel.Win32Exception)
                {
                    lastError = $"{candidate} not found";
                    continue; // try the next interpreter candidate
                }

                string? parseError = null;
                if (stdout is not null && PdfBookmarks.TryParseResult(stdout, out var count, out _, out parseError))
                {
                    File.Copy(outPath, pdfPath, overwrite: true);
                    return new BookmarkPassResult(true, count, "pypdf", null);
                }

                // stderr carries the actionable cause (e.g. "No module named 'pypdf'") —
                // always surface a tail of it, even when stdout held unparseable JSON.
                var stderrNote = string.IsNullOrWhiteSpace(stderrTail)
                    ? null
                    : $"; stderr: {stderrTail.Trim()[..Math.Min(300, stderrTail.Trim().Length)]}";
                lastError = (parseError ?? $"{candidate} exited {exitCode}") + stderrNote;
                if (exitCode == PdfBookmarks.PageMismatchExit)
                {
                    break; // page/title mismatch is definitive — retrying another interpreter won't help
                }
            }

            return new BookmarkPassResult(false, 0, "pypdf", lastError ?? "no python interpreter found");
        }
        catch (System.Exception ex)
        {
            return new BookmarkPassResult(false, 0, "pypdf", ex.Message);
        }
        finally
        {
            try { Directory.Delete(tempDir, recursive: true); } catch { /* best-effort temp cleanup */ }
        }
    }
}
