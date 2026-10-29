using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using Forge.Shared;

namespace Forge.Plugin;

// docs/cad-authoring-tools.md C20 — forge_draw_* typed authoring tools. Pure DB API: no
// dialogs, no prompts, no Editor.Command. Coordinates are WCS, angles arrive in degrees
// at the boundary, results carry the new handle plus an entity read-back. All tools are
// additive-only: they create entities and never touch existing geometry.
//
// Creation order (C25 review): new entity -> SetDatabaseDefaults(db) -> configure (style,
// layer, color, geometry) -> AppendExistingToModelSpace -> resident-only overrides. Defaults
// are applied exactly once and FIRST so they can never reset a dimstyle, an MLeader override
// or a text height the caller configured. Read-backs come from the database object, never
// from the request. An entity that throws before it is database-resident is disposed.
public sealed partial class PluginCommandProcessor
{
    internal const int DrawBatchMaxSteps = 1000;
    internal const int DrawTableMaxCells = 10000;
    private const double DrawDefaultRevCloudBulge = 0.52;

    private static readonly string[] DrawBatchTools =
    [
        "line", "polyline", "rectangle", "polygon", "circle", "arc", "ellipse", "spline",
        "revcloud", "text", "mtext", "hatch", "dimension", "leader", "block_insert"
    ];

    private static readonly HashSet<string> MTextAttachmentNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "TopLeft", "TopCenter", "TopRight", "MiddleLeft", "MiddleCenter", "MiddleRight", "BottomLeft", "BottomCenter", "BottomRight"
    };

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
        catch (System.Exception ex) when (IsStepError(ex))
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
        catch (System.Exception ex) when (IsStepError(ex))
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
        catch (System.Exception ex) when (IsStepError(ex))
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
        catch (System.Exception ex) when (IsStepError(ex))
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
        catch (System.Exception ex) when (IsStepError(ex))
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
        AttachmentPoint attachment;
        try
        {
            attachment = ParseMTextAttachment(args.AttachmentPoint);
        }
        catch (DrawStepException ex)
        {
            return ForgeResult.Failure(command.Id, ex.Code, ex.Message, "Use one of the nine MText attachment points, e.g. topLeft or middleCenter.");
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
            id = CreateMText(db, tr, new DrawBatchStep
            {
                Position = args.Position,
                Height = args.Height,
                Text = args.Text,
                Width = args.Width,
                AttachmentPoint = args.AttachmentPoint,
                RotationDeg = args.RotationDeg,
                Layer = args.Layer,
                ColorIndex = args.ColorIndex
            });
        }
        catch (System.Exception ex) when (IsStepError(ex))
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

        if (args.Scale is { } requestedScale && !(requestedScale > 0 && double.IsFinite(requestedScale)))
        {
            return ForgeResult.Failure(command.Id, "invalid_args", "scale must be a finite number greater than 0.");
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
        catch (System.Exception ex) when (IsStepError(ex))
        {
            var (code, message) = StepError(ex);
            return ForgeResult.Failure(command.Id, code, message);
        }

        // Same ordering as the batch path (CreateHatchOnBoundary). On failure the transaction
        // is not committed, so a boundary created above is rolled back with the hatch.
        ObjectId hatchId;
        try
        {
            hatchId = CreateHatchOnBoundary(db, tr, boundary.ObjectId, args.Pattern ?? "ANSI31", args.Scale ?? 1, args.AngleDeg, associative: true, args.Layer, colorIndex: null);
        }
        catch (System.Exception ex) when (IsStepError(ex))
        {
            var (code, message) = StepError(ex);
            return ForgeResult.Failure(command.Id, code, message, "Check the pattern name (e.g. ANSI31, SOLID) and that the boundary is a closed, non-self-intersecting curve.");
        }

        var hatch = (Hatch)tr.GetObject(hatchId, OpenMode.ForRead);
        var readBack = new
        {
            handle = HandleString(hatch),
            pattern = hatch.PatternName,
            scale = hatch.PatternScale,
            angleDeg = hatch.PatternAngle * 180.0 / Math.PI,
            boundaryHandle = HandleString(boundary),
            boundaryCreated,
            associative = hatch.Associative,
            layer = hatch.Layer
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
        var step = new DrawBatchStep
        {
            Tool = "dimension",
            Kind = args.Kind,
            P1 = args.P1,
            P2 = args.P2,
            Vertex = args.Vertex,
            DimLinePoint = args.DimLinePoint,
            RotationDeg = args.RotationDeg,
            UseXAxis = args.UseXAxis,
            TextOverride = args.TextOverride,
            DimStyle = args.DimStyle,
            DimScale = args.DimScale,
            Layer = args.Layer,
            ColorIndex = args.ColorIndex
        };

        var db = ActiveDb;
        using var tr = db.TransactionManager.StartTransaction();
        if (ValidateForCommand(command, db, tr, step) is { } invalid)
        {
            return invalid;
        }

        if (command.DryRun)
        {
            var plan = PlanDimension(step);
            return DryRun(command, new
            {
                tool = "forge_draw_dimension",
                kind = plan.Kind,
                p1 = PointList(plan.P1),
                p2 = plan.P2 is null ? null : PointList(plan.P2.Value),
                vertex = plan.Vertex is null ? null : PointList(plan.Vertex.Value),
                dimLinePoint = plan.DimLine is null ? null : PointList(plan.DimLine.Value),
                plan.RotationDeg,
                plan.UseXAxis,
                args.TextOverride,
                args.DimStyle,
                args.DimScale,
                args.Layer,
                args.ColorIndex
            });
        }

        if (RunCreate(command, () => CreateDimension(db, tr, step), out var id) is { } failed)
        {
            return failed;
        }

        var dim = (Dimension)tr.GetObject(id, OpenMode.ForRead);
        var kind = NormalizeDimKind(args.Kind);
        var readBack = new
        {
            handle = HandleString(dim),
            kind,
            type = dim.GetType().Name,
            measurement = TryMeasurement(dim),
            dimStyle = dim.DimensionStyleName,
            dimScale = dim.Dimscale,
            dimensionText = dim.DimensionText,
            layer = dim.Layer
        };
        tr.Commit();
        return ForgeResult.Success(
            command.Id,
            new { handle = readBack.handle, kind, measurement = readBack.measurement, dimStyle = readBack.dimStyle },
            verification: new ForgeVerification { Attempted = true, Passed = true, ReadBack = readBack });
    }

    // ===================== C25 — high-level geometry & annotations =====================

    private static ForgeResult DrawRectangle(ForgeCommand command)
    {
        var args = Args<DrawRectangleArgs>(command);
        var step = new DrawBatchStep
        {
            Tool = "rectangle",
            P1 = args.P1,
            P2 = args.P2,
            Corner1 = args.Corner1,
            Corner2 = args.Corner2,
            Center = args.Center,
            Width = args.Width,
            Height = args.Height,
            FilletRadius = args.FilletRadius,
            ChamferDistance1 = args.ChamferDistance1,
            ChamferDistance2 = args.ChamferDistance2,
            RotationDeg = args.RotationDeg,
            ConstantWidth = args.ConstantWidth,
            Layer = args.Layer,
            ColorIndex = args.ColorIndex
        };

        var db = ActiveDb;
        using var tr = db.TransactionManager.StartTransaction();
        if (ValidateForCommand(command, db, tr, step) is { } invalid)
        {
            return invalid;
        }

        if (command.DryRun)
        {
            var spec = PlanRectangle(step);
            return DryRun(command, new
            {
                tool = "forge_draw_rectangle",
                p1 = args.P1 ?? args.Corner1,
                p2 = args.P2 ?? args.Corner2,
                args.Center,
                args.Width,
                args.Height,
                vertexCount = spec.Vertices.Length,
                args.FilletRadius,
                args.ChamferDistance1,
                args.ChamferDistance2,
                args.RotationDeg,
                args.ConstantWidth,
                args.Layer,
                args.ColorIndex
            });
        }

        if (RunCreate(command, () => CreateRectangleStep(db, tr, step), out var id) is { } failed)
        {
            return failed;
        }

        var pl = (Polyline)tr.GetObject(id, OpenMode.ForRead);
        var readBack = new
        {
            handle = HandleString(pl),
            vertexCount = pl.NumberOfVertices,
            closed = pl.Closed,
            length = TryLength(pl),
            area = TryArea(pl),
            layer = pl.Layer
        };
        tr.Commit();
        return ForgeResult.Success(
            command.Id,
            new { handle = readBack.handle, vertexCount = readBack.vertexCount, length = readBack.length, area = readBack.area },
            verification: new ForgeVerification { Attempted = true, Passed = true, ReadBack = readBack });
    }

    private static ForgeResult DrawPolygon(ForgeCommand command)
    {
        var args = Args<DrawPolygonArgs>(command);
        var step = new DrawBatchStep
        {
            Tool = "polygon",
            Center = args.Center,
            Sides = args.Sides,
            Radius = args.Radius,
            Inscribed = args.Inscribed,
            Mode = args.Mode,
            RotationDeg = args.RotationDeg,
            ConstantWidth = args.ConstantWidth,
            Layer = args.Layer,
            ColorIndex = args.ColorIndex
        };

        var db = ActiveDb;
        using var tr = db.TransactionManager.StartTransaction();
        if (ValidateForCommand(command, db, tr, step) is { } invalid)
        {
            return invalid;
        }

        if (command.DryRun)
        {
            var spec = PlanPolygon(step);
            return DryRun(command, new
            {
                tool = "forge_draw_polygon",
                center = args.Center,
                args.Sides,
                args.Radius,
                inscribed = PolygonInscribed(step),
                vertexCount = spec.Vertices.Length,
                args.RotationDeg,
                args.ConstantWidth,
                args.Layer,
                args.ColorIndex
            });
        }

        if (RunCreate(command, () => CreatePolygonStep(db, tr, step), out var id) is { } failed)
        {
            return failed;
        }

        var pl = (Polyline)tr.GetObject(id, OpenMode.ForRead);
        var readBack = new
        {
            handle = HandleString(pl),
            sides = pl.NumberOfVertices,
            length = TryLength(pl),
            area = TryArea(pl),
            layer = pl.Layer
        };
        tr.Commit();
        return ForgeResult.Success(
            command.Id,
            new { handle = readBack.handle, sides = readBack.sides, length = readBack.length, area = readBack.area },
            verification: new ForgeVerification { Attempted = true, Passed = true, ReadBack = readBack });
    }

    private static ForgeResult DrawEllipse(ForgeCommand command)
    {
        var args = Args<DrawEllipseArgs>(command);
        var step = new DrawBatchStep
        {
            Tool = "ellipse",
            Center = args.Center,
            MajorAxis = args.MajorAxis,
            MajorRadius = args.MajorRadius,
            MinorRadius = args.MinorRadius,
            RadiusRatio = args.RadiusRatio,
            RotationDeg = args.RotationDeg,
            StartAngleDeg = args.StartAngleDeg,
            EndAngleDeg = args.EndAngleDeg,
            Layer = args.Layer,
            ColorIndex = args.ColorIndex
        };

        var db = ActiveDb;
        using var tr = db.TransactionManager.StartTransaction();
        if (ValidateForCommand(command, db, tr, step) is { } invalid)
        {
            return invalid;
        }

        if (command.DryRun)
        {
            var plan = PlanEllipse(step);
            return DryRun(command, new
            {
                tool = "forge_draw_ellipse",
                center = PointList(plan.Center),
                majorAxis = new[] { plan.MajorAxis.X, plan.MajorAxis.Y, plan.MajorAxis.Z },
                majorRadius = plan.MajorAxis.Length,
                minorRadius = plan.MajorAxis.Length * plan.Ratio,
                radiusRatio = plan.Ratio,
                startAngleDeg = plan.StartRad * 180.0 / Math.PI,
                endAngleDeg = plan.EndRad * 180.0 / Math.PI,
                args.Layer,
                args.ColorIndex
            });
        }

        if (RunCreate(command, () => CreateEllipse(db, tr, step), out var id) is { } failed)
        {
            return failed;
        }

        var ellipse = (Ellipse)tr.GetObject(id, OpenMode.ForRead);
        var readBack = new
        {
            handle = HandleString(ellipse),
            center = PointList(ellipse.Center),
            majorAxis = new[] { ellipse.MajorAxis.X, ellipse.MajorAxis.Y, ellipse.MajorAxis.Z },
            majorRadius = ellipse.MajorRadius,
            minorRadius = ellipse.MinorRadius,
            radiusRatio = ellipse.RadiusRatio,
            startAngleDeg = ellipse.StartAngle * 180.0 / Math.PI,
            endAngleDeg = ellipse.EndAngle * 180.0 / Math.PI,
            closed = ellipse.Closed,
            layer = ellipse.Layer
        };
        tr.Commit();
        return ForgeResult.Success(
            command.Id,
            new { handle = readBack.handle, majorRadius = readBack.majorRadius, minorRadius = readBack.minorRadius, closed = readBack.closed },
            verification: new ForgeVerification { Attempted = true, Passed = true, ReadBack = readBack });
    }

    private static ForgeResult DrawSpline(ForgeCommand command)
    {
        var args = Args<DrawSplineArgs>(command);
        var step = new DrawBatchStep
        {
            Tool = "spline",
            FitPoints = args.FitPoints,
            Closed = args.Closed,
            Degree = args.Degree,
            StartTangent = args.StartTangent,
            EndTangent = args.EndTangent,
            Layer = args.Layer,
            ColorIndex = args.ColorIndex
        };

        var db = ActiveDb;
        using var tr = db.TransactionManager.StartTransaction();
        if (ValidateForCommand(command, db, tr, step) is { } invalid)
        {
            return invalid;
        }

        if (command.DryRun)
        {
            var plan = PlanSpline(step);
            return DryRun(command, new
            {
                tool = "forge_draw_spline",
                pointCount = plan.Points.Length,
                degree = plan.Degree,
                closed = plan.Closed,
                hasTangents = plan.StartTangent is not null,
                args.Layer,
                args.ColorIndex
            });
        }

        if (RunCreate(command, () => CreateSpline(db, tr, step), out var id) is { } failed)
        {
            return failed;
        }

        var spline = (Spline)tr.GetObject(id, OpenMode.ForRead);
        var readBack = new
        {
            handle = HandleString(spline),
            fitPoints = spline.NumFitPoints,
            degree = spline.Degree,
            closed = spline.Closed,
            length = TryCurveLength(spline),
            layer = spline.Layer
        };
        tr.Commit();
        return ForgeResult.Success(
            command.Id,
            new { handle = readBack.handle, fitPoints = readBack.fitPoints, degree = readBack.degree, closed = readBack.closed },
            verification: new ForgeVerification { Attempted = true, Passed = true, ReadBack = readBack });
    }

    private static ForgeResult DrawRevCloud(ForgeCommand command)
    {
        var args = Args<DrawRevCloudArgs>(command);
        var step = new DrawBatchStep
        {
            Tool = "revcloud",
            P1 = args.P1,
            P2 = args.P2,
            Corner1 = args.Corner1,
            Corner2 = args.Corner2,
            Vertices = args.Vertices,
            ArcLength = args.ArcLength,
            BulgeMagnitude = args.BulgeMagnitude,
            ConstantWidth = args.ConstantWidth,
            Layer = args.Layer,
            ColorIndex = args.ColorIndex
        };

        var db = ActiveDb;
        using var tr = db.TransactionManager.StartTransaction();
        if (ValidateForCommand(command, db, tr, step) is { } invalid)
        {
            return invalid;
        }

        if (command.DryRun)
        {
            var spec = PlanRevCloud(step);
            return DryRun(command, new
            {
                tool = "forge_draw_revcloud",
                lobeCount = spec.Vertices.Length,
                arcLength = args.ArcLength ?? 0,
                bulgeMagnitude = args.BulgeMagnitude ?? DrawDefaultRevCloudBulge,
                args.ConstantWidth,
                args.Layer,
                args.ColorIndex
            });
        }

        if (RunCreate(command, () => CreateRevCloudStep(db, tr, step), out var id) is { } failed)
        {
            return failed;
        }

        var pl = (Polyline)tr.GetObject(id, OpenMode.ForRead);
        var readBack = new
        {
            handle = HandleString(pl),
            lobeCount = pl.NumberOfVertices,
            length = TryLength(pl),
            area = TryArea(pl),
            layer = pl.Layer
        };
        tr.Commit();
        return ForgeResult.Success(
            command.Id,
            new { handle = readBack.handle, lobeCount = readBack.lobeCount, length = readBack.length },
            verification: new ForgeVerification { Attempted = true, Passed = true, ReadBack = readBack });
    }

    private static ForgeResult DrawLeader(ForgeCommand command)
    {
        var args = Args<DrawLeaderArgs>(command);
        var step = new DrawBatchStep
        {
            Tool = "leader",
            Points = args.Points,
            Text = args.Text,
            TextHeight = args.TextHeight,
            ArrowSize = args.ArrowSize,
            HasArrowHead = args.HasArrowHead,
            Layer = args.Layer,
            ColorIndex = args.ColorIndex
        };

        var db = ActiveDb;
        using var tr = db.TransactionManager.StartTransaction();
        if (ValidateForCommand(command, db, tr, step) is { } invalid)
        {
            return invalid;
        }

        if (command.DryRun)
        {
            var plan = PlanLeader(step);
            return DryRun(command, new
            {
                tool = "forge_draw_leader",
                pointCount = plan.Points.Length,
                args.Text,
                textHeight = plan.TextHeight,
                args.ArrowSize,
                args.HasArrowHead,
                args.Layer,
                args.ColorIndex
            });
        }

        if (RunCreate(command, () => CreateLeader(db, tr, step), out var id) is { } failed)
        {
            return failed;
        }

        var mleader = (MLeader)tr.GetObject(id, OpenMode.ForRead);
        var hasText = mleader.ContentType == ContentType.MTextContent;
        var readBack = new
        {
            handle = HandleString(mleader),
            leaderCount = mleader.LeaderCount,
            vertexCount = TryLeaderVertexCount(mleader),
            hasText,
            textHeight = hasText ? mleader.TextHeight : (double?)null,
            arrowSize = mleader.ArrowSize,
            layer = mleader.Layer
        };
        tr.Commit();
        return ForgeResult.Success(
            command.Id,
            new { handle = readBack.handle, vertexCount = readBack.vertexCount, hasText },
            verification: new ForgeVerification { Attempted = true, Passed = true, ReadBack = readBack });
    }

    private static ForgeResult DrawTable(ForgeCommand command)
    {
        var args = Args<DrawTableArgs>(command);
        TablePlan plan;
        try
        {
            plan = PlanTable(args);
        }
        catch (System.Exception ex) when (IsStepError(ex))
        {
            var (code, message) = StepError(ex);
            return ForgeResult.Failure(command.Id, code, message);
        }

        var db = ActiveDb;
        using var tr = db.TransactionManager.StartTransaction();
        try
        {
            RequireValidColor(args.ColorIndex);
            RequireLayer(db, tr, args.Layer);
        }
        catch (DrawStepException ex)
        {
            return ForgeResult.Failure(command.Id, ex.Code, ex.Message);
        }

        if (command.DryRun)
        {
            return DryRun(command, new
            {
                tool = "forge_draw_table",
                position = PointList(plan.Position),
                rows = plan.TotalRows,
                columns = plan.Columns,
                title = plan.Title,
                args.HasTitleRow,
                rowHeight = plan.RowHeight,
                columnWidths = plan.ColumnWidths,
                textHeight = plan.TextHeight,
                args.Layer,
                args.ColorIndex
            });
        }

        if (RunCreate(command, () => CreateTable(db, tr, plan, args.HasTitleRow, args.Layer, args.ColorIndex), out var id) is { } failed)
        {
            return failed;
        }

        var table = (Table)tr.GetObject(id, OpenMode.ForRead);
        var rows = table.Rows.Count;
        var columns = table.Columns.Count;
        string? titleText = null;
        bool? row0Merged = null;
        try
        {
            if (plan.Title is not null)
            {
                titleText = table.Cells[0, 0].TextString;
            }

            row0Merged = table.Cells[0, 0].IsMerged;
        }
        catch (System.Exception)
        {
            // Read-back of cell details is best-effort; the row/column counts above are authoritative.
        }

        var passed = rows == plan.TotalRows && columns == plan.Columns;
        var readBack = new
        {
            handle = HandleString(table),
            rows,
            columns,
            title = titleText,
            row0Merged,
            layer = table.Layer
        };
        tr.Commit();
        return ForgeResult.Success(
            command.Id,
            new { handle = readBack.handle, rows, columns },
            verification: new ForgeVerification
            {
                Attempted = true,
                Passed = passed,
                Message = passed ? "Table size read back from the database." : $"Table read back as {rows}x{columns}, expected {plan.TotalRows}x{plan.Columns}.",
                ReadBack = readBack
            });
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

        var resultMode = (args.ResultMode ?? "full").Trim().ToLowerInvariant();
        if (resultMode is not ("full" or "errors" or "summary"))
        {
            return ForgeResult.Failure(command.Id, "invalid_result_mode", $"Unknown resultMode '{args.ResultMode}'.", "Use full (default), errors, or summary.");
        }

        var db = ActiveDb;
        var watch = System.Diagnostics.Stopwatch.StartNew();

        // Name → id lookups (layers, styles, blocks, model space) are cached for the whole batch;
        // the per-step nested transactions still open every object they write themselves.
        var previousCache = _drawCache;
        _drawCache = new DrawIdCache(db);
        try
        {
            if (command.DryRun || args.Atomic)
            {
                // Same per-step validation as the real run (tool type, required keys, geometry,
                // layer/colour/style/block existence) — read-only, nothing is created.
                using var vtr = db.TransactionManager.StartTransaction();
                var checks = new List<object>();
                var invalidCount = 0;
                for (var index = 0; index < args.Steps.Length; index++)
                {
                    var step = args.Steps[index] ?? new DrawBatchStep();
                    var tool = NormalizeStepTool(step.Tool);
                    try
                    {
                        ValidateStep(db, vtr, step);
                        if (command.DryRun && resultMode == "full")
                        {
                            checks.Add(new { index, tool, ok = true });
                        }
                    }
                    catch (System.Exception ex) when (IsStepError(ex))
                    {
                        invalidCount++;
                        var (code, message) = StepError(ex);
                        checks.Add(new { index, tool, ok = false, error = new { code, message } });
                    }
                }

                vtr.Commit();
                if (command.DryRun)
                {
                    return DryRun(command, new
                    {
                        tool = "forge_draw_batch",
                        steps = args.Steps.Length,
                        valid = args.Steps.Length - invalidCount,
                        invalid = invalidCount,
                        atomic = args.Atomic,
                        results = checks
                    });
                }

                if (invalidCount > 0)
                {
                    return ForgeResult.Failure(
                        command.Id,
                        "batch_invalid",
                        $"{invalidCount} of {args.Steps.Length} steps are invalid; the atomic batch created nothing.",
                        "Fix the listed steps, or drop atomic=true to create the valid steps and skip the rest.",
                        data: new { created = 0, failed = invalidCount, results = checks });
                }
            }

            using var tr = db.TransactionManager.StartTransaction();
            var results = new List<object>(resultMode == "full" ? args.Steps.Length : 8);
            var createdHandles = new List<string>(args.Steps.Length);
            var failedIndexes = new List<int>();
            var typeCounts = new SortedDictionary<string, int>(StringComparer.Ordinal);
            var succeeded = 0;
            for (var index = 0; index < args.Steps.Length; index++)
            {
                var step = args.Steps[index] ?? new DrawBatchStep();
                var tool = NormalizeStepTool(step.Tool);
                try
                {
                    StepOutcome outcome;
                    if (args.Atomic)
                    {
                        // Validated up front; a runtime failure aborts the single transaction below.
                        outcome = CreateStep(db, tr, step);
                    }
                    else
                    {
                        // Each step runs in its own nested transaction: a step that throws is aborted
                        // on dispose, so it leaves nothing behind (no orphan boundary, no empty hatch).
                        using var st = db.TransactionManager.StartTransaction();
                        outcome = CreateStep(db, st, step);
                        st.Commit();
                    }

                    var handle = outcome.Id.Handle.ToString();
                    createdHandles.Add(handle);
                    var extraHandles = outcome.Extra?.Select(extra => extra.Handle.ToString()).ToArray();
                    if (extraHandles is not null)
                    {
                        createdHandles.AddRange(extraHandles);
                    }

                    // The class name comes from the ObjectId's runtime class — no extra object open.
                    var type = ManagedTypeName(outcome.Id);
                    typeCounts[type] = typeCounts.TryGetValue(type, out var n) ? n + 1 : 1;
                    succeeded++;
                    if (resultMode == "full" || (resultMode == "errors" && outcome.Warnings is { Length: > 0 }))
                    {
                        results.Add(new { index, tool, ok = true, handle, type, extraHandles, warnings = outcome.Warnings });
                    }
                }
                catch (System.Exception ex) when (!args.Atomic || IsStepError(ex))
                {
                    var (code, message) = StepError(ex);
                    if (args.Atomic)
                    {
                        // Returning before tr.Commit() aborts everything created so far.
                        return ForgeResult.Failure(
                            command.Id,
                            "batch_aborted",
                            $"Step {index} ({tool}) failed during an atomic batch; nothing was created: {message}",
                            "Fix the step (dryRun=true validates every step) and resend.",
                            data: new { created = 0, failedIndex = index, error = new { code, message } });
                    }

                    // Record the failed step and continue — the batch contract keeps going.
                    failedIndexes.Add(index);
                    results.Add(new { index, tool, ok = false, error = new { code, message } });
                }
            }

            var failed = args.Steps.Length - succeeded;
            if (succeeded == 0)
            {
                // Nothing was created; the outer transaction is disposed without commit.
                return ForgeResult.Failure(
                    command.Id,
                    "batch_all_failed",
                    $"All {args.Steps.Length} batch steps failed; nothing was created.",
                    "Fix the per-step errors in data.results (forge_draw_batch with dryRun=true validates every step).",
                    data: new { created = 0, failed, results, createdHandles });
            }

            tr.Commit();
            watch.Stop();
            var elapsedMs = Math.Max(watch.Elapsed.TotalMilliseconds, 0.001);
            return ForgeResult.Success(
                command.Id,
                new
                {
                    created = succeeded,
                    failed,
                    entities = createdHandles.Count,
                    atomic = args.Atomic,
                    resultMode,
                    countsByType = typeCounts,
                    results,
                    createdHandles = resultMode == "summary" ? null : createdHandles,
                    elapsedMs = Math.Round(elapsedMs, 1),
                    entitiesPerSecond = Math.Round(createdHandles.Count / (elapsedMs / 1000.0), 0)
                },
                verification: new ForgeVerification
                {
                    Attempted = true,
                    Passed = failed == 0,
                    Message = args.Atomic
                        ? "All steps were validated first and created in one transaction (all-or-nothing). Entity classes were read from the new object ids."
                        : "Each step ran in its own nested transaction inside one outer transaction; failed steps were rolled back individually and the batch continued. Entity classes were read from the new object ids.",

                    // Counts only: the per-step results are already in data (no duplicate payload).
                    ReadBack = new { created = succeeded, failed, failedIndexes, countsByType = typeCounts }
                });
        }
        finally
        {
            _drawCache = previousCache;
        }
    }
    /// <summary>Managed type name (Line, DBText, MText, RotatedDimension …) from the id's RX class, without opening the object.</summary>
    private static string ManagedTypeName(ObjectId id)
    {
        var rx = id.ObjectClass?.Name ?? "";
        var name = rx.StartsWith("AcDb", StringComparison.Ordinal) ? rx[4..] : rx;
        return name switch
        {
            "Text" => "DBText",
            "3PointAngularDimension" => "Point3AngularDimension",
            "2LineAngularDimension" => "LineAngularDimension2",
            "" => "Entity",
            _ => name
        };
    }

    // ===================== creation cores =====================
    // Shared by the single-tool handlers and the forge_draw_batch steps. The caller owns the
    // transaction: cores never open one and never touch DryRun. They validate through the
    // same Plan*/Require* helpers that dry-run uses, then create via AppendNew (defaults first,
    // dispose on error) and throw DrawStepException with the wire error code.

    private static StepOutcome CreateStep(Database db, Transaction tr, DrawBatchStep step) => NormalizeStepTool(step.Tool) switch
    {
        "line" => new StepOutcome(CreateLine(db, tr, step)),
        "polyline" => new StepOutcome(CreatePolyline(db, tr, step, step.Bulges)),
        "rectangle" => new StepOutcome(CreateRectangleStep(db, tr, step)),
        "polygon" => new StepOutcome(CreatePolygonStep(db, tr, step)),
        "circle" => new StepOutcome(CreateCircle(db, tr, step)),
        "arc" => new StepOutcome(CreateArc(db, tr, step)),
        "ellipse" => new StepOutcome(CreateEllipse(db, tr, step)),
        "spline" => new StepOutcome(CreateSpline(db, tr, step)),
        "revcloud" => new StepOutcome(CreateRevCloudStep(db, tr, step)),
        "text" => new StepOutcome(CreateText(db, tr, step)),
        "mtext" => new StepOutcome(CreateMText(db, tr, step)),
        "hatch" => CreateHatchStep(db, tr, step),
        "dimension" => new StepOutcome(CreateDimension(db, tr, step)),
        "leader" => CreateLeaderStep(db, tr, step),
        "block_insert" => CreateBlockInsertStep(db, tr, step),
        _ => throw UnsupportedTool(step.Tool)
    };

    /// <summary>
    /// Validation shared by dry-run and the real run: everything a core checks before it
    /// creates anything (tool type, required keys, geometry, colour, layer, dimstyle, block).
    /// Read-only — never mutates the database.
    /// </summary>
    private static void ValidateStep(Database db, Transaction tr, DrawBatchStep step)
    {
        var tool = NormalizeStepTool(step.Tool);
        if (Array.IndexOf(DrawBatchTools, tool) < 0)
        {
            throw UnsupportedTool(step.Tool);
        }

        RequireValidColor(step.ColorIndex);
        RequireLayer(db, tr, step.Layer);
        switch (tool)
        {
            case "line":
                PlanLine(step);
                break;
            case "polyline":
                PlanPolyline(step, step.Bulges);
                break;
            case "rectangle":
                PlanRectangle(step);
                ValidateWidth(step.ConstantWidth);
                break;
            case "polygon":
                PlanPolygon(step);
                ValidateWidth(step.ConstantWidth);
                break;
            case "circle":
                PlanCircle(step);
                break;
            case "arc":
                PlanArc(step);
                break;
            case "ellipse":
                PlanEllipse(step);
                break;
            case "spline":
                PlanSpline(step);
                break;
            case "revcloud":
                PlanRevCloud(step);
                ValidateWidth(step.ConstantWidth);
                break;
            case "text":
                PlanText(step);
                ResolveTextStyle(db, tr, step.TextStyle);
                break;
            case "mtext":
                PlanMText(step);
                ValidateMaskScale(step.MaskScale);
                ResolveTextStyle(db, tr, step.TextStyle);
                break;
            case "hatch":
                PlanHatch(step);
                break;
            case "dimension":
                PlanDimension(step);
                ResolveDimStyle(db, tr, step.DimStyle);
                break;
            case "leader":
                PlanLeader(step);
                ValidateMaskScale(step.MaskScale);
                ResolveMLeaderStyle(db, tr, step.LeaderStyle);
                ResolveTextStyle(db, tr, step.TextStyle);
                if (!string.IsNullOrWhiteSpace(step.ContentBlock))
                {
                    ResolveBlockId(db, tr, step.ContentBlock!);
                    RequireAuthorizedAttributes(step.Attributes);
                }

                break;
            case "block_insert":
                PlanBlockInsert(db, tr, step);
                break;
        }
    }

    private static ForgeResult? ValidateForCommand(ForgeCommand command, Database db, Transaction tr, DrawBatchStep step)
    {
        try
        {
            ValidateStep(db, tr, step);
            return null;
        }
        catch (System.Exception ex) when (IsStepError(ex))
        {
            var (code, message) = StepError(ex);
            return ForgeResult.Failure(command.Id, code, message);
        }
    }

    private static ForgeResult? RunCreate(ForgeCommand command, Func<ObjectId> create, out ObjectId id)
    {
        try
        {
            id = create();
            return null;
        }
        catch (System.Exception ex) when (IsStepError(ex))
        {
            id = ObjectId.Null;
            var (code, message) = StepError(ex);
            return ForgeResult.Failure(command.Id, code, message);
        }
    }

    /// <summary>
    /// new entity -> SetDatabaseDefaults(db) -> configure -> layer/colour -> append. Defaults
    /// run once and first, so they cannot reset what configure set. If anything throws before
    /// the entity is database-resident it is disposed (no finalizer on a non-resident object).
    /// </summary>
    private static ObjectId AppendNew<T>(Database db, Transaction tr, T entity, string? layer, int? colorIndex, Action<T>? configure = null)
        where T : Entity
    {
        try
        {
            entity.SetDatabaseDefaults(db);
            configure?.Invoke(entity);
            if (layer is not null)
            {
                var layerId = LookupLayerId(db, tr, layer);
                if (layerId.IsNull)
                {
                    throw new DrawStepException("layer_not_found", $"Layer not found: {layer}");
                }

                entity.LayerId = layerId;
            }

            ApplyColor(entity, colorIndex);
            return AppendExistingToModelSpace(db, tr, entity);
        }
        catch
        {
            if (entity.ObjectId.IsNull)
            {
                entity.Dispose();
            }

            throw;
        }
    }

    private static ObjectId CreateLine(Database db, Transaction tr, DrawBatchStep step)
    {
        RequireValidColor(step.ColorIndex);
        RequireLayer(db, tr, step.Layer);
        var (start, end) = PlanLine(step);
        return AppendNew(db, tr, new Line(start, end), step.Layer, step.ColorIndex);
    }

    private static ObjectId CreatePolyline(Database db, Transaction tr, DrawBatchStep step, double[]? bulges = null)
    {
        RequireValidColor(step.ColorIndex);
        RequireLayer(db, tr, step.Layer);
        PlanPolyline(step, bulges);
        var vertices = step.Vertices!;
        var width = step.ConstantWidth ?? 0;
        return AppendNew(db, tr, new Polyline(), step.Layer, step.ColorIndex, pl =>
        {
            for (var i = 0; i < vertices.Length; i++)
            {
                var bulge = bulges is not null && i < bulges.Length ? bulges[i] : 0;
                pl.AddVertexAt(i, new Point2d(vertices[i][0], vertices[i][1]), bulge, width, width);
            }

            pl.Closed = step.Closed;
        });
    }

    private static ObjectId CreateCircle(Database db, Transaction tr, DrawBatchStep step)
    {
        RequireValidColor(step.ColorIndex);
        RequireLayer(db, tr, step.Layer);
        var (center, radius) = PlanCircle(step);
        return AppendNew(db, tr, new Circle(center, Vector3d.ZAxis, radius), step.Layer, step.ColorIndex);
    }

    private static ObjectId CreateArc(Database db, Transaction tr, DrawBatchStep step)
    {
        RequireValidColor(step.ColorIndex);
        RequireLayer(db, tr, step.Layer);
        var (center, radius, startRad, endRad) = PlanArc(step);
        return AppendNew(db, tr, new Arc(center, radius, startRad, endRad), step.Layer, step.ColorIndex);
    }

    private static ObjectId CreateText(Database db, Transaction tr, DrawBatchStep step)
    {
        RequireValidColor(step.ColorIndex);
        RequireLayer(db, tr, step.Layer);
        var position = PlanText(step);
        var style = ResolveTextStyle(db, tr, step.TextStyle);
        return AppendNew(db, tr, new DBText(), step.Layer, step.ColorIndex, text =>
        {
            if (!style.Id.IsNull)
            {
                text.TextStyleId = style.Id;
            }

            text.Position = position;
            text.Height = step.Height!.Value;
            text.Rotation = DegreesToRadians(step.RotationDeg ?? 0);
            text.TextString = step.Text!;
        });
    }

    private static ObjectId CreateMText(Database db, Transaction tr, DrawBatchStep step)
    {
        RequireValidColor(step.ColorIndex);
        RequireLayer(db, tr, step.Layer);
        var (position, attachment) = PlanMText(step);
        ValidateMaskScale(step.MaskScale);
        var style = ResolveTextStyle(db, tr, step.TextStyle);
        var id = AppendNew(db, tr, new MText(), step.Layer, step.ColorIndex, mtext =>
        {
            if (!style.Id.IsNull)
            {
                mtext.TextStyleId = style.Id;
            }

            mtext.Location = position;
            mtext.Attachment = attachment;
            mtext.Contents = MTextContents(step.Text!);
            if (step.Height is > 0)
            {
                mtext.TextHeight = step.Height.Value;
            }
            else if (style.FixedHeight > 0)
            {
                mtext.TextHeight = style.FixedHeight;
            }

            mtext.Width = step.Width ?? 0;
            mtext.Rotation = DegreesToRadians(step.RotationDeg ?? 0);
        });

        // The background mask is a resident-only override (set once the MText is in the database).
        if (step.BackgroundMask == true)
        {
            ApplyBackgroundMask((MText)tr.GetObject(id, OpenMode.ForWrite), step.MaskScale);
        }

        return id;
    }

    private static ObjectId CreateRectangleStep(Database db, Transaction tr, DrawBatchStep step)
    {
        var spec = PlanRectangle(step);
        return CreatePolyline(db, tr, step with { Vertices = spec.Vertices, Closed = true }, spec.Bulges);
    }

    private static ObjectId CreatePolygonStep(Database db, Transaction tr, DrawBatchStep step)
    {
        var spec = PlanPolygon(step);
        return CreatePolyline(db, tr, step with { Vertices = spec.Vertices, Closed = true }, spec.Bulges);
    }

    private static ObjectId CreateRevCloudStep(Database db, Transaction tr, DrawBatchStep step)
    {
        var spec = PlanRevCloud(step);
        return CreatePolyline(db, tr, step with { Vertices = spec.Vertices, Closed = true }, spec.Bulges);
    }

    private static ObjectId CreateEllipse(Database db, Transaction tr, DrawBatchStep step)
    {
        RequireValidColor(step.ColorIndex);
        RequireLayer(db, tr, step.Layer);
        var plan = PlanEllipse(step);
        Ellipse ellipse;
        try
        {
            ellipse = new Ellipse(plan.Center, Vector3d.ZAxis, plan.MajorAxis, plan.Ratio, plan.StartRad, plan.EndRad);
        }
        catch (Autodesk.AutoCAD.Runtime.Exception ex)
        {
            throw new DrawStepException("invalid_args", $"AutoCAD rejected the ellipse definition ({ex.ErrorStatus}).");
        }

        return AppendNew(db, tr, ellipse, step.Layer, step.ColorIndex);
    }

    private static ObjectId CreateSpline(Database db, Transaction tr, DrawBatchStep step)
    {
        RequireValidColor(step.ColorIndex);
        RequireLayer(db, tr, step.Layer);
        var plan = PlanSpline(step);
        Spline spline;
        using (var fitPoints = new Point3dCollection())
        {
            foreach (var point in plan.Points)
            {
                fitPoints.Add(point);
            }

            try
            {
                // Degree is passed as degree (these overloads take degree, not order). Closed
                // uses the periodic fit overload — the first point is NOT duplicated.
                spline = plan.Closed
                    ? new Spline(fitPoints, true, KnotParameterizationEnum.Chord, plan.Degree, 0.0)
                    : plan.StartTangent is { } startTangent
                        ? new Spline(fitPoints, startTangent, plan.EndTangent!.Value, KnotParameterizationEnum.Chord, plan.Degree, 0.0)
                        : new Spline(fitPoints, KnotParameterizationEnum.Chord, plan.Degree, 0.0);
            }
            catch (Autodesk.AutoCAD.Runtime.Exception ex)
            {
                throw new DrawStepException("invalid_args", $"AutoCAD rejected the spline fit data ({ex.ErrorStatus}); check degree and fit points.");
            }
        }

        return AppendNew(db, tr, spline, step.Layer, step.ColorIndex);
    }

    private static StepOutcome CreateHatchStep(Database db, Transaction tr, DrawBatchStep step)
    {
        RequireValidColor(step.ColorIndex);
        RequireLayer(db, tr, step.Layer);
        var (pattern, scale, angleDeg) = PlanHatch(step);
        var boundaryId = CreatePolyline(
            db,
            tr,
            new DrawBatchStep { Vertices = step.Vertices, Closed = true, Layer = step.Layer, ColorIndex = step.ColorIndex },
            step.Bulges);
        var hatchId = CreateHatchOnBoundary(db, tr, boundaryId, pattern, scale, angleDeg, associative: true, step.Layer, step.ColorIndex);
        return new StepOutcome(hatchId, [boundaryId]);
    }

    /// <summary>
    /// Single ordering for every hatch path (fix-smoke-20261029 D2; Autodesk "Edit Hatch Patterns (.NET)"
    /// and AcDbHatch remarks): defaults/layer/colour, append (associativity needs a resident hatch),
    /// SetHatchPattern FIRST (PatternAngle throws eInvalidInput while no pattern is set), then
    /// PatternScale and PatternAngle, then SetHatchPattern AGAIN so the definition lines are rebuilt
    /// from the new scale/angle, Associative before AppendLoop, EvaluateHatch last. The result is read
    /// back (name, scale, angle, loops, first definition line) before the step reports success.
    /// </summary>
    private static ObjectId CreateHatchOnBoundary(Database db, Transaction tr, ObjectId boundaryId, string pattern, double scale, double angleDeg, bool associative, string? layer, int? colorIndex)
    {
        var patternName = pattern.Trim().ToUpperInvariant();
        var isSolid = patternName.Equals("SOLID", StringComparison.Ordinal);
        var angleRad = DegreesToRadians(angleDeg);
        var boundaryEntity = tr.GetObject(boundaryId, OpenMode.ForRead) as Entity;
        var ownerId = boundaryEntity?.OwnerId ?? ObjectId.Null;
        var hatch = new Hatch();
        ObjectId id;
        try
        {
            hatch.SetDatabaseDefaults(db);
            if (layer is not null)
            {
                hatch.LayerId = LookupLayerId(db, tr, layer);
            }

            ApplyColor(hatch, colorIndex);
            id = AppendToOwner(db, tr, ownerId, hatch);
        }
        catch
        {
            if (hatch.ObjectId.IsNull)
            {
                hatch.Dispose();
            }

            throw;
        }

        ApplyHatchPattern(hatch, patternName, isSolid, scale, angleRad);
        hatch.Associative = associative;
        hatch.AppendLoop(HatchLoopTypes.Outermost, new ObjectIdCollection { boundaryId });
        hatch.EvaluateHatch(true);
        VerifyHatch(db, hatch, patternName, isSolid, scale, angleRad);
        return id;
    }

    /// <summary>
    /// The D2 pattern order shared by every hatch path (forge_draw_hatch, the draw_batch hatch step and
    /// forge_draw_wall): SetHatchPattern first, then PatternScale and PatternAngle, then SetHatchPattern
    /// again so the definition lines are rebuilt. A pattern AutoCAD rejects is the step error invalid_pattern.
    /// </summary>
    private static void ApplyHatchPattern(Hatch hatch, string patternName, bool isSolid, double scale, double angleRad)
    {
        try
        {
            hatch.SetHatchPattern(HatchPatternType.PreDefined, patternName);
        }
        catch (Autodesk.AutoCAD.Runtime.Exception ex)
        {
            throw new DrawStepException("invalid_pattern", $"Hatch pattern '{patternName}' could not be applied ({ex.ErrorStatus}). Use a predefined pattern such as ANSI31, ANSI37, AR-CONC or SOLID.");
        }

        if (!isSolid)
        {
            hatch.PatternScale = scale;
            hatch.PatternAngle = angleRad;
            hatch.SetHatchPattern(hatch.PatternType, hatch.PatternName);
        }
    }

    /// <summary>Per-call cache of scale-1/angle-0 first-definition lines, keyed by pattern and MEASUREMENT.</summary>
    [ThreadStatic]
    private static Dictionary<string, (double Angle, double Spacing)?>? _hatchBaselines;

    private static void ResetHatchBaselines() => _hatchBaselines = null;

    private static (double Angle, double Spacing)? HatchBaseline(Database db, string patternName)
    {
        _hatchBaselines ??= new Dictionary<string, (double, double)?>(StringComparer.OrdinalIgnoreCase);
        var key = patternName + "|" + db.Measurement;
        if (_hatchBaselines.TryGetValue(key, out var cached))
        {
            return cached;
        }

        (double, double)? baseline = null;
        try
        {
            using var probe = new Hatch();
            probe.SetDatabaseDefaults(db);
            probe.SetHatchPattern(HatchPatternType.PreDefined, patternName);
            if (probe.NumberOfPatternDefinitions > 0)
            {
                var def = probe.GetPatternDefinitionAt(0);
                baseline = (def.Angle, HatchMath.PerpendicularSpacing(def.Angle, def.OffsetX, def.OffsetY));
            }
        }
        catch
        {
            baseline = null;
        }

        _hatchBaselines[key] = baseline;
        return baseline;
    }

    private static void VerifyHatch(Database db, Hatch hatch, string patternName, bool isSolid, double scale, double angleRad)
    {
        var problems = new List<string>();
        if (!string.Equals(hatch.PatternName, patternName, StringComparison.OrdinalIgnoreCase))
        {
            problems.Add($"pattern '{hatch.PatternName}'");
        }

        if (hatch.NumberOfLoops < 1)
        {
            problems.Add("no boundary loop");
        }

        if (!isSolid)
        {
            if (Math.Abs(hatch.PatternScale - scale) > 1e-9 * Math.Max(1, Math.Abs(scale)))
            {
                problems.Add($"scale {hatch.PatternScale} (asked {scale})");
            }

            if (Math.Abs(HatchMath.WrapAngle(hatch.PatternAngle) - HatchMath.WrapAngle(angleRad)) > HatchMath.AngleTolerance)
            {
                problems.Add($"angle {hatch.PatternAngle * 180 / Math.PI:0.###}° (asked {angleRad * 180 / Math.PI:0.###}°)");
            }

            if (hatch.NumberOfPatternDefinitions < 1)
            {
                problems.Add("no pattern definition lines");
            }
            else if (HatchBaseline(db, patternName) is { } baseline)
            {
                var def = hatch.GetPatternDefinitionAt(0);
                var (expectedAngle, expectedSpacing) = HatchMath.ExpectedDefinition(baseline.Angle, baseline.Spacing, scale, angleRad);
                var spacing = HatchMath.PerpendicularSpacing(def.Angle, def.OffsetX, def.OffsetY);
                if (!HatchMath.SameLineAngle(expectedAngle, def.Angle) || !HatchMath.SameSpacing(expectedSpacing, spacing))
                {
                    problems.Add($"definition line {def.Angle * 180 / Math.PI:0.###}°/{spacing:0.####} (expected {expectedAngle * 180 / Math.PI:0.###}°/{expectedSpacing:0.####})");
                }
            }
        }

        if (problems.Count > 0)
        {
            throw new DrawStepException("hatch_readback_mismatch", $"Hatch '{patternName}' read back wrong: {string.Join("; ", problems)}.");
        }
    }

    private static ObjectId CreateDimension(Database db, Transaction tr, DrawBatchStep step)
    {
        RequireValidColor(step.ColorIndex);
        RequireLayer(db, tr, step.Layer);
        var plan = PlanDimension(step);
        var styleId = ResolveDimStyle(db, tr, step.DimStyle);
        Dimension dim = plan.Kind switch
        {
            "linear" => new RotatedDimension(DegreesToRadians(plan.RotationDeg), plan.P1, plan.P2!.Value, plan.DimLine!.Value, "", styleId),
            "aligned" => new AlignedDimension(plan.P1, plan.P2!.Value, plan.DimLine!.Value, "", styleId),
            // p1 = centre, p2 = point on the circle.
            "radius" => new RadialDimension(plan.P1, plan.P2!.Value, 0, "", styleId),
            // DiametricDimension takes two diametrically opposite chord points, not the centre.
            "diameter" => new DiametricDimension(plan.P2!.Value, plan.P1 + (plan.P1 - plan.P2!.Value), 0, "", styleId),
            // vertex = angle apex, p1/p2 = points on the two legs, dimLinePoint = where the arc passes.
            "angular" => new Point3AngularDimension(plan.Vertex!.Value, plan.P1, plan.P2!.Value, plan.DimLine!.Value, "", styleId),
            _ => new OrdinateDimension(plan.UseXAxis, plan.P1, (plan.DimLine ?? plan.P2)!.Value, "", styleId)
        };

        var id = AppendNew(db, tr, dim, step.Layer, step.ColorIndex, d => d.DimensionStyle = styleId);

        // Per-entity overrides are written once the dimension is resident (xdata needs the
        // database); defaults are never re-applied after this point.
        if (step.DimScale is > 0)
        {
            dim.Dimscale = step.DimScale.Value;
        }

        var text = step.TextOverride ?? step.Text;
        if (!string.IsNullOrEmpty(text) && text != "<>")
        {
            dim.DimensionText = text;
        }

        try
        {
            dim.RecomputeDimensionBlock(true);
        }
        catch (Autodesk.AutoCAD.Runtime.Exception)
        {
            // The dimension block is regenerated on the next regen anyway.
        }

        return id;
    }

    private static StepOutcome CreateLeaderStep(Database db, Transaction tr, DrawBatchStep step)
    {
        var warnings = new List<string>();
        var id = CreateLeader(db, tr, step, warnings);
        return new StepOutcome(id, null, warnings.Count > 0 ? warnings.ToArray() : null);
    }

    private static ObjectId CreateLeader(Database db, Transaction tr, DrawBatchStep step, List<string>? warnings = null)
    {
        RequireValidColor(step.ColorIndex);
        RequireLayer(db, tr, step.Layer);
        var plan = PlanLeader(step);
        ValidateMaskScale(step.MaskScale);
        var styleId = ResolveMLeaderStyle(db, tr, step.LeaderStyle);
        var textStyle = ResolveTextStyle(db, tr, step.TextStyle);
        var contentBlockId = string.IsNullOrWhiteSpace(step.ContentBlock) ? ObjectId.Null : ResolveBlockId(db, tr, step.ContentBlock!);
        if (!contentBlockId.IsNull)
        {
            RequireAuthorizedAttributes(step.Attributes);
        }

        // With a multileader style and no explicit height the style's text height wins.
        var keepStyleHeight = !styleId.IsNull && step.TextHeight is null && step.Height is null;
        var id = AppendNew(db, tr, new MLeader(), step.Layer, step.ColorIndex, mleader =>
        {
            if (!styleId.IsNull)
            {
                mleader.MLeaderStyle = styleId;
            }

            var leaderIndex = mleader.AddLeader();
            var lineIndex = mleader.AddLeaderLine(leaderIndex);
            foreach (var point in plan.Points)
            {
                mleader.AddLastVertex(lineIndex, point);
            }

            // hasArrowHead=false is honoured as arrow size 0 (no arrow block is drawn).
            if (!plan.HasArrowHead)
            {
                mleader.ArrowSize = 0;
            }
            else if (step.ArrowSize is > 0)
            {
                mleader.ArrowSize = step.ArrowSize.Value;
            }

            if (!contentBlockId.IsNull)
            {
                // Tag bubble: block content placed at the landing point (attributes follow once resident).
                mleader.ContentType = ContentType.BlockContent;
                mleader.BlockContentId = contentBlockId;
                mleader.BlockPosition = plan.Points[^1];
            }
            else if (!string.IsNullOrWhiteSpace(step.Text))
            {
                mleader.ContentType = ContentType.MTextContent;

                // MLeader.MText copies the MText, so the temporary is disposed here.
                using var mtext = new MText();
                mtext.SetDatabaseDefaults(db);
                if (!textStyle.Id.IsNull)
                {
                    mtext.TextStyleId = textStyle.Id;
                }

                mtext.Contents = MTextContents(step.Text!);
                if (!keepStyleHeight)
                {
                    mtext.TextHeight = plan.TextHeight;
                }

                mtext.Location = plan.Points[^1];
                if (step.BackgroundMask == true)
                {
                    ApplyBackgroundMask(mtext, step.MaskScale);
                }

                mleader.MText = mtext;
                if (!keepStyleHeight)
                {
                    mleader.TextHeight = plan.TextHeight;
                }
            }
            else
            {
                mleader.ContentType = ContentType.NoneContent;
            }
        });

        if (!contentBlockId.IsNull)
        {
            var mleader = (MLeader)tr.GetObject(id, OpenMode.ForWrite);
            var unknown = SetMLeaderBlockAttributes(tr, mleader, contentBlockId, step.Attributes);
            if (unknown.Length > 0)
            {
                // Same contract as block_insert: an unknown tag is reported, the entity is kept.
                warnings?.Add($"No non-constant attribute definition for tag(s): {string.Join(", ", unknown)}");
            }
        }

        return id;
    }

    private static ObjectId CreateTable(Database db, Transaction tr, TablePlan plan, bool hasTitleRow, string? layer, int? colorIndex)
    {
        RequireValidColor(colorIndex);
        RequireLayer(db, tr, layer);
        return AppendNew(db, tr, new Table(), layer, colorIndex, table =>
        {
            table.TableStyle = db.Tablestyle;
            table.Position = plan.Position;
            table.SetSize(plan.TotalRows, plan.Columns);
            table.SetRowHeight(plan.RowHeight);
            for (var c = 0; c < plan.Columns; c++)
            {
                table.Columns[c].Width = plan.ColumnWidths[c];
            }

            var hasTitle = plan.Title is not null;
            var lastColumn = plan.Columns - 1;
            if (!hasTitle && !hasTitleRow)
            {
                // The style's title cell style can merge row 0 across all columns, which would
                // swallow header cells. Row 0 is data here: restyle and unmerge (best-effort —
                // custom table styles may lack _HEADER).
                BestEffort(() => table.Rows[0].Style = "_HEADER");
                BestEffort(() => table.Rows[0].IsMergeAllEnabled = false);
                if (lastColumn > 0)
                {
                    BestEffort(() => table.UnmergeCells(CellRange.Create(table, 0, 0, 0, lastColumn)));
                }
            }

            var rowOffset = 0;
            if (hasTitle)
            {
                var titleCell = table.Cells[0, 0];
                titleCell.TextHeight = plan.TextHeight * 1.25;
                titleCell.TextString = plan.Title!;
                titleCell.Alignment = CellAlignment.MiddleCenter;
                if (lastColumn > 0 && table.Cells[0, 0].IsMerged != true)
                {
                    table.MergeCells(CellRange.Create(table, 0, 0, 0, lastColumn));
                }

                rowOffset = 1;
            }

            for (var r = 0; r < plan.DataRows; r++)
            {
                var row = plan.Cells[r] ?? [];
                for (var c = 0; c < plan.Columns; c++)
                {
                    var cell = table.Cells[r + rowOffset, c];
                    cell.TextHeight = plan.TextHeight;
                    cell.TextString = c < row.Length ? row[c] ?? "" : "";
                    cell.Alignment = r == 0 ? CellAlignment.MiddleCenter : CellAlignment.MiddleLeft;
                }
            }

            if (!hasTitle && hasTitleRow && lastColumn > 0 && table.Cells[0, 0].IsMerged != true)
            {
                table.MergeCells(CellRange.Create(table, 0, 0, 0, lastColumn));
            }

            table.GenerateLayout();
        });
    }

    private static StepOutcome CreateBlockInsertStep(Database db, Transaction tr, DrawBatchStep step)
    {
        RequireValidColor(step.ColorIndex);
        RequireLayer(db, tr, step.Layer);
        var (blockId, position, scale) = PlanBlockInsert(db, tr, step);
        var bref = new BlockReference(position, blockId);
        var id = AppendNew(db, tr, bref, step.Layer, step.ColorIndex);

        // Mirrors forge_block_insert: scale/rotation first, then one AttributeReference per
        // non-constant definition bound to the final block transform; supplied values are set
        // before the reference becomes database-owned.
        bref.ScaleFactors = new Scale3d(scale);
        bref.Rotation = DegreesToRadians(step.RotationDeg ?? 0);
        var supplied = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (step.Attributes is { Count: > 0 })
        {
            foreach (var pair in step.Attributes)
            {
                supplied[pair.Key] = pair.Value;
            }
        }

        var btr = (BlockTableRecord)tr.GetObject(blockId, OpenMode.ForRead);
        foreach (ObjectId entityId in btr)
        {
            if (tr.GetObject(entityId, OpenMode.ForRead) is not AttributeDefinition definition || definition.Constant)
            {
                continue;
            }

            var reference = new AttributeReference();
            try
            {
                reference.SetAttributeFromBlock(definition, bref.BlockTransform);
                if (supplied.Remove(definition.Tag, out var value))
                {
                    reference.TextString = value;
                }

                bref.AttributeCollection.AppendAttribute(reference);
                tr.AddNewlyCreatedDBObject(reference, true);
            }
            catch
            {
                if (reference.ObjectId.IsNull)
                {
                    reference.Dispose();
                }

                throw;
            }
        }

        var warnings = supplied.Count > 0
            ? new[] { $"No non-constant attribute definition for tag(s): {string.Join(", ", supplied.Keys)}" }
            : null;
        return new StepOutcome(id, null, warnings);
    }

    // ===================== planning / validation (pure, no DB writes) =====================

    private static (Point3d Start, Point3d End) PlanLine(DrawBatchStep step) =>
        (PointArg(step.Start, "start"), PointArg(step.End, "end"));

    private static void PlanPolyline(DrawBatchStep step, double[]? bulges)
    {
        ValidateVertices(step.Vertices);
        ValidateWidth(step.ConstantWidth);
        if (bulges is not null && bulges.Any(b => !double.IsFinite(b)))
        {
            throw new DrawStepException("invalid_args", "bulges must be finite numbers.");
        }
    }

    private static (Point3d Center, double Radius) PlanCircle(DrawBatchStep step) =>
        (PointArg(step.Center, "center"), RequirePositiveRadius(step.Radius));

    private static (Point3d Center, double Radius, double StartRad, double EndRad) PlanArc(DrawBatchStep step)
    {
        var radius = RequirePositiveRadius(step.Radius);
        if (step.StartAngleDeg is not { } startDeg || step.EndAngleDeg is not { } endDeg)
        {
            throw new DrawStepException("invalid_args", "startAngleDeg and endAngleDeg are required.");
        }

        if ((endDeg - startDeg) % 360 == 0)
        {
            throw new DrawStepException("zero_sweep", "Arc start and end angles must not coincide (mod 360).");
        }

        return (PointArg(step.Center, "center"), radius, DegreesToRadians(startDeg), DegreesToRadians(endDeg));
    }

    private static Point3d PlanText(DrawBatchStep step)
    {
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

        return PointArg(step.Position, "position");
    }

    private static (Point3d Position, AttachmentPoint Attachment) PlanMText(DrawBatchStep step)
    {
        if (string.IsNullOrWhiteSpace(step.Text))
        {
            throw new DrawStepException("missing_text", "Text is required.");
        }

        if (step.Height is { } height && !(height > 0 && double.IsFinite(height)))
        {
            throw new DrawStepException("invalid_height", "height must be greater than 0.");
        }

        if (step.Width is { } width && !(width >= 0 && double.IsFinite(width)))
        {
            throw new DrawStepException("invalid_args", "width must be >= 0 (0 = no wrapping).");
        }

        var attachment = ParseMTextAttachment(step.AttachmentPoint);
        return (PointArg(step.Position, "position"), attachment);
    }

    private static AttachmentPoint ParseMTextAttachment(string? value)
    {
        if (value is null)
        {
            return AttachmentPoint.TopLeft;
        }

        var name = value.Trim();
        if (!MTextAttachmentNames.Contains(name) || !Enum.TryParse(name, ignoreCase: true, out AttachmentPoint attachment))
        {
            throw new DrawStepException("invalid_attachment", $"Unknown attachmentPoint: {value}. Use topLeft, topCenter, topRight, middleLeft, middleCenter, middleRight, bottomLeft, bottomCenter or bottomRight.");
        }

        return attachment;
    }

    private static CadPolylineSpec PlanRectangle(DrawBatchStep step)
    {
        var c1 = step.P1 ?? step.Corner1 ?? step.Start;
        var c2 = step.P2 ?? step.Corner2 ?? step.End;
        var hasCorners = c1 is not null || c2 is not null;
        var hasCenter = step.Center is not null || step.Width is not null || step.Height is not null;
        if (hasCorners && hasCenter)
        {
            throw new DrawStepException("invalid_args", "Ambiguous rectangle: give either corners (p1, p2) or (center, width, height), not both.");
        }

        try
        {
            var raw = CadGeometryMath.BuildRectangle(
                c1,
                c2,
                step.Center,
                step.Width,
                step.Height,
                step.FilletRadius ?? 0,
                step.ChamferDistance1 ?? 0,
                step.ChamferDistance2 ?? 0,
                step.RotationDeg ?? 0);
            return CadGeometryMath.ToSpec(raw, true);
        }
        catch (ArgumentException ex)
        {
            throw new DrawStepException("invalid_args", ex.Message);
        }
    }

    private static bool PolygonInscribed(DrawBatchStep step) =>
        step.Inscribed ?? !string.Equals(step.Mode?.Trim(), "circumscribed", StringComparison.OrdinalIgnoreCase);

    private static CadPolylineSpec PlanPolygon(DrawBatchStep step)
    {
        if (step.Mode is { } mode && !mode.Trim().Equals("inscribed", StringComparison.OrdinalIgnoreCase) && !mode.Trim().Equals("circumscribed", StringComparison.OrdinalIgnoreCase))
        {
            throw new DrawStepException("invalid_args", $"Unknown polygon mode: {mode}. Use inscribed or circumscribed (or the inscribed boolean).");
        }

        var center = PointArg(step.Center, "center");
        try
        {
            return CadGeometryMath.BuildRegularPolygon(center.X, center.Y, step.Sides ?? 0, step.Radius ?? 0, PolygonInscribed(step), step.RotationDeg ?? 0);
        }
        catch (ArgumentException ex)
        {
            throw new DrawStepException("invalid_args", ex.Message);
        }
    }

    private static CadPolylineSpec PlanRevCloud(DrawBatchStep step)
    {
        var c1 = step.P1 ?? step.Corner1 ?? step.Start;
        var c2 = step.P2 ?? step.Corner2 ?? step.End;
        if (step.Vertices is not null)
        {
            if (c1 is not null || c2 is not null)
            {
                throw new DrawStepException("invalid_args", "Ambiguous revision cloud: give either vertices or corners (p1, p2), not both.");
            }

            ValidateVertices(step.Vertices);
            if (step.Vertices.Length < 3)
            {
                throw new DrawStepException("too_few_vertices", "A revision cloud from vertices needs at least 3 points.");
            }
        }

        try
        {
            var raw = CadGeometryMath.BuildRevCloud(step.Vertices, c1, c2, step.ArcLength ?? 0, step.BulgeMagnitude ?? DrawDefaultRevCloudBulge);
            return CadGeometryMath.ToSpec(raw, true);
        }
        catch (ArgumentException ex)
        {
            throw new DrawStepException("invalid_args", ex.Message);
        }
    }

    private static EllipsePlan PlanEllipse(DrawBatchStep step)
    {
        var center = PointArg(step.Center, "center");
        if (step.MajorAxis is not null && step.MajorRadius is not null)
        {
            throw new DrawStepException("invalid_args", "Ambiguous ellipse: give either majorAxis [dx, dy] or majorRadius (+ rotationDeg), not both.");
        }

        Vector3d major;
        if (step.MajorAxis is not null)
        {
            major = VectorArg(step.MajorAxis, "majorAxis");
            if (Math.Abs(major.Z) > 1e-9)
            {
                throw new DrawStepException("invalid_args", "majorAxis must lie in the XY plane (dz = 0).");
            }
        }
        else if (step.MajorRadius is { } majorRadius && majorRadius > 0 && double.IsFinite(majorRadius))
        {
            var rotation = DegreesToRadians(step.RotationDeg ?? 0);
            major = new Vector3d(majorRadius * Math.Cos(rotation), majorRadius * Math.Sin(rotation), 0);
        }
        else
        {
            throw new DrawStepException("invalid_args", "Provide either majorAxis [dx, dy] (non-zero) or majorRadius > 0.");
        }

        if (step.RadiusRatio is not null && step.MinorRadius is not null)
        {
            throw new DrawStepException("invalid_args", "Ambiguous ellipse: give either minorRadius or radiusRatio, not both.");
        }

        double ratio;
        if (step.RadiusRatio is { } requestedRatio)
        {
            ratio = requestedRatio;
        }
        else if (step.MinorRadius is { } minorRadius)
        {
            ratio = minorRadius / major.Length;
        }
        else
        {
            throw new DrawStepException("invalid_args", "Provide minorRadius (> 0, <= majorRadius) or radiusRatio (minor/major in (0, 1]).");
        }

        if (!(ratio > 0 && ratio <= 1))
        {
            throw new DrawStepException("invalid_ratio", "radiusRatio (or minorRadius/majorRadius) must be in (0, 1].");
        }

        var startDeg = step.StartAngleDeg ?? 0;
        var endDeg = step.EndAngleDeg ?? 360;
        if (!double.IsFinite(startDeg) || !double.IsFinite(endDeg))
        {
            throw new DrawStepException("invalid_args", "startAngleDeg and endAngleDeg must be finite.");
        }

        // Angles are AutoCAD ellipse angles measured CCW from the major axis. A sweep that is
        // a multiple of 360 is a full (closed) ellipse.
        var sweep = (endDeg - startDeg) % 360;
        return Math.Abs(sweep) < 1e-9
            ? new EllipsePlan(center, major, ratio, 0, 2 * Math.PI)
            : new EllipsePlan(center, major, ratio, DegreesToRadians(startDeg), DegreesToRadians(endDeg));
    }

    private static SplinePlan PlanSpline(DrawBatchStep step)
    {
        var raw = step.FitPoints ?? step.Vertices;
        if (raw is null || raw.Length < 2)
        {
            throw new DrawStepException("too_few_points", "fitPoints requires at least 2 points.");
        }

        ValidateVertices(raw);
        var points = raw.Select((p, i) => PointArg(p, $"fitPoints[{i}]")).ToList();
        for (var i = 1; i < points.Count; i++)
        {
            if (points[i].DistanceTo(points[i - 1]) < 1e-9)
            {
                throw new DrawStepException("invalid_args", $"fitPoints[{i}] duplicates the previous point; consecutive fit points must differ.");
            }
        }

        var degree = step.Degree ?? 3;
        if (degree is < 2 or > 3)
        {
            throw new DrawStepException("invalid_args", "degree must be 2 or 3 for a fit-point spline (default 3 = cubic).");
        }

        var hasStart = step.StartTangent is not null;
        var hasEnd = step.EndTangent is not null;
        if (hasStart != hasEnd)
        {
            throw new DrawStepException("invalid_args", "Provide both startTangent and endTangent, or neither.");
        }

        if (step.Closed)
        {
            if (hasStart)
            {
                throw new DrawStepException("invalid_args", "startTangent/endTangent cannot be combined with closed=true (a closed spline is periodic).");
            }

            // Periodic fit: drop an explicit closing point instead of duplicating it.
            if (points.Count > 1 && points[0].DistanceTo(points[^1]) < 1e-9)
            {
                points.RemoveAt(points.Count - 1);
            }

            if (points.Count < 3)
            {
                throw new DrawStepException("too_few_points", "A closed spline needs at least 3 distinct fit points.");
            }
        }

        Vector3d? startTangent = hasStart ? VectorArg(step.StartTangent, "startTangent") : null;
        Vector3d? endTangent = hasEnd ? VectorArg(step.EndTangent, "endTangent") : null;
        return new SplinePlan(points.ToArray(), degree, step.Closed, startTangent, endTangent);
    }

    private static (string Pattern, double Scale, double AngleDeg) PlanHatch(DrawBatchStep step)
    {
        ValidateVertices(step.Vertices);
        if (step.Vertices!.Length < 3)
        {
            throw new DrawStepException("too_few_vertices", "A hatch boundary needs at least 3 vertices.");
        }

        if (step.Bulges is not null && step.Bulges.Any(b => !double.IsFinite(b)))
        {
            throw new DrawStepException("invalid_args", "bulges must be finite numbers.");
        }

        if (step.Scale is { } scale && !(scale > 0 && double.IsFinite(scale)))
        {
            throw new DrawStepException("invalid_args", "scale must be a finite number greater than 0.");
        }

        var pattern = string.IsNullOrWhiteSpace(step.Pattern) ? "ANSI31" : step.Pattern!.Trim();
        return (pattern, step.Scale ?? 1.0, step.AngleDeg ?? step.RotationDeg ?? 0);
    }

    private static string? NormalizeDimKind(string? kind) => kind?.Trim().ToLowerInvariant();

    private static DimensionPlan PlanDimension(DrawBatchStep step)
    {
        var kind = NormalizeDimKind(step.Kind);
        if (kind is not ("linear" or "aligned" or "radius" or "diameter" or "angular" or "ordinate"))
        {
            throw new DrawStepException("unsupported_kind", $"Unsupported dimension kind: {step.Kind ?? "<missing>"}. kind is required: linear, aligned, radius, diameter, angular or ordinate.");
        }

        var p1Raw = step.P1 ?? step.Start;
        var p2Raw = step.P2 ?? step.End;
        if (p1Raw is null)
        {
            throw new DrawStepException("invalid_points", "p1 is required.");
        }

        var p1 = PointArg(p1Raw, "p1");
        Point3d? p2 = p2Raw is null ? null : PointArg(p2Raw, "p2");
        Point3d? dimLine = step.DimLinePoint is null ? null : PointArg(step.DimLinePoint, "dimLinePoint");
        Point3d? vertex = step.Vertex is null ? null : PointArg(step.Vertex, "vertex");

        if (kind != "ordinate" && p2 is null)
        {
            throw new DrawStepException("invalid_points", $"p2 is required for {kind} dimensions.");
        }

        if (kind is "linear" or "aligned" or "angular" && dimLine is null)
        {
            throw new DrawStepException("invalid_points", "dimLinePoint is required for linear, aligned and angular dimensions.");
        }

        if (kind == "angular" && vertex is null)
        {
            throw new DrawStepException("invalid_points", "vertex (the angle apex) is required for angular dimensions.");
        }

        if (kind == "ordinate" && dimLine is null && p2 is null)
        {
            throw new DrawStepException("invalid_points", "Ordinate dimensions need dimLinePoint (leader end point); p2 is accepted as a fallback.");
        }

        if (kind is "linear" or "aligned" or "radius" or "diameter" && p1.DistanceTo(p2!.Value) < 1e-9)
        {
            throw new DrawStepException("invalid_points", "p1 and p2 must be distinct.");
        }

        if (kind == "angular" && (vertex!.Value.DistanceTo(p1) < 1e-9 || vertex.Value.DistanceTo(p2!.Value) < 1e-9))
        {
            throw new DrawStepException("invalid_points", "vertex must differ from p1 and p2.");
        }

        if (step.DimScale is { } dimScale && !(dimScale > 0 && double.IsFinite(dimScale)))
        {
            throw new DrawStepException("invalid_args", "dimScale must be greater than 0.");
        }

        return new DimensionPlan(kind!, p1, p2, vertex, dimLine, step.RotationDeg ?? 0, step.UseXAxis ?? true);
    }

    private static ObjectId ResolveDimStyle(Database db, Transaction tr, string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return db.Dimstyle;
        }

        var cache = CacheFor(db);
        if (cache is null || !cache.DimStyles.TryGetValue(name, out var id))
        {
            var dimStyleTable = (DimStyleTable)tr.GetObject(db.DimStyleTableId, OpenMode.ForRead);
            id = dimStyleTable.Has(name) ? dimStyleTable[name] : ObjectId.Null;
            if (cache is not null)
            {
                cache.DimStyles[name] = id;
            }
        }

        return id.IsNull
            ? throw new DrawStepException("dimstyle_not_found", $"Dimension style not found: {name}. Create it first with forge_style_dim_create.")
            : id;
    }

    private static LeaderPlan PlanLeader(DrawBatchStep step)
    {
        var raw = step.Points ?? step.Vertices;
        if (raw is null || raw.Length < 2)
        {
            throw new DrawStepException("too_few_points", "points requires at least 2 points (arrow tip first, landing last).");
        }

        ValidateVertices(raw);
        var points = raw.Select((p, i) => PointArg(p, $"points[{i}]")).ToArray();
        var textHeight = step.TextHeight ?? step.Height ?? 2.5;
        if (!(textHeight > 0 && double.IsFinite(textHeight)))
        {
            throw new DrawStepException("invalid_height", "textHeight must be greater than 0.");
        }

        if (step.ArrowSize is { } arrowSize && !(arrowSize >= 0 && double.IsFinite(arrowSize)))
        {
            throw new DrawStepException("invalid_args", "arrowSize must be >= 0.");
        }

        return new LeaderPlan(points, textHeight, step.HasArrowHead ?? true);
    }

    private static TablePlan PlanTable(DrawTableArgs args)
    {
        var cells = args.Rows ?? args.Cells;
        if (cells is null || cells.Length == 0)
        {
            throw new DrawStepException("missing_cells", "rows/cells is required and must contain at least 1 row.");
        }

        var columns = cells.Max(r => r?.Length ?? 0);
        if (columns == 0)
        {
            throw new DrawStepException("missing_cells", "rows/cells must have at least 1 column.");
        }

        var title = string.IsNullOrWhiteSpace(args.Title) ? null : args.Title;
        var totalRows = cells.Length + (title is null ? 0 : 1);
        if ((long)totalRows * columns > DrawTableMaxCells)
        {
            throw new DrawStepException("table_too_large", $"A table may hold at most {DrawTableMaxCells} cells (got {totalRows} rows x {columns} columns).");
        }

        var rowHeight = PositiveOrDefault(args.RowHeight, 8.0, "rowHeight");
        var defaultWidth = PositiveOrDefault(args.ColumnWidth, 30.0, "columnWidth");
        var textHeight = PositiveOrDefault(args.TextHeight, 2.5, "textHeight");
        var widths = new double[columns];
        for (var c = 0; c < columns; c++)
        {
            if (args.ColumnWidths is { } requested && c < requested.Length)
            {
                if (!(requested[c] > 0 && double.IsFinite(requested[c])))
                {
                    throw new DrawStepException("invalid_args", $"columnWidths[{c}] must be greater than 0.");
                }

                widths[c] = requested[c];
            }
            else
            {
                widths[c] = defaultWidth;
            }
        }

        return new TablePlan(PointArg(args.Position, "position"), cells, cells.Length, columns, title, rowHeight, widths, textHeight);
    }

    private static (ObjectId BlockId, Point3d Position, double Scale) PlanBlockInsert(Database db, Transaction tr, DrawBatchStep step)
    {
        var blockName = step.BlockName ?? step.Name;
        if (string.IsNullOrWhiteSpace(blockName))
        {
            throw new DrawStepException("missing_block_name", "name/blockName is required for block_insert step.");
        }

        var blockId = ResolveBlockId(db, tr, blockName);
        if (step.Scale is { } scale && !(scale > 0 && double.IsFinite(scale)))
        {
            throw new DrawStepException("invalid_scale", "scale must be > 0.");
        }

        RequireAuthorizedAttributes(step.Attributes);
        var position = PointArg(step.Position ?? step.Center, "position");
        return (blockId, position, step.Scale ?? 1.0);
    }

    /// <summary>Drawing-registry and standards-pack rules for attribute values (block inserts and tag bubbles).</summary>
    private static void RequireAuthorizedAttributes(IReadOnlyDictionary<string, string>? attributes)
    {
        if (attributes is not { Count: > 0 })
        {
            return;
        }

        var registry = DrawingRegistryStore.Current;
        var pack = StandardsPackStore.Current;
        foreach (var pair in attributes)
        {
            if (registry is not null && !registry.TryAuthorizeAttribute(pair.Key, pair.Value ?? "", out var code, out var message))
            {
                throw new DrawStepException(code!, message!);
            }

            if (pack is not null && DrawingRegistry.IsDrawingNumberTag(pair.Key) &&
                !pack.TryValidateDrawingNo(pair.Value ?? "", out var packCode, out var packMessage))
            {
                throw new DrawStepException(packCode!, packMessage!);
            }
        }
    }

    // ===================== small helpers =====================

    private static string NormalizeStepTool(string? tool)
    {
        var normalized = tool?.Trim().ToLowerInvariant() ?? "";
        if (normalized.StartsWith("forge_draw_", StringComparison.Ordinal))
        {
            normalized = normalized["forge_draw_".Length..];
        }
        else if (normalized == "forge_block_insert")
        {
            normalized = "block_insert";
        }

        return normalized;
    }

    private static DrawStepException UnsupportedTool(string? tool) =>
        new("unsupported_tool", $"Unsupported batch tool: {tool ?? "<missing>"}. Use one of: {string.Join(", ", DrawBatchTools)}.");

    private static Point3d PointArg(double[]? point, string name)
    {
        if (point is null)
        {
            throw new DrawStepException("invalid_args", $"{name} is required ([x, y] or [x, y, z]).");
        }

        try
        {
            return ToPoint3d(point);
        }
        catch (ArgumentException ex)
        {
            throw new DrawStepException("invalid_args", $"{name}: {ex.Message}");
        }
    }

    private static Vector3d VectorArg(double[]? vector, string name)
    {
        var p = PointArg(vector, name);
        var result = new Vector3d(p.X, p.Y, p.Z);
        if (result.Length < 1e-12)
        {
            throw new DrawStepException("invalid_args", $"{name} must be a non-zero vector.");
        }

        return result;
    }

    private static double RequirePositiveRadius(double? radius)
    {
        if (radius is not { } value || !(value > 0 && double.IsFinite(value)))
        {
            throw new DrawStepException("invalid_radius", "Radius must be greater than 0.");
        }

        return value;
    }

    private static double PositiveOrDefault(double? value, double fallback, string name)
    {
        if (value is null)
        {
            return fallback;
        }

        if (!(value.Value > 0 && double.IsFinite(value.Value)))
        {
            throw new DrawStepException("invalid_args", $"{name} must be greater than 0.");
        }

        return value.Value;
    }

    private static void ValidateWidth(double? constantWidth)
    {
        if (constantWidth is { } width && !(width >= 0 && double.IsFinite(width)))
        {
            throw new DrawStepException("invalid_args", "constantWidth must be >= 0.");
        }
    }

    private static void BestEffort(Action action)
    {
        try
        {
            action();
        }
        catch (System.Exception)
        {
            // Optional cosmetic step (e.g. a cell style missing from a custom table style).
        }
    }

    private static int? TryLeaderVertexCount(MLeader mleader)
    {
        try
        {
            var total = 0;
            foreach (int leaderIndex in mleader.GetLeaderIndexes())
            {
                foreach (int lineIndex in mleader.GetLeaderLineIndexes(leaderIndex))
                {
                    total += mleader.VerticesCount(lineIndex);
                }
            }

            return total;
        }
        catch (System.Exception)
        {
            return null;
        }
    }

    private static void RequireLayer(Database db, Transaction tr, string? layer)
    {
        if (layer is not null && LookupLayerId(db, tr, layer).IsNull)
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

    private static bool IsStepError(System.Exception ex) =>
        ex is DrawStepException or ArgumentException or Autodesk.AutoCAD.Runtime.Exception;

    private static (string Code, string Message) StepError(System.Exception ex) => ex switch
    {
        DrawStepException step => (step.Code, step.Message),
        Autodesk.AutoCAD.Runtime.Exception acad when acad.ErrorStatus == Autodesk.AutoCAD.Runtime.ErrorStatus.OnLockedLayer
            => ("layer_locked", "The target layer is locked; unlock it first."),
        Autodesk.AutoCAD.Runtime.Exception acad => ("autocad_error", $"AutoCAD rejected the operation ({acad.ErrorStatus}): {acad.Message}"),
        ArgumentException => ("invalid_args", ex.Message),
        _ => ("step_failed", ex.Message)
    };

    private static double[] PointList(Point3d point) => [point.X, point.Y, point.Z];

    private static string MTextContents(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return string.Empty;
        }

        var normalized = text.Replace("\r\n", "\n");
        var sb = new System.Text.StringBuilder(normalized.Length + 8);
        for (var i = 0; i < normalized.Length; i++)
        {
            var ch = normalized[i];
            if (ch == '\n')
            {
                sb.Append("\\P");
                continue;
            }

            if (ch is '{' or '}')
            {
                sb.Append('\\').Append(ch);
                continue;
            }

            if (ch == '\\')
            {
                if (i + 1 < normalized.Length)
                {
                    var next = normalized[i + 1];
                    if (next is 'P' or '~' or '\\' or '{' or '}')
                    {
                        sb.Append('\\').Append(next);
                        i++;
                        continue;
                    }

                    if ((next is 'U' or 'u') && i + 6 < normalized.Length && normalized[i + 2] == '+')
                    {
                        sb.Append(normalized, i, 7);
                        i += 6;
                        continue;
                    }
                }

                sb.Append("\\\\");
                continue;
            }

            sb.Append(ch);
        }

        return sb.ToString();
    }

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

    private static double? TryArea(Polyline pl)
    {
        try
        {
            return pl.Area;
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

    private sealed record StepOutcome(ObjectId Id, ObjectId[]? Extra = null, string[]? Warnings = null);
    private sealed record EllipsePlan(Point3d Center, Vector3d MajorAxis, double Ratio, double StartRad, double EndRad);
    private sealed record SplinePlan(Point3d[] Points, int Degree, bool Closed, Vector3d? StartTangent, Vector3d? EndTangent);
    private sealed record DimensionPlan(string Kind, Point3d P1, Point3d? P2, Point3d? Vertex, Point3d? DimLine, double RotationDeg, bool UseXAxis);
    private sealed record LeaderPlan(Point3d[] Points, double TextHeight, bool HasArrowHead);
    private sealed record TablePlan(Point3d Position, string?[][] Cells, int DataRows, int Columns, string? Title, double RowHeight, double[] ColumnWidths, double TextHeight)
    {
        public int TotalRows => DataRows + (Title is null ? 0 : 1);
    }

    private sealed record DrawLineArgs { public double[]? Start { get; init; } public double[]? End { get; init; } public string? Layer { get; init; } public int? ColorIndex { get; init; } }
    private sealed record DrawPolylineArgs { public double[][]? Vertices { get; init; } public bool Closed { get; init; } public double[]? Bulges { get; init; } public double? ConstantWidth { get; init; } public string? Layer { get; init; } public int? ColorIndex { get; init; } }
    private sealed record DrawRectangleArgs { public double[]? P1 { get; init; } public double[]? P2 { get; init; } public double[]? Corner1 { get; init; } public double[]? Corner2 { get; init; } public double[]? Center { get; init; } public double? Width { get; init; } public double? Height { get; init; } public double? FilletRadius { get; init; } public double? ChamferDistance1 { get; init; } public double? ChamferDistance2 { get; init; } public double RotationDeg { get; init; } public double? ConstantWidth { get; init; } public string? Layer { get; init; } public int? ColorIndex { get; init; } }
    private sealed record DrawPolygonArgs { public double[]? Center { get; init; } public int Sides { get; init; } public double Radius { get; init; } public bool? Inscribed { get; init; } public string? Mode { get; init; } public double RotationDeg { get; init; } public double? ConstantWidth { get; init; } public string? Layer { get; init; } public int? ColorIndex { get; init; } }
    private sealed record DrawCircleArgs { public double[]? Center { get; init; } public double Radius { get; init; } public string? Layer { get; init; } public int? ColorIndex { get; init; } }
    private sealed record DrawArcArgs { public double[]? Center { get; init; } public double Radius { get; init; } public double StartAngleDeg { get; init; } public double EndAngleDeg { get; init; } public string? Layer { get; init; } public int? ColorIndex { get; init; } }
    private sealed record DrawEllipseArgs { public double[]? Center { get; init; } public double[]? MajorAxis { get; init; } public double? MajorRadius { get; init; } public double? MinorRadius { get; init; } public double? RadiusRatio { get; init; } public double RotationDeg { get; init; } public double? StartAngleDeg { get; init; } public double? EndAngleDeg { get; init; } public string? Layer { get; init; } public int? ColorIndex { get; init; } }
    private sealed record DrawSplineArgs { public double[][]? FitPoints { get; init; } public bool Closed { get; init; } public int? Degree { get; init; } public double[]? StartTangent { get; init; } public double[]? EndTangent { get; init; } public string? Layer { get; init; } public int? ColorIndex { get; init; } }
    private sealed record DrawRevCloudArgs { public double[]? P1 { get; init; } public double[]? P2 { get; init; } public double[]? Corner1 { get; init; } public double[]? Corner2 { get; init; } public double[][]? Vertices { get; init; } public double? ArcLength { get; init; } public double? BulgeMagnitude { get; init; } public double? ConstantWidth { get; init; } public string? Layer { get; init; } public int? ColorIndex { get; init; } }
    private sealed record DrawTextArgs { public double[]? Position { get; init; } public double Height { get; init; } public string? Text { get; init; } public double RotationDeg { get; init; } public string? Layer { get; init; } public int? ColorIndex { get; init; } }
    private sealed record DrawMTextArgs { public double[]? Position { get; init; } public double Height { get; init; } public string? Text { get; init; } public double? Width { get; init; } public string? AttachmentPoint { get; init; } public double RotationDeg { get; init; } public string? Layer { get; init; } public int? ColorIndex { get; init; } }
    private sealed record DrawHatchArgs { public string? Pattern { get; init; } public double? Scale { get; init; } public double AngleDeg { get; init; } public HatchBoundaryArgs? Boundary { get; init; } public string? Layer { get; init; } }
    private sealed record HatchBoundaryArgs { public string? Handle { get; init; } public double[][]? Vertices { get; init; } }
    private sealed record DrawDimensionArgs { public string? Kind { get; init; } public double[]? P1 { get; init; } public double[]? P2 { get; init; } public double[]? Vertex { get; init; } public double[]? DimLinePoint { get; init; } public double RotationDeg { get; init; } public bool UseXAxis { get; init; } = true; public string? TextOverride { get; init; } public string? DimStyle { get; init; } public double? DimScale { get; init; } public string? Layer { get; init; } public int? ColorIndex { get; init; } }
    private sealed record DrawLeaderArgs { public double[][]? Points { get; init; } public string? Text { get; init; } public double? TextHeight { get; init; } public double? ArrowSize { get; init; } public bool HasArrowHead { get; init; } = true; public string? Layer { get; init; } public int? ColorIndex { get; init; } }
    private sealed record DrawTableArgs { public double[]? Position { get; init; } public string?[][]? Rows { get; init; } public string?[][]? Cells { get; init; } public string? Title { get; init; } public bool HasTitleRow { get; init; } public double? RowHeight { get; init; } public double? ColumnWidth { get; init; } public double[]? ColumnWidths { get; init; } public double? TextHeight { get; init; } public string? Layer { get; init; } public int? ColorIndex { get; init; } }
    private sealed record DrawBatchArgs
    {
        public DrawBatchStep[]? Steps { get; init; }

        /// <summary>true = validate every step first and create all-or-nothing in one transaction (no nested transactions).</summary>
        public bool Atomic { get; init; }

        /// <summary>full (default, per-step results), errors (failed steps + handles), summary (counts + failed steps only).</summary>
        public string? ResultMode { get; init; }
    }

    // Keep the property set identical to Forge.Server DrawStepDto (camelCase on the wire).
    private sealed record DrawBatchStep
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

        // Speed wave (2026-10-29): text style, background mask, multileader style/block content.
        public string? TextStyle { get; init; }
        public bool? BackgroundMask { get; init; }
        public double? MaskScale { get; init; }
        public string? LeaderStyle { get; init; }
        public string? ContentBlock { get; init; }
    }
}
