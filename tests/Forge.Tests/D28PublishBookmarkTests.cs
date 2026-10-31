using Forge.Shared;

namespace Forge.Tests;

/// <summary>
/// TOOL-GAP #28: single merged PDF + one outline bookmark per layout via a pypdf
/// post-pass. Pure contract tests (script shape, titles JSON, stdout protocol) plus
/// source guards pinning the plugin/server wiring.
/// </summary>
public sealed class D28PublishBookmarkTests
{
    [Fact]
    public void Script_WritesOutlinePerTitle_InPageOrder()
    {
        var s = PdfBookmarks.Script;
        Assert.Contains("PdfReader", s, StringComparison.Ordinal);
        Assert.Contains("PdfWriter", s, StringComparison.Ordinal);
        Assert.Contains("add_outline_item", s, StringComparison.Ordinal);
        // entry i -> page i (DSD emits sheets in layout order — no page sniffing needed)
        Assert.Contains("enumerate(titles)", s, StringComparison.Ordinal);
        // fail-closed when the merged PDF has fewer pages than titles
        Assert.Contains("page_mismatch", s, StringComparison.Ordinal);
    }

    [Fact]
    public void TitlesJson_RoundTrips_UnicodeAndOddChars()
    {
        var json = PdfBookmarks.TitlesJson(["V-101", "C-301 (A)", "Sheet \"B\""]);
        using var doc = System.Text.Json.JsonDocument.Parse(json);
        Assert.Equal("V-101", doc.RootElement[0].GetString());
        Assert.Equal("C-301 (A)", doc.RootElement[1].GetString());
        Assert.Equal("Sheet \"B\"", doc.RootElement[2].GetString());
    }

    [Fact]
    public void TryParseResult_Ok_ReadsCounts()
    {
        Assert.True(PdfBookmarks.TryParseResult("{\"ok\": true, \"bookmarks\": 3, \"pages\": 3}", out var bm, out var pages, out var err));
        Assert.Equal(3, bm);
        Assert.Equal(3, pages);
        Assert.Null(err);
    }

    [Fact]
    public void TryParseResult_PageMismatch_ReportsError()
    {
        Assert.False(PdfBookmarks.TryParseResult("{\"ok\": false, \"error\": \"page_mismatch\", \"pages\": 1, \"expected\": 2}", out _, out var pages, out var err));
        Assert.Equal(1, pages);
        Assert.Equal("page_mismatch", err);
    }

    [Fact]
    public void TryParseResult_Garbage_FailsClosed()
    {
        Assert.False(PdfBookmarks.TryParseResult("python: can't open file", out _, out _, out var err));
        Assert.Equal("no JSON result on stdout", err);
    }

    [Fact]
    public void InterpreterCandidates_PythonThenPyLauncher()
    {
        Assert.Equal(["python", "py"], PdfBookmarks.PythonCandidates);
        Assert.Empty(PdfBookmarks.ArgumentsFor("python"));
        Assert.Equal(["-3"], PdfBookmarks.ArgumentsFor("py"));
    }

    [Fact]
    public void PublishProbe_RunsAfterBookmarkPass()
    {
        // The bookmarked file is the deliverable: probe + byte count must measure the
        // file AFTER pypdf rewrote it, so the pass sits between the existence check
        // and PdfProbeResult.Probe in the singlePdf branch.
        var source = File.ReadAllText(Path.Combine(PluginDispatchSyncTests.RepoPath("src", "Forge.Plugin"), "PluginCommandProcessor.cs"));
        var passAt = source.IndexOf("RunBookmarkPass(outputPath, args.Layouts)", StringComparison.Ordinal);
        var probeAt = source.IndexOf("probe = PdfProbeResult.Probe(outputPath", StringComparison.Ordinal);
        Assert.True(passAt > 0, "bookmark pass missing from the singlePdf branch");
        Assert.True(probeAt > passAt, "probe must measure the bookmarked file");
        // gating: the pass may only run when singlePdf && bookmarks (never on per-sheet publishes)
        var gatedAt = source.IndexOf("if (args.Bookmarks)", StringComparison.Ordinal);
        var branchAt = source.IndexOf("if (args.SinglePdf)", StringComparison.Ordinal);
        Assert.True(branchAt > 0 && gatedAt > branchAt && passAt > gatedAt, "bookmark pass must sit inside singlePdf && bookmarks");
    }

    [Fact]
    public void BookmarkRunner_DrainsBothPipes_BeforeWaitForExit()
    {
        var runner = File.ReadAllText(Path.Combine(PluginDispatchSyncTests.RepoPath("src", "Forge.Plugin"), "PluginCommandProcessor.Bookmarks.cs"));
        var outAt = runner.IndexOf("ReadToEndAsync()", StringComparison.Ordinal);
        var waitAt = runner.IndexOf("WaitForExit(60_000)", StringComparison.Ordinal);
        Assert.True(outAt > 0 && waitAt > outAt, "stdout/stderr ReadToEndAsync must start before WaitForExit (full-buffer deadlock class)");
    }

    [Fact]
    public void PublishExposes_BookmarksArg_AndPayload()
    {
        var plugin = File.ReadAllText(Path.Combine(PluginDispatchSyncTests.RepoPath("src", "Forge.Plugin"), "PluginCommandProcessor.cs"));
        Assert.Contains("public bool Bookmarks { get; init; } = true;", plugin, StringComparison.Ordinal);
        Assert.Contains("bookmarksApplied = bookmarkPass?.Applied", plugin, StringComparison.Ordinal);

        var mcp = File.ReadAllText(Path.Combine(PluginDispatchSyncTests.RepoPath("src", "Forge.Server"), "ForgeMcpTools.cs"));
        Assert.Contains("bool bookmarks = true", mcp, StringComparison.Ordinal);
        Assert.Contains("bookmarks, overwriteAcknowledged", mcp, StringComparison.Ordinal);
    }

    [Fact]
    public void Notice_DeclaresPypdf()
    {
        var repoRoot = Path.GetFullPath(Path.Combine(PluginDispatchSyncTests.RepoPath("src", "Forge.Shared"), "..", ".."));
        var notice = File.ReadAllText(Path.Combine(repoRoot, "NOTICE.md"));
        Assert.Contains("pypdf", notice, StringComparison.Ordinal);
        Assert.Contains("BSD-3-Clause", notice, StringComparison.Ordinal);
    }
}
