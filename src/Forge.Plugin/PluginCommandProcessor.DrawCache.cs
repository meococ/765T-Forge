using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using Forge.Shared;

namespace Forge.Plugin;

// Speed wave (2026-10-29): name → ObjectId lookups for forge_draw_batch. Every step used to
// re-open the layer table (twice), the block table and model space by name; inside a batch the
// answers cannot change (steps never create layers, styles or blocks), so they are resolved once.
// Only ids are cached — never open objects — so each nested step transaction still opens (and,
// on failure, rolls back) everything it writes. Outside a batch the cache is inactive and every
// helper falls back to the original per-call lookup.
public sealed partial class PluginCommandProcessor
{
    [ThreadStatic]
    private static DrawIdCache? _drawCache;

    [ThreadStatic]
    private static string? _activeTargetLayout;

    [ThreadStatic]
    private static bool _activeAutoCreateLayers;

    private readonly struct TargetSpaceScope : IDisposable
    {
        private readonly string? _prevLayout;
        private readonly bool _prevAutoCreate;

        public TargetSpaceScope(string? layout, bool autoCreateLayers)
        {
            _prevLayout = _activeTargetLayout;
            _prevAutoCreate = _activeAutoCreateLayers;
            _activeTargetLayout = string.IsNullOrWhiteSpace(layout) ? null : layout.Trim();
            _activeAutoCreateLayers = autoCreateLayers;
        }

        public void Dispose()
        {
            _activeTargetLayout = _prevLayout;
            _activeAutoCreateLayers = _prevAutoCreate;
        }
    }

    private static TargetSpaceScope EnterTargetSpace(string? layout, bool autoCreateLayers = false)
        => new(layout, autoCreateLayers);

    private sealed class DrawIdCache
    {
        public DrawIdCache(Database db) => Db = db;

        public Database Db { get; }
        public ObjectId ModelSpaceId { get; set; } = ObjectId.Null;
        public Dictionary<string, ObjectId> LayoutSpaces { get; } = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, ObjectId> Layers { get; } = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, ObjectId> DimStyles { get; } = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, (ObjectId Id, double FixedHeight)> TextStyles { get; } = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, ObjectId> MLeaderStyles { get; } = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, ObjectId> Blocks { get; } = new(StringComparer.OrdinalIgnoreCase);
    }

    private static DrawIdCache? CacheFor(Database db) => _drawCache is { } cache && ReferenceEquals(cache.Db, db) ? cache : null;

    /// <summary>Layer id by name, or ObjectId.Null when the layer does not exist (auto-creates when _activeAutoCreateLayers is true).</summary>
    private static ObjectId LookupLayerId(Database db, Transaction tr, string layer)
    {
        var cache = CacheFor(db);
        if (cache is not null && cache.Layers.TryGetValue(layer, out var cached) && (!cached.IsNull || !_activeAutoCreateLayers))
        {
            return cached;
        }

        var table = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);
        ObjectId id;
        var createdInCurrentTransaction = false;
        if (table.Has(layer))
        {
            id = table[layer];
        }
        else if (_activeAutoCreateLayers && IsValidBlockName(layer))
        {
            table.UpgradeOpen();
            var ltr = new LayerTableRecord
            {
                Name = layer,
                Color = Autodesk.AutoCAD.Colors.Color.FromColorIndex(Autodesk.AutoCAD.Colors.ColorMethod.ByAci, 7)
            };
            id = table.Add(ltr);
            tr.AddNewlyCreatedDBObject(ltr, true);
            createdInCurrentTransaction = true;
        }
        else
        {
            id = ObjectId.Null;
        }

        // Never cache an id created inside the current step transaction: when that step is
        // rolled back the id dies with it, and a cached zombie id would poison later steps.
        // A successful step makes the layer visible to the next table lookup anyway.
        if (cache is not null && !createdInCurrentTransaction)
        {
            cache.Layers[layer] = id;
        }

        return id;
    }

    /// <summary>Target layout BTR id (ModelSpace when layout is null/'Model', or the named PaperSpace layout BTR).</summary>
    private static ObjectId ModelSpaceIdFor(Database db, Transaction tr)
        => ResolveTargetSpaceId(db, tr, _activeTargetLayout);

    internal static ObjectId ResolveTargetSpaceId(Database db, Transaction tr, string? layout)
    {
        var cache = CacheFor(db);
        if (string.IsNullOrWhiteSpace(layout) || string.Equals(layout.Trim(), "Model", StringComparison.OrdinalIgnoreCase))
        {
            if (cache is not null && !cache.ModelSpaceId.IsNull)
            {
                return cache.ModelSpaceId;
            }

            var blockTable = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
            var id = blockTable[BlockTableRecord.ModelSpace];
            if (cache is not null)
            {
                cache.ModelSpaceId = id;
            }

            return id;
        }

        var trimmed = layout.Trim();
        if (cache is not null && cache.LayoutSpaces.TryGetValue(trimmed, out var cachedSpace))
        {
            return cachedSpace.IsNull
                ? throw new DrawStepException("layout_not_found", $"Layout not found: {trimmed}. Call forge_doc_list_layouts for available tabs.")
                : cachedSpace;
        }

        var layoutDict = (DBDictionary)tr.GetObject(db.LayoutDictionaryId, OpenMode.ForRead);
        var btrId = ObjectId.Null;
        if (layoutDict.Contains(trimmed))
        {
            var lay = (Layout)tr.GetObject(layoutDict.GetAt(trimmed), OpenMode.ForRead);
            btrId = lay.BlockTableRecordId;
        }

        if (cache is not null)
        {
            cache.LayoutSpaces[trimmed] = btrId;
        }

        return btrId.IsNull
            ? throw new DrawStepException("layout_not_found", $"Layout not found: {trimmed}. Call forge_doc_list_layouts for available tabs.")
            : btrId;
    }

    /// <summary>Text style id + fixed height (0 = variable); Null id when no style was requested.</summary>
    private static (ObjectId Id, double FixedHeight) ResolveTextStyle(Database db, Transaction tr, string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return (ObjectId.Null, 0);
        }

        var cache = CacheFor(db);
        if (cache is not null && cache.TextStyles.TryGetValue(name, out var cached))
        {
            return cached.Id.IsNull ? throw TextStyleMissing(name) : cached;
        }

        var table = (TextStyleTable)tr.GetObject(db.TextStyleTableId, OpenMode.ForRead);
        (ObjectId, double) result = (ObjectId.Null, 0);
        if (table.Has(name))
        {
            var record = (TextStyleTableRecord)tr.GetObject(table[name], OpenMode.ForRead);
            result = (record.ObjectId, record.TextSize);
        }

        if (cache is not null)
        {
            cache.TextStyles[name] = result;
        }

        return result.Item1.IsNull ? throw TextStyleMissing(name) : result;
    }

    private static DrawStepException TextStyleMissing(string name) =>
        new("text_style_not_found", $"Text style not found: {name}. Create it with forge_style_text_create or import it with forge_standards_import.");

    /// <summary>Multileader style id, Null when none requested; throws when the named style is missing.</summary>
    private static ObjectId ResolveMLeaderStyle(Database db, Transaction tr, string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return ObjectId.Null;
        }

        var cache = CacheFor(db);
        if (cache is null || !cache.MLeaderStyles.TryGetValue(name, out var id))
        {
            var dictionary = (DBDictionary)tr.GetObject(db.MLeaderStyleDictionaryId, OpenMode.ForRead);
            id = dictionary.Contains(name) ? dictionary.GetAt(name) : ObjectId.Null;
            if (cache is not null)
            {
                cache.MLeaderStyles[name] = id;
            }
        }

        return id.IsNull
            ? throw new DrawStepException("mleader_style_not_found", $"Multileader style not found: {name}. Import it with forge_standards_import.")
            : id;
    }

    /// <summary>Block definition id by name; throws block_not_found.</summary>
    private static ObjectId ResolveBlockId(Database db, Transaction tr, string name)
    {
        var cache = CacheFor(db);
        if (cache is null || !cache.Blocks.TryGetValue(name, out var id))
        {
            var table = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
            id = table.Has(name) ? table[name] : ObjectId.Null;
            if (cache is not null)
            {
                cache.Blocks[name] = id;
            }
        }

        return id.IsNull ? throw new DrawStepException("block_not_found", $"Block not found: {name}") : id;
    }

    /// <summary>Background mask (MASK / "Use background colour") on an MText; scale is the border offset factor (1–5).</summary>
    private static void ApplyBackgroundMask(MText mtext, double? scale)
    {
        mtext.BackgroundFill = true;
        mtext.UseBackgroundColor = true;
        mtext.BackgroundScaleFactor = scale ?? 1.5;
    }

    private static void ValidateMaskScale(double? scale)
    {
        if (scale is { } value && !(value >= 1 && value <= 5))
        {
            throw new DrawStepException("invalid_args", "maskScale must be between 1 and 5 (border offset factor of the background mask).");
        }
    }

    /// <summary>Attribute values on a block-content multileader (the MLeader keeps its own copies).</summary>
    private static string[] SetMLeaderBlockAttributes(Transaction tr, MLeader mleader, ObjectId blockId, IReadOnlyDictionary<string, string>? values)
    {
        var remaining = values is { Count: > 0 }
            ? new Dictionary<string, string>(values, StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var block = (BlockTableRecord)tr.GetObject(blockId, OpenMode.ForRead);
        if (!block.HasAttributeDefinitions)
        {
            return remaining.Keys.ToArray();
        }

        var placement = Matrix3d.Displacement(mleader.BlockPosition - Point3d.Origin);
        foreach (ObjectId id in block)
        {
            if (tr.GetObject(id, OpenMode.ForRead) is not AttributeDefinition definition || definition.Constant)
            {
                continue;
            }

            using var reference = new AttributeReference();
            reference.SetAttributeFromBlock(definition, placement);
            if (remaining.Remove(definition.Tag, out var value))
            {
                reference.TextString = value;
            }

            mleader.SetBlockAttribute(id, reference);
        }

        return remaining.Keys.ToArray();
    }
}
