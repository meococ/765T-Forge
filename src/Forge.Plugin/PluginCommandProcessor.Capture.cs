using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using Autodesk.AutoCAD.ApplicationServices;
using Forge.Shared;

namespace Forge.Plugin;

// docs/cad-authoring-tools.md C24 — forge_view_capture. The agent's eyes: PrintWindow renders
// AutoCAD's main window into a bitmap even while the window is in the background (U-C4N's
// screenshot approach); the MCP client reads the PNG to visually verify what was drawn.
// Read-only on the drawing — the only optional mutation is a ZOOM Extents when fit=true.
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

        if (command.DryRun)
        {
            return DryRun(command, new { outputPath, fit = args.Fit, exists = File.Exists(outputPath) });
        }

        string fitNote = "view unchanged";
        if (args.Fit)
        {
            try
            {
                ActiveEditor.Command("_.ZOOM", "_E");
                fitNote = "zoom extents applied before capture";
            }
            catch (System.Exception ex)
            {
                fitNote = $"zoom extents failed ({ex.Message}); captured the current view";
            }
        }

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

            bitmap.Save(outputPath, ImageFormat.Png);
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
                    fitNote,
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
        public bool OverwriteAcknowledged { get; init; }
    }
}
