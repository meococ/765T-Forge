using System.Text.Json;
using System.Text.Json.Serialization;
using Forge.Shared;

namespace Forge.Server;

public sealed class McpWireError
{
    public string? Code { get; init; }
    public string? Message { get; init; }
    public string? Suggestion { get; init; }
}

public class McpEnvelope<TData>
{
    [JsonRequired]
    public string Id { get; init; } = "";

    [JsonRequired]
    public bool Ok { get; init; }

    public McpWireError? Error { get; init; }
    public string? AuditId { get; init; }
    public TData? Data { get; init; }
}

public sealed class ExecEnvelope<TData> : McpEnvelope<TData>
{
    [JsonRequired]
    public bool Queued { get; init; }

    [JsonRequired]
    public bool Completed { get; init; }
}

public sealed class QaEnvelope<TData> : McpEnvelope<TData>
{
    [JsonRequired]
    public bool Passed { get; init; }
}

public sealed class PublishEnvelope<TData> : McpEnvelope<TData>
{
    [JsonRequired]
    public bool VerificationPassed { get; init; }
}

public class McpData
{
    public bool? DryRun { get; init; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Extra { get; init; }
}

public sealed class HealthData : McpData
{
    public string? Status { get; init; }
    public string? Pipe { get; init; }
    public string? Server { get; init; }
    public string? ActiveDocument { get; init; }
}

public sealed class VersionData : McpData
{
    public string? Forge { get; init; }
    public string? Envelope { get; init; }
    public string? HostVersion { get; init; }
    public string? BuiltForYear { get; init; }
    public string? ConfiguredAutoCadRoot { get; init; }
    public bool? HostMismatch { get; init; }
    public string? AutocadApplication { get; init; }
    public string? Dotnet { get; init; }
}

public sealed class GetVarData : McpData
{
    public string? Name { get; init; }
    public JsonElement? Value { get; init; }
}

public sealed class SetVarData : McpData
{
    public string? Name { get; init; }
    public JsonElement? Before { get; init; }
    public JsonElement? After { get; init; }
}

public sealed class CapabilitiesData : McpData
{
    public JsonElement? Devices { get; init; }
    public JsonElement? Media { get; init; }
    public JsonElement? PageSetups { get; init; }
    public JsonElement? Layouts { get; init; }
    public JsonElement? LayerStates { get; init; }
    public JsonElement? PlotStyles { get; init; }
}

public sealed class OpenDocumentsData : McpData
{
    public JsonElement? Documents { get; init; }
}

public sealed class LayoutListData : McpData
{
    public JsonElement? Layouts { get; init; }
}

public sealed class DocOpenData : McpData
{
    public string? Opened { get; init; }
}

public sealed class DocSaveData : McpData
{
    public string? Saved { get; init; }
}

public sealed class XrefListData : McpData
{
    public JsonElement? Xrefs { get; init; }
}

public sealed class XrefReloadData : McpData
{
    public string? Name { get; init; }
}

public sealed class XrefRepathData : McpData
{
    public string? Name { get; init; }
    public string? Path { get; init; }
}

public sealed class XrefNormalizeData : McpData
{
    public JsonElement? Changes { get; init; }
}

public sealed class LayerListData : McpData
{
    public JsonElement? Layers { get; init; }
}

public sealed class LayerStateListData : McpData
{
    public JsonElement? States { get; init; }
}

public sealed class LayerStateRestoreData : McpData
{
    public string? Restored { get; init; }
}

public sealed class BlockListData : McpData
{
    public JsonElement? Attributes { get; init; }
}

public sealed class BlockGetData : McpData
{
    public string? Tag { get; init; }
    public string? Value { get; init; }
}

public sealed class BlockSetData : McpData
{
    public int? Updated { get; init; }
}

public sealed class CampaignData : McpData
{
    public int? Updated { get; init; }
    public JsonElement? Diffs { get; init; }
}

public sealed class PageSetupImportData : McpData
{
    public string? Mode { get; init; }
    public bool? Queued { get; init; }
    public bool? Completed { get; init; }
}

public sealed class PageSetupApplyData : McpData
{
    public string? Applied { get; init; }
    public string? Layout { get; init; }
}

public sealed class PlotToPdfData : McpData
{
    public string? OutputPath { get; init; }
    public string? Layout { get; init; }
    public string? Device { get; init; }
    public string? PaperSize { get; init; }
    public bool? PreflightRan { get; init; }
}

public sealed class PlotPublishData : McpData
{
    public string? OutputPath { get; init; }
    public bool? SinglePdf { get; init; }
    public string? SheetType { get; init; }
    public int? DsdType { get; init; }
    public bool? VerificationPassed { get; init; }
    public JsonElement? Receipt { get; init; }
}

public class QaCheckData : McpData
{
    public bool? Passed { get; init; }
    public JsonElement? Diffs { get; init; }
    public JsonElement? Report { get; init; }
}

public sealed class TitleblockQaData : QaCheckData
{
}

public sealed class XrefQaData : QaCheckData
{
}

public sealed class LayerAuditData : QaCheckData
{
}

public sealed class PreflightQaData : QaCheckData
{
}

public class ReadbackData : McpData
{
    public string? TargetTool { get; init; }
    public bool? Passed { get; init; }
}

public sealed class ReadbackAfterTimeoutData : ReadbackData
{
}

public sealed class AuditSummaryData : McpData
{
    public int? Count { get; init; }
    public string? Note { get; init; }
}

public sealed class IssueSetData : McpData
{
    public bool? Passed { get; init; }
}

public sealed class SheetImportData : McpData
{
    public string? ContractId { get; init; }
    public int? SheetCount { get; init; }
    public string? OutputContractPath { get; init; }
}

public sealed class IssueDiffData : McpData
{
    public JsonElement? AddedLayouts { get; init; }
}

public sealed class RecipeData : McpData
{
    public bool? DrawingMutated { get; init; }
    public JsonElement? Steps { get; init; }
    public bool? PreflightBypassed { get; init; }
}

public sealed class PackData : McpData
{
    public string? OutputDirectory { get; init; }
}

public sealed class RegistryLoadData : McpData
{
    public string? ProjectId { get; init; }
}

public sealed class RegistryLookupData : McpData
{
    public string? DrawingNo { get; init; }
    public JsonElement? Sheets { get; init; }
}

public sealed class PackLoadData : McpData
{
    public string? PackId { get; init; }
}

public sealed class PackStatusData : McpData
{
    public string? PackId { get; init; }
    public string? ProjectId { get; init; }
}

public sealed class ToolProfileData : McpData
{
    public string? Name { get; init; }
    public JsonElement? Tools { get; init; }
    public JsonElement? Profiles { get; init; }
}

public sealed class ViewportListData : McpData
{
    public JsonElement? Viewports { get; init; }
}

public sealed class ViewportFreezeData : McpData
{
    public string? Handle { get; init; }
    public string? Layer { get; init; }
}

public sealed class BatchRunData : McpData
{
    public string? BatchId { get; init; }
    public int? JobCount { get; init; }
    public int? Failures { get; init; }
}

public sealed class BatchStatusData : McpData
{
    public string? BatchId { get; init; }
}

public sealed class ExecCommandData : McpData
{
    public string? Command { get; init; }
    public string? Mode { get; init; }
    public bool? Queued { get; init; }
    public bool? Completed { get; init; }
}

public sealed class ExecLispData : McpData
{
    public string? Mode { get; init; }
    public bool? Queued { get; init; }
    public bool? Completed { get; init; }
}

public sealed class RunScriptData : McpData
{
    public int? ExitCode { get; init; }
    public string? Stdout { get; init; }
    public string? Stderr { get; init; }
    public string? Sha256 { get; init; }
}

public sealed class ExecDotNetData : McpData
{
    public string? Mode { get; init; }
    public bool? Queued { get; init; }
    public bool? Completed { get; init; }
}

public sealed class LineworkDumpData : McpData
{
}

public sealed class LineworkTraceData : McpData
{
}

public sealed class LineworkTopologyData : McpData
{
}

public sealed class LineworkCoverageData : McpData
{
}

public sealed class LineworkSegmentsData : McpData
{
}

public sealed class LineworkCompareData : McpData
{
}

public sealed class LineworkTransformData : McpData
{
}

public static class ForgeOutputMap
{
    public static McpEnvelope<TData> Envelope<TData>(ForgeResult result) where TData : class
        => new()
        {
            Id = result.Id,
            Ok = result.Ok,
            Error = Error(result),
            AuditId = result.AuditId,
            Data = Data<TData>(result)
        };

    public static ExecEnvelope<TData> Exec<TData>(ForgeResult result) where TData : class
    {
        var element = AsElement(result.Data);
        var queued = ReadBool(element, "queued", false);
        return new ExecEnvelope<TData>
        {
            Id = result.Id,
            Ok = result.Ok,
            Queued = queued,
            Completed = ReadBool(element, "completed", result.Ok && !queued),
            Error = Error(result),
            AuditId = result.AuditId,
            Data = Data<TData>(result)
        };
    }

    public static QaEnvelope<TData> Qa<TData>(ForgeResult result) where TData : class
    {
        var element = AsElement(result.Data);
        var passed = ReadBool(element, "passed", result.Verification?.Passed ?? result.Ok);
        return new QaEnvelope<TData>
        {
            Id = result.Id,
            Ok = result.Ok,
            Passed = passed,
            Error = Error(result),
            AuditId = result.AuditId,
            Data = Data<TData>(result)
        };
    }

    public static PublishEnvelope<TData> Publish<TData>(ForgeResult result) where TData : class
    {
        var element = AsElement(result.Data);
        return new PublishEnvelope<TData>
        {
            Id = result.Id,
            Ok = result.Ok,
            VerificationPassed = ReadVerificationPassed(element, result),
            Error = Error(result),
            AuditId = result.AuditId,
            Data = Data<TData>(result)
        };
    }

    private static bool ReadVerificationPassed(JsonElement element, ForgeResult result)
    {
        if (ReadOptionalBool(element, "verificationPassed") is { } direct)
        {
            return direct;
        }

        if (element.ValueKind == JsonValueKind.Object
            && TryProperty(element, "receipt", out var receipt)
            && ReadOptionalBool(receipt, "verificationPassed") is { } fromReceipt)
        {
            return fromReceipt;
        }

        return result.Verification?.Passed ?? false;
    }

    private static TData? Data<TData>(ForgeResult result) where TData : class
    {
        var element = AsElement(result.Data);
        if (element.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null)
        {
            return null;
        }

        return element.Deserialize<TData>(ForgeJson.Options);
    }

    private static McpWireError? Error(ForgeResult result)
    {
        if (result.Error is null)
        {
            return null;
        }

        return new McpWireError
        {
            Code = result.Error.Code,
            Message = result.Error.Message,
            Suggestion = result.Error.Suggestion
        };
    }

    private static JsonElement AsElement(object? data)
    {
        if (data is null)
        {
            return default;
        }

        return data is JsonElement element ? element : ForgeJson.ToElement(data);
    }

    private static bool ReadBool(JsonElement element, string name, bool fallback)
        => ReadOptionalBool(element, name) ?? fallback;

    private static bool? ReadOptionalBool(JsonElement element, string name)
    {
        if (element.ValueKind != JsonValueKind.Object || !TryProperty(element, name, out var value))
        {
            return null;
        }

        return value.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => null
        };
    }

    private static bool TryProperty(JsonElement element, string camel, out JsonElement value)
    {
        if (element.TryGetProperty(camel, out value))
        {
            return true;
        }

        var pascal = char.ToUpperInvariant(camel[0]) + camel[1..];
        return element.TryGetProperty(pascal, out value);
    }
}
