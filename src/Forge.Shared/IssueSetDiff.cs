using System.Text.Json;
using System.Text.RegularExpressions;

namespace Forge.Shared;

/// <summary>
/// Read-only sheet inventory imported from CSV (layout,drawingNo,rev[,title]).
/// Does not write DST / Sheet Set Manager files.
/// </summary>
public sealed class SheetInventory
{
    public string InventoryId { get; init; } = Guid.NewGuid().ToString("N");
    public string? SourcePath { get; set; }
    public string Format { get; init; } = "csv";
    public List<IssueSetSheet> Sheets { get; init; } = [];

    public static SheetInventory LoadFromCsv(string path)
    {
        var rows = CsvReader.Parse(File.ReadAllText(path))
            .Where(r => r.Count > 0)
            .Where(r => !r.All(string.IsNullOrWhiteSpace))
            .Where(r => !r[0].TrimStart().StartsWith("#", StringComparison.Ordinal))
            .ToArray();
        if (rows.Length == 0)
        {
            throw new InvalidOperationException("Sheet inventory CSV is empty.");
        }

        var start = 0;
        var header = string.Join(",", rows[0]);
        if (header.IndexOf("layout", StringComparison.OrdinalIgnoreCase) >= 0
            && header.IndexOf("drawing", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            start = 1;
        }

        var sheets = new List<IssueSetSheet>();
        for (var i = start; i < rows.Length; i++)
        {
            var parts = rows[i];
            if (parts.Count < 2)
            {
                throw new InvalidOperationException($"Invalid CSV row {i + 1}: need layout,drawingNo[,rev[,title]].");
            }

            sheets.Add(new IssueSetSheet
            {
                Layout = parts[0].Trim(),
                DrawingNo = parts[1].Trim(),
                Rev = parts.Count > 2 ? parts[2].Trim() : null,
                Title = parts.Count > 3 ? parts[3].Trim() : null
            });
        }

        return new SheetInventory
        {
            SourcePath = Path.GetFullPath(path),
            Sheets = sheets
        };
    }

    public IssueSetContract ToContract(string contractId, string? projectId = null, string? rev = null)
    {
        var contract = new IssueSetContract
        {
            ContractId = contractId,
            ProjectId = projectId,
            Rev = rev,
            Sheets = Sheets.ToList(),
            SourcePath = SourcePath
        };
        contract.Validate();
        return contract;
    }
}

/// <summary>
/// Diff current publish evidence vs a previous receipt (attrs / layouts / pdf bytes).
/// </summary>
public sealed class IssueSetDiff
{
    public string? PreviousReceiptId { get; init; }
    public string? CurrentReceiptId { get; init; }
    public string[] AddedLayouts { get; init; } = [];
    public string[] RemovedLayouts { get; init; } = [];
    public bool OutputBytesChanged { get; init; }
    public long? PreviousBytes { get; init; }
    public long? CurrentBytes { get; init; }
    public string[] Notes { get; init; } = [];

    public static IssueSetDiff Compare(PublishReceipt? previous, PublishReceipt current)
    {
        var prevLayouts = previous?.Layouts ?? [];
        var currLayouts = current.Layouts;
        var added = currLayouts.Except(prevLayouts, StringComparer.OrdinalIgnoreCase).ToArray();
        var removed = prevLayouts.Except(currLayouts, StringComparer.OrdinalIgnoreCase).ToArray();
        var notes = new List<string>();
        if (previous is null)
        {
            notes.Add("No previous receipt — full set is new.");
        }

        return new IssueSetDiff
        {
            PreviousReceiptId = previous?.ReceiptId,
            CurrentReceiptId = current.ReceiptId,
            AddedLayouts = added,
            RemovedLayouts = removed,
            OutputBytesChanged = previous?.OutputBytes != current.OutputBytes,
            PreviousBytes = previous?.OutputBytes,
            CurrentBytes = current.OutputBytes,
            Notes = notes.ToArray()
        };
    }

    public static PublishReceipt? TryLoadReceipt(string path)
    {
        if (!File.Exists(path))
        {
            return null;
        }

        return JsonSerializer.Deserialize<PublishReceipt>(File.ReadAllText(path), ForgeJson.Options);
    }
}

/// <summary>
/// Tracks AccoreConsole batch progress for resume after partial failure.
/// </summary>
public sealed class BatchResumeState
{
    /// <summary>Exact batch id shape accepted for file names: 1-64 chars of [A-Za-z0-9-].</summary>
    private static readonly Regex BatchIdPattern = new(
        "^[A-Za-z0-9-]{1,64}$",
        RegexOptions.CultureInvariant,
        ForgeConstants.RegexMatchTimeout);

    public string BatchId { get; init; } = Guid.NewGuid().ToString("N");
    public DateTimeOffset CreatedUtc { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedUtc { get; set; } = DateTimeOffset.UtcNow;
    public List<BatchJobState> Jobs { get; init; } = [];
    public string? ArtifactPath { get; set; }

    public IEnumerable<BatchJobState> Pending()
        => Jobs.Where(j => j.Status is "pending" or "failed");

    public static string DefaultStoreDir()
        => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "765T-Forge",
            "batch");

    public static void ValidateBatchId(string batchId)
    {
        if (string.IsNullOrWhiteSpace(batchId) || !BatchIdPattern.IsMatch(batchId))
        {
            throw new ArgumentException(
                "BatchId must match ^[A-Za-z0-9-]{1,64}$.",
                nameof(batchId));
        }
    }

    public void Save(string? dir = null)
    {
        ValidateBatchId(BatchId);
        var root = dir ?? DefaultStoreDir();
        Directory.CreateDirectory(root);
        ArtifactPath = Path.Combine(root, $"batch-{BatchId}.json");
        UpdatedUtc = DateTimeOffset.UtcNow;
        AtomicFile.WriteAllText(ArtifactPath, JsonSerializer.Serialize(this, ForgeJson.Options));
    }

    /// <summary>
    /// Loads a batch by validated <paramref name="batchId"/> from the default store directory.
    /// Paths are deliberately not accepted here — use <see cref="LoadFromPath"/>.
    /// </summary>
    public static BatchResumeState? Load(string batchId)
    {
        ValidateBatchId(batchId);
        var path = Path.Combine(DefaultStoreDir(), $"batch-{batchId}.json");
        return File.Exists(path)
            ? JsonSerializer.Deserialize<BatchResumeState>(File.ReadAllText(path), ForgeJson.Options)
            : null;
    }

    /// <summary>Loads a batch from an explicitly supplied rooted path.</summary>
    public static BatchResumeState? LoadFromPath(string absolutePath)
    {
        if (string.IsNullOrWhiteSpace(absolutePath) || !Path.IsPathRooted(absolutePath))
        {
            throw new ArgumentException("absolutePath must be a rooted path.", nameof(absolutePath));
        }

        return File.Exists(absolutePath)
            ? JsonSerializer.Deserialize<BatchResumeState>(File.ReadAllText(absolutePath), ForgeJson.Options)
            : null;
    }
}

public sealed class BatchJobState
{
    public string DwgPath { get; init; } = "";
    public string ScriptPath { get; init; } = "";
    public string Status { get; set; } = "pending"; // pending|running|ok|failed|skipped
    public string? ErrorCode { get; set; }
    public string? Message { get; set; }
}
