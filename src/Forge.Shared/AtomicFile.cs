namespace Forge.Shared;

/// <summary>
/// Atomic-ish file replacement for Shared state files: write a sibling <c>.tmp</c> file,
/// then rename it over the destination so readers never observe a partial write.
/// </summary>
public static class AtomicFile
{
    public static void WriteAllText(string path, string contents)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException("Value cannot be null or whitespace.", nameof(path));
        }

        var temporaryPath = path + ".tmp";
        File.WriteAllText(temporaryPath, contents);
        if (File.Exists(path))
        {
            File.Replace(temporaryPath, path, null);
        }
        else
        {
            File.Move(temporaryPath, path);
        }
    }
}
