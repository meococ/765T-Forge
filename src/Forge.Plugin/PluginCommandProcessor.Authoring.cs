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
        // The id is cached during forge_draw_batch; the record is still opened in the caller's transaction.
        return (BlockTableRecord)tr.GetObject(ModelSpaceIdFor(db, tr), OpenMode.ForWrite);
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

    /// <summary>
    /// Write-opens the entity for a handle, or null when the handle is unknown, erased, not an
    /// entity, or the entity sits on a locked layer (never throws eWasErased / eOnLockedLayer).
    /// Use the overload with <see cref="EntityOpenStatus"/> to report which of these applied.
    /// </summary>
    internal static Entity? TryOpenEntityForWrite(Database db, Transaction tr, string handle) =>
        TryOpenEntityForWrite(db, tr, handle, out _);

    internal static Entity? TryOpenEntityForWrite(Database db, Transaction tr, string handle, out EntityOpenStatus status) =>
        TryOpenEntity(db, tr, handle, forWrite: true, out status);

    internal static Entity? TryOpenEntityForRead(Database db, Transaction tr, string handle)
    {
        var entity = TryOpenEntity(db, tr, handle, forWrite: false, out _);
        return entity is not null && IsInLayoutSpace(tr, entity) ? entity : null;
    }

    /// <summary>
    /// True when <paramref name="entity"/> is a top-level entity residing directly inside a layout
    /// block (ModelSpace or PaperSpace), excluding sub-entities (<see cref="AttributeReference"/>,
    /// <see cref="Vertex"/>), block-definition members, and the root paper-space viewport (#1).
    /// </summary>
    internal static bool IsInLayoutSpace(Transaction tr, Entity entity) =>
        entity is not (AttributeReference or Vertex)
        && !(entity is Viewport vp && vp.Number == 1)
        && !entity.OwnerId.IsNull
        && !entity.OwnerId.IsErased
        && tr.GetObject(entity.OwnerId, OpenMode.ForRead) is BlockTableRecord { IsLayout: true };

    /// <summary>
    /// Resolves explicit handle strings to write-opened entities. Unknown handles are reported
    /// per handle and skipped — the caller surfaces them in the result; nothing is partially
    /// modified silently. Erased handles count as unknown. Entities on a locked layer go to
    /// <paramref name="locked"/> when supplied, otherwise to <paramref name="unknown"/>.
    /// </summary>
    internal static List<Entity> OpenHandlesForWrite(Database db, Transaction tr, IEnumerable<string> handles, List<string> unknown, List<string>? locked = null)
    {
        var opened = new List<Entity>();
        var seen = new HashSet<ObjectId>();
        foreach (var handle in handles)
        {
            var entity = TryOpenEntityForWrite(db, tr, handle, out var status);
            if (entity is null)
            {
                if (status == EntityOpenStatus.LayerLocked && locked is not null)
                {
                    locked.Add(handle);
                }
                else
                {
                    unknown.Add(handle);
                }

                continue;
            }

            if (seen.Add(entity.ObjectId))
            {
                opened.Add(entity);
            }
        }

        return opened;
    }

    /// <summary>Why <see cref="TryOpenEntity"/> returned (or did not return) an entity.</summary>
    internal enum EntityOpenStatus
    {
        Ok,
        NotFound,
        Erased,
        NotAnEntity,
        LayerLocked,
        NotInLayout
    }

    /// <summary>
    /// Opens the entity for a handle without throwing: unknown/invalid handle → NotFound, erased
    /// object → Erased, non-entity object → NotAnEntity. For write the entity is opened for read
    /// first, verified to reside directly in a layout block (ModelSpace or PaperSpace — never inside
    /// a block definition, AttributeReference, Vertex sub-entity, or root paper-space viewport #1),
    /// and its layer checked; a locked layer → LayerLocked, otherwise it is upgraded to write.
    /// </summary>
    internal static Entity? TryOpenEntity(Database db, Transaction tr, string handle, bool forWrite, out EntityOpenStatus status)
    {
        var id = TryResolveHandle(db, handle);
        if (id is null || id.Value.IsNull || !id.Value.IsValid)
        {
            status = EntityOpenStatus.NotFound;
            return null;
        }

        if (id.Value.IsErased)
        {
            status = EntityOpenStatus.Erased;
            return null;
        }

        DBObject obj;
        try
        {
            obj = tr.GetObject(id.Value, OpenMode.ForRead, false);
        }
        catch (Autodesk.AutoCAD.Runtime.Exception)
        {
            status = EntityOpenStatus.NotFound;
            return null;
        }

        if (obj is not Entity entity)
        {
            status = EntityOpenStatus.NotAnEntity;
            return null;
        }

        if (forWrite)
        {
            if (!IsInLayoutSpace(tr, entity))
            {
                status = EntityOpenStatus.NotInLayout;
                return null;
            }

            if (IsOnLockedLayer(tr, entity))
            {
                status = EntityOpenStatus.LayerLocked;
                return null;
            }

            if (!entity.IsWriteEnabled)
            {
                entity.UpgradeOpen();
            }
        }

        status = EntityOpenStatus.Ok;
        return entity;
    }

    /// <summary>True when the entity's layer is locked (writes would throw eOnLockedLayer).</summary>
    internal static bool IsOnLockedLayer(Transaction tr, Entity entity)
    {
        var layerId = entity.LayerId;
        return !layerId.IsNull && tr.GetObject(layerId, OpenMode.ForRead) is LayerTableRecord { IsLocked: true };
    }

    /// <summary>Stable error code for a failed open: layer_locked, handle_erased, not_an_entity, not_in_layout, handle_not_found.</summary>
    internal static string OpenStatusCode(EntityOpenStatus status) => status switch
    {
        EntityOpenStatus.LayerLocked => "layer_locked",
        EntityOpenStatus.Erased => "handle_erased",
        EntityOpenStatus.NotAnEntity => "not_an_entity",
        EntityOpenStatus.NotInLayout => "not_in_layout",
        _ => "handle_not_found"
    };

    /// <summary>
    /// Appends an entity that already carries its properties to the block that owns its source
    /// (model space, a paper-space layout or a block definition) instead of always model space.
    /// Falls back to model space when the owner is null or not a block table record.
    /// </summary>
    internal static ObjectId AppendToOwner(Database db, Transaction tr, ObjectId ownerId, Entity entity)
    {
        BlockTableRecord owner;
        if (!ownerId.IsNull && !ownerId.IsErased && tr.GetObject(ownerId, OpenMode.ForRead) is BlockTableRecord btr)
        {
            if (!btr.IsWriteEnabled)
            {
                btr.UpgradeOpen();
            }

            owner = btr;
        }
        else
        {
            owner = ModelSpaceForWrite(db, tr);
        }

        var id = owner.AppendEntity(entity);
        tr.AddNewlyCreatedDBObject(entity, true);
        return id;
    }

    /// <summary>
    /// Deep-clones a database-resident entity (including owned sub-entities such as
    /// <see cref="AttributeReference"/>s on a <see cref="BlockReference"/> or <see cref="Vertex2d"/>s
    /// on a <see cref="Polyline2d"/>) into <paramref name="targetOwnerId"/> via
    /// <see cref="Database.DeepCloneObjects"/> and returns the clone opened for write.
    /// </summary>
    internal static Entity CloneEntityToOwner(Database db, Transaction tr, Entity source, ObjectId targetOwnerId)
    {
        var ownerId = !targetOwnerId.IsNull && !targetOwnerId.IsErased
                      && tr.GetObject(targetOwnerId, OpenMode.ForRead) is BlockTableRecord
            ? targetOwnerId
            : ((BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead))[BlockTableRecord.ModelSpace];

        if (!source.ObjectId.IsNull)
        {
            var ids = new ObjectIdCollection { source.ObjectId };
            var mapping = new IdMapping();
            db.DeepCloneObjects(ids, ownerId, mapping, false);
            if (mapping.Contains(source.ObjectId))
            {
                var pair = mapping[source.ObjectId];
                if (pair.IsCloned && !pair.Value.IsNull)
                {
                    return (Entity)tr.GetObject(pair.Value, OpenMode.ForWrite);
                }
            }
        }

        var fallback = (Entity)source.Clone();
        AppendToOwner(db, tr, ownerId, fallback);
        return fallback;
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

    internal static double RadiansToDegrees(double radians) => radians * 180.0 / Math.PI;

    internal static ForgeResult? ValidateColorIndex(ForgeCommand command, int? colorIndex)
    {
        if (colorIndex is null)
        {
            return null;
        }

        if (colorIndex is < 0 or > 256)
        {
            return ForgeResult.Failure(command.Id, "invalid_color", "colorIndex must be 0 (ByBlock), 1–255 (ACI), or 256 (ByLayer).");
        }

        return null;
    }

    internal static void ApplyLayerAndColor(Entity entity, string? layer, int? colorIndex)
    {
        if (!string.IsNullOrWhiteSpace(layer))
        {
            entity.Layer = layer!;
        }

        ApplyColor(entity, colorIndex);
    }

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
