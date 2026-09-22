#pragma warning disable MCPEXP001
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Forge.Server;

public static class ForgeMcpHost
{
    public const string ServerName = "765T-Forge";

    public const string Instructions =
        "Call forge_system_health before other tools. Take drawing numbers only from forge_registry_lookup. Do not omit handle on forge_block_set_attr. A gate fails when passed is false or Ok is false; the report stays in data. completed=false or dryRun means the work is not finished — call again with dryRun=false to perform it. Use force only when a human asks in this session. On timeout, read back; do not retry a write blindly.";

    public static void ApplyServerOptions(McpServerOptions options)
    {
        options.ServerInfo = new Implementation
        {
            Name = ServerName,
            Version = Forge.Shared.ForgeConstants.ProductVersion
        };
        options.ServerInstructions = Instructions;
        ForbidTaskSupport(options);
    }

    public static void ForbidTaskSupport(McpServerOptions options)
    {
        if (options.ToolCollection is null)
        {
            return;
        }

        foreach (var tool in options.ToolCollection)
        {
            tool.ProtocolTool.Execution ??= new ToolExecution();
            tool.ProtocolTool.Execution.TaskSupport = ToolTaskSupport.Forbidden;
        }
    }
}
