using System.Text.Json;

namespace Forge.Shared;

public sealed record AuditRecord
{
    public string AuditId { get; init; } = Guid.NewGuid().ToString("N");
    public DateTimeOffset TimestampUtc { get; init; } = DateTimeOffset.UtcNow;
    public string Source { get; init; } = "";
    public string Tool { get; init; } = "";
    public string CommandId { get; init; } = "";
    public bool DryRun { get; init; }
    public bool Allowed { get; init; }
    public string? DecisionCode { get; init; }
    public JsonElement Args { get; init; }
    public bool? Ok { get; init; }
    public string? ErrorCode { get; init; }
    public string? Message { get; init; }
}

public sealed class FileAuditSink
{
    private static readonly object Gate = new();
    private readonly string _directory;

    public FileAuditSink(string directory)
    {
        _directory = directory;
        Directory.CreateDirectory(_directory);
    }

    public string WriteBestEffort(AuditRecord record)
    {
        try
        {
            WriteCore(record);
        }
        catch (Exception ex)
        {
            // Audit must never abort a CAD operation. The caller still receives
            // the generated audit id so the command path stays correlated.
            ReportAuditFailure(record, ex);
        }

        return record.AuditId;
    }

    public async Task<string> WriteAsync(AuditRecord record, CancellationToken cancellationToken = default)
    {
        try
        {
            await Task.Run(() => WriteCore(record), cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            // Same guarantee as the synchronous path: logging failures should not
            // become drawing failures.
            ReportAuditFailure(record, ex);
        }

        return record.AuditId;
    }

    private void WriteCore(AuditRecord record)
    {
        var fileName = $"{DateTimeOffset.UtcNow:yyyyMMdd}.jsonl";
        var path = Path.Combine(_directory, fileName);
        var json = JsonSerializer.Serialize(record, ForgeJson.Options) + Environment.NewLine;

        lock (Gate)
        {
            Directory.CreateDirectory(_directory);
            for (var attempt = 0; attempt < 3; attempt++)
            {
                try
                {
                    using var stream = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite);
                    using var writer = new StreamWriter(stream);
                    writer.Write(json);
                    return;
                }
                catch (IOException) when (attempt < 2)
                {
                    Thread.Sleep(25 * (attempt + 1));
                }
            }
        }
    }

    private static void ReportAuditFailure(AuditRecord record, Exception ex)
    {
        try
        {
            Console.Error.WriteLine(
                $"[765T-Forge][audit] failed to write auditId={record.AuditId} tool={record.Tool} commandId={record.CommandId}: {ex.GetType().Name}: {ex.Message}");
        }
        catch
        {
            // Last-resort diagnostics only.
        }
    }
}
