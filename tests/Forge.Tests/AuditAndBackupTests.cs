using System.Globalization;
using System.Text.Json;
using Forge.Shared;

namespace Forge.Tests;

public sealed class AuditAndBackupTests
{
    [Fact]
    public void BackupPlannerCreatesTimestampedDwgSiblingName()
    {
        var planner = new BackupPlanner(Path.Combine("C:", "forge-backups"));

        var path = planner.PlanBackupPath(@"D:\Metro\A101.dwg", new DateTimeOffset(2026, 7, 8, 10, 20, 30, TimeSpan.Zero));

        Assert.EndsWith(".dwg", path);
        Assert.Contains("A101.20260708-102030-", path);
    }

    [Fact]
    public async Task AuditSinkWritesJsonLinesWithoutToken()
    {
        var dir = Path.Combine(Path.GetTempPath(), "765T-Forge-Tests", Guid.NewGuid().ToString("N"));
        var sink = new FileAuditSink(dir);
        var command = new ForgeCommand
        {
            Tool = "forge_exec_command",
            Args = ForgeJson.ToElement(new { command = "LINE 0,0 1,1" }),
            AuthToken = "secret"
        };

        await sink.WriteAsync(new AuditRecord
        {
            Source = "test",
            Tool = command.Tool,
            CommandId = command.Id,
            Allowed = true,
            Args = command.Args
        });

        var jsonl = Directory.GetFiles(dir, "*.jsonl").Single();
        var text = await File.ReadAllTextAsync(jsonl);

        Assert.Contains("forge_exec_command", text);
        Assert.DoesNotContain("secret", text);
        using var _ = JsonDocument.Parse(text.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries).Single());
    }

    [Fact]
    public void RepeatedBackupPathsWithinSameSecondCanBeDisambiguated()
    {
        var planner = new BackupPlanner(Path.Combine("C:", "forge-backups"));
        var stamp = new DateTimeOffset(2026, 7, 8, 10, 20, 30, TimeSpan.Zero);

        var first = planner.PlanBackupPath(@"D:\Metro\A101.dwg", stamp);
        var second = planner.PlanBackupPath(@"D:\Metro\A101.dwg", stamp.AddTicks(1));

        Assert.NotEqual(first, second);
    }

    [Fact]
    public void BackupPlannerUsesGregorianDigitsUnderNonGregorianCulture()
    {
        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("th-TH");
            var planner = new BackupPlanner(Path.Combine("C:", "forge-backups"));

            var path = planner.PlanBackupPath(@"D:\Metro\A101.dwg", new DateTimeOffset(2026, 7, 8, 10, 20, 30, TimeSpan.Zero));

            Assert.Contains("A101.20260708-102030-", path);
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    [Fact]
    public async Task AuditSinkRedactsExactlyListedSensitiveArgumentNames()
    {
        var dir = Path.Combine(Path.GetTempPath(), "765T-Forge-Tests", Guid.NewGuid().ToString("N"));
        var sink = new FileAuditSink(dir);
        await sink.WriteAsync(new AuditRecord
        {
            Source = "test",
            Tool = "forge_transmittal_seal",
            CommandId = "c1",
            Allowed = true,
            Args = ForgeJson.ToElement(new
            {
                hmacKey = "top-secret-hmac",
                token = "top-secret-token",
                password = "top-secret-password",
                nested = new { apiKey = "top-secret-api", keep = "monkey-value" },
                values = new[] { new { secret = "top-secret-array" } }
            })
        });

        var jsonl = Directory.GetFiles(dir, "*.jsonl").Single();
        var text = await File.ReadAllTextAsync(jsonl);

        Assert.DoesNotContain("top-secret-hmac", text);
        Assert.DoesNotContain("top-secret-token", text);
        Assert.DoesNotContain("top-secret-password", text);
        Assert.DoesNotContain("top-secret-api", text);
        Assert.DoesNotContain("top-secret-array", text);
        Assert.Contains("monkey-value", text);
        Assert.Contains("[redacted]", text);
        using var _ = JsonDocument.Parse(text.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries).Single());
    }

    [Theory]
    [InlineData("")]
    [InlineData("../escape")]
    [InlineData("with space")]
    [InlineData("under_score")]
    [InlineData("a/b")]
    public void BatchResumeStateRejectsUnsafeBatchIds(string batchId)
    {
        var state = new BatchResumeState { BatchId = batchId };
        var dir = Path.Combine(Path.GetTempPath(), "765T-Forge-Tests", Guid.NewGuid().ToString("N"));

        Assert.Throws<ArgumentException>(() => state.Save(dir));
        Assert.Throws<ArgumentException>(() => BatchResumeState.Load(batchId));
    }

    [Fact]
    public void BatchResumeStateAcceptsValidatedBatchIdAndWritesAtomically()
    {
        var dir = Path.Combine(Path.GetTempPath(), "765T-Forge-Tests", Guid.NewGuid().ToString("N"));
        try
        {
            var state = new BatchResumeState { BatchId = "batch-abc-123" };
            state.Save(dir);

            Assert.True(File.Exists(state.ArtifactPath));
            Assert.False(File.Exists(state.ArtifactPath + ".tmp"));
            Assert.NotNull(BatchResumeState.LoadFromPath(state.ArtifactPath!));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void BatchResumeStateLoadFromPathRequiresRootedPath()
    {
        Assert.Throws<ArgumentException>(() => BatchResumeState.LoadFromPath("not-rooted.json"));
    }
}
