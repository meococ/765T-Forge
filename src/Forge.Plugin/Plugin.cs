using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.Runtime;
using Forge.Shared;

[assembly: ExtensionApplication(typeof(Forge.Plugin.Plugin))]
[assembly: CommandClass(typeof(Forge.Plugin.Commands))]

namespace Forge.Plugin;

public sealed class Plugin : IExtensionApplication
{
    private NamedPipePluginServer? _server;

    public void Initialize()
    {
        try
        {
            var environment = ForgeEnvironment.FromProcess();
            // Side-by-side dev NETLOAD: when a dev build shares a session with a deployed
            // bundle, both would read FORGE_PIPE_NAME and race for one pipe. A dev-only
            // override lets the second plugin bind its own lane — but only a dev assembly
            // (simple name ends with the DevAsmSuffix, e.g. Forge.Plugin.Dev) may read it;
            // once this build ships, the deployed plugin would otherwise race dev builds
            // for their pipe. (LISP setenv can't reach .NET's process-env snapshot, so
            // the variable must come from the launch environment.)
            environment = environment with
            {
                PipeName = DevBuildPolicy.ResolvePipeName(environment.PipeName, typeof(Plugin).Assembly.GetName().Name)
            };
            if (environment.UsingDevDefaultToken)
            {
                WriteMessage("\n[765T-Forge] WARNING: FORGE_DEV_ALLOW_DEFAULT_TOKEN is enabled.");
            }

            var processor = new PluginCommandProcessor(
                environment,
                new SafetyPolicy(),
                new BackupPlanner(environment.BackupDirectory),
                new FileAuditSink(environment.AuditDirectory));
            _server = new NamedPipePluginServer(environment, processor);
            _server.Start();
            WriteMessage($"\n[765T-Forge] Plugin initialized. Named pipe: {environment.PipeName}");
        }
        catch (InvalidOperationException ex)
        {
            WriteMessage($"\n[765T-Forge] Plugin failed to start: {ex.Message}");
        }
    }

    public void Terminate()
    {
        _server?.Dispose();
        _server = null;
        WriteMessage("\n[765T-Forge] Plugin stopped.");
    }

    private static void WriteMessage(string message)
    {
        Application.DocumentManager.MdiActiveDocument?.Editor.WriteMessage(message);
    }
}

public sealed class Commands
{
    [CommandMethod("MCP_STATUS")]
    public void McpStatus()
    {
        var env = ForgeEnvironment.FromProcess();
        env = env with
        {
            PipeName = DevBuildPolicy.ResolvePipeName(env.PipeName, typeof(Plugin).Assembly.GetName().Name)
        };
        var ed = Application.DocumentManager.MdiActiveDocument?.Editor;
        if (ed is null)
        {
            return;
        }

        ed.WriteMessage(
            $"\n[765T-Forge] product={ForgeConstants.ProductVersion} pipe={env.PipeName} " +
            $"tokenConfigured={!string.IsNullOrWhiteSpace(env.Token)} " +
            $"unsafeOps={env.EnableUnsafeOps} " +
            $"devDefaultToken={env.UsingDevDefaultToken}");
    }
}
