namespace Forge.Server;

public static class ForgeArgDescriptions
{
    public const string DryRun =
        "Plan only. Ok=true with data.dryRun=true means the drawing was not changed. Call again with dryRun=false to perform the work.";

    public const string Force =
        "Honor only when a human explicitly asks in this session. A failed preflight still returns Ok=false with code preflight_forced and data.preflightBypassed=true.";

    public const string BlockHandle =
        "Block reference handle. Required unless blockName is set. Omitting both is rejected (ambiguous_block_target) and nothing is written.";

    public const string BlockName =
        "Block name used only when handle is omitted. Omitting both handle and blockName is rejected (ambiguous_block_target).";

    public const string ContractId =
        "Stable contract id. When omitted, the id is a timestamp, so this tool is not idempotent.";
}
