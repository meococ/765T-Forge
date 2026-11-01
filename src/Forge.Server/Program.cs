using System.Text.Json;
using Forge.Server;
using Forge.Shared;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

ForgeEnvironment environment;
try
{
    environment = ForgeEnvironment.FromProcess();
}
catch (InvalidOperationException ex)
{
    Console.Error.WriteLine($"[765T-Forge] {ex.Message}");
    return 1;
}

if (environment.UsingDevDefaultToken)
{
    Console.Error.WriteLine("[765T-Forge] WARNING: using FORGE_DEV_ALLOW_DEFAULT_TOKEN. Do not use in production.");
}

var builder = Host.CreateApplicationBuilder(args);

builder.Logging.AddConsole(options =>
{
    options.LogToStandardErrorThreshold = LogLevel.Trace;
});

// --toolsets=core,draw overrides FORGE_TOOLSETS for this process.
var toolsetSelection = environment.Toolsets;
foreach (var arg in args)
{
    if (arg.StartsWith("--toolsets=", StringComparison.Ordinal))
    {
        toolsetSelection = arg["--toolsets=".Length..];
    }
}

builder.Services.AddSingleton(environment);
builder.Services.AddSingleton(new SafetyPolicy());
builder.Services.AddSingleton(new FileAuditSink(environment.AuditDirectory));
builder.Services.AddSingleton(new BackupPlanner(environment.BackupDirectory));
builder.Services.AddSingleton(new ForgeToolsetState(toolsetSelection));
builder.Services.AddSingleton<ForgePipeClient>();
builder.Services.AddSingleton<HeadlessAccoreConsoleRunner>();
builder.Services.AddSingleton<ForgeToolRunner>();

builder.Services
    .AddMcpServer(ForgeMcpHost.ApplyServerOptions)
    .WithStdioServerTransport()
    .WithTools<ForgeMcpTools>()
    .WithResources<ForgeMcpResources>()
    .WithPrompts<ForgeMcpPrompts>()
    .WithRequestFilters(filters =>
    {
        filters.AddListToolsFilter(next => async (context, cancellationToken) =>
        {
            var result = await next(context, cancellationToken).ConfigureAwait(false);
            if (result.Tools is not null && context.Services is not null)
            {
                var toolsets = context.Services.GetRequiredService<ForgeToolsetState>();
                result.Tools = result.Tools.Where(t => toolsets.IsEnabled(t.Name)).ToList();
            }

            if (result.Tools is not null)
            {
                foreach (var tool in result.Tools)
                {
                    if (tool.InputSchema.ValueKind == JsonValueKind.Object)
                    {
                        tool.InputSchema = ForgeSchemaSanitizer.Sanitize(tool.InputSchema);
                    }

                    if (tool.OutputSchema is { ValueKind: JsonValueKind.Object } os)
                    {
                        tool.OutputSchema = ForgeSchemaSanitizer.Sanitize(os);
                    }
                }
            }

            return result;
        });
        filters.AddCompleteFilter(next => async (context, cancellationToken) =>
        {
            // Live completions for prompt/resource-template args — layout/layer/xref names
            // come from the running AutoCAD; ForgeLiveCompletions returns null for arg
            // names it does not own so SDK-defined completions still answer.
            var runner = context.Services?.GetService<ForgeToolRunner>();
            if (runner is not null && context.Params is not null)
            {
                var live = await ForgeLiveCompletions
                    .TryCompleteAsync(runner, context.Params, cancellationToken)
                    .ConfigureAwait(false);
                if (live is not null)
                {
                    return live;
                }
            }

            return await next(context, cancellationToken).ConfigureAwait(false);
        });
        filters.AddCallToolFilter(next => async (context, cancellationToken) =>
        {
            var toolsets = context.Services?.GetService<ForgeToolsetState>();
            var toolName = context.Params?.Name;
            if (toolsets is not null && toolName is not null && !toolsets.IsEnabled(toolName))
            {
                var set = ForgeToolsets.ToolsetForTool(toolName);
                var err = ForgeResult.Failure(
                    context.Params?.Name ?? "unknown",
                    "tool_not_enabled",
                    $"'{toolName}' sits in the '{set}' toolset, which is not enabled in this session.",
                    "Call forge_toolset_enable with the toolset name, or relaunch with FORGE_TOOLSETS=all.",
                    data: new { toolset = set, enabled = toolsets.Enabled });
                var structured = ForgeJson.ToElement(err);
                return new CallToolResult
                {
                    IsError = true,
                    StructuredContent = structured,
                    Content =
                    [
                        new TextContentBlock { Text = $"{err.Error!.Code}: {err.Error.Message}" },
                        new TextContentBlock { Text = JsonSerializer.Serialize(structured, ForgeJson.Options) }
                    ]
                };
            }

            // _meta.progressToken → ambient scope: pipe progress lines + the headless runner
            // forward to it as notifications/progress for the life of this call.
            var progress = ForgeProgressScope.FromRequest(context.Server, context.Params);
            using var _ = progress is not null ? ForgeProgressScope.Push(progress) : null;
            // elicitation.form capability → *_not_acknowledged refusals can ask the user
            // and retry once with the ack flag (fallback = the explicit tool args).
            using var __ = ForgeElicitationGate.Push(context.Server);
            var result = await next(context, cancellationToken).ConfigureAwait(false);
            return ForgeCallToolResults.MarkBusinessFailure(result);
        });
    });

await builder.Build().RunAsync().ConfigureAwait(false);
return 0;
