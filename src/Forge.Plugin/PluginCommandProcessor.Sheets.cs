using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using System.Globalization;
using Forge.Shared;

namespace Forge.Plugin;

// docs/cad-authoring-tools.md C24 — document/lifecycle authoring: forge_doc_new,
// forge_layer_set, forge_doc_export_dxf, forge_draw_polyline_from_csv, forge_viewport_add.
// Same conventions as the C20–C23 wave: typed params, handles out, read-back, dry-run.
public sealed partial class PluginCommandProcessor
{
    private static ForgeResult DocNew(ForgeCommand command)
    {
        var args = Args<DocNewArgs>(command);
        if (args.TemplatePath is not null)
        {
            if (string.IsNullOrWhiteSpace(args.TemplatePath))
            {
                return ForgeResult.Failure(command.Id, "missing_template", "templatePath must be a non-empty path when provided.");
            }

            if (RejectControlChars(command, ("templatePath", args.TemplatePath)) is { } reject)
            {
                return reject;
            }

            if (!File.Exists(args.TemplatePath))
            {
                return ForgeResult.Failure(command.Id, "template_not_found", $"Template not found: {args.TemplatePath}");
            }
        }

        if (command.DryRun)
        {
            return DryRun(command, new { template = args.TemplatePath, note = "A blank document is created when no template is given." });
        }

        // fix-smoke-20261029 D1: runs on the application-context (Idle) lane, so the switch to the new
        // drawing is not deferred behind a document command. The result comes from a readback.
        var before = Documents.MdiActiveDocument?.Name;
        Document document;
        try
        {
            // D10 + r1 finding 8: Documents.Add loads the given template — or the default
            // template's own foreign content when none is passed — so the scope is
            // unconditional and fails closed.
            using var proxyNotice = ProxyNoticeScope.EnterOrFail(command, out var scopeFailure);
            if (scopeFailure is not null)
            {
                return scopeFailure;
            }

            document = Documents.Add(args.TemplatePath ?? "");
        }
        catch (System.Exception ex)
        {
            return ForgeResult.Failure(
                command.Id,
                "doc_new_failed",
                $"Creating the document failed: {ex.Message}",
                "A DWT/DWG template path must be readable and loadable in this AutoCAD version.");
        }

        if (!ReferenceEquals(Documents.MdiActiveDocument, document))
        {
            Documents.MdiActiveDocument = document;
        }

        return ReadBackActiveDocument(
            command.Id,
            new DocActivateReadBack(document.Name, before, null, false, false, [])
            {
                TargetDocument = document,
                Template = args.TemplatePath,
                UnconfirmedCode = "doc_new_unconfirmed"
            });
    }

    private static ForgeResult LayerSet(ForgeCommand command)
    {
        var args = Args<LayerSetArgs>(command);
        if (string.IsNullOrWhiteSpace(args.Name))
        {
            return ForgeResult.Failure(command.Id, "missing_layer_name", "name is required.");
        }

        if (args.IsOn is null && args.IsFrozen is null && args.IsLocked is null && args.IsPlottable is null && args.ColorIndex is null && !args.MakeCurrent)
        {
            return ForgeResult.Failure(command.Id, "missing_props", "Provide at least one of isOn, isFrozen, isLocked, isPlottable, colorIndex or makeCurrent.");
        }

        if (args.ColorIndex is < 0 or > 256)
        {
            return ForgeResult.Failure(command.Id, "invalid_args", "colorIndex must be 0 (ByBlock), 1–255, or 256 (ByLayer).");
        }

        var db = ActiveDb;
        using var tr = db.TransactionManager.StartTransaction();
        var layerTable = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);
        if (!layerTable.Has(args.Name))
        {
            return ForgeResult.Failure(command.Id, "layer_not_found", $"Layer not found: {args.Name}", "Create it with forge_layer_create first.");
        }

        if (command.DryRun)
        {
            return DryRun(command, new { args.Name, args.IsOn, args.IsFrozen, args.IsLocked, args.IsPlottable, args.ColorIndex, args.MakeCurrent });
        }

        var ltr = (LayerTableRecord)tr.GetObject(layerTable[args.Name], OpenMode.ForWrite);
        var currentName = ((LayerTableRecord)tr.GetObject(db.Clayer, OpenMode.ForRead)).Name;
        if (args.IsFrozen == true && ltr.Name.Equals(currentName, StringComparison.OrdinalIgnoreCase))
        {
            return ForgeResult.Failure(
                command.Id,
                "cannot_freeze_current",
                $"Cannot freeze the current layer: {ltr.Name}",
                "Make another layer current (makeCurrent on a different layer) or thaw instead.");
        }

        if (args.IsOn is { } isOn)
        {
            ltr.IsOff = !isOn;
        }

        if (args.IsFrozen is { } isFrozen)
        {
            ltr.IsFrozen = isFrozen;
        }

        if (args.IsLocked is { } isLocked)
        {
            ltr.IsLocked = isLocked;
        }

        if (args.IsPlottable is { } isPlottable)
        {
            ltr.IsPlottable = isPlottable;
        }

        if (args.ColorIndex is { } colorIndex)
        {
            ltr.Color = Autodesk.AutoCAD.Colors.Color.FromColorIndex(Autodesk.AutoCAD.Colors.ColorMethod.ByAci, (short)colorIndex);
        }

        if (args.MakeCurrent)
        {
            db.Clayer = ltr.Id;
        }

        var readBack = new
        {
            name = ltr.Name,
            isOn = !ltr.IsOff,
            isFrozen = ltr.IsFrozen,
            isLocked = ltr.IsLocked,
            isPlottable = ltr.IsPlottable,
            colorIndex = (int)ltr.Color.ColorIndex,
            isCurrent = ltr.Name.Equals(
                ((LayerTableRecord)tr.GetObject(db.Clayer, OpenMode.ForRead)).Name,
                StringComparison.OrdinalIgnoreCase)
        };
        tr.Commit();
        return ForgeResult.Success(
            command.Id,
            readBack,
            verification: new ForgeVerification { Attempted = true, Passed = true, ReadBack = readBack });
    }

    private ForgeResult ExportDxf(ForgeCommand command)
    {
        var args = Args<ExportDxfArgs>(command);
        if (string.IsNullOrWhiteSpace(args.OutputPath))
        {
            return ForgeResult.Failure(command.Id, "missing_output_path", "outputPath is required.");
        }

        if (RejectControlChars(command, ("outputPath", args.OutputPath)) is { } reject)
        {
            return reject;
        }

        var outputPath = Path.GetFullPath(args.OutputPath);
        var exists = File.Exists(outputPath);
        if (exists && !args.OverwriteAcknowledged)
        {
            return ForgeResult.Failure(
                command.Id,
                "export_overwrite_not_acknowledged",
                $"Refusing to overwrite existing DXF: {outputPath}",
                "Pass overwriteAcknowledged=true after confirming the previous export is archived.");
        }

        if (command.DryRun)
        {
            return DryRun(command, new { outputPath, exists, note = "Exports the in-memory drawing state (unsaved changes included)." });
        }

        string? destinationBackup = null;
        if (exists)
        {
            destinationBackup = _backupPlanner.TryBackup(outputPath);
        }

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
            // DxfOut(string, int precision, bool saveThumbnailImage) — 16 digits keeps metro-scale
            // coordinates lossless in the ASCII DXF.
            ActiveDb.DxfOut(outputPath, 16, false);
        }
        catch (System.Exception ex)
        {
            return ForgeResult.Failure(
                command.Id,
                "export_dxf_failed",
                $"DxfOut failed: {ex.Message}",
                "The drawing may contain proxies or be in an unstable state — save the DWG first and retry.");
        }

        var bytes = new FileInfo(outputPath).Length;
        return ForgeResult.Success(
            command.Id,
            new { outputPath, bytes, destinationBackup },
            verification: new ForgeVerification
            {
                Attempted = true,
                Passed = bytes > 0,
                Message = "DXF written; bytes read back.",
                ReadBack = new { outputPath, bytes }
            });
    }

    private static ForgeResult DrawPolylineFromCsv(ForgeCommand command)
    {
        var args = Args<PolylineCsvArgs>(command);
        if (string.IsNullOrWhiteSpace(args.CsvPath))
        {
            return ForgeResult.Failure(command.Id, "missing_csv_path", "csvPath is required.");
        }

        if (RejectControlChars(command, ("csvPath", args.CsvPath)) is { } reject)
        {
            return reject;
        }

        if (!File.Exists(args.CsvPath))
        {
            return ForgeResult.Failure(command.Id, "csv_not_found", $"CSV not found: {args.CsvPath}");
        }

        var xColumn = Math.Max(0, args.XColumn);
        var yColumn = Math.Max(0, args.YColumn);
        var delimiter = string.IsNullOrEmpty(args.Delimiter) ? "," : args.Delimiter!;
        var vertices = new List<double[]>();
        var parseErrors = new List<object>();
        var droppedLines = 0;
        try
        {
            foreach (var (line, index) in File.ReadLines(args.CsvPath).Select((l, i) => (l, i + 1)))
            {
                if (string.IsNullOrWhiteSpace(line))
                {
                    continue;
                }

                if (args.HasHeader && index == 1)
                {
                    continue;
                }

                var cells = line.Split(delimiter);
                if (cells.Length <= Math.Max(xColumn, yColumn) ||
                    !double.TryParse(cells[xColumn], NumberStyles.Float, CultureInfo.InvariantCulture, out var x) ||
                    !double.TryParse(cells[yColumn], NumberStyles.Float, CultureInfo.InvariantCulture, out var y))
                {
                    if (parseErrors.Count < 5)
                    {
                        parseErrors.Add(new { line = index, text = line.Trim() });
                    }

                    droppedLines++;
                    continue;
                }

                vertices.Add([x, y]);
            }
        }
        catch (System.Exception ex)
        {
            return ForgeResult.Failure(command.Id, "csv_read_failed", $"Reading the CSV failed: {ex.Message}");
        }

        if (vertices.Count < 2)
        {
            return ForgeResult.Failure(
                command.Id,
                "too_few_vertices",
                $"The CSV yielded {vertices.Count} usable points (at least 2 required).",
                "Check csvPath, delimiter, xColumn/yColumn and hasHeader; coordinates must use '.' decimal separators.");
        }

        var db = ActiveDb;
        using var tr = db.TransactionManager.StartTransaction();
        if (args.Layer is not null && !LayerExists(db, tr, args.Layer))
        {
            return ForgeResult.Failure(command.Id, "layer_not_found", $"Layer not found: {args.Layer}", "Create it with forge_layer_create first.");
        }

        if (command.DryRun)
        {
            return DryRun(command, new { args.CsvPath, pointCount = vertices.Count, parseErrors, closed = args.Closed, args.Layer });
        }

        ObjectId id;
        try
        {
            id = CreatePolyline(db, tr, new DrawBatchStep
            {
                Vertices = vertices.ToArray(),
                Closed = args.Closed,
                ConstantWidth = args.ConstantWidth,
                Layer = args.Layer,
                ColorIndex = args.ColorIndex
            });
        }
        catch (System.Exception ex) when (ex is DrawStepException or ArgumentException)
        {
            var (code, message) = StepError(ex);
            return ForgeResult.Failure(command.Id, code, message);
        }

        var pl = (Polyline)tr.GetObject(id, OpenMode.ForRead);
        var readBack = new
        {
            handle = HandleString(pl),
            pointCount = pl.NumberOfVertices,
            closed = pl.Closed,
            length = TryLength(pl)
        };
        tr.Commit();
        return ForgeResult.Success(
            command.Id,
            new { handle = readBack.handle, sourceCsv = args.CsvPath, droppedLines },
            verification: new ForgeVerification { Attempted = true, Passed = true, ReadBack = readBack });
    }

    private static ForgeResult ViewportAdd(ForgeCommand command)
    {
        var args = Args<ViewportAddArgs>(command);
        if (string.IsNullOrWhiteSpace(args.Layout))
        {
            return ForgeResult.Failure(command.Id, "missing_layout", "layout is required.", "Call forge_doc_list_layouts for the available tabs.");
        }

        if (args.Width is not > 0 || args.Height is not > 0)
        {
            return ForgeResult.Failure(command.Id, "invalid_size", "width and height must be greater than 0 (paper-space drawing units).");
        }

        Point3d center;
        try
        {
            center = ToPoint3d(args.Center);
        }
        catch (ArgumentException ex)
        {
            return ForgeResult.Failure(command.Id, "invalid_args", $"center: {ex.Message}");
        }

        var db = ActiveDb;
        using var tr = db.TransactionManager.StartTransaction();
        var layoutDict = (DBDictionary)tr.GetObject(db.LayoutDictionaryId, OpenMode.ForRead);
        if (!layoutDict.Contains(args.Layout))
        {
            return ForgeResult.Failure(command.Id, "layout_not_found", $"Layout not found: {args.Layout}", "Call forge_doc_list_layouts for the available tabs.");
        }

        var layout = (Layout)tr.GetObject(layoutDict.GetAt(args.Layout), OpenMode.ForRead);
        var paperSpace = (BlockTableRecord)tr.GetObject(layout.BlockTableRecordId, OpenMode.ForWrite);
        var viewport = new Viewport
        {
            CenterPoint = center,
            Width = args.Width,
            Height = args.Height,
            On = true
        };
        paperSpace.AppendEntity(viewport);
        tr.AddNewlyCreatedDBObject(viewport, true);

        var readBack = new
        {
            handle = HandleString(viewport),
            number = viewport.Number,
            layout = args.Layout,
            center = new[] { center.X, center.Y, center.Z },
            width = args.Width,
            height = args.Height,
            note = "The viewport shows model space 1:1 at its default view; adjust with viewport tools when needed."
        };
        tr.Commit();
        return ForgeResult.Success(
            command.Id,
            new { handle = readBack.handle, number = readBack.number, layout = args.Layout },
            verification: new ForgeVerification { Attempted = true, Passed = true, ReadBack = readBack });
    }

    private sealed record DocNewArgs { public string? TemplatePath { get; init; } }
    private sealed record LayerSetArgs
    {
        public string? Name { get; init; }
        public bool? IsOn { get; init; }
        public bool? IsFrozen { get; init; }
        public bool? IsLocked { get; init; }
        public bool? IsPlottable { get; init; }
        public int? ColorIndex { get; init; }
        public bool MakeCurrent { get; init; }
    }
    private sealed record ExportDxfArgs { public string? OutputPath { get; init; } public bool OverwriteAcknowledged { get; init; } }
    private sealed record PolylineCsvArgs
    {
        public string? CsvPath { get; init; }
        public bool HasHeader { get; init; }
        public string? Delimiter { get; init; }
        public int XColumn { get; init; }
        public int YColumn { get; init; } = 1;
        public bool Closed { get; init; }
        public double? ConstantWidth { get; init; }
        public string? Layer { get; init; }
        public int? ColorIndex { get; init; }
    }
    private sealed record ViewportAddArgs
    {
        public string? Layout { get; init; }
        public double[]? Center { get; init; }
        public double Width { get; init; }
        public double Height { get; init; }
    }
}
