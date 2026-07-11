using System.Text.Json;
using Forge.Shared;

namespace Forge.Server;

public sealed class ForgeToolRunner
{
    private readonly SafetyPolicy _safetyPolicy;
    private readonly FileAuditSink _auditSink;
    private readonly ForgePipeClient _pipeClient;
    private readonly HeadlessAccoreConsoleRunner _headlessRunner;
    private readonly ForgeEnvironment _environment;

    public ForgeToolRunner(
        SafetyPolicy safetyPolicy,
        FileAuditSink auditSink,
        ForgePipeClient pipeClient,
        HeadlessAccoreConsoleRunner headlessRunner,
        ForgeEnvironment environment)
    {
        _safetyPolicy = safetyPolicy;
        _auditSink = auditSink;
        _pipeClient = pipeClient;
        _headlessRunner = headlessRunner;
        _environment = environment;
    }

    public async Task<ForgeResult> InvokeAsync(
        string tool,
        object args,
        bool dryRun = false,
        string? document = null,
        bool unsafeAcknowledged = false,
        CancellationToken cancellationToken = default)
    {
        var command = new ForgeCommand
        {
            Tool = tool,
            Args = ForgeJson.ToElement(args),
            DryRun = dryRun,
            Document = document,
            UnsafeAcknowledged = unsafeAcknowledged && _environment.EnableUnsafeOps
        };

        var decision = _safetyPolicy.Evaluate(command);
        var auditId = await _auditSink.WriteAsync(new AuditRecord
        {
            Source = "server",
            Tool = command.Tool,
            CommandId = command.Id,
            DryRun = command.DryRun,
            Allowed = decision.Allowed,
            DecisionCode = decision.Code,
            Args = command.Args
        }, cancellationToken).ConfigureAwait(false);

        if (!decision.Allowed)
        {
            return ForgeResult.Failure(command.Id, decision.Code, decision.Message, decision.Suggestion, auditId);
        }

        var result = tool.ToLowerInvariant() switch
        {
            "forge_run_script" => await _headlessRunner.RunScriptAsync(command, cancellationToken).ConfigureAwait(false),
            "forge_batch_run" => await _headlessRunner.RunBatchAsync(command, cancellationToken).ConfigureAwait(false),
            "forge_batch_status" => ResolveBatchStatus(command),
            "forge_system_tool_profile" => ResolveToolProfile(command),
            "forge_audit_summarize" => ResolveAuditSummarize(command),
            "forge_sheet_inventory_import" => ResolveSheetInventoryImport(command),
            "forge_issue_set_diff" => ResolveIssueSetDiff(command),
            _ => await _pipeClient.SendAsync(command, cancellationToken).ConfigureAwait(false)
        };

        await _auditSink.WriteAsync(new AuditRecord
        {
            AuditId = auditId,
            Source = "server",
            Tool = command.Tool,
            CommandId = command.Id,
            DryRun = command.DryRun,
            Allowed = true,
            DecisionCode = "completed",
            Args = command.Args,
            Ok = result.Ok,
            ErrorCode = result.Error?.Code,
            Message = result.Error?.Message
        }, cancellationToken).ConfigureAwait(false);

        return result with { AuditId = result.AuditId ?? auditId };
    }

    private static ForgeResult ResolveToolProfile(ForgeCommand command)
    {
        var name = command.Args.ValueKind == System.Text.Json.JsonValueKind.Object &&
                   command.Args.TryGetProperty("name", out var nameEl)
            ? nameEl.GetString()
            : null;

        if (string.IsNullOrWhiteSpace(name))
        {
            return ForgeResult.Success(command.Id, new { profiles = ToolProfiles.Names });
        }

        if (!ToolProfiles.TryGet(name, out var tools))
        {
            return ForgeResult.Failure(
                command.Id,
                "unknown_tool_profile",
                $"Unknown profile '{name}'. Known: {string.Join(", ", ToolProfiles.Names)}");
        }

        return ForgeResult.Success(command.Id, new { name, tools });
    }

    private ForgeResult ResolveAuditSummarize(ForgeCommand command)
    {
        var limit = 20;
        if (command.Args.ValueKind == JsonValueKind.Object &&
            command.Args.TryGetProperty("limit", out var lim) &&
            lim.TryGetInt32(out var n))
        {
            limit = Math.Clamp(n, 1, 200);
        }

        var dir = _environment.AuditDirectory;
        if (!Directory.Exists(dir))
        {
            return ForgeResult.Success(command.Id, new { count = 0, records = Array.Empty<object>(), note = "Audit directory empty or missing." });
        }

        var files = Directory.GetFiles(dir, "*.jsonl").OrderByDescending(File.GetLastWriteTimeUtc).Take(3).ToArray();
        var records = new List<object>();
        foreach (var file in files)
        {
            foreach (var line in File.ReadLines(file).Reverse())
            {
                if (string.IsNullOrWhiteSpace(line))
                {
                    continue;
                }

                try
                {
                    using var doc = JsonDocument.Parse(line);
                    var root = doc.RootElement;
                    records.Add(new
                    {
                        tool = root.TryGetProperty("tool", out var t) ? t.GetString() : null,
                        ok = root.TryGetProperty("ok", out var o) && o.ValueKind == JsonValueKind.True,
                        decisionCode = root.TryGetProperty("decisionCode", out var d) ? d.GetString() : null,
                        errorCode = root.TryGetProperty("errorCode", out var e) ? e.GetString() : null,
                        source = root.TryGetProperty("source", out var s) ? s.GetString() : null
                    });
                }
                catch
                {
                    // Skip malformed lines.
                }

                if (records.Count >= limit)
                {
                    break;
                }
            }

            if (records.Count >= limit)
            {
                break;
            }
        }

        return ForgeResult.Success(command.Id, new { count = records.Count, records, note = "Args/paths omitted from summary for safety." });
    }

    private static ForgeResult ResolveSheetInventoryImport(ForgeCommand command)
    {
        var args = ForgeJson.FromElement<SheetInventoryArgs>(command.Args) ?? new SheetInventoryArgs();
        if (string.IsNullOrWhiteSpace(args.CsvPath))
        {
            return ForgeResult.Failure(command.Id, "missing_csv_path", "csvPath is required.");
        }

        if (!File.Exists(args.CsvPath))
        {
            return ForgeResult.Failure(command.Id, "csv_not_found", $"CSV not found: {args.CsvPath}");
        }

        try
        {
            var inventory = SheetInventory.LoadFromCsv(args.CsvPath);
            var contractId = string.IsNullOrWhiteSpace(args.ContractId)
                ? $"imported-{DateTime.UtcNow:yyyyMMddHHmmss}"
                : args.ContractId!;
            var contract = inventory.ToContract(contractId, args.ProjectId);
            string? written = null;
            if (!string.IsNullOrWhiteSpace(args.OutputContractPath))
            {
                written = Path.GetFullPath(args.OutputContractPath);
                Directory.CreateDirectory(Path.GetDirectoryName(written)!);
                File.WriteAllText(written, JsonSerializer.Serialize(contract, ForgeJson.Options));
            }

            return ForgeResult.Success(command.Id, new
            {
                inventoryId = inventory.InventoryId,
                sheetCount = contract.Sheets.Count,
                contractId = contract.ContractId,
                outputContractPath = written,
                sheets = contract.Sheets,
                note = "Read-only import — does not write AutoCAD Sheet Set (DST) files."
            });
        }
        catch (Exception ex)
        {
            return ForgeResult.Failure(command.Id, "sheet_inventory_invalid", ex.Message);
        }
    }

    private static ForgeResult ResolveIssueSetDiff(ForgeCommand command)
    {
        var args = ForgeJson.FromElement<IssueSetDiffArgs>(command.Args) ?? new IssueSetDiffArgs();
        if (string.IsNullOrWhiteSpace(args.CurrentReceiptPath))
        {
            return ForgeResult.Failure(command.Id, "missing_receipt", "currentReceiptPath is required.");
        }

        var current = IssueSetDiff.TryLoadReceipt(args.CurrentReceiptPath);
        if (current is null)
        {
            return ForgeResult.Failure(command.Id, "receipt_not_found", $"Current receipt not found: {args.CurrentReceiptPath}");
        }

        PublishReceipt? previous = null;
        if (!string.IsNullOrWhiteSpace(args.PreviousReceiptPath))
        {
            previous = IssueSetDiff.TryLoadReceipt(args.PreviousReceiptPath);
            if (previous is null)
            {
                return ForgeResult.Failure(command.Id, "receipt_not_found", $"Previous receipt not found: {args.PreviousReceiptPath}");
            }
        }

        var diff = IssueSetDiff.Compare(previous, current);
        return ForgeResult.Success(command.Id, diff);
    }

    private static ForgeResult ResolveBatchStatus(ForgeCommand command)
    {
        var path = command.Args.ValueKind == JsonValueKind.Object &&
                   command.Args.TryGetProperty("batchIdOrPath", out var el)
            ? el.GetString()
            : null;
        if (string.IsNullOrWhiteSpace(path))
        {
            return ForgeResult.Failure(command.Id, "missing_batch_id", "batchIdOrPath is required.");
        }

        var state = BatchResumeState.Load(path);
        return state is null
            ? ForgeResult.Failure(command.Id, "batch_not_found", $"No batch state for '{path}'.")
            : ForgeResult.Success(command.Id, state);
    }

    private sealed record SheetInventoryArgs
    {
        public string? CsvPath { get; init; }
        public string? ContractId { get; init; }
        public string? ProjectId { get; init; }
        public string? OutputContractPath { get; init; }
    }

    private sealed record IssueSetDiffArgs
    {
        public string? CurrentReceiptPath { get; init; }
        public string? PreviousReceiptPath { get; init; }
    }

    public string ToolCatalogJson()
    {
        return JsonSerializer.Serialize(ForgeToolRegistry.All.OrderBy(t => t.Name), ForgeJson.Options);
    }
}
