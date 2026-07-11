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
            var one = await RunOneAsync(command.Id, job.DwgPath, job.ScriptPath, null, dryRun: false, cancellationToken)
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

        var scriptText = await File.ReadAllTextAsync(scriptPath, cancellationToken).ConfigureAwait(false);
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

        if (dryRun)
        {
            return ForgeResult.Success(commandId, new
            {
                wouldRun = accoreconsole,
                dwgPath,
                scriptPath,
                backup = _backupPlanner.PlanBackupPath(dwgPath)
            });
        }

        var backupPath = _backupPlanner.TryBackup(dwgPath);
        var psi = new ProcessStartInfo
        {
            FileName = accoreconsole,
            ArgumentList = { "/i", dwgPath, "/s", scriptPath },
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
            var stdoutTask = process.StandardOutput.ReadToEndAsync(timeoutCts.Token);
            var stderrTask = process.StandardError.ReadToEndAsync(timeoutCts.Token);
            await process.WaitForExitAsync(timeoutCts.Token).ConfigureAwait(false);
            var stdout = await stdoutTask.ConfigureAwait(false);
            var stderr = await stderrTask.ConfigureAwait(false);

            return process.ExitCode == 0
                ? ForgeResult.Success(commandId, new { exitCode = process.ExitCode, stdout, stderr, backupPath })
                : ForgeResult.Failure(commandId, "accoreconsole_failed", $"accoreconsole exited with code {process.ExitCode}.", stderr);
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
