namespace Forge.Shared;

public static class AutoCadHostInfo
{
    /// <summary>
    /// AutoCAD 2026 reports application series R25.1. A host string that is neither that series
    /// nor the year itself is a mismatch with this build.
    /// </summary>
    public static bool IsMismatch(string? hostVersion, string builtForYear)
    {
        if (string.IsNullOrWhiteSpace(hostVersion) || string.IsNullOrWhiteSpace(builtForYear))
        {
            return true;
        }

        if (hostVersion.Contains(builtForYear, StringComparison.Ordinal))
        {
            return false;
        }

        var series = builtForYear switch
        {
            "2026" => "25.1",
            "2025" => "25.0",
            _ => null
        };

        return series is null || !hostVersion.StartsWith(series, StringComparison.Ordinal);
    }
}
