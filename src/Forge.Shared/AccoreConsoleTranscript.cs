using System.Security.Cryptography;

namespace Forge.Shared;

public readonly record struct DrawingFileStamp(DateTimeOffset? MtimeUtc, long? Length, string? Sha256);

public static class AccoreConsoleTranscript
{
    public static bool HasScriptError(string? stdout, string? stderr)
    {
        var text = string.Concat(stdout, "\n", stderr);
        return text.Contains("*Cancel*", StringComparison.OrdinalIgnoreCase)
               || text.Contains("Unknown command", StringComparison.OrdinalIgnoreCase)
               || text.Contains("*Invalid*", StringComparison.OrdinalIgnoreCase);
    }

    public static DrawingFileStamp ReadStamp(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            return default;
        }

        var info = new FileInfo(path);
        string? hash = null;
        try
        {
            using var stream = File.OpenRead(path);
            hash = Convert.ToHexString(SHA256.HashData(stream));
        }
        catch (IOException)
        {
            hash = null;
        }

        return new DrawingFileStamp(info.LastWriteTimeUtc, info.Length, hash);
    }
}
