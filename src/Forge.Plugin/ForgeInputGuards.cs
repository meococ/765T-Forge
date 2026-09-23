using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Forge.Shared;

namespace Forge.Plugin;

/// <summary>
/// Exact validator for a value interpolated into an AutoCAD command string.
/// AutoCAD's command line has no backslash escape: a double quote in an argument
/// toggles the next token boundary, so it cannot be escaped — it must be rejected.
/// CR/LF and every other control character are rejected for the same reason.
/// </summary>
public static class ForgeCommandTokenGuard
{
    /// <summary>
    /// Returns <paramref name="value"/> unchanged when it is safe to interpolate into a
    /// command string; otherwise throws <see cref="ArgumentException"/> naming the field.
    /// Never attempts to escape.
    /// </summary>
    public static string RequireCommandToken(string? value, string fieldName)
    {
        if (value is null)
        {
            throw new ArgumentException($"Parameter '{fieldName}' must not be null.", fieldName);
        }

        foreach (var character in value)
        {
            if (character == '"' || char.IsControl(character))
            {
                throw new ArgumentException(
                    $"Parameter '{fieldName}' contains an illegal command-line character (quote or control character U+{(int)character:X4}).",
                    fieldName);
            }
        }

        return value;
    }
}

/// <summary>
/// Exact <c>command.Id</c> shape check: the id is interpolated into a temporary file name,
/// so a direct pipe client must not be able to smuggle path separators or traversal.
/// </summary>
public static class ForgeCommandId
{
    /// <summary>Exact accepted shape for a wire-supplied command id.</summary>
    public static readonly Regex Pattern = new(
        "^[A-Za-z0-9-]{1,64}$",
        RegexOptions.CultureInvariant,
        ForgeConstants.RegexMatchTimeout);

    public static bool IsValid(string? id) => !string.IsNullOrEmpty(id) && Pattern.IsMatch(id);

    public static string RequireValid(string? id)
    {
        if (!IsValid(id))
        {
            throw new ArgumentException("command.Id must match ^[A-Za-z0-9-]{1,64}$.", nameof(id));
        }

        return id!;
    }
}

/// <summary>Exact named-pipe frame size limit, identical on both TFMs.</summary>
public static class ForgePipeFrames
{
    public const int MaxFrameCharacters = 4_000_000;

    public static bool ExceedsLimit(string? frame)
        => frame is not null && frame.Length > MaxFrameCharacters;
}

/// <summary>Bounds for the Roslyn executor. The timeout bounds waiting, not the script.</summary>
public static class ForgeExecDotNetLimits
{
    public const int TimeoutSeconds = 60;
}

/// <summary>
/// Exact tool-name gate for the awaited dispatch path. Only the Roslyn executor needs
/// <c>await</c>; every other tool completes synchronously inside the AutoCAD command context.
/// </summary>
public static class ForgeAsyncDispatch
{
    public const string AwaitedTool = "forge_exec_dotnet";

    public static bool RequiresAwaitedDispatch(string? tool)
        => string.Equals(tool, AwaitedTool, StringComparison.OrdinalIgnoreCase);
}

/// <summary>
/// Exact units-token normalization. Accepts only the three <c>PlotPaperUnit</c> member names;
/// units are never inferred from a paper-size string.
/// </summary>
public static class ForgePlotUnits
{
    public static bool TryNormalize(string? requested, out string? units)
    {
        units = null;
        if (string.IsNullOrWhiteSpace(requested))
        {
            return true;
        }

        if (requested.Equals("Inches", StringComparison.OrdinalIgnoreCase))
        {
            units = "Inches";
            return true;
        }

        if (requested.Equals("Millimeters", StringComparison.OrdinalIgnoreCase))
        {
            units = "Millimeters";
            return true;
        }

        if (requested.Equals("Pixels", StringComparison.OrdinalIgnoreCase))
        {
            units = "Pixels";
            return true;
        }

        return false;
    }
}

/// <summary>
/// Deterministic pack-and-go destination planner. Two source files with the same file name
/// (for example <c>a\grid.dwg</c> and <c>b\grid.dwg</c>) would otherwise be copied to the
/// same destination. Collisions are resolved with a short stable suffix derived from the
/// source's parent folder name (hash fallback when the parent name has no usable characters),
/// and the comparison matches Windows file-name semantics (case-insensitive).
/// </summary>
public static class PackDestinationResolver
{
    /// <summary>Maximum length of the collision suffix appended before the extension.</summary>
    public const int MaxSuffixLength = 24;

    public static IReadOnlyList<string> Resolve(IReadOnlyList<string> sourcePaths, string destinationDirectory)
    {
#if NETFRAMEWORK
        if (sourcePaths is null)
        {
            throw new ArgumentNullException(nameof(sourcePaths));
        }
#else
        ArgumentNullException.ThrowIfNull(sourcePaths);
#endif

        if (string.IsNullOrWhiteSpace(destinationDirectory))
        {
            throw new ArgumentException("Value cannot be null or whitespace.", nameof(destinationDirectory));
        }

        var destinations = new string[sourcePaths.Count];
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // Stable order: identical inputs must always produce identical destinations.
        var order = Enumerable.Range(0, sourcePaths.Count)
            .OrderBy(index => Path.GetFileName(sourcePaths[index]), StringComparer.OrdinalIgnoreCase)
            .ThenBy(index => sourcePaths[index], StringComparer.OrdinalIgnoreCase)
            .ToArray();

        foreach (var index in order)
        {
            var source = sourcePaths[index];
            if (string.IsNullOrWhiteSpace(source))
            {
                throw new ArgumentException("Source path must not be null or whitespace.", nameof(sourcePaths));
            }

            var fileName = Path.GetFileName(source);
            if (string.IsNullOrEmpty(fileName))
            {
                throw new ArgumentException($"Source path '{source}' does not contain a file name.", nameof(sourcePaths));
            }

            var candidate = fileName;
            if (!used.Add(candidate))
            {
                var suffix = ParentFolderSuffix(source);
                candidate = AppendSuffix(fileName, suffix);
                for (var attempt = 2; !used.Add(candidate); attempt++)
                {
                    candidate = AppendSuffix(fileName, $"{suffix}-{attempt.ToString(CultureInfo.InvariantCulture)}");
                }
            }

            destinations[index] = Path.Combine(destinationDirectory, candidate);
        }

        return destinations;
    }

    private static string AppendSuffix(string fileName, string suffix)
    {
        var stem = Path.GetFileNameWithoutExtension(fileName);
        var extension = Path.GetExtension(fileName);
        return $"{stem}-{suffix}{extension}";
    }

    private static string ParentFolderSuffix(string sourcePath)
    {
        var parent = "";
        try
        {
            var directory = Path.GetDirectoryName(Path.GetFullPath(sourcePath));
            if (!string.IsNullOrEmpty(directory))
            {
                parent = Path.GetFileName(directory.TrimEnd(
                    Path.DirectorySeparatorChar,
                    Path.AltDirectorySeparatorChar));
            }
        }
        catch (ArgumentException)
        {
            parent = "";
        }

        var sanitized = SanitizeSuffix(parent);
        return sanitized.Length > 0 ? sanitized : StableHashToken(sourcePath);
    }

    private static string SanitizeSuffix(string value)
    {
        var builder = new StringBuilder(value.Length);
        foreach (var character in value)
        {
            if (char.IsLetterOrDigit(character) || character is '_' or '-')
            {
                builder.Append(character);
            }
            else
            {
                builder.Append('-');
            }
        }

        var trimmed = builder.ToString().Trim('-');
        return trimmed.Length <= MaxSuffixLength ? trimmed : trimmed.Substring(0, MaxSuffixLength);
    }

    private static string StableHashToken(string value)
    {
#if NETFRAMEWORK
        using var sha = SHA256.Create();
        var hash = sha.ComputeHash(Encoding.UTF8.GetBytes(value));
#else
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(value));
#endif
        return Hex.Encode(hash).Substring(0, 8);
    }
}
