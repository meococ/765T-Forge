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
