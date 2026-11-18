using System.Text.Json;

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

    // Header-name aliases, compared after removing every non-alphanumeric character —
    // "drawing_no", "drawing-number" and "DrawingNo" all normalize to "drawingno".
    private static readonly string[] LayoutColumns = ["layout", "layoutname"];
    private static readonly string[] DrawingNoColumns = ["drawingno", "drawingnumber", "drawing", "dwgno", "sheetno", "sheetnumber"];
    private static readonly string[] RevColumns = ["rev", "revision", "revno"];
    private static readonly string[] TitleColumns = ["title", "sheettitle", "sheetname"];

    private static string NormalizeHeader(string raw)
    {
        var t = raw.Trim().Trim('"').Trim();
        var sb = new System.Text.StringBuilder(t.Length);
        foreach (var ch in t)
        {
            if (char.IsLetterOrDigit(ch))
            {
                sb.Append(char.ToLowerInvariant(ch));
            }
        }

        return sb.ToString();
    }

    private static int FindColumn(string[] headers, string[] aliases, string displayName)
    {
        var index = IndexOfAny(headers, aliases);
        if (index < 0)
        {
            throw new InvalidOperationException(
                $"Sheet inventory CSV header must include a {displayName} column (found: {string.Join(", ", headers)}).");
        }

        return index;
    }

    private static int IndexOfAny(string[] headers, string[] aliases)
    {
        for (var i = 0; i < headers.Length; i++)
        {
            if (aliases.Contains(headers[i], StringComparer.Ordinal))
            {
                return i;
            }
        }

        return -1;
    }

    public static SheetInventory LoadFromCsv(string path)
    {
        var lines = File.ReadAllLines(path)
            .Where(l => !string.IsNullOrWhiteSpace(l) && !l.TrimStart().StartsWith('#'))
            .ToArray();
        if (lines.Length == 0)
        {
            throw new InvalidOperationException("Sheet inventory CSV is empty.");
        }

        // Positional default for headerless files: layout,drawingNo[,rev[,title]].
        var start = 0;
        var idxLayout = 0;
        var idxDrawingNo = 1;
        var idxRev = 2;
        var idxTitle = 3;
        var headerMapped = false;
        if (lines[0].Contains("layout", StringComparison.OrdinalIgnoreCase)
            && lines[0].Contains("drawing", StringComparison.OrdinalIgnoreCase))
        {
            // A header row maps every column by name — positions never leak across columns,
            // so a `...,title,rev` order can no longer swap the fields (readiness gap 7).
            start = 1;
            headerMapped = true;
            var headers = lines[0].Split(',').Select(NormalizeHeader).ToArray();
            idxLayout = FindColumn(headers, LayoutColumns, "layout");
            idxDrawingNo = FindColumn(headers, DrawingNoColumns, "drawingNo");
            idxRev = IndexOfAny(headers, RevColumns);
            idxTitle = IndexOfAny(headers, TitleColumns);
        }

        var sheets = new List<IssueSetSheet>();
        for (var i = start; i < lines.Length; i++)
        {
            var parts = lines[i].Split(',');
            if (parts.Length < 2)
            {
                throw new InvalidOperationException($"Invalid CSV row {i + 1}: need layout,drawingNo[,rev[,title]].");
            }

            string Cell(int index) => index >= 0 && index < parts.Length ? parts[index].Trim() : "";
            string? CellOrNull(int index) => index >= 0 && index < parts.Length ? parts[index].Trim() : null;

            sheets.Add(new IssueSetSheet
            {
                Layout = Cell(idxLayout),
                DrawingNo = Cell(idxDrawingNo),
                Rev = headerMapped ? CellOrNull(idxRev) : (parts.Length > 2 ? parts[2].Trim() : null),
                Title = headerMapped ? CellOrNull(idxTitle) : (parts.Length > 3 ? parts[3].Trim() : null)
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

    public void Save(string? dir = null)
    {
        var root = dir ?? DefaultStoreDir();
        Directory.CreateDirectory(root);
        ArtifactPath = Path.Combine(root, $"batch-{BatchId}.json");
        UpdatedUtc = DateTimeOffset.UtcNow;
        File.WriteAllText(ArtifactPath, JsonSerializer.Serialize(this, ForgeJson.Options));
    }

    public static BatchResumeState? Load(string batchIdOrPath)
    {
        var path = File.Exists(batchIdOrPath)
            ? batchIdOrPath
            : Path.Combine(DefaultStoreDir(), $"batch-{batchIdOrPath}.json");
        if (!File.Exists(path))
        {
            return null;
        }

        return JsonSerializer.Deserialize<BatchResumeState>(File.ReadAllText(path), ForgeJson.Options);
    }
}

public sealed class BatchJobState
{
    public string DwgPath { get; init; } = "";
    public string ScriptPath { get; init; } = "";
    public int? TimeoutSeconds { get; init; }
    public string Status { get; set; } = "pending"; // pending|running|ok|failed|skipped
    public string? ErrorCode { get; set; }
    public string? Message { get; set; }
}
