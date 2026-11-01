using Forge.Shared;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

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

        // MCP tasks (2025-11-25 experimental — the whole surface is MCPEXP001-gated):
        // every tool already advertises execution.taskSupport="optional" — honor that
        // claim so clients can detach long calls (publish, batch, dgn) into tasks/get +
        // tasks/result. In-memory is the right store: a task's result only matters while
        // this session lives, and batch resume has its own durable path (BatchResumeState).
#pragma warning disable MCPEXP001 // experimental MCP tasks surface — deliberate opt-in
        options.TaskStore = new InMemoryMcpTaskStore();
        options.Capabilities ??= new ServerCapabilities();
        options.Capabilities.Tasks = new McpTasksCapability
        {
            List = new ListMcpTasksCapability(),
            Cancel = new CancelMcpTasksCapability(),
            Requests = new RequestMcpTasksCapability
            {
                Tools = new ToolsMcpTasksCapability { Call = new CallToolMcpTasksCapability() }
            }
        };
#pragma warning restore MCPEXP001

        // completion/complete (2025-11-25) — argument auto-completion for prompt and
        // resource-template args. Without the advertised capability the SDK rejects
        // the method outright (-32601). The CompleteFilters chain in Program.cs
        // answers layout/layer/xref/profile names (live from the drawing when it can).
        options.Capabilities ??= new ServerCapabilities();
        options.Capabilities.Completions = new CompletionsCapability();
    }
}
