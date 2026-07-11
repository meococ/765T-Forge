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
}
