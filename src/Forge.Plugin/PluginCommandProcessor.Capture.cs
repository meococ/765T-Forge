using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using Forge.Shared;

namespace Forge.Plugin;

// docs/cad-authoring-tools.md C24 — forge_view_capture. The agent's eyes: PrintWindow renders
// AutoCAD's main window into a bitmap even while the window is in the background (U-C4N's
// screenshot approach); the MCP client reads the PNG to visually verify what was drawn.
// Read-only on the drawing — when fit=true, adjusts the active ViewTableRecord via SetCurrentView
// (never invokes _.ZOOM, so AutoCAD's undo stack is untouched).
public sealed partial class PluginCommandProcessor
{
    // PW_RENDERFULLCONTENT: DWM-composited windows render black or stale frames without it.
    private const uint PwRenderFullContent = 0x2;

    private static ForgeResult ViewCapture(ForgeCommand command)
    {
        var args = Args<ViewCaptureArgs>(command);
        if (string.IsNullOrWhiteSpace(args.OutputPath))
        {
            return ForgeResult.Failure(command.Id, "missing_output_path", "outputPath is required.");
        }

        if (RejectControlChars(command, ("outputPath", args.OutputPath)) is { } reject)
        {
            return reject;
        }

        var outputPath = Path.GetFullPath(args.OutputPath);
        if (File.Exists(outputPath) && !args.OverwriteAcknowledged)
        {
            return ForgeResult.Failure(
                command.Id,
                "capture_overwrite_not_acknowledged",
                $"Refusing to overwrite existing capture: {outputPath}",
                "Pass overwriteAcknowledged=true after confirming the previous capture is disposable.");
        }

        double[]? window = null;
        if (args.Window is { Length: > 0 })
        {
            if (!PlotLayoutMath.TryNormalizeWindow(args.Window, null, out var box, out var windowError))
            {
                return ForgeResult.Failure(command.Id, "invalid_window", windowError ?? "window is invalid.", "Pass window=[[x1,y1],[x2,y2]] in world coordinates of the current space.");
            }

            window = box;
        }

        var padding = Math.Clamp(args.Padding ?? 0.05, 0, 1);
        if (command.DryRun)
        {
            return DryRun(command, new { outputPath, fit = args.Fit, window, padding, cropToDrawing = args.CropToDrawing, exists = File.Exists(outputPath) });
        }

        string fitNote = "view unchanged";
        object? appliedView = null;
        if (window is not null)
        {
            try
            {
                appliedView = ZoomCurrentViewToBox(window[0], window[1], 0, window[2], window[3], 0, padding);
                fitNote = "zoom window applied via SetCurrentView before capture";
            }
            catch (System.Exception ex)
            {
                return ForgeResult.Failure(command.Id, "zoom_window_failed", $"Zoom to window failed: {ex.Message}", "Nothing was captured; retry without window or check the coordinates.");
            }
        }
        else if (args.Fit)
        {
            try
            {
                appliedView = ZoomCurrentViewToExtents();
                fitNote = appliedView is null ? "drawing extents are empty; captured the current view" : "zoom extents applied via SetCurrentView before capture";
            }
            catch (System.Exception ex)
            {
                fitNote = $"zoom extents failed ({ex.Message}); captured the current view";
            }
        }

        appliedView ??= DescribeCurrentView();

        IntPtr hwnd;
        try
        {
            hwnd = Application.MainWindow.Handle;
        }
        catch (System.Exception ex)
        {
            return ForgeResult.Failure(command.Id, "window_not_found", $"AutoCAD main window is unavailable: {ex.Message}");
        }

        if (hwnd == IntPtr.Zero || !GetWindowRect(hwnd, out var rect) || rect.Right - rect.Left <= 0 || rect.Bottom - rect.Top <= 0)
        {
            return ForgeResult.Failure(command.Id, "capture_failed", "Could not read the AutoCAD window rectangle.");
        }

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
            var width = rect.Right - rect.Left;
            var height = rect.Bottom - rect.Top;
            using var bitmap = new Bitmap(width, height, PixelFormat.Format32bppArgb);
            using var graphics = Graphics.FromImage(bitmap);
            var hdc = graphics.GetHdc();
            try
            {
                if (!PrintWindow(hwnd, hdc, PwRenderFullContent))
                {
                    return ForgeResult.Failure(
                        command.Id,
                        "capture_failed",
                        "PrintWindow returned false.",
                        "The window may be minimized — restore AutoCAD and retry, or try without fit.");
                }
            }
            finally
            {
                graphics.ReleaseHdc(hdc);
            }

            var cropNote = (string?)null;
            var saved = bitmap;
            Bitmap? cropped = null;
            if (args.CropToDrawing)
            {
                cropped = TryCropToDrawingWindow(bitmap, rect, out cropNote);
                if (cropped is not null)
                {
                    saved = cropped;
                }
            }

            using (cropped)
            {
                saved.Save(outputPath, ImageFormat.Png);
                width = saved.Width;
                height = saved.Height;
            }

            var bytes = new FileInfo(outputPath).Length;
            return ForgeResult.Success(
                command.Id,
                new
                {
                    outputPath,
                    width,
                    height,
                    bytes,
                    fit = args.Fit,
                    window,
                    fitNote,
                    view = appliedView,
                    cropToDrawing = args.CropToDrawing,
                    cropNote,
                    hint = "Read the PNG file to visually inspect the drawing."
                },
                verification: new ForgeVerification
                {
                    Attempted = true,
                    Passed = bytes > 0,
                    Message = "PNG written via PrintWindow.",
                    ReadBack = new { outputPath, bytes }
                });
        }
        catch (System.Exception ex)
        {
            return ForgeResult.Failure(
                command.Id,
                "capture_failed",
                $"Capture failed: {ex.Message}",
                "Check the output directory is writable.");
        }
    }

    /// <summary>Zooms the current view to the drawing extents. Returns the applied view, or null when the extents are empty.</summary>
    private static object? ZoomCurrentViewToExtents()
    {
        var db = ActiveDb;
        db.UpdateExt(false);
        var inPaperSpace = !db.TileMode && db.PaperSpaceVportId == ActiveEditor.CurrentViewportObjectId;
        var min = inPaperSpace ? db.Pextmin : db.Extmin;
        var max = inPaperSpace ? db.Pextmax : db.Extmax;
        if (max.X < min.X || max.Y < min.Y || double.IsInfinity(min.X) || double.IsInfinity(max.X))
        {
            return null;
        }

        return ZoomCurrentViewToBox(min.X, min.Y, min.Z, max.X, max.Y, max.Z, 0.05);
    }

    /// <summary>
    /// Sets the current view so the world box (plus <paramref name="padding"/> on each axis) fills the
    /// viewport, keeping the viewport aspect. WCS→DCS uses the canonical transform
    /// (inverse of Rotation(-twist) · Displacement(target) · PlaneToWorld(viewDirection)); all eight
    /// box corners are projected so rotated/twisted views still frame the whole box.
    /// Never runs _.ZOOM, so the undo stack is untouched.
    /// </summary>
    private static object ZoomCurrentViewToBox(double minX, double minY, double minZ, double maxX, double maxY, double maxZ, double padding)
    {
        var editor = ActiveEditor;
        using var view = editor.GetCurrentView();
        var aspect = view.Height > 1e-9 && view.Width > 1e-9 ? view.Width / view.Height : 1.5;
        var dcsToWcs = Matrix3d.Rotation(-view.ViewTwist, view.ViewDirection, view.Target)
                       * Matrix3d.Displacement(view.Target - Point3d.Origin)
                       * Matrix3d.PlaneToWorld(view.ViewDirection);
        var wcsToDcs = dcsToWcs.Inverse();

        double dMinX = double.PositiveInfinity, dMinY = double.PositiveInfinity, dMaxX = double.NegativeInfinity, dMaxY = double.NegativeInfinity;
        foreach (var x in new[] { minX, maxX })
        {
            foreach (var y in new[] { minY, maxY })
            {
                foreach (var z in new[] { minZ, maxZ })
                {
                    var p = new Point3d(x, y, z).TransformBy(wcsToDcs);
                    dMinX = Math.Min(dMinX, p.X);
                    dMinY = Math.Min(dMinY, p.Y);
                    dMaxX = Math.Max(dMaxX, p.X);
                    dMaxY = Math.Max(dMaxY, p.Y);
                }
            }
        }

        var boxW = Math.Max((dMaxX - dMinX) * (1 + 2 * padding), 1e-6);
        var boxH = Math.Max((dMaxY - dMinY) * (1 + 2 * padding), 1e-6);
        view.CenterPoint = new Point2d(0.5 * (dMinX + dMaxX), 0.5 * (dMinY + dMaxY));
        view.Height = Math.Max(boxH, boxW / Math.Max(aspect, 1e-6));
        view.Width = view.Height * aspect;
        editor.SetCurrentView(view);
        editor.UpdateScreen();
        return DescribeView(view, dcsToWcs);
    }

    private static object? DescribeCurrentView()
    {
        try
        {
            using var view = ActiveEditor.GetCurrentView();
            var dcsToWcs = Matrix3d.Rotation(-view.ViewTwist, view.ViewDirection, view.Target)
                           * Matrix3d.Displacement(view.Target - Point3d.Origin)
                           * Matrix3d.PlaneToWorld(view.ViewDirection);
            return DescribeView(view, dcsToWcs);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Read-back of a view: DCS center/size and the world box it shows (exact for plan views).</summary>
    private static object DescribeView(ViewTableRecord view, Matrix3d dcsToWcs)
    {
        var c = view.CenterPoint;
        var hw = view.Width / 2;
        var hh = view.Height / 2;
        double wMinX = double.PositiveInfinity, wMinY = double.PositiveInfinity, wMaxX = double.NegativeInfinity, wMaxY = double.NegativeInfinity;
        foreach (var (dx, dy) in new[] { (-hw, -hh), (hw, -hh), (hw, hh), (-hw, hh) })
        {
            var p = new Point3d(c.X + dx, c.Y + dy, 0).TransformBy(dcsToWcs);
            wMinX = Math.Min(wMinX, p.X);
            wMinY = Math.Min(wMinY, p.Y);
            wMaxX = Math.Max(wMaxX, p.X);
            wMaxY = Math.Max(wMaxY, p.Y);
        }

        var centerWorld = new Point3d(c.X, c.Y, 0).TransformBy(dcsToWcs);
        return new
        {
            centerDcs = new[] { c.X, c.Y },
            centerWorld = new[] { centerWorld.X, centerWorld.Y, centerWorld.Z },
            width = view.Width,
            height = view.Height,
            worldBox = new[] { wMinX, wMinY, wMaxX, wMaxY },
            viewTwistDeg = view.ViewTwist * 180.0 / Math.PI,
            planView = view.ViewDirection.IsParallelTo(Vector3d.ZAxis)
        };
    }

    /// <summary>Crops a main-window capture to the active drawing's document window (drops ribbon, palettes, command line).</summary>
    private static Bitmap? TryCropToDrawingWindow(Bitmap full, RECT mainRect, out string note)
    {
        try
        {
            var docHwnd = ActiveDoc.Window?.Handle ?? IntPtr.Zero;
            if (docHwnd == IntPtr.Zero || !GetWindowRect(docHwnd, out var docRect))
            {
                note = "document window not found; kept the full AutoCAD window";
                return null;
            }

            var x = Math.Max(0, docRect.Left - mainRect.Left);
            var y = Math.Max(0, docRect.Top - mainRect.Top);
            var w = Math.Min(full.Width - x, docRect.Right - docRect.Left);
            var h = Math.Min(full.Height - y, docRect.Bottom - docRect.Top);
            if (w <= 16 || h <= 16)
            {
                note = "document window rectangle is degenerate; kept the full AutoCAD window";
                return null;
            }

            note = "cropped to the active drawing window";
            return full.Clone(new Rectangle(x, y, w, h), full.PixelFormat);
        }
        catch (System.Exception ex)
        {
            note = $"crop failed ({ex.Message}); kept the full AutoCAD window";
            return null;
        }
    }

    [DllImport("user32.dll")]
    private static extern bool PrintWindow(IntPtr hWnd, IntPtr hdcBlt, uint nFlags);

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

    private struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    private sealed record ViewCaptureArgs
    {
        public string? OutputPath { get; init; }
        public bool Fit { get; init; }
        public double[][]? Window { get; init; }
        public double? Padding { get; init; }
        public bool CropToDrawing { get; init; }
        public bool OverwriteAcknowledged { get; init; }
    }
}
