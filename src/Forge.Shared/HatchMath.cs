namespace Forge.Shared;

/// <summary>
/// fix-smoke-20261029 D2: expected first pattern-definition line of a predefined hatch after scale/angle,
/// from a scale-1/angle-0 baseline of the same pattern. Used to read back that AutoCAD applied both.
/// </summary>
public static class HatchMath
{
    public const double AngleTolerance = 1e-6;
    public const double RelativeSpacingTolerance = 1e-6;

    /// <summary>Angle (radians, wrapped to [0, 2π)) and perpendicular line spacing of the first definition.</summary>
    public static (double AngleRad, double Spacing) ExpectedDefinition(double baselineAngleRad, double baselineSpacing, double scale, double angleRad)
        => (WrapAngle(baselineAngleRad + angleRad), Math.Abs(baselineSpacing * scale));

    /// <summary>Perpendicular distance between lines of a definition with direction angle and offset (dx, dy).</summary>
    public static double PerpendicularSpacing(double angleRad, double offsetX, double offsetY)
        => Math.Abs(-Math.Sin(angleRad) * offsetX + Math.Cos(angleRad) * offsetY);

    public static double WrapAngle(double radians)
    {
        var twoPi = 2 * Math.PI;
        var wrapped = radians % twoPi;
        if (wrapped < 0)
        {
            wrapped += twoPi;
        }

        return wrapped >= twoPi - AngleTolerance ? 0 : wrapped;
    }

    /// <summary>Line directions are equal modulo π (a hatch line at 45° is the same line set as one at 225°).</summary>
    public static bool SameLineAngle(double a, double b)
    {
        var d = Math.Abs(WrapAngle(a) - WrapAngle(b)) % Math.PI;
        return d < AngleTolerance || Math.PI - d < AngleTolerance;
    }

    public static bool SameSpacing(double expected, double actual)
        => Math.Abs(expected - actual) <= Math.Max(1e-9, Math.Abs(expected) * RelativeSpacingTolerance);
}
