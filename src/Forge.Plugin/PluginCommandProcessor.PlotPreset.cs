using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using Forge.Shared;
using AcPlotType = Autodesk.AutoCAD.DatabaseServices.PlotType;

namespace Forge.Plugin;

// forge_plot_preset_apply — one call sets device, paper, CTB, area, scale/fit, centring and
// rotation on one or many layouts (and optionally saves it as a named page setup), so every sheet
// of an issue set plots the same way without opening Page Setup. It uses the same
// PlotSettingsValidator path as forge_plot_to_pdf, and by default the no-viewer PC3 clone from the
// reliability wave, so a later manual or scripted plot can never hang on an OpenWith dialog.
// Each layout gets its own validated PlotSettings (extents/orientation are per layout) that is
// copied onto the layout at the end; any AutoCAD rejection aborts the whole transaction.
public sealed partial class PluginCommandProcessor
{
    private sealed record PlotPresetArgs
    {
        public string[]? Layouts { get; init; }
        public string? SetupName { get; init; }
        public string? Device { get; init; }
        public string? PaperSize { get; init; }
        public string? PlotStyle { get; init; }
        public string? PlotArea { get; init; }
        public double[][]? Window { get; init; }
        public string? Scale { get; init; }
        public string? Units { get; init; }
        public bool? Centered { get; init; }
        public string? Orientation { get; init; }
        public bool NoViewer { get; init; } = true;
    }

    private static ForgeResult PlotPresetApply(ForgeCommand command)
    {
        var args = Args<PlotPresetArgs>(command);
        var controls = new List<(string, string?)>
        {
            ("setupName", args.SetupName), ("device", args.Device), ("paperSize", args.PaperSize), ("plotStyle", args.PlotStyle),
            ("plotArea", args.PlotArea), ("scale", args.Scale), ("units", args.Units), ("orientation", args.Orientation)
        };
        controls.AddRange((args.Layouts ?? []).Select(l => ("layouts", (string?)l)));
        if (RejectControlChars(command, controls.ToArray()) is { } reject)
        {
            return reject;
        }

        if (args.SetupName is not null && !PlotPreset.IsValidSetupName(args.SetupName))
        {
            return ForgeResult.Failure(command.Id, "invalid_setup_name", $"Invalid page setup name '{args.SetupName}'.");
        }

        PlotAreaKind? requestedArea = null;
        if (!string.IsNullOrWhiteSpace(args.PlotArea))
        {
            if (!PlotLayoutMath.TryParseArea(args.PlotArea, out var parsed))
            {
                return ForgeResult.Failure(command.Id, "invalid_plot_area", $"Unknown plotArea '{args.PlotArea}'.", "Use layout, extents, display, limits, or window.");
            }

            requestedArea = parsed;
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
        }

        if (requestedArea == PlotAreaKind.Window && window is null)
        {
            return ForgeResult.Failure(command.Id, "missing_window", "plotArea=window requires window=[[x1,y1],[x2,y2]].");
        }

        if (!PlotLayoutMath.TryParseScale(args.Scale, out var scaleSpec))
        {
            return ForgeResult.Failure(command.Id, "invalid_scale", $"Unknown scale '{args.Scale}'.", "Use \"fit\" or \"1:100\".");
        }

        var orientation = string.IsNullOrWhiteSpace(args.Orientation) ? "auto" : args.Orientation!.Trim().ToLowerInvariant();
        if (orientation is not ("auto" or "landscape" or "portrait"))
        {
            return ForgeResult.Failure(command.Id, "invalid_orientation", $"Unknown orientation '{args.Orientation}'.");
        }

        PlotPaperUnit? units = null;
        if (!string.IsNullOrWhiteSpace(args.Units))
        {
            units = args.Units!.Trim().ToLowerInvariant() switch
            {
                "mm" or "millimeter" or "millimeters" or "millimetre" or "millimetres" => PlotPaperUnit.Millimeters,
                "in" or "inch" or "inches" => PlotPaperUnit.Inches,
                _ => null
            };
            if (units is null)
            {
                return ForgeResult.Failure(command.Id, "invalid_units", $"Unknown units '{args.Units}'.", "Use millimeters or inches.");
            }
        }

        var db = ActiveDb;
        var available = new List<string>();
        using (var ltr = db.TransactionManager.StartTransaction())
        {
            var dict = (DBDictionary)ltr.GetObject(db.LayoutDictionaryId, OpenMode.ForRead);
            foreach (DBDictionaryEntry entry in dict)
            {
                available.Add(entry.Key);
            }

            ltr.Commit();
        }

        var layouts = PlotPreset.ResolveLayouts(args.Layouts, available, LayoutManager.Current.CurrentLayout, out var unknown);
        if (unknown.Count > 0)
        {
            return ForgeResult.Failure(command.Id, "layout_not_found", $"Layout(s) not found: {string.Join(", ", unknown)}", "Call forge_doc_list_layouts; \"*\" selects every paper layout.", data: new { available });
        }

        if (layouts.Count == 0)
        {
            return ForgeResult.Failure(command.Id, "no_layouts", "No layout matched.", data: new { available });
        }

        if (layouts.Count > PlotPreset.MaxLayouts)
        {
            return ForgeResult.Failure(command.Id, "too_many_layouts", $"At most {PlotPreset.MaxLayouts} layouts per call.");
        }

        var requestedDevice = NormalizeDeviceName(string.IsNullOrWhiteSpace(args.Device) ? DefaultPdfDevice : args.Device!.Trim());
        var viewer = args.NoViewer
            ? PrepareNoViewerDevice(requestedDevice, write: !command.DryRun)
            : new ViewerSuppression("not_requested", requestedDevice, null, null, "noViewer=false: the device is used as given.");
        if (viewer.Failed)
        {
            return ForgeResult.Failure(command.Id, "plot_viewer_suppression_failed", $"Could not prepare a no-viewer copy of '{requestedDevice}': {viewer.Note}", "Pass noViewer=false to use the device as is.", data: new { viewerSuppression = viewer });
        }

        if (command.DryRun)
        {
            return DryRun(command, new
            {
                tool = "forge_plot_preset_apply",
                layouts,
                setupName = args.SetupName,
                device = viewer.PlotDevice,
                viewerSuppression = viewer,
                paperSize = args.PaperSize,
                plotStyle = args.PlotStyle,
                plotArea = requestedArea?.ToString().ToLowerInvariant(),
                scale = scaleSpec.Fit ? "fit" : $"{scaleSpec.PaperUnits}:{scaleSpec.DrawingUnits}",
                centered = args.Centered ?? true,
                orientation
            });
        }

        var psv = PlotSettingsValidator.Current;
        var results = new List<object>();
        var warnings = new List<string>();
        using var tr = db.TransactionManager.StartTransaction();
        PlotSettings? template = null;
        try
        {
            foreach (var name in layouts)
            {
                var layoutId = TryGetLayoutId(name);
                if (layoutId.IsNull)
                {
                    return ForgeResult.Failure(command.Id, "layout_not_found", $"Layout not found: {name}");
                }

                var layout = (Layout)tr.GetObject(layoutId, OpenMode.ForWrite);
                var isModel = layout.ModelType;
                using var ps = new PlotSettings(isModel);
                ps.CopyFrom(layout);
                psv.SetPlotConfigurationName(ps, viewer.PlotDevice, null);
                psv.RefreshLists(ps);
                var media = psv.GetCanonicalMediaNameList(ps)
                    .Cast<string>()
                    .Select(canonical => (Canonical: canonical, Locale: SafeLocaleMediaName(psv, ps, canonical)))
                    .ToList();

                string canonicalMedia;
                if (!string.IsNullOrWhiteSpace(args.PaperSize))
                {
                    var resolved = PlotLayoutMath.ResolveMedia(args.PaperSize, media);
                    if (resolved is null)
                    {
                        return ForgeResult.Failure(command.Id, "paper_size_not_found", $"Device '{requestedDevice}' has no paper size matching '{args.PaperSize}'.", "Pass one of the candidates.", data: new { candidates = MediaCandidates(args.PaperSize!, media) });
                    }

                    canonicalMedia = resolved;
                }
                else if (media.Any(m => string.Equals(m.Canonical, layout.CanonicalMediaName, StringComparison.Ordinal)))
                {
                    canonicalMedia = layout.CanonicalMediaName;
                }
                else
                {
                    psv.SetClosestMediaName(ps, Math.Max(layout.PlotPaperSize.X, 1), Math.Max(layout.PlotPaperSize.Y, 1), PlotPaperUnit.Millimeters, true);
                    canonicalMedia = ps.CanonicalMediaName;
                    warnings.Add($"{name}: no paperSize given and the layout's paper is not on {requestedDevice}; closest size {canonicalMedia} used.");
                }

                psv.SetCanonicalMediaName(ps, canonicalMedia);
                psv.SetPlotPaperUnits(ps, units ?? GuessPaperUnits(canonicalMedia, layout.PlotPaperUnits));

                var mediaChanged = !string.Equals(canonicalMedia, layout.CanonicalMediaName, StringComparison.Ordinal);
                var area = requestedArea ?? (isModel || mediaChanged ? PlotAreaKind.Extents : PlotAreaKind.Layout);
                if (area == PlotAreaKind.Layout && isModel)
                {
                    area = PlotAreaKind.Extents;
                    warnings.Add($"{name}: plotArea=layout is paper-space only; Model uses extents.");
                }

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
                    default:
                        psv.SetPlotType(ps, AcPlotType.Layout);
                        break;
                }

                double areaW, areaH;
                var content = area == PlotAreaKind.Extents ? ComputeContentBox(tr, db, layout, isModel, out _) : null;
                var box = area == PlotAreaKind.Window ? window : content;
                if (area == PlotAreaKind.Layout)
                {
                    (areaW, areaH) = (layout.PlotPaperSize.X, layout.PlotPaperSize.Y);
                }
                else if (box is { Length: 4 })
                {
                    (areaW, areaH) = (box[2] - box[0], box[3] - box[1]);
                }
                else
                {
                    (areaW, areaH) = (Math.Sqrt(2), 1.0);
                }

                var rotation = PlotLayoutMath.ChooseRotation(orientation, ps.PlotPaperSize.X, ps.PlotPaperSize.Y, areaW, areaH) ?? 0;
                psv.SetPlotRotation(ps, rotation == 90 ? PlotRotation.Degrees090 : PlotRotation.Degrees000);

                if (area == PlotAreaKind.Layout)
                {
                    psv.SetUseStandardScale(ps, false);
                    psv.SetCustomPrintScale(ps, scaleSpec.Fit ? new CustomScale(1, 1) : new CustomScale(scaleSpec.PaperUnits, scaleSpec.DrawingUnits));
                    if (scaleSpec.Fit && !string.IsNullOrWhiteSpace(args.Scale))
                    {
                        warnings.Add($"{name}: scale=fit does not apply to plotArea=layout; the layout plots 1:1.");
                    }
                }
                else if (scaleSpec.Fit)
                {
                    psv.SetUseStandardScale(ps, true);
                    psv.SetStdScaleType(ps, StdScaleType.ScaleToFit);
                }
                else
                {
                    psv.SetUseStandardScale(ps, false);
                    psv.SetCustomPrintScale(ps, new CustomScale(scaleSpec.PaperUnits, scaleSpec.DrawingUnits));
                }

                var centered = args.Centered ?? true;
                if (area != PlotAreaKind.Layout)
                {
                    psv.SetPlotCentered(ps, centered);
                    if (!centered)
                    {
                        psv.SetPlotOrigin(ps, Point2d.Origin);
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
                                    ?? sheets.FirstOrDefault(s => string.Equals(System.IO.Path.GetFileNameWithoutExtension(s), wanted, StringComparison.OrdinalIgnoreCase));
                        if (match is null)
                        {
                            return ForgeResult.Failure(command.Id, "plot_style_not_found", $"Plot style table not found: {wanted}", "Pass one of the available tables, or \".\" for none.", data: new { candidates = sheets.Take(60).ToArray() });
                        }

                        psv.SetCurrentStyleSheet(ps, match);
                        ps.PlotPlotStyles = true;
                    }
                }

                if (args.SetupName is not null)
                {
                    ps.PlotSettingsName = args.SetupName;
                    template ??= CloneSettings(ps);
                }

                layout.CopyFrom(ps);
                var readBack = DescribeSettings(psv, layout);
                results.Add(new
                {
                    layout = layout.LayoutName,
                    plotArea = area.ToString().ToLowerInvariant(),
                    device = readBack.Device,
                    paper = readBack.LocaleMedia,
                    canonicalMedia = readBack.CanonicalMedia,
                    paperMm = readBack.PaperMm,
                    orientation = readBack.Orientation,
                    fit = readBack.UsesFit,
                    customScale = readBack.CustomScale,
                    centered = readBack.Centered,
                    plotStyle = readBack.StyleSheet
                });
            }

            var setupReplaced = false;
            if (args.SetupName is not null && template is not null)
            {
                var dict = (DBDictionary)tr.GetObject(db.PlotSettingsDictionaryId, OpenMode.ForWrite);
                if (dict.Contains(args.SetupName))
                {
                    var existing = (PlotSettings)tr.GetObject(dict.GetAt(args.SetupName), OpenMode.ForWrite);
                    existing.CopyFrom(template);
                    template.Dispose();
                    setupReplaced = true;
                }
                else
                {
                    dict.SetAt(args.SetupName, template);
                    tr.AddNewlyCreatedDBObject(template, true);
                }

                template = null;
            }

            tr.Commit();
            return ForgeResult.Success(
                command.Id,
                new
                {
                    applied = results.Count,
                    setupName = args.SetupName,
                    setupReplaced = args.SetupName is null ? (bool?)null : setupReplaced,
                    device = viewer.PlotDevice,
                    viewerSuppression = viewer,
                    layouts = results,
                    warnings
                },
                verification: new ForgeVerification
                {
                    Attempted = true,
                    Passed = results.Count == layouts.Count,
                    Message = "Each layout's plot settings were read back after PlotSettingsValidator applied them.",
                    ReadBack = results
                });
        }
        catch (Autodesk.AutoCAD.Runtime.Exception ex)
        {
            return ForgeResult.Failure(command.Id, "plot_settings_invalid", $"AutoCAD rejected the plot settings: {ex.Message}", "Check device/paperSize/plotArea/scale; nothing was changed (the transaction was aborted).", data: new { appliedBeforeError = results });
        }
        finally
        {
            template?.Dispose();
        }
    }

    private static PlotSettings CloneSettings(PlotSettings source)
    {
        var copy = new PlotSettings(source.ModelType);
        copy.CopyFrom(source);
        copy.PlotSettingsName = source.PlotSettingsName;
        return copy;
    }
}
