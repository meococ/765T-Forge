using System.ComponentModel;
using Forge.Shared;
using ModelContextProtocol.Server;

namespace Forge.Server;

[McpServerToolType]
public sealed class ForgeMcpTools
{
    // Không gắn UseStructuredContent/OutputSchemaType=ForgeResult: object? Data (và ReadBack)
    // sinh JSON Schema "data": true — Cursor/Zod reject hết tool ở listOfferingsForUI.
    [McpServerTool(Name = "forge_system_health", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("Check whether 765T-Forge server is alive and whether the AutoCAD plugin named pipe responds.")]
    public static Task<ForgeResult> SystemHealth(ForgeToolRunner runner, CancellationToken cancellationToken)
        => runner.InvokeAsync("forge_system_health", new { }, cancellationToken: cancellationToken);

    [McpServerTool(Name = "forge_system_version", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("Return 765T-Forge product/envelope versions plus AutoCAD and .NET information.")]
    public static Task<ForgeResult> SystemVersion(ForgeToolRunner runner, CancellationToken cancellationToken)
        => runner.InvokeAsync("forge_system_version", new { }, cancellationToken: cancellationToken);

    [McpServerTool(Name = "forge_system_getvar", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("Read one AutoCAD system variable, such as FILEDIA, PSTYLEMODE, BACKGROUNDPLOT, or CMDDIA.")]
    public static Task<ForgeResult> SystemGetVar(ForgeToolRunner runner, [Description("AutoCAD system variable name.")] string name, CancellationToken cancellationToken)
        => runner.InvokeAsync("forge_system_getvar", new { name }, cancellationToken: cancellationToken);

    [McpServerTool(Name = "forge_system_setvar", ReadOnly = false, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("Set one AutoCAD system variable with backup/audit/read-back around the write.")]
    public static Task<ForgeResult> SystemSetVar(ForgeToolRunner runner, string name, string value, bool dryRun = false, CancellationToken cancellationToken = default)
        => runner.InvokeAsync("forge_system_setvar", new { name, value }, dryRun, cancellationToken: cancellationToken);

    [McpServerTool(Name = "forge_system_capabilities", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("Discover plot devices, media names, page setups, layouts, layer states, and plot styles available in the active session.")]
    public static Task<ForgeResult> SystemCapabilities(ForgeToolRunner runner, CancellationToken cancellationToken = default)
        => runner.InvokeAsync("forge_system_capabilities", new { }, cancellationToken: cancellationToken);

    [McpServerTool(Name = "forge_doc_list_open", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("List open AutoCAD documents.")]
    public static Task<ForgeResult> DocListOpen(ForgeToolRunner runner, CancellationToken cancellationToken)
        => runner.InvokeAsync("forge_doc_list_open", new { }, cancellationToken: cancellationToken);

    [McpServerTool(Name = "forge_doc_activate", ReadOnly = false, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("Make an already-open drawing the active document, by exact file name (name) or full path (path), then read back which document AutoCAD reports as active. Runs in application context (works from the Start tab). Exact match only: more than one match fails as ambiguous_document; a drawing that is not open fails as document_not_found (use forge_doc_open). Fails with doc_activate_unconfirmed if AutoCAD does not report the switch.")]
    public static Task<ForgeResult> DocActivate(ForgeToolRunner runner, string? name = null, string? path = null, bool dryRun = false, CancellationToken cancellationToken = default)
        => runner.InvokeAsync("forge_doc_activate", new { name, path }, dryRun, cancellationToken: cancellationToken);

    [McpServerTool(Name = "forge_doc_list_layouts", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("List layouts in the active or specified AutoCAD document.")]
    public static Task<ForgeResult> DocListLayouts(ForgeToolRunner runner, string? document = null, CancellationToken cancellationToken = default)
        => runner.InvokeAsync("forge_doc_list_layouts", new { }, document: document, cancellationToken: cancellationToken);

    [McpServerTool(Name = "forge_doc_open", ReadOnly = false, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("Open a DWG in AutoCAD with dialog suppression.")]
    public static Task<ForgeResult> DocOpen(ForgeToolRunner runner, string path, bool dryRun = false, CancellationToken cancellationToken = default)
        => runner.InvokeAsync("forge_doc_open", new { path }, dryRun, cancellationToken: cancellationToken);

    [McpServerTool(Name = "forge_doc_save", ReadOnly = false, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("Save the active document or save as a specified path with audit and backup.")]
    public static Task<ForgeResult> DocSave(ForgeToolRunner runner, string? path = null, bool overwriteAcknowledged = false, bool dryRun = false, CancellationToken cancellationToken = default)
        => runner.InvokeAsync("forge_doc_save", new { path, overwriteAcknowledged }, dryRun, cancellationToken: cancellationToken);

    [McpServerTool(Name = "forge_xref_list", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("List xrefs with names, paths, load state, and unresolved path hints.")]
    public static Task<ForgeResult> XrefList(ForgeToolRunner runner, CancellationToken cancellationToken)
        => runner.InvokeAsync("forge_xref_list", new { }, cancellationToken: cancellationToken);

    [McpServerTool(Name = "forge_xref_reload", ReadOnly = false, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("Reload one xref by name, or all xrefs when name is omitted.")]
    public static Task<ForgeResult> XrefReload(ForgeToolRunner runner, string? name = null, bool dryRun = false, CancellationToken cancellationToken = default)
        => runner.InvokeAsync("forge_xref_reload", new { name }, dryRun, cancellationToken: cancellationToken);

    [McpServerTool(Name = "forge_xref_repath", ReadOnly = false, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("Set an xref path, preserving audit/read-back so missing metro xrefs can be fixed before publish.")]
    public static Task<ForgeResult> XrefRepath(ForgeToolRunner runner, string name, string path, bool reload = true, bool dryRun = false, CancellationToken cancellationToken = default)
        => runner.InvokeAsync("forge_xref_repath", new { name, path, reload }, dryRun, cancellationToken: cancellationToken);

    [McpServerTool(Name = "forge_xref_normalize_relative", ReadOnly = false, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("Rewrite absolute xref paths to host-relative paths and optionally reload.")]
    public static Task<ForgeResult> XrefNormalizeRelative(ForgeToolRunner runner, string? name = null, bool reload = true, bool dryRun = false, CancellationToken cancellationToken = default)
        => runner.InvokeAsync("forge_xref_normalize_relative", new { name, reload }, dryRun, cancellationToken: cancellationToken);

    [McpServerTool(Name = "forge_layer_list", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("List layer properties relevant to plotting and locked/frozen states.")]
    public static Task<ForgeResult> LayerList(ForgeToolRunner runner, CancellationToken cancellationToken)
        => runner.InvokeAsync("forge_layer_list", new { }, cancellationToken: cancellationToken);

    [McpServerTool(Name = "forge_layer_state_list", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("List known layer states.")]
    public static Task<ForgeResult> LayerStateList(ForgeToolRunner runner, CancellationToken cancellationToken)
        => runner.InvokeAsync("forge_layer_state_list", new { }, cancellationToken: cancellationToken);

    [McpServerTool(Name = "forge_layer_state_restore", ReadOnly = false, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("Restore a named layer state synchronously before publishing.")]
    public static Task<ForgeResult> LayerStateRestore(ForgeToolRunner runner, string name, bool dryRun = false, CancellationToken cancellationToken = default)
        => runner.InvokeAsync("forge_layer_state_restore", new { name }, dryRun, cancellationToken: cancellationToken);

    [McpServerTool(Name = "forge_block_list_attributes", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("List attributes on block references, optionally filtered by block name or handle.")]
    public static Task<ForgeResult> BlockListAttributes(ForgeToolRunner runner, string? blockName = null, string? handle = null, CancellationToken cancellationToken = default)
        => runner.InvokeAsync("forge_block_list_attributes", new { blockName, handle }, cancellationToken: cancellationToken);

    [McpServerTool(Name = "forge_block_get_attr", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("Read a single block attribute from a titleblock or other attributed block.")]
    public static Task<ForgeResult> BlockGetAttr(ForgeToolRunner runner, string tag, string? blockName = null, string? handle = null, CancellationToken cancellationToken = default)
        => runner.InvokeAsync("forge_block_get_attr", new { tag, blockName, handle }, cancellationToken: cancellationToken);

    [McpServerTool(Name = "forge_block_set_attr", ReadOnly = false, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("Set a block attribute and read it back for titleblock verification.")]
    public static Task<ForgeResult> BlockSetAttr(ForgeToolRunner runner, string tag, string value, string? blockName = null, string? handle = null, bool dryRun = false, CancellationToken cancellationToken = default)
        => runner.InvokeAsync("forge_block_set_attr", new { tag, value, blockName, handle }, dryRun, cancellationToken: cancellationToken);

    [McpServerTool(Name = "forge_block_campaign", ReadOnly = false, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("Apply a titleblock attribute campaign (multi layout/tag) with dry-run diffs. Supports Unicode values.")]
    public static Task<ForgeResult> BlockCampaign(ForgeToolRunner runner, CampaignEntryDto[] entries, bool dryRun = false, CancellationToken cancellationToken = default)
        => runner.InvokeAsync("forge_block_campaign", new { entries }, dryRun, cancellationToken: cancellationToken);

    [McpServerTool(Name = "forge_layout_page_setup_import", ReadOnly = false, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("Import page setup from a DWT/DWG template using AutoCAD command automation.")]
    public static Task<ForgeResult> PageSetupImport(ForgeToolRunner runner, string templatePath, string setupName, bool dryRun = false, CancellationToken cancellationToken = default)
        => runner.InvokeAsync("forge_layout_page_setup_import", new { templatePath, setupName }, dryRun, cancellationToken: cancellationToken);

    [McpServerTool(Name = "forge_layout_page_setup_apply", ReadOnly = false, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("Apply an existing page setup to a layout synchronously via PlotSettings API.")]
    public static Task<ForgeResult> PageSetupApply(ForgeToolRunner runner, string setupName, string? layout = null, bool dryRun = false, CancellationToken cancellationToken = default)
        => runner.InvokeAsync("forge_layout_page_setup_apply", new { setupName, layout }, dryRun, cancellationToken: cancellationToken);

    [McpServerTool(Name = "forge_plot_to_pdf", ReadOnly = false, Destructive = false, Idempotent = false, OpenWorld = false)]
    [Description("Plot one layout (or Model) to PDF through the PlotEngine API. Never opens a PDF viewer: the plot always goes through a Forge-owned copy of the device PC3 with View_New_File=FALSE (\"765T-Forge <device> (no viewer).pc3\"). Defaults: device \"DWG To PDF.pc3\"; a layout keeps its own paper and plots plotArea=layout; when paperSize differs from the layout's paper the area defaults to extents + fit + centered so the drawing fills the sheet; Model defaults to ISO A1 + extents + fit. plotArea: layout|extents|display|limits|window (window=[[x1,y1],[x2,y2]] in the plotted space). scale: fit or \"1:100\". orientation: auto (default, long side to long side)|landscape|portrait, honoured against the real paper. paperSize accepts the localized or canonical media name; an unknown one fails with candidates. Read-back: device actually used, viewer suppression, media, paper/printable mm, PDF sheet size from /MediaBox, fit + coverage estimates and warnings (e.g. content would be small in a corner). engine=\"command\" is the legacy -PLOT fallback.")]
    public static Task<ForgeResult> PlotToPdf(
        ForgeToolRunner runner,
        string outputPath,
        string? layout = null,
        string? device = null,
        string? paperSize = null,
        string? plotStyle = null,
        string? plotArea = null,
        double[][]? window = null,
        string? orientation = null,
        string? scale = null,
        string? units = null,
        bool? centered = null,
        string? engine = null,
        bool overwriteAcknowledged = false,
        bool dryRun = false,
        CancellationToken cancellationToken = default)
        => runner.InvokeAsync("forge_plot_to_pdf", new { outputPath, layout, device, paperSize, plotStyle, plotArea, window, orientation, scale, units, centered, engine, overwriteAcknowledged }, dryRun, cancellationToken: cancellationToken);

    [McpServerTool(Name = "forge_plot_publish", ReadOnly = false, Destructive = false, Idempotent = false, OpenWorld = false)]
    [Description("Publish layouts to PDF via real DSD + Publisher.PublishDsd; returns PublishReceipt with PDF probe. Preflight gate unless force=true.")]
    public static Task<ForgeResult> PlotPublish(
        ForgeToolRunner runner,
        string outputPath,
        string[] layouts,
        bool singlePdf = true,
        bool overwriteAcknowledged = false,
        bool requirePreflight = true,
        bool force = false,
        string[]? requiredTitleblockTags = null,
        string? titleblockBlockName = null,
        bool dryRun = false,
        CancellationToken cancellationToken = default)
        => runner.InvokeAsync("forge_plot_publish", new { outputPath, layouts, singlePdf, overwriteAcknowledged, requirePreflight, force, requiredTitleblockTags = requiredTitleblockTags ?? [], titleblockBlockName }, dryRun, cancellationToken: cancellationToken);

    [McpServerTool(Name = "forge_dgn_import", ReadOnly = false, Destructive = false, Idempotent = false, OpenWorld = false)]
    [Description("Convert a MicroStation DGN V8 drawing to a new DWG: -DGNIMPORT into a fresh temporary document, then SaveAs targetDwgPath. Source DGN and the active drawing are never modified. DGN V7 is refused by header sniff. dryRun returns the plan; a large DGN can exceed the default 120s plugin timeout (raise FORGE_PLUGIN_RESPONSE_TIMEOUT_SECONDS).")]
    public static Task<ForgeResult> DgnImport(
        ForgeToolRunner runner,
        string dgnPath,
        string targetDwgPath,
        string? modelName = null,
        string? units = null,
        bool overwriteAcknowledged = false,
        bool dryRun = false,
        CancellationToken cancellationToken = default)
        => runner.InvokeAsync("forge_dgn_import", new { dgnPath, targetDwgPath, modelName, units, overwriteAcknowledged }, dryRun, cancellationToken: cancellationToken);

    [McpServerTool(Name = "forge_draw_line", ReadOnly = false, Destructive = false, Idempotent = false, OpenWorld = false)]
    [Description("Draw a line from start to end (WCS, drawing units, [x,y] or [x,y,z]). Returns the new entity handle; read-back echoes geometry.")]
    public static Task<ForgeResult> DrawLine(ForgeToolRunner runner, double[] start, double[] end, string? layer = null, int? colorIndex = null, bool dryRun = false, CancellationToken cancellationToken = default)
        => runner.InvokeAsync("forge_draw_line", new { start, end, layer, colorIndex }, dryRun, cancellationToken: cancellationToken);

    [McpServerTool(Name = "forge_draw_polyline", ReadOnly = false, Destructive = false, Idempotent = false, OpenWorld = false)]
    [Description("Draw a lightweight polyline from vertices (WCS [x,y] list, ≥2). Optional closed, per-vertex bulges (tan(θ/4), positive CCW), constantWidth, layer, colorIndex. Returns the handle.")]
    public static Task<ForgeResult> DrawPolyline(ForgeToolRunner runner, double[][] vertices, bool closed = false, double[]? bulges = null, double? constantWidth = null, string? layer = null, int? colorIndex = null, bool dryRun = false, CancellationToken cancellationToken = default)
        => runner.InvokeAsync("forge_draw_polyline", new { vertices, closed, bulges, constantWidth, layer, colorIndex }, dryRun, cancellationToken: cancellationToken);

    [McpServerTool(Name = "forge_draw_circle", ReadOnly = false, Destructive = false, Idempotent = false, OpenWorld = false)]
    [Description("Draw a full circle (WCS center, radius in drawing units). Returns the new entity handle.")]
    public static Task<ForgeResult> DrawCircle(ForgeToolRunner runner, double[] center, double radius, string? layer = null, int? colorIndex = null, bool dryRun = false, CancellationToken cancellationToken = default)
        => runner.InvokeAsync("forge_draw_circle", new { center, radius, layer, colorIndex }, dryRun, cancellationToken: cancellationToken);

    [McpServerTool(Name = "forge_draw_arc", ReadOnly = false, Destructive = false, Idempotent = false, OpenWorld = false)]
    [Description("Draw a circular arc (WCS center, radius, startAngleDeg→endAngleDeg CCW, wraps through 0). Returns the new entity handle.")]
    public static Task<ForgeResult> DrawArc(ForgeToolRunner runner, double[] center, double radius, double startAngleDeg, double endAngleDeg, string? layer = null, int? colorIndex = null, bool dryRun = false, CancellationToken cancellationToken = default)
        => runner.InvokeAsync("forge_draw_arc", new { center, radius, startAngleDeg, endAngleDeg, layer, colorIndex }, dryRun, cancellationToken: cancellationToken);

    [McpServerTool(Name = "forge_draw_text", ReadOnly = false, Destructive = false, Idempotent = false, OpenWorld = false)]
    [Description("Draw a single-line DBText (WCS position, height, text — no newlines; rotationDeg CCW). Returns the new entity handle.")]
    public static Task<ForgeResult> DrawText(ForgeToolRunner runner, double[] position, double height, string text, double rotationDeg = 0, string? layer = null, int? colorIndex = null, bool dryRun = false, CancellationToken cancellationToken = default)
        => runner.InvokeAsync("forge_draw_text", new { position, height, text, rotationDeg, layer, colorIndex }, dryRun, cancellationToken: cancellationToken);

    [McpServerTool(Name = "forge_draw_mtext", ReadOnly = false, Destructive = false, Idempotent = false, OpenWorld = false)]
    [Description("Draw an MText paragraph (WCS position, height, text — newlines become \\P; width 0 = no wrap; attachmentPoint e.g. topLeft/middleCenter). Returns the handle.")]
    public static Task<ForgeResult> DrawMText(ForgeToolRunner runner, double[] position, double height, string text, double? width = null, string? attachmentPoint = null, double rotationDeg = 0, string? layer = null, int? colorIndex = null, bool dryRun = false, CancellationToken cancellationToken = default)
        => runner.InvokeAsync("forge_draw_mtext", new { position, height, text, width, attachmentPoint, rotationDeg, layer, colorIndex }, dryRun, cancellationToken: cancellationToken);

    [McpServerTool(Name = "forge_draw_hatch", ReadOnly = false, Destructive = false, Idempotent = false, OpenWorld = false)]
    [Description("Draw a hatch: boundary = an existing closed curve handle, or vertices that first draw a closed polyline (associative). pattern (default ANSI31), scale, angleDeg. Returns the hatch handle.")]
    public static Task<ForgeResult> DrawHatch(ForgeToolRunner runner, string? boundaryHandle = null, double[][]? boundaryVertices = null, string? pattern = null, double? scale = null, double angleDeg = 0, string? layer = null, bool dryRun = false, CancellationToken cancellationToken = default)
        => runner.InvokeAsync("forge_draw_hatch", new { boundary = new { handle = boundaryHandle, vertices = boundaryVertices }, pattern, scale, angleDeg, layer }, dryRun, cancellationToken: cancellationToken);

    [McpServerTool(Name = "forge_draw_rectangle", ReadOnly = false, Destructive = false, Idempotent = false, OpenWorld = false)]
    [Description("Draw a closed rectangle polyline from EITHER opposite corners (p1, p2) OR (center, width, height) in WCS drawing units — do not supply both. Optional filletRadius (rounded corners, mutually exclusive with chamfer), chamferDistance1/chamferDistance2 (beveled corners), rotationDeg CCW around rectangle center, constantWidth, layer (must exist), colorIndex (0..256).")]
    public static Task<ForgeResult> DrawRectangle(
        ForgeToolRunner runner,
        double[]? p1 = null,
        double[]? p2 = null,
        double[]? center = null,
        double? width = null,
        double? height = null,
        double filletRadius = 0,
        double chamferDistance1 = 0,
        double chamferDistance2 = 0,
        double rotationDeg = 0,
        double? constantWidth = null,
        string? layer = null,
        int? colorIndex = null,
        bool dryRun = false,
        CancellationToken cancellationToken = default)
        => runner.InvokeAsync("forge_draw_rectangle", new { p1, p2, center, width, height, filletRadius, chamferDistance1, chamferDistance2, rotationDeg, constantWidth, layer, colorIndex }, dryRun, cancellationToken: cancellationToken);

    [McpServerTool(Name = "forge_draw_polygon", ReadOnly = false, Destructive = false, Idempotent = false, OpenWorld = false)]
    [Description("Draw a regular N-sided closed polygon (sides 3..1024) at WCS center [x,y] with radius > 0. inscribed=true (default) puts vertices on the circle; inscribed=false circumscribes the circle (apothem / flat-to-center = radius). Optional rotationDeg CCW, constantWidth, layer (must exist), colorIndex.")]
    public static Task<ForgeResult> DrawPolygon(
        ForgeToolRunner runner,
        double[] center,
        int sides,
        double radius,
        bool inscribed = true,
        double rotationDeg = 0,
        double? constantWidth = null,
        string? layer = null,
        int? colorIndex = null,
        bool dryRun = false,
        CancellationToken cancellationToken = default)
        => runner.InvokeAsync("forge_draw_polygon", new { center, sides, radius, inscribed, rotationDeg, constantWidth, layer, colorIndex }, dryRun, cancellationToken: cancellationToken);

    [McpServerTool(Name = "forge_draw_ellipse", ReadOnly = false, Destructive = false, Idempotent = false, OpenWorld = false)]
    [Description("Draw a full ellipse or elliptical arc at WCS center [x,y,(z)]. Specify EITHER majorAxis [dx,dy,(dz)] vector OR majorRadius (>0) + rotationDeg CCW. Minor axis via minorRadius (>0, <=majorRadius) or radiusRatio (minor/major in (0,1]). Optional startAngleDeg and endAngleDeg (parametric angles in degrees CCW from the major axis) for an elliptical arc.")]
    public static Task<ForgeResult> DrawEllipse(
        ForgeToolRunner runner,
        double[] center,
        double? majorRadius = null,
        double[]? majorAxis = null,
        double? minorRadius = null,
        double? radiusRatio = null,
        double rotationDeg = 0,
        double? startAngleDeg = null,
        double? endAngleDeg = null,
        string? layer = null,
        int? colorIndex = null,
        bool dryRun = false,
        CancellationToken cancellationToken = default)
        => runner.InvokeAsync("forge_draw_ellipse", new { center, majorRadius, majorAxis, minorRadius, radiusRatio, rotationDeg, startAngleDeg, endAngleDeg, layer, colorIndex }, dryRun, cancellationToken: cancellationToken);

    [McpServerTool(Name = "forge_draw_spline", ReadOnly = false, Destructive = false, Idempotent = false, OpenWorld = false)]
    [Description("Draw a smooth NURBS spline through fitPoints (≥2 WCS points; ≥3 when closed=true). Optional closed (creates a periodic spline without duplicating endpoints), degree (2..11, default 3), startTangent [dx,dy,(dz)] and endTangent [dx,dy,(dz)] (non-zero vectors, open splines), layer, colorIndex.")]
    public static Task<ForgeResult> DrawSpline(
        ForgeToolRunner runner,
        double[][] fitPoints,
        bool closed = false,
        int degree = 3,
        double[]? startTangent = null,
        double[]? endTangent = null,
        string? layer = null,
        int? colorIndex = null,
        bool dryRun = false,
        CancellationToken cancellationToken = default)
        => runner.InvokeAsync("forge_draw_spline", new { fitPoints, closed, degree, startTangent, endTangent, layer, colorIndex }, dryRun, cancellationToken: cancellationToken);

    [McpServerTool(Name = "forge_draw_revcloud", ReadOnly = false, Destructive = false, Idempotent = false, OpenWorld = false)]
    [Description("Draw an AEC revision cloud (closed polyline with outward arc lobes regardless of CW/CCW vertex winding) from EITHER polygon vertices (≥3) OR rectangular corners (p1, p2). Optional arcLength (>0; default = perimeter/24), bulgeMagnitude in (0,1] (default 0.52), constantWidth, layer, colorIndex.")]
    public static Task<ForgeResult> DrawRevCloud(
        ForgeToolRunner runner,
        double[][]? vertices = null,
        double[]? p1 = null,
        double[]? p2 = null,
        double arcLength = 0,
        double? bulgeMagnitude = null,
        double? constantWidth = null,
        string? layer = null,
        int? colorIndex = null,
        bool dryRun = false,
        CancellationToken cancellationToken = default)
        => runner.InvokeAsync("forge_draw_revcloud", new { vertices, p1, p2, arcLength, bulgeMagnitude, constantWidth, layer, colorIndex }, dryRun, cancellationToken: cancellationToken);

    [McpServerTool(Name = "forge_draw_leader", ReadOnly = false, Destructive = false, Idempotent = false, OpenWorld = false)]
    [Description("Draw a multi-leader (MLeader) callout: points (≥2 WCS points from arrow tip at points[0] to landing at points[last]), optional MText callout text, textHeight (>0, default 2.5), arrowSize (>0), hasArrowHead (default true; false suppresses the arrowhead), layer, colorIndex.")]
    public static Task<ForgeResult> DrawLeader(
        ForgeToolRunner runner,
        double[][] points,
        string? text = null,
        double textHeight = 2.5,
        double? arrowSize = null,
        bool hasArrowHead = true,
        string? layer = null,
        int? colorIndex = null,
        bool dryRun = false,
        CancellationToken cancellationToken = default)
        => runner.InvokeAsync("forge_draw_leader", new { points, text, textHeight, arrowSize, hasArrowHead, layer, colorIndex }, dryRun, cancellationToken: cancellationToken);

    [McpServerTool(Name = "forge_draw_table", ReadOnly = false, Destructive = false, Idempotent = false, OpenWorld = false)]
    [Description("Draw an AutoCAD Table (schedule/BOM/legend, max 10,000 cells): position [x,y,(z)], rows (2D string array of cells), optional title (prepends a merged title row above rows), hasTitleRow (when true without title, merges rows[0] across columns; when false, unmerges row 0), rowHeight (>0, default 8), columnWidths[] or uniform columnWidth (>0, default 30), textHeight (>0, default 2.5), layer, colorIndex.")]
    public static Task<ForgeResult> DrawTable(
        ForgeToolRunner runner,
        double[] position,
        string[][] rows,
        string? title = null,
        double rowHeight = 8,
        double columnWidth = 30,
        double[]? columnWidths = null,
        double textHeight = 2.5,
        bool hasTitleRow = false,
        string? layer = null,
        int? colorIndex = null,
        bool dryRun = false,
        CancellationToken cancellationToken = default)
        => runner.InvokeAsync("forge_draw_table", new { position, rows, title, rowHeight, columnWidth, columnWidths, textHeight, hasTitleRow, layer, colorIndex }, dryRun, cancellationToken: cancellationToken);

    [McpServerTool(Name = "forge_draw_dimension", ReadOnly = false, Destructive = false, Idempotent = false, OpenWorld = false)]
    [Description("Draw a dimension: kind = linear (p1, p2, dimLinePoint, optional rotationDeg: 0=horizontal, 90=vertical) | aligned (p1, p2, dimLinePoint) | radius (p1=center, p2=pointOnCircle) | diameter (p1=center, p2=pointOnCircle; far chord point is computed at 2*p1-p2) | angular (vertex=angleVertex, p1=firstRayPoint, p2=secondRayPoint, dimLinePoint=arcPassThroughPoint — all 4 required) | ordinate (p1=featurePoint, dimLinePoint or p2=leaderEndPoint, useXAxis). Optional dimStyle (must exist), dimScale (>0), textOverride ('<>' keeps measured value), layer, colorIndex.")]
    public static Task<ForgeResult> DrawDimension(
        ForgeToolRunner runner,
        string kind,
        double[] p1,
        double[]? p2 = null,
        double[]? dimLinePoint = null,
        double[]? vertex = null,
        double rotationDeg = 0,
        bool useXAxis = true,
        string? dimStyle = null,
        double? dimScale = null,
        string? textOverride = null,
        string? layer = null,
        int? colorIndex = null,
        bool dryRun = false,
        CancellationToken cancellationToken = default)
        => runner.InvokeAsync("forge_draw_dimension", new { kind, p1, p2, dimLinePoint, vertex, rotationDeg, useXAxis, dimStyle, dimScale, textOverride, layer, colorIndex }, dryRun, cancellationToken: cancellationToken);

    [McpServerTool(Name = "forge_draw_batch", ReadOnly = false, Destructive = false, Idempotent = false, OpenWorld = false)]
    [Description("Draw up to 1000 entities in ONE call: steps[] of {tool: line|polyline|rectangle|polygon|circle|arc|ellipse|spline|revcloud|text|mtext|hatch|dimension|leader|block_insert, ...params}. Annotation keys: textStyle (text/mtext/leader), backgroundMask + maskScale 1–5 (mtext and leader text), leaderStyle (multileader style name), contentBlock + attributes (leader with a block tag bubble at the landing point), dimStyle (dimension kind linear|aligned|radius|diameter|angular|ordinate; rotationDeg rotates a linear dim), pattern/scale/angleDeg (hatch inside the given vertices; the closed boundary polyline is kept). Default: each step runs in its own nested transaction so a failed step never leaves partial geometry and the rest continue; all failing returns batch_all_failed. atomic=true validates every step first and creates everything in one transaction (all-or-nothing: batch_invalid / batch_aborted create nothing) — fastest for large batches. resultMode: full (default, per-step results), errors (failed steps + warnings only), summary (counts only, no handle list). Every result carries countsByType, elapsedMs and entitiesPerSecond. Example: [{\"tool\":\"rectangle\",\"p1\":[0,0],\"p2\":[100,50]},{\"tool\":\"leader\",\"points\":[[10,10],[30,25]],\"text\":\"SD-1\",\"backgroundMask\":true}].")]
    public static Task<ForgeResult> DrawBatch(ForgeToolRunner runner, DrawStepDto[] steps, bool atomic = false, string? resultMode = null, bool dryRun = false, CancellationToken cancellationToken = default)
        => runner.InvokeAsync("forge_draw_batch", new { steps, atomic, resultMode }, dryRun, cancellationToken: cancellationToken);

    [McpServerTool(Name = "forge_entity_get", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("Read one entity by handle: returns {entity, kind, kindError?} with common summary (type, layer, color, linetype, lineweight, bbox) plus rich kind geometry (Line, Polyline with per-vertex widths up to 1000 vertices + truncated flag, Polyline2d/3d, Circle, Arc, Ellipse, Spline, DBText/MText with textStyle (+ MText backgroundMask), MLeader with backgroundMask/contentBlock, Table cells, Hatch area, Dimension measurement + dimStyleName, BlockReference attributes + dynamic effectiveName, Region area, Xline/Ray).")]
    public static Task<ForgeResult> EntityGet(ForgeToolRunner runner, string handle, CancellationToken cancellationToken = default)
        => runner.InvokeAsync("forge_entity_get", new { handle }, cancellationToken: cancellationToken);

    [McpServerTool(Name = "forge_entity_list", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("List model-space entities: exactly one of window [[x1,y1],[x2,y2]] or layer, optional exact type name (Line, Polyline, Circle, DBText...), maxEntities (default 1000). There is no select-all. includeBlocks=true also walks into block INSERTs (nested, transforms applied, xrefs via their INSERTs, blockDepth default 8 / max 32): nested rows add effectiveLayer (layer 0 shows on the INSERT's layer), depth, insertPath, blockPath and a world bbox; the window is tested in world coordinates and prunes INSERTs that miss it; the layer filter matches own or effective layer.")]
    public static Task<ForgeResult> EntityList(ForgeToolRunner runner, double[][]? window = null, string? layer = null, string? type = null, int? maxEntities = null, bool includeBlocks = false, int? blockDepth = null, CancellationToken cancellationToken = default)
        => runner.InvokeAsync("forge_entity_list", new { window, layer, type, maxEntities, includeBlocks, blockDepth }, cancellationToken: cancellationToken);

    [McpServerTool(Name = "forge_entity_move", ReadOnly = false, Destructive = true, Idempotent = false, OpenWorld = false)]
    [Description("Move entities by explicit handles: delta [dx,dy,(dz)]. Pre-write backup + undo mark apply; read-back echoes the moved geometry.")]
    public static Task<ForgeResult> EntityMove(ForgeToolRunner runner, string[] handles, double[] delta, bool dryRun = false, CancellationToken cancellationToken = default)
        => runner.InvokeAsync("forge_entity_move", new { handles, delta }, dryRun, cancellationToken: cancellationToken);

    [McpServerTool(Name = "forge_entity_copy", ReadOnly = false, Destructive = true, Idempotent = false, OpenWorld = false)]
    [Description("Copy entities by explicit handles with delta [dx,dy,(dz)], copies=1..100 (copy n lands n×delta away). Returns the new handles.")]
    public static Task<ForgeResult> EntityCopy(ForgeToolRunner runner, string[] handles, double[] delta, int copies = 1, bool dryRun = false, CancellationToken cancellationToken = default)
        => runner.InvokeAsync("forge_entity_copy", new { handles, delta, copies }, dryRun, cancellationToken: cancellationToken);

    [McpServerTool(Name = "forge_entity_rotate", ReadOnly = false, Destructive = true, Idempotent = false, OpenWorld = false)]
    [Description("Rotate entities by explicit handles about basePoint, angleDeg CCW around WCS Z. Pre-write backup + undo mark apply.")]
    public static Task<ForgeResult> EntityRotate(ForgeToolRunner runner, string[] handles, double[] basePoint, double angleDeg, bool dryRun = false, CancellationToken cancellationToken = default)
        => runner.InvokeAsync("forge_entity_rotate", new { handles, basePoint, angleDeg }, dryRun, cancellationToken: cancellationToken);

    [McpServerTool(Name = "forge_entity_scale", ReadOnly = false, Destructive = true, Idempotent = false, OpenWorld = false)]
    [Description("Uniformly scale entities by explicit handles about basePoint, factor > 0. Pre-write backup + undo mark apply.")]
    public static Task<ForgeResult> EntityScale(ForgeToolRunner runner, string[] handles, double[] basePoint, double factor, bool dryRun = false, CancellationToken cancellationToken = default)
        => runner.InvokeAsync("forge_entity_scale", new { handles, basePoint, factor }, dryRun, cancellationToken: cancellationToken);

    [McpServerTool(Name = "forge_entity_mirror", ReadOnly = false, Destructive = true, Idempotent = false, OpenWorld = false)]
    [Description("Mirror entities by explicit handles about the axis through p1–p2 (WCS). eraseSource=true moves them to the mirrored position under new handles; default keeps both.")]
    public static Task<ForgeResult> EntityMirror(ForgeToolRunner runner, string[] handles, double[] p1, double[] p2, bool eraseSource = false, bool dryRun = false, CancellationToken cancellationToken = default)
        => runner.InvokeAsync("forge_entity_mirror", new { handles, p1, p2, eraseSource }, dryRun, cancellationToken: cancellationToken);

    [McpServerTool(Name = "forge_entity_offset", ReadOnly = false, Destructive = true, Idempotent = false, OpenWorld = false)]
    [Description("Offset one curve (line/polyline/arc/circle/ellipse handle) by distance (negative toward the closed side); read-back shows the resulting geometry class. Returns new handles.")]
    public static Task<ForgeResult> EntityOffset(ForgeToolRunner runner, string handle, double distance, string? layer = null, bool dryRun = false, CancellationToken cancellationToken = default)
        => runner.InvokeAsync("forge_entity_offset", new { handle, distance, layer }, dryRun, cancellationToken: cancellationToken);

    [McpServerTool(Name = "forge_entity_erase", ReadOnly = false, Destructive = true, Idempotent = false, OpenWorld = false)]
    [Description("Erase explicit entity handles (≤1000; >100 requires acknowledgeBulk=true). Dry-run first is recommended; undo mark + backup apply. There is no select-all.")]
    public static Task<ForgeResult> EntityErase(ForgeToolRunner runner, string[] handles, bool acknowledgeBulk = false, bool dryRun = false, CancellationToken cancellationToken = default)
        => runner.InvokeAsync("forge_entity_erase", new { handles, acknowledgeBulk }, dryRun, cancellationToken: cancellationToken);

    [McpServerTool(Name = "forge_entity_set_props", ReadOnly = false, Destructive = true, Idempotent = false, OpenWorld = false)]
    [Description("Set properties on explicit entity handles: layer (must exist), colorIndex (0 ByBlock/1–255/256 ByLayer), linetype (must exist), lineweight (-3 default, -2 ByBlock, -1 ByLayer, 0–211 hundredths mm).")]
    public static Task<ForgeResult> EntitySetProps(ForgeToolRunner runner, string[] handles, string? layer = null, int? colorIndex = null, string? linetype = null, int? lineweight = null, bool dryRun = false, CancellationToken cancellationToken = default)
        => runner.InvokeAsync("forge_entity_set_props", new { handles, layer, colorIndex, linetype, lineweight }, dryRun, cancellationToken: cancellationToken);

    [McpServerTool(Name = "forge_undo", ReadOnly = false, Destructive = false, Idempotent = false, OpenWorld = false)]
    [Description("Undo one step in AutoCAD: every Forge write is one undo group, so this reverts exactly the last typed write. Honest result; nothing to undo returns undo_failed.")]
    public static Task<ForgeResult> Undo(ForgeToolRunner runner, CancellationToken cancellationToken = default)
        => runner.InvokeAsync("forge_undo", new { }, cancellationToken: cancellationToken);

    [McpServerTool(Name = "forge_block_create", ReadOnly = false, Destructive = true, Idempotent = false, OpenWorld = false)]
    [Description("Create a block definition from existing entities: basePoint becomes the insert origin; the source entities are consumed (cloned into the definition, originals erased in one transaction). Optional attributeDefs[{tag,prompt,height,position,invisible}].")]
    public static Task<ForgeResult> BlockCreate(ForgeToolRunner runner, string name, double[] basePoint, string[] handles, BlockAttributeDefDto[]? attributeDefs = null, bool dryRun = false, CancellationToken cancellationToken = default)
        => runner.InvokeAsync("forge_block_create", new { name, basePoint, handles, attributeDefs }, dryRun, cancellationToken: cancellationToken);

    [McpServerTool(Name = "forge_block_insert", ReadOnly = false, Destructive = false, Idempotent = false, OpenWorld = false)]
    [Description("Insert a block reference: name, position (WCS), uniform scale, rotationDeg, layer, attributes {tag: value}. Read-back echoes the applied attributes. Returns the insert handle.")]
    public static Task<ForgeResult> BlockInsert(ForgeToolRunner runner, string name, double[] position, double scale = 1, double rotationDeg = 0, string? layer = null, Dictionary<string, string>? attributes = null, bool dryRun = false, CancellationToken cancellationToken = default)
        => runner.InvokeAsync("forge_block_insert", new { name, position, scale, rotationDeg, layer, attributes }, dryRun, cancellationToken: cancellationToken);

    [McpServerTool(Name = "forge_block_list", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("List block definitions (not inserts): name, handle, origin, attribute tags. Use forge_block_list_attributes for the values on a specific insert.")]
    public static Task<ForgeResult> BlockList(ForgeToolRunner runner, CancellationToken cancellationToken = default)
        => runner.InvokeAsync("forge_block_list", new { }, cancellationToken: cancellationToken);

    [McpServerTool(Name = "forge_layer_create", ReadOnly = false, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("Create a layer (colorIndex ACI 0–256 default 7, linetype must already exist, plottable). Idempotent: an existing layer returns alreadyExists=true. Draw tools require the layer to exist first.")]
    public static Task<ForgeResult> LayerCreate(ForgeToolRunner runner, string name, int? colorIndex = null, string? linetype = null, bool plottable = true, bool dryRun = false, CancellationToken cancellationToken = default)
        => runner.InvokeAsync("forge_layer_create", new { name, colorIndex, linetype, plottable }, dryRun, cancellationToken: cancellationToken);

    [McpServerTool(Name = "forge_layer_set", ReadOnly = false, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("Set one layer's state: isOn, isFrozen (refused on the current layer), isLocked, isPlottable, colorIndex, and/or makeCurrent. Read-back echoes the resulting state.")]
    public static Task<ForgeResult> LayerSet(ForgeToolRunner runner, string name, bool? isOn = null, bool? isFrozen = null, bool? isLocked = null, bool? isPlottable = null, int? colorIndex = null, bool makeCurrent = false, bool dryRun = false, CancellationToken cancellationToken = default)
        => runner.InvokeAsync("forge_layer_set", new { name, isOn, isFrozen, isLocked, isPlottable, colorIndex, makeCurrent }, dryRun, cancellationToken: cancellationToken);

    [McpServerTool(Name = "forge_layout_create", ReadOnly = false, Destructive = false, Idempotent = false, OpenWorld = false)]
    [Description("Create a paper-space layout tab by name. Refuses an existing name with layout_not_unique.")]
    public static Task<ForgeResult> LayoutCreate(ForgeToolRunner runner, string name, bool dryRun = false, CancellationToken cancellationToken = default)
        => runner.InvokeAsync("forge_layout_create", new { name }, dryRun, cancellationToken: cancellationToken);

    [McpServerTool(Name = "forge_viewport_add", ReadOnly = false, Destructive = false, Idempotent = false, OpenWorld = false)]
    [Description("Add a model-space viewport to a paper-space layout: layout, paper-space center [x,y], width, height. Read-back carries the viewport handle and number.")]
    public static Task<ForgeResult> ViewportAdd(ForgeToolRunner runner, string layout, double[] center, double width, double height, bool dryRun = false, CancellationToken cancellationToken = default)
        => runner.InvokeAsync("forge_viewport_add", new { layout, center, width, height }, dryRun, cancellationToken: cancellationToken);

    [McpServerTool(Name = "forge_doc_new", ReadOnly = false, Destructive = false, Idempotent = false, OpenWorld = false)]
    [Description("Create a new document (optionally from a DWT/DWG templatePath); it becomes the active document. Call forge_doc_list_open afterwards to confirm.")]
    public static Task<ForgeResult> DocNew(ForgeToolRunner runner, string? templatePath = null, bool dryRun = false, CancellationToken cancellationToken = default)
        => runner.InvokeAsync("forge_doc_new", new { templatePath }, dryRun, cancellationToken: cancellationToken);

    [McpServerTool(Name = "forge_doc_export_dxf", ReadOnly = false, Destructive = false, Idempotent = false, OpenWorld = false)]
    [Description("Export the in-memory drawing state (unsaved changes included) to a DXF file; overwrite acknowledgement + destination backup. Verification is a byte-count probe on the written file.")]
    public static Task<ForgeResult> DocExportDxf(ForgeToolRunner runner, string outputPath, bool overwriteAcknowledged = false, bool dryRun = false, CancellationToken cancellationToken = default)
        => runner.InvokeAsync("forge_doc_export_dxf", new { outputPath, overwriteAcknowledged }, dryRun, cancellationToken: cancellationToken);

    [McpServerTool(Name = "forge_draw_polyline_from_csv", ReadOnly = false, Destructive = false, Idempotent = false, OpenWorld = false)]
    [Description("Draw one polyline from a CSV of coordinates (columns xColumn/yColumn, delimiter, hasHeader; '.' decimals) — large survey/centerline point lists without token cost. Reports dropped lines.")]
    public static Task<ForgeResult> DrawPolylineFromCsv(ForgeToolRunner runner, string csvPath, bool hasHeader = false, string? delimiter = null, int xColumn = 0, int yColumn = 1, bool closed = false, double? constantWidth = null, string? layer = null, int? colorIndex = null, bool dryRun = false, CancellationToken cancellationToken = default)
        => runner.InvokeAsync("forge_draw_polyline_from_csv", new { csvPath, hasHeader, delimiter, xColumn, yColumn, closed, constantWidth, layer, colorIndex }, dryRun, cancellationToken: cancellationToken);

    [McpServerTool(Name = "forge_measure_batch", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("True measurement from the geometry engine (never re-read vertices): per-handle length/area/type/layer, pointDistance for p1+p2, and the bbox union of the handles.")]
    public static Task<ForgeResult> MeasureBatch(ForgeToolRunner runner, string[]? handles = null, double[]? p1 = null, double[]? p2 = null, CancellationToken cancellationToken = default)
        => runner.InvokeAsync("forge_measure_batch", new { handles, p1, p2 }, cancellationToken: cancellationToken);

    [McpServerTool(Name = "forge_entity_array", ReadOnly = false, Destructive = true, Idempotent = false, OpenWorld = false)]
    [Description("Array entities by explicit handles. kind=rectangular: rows, columns, rowSpacing, colSpacing. kind=polar: center, count (2..5000), angleTotalDeg (default 360), rotateItems (default true). Sources untouched; returns new handles.")]
    public static Task<ForgeResult> EntityArray(ForgeToolRunner runner, string[] handles, string kind, int? rows = null, int? columns = null, double? rowSpacing = null, double? colSpacing = null, double[]? center = null, int? count = null, double? angleTotalDeg = null, bool rotateItems = true, bool dryRun = false, CancellationToken cancellationToken = default)
        => runner.InvokeAsync("forge_entity_array", new { handles, kind, rows, columns, rowSpacing, colSpacing, center, count, angleTotalDeg, rotateItems }, dryRun, cancellationToken: cancellationToken);

    [McpServerTool(Name = "forge_view_capture", ReadOnly = false, Destructive = false, Idempotent = false, OpenWorld = false)]
    [Description("Capture AutoCAD's main window to a PNG so the agent can LOOK at the drawing (PrintWindow works while the window is in the background, not minimized). fit=true zooms extents first; window=[[x1,y1],[x2,y2]] (world coordinates of the current space) zooms to that box instead, with padding (fraction per side, default 0.05). cropToDrawing=true crops to the drawing area (no ribbon/palettes). The zoom never runs _.ZOOM (undo untouched); the applied view (world box, center, size) is read back. Read the returned PNG path to visually verify work.")]
    public static Task<ForgeResult> ViewCapture(ForgeToolRunner runner, string outputPath, bool fit = false, double[][]? window = null, double? padding = null, bool cropToDrawing = false, bool overwriteAcknowledged = false, bool dryRun = false, CancellationToken cancellationToken = default)
        => runner.InvokeAsync("forge_view_capture", new { outputPath, fit, window, padding, cropToDrawing, overwriteAcknowledged }, dryRun, cancellationToken: cancellationToken);

    [McpServerTool(Name = "forge_entity_fillet", ReadOnly = false, Destructive = true, Idempotent = false, OpenWorld = false)]
    [Description("Fillet EITHER two coplanar Line handles (handle1, handle2, radius >= 0; radius=0 trims/extends both lines to a sharp corner) OR all straight corners of one Polyline handle (handle or handle1, radius > 0, preserving per-vertex widths and reporting filletedCorners/skippedCorners). When a line crosses the corner (X-intersection), pass pickPoint1/pickPoint2 [x,y] on the side to keep; otherwise the far endpoint survives.")]
    public static Task<ForgeResult> EntityFillet(
        ForgeToolRunner runner,
        double radius = 0,
        string? handle1 = null,
        string? handle2 = null,
        string? handle = null,
        double[]? pickPoint1 = null,
        double[]? pickPoint2 = null,
        string? layer = null,
        bool dryRun = false,
        CancellationToken cancellationToken = default)
        => runner.InvokeAsync("forge_entity_fillet", new { radius, handle1, handle2, handle, pickPoint1, pickPoint2, layer }, dryRun, cancellationToken: cancellationToken);

    [McpServerTool(Name = "forge_entity_chamfer", ReadOnly = false, Destructive = true, Idempotent = false, OpenWorld = false)]
    [Description("Chamfer two coplanar Line handles (handle1, handle2) with distance1 (>0 along line1) and optional distance2 (>0 along line2, defaults to distance1): trims/extends both lines and inserts the bevel Line segment in the source owner space. Pass pickPoint1/pickPoint2 [x,y] on the side to keep when a line crosses the corner intersection.")]
    public static Task<ForgeResult> EntityChamfer(
        ForgeToolRunner runner,
        string handle1,
        string handle2,
        double distance1,
        double? distance2 = null,
        double[]? pickPoint1 = null,
        double[]? pickPoint2 = null,
        string? layer = null,
        bool dryRun = false,
        CancellationToken cancellationToken = default)
        => runner.InvokeAsync("forge_entity_chamfer", new { handle1, handle2, distance1, distance2, pickPoint1, pickPoint2, layer }, dryRun, cancellationToken: cancellationToken);

    [McpServerTool(Name = "forge_entity_trim", ReadOnly = false, Destructive = true, Idempotent = false, OpenWorld = false)]
    [Description("Trim a curve (targetHandle) against cuttingHandles[] by removing ONLY the split segment closest to pickPoint [x,y] and keeping all other surviving segments. Closed curves (Circle, closed Polyline/Ellipse) require ≥2 distinct cuts. When a Line or Arc has 1 surviving piece it is updated in place (handlePreserved=true); otherwise returns all surviving handles[].")]
    public static Task<ForgeResult> EntityTrim(
        ForgeToolRunner runner,
        string targetHandle,
        string[] cuttingHandles,
        double[] pickPoint,
        bool dryRun = false,
        CancellationToken cancellationToken = default)
        => runner.InvokeAsync("forge_entity_trim", new { targetHandle, cuttingHandles, pickPoint }, dryRun, cancellationToken: cancellationToken);

    [McpServerTool(Name = "forge_entity_extend", ReadOnly = false, Destructive = true, Idempotent = false, OpenWorld = false)]
    [Description("Extend an open Line, Arc, or straight-ended Polyline (targetHandle) to the nearest intersection strictly BEYOND the extended endpoint with boundaryHandles[]. endToExtend = 'end' (default) | 'start' | 'both' | 'auto' (smaller extension) | 'near' (end closer to nearPoint [x,y]). Never shortens the curve.")]
    public static Task<ForgeResult> EntityExtend(
        ForgeToolRunner runner,
        string targetHandle,
        string[] boundaryHandles,
        string endToExtend = "end",
        double[]? nearPoint = null,
        bool dryRun = false,
        CancellationToken cancellationToken = default)
        => runner.InvokeAsync("forge_entity_extend", new { targetHandle, boundaryHandles, endToExtend, nearPoint }, dryRun, cancellationToken: cancellationToken);

    [McpServerTool(Name = "forge_entity_join", ReadOnly = false, Destructive = true, Idempotent = false, OpenWorld = false)]
    [Description("Join contiguous, coplanar Line, Arc, or Polyline handles (handles[], ≥2, up to 1000) into a continuous Polyline in the source owner space, preserving properties from handles[0]. Returns joinedCount and unjoinedHandles[]; fails with nothing_joined if no secondary curve connects.")]
    public static Task<ForgeResult> EntityJoin(
        ForgeToolRunner runner,
        string[] handles,
        string? layer = null,
        bool dryRun = false,
        CancellationToken cancellationToken = default)
        => runner.InvokeAsync("forge_entity_join", new { handles, layer }, dryRun, cancellationToken: cancellationToken);

    [McpServerTool(Name = "forge_entity_explode", ReadOnly = false, Destructive = true, Idempotent = false, OpenWorld = false)]
    [Description("Explode compound entities (BlockReference, Polyline, Hatch, Dimension, MText — up to 1000 handles[]) into constituent primitives in their owner space. BlockReference visible attributes are converted to standalone DBText/MText (BURST-style). eraseSource=true (default) erases the original.")]
    public static Task<ForgeResult> EntityExplode(
        ForgeToolRunner runner,
        string[] handles,
        bool eraseSource = true,
        bool dryRun = false,
        CancellationToken cancellationToken = default)
        => runner.InvokeAsync("forge_entity_explode", new { handles, eraseSource }, dryRun, cancellationToken: cancellationToken);

    [McpServerTool(Name = "forge_entity_intersect", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("Compute exact WCS intersection points between two entities (handle1, handle2). extendMode = 'none' (default, on-both-segments) | 'extendFirst' (or 'first') | 'extendSecond' (or 'second') | 'extendBoth' (or 'both').")]
    public static Task<ForgeResult> EntityIntersect(
        ForgeToolRunner runner,
        string handle1,
        string handle2,
        string extendMode = "none",
        CancellationToken cancellationToken = default)
        => runner.InvokeAsync("forge_entity_intersect", new { handle1, handle2, extendMode }, cancellationToken: cancellationToken);

    [McpServerTool(Name = "forge_linetype_load", ReadOnly = false, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("Load linetype(s) (e.g. DASHED, CENTER, HIDDEN, PHANTOM, DASHDOT) into the drawing. name and/or names[] (no wildcards); file = .lin on the support path or full path (default: acadiso.lin, then acad.lin). Already-loaded linetypes are never reloaded (alreadyLoaded=true). Returns per-name results and all loaded linetypes. Errors: linetype_not_found_in_file, lin_file_not_found.")]
    public static Task<ForgeResult> LinetypeLoad(
        ForgeToolRunner runner,
        string? name = null,
        string[]? names = null,
        string? file = null,
        bool dryRun = false,
        CancellationToken cancellationToken = default)
        => runner.InvokeAsync("forge_linetype_load", new { name, names, file }, dryRun, cancellationToken: cancellationToken);

    [McpServerTool(Name = "forge_style_text_create", ReadOnly = false, Destructive = true, Idempotent = true, OpenWorld = false)]
    [Description("Create a text style. fontName: TrueType family (e.g. 'Arial', default for new styles — supports Vietnamese), a .ttf file, or an .shx file (+ optional bigFont .shx). textSize (0 = variable height, default 0), widthFactor (>0, default 1), obliqueAngleDeg (-85..85, default 0), makeCurrent. If the style exists the call fails with style_exists unless updateExisting=true; an update writes ONLY the supplied fields. Returns DB read-back with created/updated/changedFields.")]
    public static Task<ForgeResult> StyleTextCreate(
        ForgeToolRunner runner,
        string name,
        string? fontName = null,
        string? bigFont = null,
        double? textSize = null,
        double? widthFactor = null,
        double? obliqueAngleDeg = null,
        bool makeCurrent = false,
        bool updateExisting = false,
        bool dryRun = false,
        CancellationToken cancellationToken = default)
        => runner.InvokeAsync("forge_style_text_create", new { name, fontName, bigFont, textSize, widthFactor, obliqueAngleDeg, makeCurrent, updateExisting }, dryRun, cancellationToken: cancellationToken);

    [McpServerTool(Name = "forge_style_dim_create", ReadOnly = false, Destructive = true, Idempotent = true, OpenWorld = false)]
    [Description("Create a dimension style. textHeight (DIMTXT, default 2.5), arrowSize (DIMASZ, 2.5), dimScale (DIMSCALE, 1), extensionOffset (DIMEXO, 0.625), extensionExtend (DIMEXE, 1.25), textGap (DIMGAP, 0.625), precision (DIMDEC 0..8, 0), linearScaleFactor (DIMLFAC, 1), textStyle (DIMTXSTY, must exist; default Standard), colorIndex (DIMCLRD/E/T, 0..256), makeCurrent. If the style exists the call fails with style_exists unless updateExisting=true; an update writes ONLY the supplied fields (defaults apply to new styles only). Returns DB read-back with created/updated/changedFields.")]
    public static Task<ForgeResult> StyleDimCreate(
        ForgeToolRunner runner,
        string name,
        double? textHeight = null,
        double? arrowSize = null,
        double? dimScale = null,
        double? extensionOffset = null,
        double? extensionExtend = null,
        double? textGap = null,
        int? precision = null,
        double? linearScaleFactor = null,
        string? textStyle = null,
        int? colorIndex = null,
        bool makeCurrent = false,
        bool updateExisting = false,
        bool dryRun = false,
        CancellationToken cancellationToken = default)
        => runner.InvokeAsync(
            "forge_style_dim_create",
            new { name, textHeight, arrowSize, dimScale, extensionOffset, extensionExtend, textGap, precision, linearScaleFactor, textStyle, colorIndex, makeCurrent, updateExisting },
            dryRun,
            cancellationToken: cancellationToken);

    [McpServerTool(Name = "forge_qa_verify_titleblock", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("Compare titleblock attributes against expected tag/value pairs.")]
    public static Task<ForgeResult> QaVerifyTitleblock(ForgeToolRunner runner, Dictionary<string, string> expected, string? blockName = null, CancellationToken cancellationToken = default)
        => runner.InvokeAsync("forge_qa_verify_titleblock", new { expected, blockName }, cancellationToken: cancellationToken);

    [McpServerTool(Name = "forge_qa_check_xrefs", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("Detect unloaded, missing, or unresolved xrefs before plotting.")]
    public static Task<ForgeResult> QaCheckXrefs(ForgeToolRunner runner, CancellationToken cancellationToken)
        => runner.InvokeAsync("forge_qa_check_xrefs", new { }, cancellationToken: cancellationToken);

    [McpServerTool(Name = "forge_qa_audit_layers", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("Compare current layers against expected layer names and plotting policy.")]
    public static Task<ForgeResult> QaAuditLayers(ForgeToolRunner runner, string[] expectedLayers, CancellationToken cancellationToken)
        => runner.InvokeAsync("forge_qa_audit_layers", new { expectedLayers }, cancellationToken: cancellationToken);

    [McpServerTool(Name = "forge_qa_readback", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("Run a read-back query after a write operation.")]
    public static Task<ForgeResult> QaReadback(ForgeToolRunner runner, string targetTool, Dictionary<string, string>? keys = null, CancellationToken cancellationToken = default)
        => runner.InvokeAsync("forge_qa_readback", new { targetTool, keys }, cancellationToken: cancellationToken);

    [McpServerTool(Name = "forge_qa_readback_after_timeout", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("Timeout recovery protocol: read-back after a plugin timeout. Never retry the write blind — verify state first.")]
    public static Task<ForgeResult> QaReadbackAfterTimeout(ForgeToolRunner runner, string targetTool, Dictionary<string, string>? keys = null, CancellationToken cancellationToken = default)
        => runner.InvokeAsync("forge_qa_readback_after_timeout", new { targetTool, keys }, cancellationToken: cancellationToken);

    [McpServerTool(Name = "forge_audit_summarize", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("Summarize the last N server/plugin audit JSONL records for this machine (paths may be sensitive).")]
    public static Task<ForgeResult> AuditSummarize(ForgeToolRunner runner, int limit = 20, CancellationToken cancellationToken = default)
        => runner.InvokeAsync("forge_audit_summarize", new { limit }, cancellationToken: cancellationToken);

    [McpServerTool(Name = "forge_qa_preflight", ReadOnly = false, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("Run the publish readiness gate and write a structured QaReport artifact (xrefs, titleblock tags, unresolved #### fields, layers, pack v2, issue-set contract).")]
    public static Task<ForgeResult> QaPreflight(
        ForgeToolRunner runner,
        string[]? requiredTitleblockTags = null,
        string? titleblockBlockName = null,
        string[]? expectedLayers = null,
        CancellationToken cancellationToken = default)
        => runner.InvokeAsync("forge_qa_preflight", new { requiredTitleblockTags = requiredTitleblockTags ?? [], titleblockBlockName, expectedLayers = expectedLayers ?? [] }, cancellationToken: cancellationToken);

    [McpServerTool(Name = "forge_issue_set_validate", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("Load/validate an IssueSetContract (layouts↔drawingNos↔rev) against the active drawing and optional registry.")]
    public static Task<ForgeResult> IssueSetValidate(ForgeToolRunner runner, string? contractPath = null, CancellationToken cancellationToken = default)
        => runner.InvokeAsync("forge_issue_set_validate", new { contractPath }, cancellationToken: cancellationToken);

    [McpServerTool(Name = "forge_sheet_inventory_import", ReadOnly = false, Destructive = false, Idempotent = false, OpenWorld = false)]
    [Description("Import a CSV sheet inventory (layout,drawingNo,rev[,title]) into an IssueSetContract (writes contract JSON when outputContractPath is set). Does not write DST/SSM.")]
    public static Task<ForgeResult> SheetInventoryImport(
        ForgeToolRunner runner,
        string csvPath,
        string? contractId = null,
        string? projectId = null,
        string? outputContractPath = null,
        CancellationToken cancellationToken = default)
        => runner.InvokeAsync("forge_sheet_inventory_import", new { csvPath, contractId, projectId, outputContractPath }, cancellationToken: cancellationToken);

    [McpServerTool(Name = "forge_issue_set_diff", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("Diff two PublishReceipt JSON artifacts (layouts added/removed, output bytes changed).")]
    public static Task<ForgeResult> IssueSetDiff(ForgeToolRunner runner, string currentReceiptPath, string? previousReceiptPath = null, CancellationToken cancellationToken = default)
        => runner.InvokeAsync("forge_issue_set_diff", new { currentReceiptPath, previousReceiptPath }, cancellationToken: cancellationToken);

    [McpServerTool(Name = "forge_recipe_issue_set", ReadOnly = false, Destructive = false, Idempotent = false, OpenWorld = false)]
    [Description("Orchestrate inspect→normalize→fill→preflight→publish for a metro issue set. Refuses publish when preflight fails unless force=true.")]
    public static Task<ForgeResult> RecipeIssueSet(
        ForgeToolRunner runner,
        string outputPath,
        string[] layouts,
        bool singlePdf = true,
        bool overwriteAcknowledged = false,
        bool force = false,
        bool normalizeXrefs = true,
        string? layerState = null,
        CampaignEntryDto[]? campaignEntries = null,
        string[]? requiredTitleblockTags = null,
        string? titleblockBlockName = null,
        string[]? expectedLayers = null,
        bool dryRun = false,
        CancellationToken cancellationToken = default)
        => runner.InvokeAsync("forge_recipe_issue_set", new
        {
            outputPath,
            layouts,
            singlePdf,
            overwriteAcknowledged,
            force,
            normalizeXrefs,
            layerState,
            campaign = campaignEntries is null ? null : new { entries = campaignEntries },
            requiredTitleblockTags = requiredTitleblockTags ?? [],
            titleblockBlockName,
            expectedLayers = expectedLayers ?? []
        }, dryRun, cancellationToken: cancellationToken);

    [McpServerTool(Name = "forge_pack_and_go", ReadOnly = false, Destructive = false, Idempotent = false, OpenWorld = false)]
    [Description("Pack host DWG, xrefs, and optional CTB/STB into an output folder with manifest.json and relative path rewrite.")]
    public static Task<ForgeResult> PackAndGo(
        ForgeToolRunner runner,
        string outputDirectory,
        string[]? includePlotStyles = null,
        bool rewritePaths = true,
        bool overwriteAcknowledged = false,
        bool dryRun = false,
        CancellationToken cancellationToken = default)
        => runner.InvokeAsync("forge_pack_and_go", new { outputDirectory, includePlotStyles = includePlotStyles ?? [], rewritePaths, overwriteAcknowledged }, dryRun, cancellationToken: cancellationToken);

    [McpServerTool(Name = "forge_registry_load", ReadOnly = false, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("Load a project drawing-number registry JSON. Subsequent titleblock writes for drawing-number tags must match the registry.")]
    public static Task<ForgeResult> RegistryLoad(ForgeToolRunner runner, string path, bool dryRun = false, CancellationToken cancellationToken = default)
        => runner.InvokeAsync("forge_registry_load", new { path }, dryRun, cancellationToken: cancellationToken);

    [McpServerTool(Name = "forge_registry_lookup", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("Look up sheets in the loaded drawing registry, or a single drawingNo. Never invent drawing numbers.")]
    public static Task<ForgeResult> RegistryLookup(ForgeToolRunner runner, string? drawingNo = null, CancellationToken cancellationToken = default)
        => runner.InvokeAsync("forge_registry_lookup", new { drawingNo }, cancellationToken: cancellationToken);

    [McpServerTool(Name = "forge_pack_load", ReadOnly = false, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("Load a declarative project CAD standards pack (layers, forbidden layers, drawingNo regex, required titleblock tags).")]
    public static Task<ForgeResult> PackLoad(ForgeToolRunner runner, string path, bool dryRun = false, CancellationToken cancellationToken = default)
        => runner.InvokeAsync("forge_pack_load", new { path }, dryRun, cancellationToken: cancellationToken);

    [McpServerTool(Name = "forge_pack_status", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("Show which standards pack and drawing registry are loaded in the plugin session.")]
    public static Task<ForgeResult> PackStatus(ForgeToolRunner runner, CancellationToken cancellationToken = default)
        => runner.InvokeAsync("forge_pack_status", new { }, cancellationToken: cancellationToken);

    [McpServerTool(Name = "forge_system_tool_profile", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("List tool profiles (core|plot|qa|linework|authoring) or return the tool allowlist for one profile to reduce agent tool noise.")]
    public static Task<ForgeResult> SystemToolProfile(ForgeToolRunner runner, string? name = null, CancellationToken cancellationToken = default)
        => runner.InvokeAsync("forge_system_tool_profile", new { name }, cancellationToken: cancellationToken);

    [McpServerTool(Name = "forge_viewport_list", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("List paper-space viewports (handle, layout, scale, lock).")]
    public static Task<ForgeResult> ViewportList(ForgeToolRunner runner, string? layout = null, CancellationToken cancellationToken = default)
        => runner.InvokeAsync("forge_viewport_list", new { layout }, cancellationToken: cancellationToken);

    [McpServerTool(Name = "forge_viewport_set_layer_freeze", ReadOnly = false, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("Freeze or thaw a layer inside a specific viewport by handle (VP freeze), without changing global layer state.")]
    public static Task<ForgeResult> ViewportSetLayerFreeze(ForgeToolRunner runner, string handle, string layer, bool freeze = true, bool dryRun = false, CancellationToken cancellationToken = default)
        => runner.InvokeAsync("forge_viewport_set_layer_freeze", new { handle, layer, freeze }, dryRun, cancellationToken: cancellationToken);

    [McpServerTool(Name = "forge_linework_dump", ReadOnly = false, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("Extract CAD linework entities (line/polyline/arc[/circle]) as ordered vertices with handles. Source: live drawing (model space + xref contents) or a pl_dump.txt file via source=dumpFile. Layer matching is xref-prefix aware; use layerSuffix/layerMatch for explicit modes. The DEFAULT layerFilter keeps only pipe-like layers; pass layerFilter=\"*\" to read architecture/everything. includeBlocks=true walks into block INSERTs (nested, world transforms applied, MINSERT expanded, xrefs via their INSERTs; blockDepth default 8 / max 32; bbox [x1,y1,x2,y2] is world and prunes INSERTs that miss it; layer 0 content reports the INSERT's layer, ownLayer keeps the original); each entity then carries container (block:A>B) and insertPath, and blockTraversal reports what was walked/skipped.")]
    public static Task<ForgeResult> LineworkDump(
        ForgeToolRunner runner,
        string? source = null,
        string? dumpPath = null,
        string? layerFilter = null,
        string? layerSuffix = null,
        string? layerMatch = null,
        string? excludeLayerFilter = null,
        string? entityTypes = null,
        double[]? bbox = null,
        bool includeXrefContents = true,
        bool includeBlocks = false,
        int? blockDepth = null,
        double unitsPerMeter = 0,
        int maxEntities = 50000,
        string? outputPath = null,
        string? document = null,
        CancellationToken cancellationToken = default)
        => runner.InvokeAsync("forge_linework_dump", new { source, dumpPath, layerFilter, layerSuffix, layerMatch, excludeLayerFilter, entityTypes, bbox, includeXrefContents, includeBlocks, blockDepth, unitsPerMeter, maxEntities, outputPath }, document: document, cancellationToken: cancellationToken);

    [McpServerTool(Name = "forge_linework_trace", ReadOnly = false, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("Trace a point (or handle) to the exact CAD line/polyline it belongs to: ordered vertices, nearest segment index, layer, and both end coordinates for junction/branch queries.")]
    public static Task<ForgeResult> LineworkTrace(
        ForgeToolRunner runner,
        double x = 0,
        double y = 0,
        string? units = null,
        double tolerance = 500,
        double contextRadius = 0,
        string? handle = null,
        string? source = null,
        string? dumpPath = null,
        string? layerFilter = null,
        string? layerSuffix = null,
        string? layerMatch = null,
        string? excludeLayerFilter = null,
        string? entityTypes = null,
        double[]? bbox = null,
        bool includeXrefContents = true,
        bool includeBlocks = false,
        int? blockDepth = null,
        double unitsPerMeter = 0,
        int maxEntities = 50000,
        string? outputPath = null,
        string? document = null,
        CancellationToken cancellationToken = default)
        => runner.InvokeAsync("forge_linework_trace", new { x, y, units, tolerance, contextRadius, handle, source, dumpPath, layerFilter, layerSuffix, layerMatch, excludeLayerFilter, entityTypes, bbox, includeXrefContents, includeBlocks, blockDepth, unitsPerMeter, maxEntities, outputPath }, document: document, cancellationToken: cancellationToken);

    [McpServerTool(Name = "forge_linework_topology", ReadOnly = false, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("Build the connectivity graph of CAD linework: shared-vertex nodes, edges, T-junctions, X-crossings, continuous runs, and dead-end vs junction endpoint classification.")]
    public static Task<ForgeResult> LineworkTopology(
        ForgeToolRunner runner,
        double vertexTolerance = 10,
        double junctionTolerance = 50,
        bool includeCrossings = true,
        string? source = null,
        string? dumpPath = null,
        string? layerFilter = null,
        string? layerSuffix = null,
        string? layerMatch = null,
        string? excludeLayerFilter = null,
        string? entityTypes = null,
        double[]? bbox = null,
        bool includeXrefContents = true,
        bool includeBlocks = false,
        int? blockDepth = null,
        double unitsPerMeter = 0,
        int maxEntities = 50000,
        string? outputPath = null,
        string? document = null,
        CancellationToken cancellationToken = default)
        => runner.InvokeAsync("forge_linework_topology", new { vertexTolerance, junctionTolerance, includeCrossings, source, dumpPath, layerFilter, layerSuffix, layerMatch, excludeLayerFilter, entityTypes, bbox, includeXrefContents, includeBlocks, blockDepth, unitsPerMeter, maxEntities, outputPath }, document: document, cancellationToken: cancellationToken);

    [McpServerTool(Name = "forge_linework_coverage", ReadOnly = false, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("Compare CAD linework against modeled pipe centerlines (meters): report CAD segments with no modeled pipe (missing), modeled pipes with no CAD segment (extra), and partial-coverage sub-ranges.")]
    public static Task<ForgeResult> LineworkCoverage(
        ForgeToolRunner runner,
        PipeSegmentDto[]? modelSegments = null,
        string? modelSegmentsPath = null,
        double toleranceMeters = 0.3,
        double stepMeters = 0.5,
        double minCoverageFraction = 0.5,
        double modelUnitsPerMeter = 1,
        string? source = null,
        string? dumpPath = null,
        string? layerFilter = null,
        string? layerSuffix = null,
        string? layerMatch = null,
        string? excludeLayerFilter = null,
        string? entityTypes = null,
        double[]? bbox = null,
        bool includeXrefContents = true,
        bool includeBlocks = false,
        int? blockDepth = null,
        double unitsPerMeter = 0,
        int maxEntities = 50000,
        string? outputPath = null,
        string? document = null,
        CancellationToken cancellationToken = default)
        => runner.InvokeAsync("forge_linework_coverage", new { modelSegments, modelSegmentsPath, toleranceMeters, stepMeters, minCoverageFraction, modelUnitsPerMeter, source, dumpPath, layerFilter, layerSuffix, layerMatch, excludeLayerFilter, entityTypes, bbox, includeXrefContents, includeBlocks, blockDepth, unitsPerMeter, maxEntities, outputPath }, document: document, cancellationToken: cancellationToken);

    [McpServerTool(Name = "forge_linework_segments", ReadOnly = false, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("Flatten CAD linework to individual segments: stable seg index, precise endpoints, layer, and length — the canonical feed for route/QA scripts. units=meters scales to meters; transform/transformPath also emits transformed sT/eT endpoints.")]
    public static Task<ForgeResult> LineworkSegments(
        ForgeToolRunner runner,
        string? units = null,
        string? transformPath = null,
        CadTransformSpec? transform = null,
        string? source = null,
        string? dumpPath = null,
        string? layerFilter = null,
        string? layerSuffix = null,
        string? layerMatch = null,
        string? excludeLayerFilter = null,
        string? entityTypes = null,
        double[]? bbox = null,
        bool includeXrefContents = true,
        bool includeBlocks = false,
        int? blockDepth = null,
        double unitsPerMeter = 0,
        int maxEntities = 50000,
        string? outputPath = null,
        string? document = null,
        CancellationToken cancellationToken = default)
        => runner.InvokeAsync("forge_linework_segments", new { units, transformPath, transform, source, dumpPath, layerFilter, layerSuffix, layerMatch, excludeLayerFilter, entityTypes, bbox, includeXrefContents, includeBlocks, blockDepth, unitsPerMeter, maxEntities, outputPath }, document: document, cancellationToken: cancellationToken);

    [McpServerTool(Name = "forge_linework_compare", ReadOnly = false, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("Mark CAD linework vs Revit pipe segments: per-item classification (cad: matched/partial/missing_in_revit; model: matched/partial/extra_off_cad) with nearest counterpart + distanceM, plus matched pairs and optional SVG overlay at overlayPath (green=matched, red=missing, orange=extra, purple=partial).")]
    public static Task<ForgeResult> LineworkCompare(
        ForgeToolRunner runner,
        PipeSegmentDto[]? modelSegments = null,
        string? modelSegmentsPath = null,
        double toleranceMeters = 0.3,
        double stepMeters = 0.5,
        double minCoverageFraction = 0.5,
        double modelUnitsPerMeter = 1,
        double? minZ = null,
        double? maxZ = null,
        double searchRadiusMeters = 50,
        string? transformPath = null,
        CadTransformSpec? transform = null,
        string? overlayPath = null,
        string? source = null,
        string? dumpPath = null,
        string? layerFilter = null,
        string? layerSuffix = null,
        string? layerMatch = null,
        string? excludeLayerFilter = null,
        string? entityTypes = null,
        double[]? bbox = null,
        bool includeXrefContents = true,
        double unitsPerMeter = 0,
        int maxEntities = 50000,
        string? outputPath = null,
        string? document = null,
        CancellationToken cancellationToken = default)
        => runner.InvokeAsync("forge_linework_compare", new { modelSegments, modelSegmentsPath, toleranceMeters, stepMeters, minCoverageFraction, modelUnitsPerMeter, minZ, maxZ, searchRadiusMeters, transformPath, transform, overlayPath, source, dumpPath, layerFilter, layerSuffix, layerMatch, excludeLayerFilter, entityTypes, bbox, includeXrefContents, unitsPerMeter, maxEntities, outputPath }, document: document, cancellationToken: cancellationToken);

    [McpServerTool(Name = "forge_linework_transform", ReadOnly = false, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("Calibrate or apply a 2D CAD<->Revit transform (similarity or affine). pairs[{cad:[x,y],revit:[x,y],label?}] fits (>=2 similarity, >=3 affine); transformPath persists/loads the calibration; points/polylines convert coordinates. Project convention: mm->m scale 0.001 origin-to-origin; warning when max residual > 0.5 m.")]
    public static Task<ForgeResult> LineworkTransform(
        ForgeToolRunner runner,
        TransformPairDto[]? pairs = null,
        bool affine = false,
        string? transformPath = null,
        CadTransformSpec? transform = null,
        double[][]? points = null,
        double[][][]? polylines = null,
        string? direction = null,
        string? name = null,
        string? notes = null,
        double cadUnitsPerMeter = 0,
        CancellationToken cancellationToken = default)
        => runner.InvokeAsync("forge_linework_transform", new { pairs, affine, transformPath, transform, points, polylines, direction, name, notes, cadUnitsPerMeter }, cancellationToken: cancellationToken);

    [McpServerTool(Name = "forge_batch_run", ReadOnly = false, Destructive = true, Idempotent = false, OpenWorld = false)]
    [Description("Run an AccoreConsole job queue over multiple DWG/script pairs with partial-success reporting and optional resume batchId.")]
    public static Task<ForgeResult> BatchRun(
        ForgeToolRunner runner,
        BatchJobDto[]? jobs = null,
        bool continueOnError = true,
        string? resumeBatchId = null,
        bool dryRun = false,
        CancellationToken cancellationToken = default)
        => runner.InvokeAsync("forge_batch_run", new { jobs = jobs ?? [], continueOnError, resumeBatchId }, dryRun, cancellationToken: cancellationToken);

    [McpServerTool(Name = "forge_batch_status", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("Load a saved AccoreConsole batch resume state by batchId or artifact path.")]
    public static Task<ForgeResult> BatchStatus(ForgeToolRunner runner, string batchIdOrPath, CancellationToken cancellationToken = default)
        => runner.InvokeAsync("forge_batch_status", new { batchIdOrPath }, cancellationToken: cancellationToken);

    [McpServerTool(Name = "forge_exec_command", ReadOnly = false, Destructive = true, Idempotent = false, OpenWorld = true)]
    [Description("Execute a raw AutoCAD command string after denylist, backup, audit, and dry-run checks. Disabled unless FORGE_ENABLE_UNSAFE_OPS=true and unsafeAcknowledged=true. Prefers sync Editor.Command; if queued, completed=false.")]
    public static Task<ForgeResult> ExecCommand(ForgeToolRunner runner, string command, bool unsafeAcknowledged = false, bool dryRun = false, CancellationToken cancellationToken = default)
        => runner.InvokeAsync("forge_exec_command", new { command }, dryRun, unsafeAcknowledged: unsafeAcknowledged, cancellationToken: cancellationToken);

    [McpServerTool(Name = "forge_exec_lisp", ReadOnly = false, Destructive = true, Idempotent = false, OpenWorld = true)]
    [Description("Evaluate an AutoLISP expression synchronously and return its value (after denylist, backup, audit, UTF-8-safe transport). Disabled unless FORGE_ENABLE_UNSAFE_OPS=true and unsafeAcknowledged=true. Several top-level forms run in order; the last value is returned as result (JSON: nil->null, T->true, ints, reals, strings, points->arrays, lists, dotted pairs->{car,cdr}, enames->{ename:handle}, selection sets->handles) plus resultText (AutoLISP printed form) and resultType. A LISP runtime error returns lisp_error (completed=true, message in data.lispError). Comments are stripped; unbalanced parentheses/strings are refused before evaluation. mode=sync_eval normally; queued=true/completed=false only if this AutoCAD build has no evaluator entry point.")]
    public static Task<ForgeResult> ExecLisp(ForgeToolRunner runner, string lisp, bool unsafeAcknowledged = false, bool dryRun = false, CancellationToken cancellationToken = default)
        => runner.InvokeAsync("forge_exec_lisp", new { lisp }, dryRun, unsafeAcknowledged: unsafeAcknowledged, cancellationToken: cancellationToken);

    [McpServerTool(Name = "forge_run_script", ReadOnly = false, Destructive = true, Idempotent = false, OpenWorld = true)]
    [Description("Run a .scr or .lsp script against a DWG through accoreconsole.exe (headless, separate process) with pre-run backup. A .lsp is copied to a temp file and loaded by a generated .scr ((load \"...\") with a correctly escaped path) because accoreconsole /s only runs .scr; the safety scan runs on the user's script content. stdout/stderr are decoded (UTF-16LE/UTF-8) and cleaned of NUL/control characters; result.scriptKind says how it ran.")]
    public static Task<ForgeResult> RunScript(ForgeToolRunner runner, string dwgPath, string scriptPath, int? timeoutSeconds = null, bool dryRun = false, CancellationToken cancellationToken = default)
        => runner.InvokeAsync("forge_run_script", new { dwgPath, scriptPath, timeoutSeconds }, dryRun, cancellationToken: cancellationToken);

    [McpServerTool(Name = "forge_exec_dotnet", ReadOnly = false, Destructive = true, Idempotent = false, OpenWorld = true)]
    [Description("Execute a C# script inside the AutoCAD plugin context. Disabled unless unsafe ops are enabled and acknowledged.")]
    public static Task<ForgeResult> ExecDotNet(ForgeToolRunner runner, string code, bool unsafeAcknowledged = false, bool dryRun = false, CancellationToken cancellationToken = default)
        => runner.InvokeAsync("forge_exec_dotnet", new { code }, dryRun, unsafeAcknowledged: unsafeAcknowledged, cancellationToken: cancellationToken);

    [McpServerTool(Name = "forge_drawing_summary", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("Inspect an unfamiliar drawing in one call: returns drawing units (INSUNITS, LUNITS, MEASUREMENT), overall extents/bounding box, current layer/styles, entity counts by type and layer, block definitions with insert counts and attribute tags, layouts, text/dim styles, loaded linetypes, and external references.")]
    public static Task<ForgeResult> DrawingSummary(
        ForgeToolRunner runner,
        string? layout = null,
        CancellationToken cancellationToken = default)
        => runner.InvokeAsync("forge_drawing_summary", new { layout }, cancellationToken: cancellationToken);

    [McpServerTool(Name = "forge_text_list", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("Block-aware text read: TEXT, MTEXT (formatting stripped; rawText kept), ATTRIB (block attribute values with tag), constant ATTDEF and MLEADER text, walking into nested block INSERTs and xrefs by default (includeBlocks=true, blockDepth default 8 / max 32). Each row: text, type, tag, handle, layer, effectiveLayer, style, WORLD position/alignmentPoint/center/bbox, world height and rotationDeg (block scale/rotation applied), depth, insertPath, blockPath, invisible. Filters: window [[x1,y1],[x2,y2]] (world), layer (comma-separated wildcards, own or effective layer), contains (case-insensitive), regex, types (text,mtext,attrib,attdef,mleader), layout (default Model), maxResults (default 500, max 5000).")]
    public static Task<ForgeResult> TextList(
        ForgeToolRunner runner,
        double[][]? window = null,
        string? layer = null,
        string? contains = null,
        string? regex = null,
        string? types = null,
        string? layout = null,
        bool includeBlocks = true,
        int? blockDepth = null,
        bool includeXrefs = true,
        int? maxResults = null,
        string? document = null,
        CancellationToken cancellationToken = default)
        => runner.InvokeAsync("forge_text_list", new { window, layer, contains, regex, types, layout, includeBlocks, blockDepth, includeXrefs, maxResults }, document: document, cancellationToken: cancellationToken);

    // ── Speed wave (2026-10-29): fast import, annotation QA, standards, bulk insert, plot preset ──

    [McpServerTool(Name = "forge_doc_import", ReadOnly = false, Destructive = true, Idempotent = false, OpenWorld = false)]
    [Description("Import a DXF or DWG file into the ACTIVE drawing in ONE call (one side-database read + one native insert, no per-entity round trips; built for 5–20k entities). mode: block (default; one block definition named blockName, default = file name, plus one INSERT unless insertReference=false), merge (the source model space exploded into ours; layer forces every new entity onto one layer), xref (attach, or overlay=true; a DXF source is first saved as DWG to xrefSavePath). position (default origin), scale, rotationDeg, basePoint (source point that lands on position; default the source INSBASE). Units: with matchUnits=true (default) the source INSUNITS is scaled to ours. layerMap {\"SRC\":\"DST\", \"A-*\":\"M-*\"} renames/merges source layers (exact key first, then the most specific wildcard; a * in the target takes what the key's * matched, so A-WALL becomes M-WALL, or the whole name for exact keys); layerPrefix prefixes every unmapped layer; 0 and Defpoints are never renamed. An existing block of the same name fails block_exists unless replaceExisting=true, which redefines it in place (every existing INSERT updates). dryRun returns the inventory (counts by type/layer, units, layer plan, conflicts) without writing. Returns counts by type and layer, layersCreated/layersReused, extents (world), handles (returnHandles=true, max 5000), warnings and timingsMs.")]
    public static Task<ForgeResult> DocImport(
        ForgeToolRunner runner,
        string path,
        string? mode = null,
        string? blockName = null,
        double[]? position = null,
        double? scale = null,
        double? rotationDeg = null,
        double[]? basePoint = null,
        string? layer = null,
        string? layerPrefix = null,
        Dictionary<string, string>? layerMap = null,
        bool replaceExisting = false,
        bool insertReference = true,
        bool matchUnits = true,
        bool overlay = false,
        string? xrefSavePath = null,
        bool overwriteAcknowledged = false,
        bool returnHandles = false,
        bool dryRun = false,
        string? document = null,
        CancellationToken cancellationToken = default)
        => runner.InvokeAsync("forge_doc_import", new { path, mode, blockName, position, scale, rotationDeg, basePoint, layer, layerPrefix, layerMap, replaceExisting, insertReference, matchUnits, overlay, xrefSavePath, overwriteAcknowledged, returnHandles }, dryRun, document: document, cancellationToken: cancellationToken);

    [McpServerTool(Name = "forge_qa_annotation_clash", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("Read-only presentation QA (replaces eyeballing PNGs). One block-aware walk collects WORLD text boxes (TEXT, ATTRIB, MTEXT, MLEADER content, dimension text) and linework (lines, polylines with bulges, arcs, circles, ellipses, splines, leader and dimension lines), then checks: text_overlap (text boxes overlapping each other, area ratio >= minOverlapRatio, default 0.02), text_crossing (linework through a text box; masked MTEXT/MLEADER/DIMTFILL text is exempt unless reportMaskedCrossings=true), door_swing (a tag/leader landing inside a door swing: arcs on doorLayers or inside doorBlocks), wall_straddle (a text box cut by wall linework on wallLayers), min_height (plotted text height < minTextHeightMm at plotScale, e.g. plotScale=100 for 1:100; paper layouts are 1:1). Defaults: checks = all; doorLayers \"*DOOR*\"; doorBlocks \"*DOOR*,*DR-*\"; wallLayers \"*WALL*\"; minTextHeightMm 2.0. Filters: layout (default Model), window [[x1,y1],[x2,y2]] (world), textLayers / curveLayers / excludeLayers (comma-separated wildcards, own or effective layer). Off/frozen/non-plot layers are ignored. Text and linework of the same small block (a tag bubble, a dimension) are not reported against each other. Each finding: check, severity, message, handles, layers, texts, at [x,y], overlap/ratio, masked, insert paths. Returns passed, summary counts per check, findings (maxFindings default 200), findingsTruncated, elapsedMs.")]
    public static Task<ForgeResult> QaAnnotationClash(
        ForgeToolRunner runner,
        string? layout = null,
        double[][]? window = null,
        string[]? checks = null,
        string? textLayers = null,
        string? curveLayers = null,
        string? excludeLayers = null,
        string? doorLayers = null,
        string? doorBlocks = null,
        string? wallLayers = null,
        double? plotScale = null,
        double? minTextHeightMm = null,
        string? drawingUnits = null,
        double? minOverlapRatio = null,
        double? padding = null,
        bool reportMaskedCrossings = false,
        bool includeBlocks = true,
        int? blockDepth = null,
        bool includeXrefs = true,
        int? maxFindings = null,
        string? document = null,
        CancellationToken cancellationToken = default)
        => runner.InvokeAsync("forge_qa_annotation_clash", new { layout, window, checks, textLayers, curveLayers, excludeLayers, doorLayers, doorBlocks, wallLayers, plotScale, minTextHeightMm, drawingUnits, minOverlapRatio, padding, reportMaskedCrossings, includeBlocks, blockDepth, includeXrefs, maxFindings }, document: document, cancellationToken: cancellationToken);

    [McpServerTool(Name = "forge_standards_import", ReadOnly = false, Destructive = true, Idempotent = true, OpenWorld = false)]
    [Description("Copy drawing standards from a template DWG/DWT into the ACTIVE drawing in one call: include = any of linetypes, textStyles, layers, dimStyles, mleaderStyles, tableStyles, pageSetups (default all). Copied in dependency order (linetypes and text styles before the layers and dim styles that use them), one WblockCloneObjects per kind. onConflict: skip (default — existing definitions are kept) or overwrite (replaced in place; everything using them restyles). filter = comma-separated wildcards on names. Layer 0, Defpoints and the ByLayer/ByBlock/Continuous linetypes are never overwritten; xref-dependent and anonymous names are never copied. dryRun returns the per-item plan (add/overwrite/skip_existing/skip_protected/skip_filtered). The result is a change report per kind (added, overwritten, skipped with reasons) read back from the drawing.")]
    public static Task<ForgeResult> StandardsImport(
        ForgeToolRunner runner,
        string templatePath,
        string[]? include = null,
        string? filter = null,
        string? onConflict = null,
        bool dryRun = false,
        string? document = null,
        CancellationToken cancellationToken = default)
        => runner.InvokeAsync("forge_standards_import", new { templatePath, include, filter, onConflict }, dryRun, document: document, cancellationToken: cancellationToken);

    [McpServerTool(Name = "forge_block_insert_bulk", ReadOnly = false, Destructive = true, Idempotent = false, OpenWorld = false)]
    [Description("Insert up to 2000 block references in ONE call and ONE transaction: items[] of {name?, position [x,y(,z)], rotationDeg?, scale?, layer?, colorIndex?, attributes? {TAG: value}, leader? {points [[tip],[landing]] or [[landing]] (tip = insert position), text, textHeight?, layer?, style?, backgroundMask?}}. Item fields fall back to blockName / scale / rotationDeg / layer / leaderLayer / leaderStyle / leaderTextHeight. Blocks missing from the drawing are loaded by name from libraryPath (one read, one clone); redefine=true replaces drawing definitions with the library's (existing INSERTs update). All-or-nothing: every item is validated first (numbers, block, layers, multileader styles, drawing-registry attribute rules) and nothing is created if any item fails (data.errors lists them); an attribute tag the block does not have is a warning. dryRun validates only. Returns created, leaders, blocks, library (loaded/redefined), items [{index, handle, leader}] (returnHandles=true), warnings and elapsedMs.")]
    public static Task<ForgeResult> BlockInsertBulk(
        ForgeToolRunner runner,
        BulkInsertItem[] items,
        string? blockName = null,
        string? libraryPath = null,
        bool redefine = false,
        double? scale = null,
        double? rotationDeg = null,
        string? layer = null,
        string? leaderLayer = null,
        string? leaderStyle = null,
        double? leaderTextHeight = null,
        bool returnHandles = true,
        bool dryRun = false,
        string? document = null,
        CancellationToken cancellationToken = default)
        => runner.InvokeAsync("forge_block_insert_bulk", new { items, blockName, libraryPath, redefine, scale, rotationDeg, layer, leaderLayer, leaderStyle, leaderTextHeight, returnHandles }, dryRun, document: document, cancellationToken: cancellationToken);

    [McpServerTool(Name = "forge_plot_preset_apply", ReadOnly = false, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("Apply one plot configuration to one or many layouts in a single call (and optionally save it as the named page setup setupName). layouts: names or wildcards (\"*\" = every paper layout; Model only when named; default the current layout). device (default \"DWG To PDF.pc3\"; with noViewer=true, the default, the Forge no-viewer PC3 clone is used so no plot ever opens a viewer), paperSize (localized or canonical media name), plotStyle (CTB/STB, e.g. \"monochrome.ctb\"), plotArea layout|extents|display|limits|window (window=[[x1,y1],[x2,y2]]), scale \"fit\" or \"1:100\", units mm|inches, centered, orientation auto|landscape|portrait. Every layout is validated with PlotSettingsValidator before anything is written; one rejection aborts the whole call. dryRun resolves the layouts and the device without writing anything. Returns per-layout read-back (device, media, paper mm, area, scale, centring, rotation, style sheet) and the saved setup.")]
    public static Task<ForgeResult> PlotPresetApply(
        ForgeToolRunner runner,
        string[]? layouts = null,
        string? setupName = null,
        string? device = null,
        string? paperSize = null,
        string? plotStyle = null,
        string? plotArea = null,
        double[][]? window = null,
        string? scale = null,
        string? units = null,
        bool? centered = null,
        string? orientation = null,
        bool noViewer = true,
        bool dryRun = false,
        string? document = null,
        CancellationToken cancellationToken = default)
        => runner.InvokeAsync("forge_plot_preset_apply", new { layouts, setupName, device, paperSize, plotStyle, plotArea, window, scale, units, centered, orientation, noViewer }, dryRun, document: document, cancellationToken: cancellationToken);

    [McpServerTool(Name = "forge_entity_search_text", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("Search for text across DBText, MText (with formatting codes stripped), BlockReference attributes, Dimension measurement/override text, MLeader text, and Table cells. Optional filters: layer, 2D bounding window [[minX,minY],[maxX,maxY]], layout, caseSensitive, exactMatch, useRegex, maxResults (default 200). Returns matching handles, entity/source kinds, plain text, raw text, and WCS coordinates.")]
    public static Task<ForgeResult> EntitySearchText(
        ForgeToolRunner runner,
        string query,
        string? layer = null,
        double[][]? window = null,
        string? layout = null,
        bool caseSensitive = false,
        bool exactMatch = false,
        bool useRegex = false,
        int? maxResults = null,
        CancellationToken cancellationToken = default)
        => runner.InvokeAsync("forge_entity_search_text", new { query, layer, window, layout, caseSensitive, exactMatch, useRegex, maxResults }, cancellationToken: cancellationToken);

    [McpServerTool(Name = "forge_entity_snap", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("Compute exact OSNAP points near a query point [x,y] within radius: supports 'endpoint', 'midpoint', 'center', 'quadrant', 'insertion', 'nearest', 'perpendicular' (requires fromPoint [x,y]), and 'intersection' between nearby curves. Returns ranked snap candidates (exact geometry snaps prioritized before nearest) and bestSnap for precision CAD authoring.")]
    public static Task<ForgeResult> EntitySnap(
        ForgeToolRunner runner,
        double[] point,
        double? radius = null,
        double[]? fromPoint = null,
        string[]? snapTypes = null,
        string? layer = null,
        int? maxResults = null,
        CancellationToken cancellationToken = default)
        => runner.InvokeAsync("forge_entity_snap", new { point, radius, fromPoint, snapTypes, layer, maxResults }, cancellationToken: cancellationToken);

    [McpServerTool(Name = "forge_draw_grid", ReadOnly = false, Destructive = false, Idempotent = false, OpenWorld = false)]
    [Description("Draw a complete structural/architectural column grid (axis lines, grid bubbles with labels, bay + overall dimensions) and return a named intersection dictionary (e.g. 'A-1': [x,y]) for placing columns or walls. Provide either xPositions or bay xSpacings (and yPositions or ySpacings) relative to origin [x,y]. bubbleSides: 'start'|'end'|'both'|'none'.")]
    public static Task<ForgeResult> DrawGrid(
        ForgeToolRunner runner,
        double[]? origin = null,
        double[]? xPositions = null,
        double[]? xSpacings = null,
        string[]? xLabels = null,
        double[]? yPositions = null,
        double[]? ySpacings = null,
        string[]? yLabels = null,
        double? extensionLength = null,
        double? bubbleRadius = null,
        string? bubbleSides = "start",
        bool includeDimensions = true,
        string? layer = null,
        string? gridLayer = null,
        string? bubbleLayer = null,
        string? dimLayer = null,
        string? gridLinetype = null,
        string? dimStyle = null,
        int? colorIndex = null,
        bool dryRun = false,
        CancellationToken cancellationToken = default)
        => runner.InvokeAsync("forge_draw_grid", new { origin, xPositions, xSpacings, xLabels, yPositions, ySpacings, yLabels, extensionLength, bubbleRadius, bubbleSides, includeDimensions, layer, gridLayer, bubbleLayer, dimLayer, gridLinetype, dimStyle, colorIndex }, dryRun, cancellationToken: cancellationToken);

    [McpServerTool(Name = "forge_draw_wall", ReadOnly = false, Destructive = false, Idempotent = false, OpenWorld = false)]
    [Description("Draw a double-line architectural wall along a 2D centerline/reference path with clean mitered corners, end caps, optional door/window openings (each opening has segmentIndex, offsetFromStart, width — slices the wall with jamb end-caps and returns opening center + angleDeg for door/window block insertion), and optional associative hatch fill. justification: 'center'|'left'|'right'.")]
    public static Task<ForgeResult> DrawWall(
        ForgeToolRunner runner,
        double[][] vertices,
        double thickness,
        string? justification = "center",
        bool closed = false,
        WallOpeningDto[]? openings = null,
        string? hatchPattern = null,
        double? hatchScale = null,
        double hatchAngleDeg = 0,
        string? layer = null,
        string? hatchLayer = null,
        int? colorIndex = null,
        bool dryRun = false,
        CancellationToken cancellationToken = default)
        => runner.InvokeAsync("forge_draw_wall", new { vertices, thickness, justification, closed, openings, hatchPattern, hatchScale, hatchAngleDeg, layer, hatchLayer, colorIndex }, dryRun, cancellationToken: cancellationToken);
}

public sealed record CampaignEntryDto
{
    public string? Layout { get; init; }
    public string? BlockName { get; init; }
    public string? Handle { get; init; }
    public Dictionary<string, string> Attributes { get; init; } = new();
}

public sealed record BatchJobDto
{
    public string DwgPath { get; init; } = "";
    public string ScriptPath { get; init; } = "";
    public int? TimeoutSeconds { get; init; }
}

public sealed record DrawStepDto
{
    public string? Tool { get; init; }
    public double[]? Start { get; init; }
    public double[]? End { get; init; }
    public double[]? Corner1 { get; init; }
    public double[]? Corner2 { get; init; }
    public double[]? P1 { get; init; }
    public double[]? P2 { get; init; }
    public double[]? Vertex { get; init; }
    public double[]? DimLinePoint { get; init; }
    public double[][]? Vertices { get; init; }
    public double[][]? FitPoints { get; init; }
    public double[][]? Points { get; init; }
    public double[]? Bulges { get; init; }
    public bool Closed { get; init; }
    public double? ConstantWidth { get; init; }
    public double? FilletRadius { get; init; }
    public double? ChamferDistance1 { get; init; }
    public double? ChamferDistance2 { get; init; }
    public double[]? Center { get; init; }
    public double? Radius { get; init; }
    public double? MajorRadius { get; init; }
    public double? MinorRadius { get; init; }
    public double[]? MajorAxis { get; init; }
    public double? RadiusRatio { get; init; }
    public int? Sides { get; init; }
    public bool? Inscribed { get; init; }
    public string? Mode { get; init; }
    public double? StartAngleDeg { get; init; }
    public double? EndAngleDeg { get; init; }
    public int? Degree { get; init; }
    public double[]? StartTangent { get; init; }
    public double[]? EndTangent { get; init; }
    public double? ArcLength { get; init; }
    public double? BulgeMagnitude { get; init; }
    public double[]? Position { get; init; }
    public double? Height { get; init; }
    public double? Width { get; init; }
    public string? Text { get; init; }
    public double? TextHeight { get; init; }
    public string? AttachmentPoint { get; init; }
    public string? TextOverride { get; init; }
    public double? RotationDeg { get; init; }
    public double? AngleDeg { get; init; }
    public string? Pattern { get; init; }
    public double? Scale { get; init; }
    public string? Kind { get; init; }
    public bool? UseXAxis { get; init; }
    public string? DimStyle { get; init; }
    public double? DimScale { get; init; }
    public double? ArrowSize { get; init; }
    public bool? HasArrowHead { get; init; }
    public string? Name { get; init; }
    public string? BlockName { get; init; }
    public Dictionary<string, string>? Attributes { get; init; }
    public string? Layer { get; init; }
    public int? ColorIndex { get; init; }

    // Speed wave (2026-10-29): text style, background mask, multileader style / block content.
    public string? TextStyle { get; init; }
    public bool? BackgroundMask { get; init; }
    public double? MaskScale { get; init; }
    public string? LeaderStyle { get; init; }
    public string? ContentBlock { get; init; }
}

public sealed record BlockAttributeDefDto
{
    public string? Tag { get; init; }
    public string? Prompt { get; init; }
    public double Height { get; init; }
    public double[]? Position { get; init; }
    public bool Invisible { get; init; }
}
