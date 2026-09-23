using Forge.Shared;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Forge.Server;

/// <summary>
/// MCP server identity and instructions. Task support is deliberately not configured here:
/// the SDK 1.3.0 API for it is marked experimental (MCPEXP001), and this repo does not
/// suppress analyzer diagnostics.
/// </summary>
public static class ForgeMcpHost
{
    public const string ServerName = ForgeConstants.ServerName;

    public const string Instructions =
        "Call forge_system_health before other tools. Take drawing numbers only from forge_registry_lookup. Pass handle (or blockName when the handle is unknown) on forge_block_set_attr. A gate fails when passed is false or Ok is false; the report stays in data. completed=false or dryRun means the work is not finished - call again with dryRun=false to perform it. Free-text executors are blocked as a whole unless unsafe ops are enabled and acknowledged; no argument text is inspected. Use force only when a human asks in this session, and only with FORGE_ALLOW_FORCE_PUBLISH=true; otherwise the server refuses with force_not_allowed. On timeout, read back; do not retry a write blindly.";

    public static void ApplyServerOptions(McpServerOptions options)
    {
        options.ServerInfo = new Implementation
        {
            Name = ServerName,
            Version = ForgeConstants.ProductVersion
        };
        options.ServerInstructions = Instructions;
    }
}
