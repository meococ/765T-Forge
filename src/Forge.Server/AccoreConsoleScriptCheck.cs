namespace Forge.Server;

public static class AccoreConsoleScriptCheck
{
    public const string ErrorCode = "accoreconsole_script_error";

    public static bool HasScriptError(string? stdout, string? stderr)
    {
        var text = (stdout ?? "") + "\n" + (stderr ?? "");
        return text.Contains("*Cancel*", StringComparison.OrdinalIgnoreCase)
            || text.Contains("Unknown command", StringComparison.OrdinalIgnoreCase)
            || text.Contains("*Invalid*", StringComparison.OrdinalIgnoreCase);
    }
}
