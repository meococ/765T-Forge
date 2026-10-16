using System.Text;
using System.Text.RegularExpressions;

namespace Forge.Server;

/// <summary>
/// Positive-evidence-only script check for AccoreConsole output. AccoreConsole can exit 0
/// even when a script command failed, so this looks for AutoCAD's literal abort tokens
/// <c>*Cancel*</c>, <c>Unknown command</c>, and <c>*Invalid*</c> (OrdinalIgnoreCase literal
/// search; no patterns, no fuzzy matching).
///
/// Semantics, stated exactly:
/// <list type="bullet">
/// <item>A match fails the run with <see cref="ErrorCode"/>.</item>
/// <item>A non-match does NOT prove the script succeeded. The absence of these literal markers
/// is not evidence of success and must never be read as verification.</item>
/// <item>The only deterministic verification of what happened to a drawing remains readback
/// (<c>forge_qa_readback</c> / <c>forge_qa_readback_after_timeout</c>).</item>
/// </list>
///
/// accoreconsole.exe writes UTF-16LE to a redirected stdout. Decoded as UTF-8 that becomes
/// "U\0n\0k\0..." and the literal search never matches, so the text is normalized first:
/// NUL characters are removed and whitespace runs collapse to one space.
/// </summary>
public static class AccoreConsoleScriptCheck
{
    public const string ErrorCode = "accoreconsole_script_error";

    private static readonly Regex WhitespaceRun = new(@"\s+", RegexOptions.Compiled);

    public static bool HasScriptError(string? stdout, string? stderr)
    {
        var text = Normalize(stdout) + " " + Normalize(stderr);
        return text.Contains("*Cancel*", StringComparison.OrdinalIgnoreCase)
            || text.Contains("Unknown command", StringComparison.OrdinalIgnoreCase)
            || text.Contains("*Invalid*", StringComparison.OrdinalIgnoreCase)
            || text.Contains("*Invalid selection*", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Removes NUL characters (UTF-16 read as 8-bit) and collapses whitespace runs.</summary>
    public static string Normalize(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return "";
        }

        return WhitespaceRun.Replace(text.Replace("\0", ""), " ");
    }

    /// <summary>
    /// Decodes raw accoreconsole output bytes: honours a BOM, otherwise detects UTF-16LE by the
    /// share of zero bytes at odd offsets (ASCII-range UTF-16LE text), else UTF-8. NUL
    /// characters are stripped from the result in every case.
    /// </summary>
    public static string DecodeOutput(byte[]? bytes)
    {
        if (bytes is null || bytes.Length == 0)
        {
            return "";
        }

        string text;
        if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE)
        {
            text = Encoding.Unicode.GetString(bytes, 2, bytes.Length - 2);
        }
        else if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF)
        {
            text = Encoding.BigEndianUnicode.GetString(bytes, 2, bytes.Length - 2);
        }
        else if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
        {
            text = Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3);
        }
        else if (LooksLikeUtf16Le(bytes))
        {
            text = Encoding.Unicode.GetString(bytes, 0, bytes.Length - (bytes.Length % 2));
        }
        else
        {
            text = Encoding.UTF8.GetString(bytes);
        }

        return text.Replace("\0", "");
    }

    private static bool LooksLikeUtf16Le(byte[] bytes)
    {
        var sample = Math.Min(bytes.Length - (bytes.Length % 2), 8192);
        if (sample < 2)
        {
            return false;
        }

        int oddZeros = 0, evenZeros = 0;
        for (var i = 0; i < sample; i += 2)
        {
            if (bytes[i] == 0)
            {
                evenZeros++;
            }

            if (bytes[i + 1] == 0)
            {
                oddZeros++;
            }
        }

        var pairs = sample / 2;
        return oddZeros >= pairs * 0.3 && evenZeros < pairs * 0.1;
    }
}
