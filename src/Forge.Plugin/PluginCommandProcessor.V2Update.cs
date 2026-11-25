using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.Colors;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using Forge.Shared;

namespace Forge.Plugin;

// V2 Big Update — Zero-LISP sheet production, viewport twist/scale/lock, layout lifecycle,
// draw order, wipeout masks, raster images (logos/satellite), and civil curve stationing.
public sealed partial class PluginCommandProcessor
{
    // ===================== Viewport Engine v2 =====================

    private static ForgeResult ViewportSet(ForgeCommand command)
    {
        var args = Args<ViewportSetArgs>(command);
        if (string.IsNullOrWhiteSpace(args.Handle))
        {
            return ForgeResult.Failure(command.Id, "missing_handle", "handle is required.");
        }

        Point3d? paperCenter = null;
        if (args.Center is not null)
        {
            try
            {
                paperCenter = ToPoint3d(args.Center);
            }
            catch (ArgumentException ex)
            {
                return ForgeResult.Failure(command.Id, "invalid_args", $"center: {ex.Message}");
            }
        }

        Point3d? viewCenter = null;
        if (args.ViewCenter is not null)
        {
            try
            {
                viewCenter = ToPoint3d(args.ViewCenter);
            }
            catch (ArgumentException ex)
            {
                return ForgeResult.Failure(command.Id, "invalid_args", $"viewCenter: {ex.Message}");
            }
        }

        if (args.Width is <= 0 || args.Height is <= 0)
        {
            return ForgeResult.Failure(command.Id, "invalid_size", "width and height must be > 0 when supplied.");
        }

        if (args.Scale is <= 0)
        {
            return ForgeResult.Failure(command.Id, "invalid_args", "scale must be > 0.");
        }

        if (args.CustomScale is <= 0)
        {
            return ForgeResult.Failure(command.Id, "invalid_args", "customScale must be > 0.");
        }

        var db = ActiveDb;
        var resolved = TryResolveHandle(db, args.Handle);
        if (resolved is null || resolved.Value.IsNull || resolved.Value.IsErased)
        {
            return ForgeResult.Failure(command.Id, "viewport_not_found", $"Viewport handle not found: {args.Handle}");
        }

        using var tr = db.TransactionManager.StartTransaction();
        if (tr.GetObject(resolved.Value, command.DryRun ? OpenMode.ForRead : OpenMode.ForWrite) is not Viewport vp)
        {
            return ForgeResult.Failure(command.Id, "not_a_viewport", $"Handle {args.Handle} is not a viewport.");
        }

        if (ViewportSheetFilter.IsSheetViewport(
                vp.Number, vp.CenterPoint.X, vp.CenterPoint.Y, vp.ViewCenter.X, vp.ViewCenter.Y, vp.Height, vp.ViewHeight))
        {
            return ForgeResult.Failure(
                command.Id,
                "cannot_modify_sheet_viewport",
                $"Handle {args.Handle} (#1) is the root Paper Space sheet camera; pass a floating model viewport handle.");
        }

        var layoutName = ResolveOwnerLayoutName(tr, vp.OwnerId) ?? "";
        if (command.DryRun)
        {
            return DryRun(command, new
            {
                args.Handle,
                current = BuildViewportReadBack(tr, vp, layoutName),
                planned = new
                {
                    args.Center,
                    args.Width,
                    args.Height,
                    args.ViewCenter,
                    args.TwistDeg,
                    args.AlignAngleDeg,
                    args.CustomScale,
                    args.Scale,
                    args.ModelUnits,
                    args.Locked,
                    args.IsOn,
                    args.FreezeLayers,
                    args.ThawLayers,
                    args.ClipBoundaryHandle
                }
            });
        }

        var wasLocked = vp.Locked;
        if (wasLocked)
        {
            vp.Locked = false;
        }

        if (paperCenter is { } pc)
        {
            vp.CenterPoint = pc;
        }

        if (args.Width is { } w)
        {
            vp.Width = w;
        }

        if (args.Height is { } h)
        {
            vp.Height = h;
        }

        if (ApplyViewportCameraAndLayers(
                command,
                db,
                tr,
                vp,
                viewCenter,
                args.TwistDeg,
                args.AlignAngleDeg,
                args.CustomScale,
                args.Scale,
                args.ModelUnits,
                args.Locked ?? wasLocked,
                args.FreezeLayers,
                args.ThawLayers,
                args.ClipBoundaryHandle,
                isCreate: false) is { } vpErr)
        {
            return vpErr;
        }

        if (args.IsOn is { } isOn && string.Equals(LayoutManager.Current.CurrentLayout, layoutName, StringComparison.OrdinalIgnoreCase))
        {
            vp.On = isOn;
        }

        var readBack = BuildViewportReadBack(tr, vp, layoutName);
        tr.Commit();
        return ForgeResult.Success(
            command.Id,
            readBack,
            verification: new ForgeVerification { Attempted = true, Passed = true, ReadBack = readBack });
    }

    private static ForgeResult? ApplyViewportCameraAndLayers(
        ForgeCommand command,
        Database db,
        Transaction tr,
        Viewport vp,
        Point3d? requestedWcsCenter,
        double? twistDeg,
        double? alignAngleDeg,
        double? customScale,
        double? scaleDenom,
        string? modelUnits,
        bool? locked,
        string[]? freezeLayers,
        string[]? thawLayers,
        string? clipBoundaryHandle,
        bool isCreate)
    {
        // Current WCS center before modifying TwistAngle so changing twist/scale alone never shifts the view target.
        var existingWcs = GetViewportWcsCenter(vp);
        var targetX = requestedWcsCenter?.X ?? existingWcs.X;
        var targetY = requestedWcsCenter?.Y ?? existingWcs.Y;

        double? targetTwistDeg = twistDeg ?? (alignAngleDeg.HasValue ? NormalizeDegrees(-alignAngleDeg.Value) : null);
        if (targetTwistDeg.HasValue)
        {
            vp.ViewDirection = Vector3d.ZAxis;
            vp.TwistAngle = DegreesToRadians(targetTwistDeg.Value);
        }

        if (requestedWcsCenter.HasValue || targetTwistDeg.HasValue)
        {
            vp.ViewDirection = Vector3d.ZAxis;
            // Pinning ViewTarget = (targetX, targetY, 0) with ViewCenter = (0, 0) keeps the camera centered on
            // (targetX, targetY) under any TwistAngle without needing _.MSPACE or _.PLAN.
            vp.ViewTarget = new Point3d(targetX, targetY, 0);
            vp.ViewCenter = Point2d.Origin;
        }
        else if (isCreate && requestedWcsCenter.HasValue)
        {
            vp.ViewCenter = new Point2d(targetX, targetY);
        }

        var resolvedScale = ResolveViewportCustomScale(db, customScale, scaleDenom, modelUnits);
        if (resolvedScale is > 0)
        {
            vp.ViewHeight = vp.Height / resolvedScale.Value;
            vp.CustomScale = resolvedScale.Value;
        }

        var layerTable = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);
        if (freezeLayers is { Length: > 0 })
        {
            var ids = new ObjectIdCollection();
            foreach (var raw in freezeLayers)
            {
                if (!string.IsNullOrWhiteSpace(raw) && layerTable.Has(raw.Trim()))
                {
                    ids.Add(layerTable[raw.Trim()]);
                }
            }

            if (ids.Count > 0)
            {
                vp.FreezeLayersInViewport(ids.GetEnumerator());
            }
        }

        if (thawLayers is { Length: > 0 })
        {
            var ids = new ObjectIdCollection();
            foreach (var raw in thawLayers)
            {
                if (!string.IsNullOrWhiteSpace(raw) && layerTable.Has(raw.Trim()))
                {
                    ids.Add(layerTable[raw.Trim()]);
                }
            }

            if (ids.Count > 0)
            {
                vp.ThawLayersInViewport(ids.GetEnumerator());
            }
        }

        if (!string.IsNullOrWhiteSpace(clipBoundaryHandle))
        {
            var clipId = TryResolveHandle(db, clipBoundaryHandle);
            if (clipId is null || clipId.Value.IsNull || clipId.Value.IsErased ||
                tr.GetObject(clipId.Value, OpenMode.ForRead) is not Entity clipEnt ||
                clipEnt.OwnerId != vp.OwnerId)
            {
                return ForgeResult.Failure(
                    command.Id,
                    "clip_boundary_not_found",
                    $"Clip boundary '{clipBoundaryHandle}' must be a closed curve/polyline in the same Paper Space layout as the viewport.");
            }

            var validClipShape = clipEnt switch
            {
                Curve curve => curve.Closed,
                Region or Face => true,
                _ => false
            };
            if (!validClipShape)
            {
                return ForgeResult.Failure(
                    command.Id,
                    "clip_boundary_not_closed",
                    $"Clip boundary '{clipBoundaryHandle}' is {clipEnt.GetType().Name}; use a closed polyline/spline/ellipse/circle, a region, or a face.");
            }

            vp.NonRectClipEntityId = clipId.Value;
            vp.NonRectClipOn = true;
        }

        if (locked.HasValue)
        {
            vp.Locked = locked.Value;
        }

        return null;
    }

    internal static Point2d GetViewportWcsCenter(Viewport vp)
    {
        var tau = vp.TwistAngle;
        var cos = Math.Cos(tau);
        var sin = Math.Sin(tau);
        var dx = vp.ViewCenter.X * cos + vp.ViewCenter.Y * sin;
        var dy = -vp.ViewCenter.X * sin + vp.ViewCenter.Y * cos;
        return new Point2d(vp.ViewTarget.X + dx, vp.ViewTarget.Y + dy);
    }

    internal static object BuildViewportReadBack(Transaction tr, Viewport vp, string layoutName)
    {
        var wcsCenter = GetViewportWcsCenter(vp);
        var scale = vp.CustomScale > 0 && double.IsFinite(vp.CustomScale)
            ? vp.CustomScale
            : (vp.ViewHeight > 0 ? vp.Height / vp.ViewHeight : 1.0);
        var halfW = scale > 0 ? (vp.Width / scale) * 0.5 : 0;
        var halfH = scale > 0 ? (vp.Height / scale) * 0.5 : 0;

        var tau = vp.TwistAngle;
        var cos = Math.Cos(tau);
        var sin = Math.Sin(tau);
        double[][] dcsCorners = [[-halfW, -halfH], [halfW, -halfH], [halfW, halfH], [-halfW, halfH]];
        var worldPolygon = dcsCorners.Select(c => new[]
        {
            Math.Round(wcsCenter.X + c[0] * cos + c[1] * sin, 4),
            Math.Round(wcsCenter.Y - c[0] * sin + c[1] * cos, 4)
        }).ToArray();

        var frozenLayers = new List<string>();
        try
        {
            foreach (ObjectId lid in vp.GetFrozenLayers())
            {
                if (!lid.IsNull && !lid.IsErased && tr.GetObject(lid, OpenMode.ForRead) is LayerTableRecord ltr)
                {
                    frozenLayers.Add(ltr.Name);
                }
            }
            frozenLayers.Sort(StringComparer.OrdinalIgnoreCase);
        }
        catch
        {
            // Keep empty list if viewport has no frozen xdata yet.
        }

        return new
        {
            handle = vp.Handle.Value.ToString("X"),
            number = vp.Number,
            layout = layoutName,
            center = new[] { Math.Round(vp.CenterPoint.X, 4), Math.Round(vp.CenterPoint.Y, 4), Math.Round(vp.CenterPoint.Z, 4) },
            paperCenter = new[] { Math.Round(vp.CenterPoint.X, 4), Math.Round(vp.CenterPoint.Y, 4) },
            width = Math.Round(vp.Width, 4),
            height = Math.Round(vp.Height, 4),
            viewCenterWcs = new[] { Math.Round(wcsCenter.X, 4), Math.Round(wcsCenter.Y, 4) },
            twistDeg = Math.Round(RadiansToDegrees(vp.TwistAngle), 6),
            customScale = vp.CustomScale,
            locked = vp.Locked,
            on = vp.On,
            nonRectClipped = vp.NonRectClipOn,
            frozenLayers,
            worldPolygon
        };
    }

    private static double? ResolveViewportCustomScale(Database db, double? customScale, double? scaleDenom, string? modelUnits)
    {
        if (customScale is > 0)
        {
            return customScale.Value;
        }

        if (scaleDenom is not > 0)
        {
            return null;
        }

        var mode = (modelUnits ?? "").Trim().ToLowerInvariant();
        var factor = mode switch
        {
            "m" or "meter" or "meters" => 1000.0,
            "mm" or "millimeter" or "millimeters" => 1.0,
            "auto" => db.Insunits == UnitsValue.Meters ? 1000.0 : 1.0,
            // Preserve legacy 1.0 / scale behavior when modelUnits is omitted.
            _ => 1.0
        };
        return factor / scaleDenom.Value;
    }

    private static double NormalizeDegrees(double deg)
    {
        var r = deg % 360.0;
        return r < 0 ? r + 360.0 : r;
    }

    private static string? ResolveOwnerLayoutName(Transaction tr, ObjectId ownerId)
    {
        if (!ownerId.IsNull && tr.GetObject(ownerId, OpenMode.ForRead) is BlockTableRecord { IsLayout: true } btr && !btr.LayoutId.IsNull &&
            tr.GetObject(btr.LayoutId, OpenMode.ForRead) is Layout lay)
        {
            return lay.LayoutName;
        }

        return null;
    }

    // ===================== Layout Lifecycle Engine =====================

    private static ForgeResult LayoutManage(ForgeCommand command)
    {
        var args = Args<LayoutManageArgs>(command);
        var action = args.Action?.Trim().ToLowerInvariant() ?? "";
        if (string.IsNullOrWhiteSpace(action))
        {
            return ForgeResult.Failure(command.Id, "missing_action", "action is required (create | rename | clone | delete | reorder | activate).");
        }

        if (RejectControlChars(command, ("name", args.Name), ("newName", args.NewName), ("sourceLayout", args.SourceLayout), ("sourceTemplatePath", args.SourceTemplatePath)) is { } reject)
        {
            return reject;
        }

        var db = ActiveDb;
        switch (action)
        {
            case "create":
            {
                if (string.IsNullOrWhiteSpace(args.Name))
                {
                    return ForgeResult.Failure(command.Id, "missing_layout_name", "name is required for action='create'.");
                }

                using (var checkTr = db.TransactionManager.StartTransaction())
                {
                    var dict = (DBDictionary)checkTr.GetObject(db.LayoutDictionaryId, OpenMode.ForRead);
                    if (dict.Contains(args.Name))
                    {
                        return ForgeResult.Failure(command.Id, "layout_not_unique", $"Layout already exists: {args.Name}");
                    }
                    checkTr.Commit();
                }

                if (command.DryRun)
                {
                    return DryRun(command, new { action, args.Name, args.EraseDefaultViewport });
                }

                ObjectId layoutId;
                try
                {
                    layoutId = LayoutManager.Current.CreateLayout(args.Name);
                }
                catch (System.Exception ex)
                {
                    return ForgeResult.Failure(command.Id, "layout_create_failed", $"CreateLayout failed: {ex.Message}");
                }

                var erasedViewports = 0;
                if (args.EraseDefaultViewport)
                {
                    using var tr = db.TransactionManager.StartTransaction();
                    var lay = (Layout)tr.GetObject(layoutId, OpenMode.ForRead);
                    var btr = (BlockTableRecord)tr.GetObject(lay.BlockTableRecordId, OpenMode.ForWrite);
                    erasedViewports = EraseFloatingViewportsKeepSheetRoot(tr, btr);
                    tr.Commit();
                }

                var payload = new { action, name = args.Name, layoutHandle = layoutId.Handle.ToString(), erasedDefaultViewports = erasedViewports };
                return ForgeResult.Success(command.Id, payload, verification: new ForgeVerification { Attempted = true, Passed = true, ReadBack = payload });
            }

            case "rename":
            {
                if (string.IsNullOrWhiteSpace(args.Name) || string.IsNullOrWhiteSpace(args.NewName))
                {
                    return ForgeResult.Failure(command.Id, "invalid_args", "name and newName are required for action='rename'.");
                }

                if (args.Name.Equals("Model", StringComparison.OrdinalIgnoreCase))
                {
                    return ForgeResult.Failure(command.Id, "cannot_rename_model", "The Model layout cannot be renamed.");
                }

                if (command.DryRun)
                {
                    return DryRun(command, new { action, args.Name, args.NewName });
                }

                try
                {
                    LayoutManager.Current.RenameLayout(args.Name, args.NewName);
                }
                catch (System.Exception ex)
                {
                    return ForgeResult.Failure(command.Id, "layout_rename_failed", $"RenameLayout failed: {ex.Message}");
                }

                var payload = new { action, oldName = args.Name, newName = args.NewName };
                return ForgeResult.Success(command.Id, payload, verification: new ForgeVerification { Attempted = true, Passed = true, ReadBack = payload });
            }

            case "clone":
            case "clone_from_template":
            {
                var srcLayout = args.SourceLayout ?? args.Name;
                if (string.IsNullOrWhiteSpace(srcLayout) || string.IsNullOrWhiteSpace(args.NewName))
                {
                    return ForgeResult.Failure(command.Id, "invalid_args", $"sourceLayout (or name) and newName are required for action='{action}'.");
                }

                if (action == "clone_from_template" && string.IsNullOrWhiteSpace(args.SourceTemplatePath))
                {
                    return ForgeResult.Failure(command.Id, "missing_template_path", "sourceTemplatePath is required for action='clone_from_template'.");
                }

                using (var checkTr = db.TransactionManager.StartTransaction())
                {
                    var dict = (DBDictionary)checkTr.GetObject(db.LayoutDictionaryId, OpenMode.ForRead);
                    if (dict.Contains(args.NewName))
                    {
                        return ForgeResult.Failure(command.Id, "layout_not_unique", $"Target layout already exists: {args.NewName}");
                    }
                    checkTr.Commit();
                }

                if (command.DryRun)
                {
                    return DryRun(command, new { action, sourceLayout = srcLayout, args.NewName, args.SourceTemplatePath });
                }

                if (string.IsNullOrWhiteSpace(args.SourceTemplatePath))
                {
                    try
                    {
                        LayoutManager.Current.CopyLayout(srcLayout, args.NewName);
                    }
                    catch (System.Exception ex)
                    {
                        return ForgeResult.Failure(command.Id, "layout_clone_failed", $"CopyLayout failed: {ex.Message}");
                    }
                }
                else
                {
                    using var proxyScope = ProxyNoticeScope.EnterOrFail(command, out var scopeFail);
                    if (scopeFail is not null)
                    {
                        return scopeFail;
                    }

                    if (CloneLayoutFromExternalTemplate(command, db, args.SourceTemplatePath, srcLayout, args.NewName) is { } cloneErr)
                    {
                        return cloneErr;
                    }
                }

                var payload = new { action, sourceLayout = srcLayout, newName = args.NewName, sourceTemplatePath = args.SourceTemplatePath };
                return ForgeResult.Success(command.Id, payload, verification: new ForgeVerification { Attempted = true, Passed = true, ReadBack = payload });
            }

            case "delete":
            {
                if (string.IsNullOrWhiteSpace(args.Name))
                {
                    return ForgeResult.Failure(command.Id, "missing_layout_name", "name is required for action='delete'.");
                }

                if (args.Name.Equals("Model", StringComparison.OrdinalIgnoreCase))
                {
                    return ForgeResult.Failure(command.Id, "cannot_delete_model", "The Model layout cannot be deleted.");
                }

                if (command.DryRun)
                {
                    return DryRun(command, new { action, args.Name });
                }

                try
                {
                    LayoutManager.Current.DeleteLayout(args.Name);
                }
                catch (System.Exception ex)
                {
                    return ForgeResult.Failure(command.Id, "layout_delete_failed", $"DeleteLayout failed: {ex.Message}");
                }

                var payload = new { action, deleted = args.Name };
                return ForgeResult.Success(command.Id, payload, verification: new ForgeVerification { Attempted = true, Passed = true, ReadBack = payload });
            }

            case "reorder":
            {
                if (command.DryRun)
                {
                    return DryRun(command, new { action, args.Name, args.TabOrder, args.OrderedLayouts });
                }

                using var tr = db.TransactionManager.StartTransaction();
                var dict = (DBDictionary)tr.GetObject(db.LayoutDictionaryId, OpenMode.ForRead);
                var paperLayouts = new List<Layout>();
                foreach (DBDictionaryEntry entry in dict)
                {
                    var lay = (Layout)tr.GetObject(entry.Value, OpenMode.ForWrite);
                    if (!lay.ModelType)
                    {
                        paperLayouts.Add(lay);
                    }
                }

                paperLayouts.Sort((a, b) => a.TabOrder.CompareTo(b.TabOrder));
                if (args.OrderedLayouts is { Length: > 0 })
                {
                    var orderMap = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                    for (var i = 0; i < args.OrderedLayouts.Length; i++)
                    {
                        if (!string.IsNullOrWhiteSpace(args.OrderedLayouts[i]))
                        {
                            orderMap[args.OrderedLayouts[i].Trim()] = i;
                        }
                    }

                    paperLayouts = paperLayouts
                        .OrderBy(l => orderMap.TryGetValue(l.LayoutName, out var r) ? r : int.MaxValue)
                        .ThenBy(l => l.TabOrder)
                        .ToList();
                }
                else if (!string.IsNullOrWhiteSpace(args.Name) && args.TabOrder.HasValue)
                {
                    var target = paperLayouts.FirstOrDefault(l => l.LayoutName.Equals(args.Name, StringComparison.OrdinalIgnoreCase));
                    if (target is null)
                    {
                        return ForgeResult.Failure(command.Id, "layout_not_found", $"Layout not found: {args.Name}");
                    }

                    paperLayouts.Remove(target);
                    var insertAt = Math.Clamp(args.TabOrder.Value - 1, 0, paperLayouts.Count);
                    paperLayouts.Insert(insertAt, target);
                }
                else
                {
                    return ForgeResult.Failure(command.Id, "invalid_args", "Provide orderedLayouts[] or (name + tabOrder) for action='reorder'.");
                }

                var newSequence = new List<object>(paperLayouts.Count);
                for (var i = 0; i < paperLayouts.Count; i++)
                {
                    paperLayouts[i].TabOrder = i + 1;
                    newSequence.Add(new { name = paperLayouts[i].LayoutName, tabOrder = paperLayouts[i].TabOrder });
                }

                tr.Commit();
                var payload = new { action, layouts = newSequence };
                return ForgeResult.Success(command.Id, payload, verification: new ForgeVerification { Attempted = true, Passed = true, ReadBack = payload });
            }

            case "activate":
            {
                if (string.IsNullOrWhiteSpace(args.Name))
                {
                    return ForgeResult.Failure(command.Id, "missing_layout_name", "name is required for action='activate'.");
                }

                if (command.DryRun)
                {
                    return DryRun(command, new { action, args.Name });
                }

                try
                {
                    LayoutManager.Current.CurrentLayout = args.Name;
                }
                catch (System.Exception ex)
                {
                    return ForgeResult.Failure(command.Id, "layout_activate_failed", $"Failed to activate layout '{args.Name}': {ex.Message}");
                }

                var current = LayoutManager.Current.CurrentLayout;
                var payload = new { action, activeLayout = current };
                return ForgeResult.Success(
                    command.Id,
                    payload,
                    verification: new ForgeVerification
                    {
                        Attempted = true,
                        Passed = string.Equals(current, args.Name, StringComparison.OrdinalIgnoreCase),
                        ReadBack = payload
                    });
            }

            default:
                return ForgeResult.Failure(command.Id, "invalid_action", $"Unknown layout action '{args.Action}'. Use create, rename, clone, delete, reorder or activate.");
        }
    }

    private static int EraseFloatingViewportsKeepSheetRoot(Transaction tr, BlockTableRecord paperBtr)
    {
        var viewports = new List<Viewport>();
        foreach (ObjectId id in paperBtr)
        {
            if (tr.GetObject(id, OpenMode.ForWrite) is Viewport vp)
            {
                viewports.Add(vp);
            }
        }

        if (viewports.Count <= 1)
        {
            return 0;
        }

        var erased = 0;
        var keptSheetRoot = false;
        foreach (var vp in viewports)
        {
            if (!keptSheetRoot && ViewportSheetFilter.IsSheetViewport(
                    vp.Number, vp.CenterPoint.X, vp.CenterPoint.Y, vp.ViewCenter.X, vp.ViewCenter.Y, vp.Height, vp.ViewHeight))
            {
                keptSheetRoot = true;
                continue;
            }

            if (!keptSheetRoot && vp == viewports[0])
            {
                keptSheetRoot = true;
                continue;
            }

            vp.Erase();
            erased++;
        }

        return erased;
    }

    private static ForgeResult? CloneLayoutFromExternalTemplate(ForgeCommand command, Database targetDb, string templatePath, string srcLayoutName, string newLayoutName)
    {
        var fullPath = DocumentMatcher.TryGetFullPath(templatePath.Trim());
        if (fullPath is null)
        {
            return ForgeResult.Failure(command.Id, "invalid_path", $"Template path is invalid: {templatePath}");
        }

        if (!File.Exists(fullPath))
        {
            return ForgeResult.Failure(command.Id, "template_not_found", $"Template file not found: {fullPath}");
        }

        ObjectId createdLayoutId;
        try
        {
            createdLayoutId = LayoutManager.Current.CreateLayout(newLayoutName);
        }
        catch (System.Exception ex)
        {
            return ForgeResult.Failure(command.Id, "layout_create_failed", $"CreateLayout failed for '{newLayoutName}': {ex.Message}");
        }

        try
        {
            using var srcDb = new Database(false, true);
            srcDb.ReadDwgFile(fullPath, FileOpenMode.OpenForReadAndAllShare, true, null);
            srcDb.CloseInput(true);

            using var srcTr = srcDb.TransactionManager.StartTransaction();
            var srcDict = (DBDictionary)srcTr.GetObject(srcDb.LayoutDictionaryId, OpenMode.ForRead);
            if (!srcDict.Contains(srcLayoutName))
            {
                LayoutManager.Current.DeleteLayout(newLayoutName);
                return ForgeResult.Failure(command.Id, "source_layout_not_found", $"Layout '{srcLayoutName}' not found in '{fullPath}'.");
            }

            var srcLayout = (Layout)srcTr.GetObject(srcDict.GetAt(srcLayoutName), OpenMode.ForRead);
            var srcBtr = (BlockTableRecord)srcTr.GetObject(srcLayout.BlockTableRecordId, OpenMode.ForRead);

            using var dstTr = targetDb.TransactionManager.StartTransaction();
            var dstLayout = (Layout)dstTr.GetObject(createdLayoutId, OpenMode.ForWrite);
            var dstBtr = (BlockTableRecord)dstTr.GetObject(dstLayout.BlockTableRecordId, OpenMode.ForWrite);

            EraseFloatingViewportsKeepSheetRoot(dstTr, dstBtr);
            dstLayout.CopyFrom(srcLayout);
            dstLayout.LayoutName = newLayoutName;

            var idsToClone = new ObjectIdCollection();
            var skippedRoot = false;
            foreach (ObjectId id in srcBtr)
            {
                if (srcTr.GetObject(id, OpenMode.ForRead) is Viewport vp && !skippedRoot &&
                    ViewportSheetFilter.IsSheetViewport(vp.Number, vp.CenterPoint.X, vp.CenterPoint.Y, vp.ViewCenter.X, vp.ViewCenter.Y, vp.Height, vp.ViewHeight))
                {
                    skippedRoot = true;
                    continue;
                }

                idsToClone.Add(id);
            }

            if (idsToClone.Count > 0)
            {
                using var mapping = new IdMapping();
                srcDb.WblockCloneObjects(idsToClone, dstBtr.ObjectId, mapping, DuplicateRecordCloning.Ignore, false);
            }

            dstTr.Commit();
            srcTr.Commit();
            return null;
        }
        catch (System.Exception ex)
        {
            try
            {
                LayoutManager.Current.DeleteLayout(newLayoutName);
            }
            catch
            {
                // Best effort: report the clone failure even when the placeholder layout cannot be removed.
            }

            return ForgeResult.Failure(command.Id, "layout_clone_failed", $"External layout clone failed: {ex.Message}");
        }
    }

    // ===================== Draw Order & Wipeout Engine =====================

    private static ForgeResult EntityDrawOrder(ForgeCommand command)
    {
        var args = Args<EntityDrawOrderArgs>(command);
        if (CheckHandles(command, args.Handles, "too_many_handles") is { } denied)
        {
            return denied;
        }

        var action = args.Action?.Trim().ToLowerInvariant() ?? "";
        if (action is not ("front" or "back" or "above" or "below"))
        {
            return ForgeResult.Failure(command.Id, "invalid_action", $"Unknown draworder action '{args.Action}'. Use front, back, above or below.");
        }

        if (action is "above" or "below" && string.IsNullOrWhiteSpace(args.TargetHandle))
        {
            return ForgeResult.Failure(command.Id, "missing_target_handle", $"targetHandle is required for action='{action}'.");
        }

        var db = ActiveDb;
        using var tr = db.TransactionManager.StartTransaction();
        ObjectId spaceId;
        try
        {
            spaceId = ResolveTargetSpaceId(db, tr, args.Layout);
        }
        catch (DrawStepException ex)
        {
            return ForgeResult.Failure(command.Id, ex.Code, ex.Message);
        }

        var ids = new ObjectIdCollection();
        var missing = new List<string>();
        foreach (var h in args.Handles!)
        {
            var resolved = TryResolveHandle(db, h);
            if (resolved is null || resolved.Value.IsNull || resolved.Value.IsErased ||
                tr.GetObject(resolved.Value, OpenMode.ForRead) is not Entity ent ||
                ent.OwnerId != spaceId)
            {
                missing.Add(h);
                continue;
            }

            ids.Add(resolved.Value);
        }

        if (ids.Count == 0)
        {
            return ForgeResult.Failure(
                command.Id,
                "handle_not_found",
                $"None of the supplied handles reside in {(string.IsNullOrWhiteSpace(args.Layout) ? "Model" : args.Layout)}.",
                data: new { unknownHandles = missing, handleLocations = DiagnoseHandleLocations(db, tr, missing) });
        }

        var refId = ObjectId.Null;
        if (action is "above" or "below")
        {
            var resolvedRef = TryResolveHandle(db, args.TargetHandle!);
            if (resolvedRef is null || resolvedRef.Value.IsNull || resolvedRef.Value.IsErased ||
                tr.GetObject(resolvedRef.Value, OpenMode.ForRead) is not Entity refEnt ||
                refEnt.OwnerId != spaceId)
            {
                return ForgeResult.Failure(command.Id, "target_handle_not_found", $"targetHandle '{args.TargetHandle}' was not found in the target space.");
            }

            refId = resolvedRef.Value;
        }

        if (command.DryRun)
        {
            return DryRun(command, new { action, layout = args.Layout ?? "Model", count = ids.Count, args.TargetHandle });
        }

        var spaceBtr = (BlockTableRecord)tr.GetObject(spaceId, OpenMode.ForRead);
        var dot = (DrawOrderTable)tr.GetObject(spaceBtr.DrawOrderTableId, OpenMode.ForWrite);
        switch (action)
        {
            case "front":
                dot.MoveToTop(ids);
                break;
            case "back":
                dot.MoveToBottom(ids);
                break;
            case "above":
                dot.MoveAbove(ids, refId);
                break;
            case "below":
                dot.MoveBelow(ids, refId);
                break;
        }

        tr.Commit();
        var payload = new { action, movedCount = ids.Count, skippedHandles = missing, layout = args.Layout ?? "Model" };
        return ForgeResult.Success(command.Id, payload, verification: new ForgeVerification { Attempted = true, Passed = missing.Count == 0, ReadBack = payload });
    }

    private static ForgeResult DrawWipeout(ForgeCommand command)
    {
        var args = Args<DrawWipeoutArgs>(command);
        if (args.Vertices is null || args.Vertices.Length < 3)
        {
            return ForgeResult.Failure(command.Id, "too_few_vertices", "At least 3 [x, y] vertices are required for a Wipeout.");
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
        if (ValidateTargetLayout(command, db, tr, args.Layout) is { } layoutErr)
        {
            return layoutErr;
        }

        if (args.Layer is not null && !LayerExists(db, tr, args.Layer))
        {
            return ForgeResult.Failure(command.Id, "layer_not_found", $"Layer not found: {args.Layer}", "Create the layer first with forge_layer_create.");
        }

        if (command.DryRun)
        {
            return DryRun(command, new { tool = "forge_draw_wipeout", vertexCount = args.Vertices.Length, layout = args.Layout ?? "Model", args.Layer, args.FrameMode, args.SendToBack });
        }

        var pts = new Point2dCollection();
        foreach (var v in args.Vertices)
        {
            pts.Add(new Point2d(v[0], v[1]));
        }

        if (!pts[0].IsEqualTo(pts[^1]))
        {
            pts.Add(pts[0]);
        }

        var wipeout = new Wipeout();
        wipeout.SetDatabaseDefaults(db);
        wipeout.SetFrom(pts, Vector3d.ZAxis);
        if (args.Layer is not null)
        {
            wipeout.Layer = args.Layer;
        }

        var spaceId = ResolveTargetSpaceId(db, tr, args.Layout);
        var space = (BlockTableRecord)tr.GetObject(spaceId, OpenMode.ForWrite);
        space.AppendEntity(wipeout);
        tr.AddNewlyCreatedDBObject(wipeout, true);

        if (args.SendToBack)
        {
            var dot = (DrawOrderTable)tr.GetObject(space.DrawOrderTableId, OpenMode.ForWrite);
            dot.MoveToBottom(new ObjectIdCollection { wipeout.ObjectId });
        }

        if (!string.IsNullOrWhiteSpace(args.FrameMode))
        {
            var frameVal = args.FrameMode.Trim().ToLowerInvariant() switch
            {
                "off" => (short)0,
                "plot" => (short)1,
                "display_only" => (short)2,
                _ => (short)0
            };
            try
            {
                Application.SetSystemVariable("WIPEOUTFRAME", frameVal);
            }
            catch
            {
                // Best effort when sysvar is unavailable.
            }
        }

        var handle = HandleString(wipeout);
        var readBack = EntitySummary(wipeout);
        tr.Commit();
        return ForgeResult.Success(
            command.Id,
            new { handle, layout = args.Layout ?? "Model" },
            verification: new ForgeVerification { Attempted = true, Passed = true, ReadBack = readBack });
    }

    // ===================== Raster Image Engine =====================

    private static ForgeResult ImageList(ForgeCommand command)
    {
        _ = Args<ImageListArgs>(command);
        var db = ActiveDb;
        using var tr = db.TransactionManager.StartTransaction();
        var definitions = new List<object>();
        var dictId = RasterImageDef.GetImageDictionary(db);
        var defNamesById = new Dictionary<ObjectId, string>();

        if (!dictId.IsNull)
        {
            var dict = (DBDictionary)tr.GetObject(dictId, OpenMode.ForRead);
            foreach (DBDictionaryEntry entry in dict)
            {
                if (tr.GetObject(entry.Value, OpenMode.ForRead) is not RasterImageDef def)
                {
                    continue;
                }

                defNamesById[entry.Value] = entry.Key;
                var srcPath = def.SourceFileName ?? "";
                definitions.Add(new
                {
                    name = entry.Key,
                    defHandle = HandleString(def),
                    sourcePath = srcPath,
                    activePath = def.ActiveFileName,
                    isLoaded = def.IsLoaded,
                    pathExists = !string.IsNullOrWhiteSpace(srcPath) && File.Exists(srcPath),
                    pixelSize = def.IsLoaded ? new[] { def.Size.X, def.Size.Y } : null
                });
            }
        }

        var placements = new List<object>();
        var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
        foreach (ObjectId btrId in bt)
        {
            var btr = (BlockTableRecord)tr.GetObject(btrId, OpenMode.ForRead);
            if (btr.IsFromExternalReference || btr.IsFromOverlayReference)
            {
                continue;
            }

            var ownerLabel = btr.IsLayout && !btr.LayoutId.IsNull && tr.GetObject(btr.LayoutId, OpenMode.ForRead) is Layout lay
                ? $"layout:{lay.LayoutName}"
                : $"block:{btr.Name}";

            foreach (ObjectId entId in btr)
            {
                if (tr.GetObject(entId, OpenMode.ForRead) is not RasterImage img)
                {
                    continue;
                }

                defNamesById.TryGetValue(img.ImageDefId, out var defName);
                var cs = img.Orientation;
                placements.Add(new
                {
                    handle = HandleString(img),
                    definitionName = defName,
                    space = ownerLabel,
                    layer = img.Layer,
                    origin = new[] { Math.Round(cs.Origin.X, 4), Math.Round(cs.Origin.Y, 4), Math.Round(cs.Origin.Z, 4) },
                    width = Math.Round(cs.Xaxis.Length, 4),
                    height = Math.Round(cs.Yaxis.Length, 4),
                    fade = img.Fade,
                    showImage = img.ShowImage
                });
            }
        }

        tr.Commit();
        return ForgeResult.Success(command.Id, new { count = definitions.Count, definitions, placements });
    }

    private static ForgeResult ImageAttach(ForgeCommand command)
    {
        var args = Args<ImageAttachArgs>(command);
        if (string.IsNullOrWhiteSpace(args.ImagePath))
        {
            return ForgeResult.Failure(command.Id, "missing_image_path", "imagePath is required.");
        }

        if (RejectControlChars(command, ("imagePath", args.ImagePath), ("name", args.Name), ("layout", args.Layout)) is { } reject)
        {
            return reject;
        }

        var fullPath = DocumentMatcher.TryGetFullPath(args.ImagePath.Trim());
        if (fullPath is null)
        {
            return ForgeResult.Failure(command.Id, "invalid_path", $"Image path is invalid: {args.ImagePath}");
        }

        if (!File.Exists(fullPath))
        {
            return ForgeResult.Failure(command.Id, "image_not_found", $"Image file not found: {fullPath}");
        }

        Point3d origin;
        try
        {
            origin = ToPoint3d(args.Position);
        }
        catch (ArgumentException ex)
        {
            return ForgeResult.Failure(command.Id, "invalid_args", $"position: {ex.Message}");
        }

        if (args.Width is <= 0 || args.Height is <= 0)
        {
            return ForgeResult.Failure(command.Id, "invalid_size", "width and height must be > 0 when provided.");
        }

        var dictKey = string.IsNullOrWhiteSpace(args.Name)
            ? Path.GetFileNameWithoutExtension(fullPath)
            : args.Name.Trim();

        var db = ActiveDb;
        using var tr = db.TransactionManager.StartTransaction();
        if (ValidateTargetLayout(command, db, tr, args.Layout) is { } layoutErr)
        {
            return layoutErr;
        }

        if (args.Layer is not null && !LayerExists(db, tr, args.Layer))
        {
            return ForgeResult.Failure(command.Id, "layer_not_found", $"Layer not found: {args.Layer}", "Create it with forge_layer_create first.");
        }

        if (command.DryRun)
        {
            return DryRun(command, new { dictKey, fullPath, position = PointList(origin), args.Width, args.Height, args.RotationDeg, layout = args.Layout ?? "Model", args.Layer });
        }

        var imgDictId = RasterImageDef.GetImageDictionary(db);
        if (imgDictId.IsNull)
        {
            imgDictId = RasterImageDef.CreateImageDictionary(db);
        }

        var imgDict = (DBDictionary)tr.GetObject(imgDictId, OpenMode.ForWrite);
        RasterImageDef imgDef;
        ObjectId imgDefId;
        try
        {
            if (imgDict.Contains(dictKey))
            {
                imgDefId = imgDict.GetAt(dictKey);
                imgDef = (RasterImageDef)tr.GetObject(imgDefId, OpenMode.ForWrite);
                imgDef.SourceFileName = fullPath;
                imgDef.Load();
            }
            else
            {
                imgDef = new RasterImageDef { SourceFileName = fullPath };
                imgDef.Load();
                imgDefId = imgDict.SetAt(dictKey, imgDef);
                tr.AddNewlyCreatedDBObject(imgDef, true);
            }
        }
        catch (System.Exception ex)
        {
            return ForgeResult.Failure(command.Id, "image_load_failed", $"AutoCAD could not load raster image '{fullPath}': {ex.Message}");
        }

        var pxW = Math.Max(1.0, imgDef.Size.X);
        var pxH = Math.Max(1.0, imgDef.Size.Y);
        var aspect = pxW / pxH;

        double finalW, finalH;
        if (args.Width is { } rw && args.Height is { } rh)
        {
            finalW = rw;
            finalH = rh;
        }
        else if (args.Width is { } onlyW)
        {
            finalW = onlyW;
            finalH = onlyW / aspect;
        }
        else if (args.Height is { } onlyH)
        {
            finalH = onlyH;
            finalW = onlyH * aspect;
        }
        else
        {
            finalW = 100.0;
            finalH = 100.0 / aspect;
        }

        var rad = DegreesToRadians(args.RotationDeg);
        var uVec = new Vector3d(finalW * Math.Cos(rad), finalW * Math.Sin(rad), 0);
        var vVec = new Vector3d(-finalH * Math.Sin(rad), finalH * Math.Cos(rad), 0);

        var raster = new RasterImage();
        raster.SetDatabaseDefaults(db);
        raster.ImageDefId = imgDefId;
        raster.Orientation = new CoordinateSystem3d(origin, uVec, vVec);
        raster.ShowImage = true;
        raster.ImageTransparency = true;
        raster.Fade = (byte)Math.Clamp(args.Fade, 0, 100);
        if (args.Layer is not null)
        {
            raster.Layer = args.Layer;
        }

        var spaceId = ResolveTargetSpaceId(db, tr, args.Layout);
        var space = (BlockTableRecord)tr.GetObject(spaceId, OpenMode.ForWrite);
        space.AppendEntity(raster);
        tr.AddNewlyCreatedDBObject(raster, true);

        RasterImage.EnableReactors(true);
        raster.AssociateRasterDef(imgDef);

        if (args.SendToBack)
        {
            var dot = (DrawOrderTable)tr.GetObject(space.DrawOrderTableId, OpenMode.ForWrite);
            dot.MoveToBottom(new ObjectIdCollection { raster.ObjectId });
        }

        var readBack = new
        {
            handle = HandleString(raster),
            definitionName = dictKey,
            layout = args.Layout ?? "Model",
            width = Math.Round(finalW, 4),
            height = Math.Round(finalH, 4),
            pixelSize = new[] { pxW, pxH },
            isLoaded = imgDef.IsLoaded
        };
        tr.Commit();
        return ForgeResult.Success(
            command.Id,
            readBack,
            verification: new ForgeVerification { Attempted = true, Passed = imgDef.IsLoaded, ReadBack = readBack });
    }

    private static ForgeResult ImageRepath(ForgeCommand command)
    {
        var args = Args<ImageRepathArgs>(command);
        if (string.IsNullOrWhiteSpace(args.NewPath) && string.IsNullOrWhiteSpace(args.SearchDirectory))
        {
            return ForgeResult.Failure(command.Id, "missing_path", "Provide newPath (with name) or searchDirectory.");
        }

        if (!string.IsNullOrWhiteSpace(args.NewPath) && string.IsNullOrWhiteSpace(args.Name))
        {
            return ForgeResult.Failure(command.Id, "missing_name", "name is required when setting an explicit newPath.");
        }

        var db = ActiveDb;
        using var tr = db.TransactionManager.StartTransaction();
        var dictId = RasterImageDef.GetImageDictionary(db);
        if (dictId.IsNull)
        {
            return ForgeResult.Success(command.Id, new { updatedCount = 0, updated = Array.Empty<object>() });
        }

        var dict = (DBDictionary)tr.GetObject(dictId, OpenMode.ForRead);
        var updated = new List<object>();
        foreach (DBDictionaryEntry entry in dict)
        {
            if (!string.IsNullOrWhiteSpace(args.Name) && !entry.Key.Equals(args.Name.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (tr.GetObject(entry.Value, command.DryRun ? OpenMode.ForRead : OpenMode.ForWrite) is not RasterImageDef def)
            {
                continue;
            }

            string? candidate = null;
            if (!string.IsNullOrWhiteSpace(args.NewPath))
            {
                candidate = DocumentMatcher.TryGetFullPath(args.NewPath.Trim());
                if (candidate is null)
                {
                    return ForgeResult.Failure(command.Id, "invalid_path", $"newPath is invalid: {args.NewPath}");
                }
            }
            else if (!string.IsNullOrWhiteSpace(args.SearchDirectory))
            {
                var dir = DocumentMatcher.TryGetFullPath(args.SearchDirectory.Trim());
                if (dir is null)
                {
                    return ForgeResult.Failure(command.Id, "invalid_path", $"searchDirectory is invalid: {args.SearchDirectory}");
                }

                var leaf = Path.GetFileName(def.SourceFileName);
                if (!string.IsNullOrWhiteSpace(leaf))
                {
                    var probe = Path.Combine(dir, leaf);
                    if (File.Exists(probe))
                    {
                        candidate = probe;
                    }
                }

                if (candidate is null && Directory.Exists(dir))
                {
                    foreach (var ext in new[] { ".png", ".jpg", ".jpeg", ".tif", ".tiff", ".bmp" })
                    {
                        var probe = Path.Combine(dir, entry.Key + ext);
                        if (File.Exists(probe))
                        {
                            candidate = probe;
                            break;
                        }
                    }
                }
            }

            if (candidate is null || !File.Exists(candidate))
            {
                continue;
            }

            var previousPath = def.SourceFileName;
            if (!command.DryRun)
            {
                def.SourceFileName = candidate;
                if (args.Reload)
                {
                    def.Load();
                }
            }

            updated.Add(new
            {
                name = entry.Key,
                oldPath = previousPath,
                newPath = candidate,
                isLoaded = def.IsLoaded
            });
        }

        if (command.DryRun)
        {
            return DryRun(command, new { updatedCount = updated.Count, updated });
        }

        tr.Commit();
        var payload = new { updatedCount = updated.Count, updated };
        return ForgeResult.Success(command.Id, payload, verification: new ForgeVerification { Attempted = true, Passed = true, ReadBack = payload });
    }

    // ===================== Civil / Curve Stationing Engine =====================

    private static ForgeResult CurveStation(ForgeCommand command)
    {
        var args = Args<CurveStationArgs>(command);
        if (string.IsNullOrWhiteSpace(args.Handle))
        {
            return ForgeResult.Failure(command.Id, "missing_handle", "handle is required.");
        }

        if ((args.Distances is null || args.Distances.Length == 0) && (args.Points is null || args.Points.Length == 0))
        {
            return ForgeResult.Failure(command.Id, "missing_query", "Provide distances[] and/or points[[x,y],...].");
        }

        var db = ActiveDb;
        using var tr = db.TransactionManager.StartTransaction();
        var entity = TryOpenEntity(db, tr, args.Handle, forWrite: false, out var status);
        if (entity is null)
        {
            return ForgeResult.Failure(command.Id, OpenStatusCode(status), $"Could not open curve handle '{args.Handle}' ({status}).");
        }

        if (entity is not Curve curve)
        {
            return ForgeResult.Failure(command.Id, "not_a_curve", $"Handle '{args.Handle}' is {entity.GetType().Name}, not a Curve.");
        }

        var startDist = curve.GetDistanceAtParameter(curve.StartParam);
        var endDist = curve.GetDistanceAtParameter(curve.EndParam);
        var totalLength = Math.Max(0, endDist - startDist);

        var stations = new List<object>();
        foreach (var station in args.Distances ?? [])
        {
            if (!double.IsFinite(station))
            {
                continue;
            }

            var distAlong = Math.Clamp(station - args.StartStation, 0, totalLength);
            var pt = curve.GetPointAtDist(startDist + distAlong);
            var deriv = curve.GetFirstDerivative(pt);
            var tangentDeg = NormalizeDegrees(RadiansToDegrees(Math.Atan2(deriv.Y, deriv.X)));
            var leftNormalDeg = NormalizeDegrees(tangentDeg + 90.0);

            stations.Add(new
            {
                station = Math.Round(args.StartStation + distAlong, 4),
                distanceAlong = Math.Round(distAlong, 4),
                point = new[] { Math.Round(pt.X, 4), Math.Round(pt.Y, 4), Math.Round(pt.Z, 4) },
                tangentDeg = Math.Round(tangentDeg, 6),
                leftNormalDeg = Math.Round(leftNormalDeg, 6)
            });
        }

        var projections = new List<object>();
        foreach (var rawPt in args.Points ?? [])
        {
            if (rawPt is null || rawPt.Length < 2 || !double.IsFinite(rawPt[0]) || !double.IsFinite(rawPt[1]))
            {
                continue;
            }

            var queryPt = new Point3d(rawPt[0], rawPt[1], rawPt.Length >= 3 && double.IsFinite(rawPt[2]) ? rawPt[2] : 0);
            var closest = curve.GetClosestPointTo(queryPt, extend: false);
            var distAlong = curve.GetDistAtPoint(closest) - startDist;
            var deriv = curve.GetFirstDerivative(closest);

            var dx = queryPt.X - closest.X;
            var dy = queryPt.Y - closest.Y;
            var dist2D = Math.Sqrt(dx * dx + dy * dy);
            var cross = deriv.X * dy - deriv.Y * dx;
            var signedOffset = cross >= 0 ? dist2D : -dist2D;
            var tangentDeg = NormalizeDegrees(RadiansToDegrees(Math.Atan2(deriv.Y, deriv.X)));

            projections.Add(new
            {
                input = new[] { rawPt[0], rawPt[1] },
                station = Math.Round(args.StartStation + distAlong, 4),
                distanceAlong = Math.Round(distAlong, 4),
                closestPoint = new[] { Math.Round(closest.X, 4), Math.Round(closest.Y, 4), Math.Round(closest.Z, 4) },
                signedOffset = Math.Round(signedOffset, 4),
                tangentDeg = Math.Round(tangentDeg, 6)
            });
        }

        tr.Commit();
        return ForgeResult.Success(command.Id, new
        {
            handle = HandleString(curve),
            type = curve.GetType().Name,
            totalLength = Math.Round(totalLength, 4),
            startStation = args.StartStation,
            endStation = Math.Round(args.StartStation + totalLength, 4),
            stations,
            projections
        });
    }

    // ===================== Handle Location Diagnostics =====================

    internal static object[] DiagnoseHandleLocations(Database db, Transaction tr, IEnumerable<string> handles)
    {
        var details = new List<object>();
        foreach (var h in handles.Take(25))
        {
            var id = TryResolveHandle(db, h);
            if (id is null || id.Value.IsNull || !id.Value.IsValid || id.Value.IsErased)
            {
                continue;
            }

            try
            {
                if (tr.GetObject(id.Value, OpenMode.ForRead, false) is Entity ent &&
                    !ent.OwnerId.IsNull &&
                    tr.GetObject(ent.OwnerId, OpenMode.ForRead) is BlockTableRecord ownerBtr)
                {
                    var layoutName = ownerBtr.IsLayout && !ownerBtr.LayoutId.IsNull && tr.GetObject(ownerBtr.LayoutId, OpenMode.ForRead) is Layout lay
                        ? lay.LayoutName
                        : null;
                    details.Add(new
                    {
                        handle = h,
                        type = ent.GetType().Name,
                        layer = ent.Layer,
                        ownerBlock = ownerBtr.Name,
                        ownerLayout = layoutName
                    });
                }
            }
            catch
            {
                // Ignore unreadable diagnostics.
            }
        }

        return details.ToArray();
    }

    // ===================== V2 Args DTOs =====================

    private sealed record ViewportSetArgs
    {
        public string? Handle { get; init; }
        public double[]? Center { get; init; }
        public double? Width { get; init; }
        public double? Height { get; init; }
        public double[]? ViewCenter { get; init; }
        public double? TwistDeg { get; init; }
        public double? AlignAngleDeg { get; init; }
        public double? CustomScale { get; init; }
        public double? Scale { get; init; }
        public string? ModelUnits { get; init; }
        public bool? Locked { get; init; }
        public bool? IsOn { get; init; }
        public string[]? FreezeLayers { get; init; }
        public string[]? ThawLayers { get; init; }
        public string? ClipBoundaryHandle { get; init; }
    }

    private sealed record LayoutManageArgs
    {
        public string? Action { get; init; }
        public string? Name { get; init; }
        public string? NewName { get; init; }
        public string? SourceLayout { get; init; }
        public string? SourceTemplatePath { get; init; }
        public int? TabOrder { get; init; }
        public string[]? OrderedLayouts { get; init; }
        public bool EraseDefaultViewport { get; init; } = true;
    }

    private sealed record EntityDrawOrderArgs
    {
        public string? Layout { get; init; }
        public string? Action { get; init; }
        public string[]? Handles { get; init; }
        public string? TargetHandle { get; init; }
    }

    private sealed record DrawWipeoutArgs
    {
        public string? Layout { get; init; }
        public double[][]? Vertices { get; init; }
        public string? Layer { get; init; }
        public string? FrameMode { get; init; }
        public bool SendToBack { get; init; }
    }

    private sealed record ImageListArgs { }

    private sealed record ImageAttachArgs
    {
        public string? Layout { get; init; }
        public string? Name { get; init; }
        public string? ImagePath { get; init; }
        public double[]? Position { get; init; }
        public double? Width { get; init; }
        public double? Height { get; init; }
        public double RotationDeg { get; init; }
        public string? Layer { get; init; }
        public int Fade { get; init; }
        public bool SendToBack { get; init; }
    }

    private sealed record ImageRepathArgs
    {
        public string? Name { get; init; }
        public string? NewPath { get; init; }
        public string? SearchDirectory { get; init; }
        public bool Reload { get; init; } = true;
    }

    private sealed record CurveStationArgs
    {
        public string? Handle { get; init; }
        public double StartStation { get; init; }
        public double[]? Distances { get; init; }
        public double[][]? Points { get; init; }
    }
}
