using System.Text.Json;

namespace Forge.Shared;

public readonly record struct AuditWrite(string AuditId, bool Written);

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

    public AuditWrite WriteBestEffort(AuditRecord record)
    {
        try
        {
            WriteCore(record);
            return new AuditWrite(record.AuditId, true);
        }
        catch (Exception ex)
        {
            ReportAuditFailure(record, ex);
            return new AuditWrite(record.AuditId, false);
        }
    }

    public async Task<AuditWrite> WriteAsync(AuditRecord record, CancellationToken cancellationToken = default)
    {
        try
        {
            await Task.Run(() => WriteCore(record), cancellationToken).ConfigureAwait(false);
            return new AuditWrite(record.AuditId, true);
        }
        catch (Exception ex)
        {
            ReportAuditFailure(record, ex);
            return new AuditWrite(record.AuditId, false);
        }
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
