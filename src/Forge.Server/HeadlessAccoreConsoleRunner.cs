using System.Diagnostics;
using System.Text.Json;
using Forge.Shared;

namespace Forge.Server;

public sealed class HeadlessAccoreConsoleRunner
{
    private readonly ForgeEnvironment _environment;
    private readonly BackupPlanner _backupPlanner;
    private readonly SafetyPolicy _safetyPolicy;

    public HeadlessAccoreConsoleRunner(ForgeEnvironment environment, BackupPlanner backupPlanner, SafetyPolicy safetyPolicy)
    {
        _environment = environment;
        _backupPlanner = backupPlanner;
        _safetyPolicy = safetyPolicy;
    }

    public async Task<ForgeResult> RunScriptAsync(ForgeCommand command, CancellationToken cancellationToken = default)
    {
        var args = ForgeJson.FromElement<RunScriptArgs>(command.Args) ?? new RunScriptArgs();
        return await RunOneAsync(command.Id, args.DwgPath, args.ScriptPath, args.TimeoutSeconds, command.DryRun, cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<ForgeResult> RunBatchAsync(ForgeCommand command, CancellationToken cancellationToken = default)
    {
        var args = ForgeJson.FromElement<BatchArgs>(command.Args) ?? new BatchArgs();
        BatchResumeState state;
        if (!string.IsNullOrWhiteSpace(args.ResumeBatchId))
        {
            var loaded = BatchResumeState.Load(args.ResumeBatchId);
            if (loaded is null)
            {
                return ForgeResult.Failure(command.Id, "batch_not_found", $"Cannot resume batch '{args.ResumeBatchId}'.");
            }

            state = loaded;
            foreach (var job in state.Jobs.Where(j => j.Status is "failed" or "pending"))
            {
                job.Status = "pending";
                job.ErrorCode = null;
                job.Message = null;
            }
        }
        else
        {
            if (args.Jobs.Length == 0)
            {
                return ForgeResult.Failure(command.Id, "missing_batch_jobs", "jobs array is required (or pass resumeBatchId).");
            }

            if (args.Jobs.Length > 200)
            {
                return ForgeResult.Failure(command.Id, "batch_too_large", "Batch is limited to 200 jobs per call.", "Split the queue and re-run.");
            }

            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var job in args.Jobs)
            {
                var key = $"{job.DwgPath}|{job.ScriptPath}";
                if (!seen.Add(key))
                {
                    return ForgeResult.Failure(command.Id, "batch_duplicate_job", $"Duplicate job pair: {job.DwgPath} + {job.ScriptPath}");
                }
            }

            state = new BatchResumeState
            {
                Jobs = args.Jobs.Select(j => new BatchJobState
                {
                    DwgPath = j.DwgPath ?? "",
                    ScriptPath = j.ScriptPath ?? "",
                    TimeoutSeconds = j.TimeoutSeconds,
                    Status = "pending"
                }).ToList()
            };
        }

        if (command.DryRun)
        {
            return ForgeResult.Success(command.Id, new
            {
                dryRun = true,
                batchId = state.BatchId,
                jobCount = state.Jobs.Count,
                pending = state.Pending().Count(),
                continueOnError = args.ContinueOnError,
                note = "Ensure scripts set FILEDIA=0 and BACKGROUNDPLOT=0; AccoreConsole has no UI dialogs.",
                jobs = state.Jobs.Select(j => new
                {
                    j.DwgPath,
                    j.ScriptPath,
                    j.TimeoutSeconds,
                    j.Status,
                    dwgExists = !string.IsNullOrWhiteSpace(j.DwgPath) && File.Exists(j.DwgPath),
                    scriptExists = !string.IsNullOrWhiteSpace(j.ScriptPath) && File.Exists(j.ScriptPath),
                    backup = string.IsNullOrWhiteSpace(j.DwgPath) ? null : _backupPlanner.PlanBackupPath(j.DwgPath)
                })
            });
        }

        var results = new List<object>();
        var failures = 0;
        foreach (var job in state.Jobs)
        {
            if (job.Status == "ok")
            {
                results.Add(new { job.DwgPath, job.ScriptPath, Ok = true, skipped = true, status = job.Status });
                continue;
            }

            cancellationToken.ThrowIfCancellationRequested();
            job.Status = "running";
            state.Save();
            var one = await RunOneAsync(command.Id, job.DwgPath, job.ScriptPath, job.TimeoutSeconds, dryRun: false, cancellationToken)
                .ConfigureAwait(false);
            if (one.Ok)
            {
                job.Status = "ok";
                job.Message = "ok";
            }
            else
            {
                job.Status = "failed";
                job.ErrorCode = one.Error?.Code;
                job.Message = one.Error?.Message;
                failures++;
            }

            state.Save();
            results.Add(new
            {
                job.DwgPath,
                job.ScriptPath,
                one.Ok,
                one.Error,
                one.Data,
                status = job.Status
            });
            if (!one.Ok && !args.ContinueOnError)
            {
                break;
            }
        }

        state.Save();
        return failures == 0
            ? ForgeResult.Success(command.Id, new
            {
                batchId = state.BatchId,
                artifactPath = state.ArtifactPath,
                completed = results.Count,
                failures,
                results
            })
            : new ForgeResult
            {
                Id = command.Id,
                Ok = false,
                Error = new ForgeError(
                    "batch_partial_failure",
                    $"{failures} AccoreConsole job(s) failed. Resume with resumeBatchId={state.BatchId}.",
                    "Call forge_batch_status then forge_batch_run with resumeBatchId."),
                Data = new
                {
                    batchId = state.BatchId,
                    artifactPath = state.ArtifactPath,
                    completed = results.Count,
                    failures,
                    results
                }
            };
    }

    private async Task<ForgeResult> RunOneAsync(
        string commandId,
        string? dwgPath,
        string? scriptPath,
        int? timeoutSeconds,
        bool dryRun,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(dwgPath) || string.IsNullOrWhiteSpace(scriptPath))
        {
            return ForgeResult.Failure(commandId, "missing_script_args", "dwgPath and scriptPath are required.");
        }

        if (!File.Exists(dwgPath))
        {
            return ForgeResult.Failure(commandId, "dwg_not_found", $"DWG file not found: {dwgPath}");
        }

        if (!File.Exists(scriptPath))
        {
            return ForgeResult.Failure(commandId, "script_not_found", $"Script file not found: {scriptPath}");
        }

        if (!AccoreScriptPlan.TryGetKind(scriptPath, out var scriptKind, out var kindError))
        {
            return ForgeResult.Failure(commandId, "unsupported_script_type", kindError!, "Pass a .scr command script or a .lsp AutoLISP file.");
        }

        // The safety scan runs on the user's own script bytes (.scr commands or .lsp source); the
        // generated .scr loader for a .lsp is Forge's trusted text and is never re-scanned.
        var scriptBytes = await File.ReadAllBytesAsync(scriptPath, cancellationToken).ConfigureAwait(false);
        var scriptText = AccoreConsoleScriptCheck.DecodeOutput(scriptBytes);
        var scriptDecision = _safetyPolicy.EvaluateText("forge_run_script", scriptText);
        if (!scriptDecision.Allowed)
        {
            return ForgeResult.Failure(commandId, scriptDecision.Code, scriptDecision.Message, scriptDecision.Suggestion);
        }

        var accoreconsole = Path.Combine(_environment.AutoCadRoot, "accoreconsole.exe");
        if (!File.Exists(accoreconsole))
        {
            return ForgeResult.Failure(commandId, "accoreconsole_not_found", $"accoreconsole.exe not found at {accoreconsole}.");
        }

        var scriptKindName = scriptKind == AccoreScriptPlan.ScriptKind.Lisp ? "lsp" : "scr";
        if (dryRun)
        {
            return ForgeResult.Success(commandId, new
            {
                wouldRun = accoreconsole,
                dwgPath,
                scriptPath,
                scriptKind = scriptKindName,
                loader = scriptKind == AccoreScriptPlan.ScriptKind.Lisp
                    ? AccoreScriptPlan.LoaderScript(Path.Combine(Path.GetTempPath(), "forge-script-<id>.lsp"))
                    : null,
                backup = _backupPlanner.PlanBackupPath(dwgPath)
            });
        }

        var backupPath = _backupPlanner.TryBackup(dwgPath);
        var tempStem = Path.Combine(Path.GetTempPath(), $"forge-script-{commandId}-{Guid.NewGuid():N}");
        var tempScriptPath = tempStem + ".scr";
        string? tempLispPath = null;
        if (scriptKind == AccoreScriptPlan.ScriptKind.Lisp)
        {
            // accoreconsole /s only runs .scr ("probe.lsp" → "probe.lsp.scr: Can't find file"),
            // so the verified .lsp bytes are loaded by a generated .scr.
            tempLispPath = tempStem + ".lsp";
            await File.WriteAllBytesAsync(tempLispPath, scriptBytes, cancellationToken).ConfigureAwait(false);
            await File.WriteAllBytesAsync(tempScriptPath, AccoreScriptPlan.LoaderScriptBytes(tempLispPath), cancellationToken).ConfigureAwait(false);
        }
        else
        {
            await File.WriteAllBytesAsync(tempScriptPath, scriptBytes, cancellationToken).ConfigureAwait(false);
        }

        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = accoreconsole,
                ArgumentList = { "/i", dwgPath, "/s", tempScriptPath },
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            using var process = Process.Start(psi);
            if (process is null)
            {
                return ForgeResult.Failure(commandId, "accoreconsole_start_failed", "Failed to start accoreconsole.exe.");
            }

            var timeout = TimeSpan.FromSeconds(Math.Clamp(timeoutSeconds ?? 300, 5, 3600));
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(timeout);

            try
            {
                // accoreconsole writes UTF-16LE when redirected; a StreamReader with the default
                // UTF-8 encoding yields "U\0n\0k\0..." text. Read raw bytes and detect the encoding.
                var stdoutTask = ReadAllBytesAsync(process.StandardOutput.BaseStream, timeoutCts.Token);
                var stderrTask = ReadAllBytesAsync(process.StandardError.BaseStream, timeoutCts.Token);
                await process.WaitForExitAsync(timeoutCts.Token).ConfigureAwait(false);
                var stdout = AccoreConsoleScriptCheck.CleanForReport(AccoreConsoleScriptCheck.DecodeOutput(await stdoutTask.ConfigureAwait(false)));
                var stderr = AccoreConsoleScriptCheck.CleanForReport(AccoreConsoleScriptCheck.DecodeOutput(await stderrTask.ConfigureAwait(false)));

                if (process.ExitCode != 0)
                {
                    return ForgeResult.Failure(commandId, "accoreconsole_failed", $"accoreconsole exited with code {process.ExitCode}.", stderr);
                }

                if (AccoreConsoleScriptCheck.HasScriptError(stdout, stderr))
                {
                    return ForgeResult.Failure(
                        commandId,
                        AccoreConsoleScriptCheck.ErrorCode,
                        "accoreconsole exited 0, but stdout/stderr reported a script error (*Cancel*, Unknown command, or *Invalid*).",
                        "Inspect data.stdout / data.stderr and fix the script tokens.",
                        data: new { exitCode = process.ExitCode, scriptKind = scriptKindName, stdout, stderr, backupPath });
                }

                return ForgeResult.Success(commandId, new { exitCode = process.ExitCode, scriptKind = scriptKindName, stdout, stderr, backupPath });
            }
            catch (OperationCanceledException)
            {
                try
                {
                    if (!process.HasExited)
                    {
                        process.Kill(entireProcessTree: true);
                    }
                }
                catch
                {
                    // Best effort cleanup after timeout.
                }

                return ForgeResult.Failure(commandId, "accoreconsole_timeout", $"accoreconsole exceeded timeout {timeout.TotalSeconds:n0}s.");
            }
        }
        finally
        {
            try
            {
                if (File.Exists(tempScriptPath))
                {
                    File.Delete(tempScriptPath);
                }

                if (tempLispPath is not null && File.Exists(tempLispPath))
                {
                    File.Delete(tempLispPath);
                }
            }
            catch
            {
                // Best effort temp file cleanup.
            }
        }
    }

    private static async Task<byte[]> ReadAllBytesAsync(Stream stream, CancellationToken cancellationToken)
    {
        using var buffer = new MemoryStream();
        await stream.CopyToAsync(buffer, cancellationToken).ConfigureAwait(false);
        return buffer.ToArray();
    }

    private sealed record RunScriptArgs
    {
        public string? DwgPath { get; init; }
        public string? ScriptPath { get; init; }
        public int? TimeoutSeconds { get; init; }
    }

    private sealed record BatchJobArgs
    {
        public string? DwgPath { get; init; }
        public string? ScriptPath { get; init; }
        public int? TimeoutSeconds { get; init; }
    }

    private sealed record BatchArgs
    {
        public BatchJobArgs[] Jobs { get; init; } = [];
        public bool ContinueOnError { get; init; } = true;
        public string? ResumeBatchId { get; init; }
    }
}
