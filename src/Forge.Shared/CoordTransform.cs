using System.Text.Json;

namespace Forge.Shared;

/// <summary>
/// A calibrated 2D transform between CAD drawing units and model (Revit) space.
/// Stored as a 2x2 matrix + translation so similarity and affine fits share one shape:
///   model = [m11 m12; m21 m22] * cad + [tx ty]
/// In this project the DWG underlay is linked origin_to_origin with millimeter import
/// units, so the usual transform is scale 0.001 (mm->m) with no rotation/translation.
/// </summary>
public sealed record CadTransform
{
    /// <summary>"similarity" (scale+rotation+translation), "affine", or "units_scale" (axis scale only).</summary>
    public string Kind { get; init; } = "similarity";
    public double M11 { get; init; } = 1;
    public double M12 { get; init; }
    public double M21 { get; init; }
    public double M22 { get; init; } = 1;
    public double Tx { get; init; }
    public double Ty { get; init; }

    // ---- calibration metadata (persisted in the calibration file) ----
    public string? Name { get; init; }
    public string? Notes { get; init; }
    /// <summary>Documentation only: CAD units per meter of the source drawing (1000 = mm).</summary>
    public double CadUnitsPerMeter { get; init; } = 1000;
    public int PairCount { get; init; }
    public double? RmsM { get; init; }
    public double? MaxResidualM { get; init; }
    public TransformPairDto[]? Pairs { get; init; }
    public string? CreatedUtc { get; init; }

    /// <summary>Effective uniform scale of the first column (|s| for similarity transforms).</summary>
    public double Scale => Math.Sqrt(M11 * M11 + M21 * M21);

    /// <summary>Effective rotation of the first column in degrees (0 for affine shear).</summary>
    public double RotationDeg => Math.Atan2(M21, M11) * 180.0 / Math.PI;

    public double[] Apply(double x, double y)
        => [M11 * x + M12 * y + Tx, M21 * x + M22 * y + Ty];

    /// <summary>Inverse map (model -> cad). Null when the matrix is singular.</summary>
    public double[]? ApplyInverse(double x, double y)
    {
        var det = M11 * M22 - M12 * M21;
        if (Math.Abs(det) < 1e-15)
        {
            return null;
        }

        var ux = x - Tx;
        var uy = y - Ty;
        return [(M22 * ux - M12 * uy) / det, (-M21 * ux + M11 * uy) / det];
    }

    public static CadTransform FromSimilarity(double scale, double rotationDeg, double tx, double ty)
    {
        var r = rotationDeg * Math.PI / 180.0;
        var c = scale * Math.Cos(r);
        var s = scale * Math.Sin(r);
        return new CadTransform { Kind = "similarity", M11 = c, M12 = -s, M21 = s, M22 = c, Tx = tx, Ty = ty };
    }

    /// <summary>Pure unit conversion (drawing units -> meters), no rotation/translation.</summary>
    public static CadTransform UnitsScale(double unitsPerMeter)
        => new() { Kind = "units_scale", M11 = 1.0 / Math.Max(unitsPerMeter, 1e-12), M22 = 1.0 / Math.Max(unitsPerMeter, 1e-12), CadUnitsPerMeter = unitsPerMeter };

    public void SaveFile(string path)
    {
        var full = Path.GetFullPath(path);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllText(full, JsonSerializer.Serialize(this, ForgeJson.Options));
    }
}

/// <summary>
/// Flexible transform input — an inline tool arg or a hand-authored calibration file.
/// Either supply the matrix (m11..ty) or the similarity shorthand (scale, rotationDeg, tx, ty).
/// A persisted CadTransform deserializes into this spec losslessly (m11 wins over scale).
/// </summary>
public sealed record CadTransformSpec
{
    public string? Kind { get; init; }
    public double? M11 { get; init; }
    public double? M12 { get; init; }
    public double? M21 { get; init; }
    public double? M22 { get; init; }
    public double? Tx { get; init; }
    public double? Ty { get; init; }
    /// <summary>Similarity shorthand: uniform scale (e.g. 0.001 for mm->m).</summary>
    public double? Scale { get; init; }
    /// <summary>Similarity shorthand: rotation in degrees.</summary>
    public double? RotationDeg { get; init; }
    public string? Name { get; init; }
    public string? Notes { get; init; }
    public double? CadUnitsPerMeter { get; init; }
    public int? PairCount { get; init; }
    public double? RmsM { get; init; }
    public double? MaxResidualM { get; init; }
    public TransformPairDto[]? Pairs { get; init; }
    public string? CreatedUtc { get; init; }

    public CadTransform ToTransform()
    {
        CadTransform t;
        if (M11.HasValue || M12.HasValue || M21.HasValue || M22.HasValue)
        {
            t = new CadTransform
            {
                Kind = string.IsNullOrWhiteSpace(Kind) ? "affine" : Kind!,
                M11 = M11 ?? 1,
                M12 = M12 ?? 0,
                M21 = M21 ?? 0,
                M22 = M22 ?? 1,
                Tx = Tx ?? 0,
                Ty = Ty ?? 0
            };
        }
        else
        {
            t = CadTransform.FromSimilarity(Scale ?? 1, RotationDeg ?? 0, Tx ?? 0, Ty ?? 0) with { Kind = string.IsNullOrWhiteSpace(Kind) ? "similarity" : Kind! };
        }

        return t with
        {
            Name = Name,
            Notes = Notes,
            CadUnitsPerMeter = CadUnitsPerMeter ?? 0,
            PairCount = PairCount ?? 0,
            RmsM = RmsM,
            MaxResidualM = MaxResidualM,
            Pairs = Pairs,
            CreatedUtc = CreatedUtc
        };
    }
}

/// <summary>One calibration anchor: a point known in both CAD drawing units and model meters.</summary>
public sealed record TransformPairDto
{
    public double[]? Cad { get; init; }
    public double[]? Revit { get; init; }
    /// <summary>Optional anchor label (e.g. "grid A/1", "PCCC pipe end") for evidence.</summary>
    public string? Label { get; init; }
}

public sealed record LineworkTransformArgs
{
    /// <summary>Calibration anchors [{cad:[x,y], revit:[x,y], label?}]. >=2 for similarity, >=3 for affine.</summary>
    public TransformPairDto[]? Pairs { get; init; }

    /// <summary>Fit a full 6-parameter affine instead of a similarity transform. Needs >=3 pairs.</summary>
    public bool Affine { get; init; }

    /// <summary>Calibration file path: written when calibrating (pairs), read when applying.</summary>
    public string? TransformPath { get; init; }

    /// <summary>Inline transform spec (alternative to transformPath when applying).</summary>
    public CadTransformSpec? Transform { get; init; }

    /// <summary>Points [[x,y],...] to convert with the resolved transform.</summary>
    public double[][]? Points { get; init; }

    /// <summary>Polylines [[[x,y],...],...] to convert.</summary>
    public double[][][]? Polylines { get; init; }

    /// <summary>"cadToRevit" (default) or "revitToCad" (inverse map back to drawing units).</summary>
    public string? Direction { get; init; }

    /// <summary>Calibration name stored in the file (e.g. "aeon-bf-underlay").</summary>
    public string? Name { get; init; }

    /// <summary>Free-form calibration notes (anchor provenance, sheet, level).</summary>
    public string? Notes { get; init; }

    /// <summary>CAD units per meter, persisted as metadata (default 1000 = mm).</summary>
    public double CadUnitsPerMeter { get; init; }
}

/// <summary>Least-squares 2D transform fitting from anchor pairs.</summary>
public static class TransformFitter
{
    public sealed record FitResult(CadTransform Transform, double[] Residuals, double RmsM, double MaxResidualM);

    /// <summary>
    /// Helmert similarity fit (uniform scale + rotation + translation). Exact with 2 pairs,
    /// least-squares with more. Residuals are in the target (revit) coordinate units.
    /// </summary>
    public static FitResult FitSimilarity(IReadOnlyList<(double cx, double cy, double rx, double ry)> pairs)
    {
        if (pairs.Count < 2)
        {
            throw new ArgumentException("Similarity fit needs at least 2 pairs.");
        }

        var n = pairs.Count;
        var cbx = pairs.Average(p => p.cx);
        var cby = pairs.Average(p => p.cy);
        var rbx = pairs.Average(p => p.rx);
        var rby = pairs.Average(p => p.ry);

        double a = 0, b = 0, den = 0;
        foreach (var p in pairs)
        {
            var ux = p.cx - cbx;
            var uy = p.cy - cby;
            var vx = p.rx - rbx;
            var vy = p.ry - rby;
            a += ux * vx + uy * vy;
            b += ux * vy - uy * vx;
            den += ux * ux + uy * uy;
        }

        if (den <= 1e-18)
        {
            throw new ArgumentException("Calibration pairs are degenerate: all CAD points coincide.");
        }

        // w = s*e^{i theta} = (a + i b) / den
        var scale = Math.Sqrt(a * a + b * b) / den;
        var theta = Math.Atan2(b, a);
        var cos = Math.Cos(theta);
        var sin = Math.Sin(theta);
        var tx = rbx - scale * (cos * cbx - sin * cby);
        var ty = rby - scale * (sin * cbx + cos * cby);

        var xf = new CadTransform
        {
            Kind = "similarity",
            M11 = scale * cos,
            M12 = -scale * sin,
            M21 = scale * sin,
            M22 = scale * cos,
            Tx = tx,
            Ty = ty
        };
        return Finish(xf, pairs);
    }

    /// <summary>Full 6-parameter affine fit via normal equations. Least-squares, needs >=3 pairs.</summary>
    public static FitResult FitAffine(IReadOnlyList<(double cx, double cy, double rx, double ry)> pairs)
    {
        if (pairs.Count < 3)
        {
            throw new ArgumentException("Affine fit needs at least 3 pairs.");
        }

        // Solve [x' ; y'] = [m11 m12 tx ; m21 m22 ty] [x y 1] via two 3x3 normal systems.
        double sxx = 0, sxy = 0, syy = 0, sx = 0, sy = 0;
        double sxr = 0, syr = 0, sr = 0, sxs = 0, sys = 0, ss = 0;
        foreach (var p in pairs)
        {
            sxx += p.cx * p.cx;
            sxy += p.cx * p.cy;
            syy += p.cy * p.cy;
            sx += p.cx;
            sy += p.cy;
            sxr += p.cx * p.rx;
            syr += p.cy * p.rx;
            sr += p.rx;
            sxs += p.cx * p.ry;
            sys += p.cy * p.ry;
            ss += p.ry;
        }

        double[,] m =
        {
            { sxx, sxy, sx },
            { sxy, syy, sy },
            { sx, sy, pairs.Count }
        };
        var row1 = Solve3(m, [sxr, syr, sr]);
        var row2 = Solve3(m, [sxs, sys, ss]);

        var xf = new CadTransform
        {
            Kind = "affine",
            M11 = row1[0],
            M12 = row1[1],
            Tx = row1[2],
            M21 = row2[0],
            M22 = row2[1],
            Ty = row2[2]
        };
        return Finish(xf, pairs);
    }

    private static FitResult Finish(CadTransform xf, IReadOnlyList<(double cx, double cy, double rx, double ry)> pairs)
    {
        var residuals = new double[pairs.Count];
        double sum = 0, max = 0;
        for (var i = 0; i < pairs.Count; i++)
        {
            var p = pairs[i];
            var q = xf.Apply(p.cx, p.cy);
            var d = Math.Sqrt((q[0] - p.rx) * (q[0] - p.rx) + (q[1] - p.ry) * (q[1] - p.ry));
            residuals[i] = d;
            sum += d * d;
            max = Math.Max(max, d);
        }

        return new FitResult(xf, residuals, Math.Sqrt(sum / pairs.Count), max);
    }

    private static double[] Solve3(double[,] a, double[] b)
    {
        // Gaussian elimination with partial pivot.
        var m = (double[,])a.Clone();
        var v = (double[])b.Clone();
        for (var col = 0; col < 3; col++)
        {
            var pivot = col;
            for (var r = col + 1; r < 3; r++)
            {
                if (Math.Abs(m[r, col]) > Math.Abs(m[pivot, col]))
                {
                    pivot = r;
                }
            }

            if (Math.Abs(m[pivot, col]) < 1e-15)
            {
                throw new ArgumentException("Calibration pairs are degenerate: cannot solve affine system.");
            }

            if (pivot != col)
            {
                for (var c = 0; c < 3; c++)
                {
                    (m[col, c], m[pivot, c]) = (m[pivot, c], m[col, c]);
                }

                (v[col], v[pivot]) = (v[pivot], v[col]);
            }

            for (var r = col + 1; r < 3; r++)
            {
                var f = m[r, col] / m[col, col];
                for (var c = col; c < 3; c++)
                {
                    m[r, c] -= f * m[col, c];
                }

                v[r] -= f * v[col];
            }
        }

        var x = new double[3];
        for (var r = 2; r >= 0; r--)
        {
            var s = v[r];
            for (var c = r + 1; c < 3; c++)
            {
                s -= m[r, c] * x[c];
            }

            x[r] = s / m[r, r];
        }

        return x;
    }
}

/// <summary>Resolve a transform from an inline spec or a calibration file path (shared by linework tools).</summary>
public static class TransformResolver
{
    public static bool TryResolve(
        CadTransformSpec? spec,
        string? path,
        out CadTransform? transform,
        out string? source,
        out string? error)
    {
        transform = null;
        source = null;
        error = null;
        if (spec is not null)
        {
            transform = spec.ToTransform();
            source = "inline";
            return true;
        }

        if (!string.IsNullOrWhiteSpace(path))
        {
            if (!File.Exists(path))
            {
                error = $"Transform file not found: {path}";
                return false;
            }

            try
            {
                var loaded = JsonSerializer.Deserialize<CadTransformSpec>(File.ReadAllText(path), ForgeJson.Options);
                if (loaded is null)
                {
                    error = $"Transform file is empty or invalid: {path}";
                    return false;
                }

                transform = loaded.ToTransform();
                source = path;
                return true;
            }
            catch (Exception ex)
            {
                error = $"Could not parse transform file {path}: {ex.Message}";
                return false;
            }
        }

        return false;
    }
}

/// <summary>
/// Server-side executor for forge_linework_transform: calibrate (pairs), persist
/// (transformPath), and apply (points/polylines) — no AutoCAD required.
/// </summary>
public static class CoordTransformTool
{
    public static ForgeResult Execute(ForgeCommand command)
    {
        var args = ForgeJson.FromElement<LineworkTransformArgs>(command.Args) ?? new LineworkTransformArgs();
        var inverse = string.Equals(args.Direction, "revitToCad", StringComparison.OrdinalIgnoreCase);

        var pairs = (args.Pairs ?? [])
            .Where(p => p.Cad is { Length: >= 2 } && p.Revit is { Length: >= 2 })
            .Select(p => (cx: p.Cad![0], cy: p.Cad[1], rx: p.Revit![0], ry: p.Revit[1], raw: p))
            .ToList();

        // Fail closed: a silently dropped anchor would produce a fit on fewer pairs than the caller supplied.
        if (args.Pairs is { Length: > 0 } && pairs.Count != args.Pairs.Length)
        {
            return ForgeResult.Failure(command.Id, "invalid_pair",
                $"{args.Pairs.Length - pairs.Count} of {args.Pairs.Length} pairs lack cad/revit [x,y] endpoints — fix or remove them.");
        }

        CadTransform? xf = null;
        TransformFitter.FitResult? fit = null;
        string? transformSource = null;
        var calibrating = pairs.Count > 0;

        if (calibrating)
        {
            if (args.Affine && pairs.Count < 3)
            {
                return ForgeResult.Failure(command.Id, "insufficient_pairs",
                    $"Affine fit needs at least 3 pairs; got {pairs.Count}.",
                    "Pass affine=false for a similarity fit, or add more anchors.");
            }

            if (pairs.Count < 2)
            {
                return ForgeResult.Failure(command.Id, "insufficient_pairs",
                    $"Similarity fit needs at least 2 pairs; got {pairs.Count}.");
            }

            try
            {
                fit = args.Affine
                    ? TransformFitter.FitAffine(pairs.Select(p => (p.cx, p.cy, p.rx, p.ry)).ToArray())
                    : TransformFitter.FitSimilarity(pairs.Select(p => (p.cx, p.cy, p.rx, p.ry)).ToArray());
            }
            catch (ArgumentException ex)
            {
                return ForgeResult.Failure(command.Id, "calibration_failed", ex.Message);
            }

            xf = fit.Transform with
            {
                Name = args.Name,
                Notes = args.Notes,
                CadUnitsPerMeter = args.CadUnitsPerMeter > 0 ? args.CadUnitsPerMeter : 1000,
                PairCount = pairs.Count,
                RmsM = Math.Round(fit.RmsM, 4),
                MaxResidualM = Math.Round(fit.MaxResidualM, 4),
                Pairs = args.Pairs,
                CreatedUtc = DateTime.UtcNow.ToString("O")
            };
            transformSource = "calibrated";

            if (!string.IsNullOrWhiteSpace(args.TransformPath))
            {
                try
                {
                    xf.SaveFile(args.TransformPath!);
                    transformSource = Path.GetFullPath(args.TransformPath);
                }
                catch (Exception ex)
                {
                    return ForgeResult.Failure(command.Id, "transform_write_failed", $"Could not write transformPath: {ex.Message}");
                }
            }
        }
        else
        {
            if (!TransformResolver.TryResolve(args.Transform, args.TransformPath, out xf, out transformSource, out var resolveError))
            {
                if (resolveError is not null)
                {
                    return ForgeResult.Failure(command.Id, "transform_invalid", resolveError);
                }
            }
        }

        var hasGeometry = (args.Points?.Length ?? 0) > 0 || (args.Polylines?.Length ?? 0) > 0;
        if (xf is null)
        {
            return ForgeResult.Failure(command.Id, "missing_transform",
                "Nothing to do: pass pairs[] to calibrate, transform/transformPath to apply, or points/polylines to convert.");
        }

        object? transformedPoints = null;
        object? transformedPolylines = null;
        if (hasGeometry)
        {
            if (inverse && xf.ApplyInverse(0, 0) is null)
            {
                return ForgeResult.Failure(command.Id, "transform_not_invertible", "Transform matrix is singular; cannot map revitToCad.");
            }

            double[] Map(double[] p)
            {
                var q = inverse ? xf.ApplyInverse(p[0], p[1])! : xf.Apply(p[0], p[1]);
                return q;
            }

            transformedPoints = args.Points?.Select(p => p is { Length: >= 2 } ? Map(p) : p).ToArray();
            transformedPolylines = args.Polylines?.Select(pl => pl.Select(p => p is { Length: >= 2 } ? Map(p) : p).ToArray()).ToArray();
        }

        var warning = fit is not null && fit.MaxResidualM > 0.5
            ? $"maxResidualM {fit.MaxResidualM:0.###} exceeds the 0.5 m project convention — verify anchors span multiple areas (N/S/E/W) before trusting this fit."
            : null;

        return ForgeResult.Success(command.Id, new
        {
            action = calibrating ? (hasGeometry ? "calibrate+apply" : "calibrate") : "apply",
            direction = inverse ? "revitToCad" : "cadToRevit",
            transformSource,
            transform = xf,
            transformPath = transformSource is "inline" or "calibrated" ? null : transformSource,
            calibration = fit is null
                ? null
                : new
                {
                    pairCount = pairs.Count,
                    rmsM = Math.Round(fit.RmsM, 4),
                    maxResidualM = Math.Round(fit.MaxResidualM, 4),
                    residuals = pairs.Select((p, i) => new
                    {
                        p.raw.Label,
                        cad = new[] { p.cx, p.cy },
                        revit = new[] { p.rx, p.ry },
                        applied = xf.Apply(p.cx, p.cy),
                        residualM = Math.Round(fit.Residuals[i], 4)
                    }).ToArray()
                },
            transformedPoints,
            transformedPolylines,
            warning
        });
    }
}
