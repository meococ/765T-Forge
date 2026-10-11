using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using Forge.Shared;

namespace Forge.Plugin;

// docs/cad-authoring-tools.md C20 — forge_draw_* typed authoring tools. Pure DB API: no
// dialogs, no prompts, no Editor.Command. Coordinates are WCS, angles arrive in degrees
// at the boundary, results carry the new handle plus an entity read-back. All tools are
// additive-only: they create entities and never touch existing geometry.
public sealed partial class PluginCommandProcessor
{
    internal const int DrawBatchMaxSteps = 500;

    private static ForgeResult DrawLine(ForgeCommand command)
    {
        var args = Args<DrawLineArgs>(command);
        Point3d start;
        Point3d end;
        try
        {
            start = ToPoint3d(args.Start);
            end = ToPoint3d(args.End);
        }
        catch (ArgumentException ex)
        {
            return ForgeResult.Failure(command.Id, "invalid_args", ex.Message);
        }

        var db = ActiveDb;
        using var tr = db.TransactionManager.StartTransaction();
        if (args.Layer is not null && !LayerExists(db, tr, args.Layer))
        {
            return ForgeResult.Failure(command.Id, "layer_not_found", $"Layer not found: {args.Layer}", "Create the layer first with forge_layer_create.");
        }

        if (command.DryRun)
        {
            return DryRun(command, new { tool = "forge_draw_line", start = PointList(start), end = PointList(end), args.Layer, args.ColorIndex });
        }

        ObjectId id;
        try
        {
            id = CreateLine(db, tr, new DrawBatchStep { Start = args.Start, End = args.End, Layer = args.Layer, ColorIndex = args.ColorIndex });
        }
        catch (System.Exception ex) when (ex is DrawStepException or ArgumentException)
        {
            var (code, message) = StepError(ex);
            return ForgeResult.Failure(command.Id, code, message);
        }

        var line = (Line)tr.GetObject(id, OpenMode.ForRead);
        var handle = HandleString(line);
        var readBack = EntitySummary(line);
        tr.Commit();
        return ForgeResult.Success(
            command.Id,
            new { handle },
            verification: new ForgeVerification { Attempted = true, Passed = true, ReadBack = readBack });
    }

    private static ForgeResult DrawPolyline(ForgeCommand command)
    {
        var args = Args<DrawPolylineArgs>(command);
        if (args.Vertices is null || args.Vertices.Length < 2)
        {
            return ForgeResult.Failure(command.Id, "too_few_vertices", "At least two vertices are required.", "Provide at least two [x, y] vertices.");
        }

        try
        {
            ValidateVertices(args.Vertices);
        }
        catch (DrawStepException ex)
        {
            return ForgeResult.Failure(command.Id, ex.Code, ex.Message);
        }

        var db = ActiveDb;
        using var tr = db.TransactionManager.StartTransaction();
        if (args.Layer is not null && !LayerExists(db, tr, args.Layer))
        {
            return ForgeResult.Failure(command.Id, "layer_not_found", $"Layer not found: {args.Layer}", "Create the layer first with forge_layer_create.");
        }

        if (command.DryRun)
        {
            return DryRun(command, new { tool = "forge_draw_polyline", vertexCount = args.Vertices.Length, closed = args.Closed, bulgeCount = args.Bulges?.Length ?? 0, constantWidth = args.ConstantWidth ?? 0, args.Layer, args.ColorIndex });
        }

        ObjectId id;
        try
        {
            id = CreatePolyline(db, tr, new DrawBatchStep { Vertices = args.Vertices, Closed = args.Closed, ConstantWidth = args.ConstantWidth, Layer = args.Layer, ColorIndex = args.ColorIndex }, args.Bulges);
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
            vertexCount = pl.NumberOfVertices,
            closed = pl.Closed,
            length = TryLength(pl)
        };
        tr.Commit();
        return ForgeResult.Success(
            command.Id,
            new { handle = readBack.handle },
            verification: new ForgeVerification { Attempted = true, Passed = true, ReadBack = readBack });
    }

    private static ForgeResult DrawCircle(ForgeCommand command)
    {
        var args = Args<DrawCircleArgs>(command);
        if (args.Radius is not > 0)
        {
            return ForgeResult.Failure(command.Id, "invalid_radius", "Radius must be greater than 0.");
        }

        Point3d center;
        try
        {
            center = ToPoint3d(args.Center);
        }
        catch (ArgumentException ex)
        {
            return ForgeResult.Failure(command.Id, "invalid_args", ex.Message);
        }

        var db = ActiveDb;
        using var tr = db.TransactionManager.StartTransaction();
        if (args.Layer is not null && !LayerExists(db, tr, args.Layer))
        {
            return ForgeResult.Failure(command.Id, "layer_not_found", $"Layer not found: {args.Layer}", "Create the layer first with forge_layer_create.");
        }

        if (command.DryRun)
        {
            return DryRun(command, new { tool = "forge_draw_circle", center = PointList(center), args.Radius, args.Layer, args.ColorIndex });
        }

        ObjectId id;
        try
        {
            id = CreateCircle(db, tr, new DrawBatchStep { Center = args.Center, Radius = args.Radius, Layer = args.Layer, ColorIndex = args.ColorIndex });
        }
        catch (System.Exception ex) when (ex is DrawStepException or ArgumentException)
        {
            var (code, message) = StepError(ex);
            return ForgeResult.Failure(command.Id, code, message);
        }

        var circle = (Circle)tr.GetObject(id, OpenMode.ForRead);
        var handle = HandleString(circle);
        var readBack = EntitySummary(circle);
        tr.Commit();
        return ForgeResult.Success(
            command.Id,
            new { handle },
            verification: new ForgeVerification { Attempted = true, Passed = true, ReadBack = readBack });
    }

    private static ForgeResult DrawArc(ForgeCommand command)
    {
        var args = Args<DrawArcArgs>(command);
        if (args.Radius is not > 0)
        {
            return ForgeResult.Failure(command.Id, "invalid_radius", "Radius must be greater than 0.");
        }

        // CCW sweep in degrees; end == start (mod 360) draws nothing.
        if ((args.EndAngleDeg - args.StartAngleDeg) % 360 == 0)
        {
            return ForgeResult.Failure(command.Id, "zero_sweep", "Arc start and end angles must not coincide (mod 360).");
        }

        Point3d center;
        try
        {
            center = ToPoint3d(args.Center);
        }
        catch (ArgumentException ex)
        {
            return ForgeResult.Failure(command.Id, "invalid_args", ex.Message);
        }

        var db = ActiveDb;
        using var tr = db.TransactionManager.StartTransaction();
        if (args.Layer is not null && !LayerExists(db, tr, args.Layer))
        {
            return ForgeResult.Failure(command.Id, "layer_not_found", $"Layer not found: {args.Layer}", "Create the layer first with forge_layer_create.");
        }

        if (command.DryRun)
        {
            return DryRun(command, new { tool = "forge_draw_arc", center = PointList(center), args.Radius, args.StartAngleDeg, args.EndAngleDeg, args.Layer, args.ColorIndex });
        }

        ObjectId id;
        try
        {
            id = CreateArc(db, tr, new DrawBatchStep { Center = args.Center, Radius = args.Radius, StartAngleDeg = args.StartAngleDeg, EndAngleDeg = args.EndAngleDeg, Layer = args.Layer, ColorIndex = args.ColorIndex });
        }
        catch (System.Exception ex) when (ex is DrawStepException or ArgumentException)
        {
            var (code, message) = StepError(ex);
            return ForgeResult.Failure(command.Id, code, message);
        }

        var arc = (Arc)tr.GetObject(id, OpenMode.ForRead);
        var handle = HandleString(arc);
        var readBack = EntitySummary(arc);
        tr.Commit();
        return ForgeResult.Success(
            command.Id,
            new { handle },
            verification: new ForgeVerification { Attempted = true, Passed = true, ReadBack = readBack });
    }

    private static ForgeResult DrawText(ForgeCommand command)
    {
        var args = Args<DrawTextArgs>(command);
        if (args.Height is not > 0)
        {
            return ForgeResult.Failure(command.Id, "invalid_height", "Height must be greater than 0.");
        }

        if (string.IsNullOrWhiteSpace(args.Text))
        {
            return ForgeResult.Failure(command.Id, "missing_text", "Text is required.");
        }

        if (RejectControlChars(command, ("text", args.Text)) is { } reject)
        {
            return reject;
        }

        Point3d position;
        try
        {
            position = ToPoint3d(args.Position);
        }
        catch (ArgumentException ex)
        {
            return ForgeResult.Failure(command.Id, "invalid_args", ex.Message);
        }

        var db = ActiveDb;
        using var tr = db.TransactionManager.StartTransaction();
        if (args.Layer is not null && !LayerExists(db, tr, args.Layer))
        {
            return ForgeResult.Failure(command.Id, "layer_not_found", $"Layer not found: {args.Layer}", "Create the layer first with forge_layer_create.");
        }

        if (command.DryRun)
        {
            return DryRun(command, new { tool = "forge_draw_text", position = PointList(position), args.Height, args.Text, args.RotationDeg, args.Layer, args.ColorIndex });
        }

        ObjectId id;
        try
        {
            id = CreateText(db, tr, new DrawBatchStep { Position = args.Position, Height = args.Height, Text = args.Text, RotationDeg = args.RotationDeg, Layer = args.Layer, ColorIndex = args.ColorIndex });
        }
        catch (System.Exception ex) when (ex is DrawStepException or ArgumentException)
        {
            var (code, message) = StepError(ex);
            return ForgeResult.Failure(command.Id, code, message);
        }

        var text = (DBText)tr.GetObject(id, OpenMode.ForRead);
        var handle = HandleString(text);
        var readBack = EntitySummary(text);
        tr.Commit();
        return ForgeResult.Success(
            command.Id,
            new { handle },
            verification: new ForgeVerification { Attempted = true, Passed = true, ReadBack = readBack });
    }

    private static ForgeResult DrawMText(ForgeCommand command)
    {
        var args = Args<DrawMTextArgs>(command);
        if (string.IsNullOrWhiteSpace(args.Text))
        {
            return ForgeResult.Failure(command.Id, "missing_text", "Text is required.");
        }

        // Newlines are legal in MText (converted to \P); no RejectControlChars here.
        var attachment = AttachmentPoint.TopLeft;
        if (args.AttachmentPoint is not null && !Enum.TryParse(args.AttachmentPoint, ignoreCase: true, out attachment))
        {
            return ForgeResult.Failure(command.Id, "invalid_attachment", $"Unknown attachmentPoint: {args.AttachmentPoint}", "Use one of the nine MText attachment points, e.g. topLeft or middleCenter.");
        }

        Point3d position;
        try
        {
            position = ToPoint3d(args.Position);
        }
        catch (ArgumentException ex)
        {
            return ForgeResult.Failure(command.Id, "invalid_args", ex.Message);
        }

        var db = ActiveDb;
        using var tr = db.TransactionManager.StartTransaction();
        if (args.Layer is not null && !LayerExists(db, tr, args.Layer))
        {
            return ForgeResult.Failure(command.Id, "layer_not_found", $"Layer not found: {args.Layer}", "Create the layer first with forge_layer_create.");
        }

        if (command.DryRun)
        {
            return DryRun(command, new { tool = "forge_draw_mtext", position = PointList(position), args.Height, contents = MTextContents(args.Text), width = args.Width ?? 0, attachmentPoint = attachment.ToString(), args.RotationDeg, args.Layer, args.ColorIndex });
        }

        ObjectId id;
        try
        {
            id = CreateMText(db, tr, new DrawBatchStep { Position = args.Position, Height = args.Height, Text = args.Text, RotationDeg = args.RotationDeg, Layer = args.Layer, ColorIndex = args.ColorIndex }, args.Width, attachment);
        }
        catch (System.Exception ex) when (ex is DrawStepException or ArgumentException)
        {
            var (code, message) = StepError(ex);
            return ForgeResult.Failure(command.Id, code, message);
        }

        var mtext = (MText)tr.GetObject(id, OpenMode.ForRead);
        var handle = HandleString(mtext);
        var readBack = EntitySummary(mtext);
        tr.Commit();
        return ForgeResult.Success(
            command.Id,
            new { handle },
            verification: new ForgeVerification { Attempted = true, Passed = true, ReadBack = readBack });
    }

    private static ForgeResult DrawHatch(ForgeCommand command)
    {
        var args = Args<DrawHatchArgs>(command);
        if (args.Boundary is null || (args.Boundary.Handle is null) == (args.Boundary.Vertices is null))
        {
            return ForgeResult.Failure(command.Id, "missing_boundary", "Provide exactly one of boundary.handle or boundary.vertices.");
        }

        var db = ActiveDb;
        using var tr = db.TransactionManager.StartTransaction();
        if (args.Layer is not null && !LayerExists(db, tr, args.Layer))
        {
            return ForgeResult.Failure(command.Id, "layer_not_found", $"Layer not found: {args.Layer}", "Create the layer first with forge_layer_create.");
        }

        if (command.DryRun)
        {
            return DryRun(command, new
            {
                tool = "forge_draw_hatch",
                pattern = args.Pattern ?? "ANSI31",
                scale = args.Scale ?? 1,
                args.AngleDeg,
                boundaryMode = args.Boundary.Handle is null ? "vertices" : "handle",
                boundaryHandle = args.Boundary.Handle,
                vertexCount = args.Boundary.Vertices?.Length,
                args.Layer,
                associative = true
            });
        }

        Entity boundary;
        var boundaryCreated = false;
        try
        {
            if (args.Boundary.Handle is not null)
            {
                var existing = TryOpenEntityForRead(db, tr, args.Boundary.Handle);
                if (existing is null)
                {
                    return ForgeResult.Failure(command.Id, "boundary_not_found", $"Boundary handle not found: {args.Boundary.Handle}");
                }

                if (existing is not Curve curve || !curve.Closed)
                {
                    return ForgeResult.Failure(command.Id, "boundary_not_closed", "Boundary must be a closed curve (e.g. a closed polyline or circle).");
                }

                boundary = existing;
            }
            else
            {
                ValidateVertices(args.Boundary.Vertices);
                var boundaryId = CreatePolyline(db, tr, new DrawBatchStep { Vertices = args.Boundary.Vertices, Closed = true, Layer = args.Layer });
                boundary = (Polyline)tr.GetObject(boundaryId, OpenMode.ForRead);
                boundaryCreated = true;
            }
        }
        catch (System.Exception ex) when (ex is DrawStepException or ArgumentException)
        {
            var (code, message) = StepError(ex);
            return ForgeResult.Failure(command.Id, code, message);
        }

        // Ordering is load-bearing (docs/cad-authoring-tools.md cookbook): append before
        // SetHatchPattern, pattern before scale/angle, Associative before AppendLoop, and
        // EvaluateHatch last or the hatch renders blank. An unknown pattern name throws
        // here and is carried by the dispatcher's plugin_exception.
        var hatch = new Hatch();
        AppendToModelSpace(db, tr, hatch);
        hatch.SetHatchPattern(HatchPatternType.PreDefined, args.Pattern ?? "ANSI31");
        hatch.PatternScale = args.Scale ?? 1;
        hatch.PatternAngle = DegreesToRadians(args.AngleDeg);
        hatch.Associative = true;
        var loop = new ObjectIdCollection { boundary.ObjectId };
        hatch.AppendLoop(HatchLoopTypes.Outermost, loop);
        hatch.EvaluateHatch(true);
        if (args.Layer is not null)
        {
            hatch.Layer = args.Layer;
        }

        var readBack = new
        {
            handle = HandleString(hatch),
            pattern = hatch.PatternName,
            scale = hatch.PatternScale,
            angleDeg = args.AngleDeg,
            boundaryHandle = HandleString(boundary),
            boundaryCreated,
            associative = hatch.Associative
        };
        tr.Commit();
        return ForgeResult.Success(
            command.Id,
            new { handle = readBack.handle, boundaryHandle = readBack.boundaryHandle, boundaryCreated },
            verification: new ForgeVerification { Attempted = true, Passed = true, ReadBack = readBack });
    }

    private static ForgeResult DrawDimension(ForgeCommand command)
    {
        var args = Args<DrawDimensionArgs>(command);
        var kind = args.Kind?.ToLowerInvariant() ?? "";
        if (kind is not ("linear" or "aligned" or "radius" or "diameter"))
        {
            return ForgeResult.Failure(command.Id, "unsupported_kind", $"Unsupported dimension kind: {args.Kind ?? "<missing>"}. Use linear, aligned, radius or diameter.", "angular dimensions are on the C24 roadmap");
        }

        if (args.P1 is null || args.P2 is null)
        {
            return ForgeResult.Failure(command.Id, "invalid_points", "p1 and p2 are required.");
        }

        if ((kind == "linear" || kind == "aligned") && args.DimLinePoint is null)
        {
            return ForgeResult.Failure(command.Id, "invalid_points", "dimLinePoint is required for linear and aligned dimensions.");
        }

        Point3d p1;
        Point3d p2;
        Point3d? dimLinePoint = null;
        try
        {
            p1 = ToPoint3d(args.P1);
            p2 = ToPoint3d(args.P2);
            if (args.DimLinePoint is not null)
            {
                dimLinePoint = ToPoint3d(args.DimLinePoint);
            }
        }
        catch (ArgumentException ex)
        {
            return ForgeResult.Failure(command.Id, "invalid_args", ex.Message);
        }

        var db = ActiveDb;
        using var tr = db.TransactionManager.StartTransaction();
        if (args.Layer is not null && !LayerExists(db, tr, args.Layer))
        {
            return ForgeResult.Failure(command.Id, "layer_not_found", $"Layer not found: {args.Layer}", "Create the layer first with forge_layer_create.");
        }

        if (command.DryRun)
        {
            return DryRun(command, new
            {
                tool = "forge_draw_dimension",
                kind,
                p1 = PointList(p1),
                p2 = PointList(p2),
                dimLinePoint = dimLinePoint is null ? null : PointList(dimLinePoint.Value),
                args.TextOverride,
                args.Layer
            });
        }

        var styleId = db.Dimstyle;
        Dimension dim = kind switch
        {
            "linear" => new RotatedDimension(0, p1, p2, dimLinePoint!.Value, "", styleId),
            "aligned" => new AlignedDimension(p1, p2, dimLinePoint!.Value, "", styleId),
            "radius" => new RadialDimension(p1, p2, 0, "", styleId),
            _ => new DiametricDimension(p1, p2, 0, "", styleId)
        };

        AppendToModelSpace(db, tr, dim);
        if (args.Layer is not null)
        {
            dim.Layer = args.Layer;
        }

        if (!string.IsNullOrEmpty(args.TextOverride) && args.TextOverride != "<>")
        {
            dim.DimensionText = args.TextOverride;
        }

        var readBack = new { handle = HandleString(dim), kind, measurement = TryMeasurement(dim) };
        tr.Commit();
        return ForgeResult.Success(
            command.Id,
            new { handle = readBack.handle, kind },
            verification: new ForgeVerification { Attempted = true, Passed = true, ReadBack = readBack });
    }

    private static ForgeResult DrawBatch(ForgeCommand command)
    {
        var args = Args<DrawBatchArgs>(command);
        if (args.Steps is null || args.Steps.Length == 0)
        {
            return ForgeResult.Failure(command.Id, "missing_steps", "steps is required and must not be empty.");
        }

        if (args.Steps.Length > DrawBatchMaxSteps)
        {
            return ForgeResult.Failure(command.Id, "batch_too_large", $"forge_draw_batch accepts at most {DrawBatchMaxSteps} steps.", "Split the work into smaller batches.");
        }

        if (command.DryRun)
        {
            return DryRun(command, new { tool = "forge_draw_batch", steps = args.Steps.Length, tools = args.Steps.Select(step => step.Tool).ToArray() });
        }

        var db = ActiveDb;
        using var tr = db.TransactionManager.StartTransaction();
        var results = new List<object>();
        var createdHandles = new List<string>();
        for (var index = 0; index < args.Steps.Length; index++)
        {
            var step = args.Steps[index];
            var tool = step.Tool?.ToLowerInvariant() ?? "";
            try
            {
                var id = tool switch
                {
                    "line" => CreateLine(db, tr, step),
                    "polyline" => CreatePolyline(db, tr, step),
                    "circle" => CreateCircle(db, tr, step),
                    "arc" => CreateArc(db, tr, step),
                    "text" => CreateText(db, tr, step),
                    "mtext" => CreateMText(db, tr, step),
                    _ => throw new DrawStepException("unsupported_tool", $"Unsupported batch tool: {step.Tool ?? "<missing>"}")
                };

                var handle = id.Handle.ToString();
                createdHandles.Add(handle);
                results.Add(new { index, tool, ok = true, handle });
            }
            catch (System.Exception ex)
            {
                // Record the failed step and continue — the batch contract keeps going.
                var (code, message) = StepError(ex);
                results.Add(new { index, tool, ok = false, error = new { code, message } });
            }
        }

        tr.Commit();
        var created = createdHandles.Count;
        var failed = args.Steps.Length - created;
        return ForgeResult.Success(
            command.Id,
            new { created, failed, results, createdHandles },
            verification: new ForgeVerification
            {
                Attempted = true,
                Passed = failed == 0,
                Message = "Batch ran in one transaction and continued past failed steps; per-step results were read back.",
                ReadBack = new { created, failed, results }
            });
    }

    // Creation cores shared by the single-tool handlers and the forge_draw_batch steps.
    // The caller owns the transaction: cores never open one and never touch DryRun. They
    // validate before appending (so a rejected step leaves nothing behind) and throw
    // DrawStepException with the wire error code so the batch can record and continue.
    private static ObjectId CreateLine(Database db, Transaction tr, DrawBatchStep step)
    {
        RequireValidColor(step.ColorIndex);
        RequireLayer(db, tr, step.Layer);
        var line = new Line(ToPoint3d(step.Start), ToPoint3d(step.End));
        AppendToModelSpace(db, tr, line);
        if (step.Layer is not null)
        {
            line.Layer = step.Layer;
        }

        ApplyColor(line, step.ColorIndex);
        return line.ObjectId;
    }

    private static ObjectId CreatePolyline(Database db, Transaction tr, DrawBatchStep step, double[]? bulges = null)
    {
        RequireValidColor(step.ColorIndex);
        RequireLayer(db, tr, step.Layer);
        ValidateVertices(step.Vertices);
        var vertices = step.Vertices!;
        var width = step.ConstantWidth ?? 0;
        var pl = new Polyline();
        for (var i = 0; i < vertices.Length; i++)
        {
            var bulge = bulges is not null && i < bulges.Length ? bulges[i] : 0;
            pl.AddVertexAt(i, new Point2d(vertices[i][0], vertices[i][1]), bulge, width, width);
        }

        pl.Closed = step.Closed;
        AppendToModelSpace(db, tr, pl);
        if (step.Layer is not null)
        {
            pl.Layer = step.Layer;
        }

        ApplyColor(pl, step.ColorIndex);
        return pl.ObjectId;
    }

    private static ObjectId CreateCircle(Database db, Transaction tr, DrawBatchStep step)
    {
        RequireValidColor(step.ColorIndex);
        RequireLayer(db, tr, step.Layer);
        if (step.Radius is not > 0)
        {
            throw new DrawStepException("invalid_radius", "Radius must be greater than 0.");
        }

        var circle = new Circle(ToPoint3d(step.Center), Vector3d.ZAxis, step.Radius.Value);
        AppendToModelSpace(db, tr, circle);
        if (step.Layer is not null)
        {
            circle.Layer = step.Layer;
        }

        ApplyColor(circle, step.ColorIndex);
        return circle.ObjectId;
    }

    private static ObjectId CreateArc(Database db, Transaction tr, DrawBatchStep step)
    {
        RequireValidColor(step.ColorIndex);
        RequireLayer(db, tr, step.Layer);
        if (step.Radius is not > 0)
        {
            throw new DrawStepException("invalid_radius", "Radius must be greater than 0.");
        }

        if (step.StartAngleDeg is null || step.EndAngleDeg is null)
        {
            throw new DrawStepException("invalid_args", "startAngleDeg and endAngleDeg are required.");
        }

        if ((step.EndAngleDeg.Value - step.StartAngleDeg.Value) % 360 == 0)
        {
            throw new DrawStepException("zero_sweep", "Arc start and end angles must not coincide (mod 360).");
        }

        var arc = new Arc(ToPoint3d(step.Center), step.Radius.Value, DegreesToRadians(step.StartAngleDeg.Value), DegreesToRadians(step.EndAngleDeg.Value));
        AppendToModelSpace(db, tr, arc);
        if (step.Layer is not null)
        {
            arc.Layer = step.Layer;
        }

        ApplyColor(arc, step.ColorIndex);
        return arc.ObjectId;
    }

    private static ObjectId CreateText(Database db, Transaction tr, DrawBatchStep step)
    {
        RequireValidColor(step.ColorIndex);
        RequireLayer(db, tr, step.Layer);
        if (step.Height is not > 0)
        {
            throw new DrawStepException("invalid_height", "Height must be greater than 0.");
        }

        if (string.IsNullOrWhiteSpace(step.Text))
        {
            throw new DrawStepException("missing_text", "Text is required.");
        }

        if (step.Text.IndexOfAny(CommandControlChars) >= 0)
        {
            throw new DrawStepException("illegal_control_character", "Parameter 'text' contains a newline; single-line text requires single-line values.");
        }

        var text = new DBText
        {
            Position = ToPoint3d(step.Position),
            Height = step.Height.Value,
            Rotation = DegreesToRadians(step.RotationDeg ?? 0),
            TextString = step.Text
        };
        AppendToModelSpace(db, tr, text);
        if (step.Layer is not null)
        {
            text.Layer = step.Layer;
        }

        ApplyColor(text, step.ColorIndex);
        return text.ObjectId;
    }

    private static ObjectId CreateMText(Database db, Transaction tr, DrawBatchStep step, double? width = null, AttachmentPoint attachment = AttachmentPoint.TopLeft)
    {
        RequireValidColor(step.ColorIndex);
        RequireLayer(db, tr, step.Layer);
        if (string.IsNullOrWhiteSpace(step.Text))
        {
            throw new DrawStepException("missing_text", "Text is required.");
        }

        var mtext = new MText
        {
            Location = ToPoint3d(step.Position),
            Contents = MTextContents(step.Text),
            TextHeight = step.Height ?? 0,
            Width = width ?? 0,
            Attachment = attachment,
            Rotation = DegreesToRadians(step.RotationDeg ?? 0)
        };
        AppendToModelSpace(db, tr, mtext);
        if (step.Layer is not null)
        {
            mtext.Layer = step.Layer;
        }

        ApplyColor(mtext, step.ColorIndex);
        return mtext.ObjectId;
    }

    private static void RequireLayer(Database db, Transaction tr, string? layer)
    {
        if (layer is not null && !LayerExists(db, tr, layer))
        {
            throw new DrawStepException("layer_not_found", $"Layer not found: {layer}");
        }
    }

    private static void RequireValidColor(int? colorIndex)
    {
        if (colorIndex is < 0 or > 256)
        {
            throw new DrawStepException("invalid_args", "colorIndex must be 0 (ByBlock), 1–255, or 256 (ByLayer).");
        }
    }

    private static void ValidateVertices(double[][]? vertices)
    {
        if (vertices is null || vertices.Length < 2)
        {
            throw new DrawStepException("too_few_vertices", "At least two vertices are required.");
        }

        foreach (var vertex in vertices)
        {
            if (vertex is null || vertex.Length < 2 || vertex.Any(double.IsNaN) || vertex.Any(double.IsInfinity))
            {
                throw new DrawStepException("invalid_args", "Each vertex must be [x, y] with finite values.");
            }
        }
    }

    private static (string Code, string Message) StepError(System.Exception ex) => ex switch
    {
        DrawStepException step => (step.Code, step.Message),
        ArgumentException => ("invalid_args", ex.Message),
        _ => ("step_failed", ex.Message)
    };

    private static double[] PointList(Point3d point) => [point.X, point.Y, point.Z];

    private static string MTextContents(string text) => text.Replace("\r\n", "\\P").Replace("\n", "\\P");

    private static double? TryLength(Polyline pl)
    {
        try
        {
            return pl.Length;
        }
        catch
        {
            return null;
        }
    }

    private static double? TryMeasurement(Dimension dim)
    {
        try
        {
            return dim.Measurement;
        }
        catch
        {
            return null;
        }
    }

    private sealed class DrawStepException(string code, string message) : System.Exception(message)
    {
        public string Code { get; } = code;
    }

    private sealed record DrawLineArgs { public double[]? Start { get; init; } public double[]? End { get; init; } public string? Layer { get; init; } public int? ColorIndex { get; init; } }
    private sealed record DrawPolylineArgs { public double[][]? Vertices { get; init; } public bool Closed { get; init; } public double[]? Bulges { get; init; } public double? ConstantWidth { get; init; } public string? Layer { get; init; } public int? ColorIndex { get; init; } }
    private sealed record DrawCircleArgs { public double[]? Center { get; init; } public double Radius { get; init; } public string? Layer { get; init; } public int? ColorIndex { get; init; } }
    private sealed record DrawArcArgs { public double[]? Center { get; init; } public double Radius { get; init; } public double StartAngleDeg { get; init; } public double EndAngleDeg { get; init; } public string? Layer { get; init; } public int? ColorIndex { get; init; } }
    private sealed record DrawTextArgs { public double[]? Position { get; init; } public double Height { get; init; } public string? Text { get; init; } public double RotationDeg { get; init; } public string? Layer { get; init; } public int? ColorIndex { get; init; } }
    private sealed record DrawMTextArgs { public double[]? Position { get; init; } public double Height { get; init; } public string? Text { get; init; } public double? Width { get; init; } public string? AttachmentPoint { get; init; } public double RotationDeg { get; init; } public string? Layer { get; init; } public int? ColorIndex { get; init; } }
    private sealed record DrawHatchArgs { public string? Pattern { get; init; } public double? Scale { get; init; } public double AngleDeg { get; init; } public HatchBoundaryArgs? Boundary { get; init; } public string? Layer { get; init; } }
    private sealed record HatchBoundaryArgs { public string? Handle { get; init; } public double[][]? Vertices { get; init; } }
    private sealed record DrawDimensionArgs { public string? Kind { get; init; } public double[]? P1 { get; init; } public double[]? P2 { get; init; } public double[]? DimLinePoint { get; init; } public string? TextOverride { get; init; } public string? Layer { get; init; } }
    private sealed record DrawBatchArgs { public DrawBatchStep[]? Steps { get; init; } }
    private sealed record DrawBatchStep { public string? Tool { get; init; } public double[]? Start { get; init; } public double[]? End { get; init; } public double[][]? Vertices { get; init; } public bool Closed { get; init; } public double? ConstantWidth { get; init; } public double[]? Center { get; init; } public double? Radius { get; init; } public double? StartAngleDeg { get; init; } public double? EndAngleDeg { get; init; } public double[]? Position { get; init; } public double? Height { get; init; } public string? Text { get; init; } public double? RotationDeg { get; init; } public string? Layer { get; init; } public int? ColorIndex { get; init; } }
}
