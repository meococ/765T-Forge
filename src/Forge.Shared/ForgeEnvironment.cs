using System.Globalization;
using System.Text.RegularExpressions;

namespace Forge.Shared;

public sealed record ForgeEnvironment
{
    /// <summary>Highest-priority explicit AutoCAD install root override.</summary>
    public const string AutoCadRootVariable = "FORGE_AUTOCAD_ROOT";

    /// <summary>Deprecated alias for <see cref="AutoCadRootVariable"/>; still honored.</summary>
    public const string AutoCadRootLegacyVariable = "AUTOCAD_2026_ROOT";

    /// <summary>Parent directory scanned for <c>AutoCAD &lt;year&gt;</c> installs.</summary>
    public const string AutoCadInstallParent = @"C:\Program Files\Autodesk";

    /// <summary>Exact install-directory pattern: "AutoCAD " followed by four digits.</summary>
    public static readonly Regex AutoCadDirectoryPattern = new(
        @"^AutoCAD [0-9]{4}$",
        RegexOptions.CultureInvariant,
        ForgeConstants.RegexMatchTimeout);

    public string PipeName { get; init; } = ForgeConstants.DefaultPipeName;
    public string Token { get; init; } = "";
    public string BackupDirectory { get; init; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "765T-Forge",
        "backups");
    public string AuditDirectory { get; init; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "765T-Forge",
        "audit");
    public string AutoCadRoot { get; init; } = DefaultAutoCadRoot();

    /// <summary>Four-digit AutoCAD release discovered from <see cref="AutoCadRoot"/>, when recognizable.</summary>
    public string? AutoCadYear { get; init; }

    public bool EnableUnsafeOps { get; init; }
    /// <summary>When false (default), <c>force=true</c> on publish/recipe is refused (<c>force_not_allowed</c>).</summary>
    public bool AllowForcePublish { get; init; }
    public int PluginResponseTimeoutSeconds { get; init; } = 120;
    public bool UsingDevDefaultToken { get; init; }

    /// <summary>
    /// Deterministic AutoCAD root resolution: <c>FORGE_AUTOCAD_ROOT</c> override, then the
    /// deprecated <c>AUTOCAD_2026_ROOT</c> alias, then install discovery. Never guesses a path
    /// that does not exist — discovery returns an empty string when nothing validated is found.
    /// </summary>
    public static string DefaultAutoCadRoot()
    {
        var overrideRoot = Environment.GetEnvironmentVariable(AutoCadRootVariable);
        if (!string.IsNullOrWhiteSpace(overrideRoot))
        {
            return overrideRoot!.Trim();
        }

        var legacyRoot = Environment.GetEnvironmentVariable(AutoCadRootLegacyVariable);
        if (!string.IsNullOrWhiteSpace(legacyRoot))
        {
            return legacyRoot!.Trim();
        }

        return DiscoverAutoCadRoot();
    }

    /// <summary>
    /// Scans <see cref="AutoCadInstallParent"/> for exact <c>AutoCAD &lt;year&gt;</c> directories,
    /// keeps only installs that contain <c>accoreconsole.exe</c>, and returns the highest year.
    /// Returns an empty string when no validated install exists.
    /// </summary>
    public static string DiscoverAutoCadRoot()
    {
        try
        {
            if (!Directory.Exists(AutoCadInstallParent))
            {
                return "";
            }

            var bestYear = -1;
            var bestRoot = "";
            foreach (var directory in Directory.GetDirectories(AutoCadInstallParent))
            {
                var name = Path.GetFileName(directory);
                if (!AutoCadDirectoryPattern.IsMatch(name))
                {
                    continue;
                }

                if (!int.TryParse(
                        name.Substring("AutoCAD ".Length),
                        NumberStyles.None,
                        CultureInfo.InvariantCulture,
                        out var year))
                {
                    continue;
                }

                if (!File.Exists(Path.Combine(directory, "accoreconsole.exe")))
                {
                    continue;
                }

                if (year > bestYear)
                {
                    bestYear = year;
                    bestRoot = directory;
                }
            }

            return bestRoot;
        }
        catch (IOException)
        {
            return "";
        }
        catch (UnauthorizedAccessException)
        {
            return "";
        }
        catch (System.Security.SecurityException)
        {
            return "";
        }
    }

    /// <summary>Extracts the year from a root whose final directory is exactly <c>AutoCAD &lt;year&gt;</c>.</summary>
    public static string? TryExtractAutoCadYear(string? autoCadRoot)
    {
        if (string.IsNullOrWhiteSpace(autoCadRoot))
        {
            return null;
        }

        try
        {
            var trimmed = autoCadRoot!.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var name = Path.GetFileName(trimmed);
            return AutoCadDirectoryPattern.IsMatch(name) ? name.Substring("AutoCAD ".Length) : null;
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    public static ForgeEnvironment FromProcess()
    {
        var token = FirstNonEmpty(
            Environment.GetEnvironmentVariable("FORGE_AUTOCAD_TOKEN"),
            Environment.GetEnvironmentVariable("MCP_AUTOCAD_TOKEN"));
        var allowDevDefault = bool.TryParse(
            Environment.GetEnvironmentVariable("FORGE_DEV_ALLOW_DEFAULT_TOKEN"),
            out var enabled) && enabled;

        if (string.IsNullOrWhiteSpace(token))
        {
            if (!allowDevDefault)
            {
                throw new InvalidOperationException(
                    "FORGE_AUTOCAD_TOKEN (or MCP_AUTOCAD_TOKEN) must be set. " +
                    "For local development only, set FORGE_DEV_ALLOW_DEFAULT_TOKEN=true.");
            }

            token = ForgeConstants.DevOnlyInsecureToken;
        }

        var autoCadRoot = DefaultAutoCadRoot();
        return new ForgeEnvironment
        {
            PipeName = Get("FORGE_PIPE_NAME", ForgeConstants.DefaultPipeName),
            Token = token!,
            UsingDevDefaultToken = string.Equals(token, ForgeConstants.DevOnlyInsecureToken, StringComparison.Ordinal),
            BackupDirectory = Get("FORGE_BACKUP_DIR", Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "765T-Forge",
                "backups")),
            AuditDirectory = Get("FORGE_AUDIT_DIR", Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "765T-Forge",
                "audit")),
            AutoCadRoot = autoCadRoot,
            AutoCadYear = TryExtractAutoCadYear(autoCadRoot),
            EnableUnsafeOps = bool.TryParse(Environment.GetEnvironmentVariable("FORGE_ENABLE_UNSAFE_OPS"), out var unsafeOps) && unsafeOps,
            AllowForcePublish = bool.TryParse(Environment.GetEnvironmentVariable("FORGE_ALLOW_FORCE_PUBLISH"), out var allowForce) && allowForce,
            PluginResponseTimeoutSeconds = ParseTimeout(Environment.GetEnvironmentVariable("FORGE_PLUGIN_RESPONSE_TIMEOUT_SECONDS"))
        };
    }

    private static int ParseTimeout(string? value)
    {
        if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var timeout))
        {
            return 120;
        }

        return timeout < 5 ? 5 : (timeout > 3600 ? 3600 : timeout);
    }

    private static string Get(string name, string fallback)
    {
        var value = Environment.GetEnvironmentVariable(name);
        return string.IsNullOrWhiteSpace(value) ? fallback : value;
    }

    private static string? FirstNonEmpty(params string?[] values)
    {
        foreach (var value in values)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value;
            }
        }

        return null;
    }
}
