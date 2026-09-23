using System.Text;
using System.Text.Json;

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
    public bool VerificationPassed { get; init; }
    public string? Message { get; init; }
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
        using var sha = System.Security.Cryptography.SHA256.Create();
        var bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(payload));
        return Hex.Encode(bytes).Substring(0, 16);
    }

    /// <summary>
    /// Writes the receipt artifact. On failure <paramref name="error"/> carries the exact
    /// reason and the return value is null. The previous implementation swallowed the
    /// exception and returned a bare null, so a publish could report success while its
    /// receipt silently did not exist - which defeats the point of a receipt.
    /// </summary>
    public static string? TryWriteArtifact(PublishReceipt receipt, out string? error, string? auditDir = null)
    {
        error = null;
        try
        {
            var root = auditDir
                       ?? Path.Combine(
                           Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                           "765T-Forge",
                           "receipts");
            Directory.CreateDirectory(root);
            var path = Path.Combine(root, $"receipt-{receipt.ReceiptId}.json");
            AtomicFile.WriteAllText(path, JsonSerializer.Serialize(receipt, ForgeJson.Options));
            return path;
        }
        catch (System.Exception ex)
        {
            error = $"{ex.GetType().Name}: {ex.Message}";
            return null;
        }
    }
}

public sealed class PdfProbeResult
{
    /// <summary>Page count is never verified; this is the only value this probe reports.</summary>
    public const string PageCountNotAvailable = "not_available";

    public bool Exists { get; init; }
    public long Bytes { get; init; }
    public bool NonEmpty { get; init; }

    /// <summary>Always null: this probe does not parse PDF page objects.</summary>
    public int? PageCount { get; init; }

    /// <summary>Provenance of <see cref="PageCount"/>. Always <see cref="PageCountNotAvailable"/>.</summary>
    public string PageCountSource { get; init; } = PageCountNotAvailable;

    public int? ExpectedPages { get; init; }
    public bool PageCountMatches { get; init; }
    public bool Passed { get; init; }
    public string? Message { get; init; }
    public string[] Warnings { get; init; } = [];

    /// <summary>
    /// Deterministic PDF probe: existence, non-zero length, and an exact <c>%PDF-</c> header
    /// (first 5 bytes). Page count is NOT verified — no PDF library is used and binary content
    /// is never regex-scanned.
    /// </summary>
    public static PdfProbeResult Probe(string path, int? expectedPages = null)
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

        var header = ReadHeader(path);
        var headerValid = string.Equals(header, "%PDF-", StringComparison.Ordinal);
        if (!headerValid)
        {
            warnings.Add("PDF header invalid: first 5 bytes are not %PDF-.");
        }

        if (expectedPages is not null)
        {
            warnings.Add($"Page count is not verified (pageCountSource={PageCountNotAvailable}); expected {expectedPages} page(s) cannot be confirmed.");
        }

        var passed = headerValid;
        return new PdfProbeResult
        {
            Exists = true,
            Bytes = bytes,
            NonEmpty = bytes > 0,
            PageCount = null,
            PageCountSource = PageCountNotAvailable,
            ExpectedPages = expectedPages,
            PageCountMatches = true,
            Passed = passed,
            Message = passed ? "PDF probe passed (existence, size, header)." : "PDF probe failed: header is not %PDF-.",
            Warnings = warnings.ToArray()
        };
    }

    private static string ReadHeader(string path)
    {
        try
        {
            using var stream = File.OpenRead(path);
            var bytes = new byte[5];
            var read = 0;
            while (read < bytes.Length)
            {
                var chunk = stream.Read(bytes, read, bytes.Length - read);
                if (chunk <= 0)
                {
                    break;
                }

                read += chunk;
            }

            return read == 5 ? Encoding.ASCII.GetString(bytes, 0, 5) : "";
        }
        catch (IOException)
        {
            return "";
        }
        catch (UnauthorizedAccessException)
        {
            return "";
        }
    }
}
