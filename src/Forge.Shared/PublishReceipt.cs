using System.Text.Json;
using System.Text.RegularExpressions;

namespace Forge.Shared;

/// <summary>
/// Evidence artifact for a plot/publish run — agents and humans can audit like a plot log.
/// </summary>
public sealed class PublishReceipt
{
    public string ReceiptId { get; init; } = Guid.NewGuid().ToString("N");
    public DateTimeOffset CreatedUtc { get; init; } = DateTimeOffset.UtcNow;
    public string? DocumentPath { get; init; }
    public string? OutputPath { get; init; }
    public string[] Layouts { get; init; } = [];
    public string? DeviceName { get; init; }
    public string? PaperSize { get; init; }
    public string? PlotStyle { get; init; }
    public bool SinglePdf { get; init; } = true;
    public string? PreflightReportId { get; init; }
    public string? PreflightHash { get; init; }
    public string? AuditId { get; init; }
    public long? OutputBytes { get; init; }
    public DateTimeOffset? OutputMtimeUtc { get; init; }
    public PdfProbeResult? PdfProbe { get; init; }

    /// <summary>TOOL-GAP #28: outline items written onto the merged PDF (null when bookmarks were not requested/applied).</summary>
    public int? BookmarkCount { get; init; }

    /// <summary>TOOL-GAP #28: why bookmarks are absent even though requested (missing interpreter, script error, page mismatch).</summary>
    public string? BookmarkError { get; init; }
    public bool VerificationPassed { get; init; }
    public string? Message { get; set; }
    public string? ArtifactPath { get; set; }

    public string ComputePreflightHash(QaReport? report)
    {
        if (report is null)
        {
            return "";
        }

        var payload = JsonSerializer.Serialize(new
        {
            report.Id,
            report.Passed,
            findings = report.Findings.Select(f => new { f.Code, f.Severity, f.Message })
        }, ForgeJson.Options);
        var bytes = System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(payload));
        return Convert.ToHexString(bytes)[..16];
    }

    public static string? TryWriteArtifact(PublishReceipt receipt, string? auditDir = null)
        => TryWriteArtifact(receipt, out _, auditDir);

    public static string? TryWriteArtifact(PublishReceipt receipt, out string? error, string? auditDir = null)
    {
        try
        {
            var root = auditDir
                       ?? Path.Combine(
                           Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                           "765T-Forge",
                           "receipts");
            Directory.CreateDirectory(root);
            var path = Path.Combine(root, $"receipt-{receipt.ReceiptId}.json");
            File.WriteAllText(path, JsonSerializer.Serialize(receipt, ForgeJson.Options));
            error = null;
            return path;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return null;
        }
    }
}

public sealed class PdfProbeResult
{
    public bool Exists { get; init; }
    public long Bytes { get; init; }
    public bool NonEmpty { get; init; }
    public int? PageCount { get; init; }
    public int? ExpectedPages { get; init; }
    public bool PageCountMatches { get; init; }
    public bool Passed { get; init; }
    public string? Message { get; init; }
    public string[] Warnings { get; init; } = [];

    /// <summary>
    /// Lightweight PDF probe: existence, size, %PDF- header, optional freshness (mtime &gt;= notBeforeUtc),
    /// and page-count via /Type /Page counts (heuristic).
    /// </summary>
    public static PdfProbeResult Probe(string path, int? expectedPages = null, DateTimeOffset? notBeforeUtc = null)
    {
        var warnings = new List<string>();
        if (!File.Exists(path))
        {
            return new PdfProbeResult
            {
                Exists = false,
                Passed = false,
                ExpectedPages = expectedPages,
                Message = "PDF output does not exist."
            };
        }

        var info = new FileInfo(path);
        var bytes = info.Length;
        if (bytes <= 0)
        {
            return new PdfProbeResult
            {
                Exists = true,
                Bytes = 0,
                NonEmpty = false,
                ExpectedPages = expectedPages,
                Passed = false,
                Message = "PDF exists but is zero bytes."
            };
        }

        if (notBeforeUtc.HasValue && info.LastWriteTimeUtc < notBeforeUtc.Value.UtcDateTime.AddSeconds(-1))
        {
            return new PdfProbeResult
            {
                Exists = true,
                Bytes = bytes,
                NonEmpty = true,
                ExpectedPages = expectedPages,
                Passed = false,
                Message = $"PDF output is stale: last-write time ({info.LastWriteTimeUtc:O}) precedes plot start ({notBeforeUtc.Value.UtcDateTime:O})."
            };
        }

        int? pageCount = null;
        var hasPdfHeader = false;
        var hasPdfTrailer = false;
        try
        {
            var text = File.ReadAllText(path);
            hasPdfHeader = text.StartsWith("%PDF-", StringComparison.Ordinal);
            if (!hasPdfHeader)
            {
                return new PdfProbeResult
                {
                    Exists = true,
                    Bytes = bytes,
                    NonEmpty = true,
                    ExpectedPages = expectedPages,
                    Passed = false,
                    Message = "Output file is not a valid PDF (missing %PDF- header)."
                };
            }

            hasPdfTrailer = text.Contains("%%EOF", StringComparison.Ordinal);
            if (!hasPdfTrailer)
            {
                return new PdfProbeResult
                {
                    Exists = true,
                    Bytes = bytes,
                    NonEmpty = true,
                    ExpectedPages = expectedPages,
                    Passed = false,
                    Message = "Output PDF is incomplete or truncated (missing %%EOF trailer)."
                };
            }

            // Heuristic: count "/Type /Page" not "/Type /Pages"
            var matches = Regex.Matches(text, @"/Type\s*/Page\b");
            pageCount = matches.Count;
            if (pageCount == 0)
            {
                warnings.Add("Could not detect PDF page objects; pageCount treated as unknown.");
                pageCount = null;
            }
        }
        catch (Exception ex)
        {
            warnings.Add($"PDF text probe failed: {ex.Message}");
        }

        var pageOk = expectedPages is null
            ? (pageCount is null || pageCount > 0)
            : (pageCount is not null && pageCount == expectedPages);
        if (expectedPages is not null && pageCount != expectedPages)
        {
            warnings.Add($"Page count {(pageCount?.ToString() ?? "unknown")} != expected {expectedPages}.");
        }

        var passed = bytes > 0 && hasPdfHeader && hasPdfTrailer && pageOk;
        return new PdfProbeResult
        {
            Exists = true,
            Bytes = bytes,
            NonEmpty = bytes > 0,
            PageCount = pageCount,
            ExpectedPages = expectedPages,
            PageCountMatches = pageOk,
            Passed = passed,
            Message = passed ? "PDF probe passed." : "PDF probe failed.",
            Warnings = warnings.ToArray()
        };
    }
}
