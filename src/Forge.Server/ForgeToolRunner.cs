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
    private readonly ForgeToolsetState _toolsets;

    public ForgeToolRunner(
        SafetyPolicy safetyPolicy,
        FileAuditSink auditSink,
        ForgePipeClient pipeClient,
        HeadlessAccoreConsoleRunner headlessRunner,
        ForgeEnvironment environment,
        ForgeToolsetState toolsets)
    {
        _safetyPolicy = safetyPolicy;
        _auditSink = auditSink;
        _pipeClient = pipeClient;
        _headlessRunner = headlessRunner;
        _environment = environment;
        _toolsets = toolsets;
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
            "forge_toolset_list" => ResolveToolsetList(command),
            "forge_toolset_enable" => ResolveToolsetEnable(command),
            "forge_toolset_search" => ResolveToolsetSearch(command),
            "forge_audit_summarize" => ResolveAuditSummarize(command),
            "forge_sheet_inventory_import" => ResolveSheetInventoryImport(command),
            "forge_issue_set_diff" => ResolveIssueSetDiff(command),
            // Pure coordinate math — always resolves server-side (no AutoCAD needed).
            "forge_linework_transform" => CoordTransformTool.Execute(command),
            // Linework tools resolve locally (no AutoCAD) when the caller targets a dump file;
            // live-drawing queries still route to the plugin over the pipe.
            _ when LineworkLocal.IsLineworkTool(tool) && LineworkLocal.IsDumpFileSource(command.Args)
                => LineworkLocal.Execute(command),
            _ => await SendToPluginAsync(command, tool, cancellationToken).ConfigureAwait(false)
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

    /// <summary>
    /// Pipe dispatch with elicitation: an *_overwrite_not_acknowledged / discard / bulk
    /// refusal asks the user once when the client supports elicitation.form; "accept"
    /// retries the same command with the mapped ack flag injected. Anything else
    /// (no capability, decline, cancel, elicitation error) returns the original refusal —
    /// the explicit args stay the fallback.
    /// </summary>
    private async Task<ForgeResult> SendToPluginAsync(ForgeCommand command, string tool, CancellationToken cancellationToken)
    {
        var result = await _pipeClient.SendAsync(command, cancellationToken).ConfigureAwait(false);
        if (result.Ok || result.Error is null ||
            !ForgeElicitationGate.AckFlagByCode.TryGetValue(result.Error.Code, out var flagName))
        {
            return result;
        }

        if (!await ForgeElicitationGate
                .ConfirmAsync(tool, result.Error.Code, result.Error.Message, cancellationToken)
                .ConfigureAwait(false))
        {
            return result;
        }

        var confirmed = ForgeElicitationGate.WithAck(command, flagName);
        await _auditSink.WriteAsync(new AuditRecord
        {
            Source = "server",
            Tool = command.Tool,
            CommandId = command.Id,
            DryRun = command.DryRun,
            Allowed = true,
            DecisionCode = "elicitation_confirmed",
            Message = $"User confirmed {flagName} via elicitation after {result.Error.Code}.",
            Args = confirmed.Args
        }, cancellationToken).ConfigureAwait(false);

        return await _pipeClient.SendAsync(confirmed, cancellationToken).ConfigureAwait(false);
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

    private ForgeResult ResolveToolsetList(ForgeCommand command)
    {
        var enabled = _toolsets.Enabled;
        var sets = ForgeToolsets.Names.Select(name => new
        {
            name,
            enabled = enabled.Contains(name, StringComparer.OrdinalIgnoreCase),
            toolCount = ForgeToolsets.ToolsInSet(name).Count,
            tools = ForgeToolsets.ToolsInSet(name)
        }).ToArray();
        return ForgeResult.Success(command.Id, new
        {
            enabled,
            toolsets = sets,
            meta = ForgeToolsets.ToolsInSet(ForgeToolsets.Meta),
            hint = "forge_toolset_enable turns a set on for this session (the client re-lists tools); FORGE_TOOLSETS selects at launch; \"all\" loads every tool."
        });
    }

    private ForgeResult ResolveToolsetEnable(ForgeCommand command)
    {
        var names = new List<string>();
        if (command.Args.ValueKind == JsonValueKind.Object)
        {
            if (command.Args.TryGetProperty("names", out var arr) && arr.ValueKind == JsonValueKind.Array)
            {
                names.AddRange(arr.EnumerateArray().Where(e => e.ValueKind == JsonValueKind.String).Select(e => e.GetString()!));
            }

            if (command.Args.TryGetProperty("name", out var single) && single.ValueKind == JsonValueKind.String)
            {
                names.Add(single.GetString()!);
            }
        }

        if (names.Count == 0)
        {
            return ForgeResult.Failure(command.Id, "missing_toolset", "Provide names[] (or name) of the toolset(s) to enable.",
                "Call forge_toolset_list for the available sets.");
        }

        var added = new List<string>();
        var alreadyOn = new List<string>();
        var unknown = new List<string>();
        foreach (var name in names.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (!ForgeToolsets.SetGroups.ContainsKey(name))
            {
                unknown.Add(name);
                continue;
            }

            (_toolsets.Enabled.Contains(name, StringComparer.OrdinalIgnoreCase) ? alreadyOn : added).Add(name);
            _toolsets.Enable(name);
        }

        var data = new
        {
            enabled = _toolsets.Enabled,
            added,
            alreadyOn,
            unknown,
            toolsNowListed = ForgeToolsets.EnabledTools(_toolsets.Enabled).Count,
            toolsListChanged = added.Count > 0,
            warning = unknown.Count > 0 ? $"unknown toolset(s) ignored: {string.Join(", ", unknown)}" : null
        };
        return unknown.Count == names.Count
            ? ForgeResult.Failure(command.Id, "unknown_toolset",
                $"Unknown toolset(s): {string.Join(", ", unknown)}.",
                $"Known: {string.Join(", ", ForgeToolsets.Names)}", data: data)
            : ForgeResult.Success(command.Id, data);
    }

    private ForgeResult ResolveToolsetSearch(ForgeCommand command)
    {
        var query = command.Args.ValueKind == JsonValueKind.Object &&
                    command.Args.TryGetProperty("query", out var q)
            ? q.GetString() : null;
        if (string.IsNullOrWhiteSpace(query))
        {
            return ForgeResult.Failure(command.Id, "missing_query", "query is required — a tool name fragment or keyword.",
                "Example: \"publish\", \"linework\", \"table\".");
        }

        var hits = ForgeToolRegistry.All
            .Select(t => new
            {
                t.Name,
                Toolset = ForgeToolsets.ToolsetForTool(t.Name),
                t.Group,
                t.ReadOnly,
                t.Destructive,
                t.Unsafe,
                Enabled = _toolsets.IsEnabled(t.Name)
            })
            .Where(t => t.Name.Contains(query, StringComparison.OrdinalIgnoreCase)
                        || t.Toolset.Contains(query, StringComparison.OrdinalIgnoreCase)
                        || t.Group.Contains(query, StringComparison.OrdinalIgnoreCase)
                        || t.Name.Replace("forge_", "").Replace("_", " ").Contains(query, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(t => t.Enabled)
            .ThenBy(t => t.Name)
            .Take(25)
            .ToArray();
        return ForgeResult.Success(command.Id, new
        {
            query,
            count = hits.Length,
            tools = hits,
            hint = hits.Any(h => !h.Enabled) ? "Some matches sit in disabled toolsets — forge_toolset_enable turns them on." : null
        });
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
