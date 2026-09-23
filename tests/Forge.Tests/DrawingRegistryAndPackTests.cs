using Forge.Shared;

namespace Forge.Tests;

public sealed class DrawingRegistryAndPackTests
{
    [Fact]
    public void RegistryRejectsUnknownDrawingNumber()
    {
        var path = WriteTempJson("""
            {
              "projectId": "DEMO",
              "sheets": [
                { "drawingNo": "MTR-DEMO-A101", "layout": "A101", "rev": "A" }
              ]
            }
            """);

        try
        {
            var registry = DrawingRegistry.LoadFromFile(path);
            Assert.True(registry.TryAuthorizeAttribute("TITLE", "anything", out _, out _));
            Assert.False(registry.TryAuthorizeAttribute("DWG_NO", "INVENTED", out var code, out _));
            Assert.Equal("deny_unknown_drawing_no", code);
            Assert.NotNull(registry.FindByDrawingNo("MTR-DEMO-A101"));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void PackEvaluateLayersAndDrawingNoRegex()
    {
        var path = WriteTempJson("""
            {
              "packId": "demo-pack",
              "layers": ["A-ANNO-TTLB"],
              "forbiddenLayers": ["0-TEMP"],
              "drawingNoRegex": "^MTR-",
              "requiredTitleblockTags": ["DWG_NO"]
            }
            """);

        try
        {
            var pack = StandardsPack.LoadFromFile(path);
            var findings = pack.EvaluateLayers(["0", "0-TEMP"]);
            Assert.Contains(findings, f => f.Code == "pack_layer_missing");
            Assert.Contains(findings, f => f.Code == "pack_layer_forbidden");
            Assert.False(pack.TryValidateDrawingNo("BAD", out var code, out _));
            Assert.Equal("deny_drawing_no_format", code);
            Assert.True(pack.TryValidateDrawingNo("MTR-1", out _, out _));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void ToolProfilesExposeCorePlotQa()
    {
        Assert.True(ToolProfiles.TryGet("plot", out var tools));
        Assert.Contains("forge_qa_preflight", tools);
        Assert.Contains("core", ToolProfiles.Names);
    }

    private static string WriteTempJson(string json)
    {
        var path = Path.Combine(Path.GetTempPath(), $"forge-fix-{Guid.NewGuid():N}.json");
        File.WriteAllText(path, json);
        return path;
    }
}
