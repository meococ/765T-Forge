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

    public const string ListDocument =
        "Document for this list call only. The active drawing is restored afterward. Fill, preflight, and publish run on the MDI active document unless those tools receive document.";

    public const string TargetDocument =
        "Open drawing for this call only. When omitted, the tool runs on the MDI active document. The previous active drawing is restored after the call.";

    public const string Overwrite =
        "Set true only after a human agrees to overwrite an existing file. Do not set it on your own to get past the acknowledgement error.";

    public const string PlotLayout =
        "Layout name copied from forge_doc_list_layouts. Required. This tool does not run preflight and does not default a blank layout to Model.";

    public const string PlotDevice =
        "Plot device copied from forge_system_capabilities. Required. A blank value is plot_args_required, not a silent DWG To PDF.pc3 default.";

    public const string PlotPaper =
        "Paper size copied from forge_system_capabilities. Required. A blank value is plot_args_required, not a silent ISO A1 default.";

    public const string PlotStyle =
        "CTB or STB name from forge_system_capabilities. Optional. This tool does not run preflight.";

    public const string SinglePdf =
        "true (default) is one multi-sheet PDF (DSD Type 6, sheetType SinglePdf). false is one PDF per layout (DSD Type 7, sheetType MultiPdf). Dry-run echoes this flag and does not swap the labels.";

    public const string Layouts =
        "Layout names from forge_doc_list_layouts of the drawing this call runs on. Unknown names return layout_not_found before publish.";

    public const string RequiredTags =
        "Title-block tags that every matching block must contain. An empty list with no loaded pack fails the gate (preflight_no_titleblock_requirements).";

    public const string TitleblockBlock =
        "Block name to check. Every reference is checked. One good attribute does not pass the other references.";

    public const string ExpectedLayers =
        "Layer names that must exist. A missing name is an error (layer_missing) and fails the gate. This does not check freeze, lock, or plot.";

    public const string SysvarName =
        "AutoCAD system variable name. SECURELOAD, TRUSTEDPATHS, and the other security variables are denied by the typed tool (deny_sysvar), not by the command denylist.";

    public const string UnsafeAck =
        "Per-call acknowledgement. Also requires FORGE_ENABLE_UNSAFE_OPS=true on the server and the plugin. There is no enable_unsafe_ops config key.";

    public const string LwSource =
        "drawing reads the live model space, including xref contents. dumpFile reads dumpPath on the server and does not need AutoCAD.";

    public const string LwDumpPath =
        "Path of a pl_dump.txt file. Required when source is dumpFile.";

    public const string LwLayerFilter =
        "Layer patterns. A pattern matches the full name and the bare name after the last $ or |, so xref-prefixed layers still match.";

    public const string LwLayerSuffix =
        "When set, the bare layer name must end with this text.";

    public const string LwLayerMatch =
        "How a plain layerFilter is applied: auto, exact, suffix, prefix, or substring. prefix is the old startswith and drops xref content.";

    public const string LwExcludeLayer =
        "Layer patterns to drop after layerFilter, using the same xref-aware match.";

    public const string LwEntityTypes =
        "Entity types to keep, such as line, polyline, arc, or circle. Omit to use the tool default.";

    public const string LwBbox =
        "Optional extents filter as minX, minY, maxX, maxY in drawing units.";

    public const string LwIncludeXref =
        "When true, model-space xref contents are transformed into the host drawing and included.";

    public const string LwUnitsPerMeter =
        "Drawing units in one meter. 0 lets the tool choose. 1000 means millimeters.";

    public const string LwMaxEntities =
        "Maximum entities to read. The default is 50000.";

    public const string LwOutputPath =
        "Optional path for a JSON copy of the result. The tool stays read-only toward the drawing.";

    public const string LwModelSegments =
        "Modeled pipe centerlines in meters. Each item is {x1,y1,x2,y2} or {s:[x,y],e:[x,y]}.";

    public const string LwModelSegmentsPath =
        "JSON file of modeled pipe centerlines. Used when modelSegments is omitted.";
}
