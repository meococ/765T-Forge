using Autodesk.AutoCAD.Colors;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using Forge.Shared;

namespace Forge.Plugin;

// docs/cad-authoring-tools.md C20–C23 — shared plumbing for the authoring wave. Every
// forge_draw_* / forge_entity_* / forge_block_* handler builds on these helpers so the
// transaction, handle and error conventions stay identical across the partial files.
public sealed partial class PluginCommandProcessor
{
    /// <summary>Max entities accepted by one authoring call (batch steps, erase bulk, list cap).</summary>
    internal const int AuthoringMaxEntities = 1000;

    /// <summary>Bulk-acknowledgement threshold for destructive entity tools.</summary>
    internal const int AuthoringBulkAckThreshold = 100;

    internal const string AuthoringJustification =
        "Authoring tools create or modify only what their explicit parameters name — no broad " +
        "selection, no command strings — so the backup policy follows the C15 precedent: " +
        "additive-only tools skip the DWG copy, mutating tools require one.";

    /// <summary>Model-space BTR opened for write inside the caller's transaction.</summary>
    internal static BlockTableRecord ModelSpaceForWrite(Database db, Transaction tr)
    {
        var blockTable = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
        return (BlockTableRecord)tr.GetObject(blockTable[BlockTableRecord.ModelSpace], OpenMode.ForWrite);
    }

    /// <summary>
    /// Sets database defaults, appends to model space and registers the new entity with the
    /// transaction. The database owns the entity afterwards — never dispose it.
    /// </summary>
    internal static ObjectId AppendToModelSpace(Database db, Transaction tr, Entity entity)
    {
        entity.SetDatabaseDefaults();
        var modelSpace = ModelSpaceForWrite(db, tr);
        var id = modelSpace.AppendEntity(entity);
        tr.AddNewlyCreatedDBObject(entity, true);
        return id;
    }

    /// <summary>
    /// Appends an entity that already carries its properties (e.g. a clone) to model space —
    /// no database defaults are applied, so cloned geometry keeps layer/color/linetype.
    /// </summary>
    internal static ObjectId AppendExistingToModelSpace(Database db, Transaction tr, Entity entity)
    {
        var modelSpace = ModelSpaceForWrite(db, tr);
        var id = modelSpace.AppendEntity(entity);
        tr.AddNewlyCreatedDBObject(entity, true);
        return id;
    }

    internal static string HandleString(DBObject obj) => obj.Handle.ToString();

    /// <summary>Resolves a handle string (hex) to an ObjectId, or null when unknown/invalid.</summary>
    internal static ObjectId? TryResolveHandle(Database db, string handle)
    {
        if (string.IsNullOrWhiteSpace(handle))
        {
            return null;
        }

        try
        {
            if (!long.TryParse(handle.Trim(), System.Globalization.NumberStyles.HexNumber, null, out var value))
            {
                return null;
            }

            return db.GetObjectId(false, new Handle(value), 0);
        }
        catch
        {
            return null;
        }
    }

    internal static Entity? TryOpenEntityForWrite(Database db, Transaction tr, string handle)
    {
        var id = TryResolveHandle(db, handle);
        return id is null ? null : tr.GetObject(id.Value, OpenMode.ForWrite) as Entity;
    }

    internal static Entity? TryOpenEntityForRead(Database db, Transaction tr, string handle)
    {
        var id = TryResolveHandle(db, handle);
        return id is null ? null : tr.GetObject(id.Value, OpenMode.ForRead) as Entity;
    }

    /// <summary>
    /// Resolves explicit handle strings to write-opened entities. Unknown handles are reported
    /// per handle and skipped — the caller surfaces them in the result; nothing is partially
    /// modified silently.
    /// </summary>
    internal static List<Entity> OpenHandlesForWrite(Database db, Transaction tr, IEnumerable<string> handles, List<string> unknown)
    {
        var opened = new List<Entity>();
        foreach (var handle in handles)
        {
            var entity = TryOpenEntityForWrite(db, tr, handle);
            if (entity is null)
            {
                unknown.Add(handle);
                continue;
            }

            opened.Add(entity);
        }

        return opened;
    }

    /// <summary>[x, y] or [x, y, z] → Point3d. Anything else throws (surfaced as invalid_args).</summary>
    internal static Point3d ToPoint3d(double[]? point)
    {
        if (point is null || point.Length is < 2 or > 3 || point.Any(double.IsNaN) || point.Any(double.IsInfinity))
        {
            throw new ArgumentException("Point must be [x, y] or [x, y, z].");
        }

        return point.Length == 2 ? new Point3d(point[0], point[1], 0) : new Point3d(point[0], point[1], point[2]);
    }

    internal static double DegreesToRadians(double degrees) => degrees * Math.PI / 180.0;

    /// <summary>True when the layer table holds the exact name (case-insensitive per CAD rules).</summary>
    internal static bool LayerExists(Database db, Transaction tr, string layer)
    {
        var layerTable = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);
        return layerTable.Has(layer);
    }

    internal static bool LinetypeExists(Database db, Transaction tr, string linetype)
    {
        var linetypeTable = (LinetypeTable)tr.GetObject(db.LinetypeTableId, OpenMode.ForRead);
        return linetypeTable.Has(linetype);
    }

    /// <summary>Applies optional color index (0 ByBlock, 1–255 ACI, 256 ByLayer) to an entity.</summary>
    internal static void ApplyColor(Entity entity, int? colorIndex)
    {
        if (colorIndex is null)
        {
            return;
        }

        if (colorIndex is < 0 or > 256)
        {
            throw new ArgumentException("colorIndex must be 0 (ByBlock), 1–255, or 256 (ByLayer).");
        }

        entity.Color = Color.FromColorIndex(ColorMethod.ByAci, (short)colorIndex.Value);
    }

    /// <summary>Bounding box when the entity has one (curves/text); null otherwise (empty extents).</summary>
    internal static double[]? TryExtents(Entity entity)
    {
        try
        {
            var extents = entity.GeometricExtents;
            return
            [
                extents.MinPoint.X, extents.MinPoint.Y, extents.MinPoint.Z,
                extents.MaxPoint.X, extents.MaxPoint.Y, extents.MaxPoint.Z
            ];
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Common read-back fields for any entity (handle, type, layer, color, bbox).</summary>
    internal static object EntitySummary(Entity entity)
    {
        return new
        {
            handle = HandleString(entity),
            type = entity.GetType().Name,
            layer = entity.Layer,
            color = entity.Color.ColorValue.Name ?? entity.Color.ColorIndex.ToString(),
            bbox = TryExtents(entity)
        };
    }

    /// <summary>Block-name validity per AutoCAD rules; surfaces a typed error instead of an API throw.</summary>
    internal static bool IsValidBlockName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Length > 255)
        {
            return false;
        }

        const string invalid = "<>/\\\":;?*|=`";
        return name.IndexOfAny(invalid.ToCharArray()) < 0;
    }
}
