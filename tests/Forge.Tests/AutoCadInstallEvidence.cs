using Xunit;

namespace Forge.Tests;

/// <summary>
/// Detects whether this machine has a validated AutoCAD install inside the 2017-2026 catalog
/// (a discovered install root containing <c>accoreconsole.exe</c>). Tests that need a real
/// install report as Skipped with an explicit reason instead of passing silently, so the
/// suite never claims coverage it did not execute.
/// </summary>
internal static class AutoCadInstallEvidence
{
    public const string ConfigurationHint =
        "No supported AutoCAD install was found. Set FORGE_AUTOCAD_ROOT to an 'AutoCAD <year>' install directory containing accoreconsole.exe, or install AutoCAD 2017-2026 under C:\\Program Files\\Autodesk. A discovered year outside 2017-2026 does not count.";

    public static bool IsAvailable
    {
        get
        {
            var root = Forge.Shared.ForgeEnvironment.DefaultAutoCadRoot();
            return Forge.Shared.AutoCadHostCatalog.ResolveDefaultHost(root) is not null;
        }
    }
}

/// <summary>
/// A fact that reports as Skipped with a reason when no validated AutoCAD install is present,
/// instead of returning early and counting as a pass.
/// </summary>
internal sealed class AutoCadInstallFactAttribute : FactAttribute
{
    public AutoCadInstallFactAttribute()
    {
        if (!AutoCadInstallEvidence.IsAvailable)
        {
            Skip = AutoCadInstallEvidence.ConfigurationHint;
        }
    }
}
