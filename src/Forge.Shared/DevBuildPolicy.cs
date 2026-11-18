namespace Forge.Shared;

/// <summary>
/// Side-by-side dev builds are compiled with -p:DevAsmSuffix=.Dev, which renames the
/// plugin (and shared) assembly so it can NETLOAD next to a deployed bundle — the same
/// simple name would just resolve to the already-loaded deployed copy. The
/// FORGE_PIPE_NAME_DEV override exists so that dev plugin can bind its own pipe in a
/// session where a deployed plugin already holds FORGE_PIPE_NAME. Only a dev assembly
/// may honour it: once a build carrying the override ships, the deployed plugin sees
/// the same variable in a dev session and would race the dev build for one pipe.
/// </summary>
public static class DevBuildPolicy
{
    /// <summary>Assembly-name suffix produced by -p:DevAsmSuffix for dev builds.</summary>
    public const string AssemblySuffix = ".Dev";

    /// <summary>
    /// True when an assembly simple name carries the dev suffix (Forge.Plugin.Dev,
    /// Forge.Shared.Dev). Case-insensitive: a dev build with a differently-cased
    /// -p:DevAsmSuffix value must still recognise itself as dev rather than fall back
    /// to FORGE_PIPE_NAME and race the deployed plugin for the main pipe.
    /// </summary>
    public static bool IsDevAssembly(string? simpleName)
        => !string.IsNullOrEmpty(simpleName)
           && simpleName.EndsWith(AssemblySuffix, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Resolves the pipe the plugin binds: FORGE_PIPE_NAME_DEV when the running plugin
    /// assembly is a dev build and the variable is set, otherwise the configured name.
    /// A production assembly never reads the variable.
    /// </summary>
    public static string ResolvePipeName(string configuredPipeName, string? pluginAssemblySimpleName)
    {
        if (!IsDevAssembly(pluginAssemblySimpleName))
        {
            return configuredPipeName;
        }

        var devPipe = Environment.GetEnvironmentVariable("FORGE_PIPE_NAME_DEV");
        return string.IsNullOrWhiteSpace(devPipe) ? configuredPipeName : devPipe;
    }
}
