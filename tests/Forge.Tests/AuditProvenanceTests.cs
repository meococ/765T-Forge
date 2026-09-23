using System.Globalization;
using Forge.Shared;

namespace Forge.Tests;

/// <summary>
/// The audit file name carries the writer's provenance ({source}-{yyyyMMdd}.jsonl) so the server
/// and the plugin can never interleave partial lines in the same file.
/// </summary>
public sealed class AuditProvenanceTests
{
    [Fact]
    public async Task AuditFileNameIncludesSourceAndDate()
    {
        var dir = Path.Combine(Path.GetTempPath(), "765T-Forge-Tests", Guid.NewGuid().ToString("N"));
        try
        {
            var sink = new FileAuditSink(dir);
            await sink.WriteAsync(new AuditRecord
            {
                Source = "plugin",
                Tool = "forge_system_health",
                Args = ForgeJson.ToElement(new { })
            });

            var expected = $"{DateTimeOffset.UtcNow.ToString("yyyyMMdd", CultureInfo.InvariantCulture)}.jsonl";
            var files = Directory.GetFiles(dir, "*.jsonl");
            var file = Assert.Single(files);
            Assert.Equal($"plugin-{expected}", Path.GetFileName(file));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public async Task ServerAndPluginWriteSeparateFiles()
    {
        var dir = Path.Combine(Path.GetTempPath(), "765T-Forge-Tests", Guid.NewGuid().ToString("N"));
        try
        {
            var serverSink = new FileAuditSink(dir);
            var pluginSink = new FileAuditSink(dir);
            await serverSink.WriteAsync(new AuditRecord
            {
                Source = "server",
                Tool = "forge_plot_publish",
                Args = ForgeJson.ToElement(new { })
            });
            await pluginSink.WriteAsync(new AuditRecord
            {
                Source = "plugin",
                Tool = "forge_plot_publish",
                Args = ForgeJson.ToElement(new { })
            });

            var names = Directory.GetFiles(dir, "*.jsonl").Select(Path.GetFileName).OrderBy(x => x).ToArray();
            Assert.Equal(2, names.Length);
            Assert.StartsWith("plugin-", names[0], StringComparison.Ordinal);
            Assert.StartsWith("server-", names[1], StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void BlankSourceIsReportedAsAFailureInsteadOfBeingDefaulted()
    {
        var dir = Path.Combine(Path.GetTempPath(), "765T-Forge-Tests", Guid.NewGuid().ToString("N"));
        try
        {
            var sink = new FileAuditSink(dir);
            var auditId = sink.WriteBestEffort(new AuditRecord
            {
                Source = "",
                Tool = "forge_system_health",
                Args = ForgeJson.ToElement(new { })
            });

            Assert.False(string.IsNullOrWhiteSpace(auditId));
            Assert.Empty(Directory.GetFiles(dir, "*.jsonl"));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void AuditReadersEnumerateEveryJsonlFileNotASingleName()
    {
        // Guard: both readers must glob *.jsonl so a source-prefixed file name is discovered.
        var auditRecordSource = File.ReadAllText(FindRepoFile("src", "Forge.Shared", "AuditRecord.cs"));
        Assert.Contains("$\"{source}-{", auditRecordSource, StringComparison.Ordinal);

        var toolRunner = File.ReadAllText(FindRepoFile("src", "Forge.Server", "ForgeToolRunner.cs"));
        Assert.Contains("GetFiles(dir, \"*.jsonl\")", toolRunner, StringComparison.Ordinal);

        var ceremony = File.ReadAllText(FindRepoFile("src", "Forge.Shared", "CeremonyEvidence.cs"));
        Assert.Contains("GetFiles(auditDirectory, \"*.jsonl\")", ceremony, StringComparison.Ordinal);
    }

    private static string FindRepoFile(params string[] parts)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(new[] { dir.FullName }.Concat(parts).ToArray());
            if (File.Exists(candidate))
            {
                return candidate;
            }

            dir = dir.Parent;
        }

        throw new FileNotFoundException("Could not locate repository file: " + string.Join("/", parts));
    }
}
