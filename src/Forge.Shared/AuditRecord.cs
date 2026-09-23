using System.Globalization;
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
    /// <summary>
    /// Exact, case-insensitive argument property names whose values are replaced with
    /// <c>"[redacted]"</c> before the audit JSONL line is serialized. This is an exact-name
    /// membership test — no suffix wildcards, no fuzzy matching.
    /// </summary>
    private static readonly HashSet<string> SensitiveArgumentNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "hmacKey",
        "token",
        "authToken",
        "secret",
        "password",
        "apiKey"
    };

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
        // Provenance is part of the file name ({source}-{yyyyMMdd}.jsonl) so two processes
        // (server and plugin) never append to the same file and interleave partial lines.
        var source = RequireFileNameSafeSource(record.Source);
        var fileName = $"{source}-{DateTimeOffset.UtcNow.ToString("yyyyMMdd", CultureInfo.InvariantCulture)}.jsonl";
        var path = Path.Combine(_directory, fileName);
        var redacted = record with { Args = RedactArgs(record.Args) };
        var json = JsonSerializer.Serialize(redacted, ForgeJson.Options) + Environment.NewLine;

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

    /// <summary>
    /// Exact validation of the provenance token used in the audit file name. Never substituted
    /// with a default: an unset or file-name-unsafe source is reported as an audit write failure.
    /// </summary>
    private static string RequireFileNameSafeSource(string? source)
    {
        if (source is null || string.IsNullOrWhiteSpace(source))
        {
            throw new ArgumentException(
                "AuditRecord.Source must be a non-empty provenance token ('server' or 'plugin') for the audit file name.",
                nameof(source));
        }

        if (source.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
        {
            throw new ArgumentException(
                $"AuditRecord.Source '{source}' contains characters that are not valid in a file name.",
                nameof(source));
        }

        return source;
    }

    private static JsonElement RedactArgs(JsonElement args)
    {
        if (args.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null)
        {
            return args;
        }

        using var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            WriteRedacted(writer, args);
        }

        using var document = JsonDocument.Parse(buffer.ToArray());
        return document.RootElement.Clone();
    }

    private static void WriteRedacted(Utf8JsonWriter writer, JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                writer.WriteStartObject();
                foreach (var property in element.EnumerateObject())
                {
                    writer.WritePropertyName(property.Name);
                    if (SensitiveArgumentNames.Contains(property.Name))
                    {
                        writer.WriteStringValue("[redacted]");
                    }
                    else
                    {
                        WriteRedacted(writer, property.Value);
                    }
                }

                writer.WriteEndObject();
                break;
            case JsonValueKind.Array:
                writer.WriteStartArray();
                foreach (var item in element.EnumerateArray())
                {
                    WriteRedacted(writer, item);
                }

                writer.WriteEndArray();
                break;
            default:
                element.WriteTo(writer);
                break;
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
