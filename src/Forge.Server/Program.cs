using Forge.Server;
using Forge.Shared;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
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

builder.Services.AddSingleton(environment);
builder.Services.AddSingleton(new SafetyPolicy());
builder.Services.AddSingleton(new FileAuditSink(environment.AuditDirectory));
builder.Services.AddSingleton(new BackupPlanner(environment.BackupDirectory));
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
        filters.AddCallToolFilter(next => async (request, cancellationToken) =>
        {
            var result = await next(request, cancellationToken).ConfigureAwait(false);
            return ForgeCallToolResults.MarkBusinessFailure(result);
        });
        filters.AddListToolsFilter(next => async (request, cancellationToken) =>
        {
            var result = await next(request, cancellationToken).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(environment.ToolProfile) || result.Tools is null)
            {
                return result;
            }

            if (!ToolProfiles.TryGet(environment.ToolProfile, out var allow))
            {
                return result;
            }

            var allowed = new HashSet<string>(allow, StringComparer.OrdinalIgnoreCase);
            result.Tools = result.Tools.Where(tool => allowed.Contains(tool.Name)).ToList();
            return result;
        });
    });

await builder.Build().RunAsync().ConfigureAwait(false);
return 0;
