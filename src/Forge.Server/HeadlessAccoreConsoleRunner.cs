using System.Diagnostics;
using System.Text.Json;
using Forge.Shared;

namespace Forge.Server;

public sealed class HeadlessAccoreConsoleRunner
{
    private readonly BackupPlanner _backupPlanner;
    private readonly SafetyPolicy _safetyPolicy;
    private readonly Func<string, string?> _readEnv;

    public HeadlessAccoreConsoleRunner(
        ForgeEnvironment environment,
        BackupPlanner backupPlanner,
        SafetyPolicy safetyPolicy,
        Func<string, string?>? readEnv = null)
    {
        // Console selection uses AccoreConsoleLocator, not ForgeEnvironment.AutoCadRoot.
        _ = environment;
        _backupPlanner = backupPlanner;
        _safetyPolicy = safetyPolicy;
        _readEnv = readEnv ?? Environment.GetEnvironmentVariable;
    }

    public async Task<ForgeResult> RunScriptAsync(ForgeCommand command, CancellationToken cancellationToken = default)
    {
        var args = ForgeJson.FromElement<RunScriptArgs>(command.Args) ?? new RunScriptArgs();
        return await RunOneAsync(
                command.Id,
                args.DwgPath,
                args.ScriptPath,
                args.TimeoutSeconds,
                command.DryRun,
                jobYear: null,
                callYear: args.AutoCadYear,
                cancellationToken)
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
            for (var index = 0; index < state.Jobs.Count; index++)
            {
                var job = state.Jobs[index];
                if (string.IsNullOrWhiteSpace(job.ScriptPath) || !File.Exists(job.ScriptPath))
                {
                    return ForgeResult.Failure(command.Id, "script_not_found", $"Script file not found: {job.ScriptPath}");
                }

                var scriptText = await File.ReadAllTextAsync(job.ScriptPath, cancellationToken).ConfigureAwait(false);
                // Scan the script body with the open-world script tool. forge_batch_run stays
                // OpenWorld=false so job paths are not treated as commands.
                var scriptDecision = _safetyPolicy.EvaluateText("forge_run_script", scriptText);
                if (!scriptDecision.Allowed)
                {
                    return ForgeResult.Failure(command.Id, scriptDecision.Code, scriptDecision.Message, scriptDecision.Suggestion);
                }

                if (BatchDryRunShouldLocate(args))
                {
                    var missing = MissingConsole(command.Id, JobYear(args, index), args.AutoCadYear);
                    if (missing is not null)
                    {
                        return missing;
                    }
                }
            }

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
        for (var index = 0; index < state.Jobs.Count; index++)
        {
            var job = state.Jobs[index];
            if (job.Status == "ok")
            {
                results.Add(new { job.DwgPath, job.ScriptPath, Ok = true, skipped = true, status = job.Status });
                continue;
            }

            cancellationToken.ThrowIfCancellationRequested();
            job.Status = "running";
            state.Save();
            var one = await RunOneAsync(
                    command.Id,
                    job.DwgPath,
                    job.ScriptPath,
                    JobTimeout(args, index),
                    dryRun: false,
                    JobYear(args, index),
                    args.AutoCadYear,
                    cancellationToken)
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
        int? jobYear,
        int? callYear,
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

        var year = AccoreConsoleLocator.ResolveYear(jobYear, callYear, _readEnv(AccoreConsoleLocator.YearEnvironmentVariable));
        var choice = AccoreConsoleLocator.Locate(year, _readEnv, File.Exists);
        if (!choice.Found)
        {
            return ForgeResult.Failure(
                commandId,
                choice.ErrorCode ?? AccoreConsoleLocator.NotFoundCode,
                choice.Message ?? "accoreconsole.exe was not found.");
        }

        var accoreconsole = choice.ExePath ?? "";

        if (dryRun)
        {
            return ForgeResult.Success(commandId, new
            {
                wouldRun = accoreconsole,
                autoCadYear = choice.Year,
                dwgPath,
                scriptPath,
                backup = _backupPlanner.PlanBackupPath(dwgPath)
            });
        }

        var backupPath = _backupPlanner.TryBackup(dwgPath);
        if (backupPath is null)
        {
            return ForgeResult.Failure(
                commandId,
                "backup_unavailable",
                "Refusing to run the script because a drawing backup could not be created.",
                "Ensure the DWG exists and FORGE_BACKUP_DIR is writable.");
        }

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

        var timeout = TimeSpan.FromSeconds(ClampJobTimeout(timeoutSeconds));
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(timeout);

        try
        {
            var stdoutTask = process.StandardOutput.ReadToEndAsync(timeoutCts.Token);
            var stderrTask = process.StandardError.ReadToEndAsync(timeoutCts.Token);
            await process.WaitForExitAsync(timeoutCts.Token).ConfigureAwait(false);
            var stdout = await stdoutTask.ConfigureAwait(false);
            var stderr = await stderrTask.ConfigureAwait(false);

            if (process.ExitCode != 0)
            {
                return ForgeResult.Failure(commandId, "accoreconsole_failed", $"accoreconsole exited with code {process.ExitCode}.", stderr);
            }

            if (AccoreConsoleScriptCheck.HasScriptError(stdout, stderr))
            {
                return ForgeResult.Failure(
                    commandId,
                    AccoreConsoleScriptCheck.ErrorCode,
                    "accoreconsole exited 0 but stdout reports *Cancel*, Unknown command, or *Invalid*.",
                    "Fix the script. Exit code 0 alone is not a successful run. AutoCAD's real exit-code behavior for these messages is unverified without accoreconsole.");
            }

            return ForgeResult.Success(commandId, new { exitCode = process.ExitCode, stdout, stderr, backupPath });
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

    private bool BatchDryRunShouldLocate(BatchArgs args)
    {
        if (args.AutoCadYear is not null || args.Jobs.Any(job => job.AutoCadYear is not null))
        {
            return true;
        }

        return !string.IsNullOrWhiteSpace(_readEnv(AccoreConsoleLocator.YearEnvironmentVariable));
    }

    public static int ClampJobTimeout(int? timeoutSeconds)
        => Math.Clamp(timeoutSeconds ?? 300, 5, 3600);

    private static int? JobTimeout(BatchArgs args, int index)
    {
        if (!string.IsNullOrWhiteSpace(args.ResumeBatchId) || index < 0 || index >= args.Jobs.Length)
        {
            return null;
        }

        return args.Jobs[index].TimeoutSeconds;
    }

    private int? JobYear(BatchArgs args, int index)
    {
        if (!string.IsNullOrWhiteSpace(args.ResumeBatchId) || index < 0 || index >= args.Jobs.Length)
        {
            return null;
        }

        return args.Jobs[index].AutoCadYear;
    }

    private ForgeResult? MissingConsole(string commandId, int? jobYear, int? callYear)
    {
        var year = AccoreConsoleLocator.ResolveYear(jobYear, callYear, _readEnv(AccoreConsoleLocator.YearEnvironmentVariable));
        var choice = AccoreConsoleLocator.Locate(year, _readEnv, File.Exists);
        if (choice.Found)
        {
            return null;
        }

        return ForgeResult.Failure(
            commandId,
            choice.ErrorCode ?? AccoreConsoleLocator.NotFoundCode,
            choice.Message ?? "accoreconsole.exe was not found.");
    }

    private sealed record RunScriptArgs
    {
        public string? DwgPath { get; init; }
        public string? ScriptPath { get; init; }
        public int? TimeoutSeconds { get; init; }
        public int? AutoCadYear { get; init; }
    }

    private sealed record BatchJobArgs
    {
        public string? DwgPath { get; init; }
        public string? ScriptPath { get; init; }
        public int? TimeoutSeconds { get; init; }
        public int? AutoCadYear { get; init; }
    }

    private sealed record BatchArgs
    {
        public BatchJobArgs[] Jobs { get; init; } = [];
        public bool ContinueOnError { get; init; } = true;
        public string? ResumeBatchId { get; init; }
        public int? AutoCadYear { get; init; }
    }
}
