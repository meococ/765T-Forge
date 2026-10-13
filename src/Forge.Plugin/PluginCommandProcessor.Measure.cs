using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using Forge.Shared;

namespace Forge.Plugin;

// docs/cad-authoring-tools.md C24 — forge_measure_batch and forge_entity_array. Measurement
// comes from the geometry engine (Curve.GetDistanceAtParameter / Area), never from re-reading
// vertices — shoelace math silently loses area on curved segments. Array is entity_copy
// generalized: clones + TransformBy placements, sources untouched.
public sealed partial class PluginCommandProcessor
{
    /// <summary>Total clones one array call may create.</summary>
    internal const int ArrayMaxClones = 5000;

    private static ForgeResult MeasureBatch(ForgeCommand command)
    {
        var args = Args<MeasureBatchArgs>(command);
        var hasPointPair = args.P1 is not null && args.P2 is not null;
        if ((args.Handles is null || args.Handles.Length == 0) && !hasPointPair)
        {
            return ForgeResult.Failure(
                command.Id,
                "missing_query",
                "Provide handles[] and/or p1+p2.",
                "forge_entity_list and forge_linework_dump are the discovery paths for handles.");
        }

        if ((args.P1 is null) != (args.P2 is null))
        {
            return ForgeResult.Failure(command.Id, "missing_query", "p1 and p2 must be provided together.");
        }

        if (args.Handles is { Length: > AuthoringMaxEntities })
        {
            return ForgeResult.Failure(command.Id, "too_many_handles", $"A single call accepts at most {AuthoringMaxEntities} handles.", "Split the batch across calls.");
        }

        var db = ActiveDb;
        using var tr = db.TransactionManager.StartTransaction();

        double? pointDistance = null;
        if (hasPointPair)
        {
            try
            {
                pointDistance = ToPoint3d(args.P1).DistanceTo(ToPoint3d(args.P2));
            }
            catch (ArgumentException ex)
            {
                return ForgeResult.Failure(command.Id, "invalid_args", ex.Message);
            }
        }

        var entities = new List<object>();
        double[]? bboxUnion = null;
        foreach (var handle in args.Handles ?? [])
        {
            var entity = TryOpenEntityForRead(db, tr, handle);
            if (entity is null)
            {
                entities.Add(new { handle, found = false });
                continue;
            }

            double? length = null;
            double? area = null;
            if (entity is Curve curve)
            {
                length = TryCurveLength(curve);
                area = TryArea(curve);
            }

            var bbox = TryExtents(entity);
            if (bbox is not null)
            {
                bboxUnion = bboxUnion is null
                    ? bbox
                    :
                    [
                        Math.Min(bboxUnion[0], bbox[0]), Math.Min(bboxUnion[1], bbox[1]), Math.Min(bboxUnion[2], bbox[2]),
                        Math.Max(bboxUnion[3], bbox[3]), Math.Max(bboxUnion[4], bbox[4]), Math.Max(bboxUnion[5], bbox[5])
                    ];
            }

            entities.Add(new { handle, found = true, type = entity.GetType().Name, layer = entity.Layer, length, area });
        }

        var measured = entities.Count(x => x.GetType().GetProperty("found")?.GetValue(x) is true);
        return ForgeResult.Success(
            command.Id,
            new { entities, pointDistance, bbox = bboxUnion },
            verification: new ForgeVerification
            {
                Attempted = true,
                Passed = measured > 0 || pointDistance is not null,
                Message = "Values come from the geometry engine, not from re-reading vertices.",
                ReadBack = new { measured, pointDistance }
            });
    }

    private static ForgeResult EntityArray(ForgeCommand command)
    {
        var args = Args<EntityArrayArgs>(command);
        if (CheckHandles(command, args.Handles, "missing_handles") is { } denied)
        {
            return denied;
        }

        var kind = args.Kind?.ToLowerInvariant() ?? "";
        if (kind is not ("rectangular" or "polar"))
        {
            return ForgeResult.Failure(command.Id, "unsupported_kind", $"Unsupported array kind: {args.Kind ?? "<missing>"}. Use rectangular or polar.");
        }

        List<Matrix3d> placements;
        try
        {
            placements = kind == "rectangular"
                ? BuildRectangularPlacements(args)
                : BuildPolarPlacements(args, placementBases: null);
        }
        catch (ArgumentException ex)
        {
            return ForgeResult.Failure(command.Id, "invalid_args", ex.Message);
        }
        catch (DrawStepException ex)
        {
            return ForgeResult.Failure(command.Id, ex.Code, ex.Message, "Reduce rows/columns/count or split into several array calls.");
        }

        if (command.DryRun)
        {
            return DryRun(command, new { args.Kind, sources = args.Handles!.Length, placements, totalClones = placements.Count * args.Handles.Length });
        }

        var db = ActiveDb;
        using var tr = db.TransactionManager.StartTransaction();
        // Polar "keep orientation" needs each source's bbox center, available only inside the
        // transaction — rebuild the placements now that we can read the entities.
        if (kind == "polar")
        {
            var bases = new List<(double[]? Bbox, Point3d Center)>();
            foreach (var handle in args.Handles!)
            {
                var source = TryOpenEntityForRead(db, tr, handle);
                bases.Add((source is null ? null : TryExtents(source), ToPoint3d(args.Center)));
            }

            placements = BuildPolarPlacements(args, bases);
        }

        var unknown = new List<string>();
        var created = new List<Entity>();
        foreach (var handle in args.Handles!)
        {
            var source = TryOpenEntityForRead(db, tr, handle);
            if (source is null)
            {
                unknown.Add(handle);
                continue;
            }

            foreach (var placement in placements)
            {
                var clone = (Entity)source.Clone();
                clone.TransformBy(placement);
                AppendExistingToModelSpace(db, tr, clone);
                created.Add(clone);
            }
        }

        var newHandles = created.Select(HandleString).ToArray();
        var readBack = created.Select(EntitySummary).ToArray();
        tr.Commit();
        return ForgeResult.Success(
            command.Id,
            new { created = created.Count, unknownHandles = unknown, newHandles },
            verification: new ForgeVerification
            {
                Attempted = true,
                Passed = created.Count > 0,
                Message = "Array placements applied to clones; sources untouched.",
                ReadBack = readBack
            });
    }

    private static List<Matrix3d> BuildRectangularPlacements(EntityArrayArgs args)
    {
        var rows = args.Rows ?? 1;
        var columns = args.Columns ?? 1;
        if (rows < 1 || columns < 1)
        {
            throw new DrawStepException("invalid_args", "rows and columns must be at least 1.");
        }

        if (args.RowSpacing == 0 && args.ColSpacing == 0)
        {
            throw new DrawStepException("invalid_args", "At least one of rowSpacing / colSpacing must be non-zero.");
        }

        if ((long)rows * columns - 1 > ArrayMaxClones)
        {
            throw new DrawStepException("array_too_large", $"A rectangular array may create at most {ArrayMaxClones} clones.");
        }

        var placements = new List<Matrix3d>();
        for (var r = 0; r < rows; r++)
        {
            for (var c = 0; c < columns; c++)
            {
                if (r == 0 && c == 0)
                {
                    continue; // the source occupies the origin placement
                }

                placements.Add(Matrix3d.Displacement(new Vector3d(c * (args.ColSpacing ?? 0), r * (args.RowSpacing ?? 0), 0)));
            }
        }

        return placements;
    }

    private static List<Matrix3d> BuildPolarPlacements(EntityArrayArgs args, List<(double[]? Bbox, Point3d Center)>? placementBases)
    {
        if (args.Center is null)
        {
            throw new ArgumentException("center is required for a polar array.");
        }

        var count = args.Count ?? 0;
        if (count is < 2 or > ArrayMaxClones)
        {
            throw new DrawStepException("invalid_args", $"count must be between 2 and {ArrayMaxClones}.");
        }

        var totalDegrees = args.AngleTotalDeg ?? 360;
        if (totalDegrees == 0)
        {
            throw new DrawStepException("invalid_args", "angleTotalDeg must not be 0.");
        }

        var center = ToPoint3d(args.Center);
        var stepDegrees = totalDegrees / count;
        var placements = new List<Matrix3d>();
        for (var i = 1; i < count; i++)
        {
            var rotation = Matrix3d.Rotation(DegreesToRadians(stepDegrees * i), Vector3d.ZAxis, center);
            if (args.RotateItems || placementBases is null)
            {
                placements.Add(rotation);
                continue;
            }

            // Keep the item's orientation: rotate its bbox center around the array center,
            // then translate by the difference instead of spinning the geometry.
            var baseBbox = placementBases[i - 1].Bbox;
            if (baseBbox is null)
            {
                placements.Add(rotation);
                continue;
            }

            var itemCenter = new Point3d((baseBbox[0] + baseBbox[3]) / 2, (baseBbox[1] + baseBbox[4]) / 2, (baseBbox[2] + baseBbox[5]) / 2);
            var rotatedCenter = itemCenter.TransformBy(rotation);
            placements.Add(Matrix3d.Displacement(rotatedCenter - itemCenter));
        }

        return placements;
    }

    private static double? TryCurveLength(Curve curve)
    {
        try
        {
            return curve.GetDistanceAtParameter(curve.EndParam) - curve.GetDistanceAtParameter(curve.StartParam);
        }
        catch
        {
            return null;
        }
    }

    private static double? TryArea(Curve curve)
    {
        try
        {
            return curve.Area;
        }
        catch
        {
            return null;
        }
    }

    private sealed record MeasureBatchArgs
    {
        public string[]? Handles { get; init; }
        public double[]? P1 { get; init; }
        public double[]? P2 { get; init; }
    }

    private sealed record EntityArrayArgs
    {
        public string[]? Handles { get; init; }
        public string? Kind { get; init; }
        public int? Rows { get; init; }
        public int? Columns { get; init; }
        public double? RowSpacing { get; init; }
        public double? ColSpacing { get; init; }
        public double[]? Center { get; init; }
        public int? Count { get; init; }
        public double? AngleTotalDeg { get; init; }
        public bool RotateItems { get; init; } = true;
    }
}
