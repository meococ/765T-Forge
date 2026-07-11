namespace Forge.Shared;

public sealed class BackupPlanner
{
    private readonly string _backupRoot;

    public BackupPlanner(string backupRoot)
    {
        _backupRoot = backupRoot;
    }

    public string PlanBackupPath(string drawingPath, DateTimeOffset? timestamp = null)
    {
        if (string.IsNullOrWhiteSpace(drawingPath))
        {
            throw new ArgumentException("Drawing path is required.", nameof(drawingPath));
        }

        var stamp = (timestamp ?? DateTimeOffset.Now).ToString("yyyyMMdd-HHmmss-fffffff");
        var fileName = Path.GetFileNameWithoutExtension(drawingPath);
        var extension = Path.GetExtension(drawingPath);
        var safeName = string.Join("_", fileName.Split(Path.GetInvalidFileNameChars(), StringSplitOptions.RemoveEmptyEntries));
        return Path.Combine(_backupRoot, $"{safeName}.{stamp}{extension}");
    }

    public string? TryBackup(string? drawingPath)
    {
        if (string.IsNullOrWhiteSpace(drawingPath) || !File.Exists(drawingPath))
        {
            return null;
        }

        Directory.CreateDirectory(_backupRoot);
        for (var attempt = 0; attempt < 5; attempt++)
        {
            var backupPath = PlanBackupPath(drawingPath);
            if (attempt > 0)
            {
                backupPath = Path.Combine(
                    Path.GetDirectoryName(backupPath) ?? _backupRoot,
                    $"{Path.GetFileNameWithoutExtension(backupPath)}-{attempt}{Path.GetExtension(backupPath)}");
            }

            try
            {
                File.Copy(drawingPath, backupPath, overwrite: false);
                return backupPath;
            }
            catch (IOException) when (File.Exists(backupPath))
            {
                Thread.Sleep(10);
            }
        }

        throw new IOException($"Could not create a unique backup for {drawingPath}.");
    }
}
