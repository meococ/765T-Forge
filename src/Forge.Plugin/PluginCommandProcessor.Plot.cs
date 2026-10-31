using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.PlottingServices;
using Forge.Shared;
using AcPlotType = Autodesk.AutoCAD.DatabaseServices.PlotType;

namespace Forge.Plugin;

// forge_plot_to_pdf.
//
// Hang root cause: the PDF drivers carry a PC3 custom property View_New_File=TRUE ("open in viewer
// when done"). On a PC with no default PDF application Windows answers with an OpenWith dialog and
// AutoCAD stalls on "Sending sheet to plotter" until the plugin watchdog gives up. Forge now always
// plots through a clone of the requested PC3 ("765T-Forge <device> (no viewer).pc3", same Plotters
// folder) with that property forced to FALSE — whatever the original PC3 or profile says.
//
// "Tiny drawing in one corner" root cause: plotArea=layout keeps the layout's own 1:1 paper area;
// forcing a bigger sheet (the old default was ISO A1 for everything) left the layout content in the
// lower-left corner. A layout now keeps its own media by default; when a different sheet is asked
// for, the default area becomes extents + fit + centered, and every plot reports a coverage estimate
// with a warning when the content would come out small.
//
// The default engine is the PlotEngine API (PlotSettingsValidator + PlotInfoValidator), which never
// prompts; engine="command" keeps the legacy -PLOT token path (also on the no-viewer device).
public sealed partial class PluginCommandProcessor
{
    private const string DefaultPdfDevice = "DWG To PDF.pc3";
    private const string DefaultPngDevice = "PublishToWeb PNG.pc3";
    private const string DefaultModelPaper = "ISO A1 (841.00 x 594.00 MM)";
    private const int MaxExtentsScanEntities = 200_000;

    private sealed record ViewerSuppression(string State, string PlotDevice, string? SourcePc3, string? ClonePc3, string? Note)
    {
        public bool Failed => State == "failed";

        /// <summary>review-wp2-r2 Q3: the clone path only when this call wrote it — see <see cref="Pc3File.PreparedClonePath"/>.</summary>
        public string? Pc3Prepared => Pc3File.PreparedClonePath(State, Note, ClonePc3);
    }

    private static ForgeResult PlotToPdf(ForgeCommand command)
    {
        var args = Args<PlotArgs>(command);
        if (string.IsNullOrWhiteSpace(args.OutputPath))
        {
            return ForgeResult.Failure(command.Id, "missing_output_path", "outputPath is required.");
        }

        if (RejectControlChars(
                command,
                ("outputPath", args.OutputPath),
                ("layout", args.Layout),
                ("device", args.Device),
                ("paperSize", args.PaperSize),
                ("plotStyle", args.PlotStyle),
                ("plotArea", args.PlotArea),
                ("orientation", args.Orientation),
                ("scale", args.Scale),
                ("units", args.Units),
                ("engine", args.Engine)) is { } reject)
        {
            return reject;
        }

        var outputPath = Path.GetFullPath(args.OutputPath!);

        // TOOL-GAP #27: outputFormat=png routes the same guarded plot pipeline onto
        // PublishToWeb PNG.pc3 (or the caller's raster device). The pixel medium is
        // injected into the Forge clone pc3 at dpi — see TryResolveMedia's raster branch.
        var isPng = string.Equals(args.OutputFormat?.Trim(), "png", StringComparison.OrdinalIgnoreCase);
        if (!isPng && !string.IsNullOrWhiteSpace(args.OutputFormat) && !string.Equals(args.OutputFormat.Trim(), "pdf", StringComparison.OrdinalIgnoreCase))
        {
            return ForgeResult.Failure(command.Id, "invalid_output_format", $"Unknown outputFormat '{args.OutputFormat}'.", "Use \"pdf\" (default) or \"png\".");
        }

        var dpi = args.Dpi ?? 150;
        if (isPng && (dpi < 50 || dpi > 2400))
        {
            return ForgeResult.Failure(command.Id, "invalid_dpi", $"dpi {dpi} is out of range.", "Use a dpi between 50 and 2400 (default 150).");
        }

        if (isPng && !outputPath.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
        {
            outputPath = Path.ChangeExtension(outputPath, ".png");
        }

        var engine = string.IsNullOrWhiteSpace(args.Engine) ? "api" : args.Engine!.Trim().ToLowerInvariant();
        if (engine is not ("api" or "command"))
        {
            return ForgeResult.Failure(command.Id, "invalid_engine", $"Unknown plot engine '{args.Engine}'.", "Use engine=\"api\" (default) or engine=\"command\" (legacy -PLOT).");
        }

        if (isPng && engine == "command")
        {
            return ForgeResult.Failure(command.Id, "invalid_engine", "engine=\"command\" is not supported for outputFormat=\"png\".", "The legacy -PLOT token path answers paper units in mm/inches; raster devices plot in pixels. Use the default api engine.");
        }

        PlotAreaKind? requestedArea = null;
        if (!string.IsNullOrWhiteSpace(args.PlotArea))
        {
            if (!PlotLayoutMath.TryParseArea(args.PlotArea, out var parsedArea))
            {
                return ForgeResult.Failure(command.Id, "invalid_plot_area", $"Unknown plotArea '{args.PlotArea}'.", "Use layout, extents, display, limits, or window (with window=[[x1,y1],[x2,y2]]).");
            }

            requestedArea = parsedArea;
        }

        double[]? window = null;
        if (args.Window is { Length: > 0 })
        {
            if (!PlotLayoutMath.TryNormalizeWindow(args.Window, null, out var box, out var windowError))
            {
                return ForgeResult.Failure(command.Id, "invalid_window", windowError ?? "window is invalid.");
            }

            window = box;
            requestedArea ??= PlotAreaKind.Window;
            if (requestedArea != PlotAreaKind.Window)
            {
                return ForgeResult.Failure(command.Id, "invalid_window", "window is only used with plotArea=window.");
            }
        }
        else if (requestedArea == PlotAreaKind.Window)
        {
            return ForgeResult.Failure(command.Id, "missing_window", "plotArea=window requires window=[[x1,y1],[x2,y2]] in the plotted space's coordinates.");
        }

        if (!PlotLayoutMath.TryParseScale(args.Scale, out var scaleSpec))
        {
            return ForgeResult.Failure(command.Id, "invalid_scale", $"Unknown scale '{args.Scale}'.", "Use \"fit\" or paper:drawing units such as \"1:100\".");
        }

        var scaleRequested = !string.IsNullOrWhiteSpace(args.Scale);
        var orientationWord = string.IsNullOrWhiteSpace(args.Orientation) ? null : args.Orientation!.Trim().ToLowerInvariant();
        if (orientationWord is not null and not ("auto" or "landscape" or "portrait"))
        {
            return ForgeResult.Failure(command.Id, "invalid_orientation", $"Unknown orientation '{args.Orientation}'.", "Use auto, landscape, or portrait.");
        }

        PlotPaperUnit? requestedUnits = null;
        if (!string.IsNullOrWhiteSpace(args.Units))
        {
            requestedUnits = args.Units!.Trim().ToLowerInvariant() switch
            {
                "mm" or "millimeter" or "millimeters" or "millimetre" or "millimetres" => PlotPaperUnit.Millimeters,
                "in" or "inch" or "inches" => PlotPaperUnit.Inches,
                _ => null
            };
            if (requestedUnits is null)
            {
                return ForgeResult.Failure(command.Id, "invalid_units", $"Unknown units '{args.Units}'.", "Use millimeters or inches.");
            }
        }

        if (!command.DryRun && File.Exists(outputPath) && !args.OverwriteAcknowledged)
        {
            return ForgeResult.Failure(
                command.Id,
                "plot_overwrite_not_acknowledged",
                $"Refusing to overwrite existing {(isPng ? "PNG" : "PDF")}: {outputPath}",
                "Pass overwriteAcknowledged=true after confirming the previous issue PDF is archived.");
        }

        var doc = ActiveDoc;
        var db = doc.Database;
        var layoutName = string.IsNullOrWhiteSpace(args.Layout) ? LayoutManager.Current.CurrentLayout : args.Layout!.Trim();
        var layoutId = TryGetLayoutId(layoutName);
        if (layoutId.IsNull)
        {
            return ForgeResult.Failure(command.Id, "layout_not_found", $"Layout not found: {layoutName}", "Call forge_doc_list_layouts to see available layouts.");
        }

        var requestedDevice = NormalizeDeviceName(string.IsNullOrWhiteSpace(args.Device) ? (isPng ? DefaultPngDevice : DefaultPdfDevice) : args.Device!.Trim());
        var viewer = PrepareNoViewerDevice(requestedDevice, write: !command.DryRun);
        if (viewer.Failed)
        {
            return ForgeResult.Failure(
                command.Id,
                "plot_viewer_suppression_failed",
                $"Could not prepare a no-viewer copy of '{requestedDevice}': {viewer.Note}",
                "Nothing was plotted. A PDF plot that opens a viewer can hang AutoCAD on an OpenWith dialog; fix write access to the Plotters folder or pass a device whose PC3 has View_New_File=FALSE.",
                data: new { viewerSuppression = viewer });
        }

        // fix-smoke-20261029 D4: PlotInfoValidator only accepts the CURRENT layout (eLayoutNotCurrent in r1).
        // A real plot makes the target layout current, gates on the readback, plots, and restores the
        // previous layout in finally. A dry run never switches; it defers validation instead.
        bool targetIsModel;
        MediaPlan? media;
        using (var ltr = db.TransactionManager.StartOpenCloseTransaction())
        {
            var target = (Layout)ltr.GetObject(layoutId, OpenMode.ForRead);
            layoutName = target.LayoutName;
            targetIsModel = target.ModelType;
            // review-wp2-r1 finding 4 (D9): the stored-media-vs-device check and paperSize
            // resolution run here — while the layout is merely open, not current — so a typed
            // failure precedes the layout switch and leaves no side effect (no initialized
            // layout, DBMOD unchanged).
            if (TryResolveMedia(target, targetIsModel, requestedDevice, viewer, args, requestedUnits, command, out media) is { } mediaFailure)
            {
                ltr.Commit();
                return mediaFailure;
            }

            ltr.Commit();
        }

        var before = ReadLayoutState();
        var action = PlotLayoutMath.PlanLayoutSwitch(LayoutManager.Current.CurrentLayout, layoutName, targetIsModel, before.Tilemode, before.Cvport, command.DryRun);
        var request = new PlotRequest(command, args, outputPath, engine, requestedArea, window, scaleSpec, scaleRequested, orientationWord, layoutId, layoutName, requestedDevice, viewer, action == PlotLayoutAction.DeferValidation, media!);
        try
        {
            if (action is PlotLayoutAction.None or PlotLayoutAction.DeferValidation)
            {
                var plain = PlotOnLayout(request);
                return command.DryRun ? plain : AttachLayoutReport(plain, new PlotLayoutReport { DbmodBefore = before.Dbmod, DbmodAfter = ReadLayoutState().Dbmod });
            }

            var report = new PlotLayoutReport
            {
                Switched = true,
                SwitchKind = action == PlotLayoutAction.SwitchToPaperSpace ? "paper_space" : "layout",
                DbmodBefore = before.Dbmod,
                Before = before
            };
            string? switchError = null;
            var appended = new List<string>();
            void OnAppended(object? sender, ObjectEventArgs e)
            {
                try
                {
                    appended.Add(e.DBObject.Handle.ToString());
                }
                catch
                {
                    // Never throw into AutoCAD.
                }
            }

            ForgeResult result;
            try
            {
                db.ObjectAppended += OnAppended;
                try
                {
                    if (action == PlotLayoutAction.Switch)
                    {
                        LayoutManager.Current.CurrentLayout = layoutName;
                    }

                    if (!targetIsModel && ReadLayoutState().Cvport != 1)
                    {
                        doc.Editor.SwitchToPaperSpace();
                    }
                }
                catch (System.Exception ex)
                {
                    switchError = ex.Message;
                }
                finally
                {
                    db.ObjectAppended -= OnAppended;
                }

                report.Initialized = appended.Count > 0;
                report.AppendedCount = appended.Count;
                report.AppendedSample = appended.Take(50).ToArray();
                var now = ReadLayoutState();
                if (switchError is null && PlotLayoutMath.IsLayoutReady(layoutName, now.Ctab, LayoutManager.Current.CurrentLayout, targetIsModel, now.Tilemode, now.Cvport))
                {
                    result = PlotOnLayout(request);
                }
                else
                {
                    result = ForgeResult.Failure(
                        command.Id,
                        "plot_layout_not_current",
                        $"Layout '{layoutName}' could not be made current for plotting (CTAB={now.Ctab}, TILEMODE={now.Tilemode}, CVPORT={now.Cvport}{(switchError is null ? "" : "; " + switchError)}). Nothing was plotted.",
                        "Make the layout current in AutoCAD (and paper space active) and retry.");
                }
            }
            finally
            {
                RestoreLayout(doc, before, report);
            }

            report.DbmodAfter = ReadLayoutState().Dbmod;
            return AttachLayoutReport(result, report);
        }
        finally
        {
            // The cloned PlotSettings rides the request into PlotOnLayout but is owned here,
            // so the plot_layout_not_current early-out cannot leak it.
            media!.Settings.Dispose();
        }
    }

    private sealed record PlotRequest(
        ForgeCommand Command,
        PlotArgs Args,
        string OutputPath,
        string Engine,
        PlotAreaKind? RequestedArea,
        double[]? Window,
        PlotScaleSpec ScaleSpec,
        bool ScaleRequested,
        string? OrientationWord,
        ObjectId LayoutId,
        string LayoutName,
        string RequestedDevice,
        ViewerSuppression Viewer,
        bool DeferValidation,
        MediaPlan Media);

    /// <summary>The media/device outcome resolved before any layout switch (review-wp2-r1 finding 4, D9).</summary>
    private sealed record MediaPlan(
        PlotSettings Settings,
        string CanonicalMedia,
        string MediaSource,
        string? MappedFromMedia,
        (double WidthMm, double HeightMm)? StoredSheetMm,
        string LayoutMedia,
        bool MediaChanged,
        (int W, int H)? RasterPx,
        string? RasterDevice);

    /// <summary>
    /// review-wp2-r1 finding 4 (D9): device media list, stored-paper mapping and paperSize
    /// resolve on a cloned PlotSettings while the layout is only open (never current), so a
    /// typed failure — paper_size_not_found, plot_paper_not_on_device, plot_device_invalid —
    /// cannot leave behind a switched/initialized layout or a DBMOD bump. The returned
    /// settings object is not database-resident; ownership passes to the caller.
    /// </summary>
    private static ForgeResult? TryResolveMedia(
        Layout layout,
        bool isModel,
        string requestedDevice,
        ViewerSuppression viewer,
        PlotArgs args,
        PlotPaperUnit? requestedUnits,
        ForgeCommand command,
        out MediaPlan? plan)
    {
        plan = null;
        PlotSettings? ps = new PlotSettings(isModel);
        try
        {
            ps.CopyFrom(layout);
            var psv = PlotSettingsValidator.Current;
            var layoutMedia = layout.CanonicalMediaName;
            var layoutPaperUnits = layout.PlotPaperUnits;

            string canonicalMedia;
            string mediaSource;
            string? mappedFromMedia = null;
            (double WidthMm, double HeightMm)? storedSheetMm = null;

            // TOOL-GAP #27: a raster plot needs a pixel medium that no pc3 ships — rewrite
            // the stored user-defined size inside the Forge clone BEFORE the device is
            // registered, so the validator's media list exposes our Forge_WxHpx medium.
            var isPng = string.Equals(args.OutputFormat?.Trim(), "png", StringComparison.OrdinalIgnoreCase);
            var dpi = args.Dpi ?? 150;
            (int W, int H)? rasterPx = null;
            string? rasterDevice = null;
            if (isPng)
            {
                var sheetMm = isModel
                    ? (841.0, 594.0)
                    : (PaperMatch.ParseSizeMm(layoutMedia) ?? StoredSheetMm(layout) ?? (841.0, 594.0));
                storedSheetMm = sheetMm;
                rasterPx = (RasterPc3.MmToPixels(sheetMm.Item1, dpi), RasterPc3.MmToPixels(sheetMm.Item2, dpi));
                if ((long)rasterPx.Value.W * rasterPx.Value.H > 1_000_000_000)
                {
                    return ForgeResult.Failure(
                        command.Id,
                        "invalid_dpi",
                        $"Raster target {rasterPx.Value.W}x{rasterPx.Value.H}px exceeds a billion pixels.",
                        "Lower dpi or use outputFormat=\"pdf\" for large sheets.");
                }

                if (!command.DryRun)
                {
                    var rasterPc3Path = viewer.ClonePc3;
                    if (rasterPc3Path is null)
                    {
                        // A device that never opens a viewer produces no no-viewer clone —
                        // the raster patch still needs a writable Forge pc3, so clone the
                        // source ourselves under the same naming convention.
                        if (string.IsNullOrWhiteSpace(viewer.SourcePc3) || !File.Exists(viewer.SourcePc3))
                        {
                            return ForgeResult.Failure(
                                command.Id,
                                "plot_raster_pc3_unreadable",
                                $"Raster plotting needs a writable pc3 clone but '{requestedDevice}' has no readable source pc3.",
                                "Pass a file-based raster device such as PublishToWeb PNG.pc3.",
                                data: new { viewerSuppression = viewer });
                        }

                        rasterPc3Path = Path.Combine(
                            Path.GetDirectoryName(viewer.SourcePc3)!,
                            $"765T-Forge {Path.GetFileNameWithoutExtension(viewer.SourcePc3)} (no viewer).pc3");
                        try
                        {
                            File.Copy(viewer.SourcePc3, rasterPc3Path, overwrite: true);
                            // a freshly dropped pc3 is invisible until the device list
                            // refreshes — same proof PrepareNoViewerDevice runs on its clone.
                            PlotConfigManager.RefreshList(RefreshCode.All);
                            var listed = psv.GetPlotDeviceList()
                                .Cast<string>()
                                .Any(d => string.Equals(d, Path.GetFileName(rasterPc3Path), StringComparison.OrdinalIgnoreCase));
                            if (!listed)
                            {
                                return ForgeResult.Failure(
                                    command.Id,
                                    "plot_raster_pc3_unreadable",
                                    $"The raster clone '{rasterPc3Path}' was written but AutoCAD does not list it as a plot device.",
                                    "Check the Plotters folder and AutoCAD's printer support paths, then retry.",
                                    data: new { viewerSuppression = viewer });
                            }
                        }
                        catch (System.Exception ex)
                        {
                            return ForgeResult.Failure(
                                command.Id,
                                "plot_raster_pc3_unreadable",
                                $"Could not prepare the raster pc3 clone: {ex.Message}",
                                "Check the Plotters folder is writable and retry.",
                                data: new { viewerSuppression = viewer });
                        }
                    }

                    // Device names keep the .pc3 suffix (GetPlotDeviceList entries do).
                    rasterDevice = Path.GetFileName(rasterPc3Path);
                    string? rasterIoError = null;
                    try
                    {
                        if (!Pc3File.TryRead(File.ReadAllBytes(rasterPc3Path), out var pc3Text, out var pc3Header))
                        {
                            rasterIoError = $"could not decode '{rasterPc3Path}'";
                        }
                        else
                        {
                            var patched = RasterPc3.Apply(pc3Text, rasterPx.Value.W, rasterPx.Value.H, dpi, out var rasterError);
                            if (rasterError is not null)
                            {
                                return ForgeResult.Failure(
                                    command.Id,
                                    "plot_raster_pc3",
                                    $"Raster medium injection failed on '{rasterPc3Path}': {rasterError}",
                                    "Pass a device whose pc3 stores a user-defined raster medium (PublishToWeb PNG.pc3).",
                                    data: new { rasterError, viewerSuppression = viewer });
                            }

                            File.WriteAllBytes(rasterPc3Path, Pc3File.Write(patched, pc3Header));
                            if (!Pc3File.TryRead(File.ReadAllBytes(rasterPc3Path), out var verifyText, out _)
                                || !RasterPc3.Verify(verifyText, rasterPx.Value.W, rasterPx.Value.H, dpi))
                            {
                                rasterIoError = "did not read back with the expected medium/dpi";
                            }
                        }
                    }
                    catch (System.Exception ex)
                    {
                        rasterIoError = ex.Message;
                    }

                    if (rasterIoError is not null)
                    {
                        return ForgeResult.Failure(
                            command.Id,
                            "plot_raster_pc3_verify",
                            $"The raster pc3 '{rasterPc3Path}' could not be prepared: {rasterIoError}",
                            "Check the Plotters folder is writable and retry.",
                            data: new { viewerSuppression = viewer });
                    }
                }
            }

            psv.SetPlotConfigurationName(ps, rasterDevice ?? viewer.PlotDevice, null);
            psv.RefreshLists(ps);
            var mediaList = psv.GetCanonicalMediaNameList(ps)
                .Cast<string>()
                .Select(canonical => (Canonical: canonical, Locale: SafeLocaleMediaName(psv, ps, canonical)))
                .ToList();
            var deviceMedia = mediaList.Select(m => PaperMatch.ToMedia(m.Canonical, m.Locale)).ToArray();

            if (isPng && rasterPx is { } px)
            {
                var injected = RasterPc3.MediaName(px.W, px.H);
                var match = mediaList.FirstOrDefault(m => string.Equals(m.Canonical, injected, StringComparison.OrdinalIgnoreCase));
                if (match.Canonical is not null)
                {
                    canonicalMedia = match.Canonical;
                    mediaSource = "raster";
                }
                else if (command.DryRun)
                {
                    // dry-run never writes the clone pc3, so the injected medium cannot
                    // appear in the list — the plan is reported, validation is deferred.
                    canonicalMedia = injected;
                    mediaSource = "raster_planned";
                }
                else
                {
                    return ForgeResult.Failure(
                        command.Id,
                        "plot_raster_media_unresolved",
                        $"The patched raster medium '{injected}' is not in '{requestedDevice}'s media list.",
                        "Inspect the device media names listed under candidates; the pc3 patch may not have taken effect.",
                        data: new { requestedMedia = injected, candidates = mediaList.Select(m => m.Canonical).Take(60).ToArray(), viewerSuppression = viewer });
                }
            }
            else if (!string.IsNullOrWhiteSpace(args.PaperSize))
            {
                var resolved = PlotLayoutMath.ResolveMedia(args.PaperSize, mediaList);
                if (resolved is null)
                {
                    return ForgeResult.Failure(
                        command.Id,
                        "paper_size_not_found",
                        $"Device '{requestedDevice}' has no paper size matching '{args.PaperSize}'.",
                        "Pass one of the candidates (localized or canonical name).",
                        // review-wp2-r2 Q3: the no-viewer clone may already be written (required
                        // ordering) — pc3Prepared names the file when this call created it.
                        data: new { device = requestedDevice, candidates = MediaCandidates(args.PaperSize!, mediaList), pc3Prepared = viewer.Pc3Prepared, viewerSuppression = viewer });
                }

                canonicalMedia = resolved;
                mediaSource = "requested";
            }
            else if (!isModel && mediaList.Any(m => string.Equals(m.Canonical, layoutMedia, StringComparison.Ordinal)))
            {
                canonicalMedia = layoutMedia;
                mediaSource = "layout";
            }
            else if (!isModel)
            {
                // fix-smoke-20261029-r2 D9: the stored paper name is not offered by this device
                // (custom media like ezdxf_(841.00_x_594.00_MM), or an imperial sheet name).
                // Map the stored sheet size onto a device medium of equal size instead of
                // dying as eInvalidInput; no match or an ambiguous one fails closed.
                storedSheetMm = PaperMatch.ParseSizeMm(layoutMedia) ?? StoredSheetMm(layout);
                var paperMatch = PaperMatch.MatchBySize(storedSheetMm?.WidthMm, storedSheetMm?.HeightMm, deviceMedia);
                if (!paperMatch.Matched)
                {
                    var sizeText = storedSheetMm is { } sz ? $"{sz.WidthMm:0.###} x {sz.HeightMm:0.###} mm" : "unknown size";
                    return ForgeResult.Failure(
                        command.Id,
                        PaperMatch.NotOnDeviceCode,
                        paperMatch.Ambiguous
                            ? $"Layout paper '{layoutMedia}' ({sizeText}) maps to more than one media on '{requestedDevice}' ({string.Join(", ", paperMatch.Nearest)}); refusing to guess."
                            : $"Layout paper '{layoutMedia}' ({sizeText}) is not a media on '{requestedDevice}'. Nearest by size: {string.Join(", ", paperMatch.Nearest)}.",
                        "Pass paperSize with one of the listed device media names (forge_system_capabilities.plotDevices).",
                        data: new
                        {
                            device = requestedDevice,
                            storedMedia = layoutMedia,
                            storedSizeMm = storedSheetMm is { } ss ? new[] { ss.WidthMm, ss.HeightMm } : null,
                            paperMatch.Ambiguous,
                            nearest = paperMatch.Nearest,
                            // review-wp2-r2 Q3: the no-viewer clone may already be written
                            // (required ordering) — pc3Prepared names the file when this call created it.
                            pc3Prepared = viewer.Pc3Prepared,
                            viewerSuppression = viewer
                        });
                }

                canonicalMedia = paperMatch.Canonical!;
                mappedFromMedia = layoutMedia;
                mediaSource = "mapped_to_device_media";
            }
            else
            {
                var a1 = PlotLayoutMath.ResolveMedia(DefaultModelPaper, mediaList);
                if (a1 is null)
                {
                    psv.SetClosestMediaName(ps, 841, 594, PlotPaperUnit.Millimeters, true);
                    canonicalMedia = ps.CanonicalMediaName;
                }
                else
                {
                    canonicalMedia = a1;
                }

                mediaSource = "default_a1";
            }

            // raster_planned (dry-run PNG): the injected medium cannot exist on the
            // unpatched device, so SetCanonicalMediaName would throw eInvalidInput —
            // the copied layout settings stay on ps and the plan reports the px dims.
            if (mediaSource != "raster_planned")
            {
                psv.SetCanonicalMediaName(ps, canonicalMedia);
                var units = isPng
                    ? PlotPaperUnit.Pixels
                    : requestedUnits
                        ?? (mediaSource == "layout" ? layoutPaperUnits : GuessPaperUnits(canonicalMedia, layoutPaperUnits));
                psv.SetPlotPaperUnits(ps, units);
            }

            plan = new MediaPlan(
                ps,
                canonicalMedia,
                mediaSource,
                mappedFromMedia,
                storedSheetMm,
                layoutMedia,
                !isPng && !string.Equals(canonicalMedia, layoutMedia, StringComparison.Ordinal),
                rasterPx,
                rasterDevice);
            ps = null;
            return null;
        }
        catch (Autodesk.AutoCAD.Runtime.Exception ex)
        {
            // review-wp2-r2 Q3: same pre-failure side effect — pc3Prepared names the clone
            // file when this call already wrote it.
            return ForgeResult.Failure(
                command.Id,
                "plot_device_invalid",
                $"Device/paper setup failed: {ex.Message}",
                "Check the device name with forge_system_capabilities.",
                data: new { device = requestedDevice, pc3Prepared = viewer.Pc3Prepared, viewerSuppression = viewer });
        }
        finally
        {
            ps?.Dispose();
        }
    }

    /// <summary>Plots the layout (already made current, or a dry run). Everything after the D4 layout switch.</summary>
    private static ForgeResult PlotOnLayout(PlotRequest request)
    {
        var (command, args, outputPath, engine, requestedArea, window, scaleSpec, scaleRequested, orientationWord, layoutId, _, requestedDevice, viewer, deferValidation, media) = request;
        var layoutName = request.LayoutName;
        var doc = ActiveDoc;
        var db = doc.Database;
        var warnings = new List<string>();
        using var tr = db.TransactionManager.StartTransaction();
        var layout = (Layout)tr.GetObject(layoutId, OpenMode.ForRead);
        layoutName = layout.LayoutName;
        var isModel = layout.ModelType;

        // TOOL-GAP #26: a loaded underlay whose file does not resolve plots silently without it —
        // a PDF-only sheet can issue blank while the probe still passes (V-101). Every underlay
        // definition is checked in the open transaction before any plot work (incl. UpdateExt).
        var underlays = EnumerateUnderlayDefinitions(tr, db);
        if (UnderlayPreflightFailure(command, underlays) is { } underlayFailure)
        {
            tr.Commit();
            return underlayFailure;
        }

        var layoutMedia = media.LayoutMedia;

        // review-wp2-r1 finding 4 (D9): media, device and paper units were resolved before any
        // layout switch; this settings object already carries canonicalMedia + paper units.
        // Disposed by PlotToPdf (it is also needed on paths that never reach this method).
        var ps = media.Settings;
        var psv = PlotSettingsValidator.Current;
        var canonicalMedia = media.CanonicalMedia;
        var mediaSource = media.MediaSource;
        var mappedFromMedia = media.MappedFromMedia;
        var storedSheetMm = media.StoredSheetMm;
        var mediaChanged = media.MediaChanged;
        var rasterPx = media.RasterPx;
        var isPng = string.Equals(args.OutputFormat?.Trim(), "png", StringComparison.OrdinalIgnoreCase);
        var rasterDpi = args.Dpi ?? 150;
        var plotDevice = media.RasterDevice ?? viewer.PlotDevice;

        if (isPng)
        {
            if (!string.IsNullOrWhiteSpace(args.PaperSize))
            {
                warnings.Add("paperSize is ignored for outputFormat=png; the raster medium is derived from the sheet size at dpi.");
            }

            if (!string.IsNullOrWhiteSpace(args.Units))
            {
                warnings.Add("units is ignored for outputFormat=png; raster plots use pixel paper units.");
            }
        }

        if (mappedFromMedia is not null)
        {
            warnings.Add(
                $"Layout paper '{mappedFromMedia}' is not offered by '{requestedDevice}'; plotted on '{canonicalMedia}' with the same sheet size" +
                (storedSheetMm is { } sm ? $" ({sm.WidthMm:0.###} x {sm.HeightMm:0.###} mm)." : "."));
        }

        // fix-smoke-20261029-r2 D12: with no caller plotArea the layout's OWN stored plot type
        // wins (client sheets store Window + a stored window; the old default silently plotted
        // "layout" and produced an empty PDF). Stored type/window/view read is best-effort —
        // an unreadable stored setup falls back to the previous defaults.
        StoredPlotType? storedPlotType = null;
        double[]? storedWindowBox = null;
        string? storedViewName = null;
        if (!isModel)
        {
            try
            {
                // live-smoke-20261030 fix4 (D12): layout.PlotType is the managed, 0-based
                // Autodesk.AutoCAD.DatabaseServices.PlotType — NOT the 1-based ActiveX
                // AcPlotType. Convert member-by-member by name; never cast it to int.
                storedPlotType = layout.PlotType switch
                {
                    AcPlotType.Display => StoredPlotType.Display,
                    AcPlotType.Extents => StoredPlotType.Extents,
                    AcPlotType.Limits => StoredPlotType.Limits,
                    AcPlotType.View => StoredPlotType.View,
                    AcPlotType.Window => StoredPlotType.Window,
                    AcPlotType.Layout => StoredPlotType.Layout,
                    _ => null
                };
                var wa = layout.PlotWindowArea;
                storedWindowBox = [wa.MinPoint.X, wa.MinPoint.Y, wa.MaxPoint.X, wa.MaxPoint.Y];
                storedViewName = layout.PlotViewName;
            }
            catch
            {
                // Unreadable stored plot settings: the area resolution below uses defaults.
            }
        }

        var resolution = PlotLayoutMath.ResolveArea(requestedArea, isModel, storedPlotType, storedWindowBox, storedViewName, mediaChanged);
        var area = resolution.Area;
        var areaSource = resolution.Source;
        if (area == PlotAreaKind.Window && window is null)
        {
            window = resolution.Window;
        }

        if (area == PlotAreaKind.Layout && isModel)
        {
            tr.Commit();
            return ForgeResult.Failure(command.Id, "invalid_plot_area", "plotArea=layout is only valid for paper-space layouts.", "Use extents, display, limits or window for Model.");
        }

        if (area == PlotAreaKind.Layout && mediaChanged && requestedArea == PlotAreaKind.Layout)
        {
            warnings.Add($"plotArea=layout on a different sheet than the layout's own ({layoutMedia} -> {canonicalMedia}) keeps the layout at its own scale; the drawing may not fill the sheet. Prefer plotArea=extents with scale=fit.");
        }

        // fix-smoke-20261029 D5: EXTMIN/EXTMAX only grow, so an extents plot of a stale header plotted empty
        // sheet space in r1. A real extents plot refreshes the header from the geometry first (as ZOOM
        // Extents does; a header write, so DBMOD is not hidden). A dry run never writes.
        var headerExtentsBefore = HeaderExtents(db, isModel);
        double[]? headerExtentsAfter = null;
        bool? headerExtentsRefreshed = null;
        string? headerExtentsRefresh = null;
        if (area == PlotAreaKind.Extents)
        {
            if (command.DryRun)
            {
                headerExtentsRefresh = "The real plot refreshes EXTMIN/EXTMAX from the geometry (UpdateExt) before plotting.";
            }
            else
            {
                try
                {
                    db.UpdateExt(true);
                    headerExtentsRefreshed = true;
                }
                catch (Autodesk.AutoCAD.Runtime.Exception ex)
                {
                    headerExtentsRefreshed = false;
                    warnings.Add($"Header extents could not be refreshed ({ex.ErrorStatus}); the extents plot may include stale empty space.");
                }

                headerExtentsAfter = HeaderExtents(db, isModel);
            }
        }

        var content = ComputeContentBox(tr, db, layout, isModel, out var contentSource);
        var areaBox = area switch
        {
            PlotAreaKind.Extents => content,
            PlotAreaKind.Window => window,
            PlotAreaKind.Limits => isModel
                ? [db.Limmin.X, db.Limmin.Y, db.Limmax.X, db.Limmax.Y]
                : [layout.Limits.MinPoint.X, layout.Limits.MinPoint.Y, layout.Limits.MaxPoint.X, layout.Limits.MaxPoint.Y],
            PlotAreaKind.Display => TryCurrentViewBox(layoutName),
            _ => null
        };

        // review-wp2-r1 finding 6 (D12): a stored page setup keeps its own centring/offset —
        // CopyFrom already carried it into ps — unless the caller passed centered explicitly.
        // review-wp2-r2 M1: centring (KeepStored included) also drives the legacy -PLOT path.
        var centring = PlotCentring.Resolve(args.Centered, areaSource, ps.PlotCentered);
        int rotationDeg;
        try
        {
            switch (area)
            {
                case PlotAreaKind.Window:
                    psv.SetPlotWindowArea(ps, new Extents2d(window![0], window[1], window[2], window[3]));
                    psv.SetPlotType(ps, AcPlotType.Window);
                    break;
                case PlotAreaKind.Extents:
                    psv.SetPlotType(ps, AcPlotType.Extents);
                    break;
                case PlotAreaKind.Display:
                    psv.SetPlotType(ps, AcPlotType.Display);
                    break;
                case PlotAreaKind.Limits:
                    psv.SetPlotType(ps, AcPlotType.Limits);
                    break;
                case PlotAreaKind.View:
                    // D12: a stored named view — ps.PlotViewName already holds the layout's
                    // stored view name via CopyFrom.
                    psv.SetPlotType(ps, AcPlotType.View);
                    break;
                default:
                    psv.SetPlotType(ps, AcPlotType.Layout);
                    break;
            }

            var paper = ps.PlotPaperSize;
            if (area is PlotAreaKind.Layout or PlotAreaKind.View && orientationWord is null)
            {
                rotationDeg = RotationDegrees(ps.PlotRotation);
            }
            else
            {
                double areaW, areaH;
                if (area == PlotAreaKind.Layout)
                {
                    (areaW, areaH) = (layout.PlotPaperSize.X, layout.PlotPaperSize.Y);
                    if (RotationDegrees(layout.PlotRotation) % 180 != 0)
                    {
                        (areaW, areaH) = (areaH, areaW);
                    }
                }
                else if (areaBox is { Length: 4 })
                {
                    (areaW, areaH) = (areaBox[2] - areaBox[0], areaBox[3] - areaBox[1]);
                }
                else
                {
                    (areaW, areaH) = (Math.Sqrt(2), 1.0);
                    warnings.Add("Plot area size is unknown before plotting; orientation defaulted to landscape.");
                }

                rotationDeg = PlotLayoutMath.ChooseRotation(orientationWord ?? "auto", paper.X, paper.Y, areaW, areaH) ?? 0;
                psv.SetPlotRotation(ps, rotationDeg == 90 ? PlotRotation.Degrees090 : PlotRotation.Degrees000);
            }

            if (area is PlotAreaKind.Layout or PlotAreaKind.View)
            {
                // Layout and stored named views plot at the layout's own (stored) scale.
                if (scaleRequested && !scaleSpec.Fit)
                {
                    psv.SetUseStandardScale(ps, false);
                    psv.SetCustomPrintScale(ps, new CustomScale(scaleSpec.PaperUnits, scaleSpec.DrawingUnits));
                }
                else if (scaleRequested)
                {
                    warnings.Add($"scale=fit does not apply to plotArea={area.ToString().ToLowerInvariant()}; it plots at its own scale. Use plotArea=extents for fit-to-sheet.");
                }

                // live-smoke-20261030 fix4-r4 (review F1): an explicit centered is dropped for
                // Layout/View on BOTH engines — api skips the SetPlotCentered/SetPlotOrigin
                // writes below and command answers Enter at the offset prompt. Warn like the
                // scale=fit one above so the caller knows the argument did nothing.
                if (PlotCentring.CenteredIgnoredWarning(args.Centered, area) is { } centeredWarning)
                {
                    warnings.Add(centeredWarning);
                }
            }
            else
            {
                if (scaleSpec.Fit)
                {
                    psv.SetUseStandardScale(ps, true);
                    psv.SetStdScaleType(ps, StdScaleType.ScaleToFit);
                }
                else
                {
                    psv.SetUseStandardScale(ps, false);
                    psv.SetCustomPrintScale(ps, new CustomScale(scaleSpec.PaperUnits, scaleSpec.DrawingUnits));
                }

                if (!centring.KeepStored)
                {
                    psv.SetPlotCentered(ps, centring.Centered);
                    if (!centring.Centered)
                    {
                        psv.SetPlotOrigin(ps, Point2d.Origin);
                    }
                }
            }

            if (!string.IsNullOrWhiteSpace(args.PlotStyle))
            {
                var wanted = args.PlotStyle!.Trim();
                if (wanted == ".")
                {
                    psv.SetCurrentStyleSheet(ps, "");
                }
                else
                {
                    var sheets = psv.GetPlotStyleSheetList().Cast<string>().ToList();
                    var match = sheets.FirstOrDefault(s => string.Equals(s, wanted, StringComparison.OrdinalIgnoreCase))
                                ?? sheets.FirstOrDefault(s => string.Equals(Path.GetFileNameWithoutExtension(s), wanted, StringComparison.OrdinalIgnoreCase));
                    if (match is null)
                    {
                        tr.Commit();
                        return ForgeResult.Failure(
                            command.Id,
                            "plot_style_not_found",
                            $"Plot style table not found: {wanted}",
                            "Pass one of the available plot style tables, or \".\" for none.",
                            data: new { candidates = sheets.Take(60).ToArray() });
                    }

                    psv.SetCurrentStyleSheet(ps, match);
                    ps.PlotPlotStyles = true;
                }
            }
        }
        catch (Autodesk.AutoCAD.Runtime.Exception ex)
        {
            tr.Commit();
            return ForgeResult.Failure(command.Id, "plot_settings_invalid", $"Plot settings were rejected by AutoCAD: {ex.Message}", "Check plotArea/window/scale/orientation; window coordinates are in the plotted space.");
        }

        var info = new PlotInfo { Layout = layoutId, OverrideSettings = ps };
        PlotSettings validated;
        string validation;
        if (deferValidation)
        {
            // D4: a dry run never switches layouts and the validator rejects a layout that is not current,
            // so the plan is built from the unvalidated settings.
            validated = ps;
            validation = "deferred";
            warnings.Add($"Validation deferred: layout '{layoutName}' is not current and a dry run does not switch to it. The real plot switches to it, validates, plots and switches back.");
        }
        else
        {
            try
            {
                var validator = new PlotInfoValidator { MediaMatchingPolicy = MatchingPolicy.MatchEnabled };
                validator.Validate(info);
                validated = info.ValidatedSettings;
                validation = "passed";
            }
            catch (Autodesk.AutoCAD.Runtime.Exception ex)
            {
                tr.Commit();
                return ForgeResult.Failure(command.Id, "plot_validation_failed", $"AutoCAD rejected the plot settings: {ex.Message}", "Try another paperSize or plotArea=extents.");
            }
        }

        var readBack = DescribeSettings(psv, validated);
        // Raster media report pixels: px * (25.4/dpi) converts to the mm the fit/coverage math expects.
        var paperUnitsPerMm = validated.PlotPaperUnits switch
        {
            PlotPaperUnit.Inches => 1 / 25.4,
            PlotPaperUnit.Pixels => 25.4 / rasterDpi,
            _ => 1.0
        };
        var printW = readBack.PrintableMm[0] * paperUnitsPerMm;
        var printH = readBack.PrintableMm[1] * paperUnitsPerMm;

        PlotFitResult? fit = null;
        PlotCoverage? coverage = null;
        if (area != PlotAreaKind.Layout && areaBox is { Length: 4 })
        {
            // raster custom scales are in pixel units — convert to mm-equivalent so the fit math stays consistent
            var fitScale = isPng && !scaleSpec.Fit
                ? scaleSpec with { PaperUnits = scaleSpec.PaperUnits * 25.4 / rasterDpi }
                : scaleSpec;
            fit = PlotLayoutMath.ComputeFit(areaBox[2] - areaBox[0], areaBox[3] - areaBox[1], printW, printH, readBack.RotationDeg, fitScale);
            if (content is { Length: 4 })
            {
                coverage = PlotLayoutMath.ComputeCoverage(areaBox, content);
            }
        }
        else if (area is PlotAreaKind.Layout or PlotAreaKind.View && content is { Length: 4 })
        {
            var rotated = readBack.RotationDeg % 180 != 0;
            var layoutFactor = readBack.UsesFit || readBack.CustomScale is not { } cs || cs[0] <= 0 || cs[1] <= 0 ? 1.0 : cs[0] / cs[1];
            var aw = (rotated ? printH : printW) / layoutFactor;
            var ah = (rotated ? printW : printH) / layoutFactor;
            coverage = PlotLayoutMath.ComputeCoverage([0, 0, aw, ah], [0, 0, content[2] - content[0], content[3] - content[1]]);
        }

        if (coverage?.Warning is { } coverageWarning)
        {
            warnings.Add(coverageWarning);
        }

        var plan = new
        {
            outputPath,
            layout = layoutName,
            isModel,
            engine,
            device = requestedDevice,
            plotDevice,
            viewerSuppression = viewer,
            paperSize = readBack.LocaleMedia,
            canonicalMedia = mediaSource is "raster" or "raster_planned" ? canonicalMedia : readBack.CanonicalMedia,
            outputFormat = isPng ? "png" : "pdf",
            dpi = isPng ? rasterDpi : (int?)null,
            rasterPixels = rasterPx is { } rp ? new[] { rp.W, rp.H } : null,
            mediaSource,
            mediaMapped = mappedFromMedia is null ? null : new { from = mappedFromMedia, to = canonicalMedia },
            layoutMedia,
            paperMm = readBack.PaperMm,
            printableMm = readBack.PrintableMm,
            paperUnits = validated.PlotPaperUnits.ToString(),
            plotArea = area.ToString().ToLowerInvariant(),
            plotAreaSource = areaSource,
            storedPlotType = storedPlotType?.ToString(),
            storedViewName,
            window,
            plotAreaBox = areaBox,
            contentBox = content,
            contentSource,
            validation,
            headerExtentsBefore,
            headerExtentsAfter,
            headerExtentsRefreshed,
            headerExtentsRefresh,
            rotationDeg = readBack.RotationDeg,
            orientation = readBack.Orientation,
            scale = readBack.UsesFit ? "fit" : readBack.CustomScale is { } s ? $"{s[0].ToString("0.######", CultureInfo.InvariantCulture)}:{s[1].ToString("0.######", CultureInfo.InvariantCulture)}" : "layout",
            centered = readBack.Centered,
            plotStyle = readBack.StyleSheet,
            fitEstimate = fit,
            coverageEstimate = coverage,
            underlayCount = underlays.Count,
            underlaysResolved = true,
            underlays,
            warnings
        };

        if (command.DryRun)
        {
            tr.Commit();
            return DryRun(command, plan);
        }

        if (PlotFactory.ProcessPlotState != ProcessPlotState.NotPlotting)
        {
            tr.Commit();
            return ForgeResult.Failure(command.Id, "plot_engine_busy", "Another plot or publish job is running in this AutoCAD session.", "Wait for it to finish (forge_system_health), then retry.");
        }

        var plotStartedUtc = DateTimeOffset.UtcNow;
        var previousBgPlot = Application.GetSystemVariable("BACKGROUNDPLOT");
        Application.SetSystemVariable("BACKGROUNDPLOT", 0);
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
            if (engine == "api")
            {
                RunPlotEngine(doc, info, outputPath);
            }
            else
            {
                RunLegacyPlotCommand(layoutName, isModel, viewer.PlotDevice, readBack, area, window, storedViewName, centring, args.PlotStyle, outputPath);
            }
        }
        catch (System.Exception ex)
        {
            return ForgeResult.Failure(
                command.Id,
                "plot_failed",
                $"Plot failed ({engine}): {ex.Message}",
                "Verify device/paper/plot style via forge_system_capabilities and that the layout is plottable; engine=\"command\" is the legacy fallback.",
                data: plan);
        }
        finally
        {
            Application.SetSystemVariable("BACKGROUNDPLOT", previousBgPlot);
            tr.Commit();
        }

        if (!File.Exists(outputPath))
        {
            return ForgeResult.Failure(command.Id, "plot_no_output", $"Plot ran but no output was produced at {outputPath}.", "Check the output directory is writable and the layout has plottable content.", data: plan);
        }

        // TOOL-GAP #27: raster plots verify against the PNG signature probe; the PDF
        // probe/ink/coverage path below stays the pdf branch.
        if (isPng)
        {
            var pngProbe = PngProbeResult.Probe(outputPath, notBeforeUtc: plotStartedUtc, expectedW: rasterPx?.W, expectedH: rasterPx?.H);
            var pngVerification = new ForgeVerification { Attempted = true, Passed = pngProbe.Passed, Message = pngProbe.Message, ReadBack = pngProbe };
            var pngPayload = new
            {
                plan.outputPath,
                plan.layout,
                plan.engine,
                plan.device,
                plan.plotDevice,
                plan.viewerSuppression,
                plan.outputFormat,
                plan.dpi,
                plan.rasterPixels,
                plan.canonicalMedia,
                plan.mediaSource,
                plan.mediaMapped,
                plotArea = plan.plotArea,
                plan.plotAreaSource,
                plan.window,
                plan.rotationDeg,
                plan.orientation,
                plan.scale,
                plan.centered,
                plan.plotStyle,
                plan.validation,
                plan.underlayCount,
                plan.underlaysResolved,
                plan.underlays,
                plan.warnings,
                bytes = pngProbe.Bytes,
                pngProbe
            };

            return pngProbe.Passed
                ? ForgeResult.Success(command.Id, pngPayload, verification: pngVerification)
                : ForgeResult.Failure(
                    command.Id,
                    "plot_png_probe",
                    "Plot wrote a file but the PNG probe failed.",
                    "Inspect data.pngProbe. Do not treat this output as published.",
                    data: pngPayload,
                    verification: pngVerification);
        }

        var probe = PdfProbeResult.Probe(outputPath, expectedPages: 1, notBeforeUtc: plotStartedUtc);
        double[]? pdfSheetMm = null;
        try
        {
            if (PlotLayoutMath.TryReadFirstMediaBox(File.ReadAllBytes(outputPath), out var wMm, out var hMm))
            {
                pdfSheetMm = [wMm, hMm];
            }
        }
        catch
        {
            // Read-back of the sheet size is best-effort; the probe above is the gate.
        }

        // fix-smoke-20261029 D5: coverage and looksTiny come from the PDF itself, not from the plan.
        var pdfContent = probe.Passed ? PdfContentBox.Read(outputPath) : null;
        var coverageDecision = pdfContent is null
            ? null
            : PlotPdfGate.DecideCoverage(
                area,
                scaleSpec.Fit,
                coverage?.LooksTiny,
                content is { Length: 4 },
                pdfContent.Status switch { PdfInkStatus.Ok => "ok", PdfInkStatus.Empty => "empty", _ => "unavailable" },
                pdfContent.LooksTiny == true,
                pdfContent.FillX,
                pdfContent.FillY,
                pdfContent.Reason);
        if (coverageDecision?.Warning is { } pdfCoverageWarning)
        {
            warnings.Add(pdfCoverageWarning);
        }

        return PlotPdfGate.FromProbe(
            command.Id,
            probe,
            new
            {
                plan.outputPath,
                plan.layout,
                plan.engine,
                plan.device,
                plan.plotDevice,
                plan.viewerSuppression,
                plan.paperSize,
                plan.canonicalMedia,
                plan.mediaSource,
                plan.mediaMapped,
                plan.paperMm,
                plan.printableMm,
                pdfSheetMm,
                plan.plotArea,
                plan.plotAreaSource,
                plan.window,
                plan.rotationDeg,
                plan.orientation,
                plan.scale,
                plan.centered,
                plan.plotStyle,
                plan.fitEstimate,
                plan.contentBox,
                plan.contentSource,
                plan.validation,
                plan.headerExtentsBefore,
                plan.headerExtentsAfter,
                plan.headerExtentsRefreshed,
                coverageSource = coverageDecision?.CoverageSource ?? PlotPdfGate.SourceUnavailable,
                looksTiny = coverageDecision?.LooksTiny,
                coverage = coverageDecision,
                pdfContent,
                plan.coverageEstimate,
                plan.underlayCount,
                plan.underlaysResolved,
                plan.underlays,
                plan.warnings,
                bytes = probe.Bytes,
                pdfProbe = probe
            },
            coverageDecision);
    }

    private sealed record LayoutState(string Ctab, int Tilemode, int Cvport, int Dbmod);

    /// <summary>D4 report: what the layout switch did, and whether the previous state came back.</summary>
    private sealed class PlotLayoutReport
    {
        public bool Switched { get; set; }
        public string? SwitchKind { get; set; }
        public bool Initialized { get; set; }
        public int AppendedCount { get; set; }
        public string[] AppendedSample { get; set; } = [];
        public bool? Restored { get; set; }
        public int DbmodBefore { get; set; }
        public int DbmodAfter { get; set; }
        public LayoutState? Before { get; set; }
        public LayoutState? After { get; set; }
        public List<string> Warnings { get; } = [];
    }

    private static LayoutState ReadLayoutState()
    {
        static int Int(string name)
        {
            try
            {
                return System.Convert.ToInt32(Application.GetSystemVariable(name), CultureInfo.InvariantCulture);
            }
            catch
            {
                return -1;
            }
        }

        string ctab;
        try
        {
            ctab = System.Convert.ToString(Application.GetSystemVariable("CTAB"), CultureInfo.InvariantCulture) ?? "";
        }
        catch
        {
            ctab = "";
        }

        return new LayoutState(ctab, Int("TILEMODE"), Int("CVPORT"), Int("DBMOD"));
    }

    /// <summary>Restores the layout (and paper/model space inside it) that was current before the plot, then reads it back.</summary>
    private static void RestoreLayout(Document doc, LayoutState before, PlotLayoutReport report)
    {
        string? error = null;
        try
        {
            if (before.Ctab.Length > 0 && !PlotLayoutMath.SameLayout(LayoutManager.Current.CurrentLayout, before.Ctab))
            {
                LayoutManager.Current.CurrentLayout = before.Ctab;
            }

            if (before.Tilemode == 0)
            {
                var cvport = ReadLayoutState().Cvport;
                if (before.Cvport != 1 && cvport == 1)
                {
                    doc.Editor.SwitchToModelSpace();
                }
                else if (before.Cvport == 1 && cvport != 1)
                {
                    doc.Editor.SwitchToPaperSpace();
                }
            }
        }
        catch (System.Exception ex)
        {
            error = ex.Message;
        }

        var after = ReadLayoutState();
        var mismatches = PlotLayoutMath.LayoutRestoreMismatches(before.Ctab, before.Tilemode, before.Cvport, after.Ctab, after.Tilemode, after.Cvport).ToList();
        if (error is not null)
        {
            mismatches.Insert(0, $"restore failed: {error}");
        }

        report.After = after;
        report.Restored = mismatches.Count == 0;
        if (mismatches.Count > 0)
        {
            report.Warnings.Add($"layout_restore_mismatch: the layout state before the plot was not fully restored ({string.Join("; ", mismatches)}); '{after.Ctab}' is left current. The PDF itself is unaffected.");
        }
    }

    /// <summary>Adds the D4 layout fields to a plot result's data (success or failure).</summary>
    private static ForgeResult AttachLayoutReport(ForgeResult result, PlotLayoutReport report)
    {
        JsonObject obj;
        if (result.Data is null)
        {
            obj = new JsonObject();
        }
        else
        {
            var node = JsonSerializer.SerializeToNode(result.Data, ForgeJson.Options);
            obj = node as JsonObject ?? new JsonObject { ["result"] = node };
        }

        obj["layoutSwitched"] = report.Switched;
        if (report.Switched)
        {
            obj["layoutSwitchKind"] = report.SwitchKind;
            obj["layoutInitialized"] = report.Initialized;
            obj["layoutAppendedCount"] = report.AppendedCount;
            obj["layoutAppendedSample"] = JsonSerializer.SerializeToNode(report.AppendedSample, ForgeJson.Options);
            obj["layoutRestored"] = report.Restored;
            obj["layoutBefore"] = JsonSerializer.SerializeToNode(report.Before, ForgeJson.Options);
            obj["layoutAfter"] = JsonSerializer.SerializeToNode(report.After, ForgeJson.Options);
        }

        obj["dbmodBefore"] = report.DbmodBefore;
        obj["dbmodAfter"] = report.DbmodAfter;
        if (report.Warnings.Count > 0)
        {
            var warnings = obj["warnings"] as JsonArray ?? new JsonArray();
            foreach (var warning in report.Warnings)
            {
                warnings.Add(warning);
            }

            obj["warnings"] = warnings;
        }

        return result with { Data = obj };
    }

    /// <summary>EXTMIN/EXTMAX (Model) or PEXTMIN/PEXTMAX (paper) as [minX,minY,maxX,maxY]; null when unset.</summary>
    private static double[]? HeaderExtents(Database db, bool isModel)
    {
        try
        {
            var min = isModel ? db.Extmin : db.Pextmin;
            var max = isModel ? db.Extmax : db.Pextmax;
            return min.X <= max.X && min.Y <= max.Y && Math.Abs(max.X - min.X) < 1e20
                ? [min.X, min.Y, max.X, max.Y]
                : null;
        }
        catch
        {
            return null;
        }
    }

    private static void RunPlotEngine(Document doc, PlotInfo info, string outputPath)
    {
        using var engine = PlotFactory.CreatePublishEngine();
        using var progress = new PlotProgressDialog(false, 1, true);
        progress.LowerPlotProgressRange = 0;
        progress.UpperPlotProgressRange = 100;
        progress.PlotProgressPos = 0;
        progress.OnBeginPlot();
        progress.IsVisible = true;
        engine.BeginPlot(progress, null);
        engine.BeginDocument(info, doc.Name, null, 1, true, outputPath);
        progress.OnBeginSheet();
        progress.LowerSheetProgressRange = 0;
        progress.UpperSheetProgressRange = 100;
        progress.SheetProgressPos = 0;
        var pageInfo = new PlotPageInfo();
        engine.BeginPage(pageInfo, info, true, null);
        engine.BeginGenerateGraphics(null);
        engine.EndGenerateGraphics(null);
        engine.EndPage(null);
        progress.SheetProgressPos = 100;
        progress.OnEndSheet();
        engine.EndDocument(null);
        progress.PlotProgressPos = 100;
        progress.OnEndPlot();
        engine.EndPlot(null);
    }

    private static void RunLegacyPlotCommand(
        string layoutName,
        bool isModel,
        string plotDevice,
        PlotReadBack readBack,
        PlotAreaKind area,
        double[]? window,
        string? viewName,
        PlotCentring centring,
        string? plotStyle,
        string outputPath)
    {
        // The caller (PlotToPdf) already made the layout current and restores it afterwards (D4).
        var previousFileDia = Application.GetSystemVariable("FILEDIA");
        Application.SetSystemVariable("FILEDIA", 0);
        try
        {
            var tokens = new List<object>
            {
                "-PLOT",
                "Yes",
                layoutName,
                plotDevice,
                readBack.LocaleMedia,
                readBack.PaperUnits == PlotPaperUnit.Inches ? "Inches" : "Millimeters",
                readBack.Orientation == "portrait" ? "Portrait" : "Landscape",
                "No"
            };

            switch (area)
            {
                case PlotAreaKind.Window:
                    tokens.Add("Window");
                    tokens.Add(string.Create(CultureInfo.InvariantCulture, $"{window![0]},{window[1]}"));
                    tokens.Add(string.Create(CultureInfo.InvariantCulture, $"{window[2]},{window[3]}"));
                    break;
                case PlotAreaKind.View:
                    // -PLOT asks for the view name right after the area choice.
                    tokens.Add("View");
                    tokens.Add(viewName!);
                    break;
                default:
                    tokens.Add(area.ToString());
                    break;
            }

            tokens.Add(readBack.UsesFit ? "Fit" : readBack.CustomScale is { } s ? string.Create(CultureInfo.InvariantCulture, $"{s[0]}:{s[1]}") : "1:1");
            // review-wp2-r2 M1: a stored, uncentred Window/View must NOT be answered "0,0" — that
            // overrides the stored offset. KeepStored answers "" (Enter = keep the page setup's
            // stored offset and stored centred flag); see PlotCentring.OffsetPromptAnswer.
            // live-smoke-20261030 fix4: Layout/View answer "" unconditionally — the api engine
            // never writes centring/origin for them, and a Layout-area -PLOT prompt has no
            // [Center] option on this host (AutoCAD 2025).
            tokens.Add(centring.OffsetPromptAnswer(area));
            tokens.Add("Yes");
            tokens.Add(string.IsNullOrWhiteSpace(plotStyle) ? "" : plotStyle!.Trim());
            tokens.Add("Yes");
            if (isModel)
            {
                tokens.Add("As displayed");
            }
            else
            {
                tokens.Add("No");
                tokens.Add("No");
                tokens.Add("No");
            }

            tokens.Add(outputPath);
            tokens.Add("No");
            tokens.Add("Yes");
            ActiveEditor.Command(tokens.ToArray());
        }
        finally
        {
            Application.SetSystemVariable("FILEDIA", previousFileDia);
        }
    }

    private sealed record PlotReadBack(
        string Device,
        string CanonicalMedia,
        string LocaleMedia,
        double[] PaperMm,
        double[] PrintableMm,
        PlotPaperUnit PaperUnits,
        int RotationDeg,
        string Orientation,
        bool UsesFit,
        double[]? CustomScale,
        bool Centered,
        string StyleSheet);

    private static PlotReadBack DescribeSettings(PlotSettingsValidator psv, PlotSettings settings)
    {
        var paper = settings.PlotPaperSize;
        var margins = settings.PlotPaperMargins;
        var printW = Math.Max(0, paper.X - margins.MinPoint.X - margins.MaxPoint.X);
        var printH = Math.Max(0, paper.Y - margins.MinPoint.Y - margins.MaxPoint.Y);
        var rotation = RotationDegrees(settings.PlotRotation);
        var sheetW = rotation % 180 == 0 ? paper.X : paper.Y;
        var sheetH = rotation % 180 == 0 ? paper.Y : paper.X;
        var usesFit = settings.UseStandardScale && settings.StdScaleType == StdScaleType.ScaleToFit;
        double[]? custom = null;
        if (!usesFit)
        {
            try
            {
                var cs = settings.CustomPrintScale;
                custom = [cs.Numerator, cs.Denominator];
            }
            catch
            {
                custom = null;
            }
        }

        return new PlotReadBack(
            settings.PlotConfigurationName,
            settings.CanonicalMediaName,
            SafeLocaleMediaName(psv, settings, settings.CanonicalMediaName) ?? settings.CanonicalMediaName,
            [Math.Round(paper.X, 2), Math.Round(paper.Y, 2)],
            [Math.Round(printW, 2), Math.Round(printH, 2)],
            settings.PlotPaperUnits,
            rotation,
            sheetW >= sheetH ? "landscape" : "portrait",
            usesFit,
            custom,
            settings.PlotCentered,
            settings.CurrentStyleSheet ?? "");
    }

    private static int RotationDegrees(PlotRotation rotation) => rotation switch
    {
        PlotRotation.Degrees090 => 90,
        PlotRotation.Degrees180 => 180,
        PlotRotation.Degrees270 => 270,
        _ => 0
    };

    private static string? SafeLocaleMediaName(PlotSettingsValidator psv, PlotSettings ps, string canonical)
    {
        try
        {
            return psv.GetLocaleMediaName(ps, canonical);
        }
        catch
        {
            return null;
        }
    }

    private static string[] MediaCandidates(string requested, IReadOnlyList<(string Canonical, string? Locale)> media)
    {
        var words = requested.Split([' ', '_', '(', ')', 'x', 'X'], StringSplitOptions.RemoveEmptyEntries)
            .Where(w => w.Length >= 2)
            .ToArray();
        var ranked = media
            .Select(m => (Name: m.Locale ?? m.Canonical, Score: words.Count(w => (m.Locale ?? m.Canonical).Contains(w, StringComparison.OrdinalIgnoreCase))))
            .OrderByDescending(m => m.Score)
            .ThenBy(m => m.Name, StringComparer.OrdinalIgnoreCase)
            .Take(25)
            .Select(m => m.Name)
            .ToArray();
        return ranked;
    }

    /// <summary>
    /// fix-smoke-20261029-r2 D9: <see cref="Layout.PlotPaperSize"/> is expressed in the
    /// layout's own paper units (inches on imperial templates — passing them as millimeters
    /// is what made the imperial Layout1 plot throw eInvalidInput). Converted to mm for
    /// <see cref="PaperMatch"/>; pixel-sized paper cannot be compared to a sheet.
    /// </summary>
    private static (double WidthMm, double HeightMm)? StoredSheetMm(Layout layout)
    {
        var size = layout.PlotPaperSize;
        if (size.X <= 0 || size.Y <= 0)
        {
            return null;
        }

        return layout.PlotPaperUnits switch
        {
            PlotPaperUnit.Millimeters => (size.X, size.Y),
            PlotPaperUnit.Inches => (size.X * 25.4, size.Y * 25.4),
            _ => null
        };
    }

    private static PlotPaperUnit GuessPaperUnits(string canonicalMedia, PlotPaperUnit fallback)
    {
        if (canonicalMedia.Contains("MM)", StringComparison.OrdinalIgnoreCase) || canonicalMedia.Contains("_MM", StringComparison.OrdinalIgnoreCase))
        {
            return PlotPaperUnit.Millimeters;
        }

        if (canonicalMedia.Contains("Inch", StringComparison.OrdinalIgnoreCase))
        {
            return PlotPaperUnit.Inches;
        }

        return fallback == PlotPaperUnit.Pixels ? PlotPaperUnit.Millimeters : fallback;
    }

    private static ObjectId TryGetLayoutId(string layoutName)
    {
        try
        {
            return LayoutManager.Current.GetLayoutId(layoutName);
        }
        catch
        {
            return ObjectId.Null;
        }
    }

    /// <summary>"DWG To PDF" → "DWG To PDF.pc3" when that PC3 device exists; otherwise the name as given.</summary>
    private static string NormalizeDeviceName(string device)
    {
        if (!string.IsNullOrEmpty(Path.GetExtension(device)))
        {
            return device;
        }

        try
        {
            var devices = PlotConfigManager.Devices;
            for (var i = 0; i < devices.Count; i++)
            {
                if (string.Equals(devices[i].DeviceName, device + ".pc3", StringComparison.OrdinalIgnoreCase))
                {
                    return devices[i].DeviceName;
                }
            }
        }
        catch
        {
            // Fall through with the name as given.
        }

        return device;
    }

    private static string? FindPc3Path(string device)
    {
        var dirs = new List<string>();
        try
        {
            var devices = PlotConfigManager.Devices;
            for (var i = 0; i < devices.Count; i++)
            {
                var info = devices[i];
                var full = info.FullPath;
                if (string.IsNullOrWhiteSpace(full) || !full.EndsWith(".pc3", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (string.Equals(info.DeviceName, device, StringComparison.OrdinalIgnoreCase) && File.Exists(full))
                {
                    return full;
                }

                var dir = Path.GetDirectoryName(full);
                if (!string.IsNullOrEmpty(dir) && !dirs.Contains(dir, StringComparer.OrdinalIgnoreCase))
                {
                    dirs.Add(dir);
                }
            }
        }
        catch
        {
            // Enumeration failure: try the directories we already have.
        }

        var fileName = device.EndsWith(".pc3", StringComparison.OrdinalIgnoreCase) ? device : device + ".pc3";
        foreach (var dir in dirs)
        {
            var candidate = Path.Combine(dir, fileName);
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }

    /// <summary>
    /// Makes sure the plot cannot open a viewer: returns the device to plot with — a Forge-owned
    /// clone of the PC3 with View_New_File=FALSE when the original has it TRUE.
    /// </summary>
    private static ViewerSuppression PrepareNoViewerDevice(string device, bool write)
    {
        var pc3Path = FindPc3Path(device);
        if (pc3Path is null)
        {
            return new ViewerSuppression("not_pc3", device, null, null, "Device is not a PC3 file (system printer); there is no PC3 viewer flag to suppress.");
        }

        byte[] bytes;
        try
        {
            bytes = File.ReadAllBytes(pc3Path);
        }
        catch (System.Exception ex)
        {
            return new ViewerSuppression("failed", device, pc3Path, null, $"Cannot read {pc3Path}: {ex.Message}");
        }

        if (!Pc3File.TryRead(bytes, out var text, out var header))
        {
            return new ViewerSuppression("failed", device, pc3Path, null, "The PC3 file is not a readable PIA container, so the viewer flag cannot be checked.");
        }

        var current = Pc3File.GetCustomValue(text, Pc3File.ViewerProperty);
        if (current is null)
        {
            return new ViewerSuppression("property_absent", device, pc3Path, null, "This driver has no open-in-viewer property.");
        }

        if (!Pc3File.OpensViewer(text))
        {
            return new ViewerSuppression("already_off", device, pc3Path, null, null);
        }

        var cloneName = Pc3File.NoViewerCloneName(device);
        var clonePath = Path.Combine(Path.GetDirectoryName(pc3Path)!, cloneName);
        if (!write)
        {
            return new ViewerSuppression("would_clone", device, pc3Path, clonePath, "Dry run: the plot would use the no-viewer clone.");
        }

        var cloneText = Pc3File.SetCustomValue(text, Pc3File.ViewerProperty, "FALSE", out _);
        string note;
        try
        {
            var upToDate = File.Exists(clonePath)
                           && Pc3File.TryRead(File.ReadAllBytes(clonePath), out var existing, out _)
                           && string.Equals(existing, cloneText, StringComparison.Ordinal);
            if (upToDate)
            {
                note = "reused";
            }
            else
            {
                var temp = clonePath + ".forge-tmp";
                File.WriteAllBytes(temp, Pc3File.Write(cloneText, header));
                File.Move(temp, clonePath, overwrite: true);
                note = "created";
            }
        }
        catch (System.Exception ex)
        {
            return new ViewerSuppression("failed", device, pc3Path, clonePath, $"Cannot write the no-viewer clone: {ex.Message}");
        }

        try
        {
            PlotConfigManager.RefreshList(RefreshCode.All);
            var known = PlotSettingsValidator.Current.GetPlotDeviceList()
                .Cast<string>()
                .Any(d => string.Equals(d, cloneName, StringComparison.OrdinalIgnoreCase));
            if (!known)
            {
                return new ViewerSuppression("failed", device, pc3Path, clonePath, "The clone was written but AutoCAD does not list it as a plot device.");
            }
        }
        catch (System.Exception ex)
        {
            return new ViewerSuppression("failed", device, pc3Path, clonePath, $"AutoCAD could not refresh its device list: {ex.Message}");
        }

        return new ViewerSuppression("clone", cloneName, pc3Path, clonePath, note);
    }

    /// <summary>One underlay/raster-image definition as the preflight sees it. Status: resolved|missing|unloaded|unreferenced.
    /// DefinitionId stays out of the payload: a live ObjectId does not serialize (cycle via Database).</summary>
    private sealed record UnderlayEntry(
        string Kind,
        string Name,
        string Path,
        string? ResolvedPath,
        string Status,
        string? ItemName,
        [property: System.Text.Json.Serialization.JsonIgnore] ObjectId DefinitionId);

    private static readonly (string Dict, string Kind)[] UnderlayDictionaries =
    [
        ("ACAD_PDFDEFINITIONS", "pdf"),
        ("ACAD_DWFDEFINITIONS", "dwf"),
        ("ACAD_DGNDEFINITIONS", "dgn"),
        ("ACAD_IMAGE_DICT", "image"),
    ];

    /// <summary>
    /// TOOL-GAP #26: every underlay definition (PDF/DWF/DGN underlay + raster image) in the drawing
    /// with resolved status. Definitions live in the named-object dictionaries the DXF OBJECTS
    /// section serialises — ACAD_PDFDEFINITIONS holds the PDFDEFINITION whose group-1 path was
    /// relative in sheet-set_R1.dxf (the exact shape DxfUnderlaySniff parses offline). Managed
    /// member names vary by release, so the stored path / load flag are read by reflection:
    /// UnderlayDefinition and RasterImageDef expose SourceFileName/FileName/ActiveFileName +
    /// IsLoaded. Resolution runs BEFORE the load flag: AutoCAD marks a missing file's
    /// definition IsLoaded=false at doc open, so UnderlayResolution.Classify decides —
    /// unresolvable path = "missing" (fails the gate), resolved + IsLoaded=false = "unloaded".
    /// </summary>
    private static List<UnderlayEntry> EnumerateUnderlayDefinitions(Transaction tr, Database db)
    {
        var drawingFolder = DrawingFolder(db);
        var result = new List<UnderlayEntry>();
        var nod = (DBDictionary)tr.GetObject(db.NamedObjectsDictionaryId, OpenMode.ForRead);
        foreach (var (dictName, kind) in UnderlayDictionaries)
        {
            if (!nod.Contains(dictName) || tr.GetObject(nod.GetAt(dictName), OpenMode.ForRead) is not DBDictionary dict)
            {
                continue;
            }

            foreach (DBDictionaryEntry entry in dict)
            {
                DBObject? def;
                try
                {
                    def = tr.GetObject(entry.Value, OpenMode.ForRead);
                }
                catch
                {
                    continue; // erased dictionary entry — nothing to resolve
                }

                if (def is null)
                {
                    continue;
                }

                var name = string.IsNullOrWhiteSpace(entry.Key) ? def.Handle.ToString() : entry.Key;
                var storedPath = ReflectedString(def, "SourceFileName", "FileName");
                var activePath = ReflectedString(def, "ActiveFileName"); // AutoCAD's resolved path after re-pathing
                var itemName = ReflectedString(def, "ItemName");
                var resolved = ResolveUnderlayFile(db, storedPath, drawingFolder)
                    ?? ResolveUnderlayFile(db, activePath, drawingFolder);
                var isLoaded = ReflectedBool(def, "IsLoaded", "Loaded") ?? true;
                result.Add(new UnderlayEntry(
                    kind,
                    name,
                    storedPath ?? "",
                    resolved ?? UnderlayResolution.ResolveCandidatePath(storedPath, drawingFolder),
                    UnderlayResolution.Classify(resolved, isLoaded),
                    itemName,
                    def.ObjectId));
            }
        }

        // Detached underlays/images leave their definition behind until PURGE. An unreferenced
        // def cannot paint anything, so it is reported but never a failure (F3).
        var missingIds = result.Where(u => u.Status == "missing").Select(u => u.DefinitionId).ToHashSet();
        if (missingIds.Count > 0)
        {
            var referenced = CollectReferencedUnderlayIds(tr, db, missingIds);
            result = result
                .Select(u => u.Status == "missing" && !referenced.Contains(u.DefinitionId) ? u with { Status = "unreferenced" } : u)
                .ToList();
        }

        return result;
    }

    /// <summary>Which of <paramref name="wanted"/> underlay-definition ids are still referenced by an entity somewhere in the drawing.</summary>
    private static HashSet<ObjectId> CollectReferencedUnderlayIds(Transaction tr, Database db, HashSet<ObjectId> wanted)
    {
        var referenced = new HashSet<ObjectId>();
        var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
        foreach (ObjectId btrId in bt)
        {
            var btr = (BlockTableRecord)tr.GetObject(btrId, OpenMode.ForRead);
            foreach (ObjectId id in btr)
            {
                if (id.IsErased)
                {
                    continue;
                }

                Entity? entity;
                try
                {
                    entity = tr.GetObject(id, OpenMode.ForRead) as Entity;
                }
                catch
                {
                    continue; // unopenable/proxy entity — not an underlay reference
                }

                var defId = entity switch
                {
                    UnderlayReference ur => ur.DefinitionId,
                    RasterImage ri => ri.ImageDefId,
                    _ => ObjectId.Null
                };
                if (wanted.Contains(defId))
                {
                    referenced.Add(defId);
                    if (referenced.Count == wanted.Count)
                    {
                        return referenced; // every wanted id accounted for — stop scanning
                    }
                }
            }
        }

        return referenced;
    }

    private static string? DrawingFolder(Database db)
    {
        try
        {
            return Path.GetDirectoryName(db.Filename);
        }
        catch
        {
            return null;
        }
    }

    private static string? ResolveUnderlayFile(Database db, string? storedPath, string? drawingFolder)
    {
        if (UnderlayResolution.ResolveCandidatePath(storedPath, drawingFolder) is { } candidate && File.Exists(candidate))
        {
            return candidate;
        }

        // Support-search-path fallback — the same FindFile the linetype loader already uses.
        return string.IsNullOrWhiteSpace(storedPath) ? null : TryFindSupportFile(db, storedPath);
    }

    /// <summary>Shared missing-underlay failure for the plot and publish entry points; null when all resolve.</summary>
    private static ForgeResult? UnderlayPreflightFailure(ForgeCommand command, IReadOnlyList<UnderlayEntry> underlays)
    {
        var missing = underlays.Where(u => u.Status == "missing").ToArray();
        if (missing.Length == 0)
        {
            return null;
        }

        return ForgeResult.Failure(
            command.Id,
            "plot_underlay_unresolved",
            $"Unresolved underlay file(s): {string.Join("; ", missing.Select(m => $"{m.Kind} '{m.Name}' -> {(m.Path.Length > 0 ? m.Path : "(no file name)")}"))}. The plot would come out without the underlay image.",
            "Repath the underlay to an existing file (absolute paths are safest), or detach/unload it (-IMAGE, -PDFATTACH or the Reference palette) and re-plot. Orphaned definitions left after detach can be removed with PURGE.",
            data: new { underlays, missing, underlayCount = underlays.Count, underlaysResolved = false });
    }

    private static string? ReflectedString(object target, params string[] propertyNames)
    {
        foreach (var name in propertyNames)
        {
            try
            {
                if (target.GetType().GetProperty(name) is { } property && property.GetValue(target) is string value && !string.IsNullOrWhiteSpace(value))
                {
                    return value;
                }
            }
            catch
            {
                // Getter threw (erased/unimplemented member) — try the next name.
            }
        }

        return null;
    }

    private static bool? ReflectedBool(object target, params string[] propertyNames)
    {
        foreach (var name in propertyNames)
        {
            try
            {
                if (target.GetType().GetProperty(name) is { } property && property.GetValue(target) is bool value)
                {
                    return value;
                }
            }
            catch
            {
            }
        }

        return null;
    }

    /// <summary>
    /// World box [minX,minY,maxX,maxY] of what prints in a space, scanned from the entities (frozen/off
    /// layers excluded; a paper layout's own viewport excluded). fix-smoke-20261029 D5: Model is scanned
    /// too, because EXTMIN/EXTMAX only grow; the header box is used only past the scan cap.
    /// </summary>
    private static double[]? ComputeContentBox(Transaction tr, Database db, Layout layout, bool isModel, out string source)
    {
        var btr = (BlockTableRecord)tr.GetObject(layout.BlockTableRecordId, OpenMode.ForRead);
        var hiddenLayers = new Dictionary<ObjectId, bool>();
        double minX = double.PositiveInfinity, minY = double.PositiveInfinity, maxX = double.NegativeInfinity, maxY = double.NegativeInfinity;
        var scanned = 0;
        var paperViewportSkipped = isModel;
        foreach (ObjectId id in btr)
        {
            if (++scanned > MaxExtentsScanEntities)
            {
                // Past the cap Model falls back to the header box, labelled as not verified (it may be stale).
                if (isModel && HeaderExtents(db, true) is { } header)
                {
                    source = "header_extents_unverified";
                    return header;
                }

                source = "too_many_entities";
                return null;
            }

            if (tr.GetObject(id, OpenMode.ForRead) is not Entity entity || !entity.Visible)
            {
                continue;
            }

            if (!paperViewportSkipped && entity is Viewport)
            {
                paperViewportSkipped = true;
                continue;
            }

            if (!hiddenLayers.TryGetValue(entity.LayerId, out var hidden))
            {
                hidden = tr.GetObject(entity.LayerId, OpenMode.ForRead) is LayerTableRecord ltr && (ltr.IsFrozen || ltr.IsOff);
                hiddenLayers[entity.LayerId] = hidden;
            }

            if (hidden || TryExtents(entity) is not { } e)
            {
                continue;
            }

            minX = Math.Min(minX, e[0]);
            minY = Math.Min(minY, e[1]);
            maxX = Math.Max(maxX, e[3]);
            maxY = Math.Max(maxY, e[4]);
        }

        if (double.IsInfinity(minX))
        {
            source = "empty";
            return null;
        }

        source = isModel ? "scanned_model" : "scanned_layout";
        return [minX, minY, maxX, maxY];
    }

    private static double[]? TryCurrentViewBox(string layoutName)
    {
        try
        {
            if (!string.Equals(LayoutManager.Current.CurrentLayout, layoutName, StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            using var view = ActiveEditor.GetCurrentView();
            var c = view.CenterPoint;
            return [c.X - view.Width / 2, c.Y - view.Height / 2, c.X + view.Width / 2, c.Y + view.Height / 2];
        }
        catch
        {
            return null;
        }
    }
}
