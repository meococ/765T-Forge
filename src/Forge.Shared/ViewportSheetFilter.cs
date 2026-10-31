namespace Forge.Shared;

/// <summary>
/// Decides whether a paper-space viewport entity is the layout's own sheet viewport
/// (the viewport that renders the paper itself) rather than a model-space view.
/// DXF-imported drawings lose model-viewport Numbers (they come back as 0), so a
/// bare Number>1 test filters every viewport out. The sheet viewport is the only
/// viewport that looks at the paper it lives on: its ViewCenter equals its Center
/// and its ViewHeight equals its Height.
/// </summary>
public static class ViewportSheetFilter
{
    private const double Tolerance = 1e-4;

    public static bool IsSheetViewport(
        int number,
        double centerX, double centerY,
        double viewCenterX, double viewCenterY,
        double height, double viewHeight)
    {
        // Number 1 is the canonical sheet viewport; any other number still gets the
        // geometry check so a sheet viewport that landed on an odd number (or a DXF
        // that zeroed it) is not misclassified as a model view (F8).
        return number == 1
            || (NearlyEqual(viewCenterX, centerX)
                && NearlyEqual(viewCenterY, centerY)
                && NearlyEqual(viewHeight, height));
    }

    public static bool IsModelViewport(
        int number,
        double centerX, double centerY,
        double viewCenterX, double viewCenterY,
        double height, double viewHeight)
        => !IsSheetViewport(number, centerX, centerY, viewCenterX, viewCenterY, height, viewHeight);

    private static bool NearlyEqual(double a, double b)
        => Math.Abs(a - b) <= Tolerance;
}
