using Forge.Shared;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Forge.Server;

/// <summary>
/// MCP server identity and instructions.
/// </summary>
public static class ForgeMcpHost
{
    public const string ServerName = ForgeConstants.ServerName;

    public const string Instructions =
        "Call forge_system_health before other tools. Prefer typed tools (forge_<group>_<action>) over generic executors. Take drawing numbers only from forge_registry_lookup. Pass handle (or blockName when the handle is unknown) on forge_block_set_attr. A gate fails when passed is false or Ok is false; the report stays in data. completed=false or dryRun means the work is not finished - call again with dryRun=false to perform it. On timeout, call forge_qa_readback_after_timeout; do not retry a write blindly.";

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
