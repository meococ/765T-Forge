using System.Text.Json;

namespace Forge.Shared;

/// <summary>
/// Shared forge_linework_* pipeline, independent of where entities came from.
/// The MCP server calls <see cref="Execute"/> for source=dumpFile calls (no AutoCAD needed);
/// <see cref="ExecuteOnEntities"/> is the entry point for a caller that has already extracted
/// entities. One code path = identical results either way.
/// </summary>
public static class LineworkLocal
{
    /// <summary>Where the entities came from + unit context, echoed back in every response.</summary>
    public sealed record SourceInfo(
        string Source,
        string? Document,
        string? DumpPath,
        int Insunits,
        double UnitsPerMeter);

    public static bool IsLineworkTool(string tool)
        => tool.StartsWith("forge_linework_", StringComparison.OrdinalIgnoreCase);

    /// <summary>True when the args request dump-file mode (explicit source, or dumpPath implies it).</summary>
    public static bool IsDumpFileSource(JsonElement args)
    {
        if (args.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        var source = args.TryGetProperty("source", out var s) ? s.GetString() : null;
        if (!string.IsNullOrWhiteSpace(source))
        {
            // Anything other than the live-drawing modes counts as a file source.
            return !string.Equals(source, "drawing", StringComparison.OrdinalIgnoreCase) &&
                   !string.Equals(source, "live", StringComparison.OrdinalIgnoreCase);
        }

        return args.TryGetProperty("dumpPath", out var dp) && !string.IsNullOrWhiteSpace(dp.GetString());
    }

    /// <summary>Full local resolution for the MCP server (dumpFile source only).</summary>
    public static ForgeResult Execute(ForgeCommand command)
    {
        var baseArgs = ForgeJson.ArgsOrDefault<LineworkQueryArgs>(command.Args);
        if (!IsDumpFileSource(command.Args))
        {
            // Fail closed rather than silently reading dumpPath for a caller who asked for the
            // live drawing. ForgeToolRunner routes a live-drawing request to the plugin, so this
            // is only reachable when this resolver is called directly.
            return ForgeResult.Failure(
                command.Id,
                "live_source_unavailable",
                "Live-drawing linework extraction must be dispatched to the AutoCAD plugin. Pass source=dumpFile with dumpPath to resolve without AutoCAD.");
        }

        if (string.IsNullOrWhiteSpace(baseArgs.DumpPath))
        {
            return ForgeResult.Failure(command.Id, "missing_dump_path", "dumpPath is required when source=dumpFile.");
        }

        if (!File.Exists(baseArgs.DumpPath))
        {
            return ForgeResult.Failure(command.Id, "dump_not_found", $"Linework dump not found: {baseArgs.DumpPath}");
        }

        List<LineworkEntity> entities;
        try
        {
            entities = LineworkDumpParser.ParseFile(baseArgs.DumpPath!);
        }
        catch (Exception ex)
        {
            return ForgeResult.Failure(command.Id, "dump_invalid", $"Could not parse linework dump: {ex.Message}");
        }

        var upm = baseArgs.UnitsPerMeter > 0 ? baseArgs.UnitsPerMeter : 1000.0;
        var source = new SourceInfo("dumpFile", null, baseArgs.DumpPath, 0, upm);

        return command.Tool.ToLowerInvariant() switch
        {
            "forge_linework_dump" => ExecuteDump(command, baseArgs, entities, source),
            "forge_linework_trace" => ExecuteTrace(command,
                ForgeJson.ArgsOrDefault<LineworkTraceArgs>(command.Args), entities, source),
            "forge_linework_topology" => ExecuteTopology(command,
                ForgeJson.ArgsOrDefault<LineworkTopologyArgs>(command.Args), entities, source),
            "forge_linework_coverage" => ExecuteCoverage(command,
                ForgeJson.ArgsOrDefault<LineworkCoverageArgs>(command.Args), entities, source),
            "forge_linework_segments" => ExecuteSegments(command,
                ForgeJson.ArgsOrDefault<LineworkSegmentsArgs>(command.Args), entities, source),
            "forge_linework_compare" => ExecuteCompare(command,
                ForgeJson.ArgsOrDefault<LineworkCompareArgs>(command.Args), entities, source),
            _ => ForgeResult.Failure(command.Id, "unknown_tool", $"Unknown linework tool: {command.Tool}")
        };
    }

    /// <summary>Plugin-side entry: entities already extracted (drawing or dumpFile), run the tool pipeline.</summary>
    public static ForgeResult ExecuteOnEntities(
        ForgeCommand command,
        LineworkQueryArgs baseArgs,
        IReadOnlyList<LineworkEntity> entities,
        SourceInfo source)
    {
        return command.Tool.ToLowerInvariant() switch
        {
            "forge_linework_dump" => ExecuteDump(command, baseArgs, entities, source),
            "forge_linework_trace" => ExecuteTrace(command,
                baseArgs as LineworkTraceArgs ?? ForgeJson.ArgsOrDefault<LineworkTraceArgs>(command.Args), entities, source),
            "forge_linework_topology" => ExecuteTopology(command,
                baseArgs as LineworkTopologyArgs ?? ForgeJson.ArgsOrDefault<LineworkTopologyArgs>(command.Args), entities, source),
            "forge_linework_coverage" => ExecuteCoverage(command,
                baseArgs as LineworkCoverageArgs ?? ForgeJson.ArgsOrDefault<LineworkCoverageArgs>(command.Args), entities, source),
            "forge_linework_segments" => ExecuteSegments(command,
                baseArgs as LineworkSegmentsArgs ?? ForgeJson.ArgsOrDefault<LineworkSegmentsArgs>(command.Args), entities, source),
            "forge_linework_compare" => ExecuteCompare(command,
                baseArgs as LineworkCompareArgs ?? ForgeJson.ArgsOrDefault<LineworkCompareArgs>(command.Args), entities, source),
            _ => ForgeResult.Failure(command.Id, "unknown_tool", $"Unknown linework tool: {command.Tool}")
        };
    }

    private static ForgeResult ExecuteDump(ForgeCommand command, LineworkQueryArgs args, IReadOnlyList<LineworkEntity> entities, SourceInfo source)
    {
        var filtered = LineworkQuery.Apply(entities, args);
        var data = new
        {
            source.Source,
            source.Document,
            source.DumpPath,
            source.Insunits,
            source.UnitsPerMeter,
            coordinateUnits = "drawing",
            entityCount = filtered.Entities.Count,
            filtered.Truncated,
            filtered.IncludePatterns,
            filtered.ExcludePatterns,
            layerMatch = filtered.LayerMatch,
            layerSuffix = filtered.SuffixPatterns,
            skipped = filtered.SkippedByKind,
            layerCounts = filtered.Entities.GroupBy(e => e.Layer).ToDictionary(g => g.Key, g => g.Count()),
            entities = filtered.Entities.Select(ToEntityDto).ToArray()
        };

        return WriteArtifactOrSuccess(command, args, data, "linework_dump");
    }

    private static ForgeResult ExecuteTrace(ForgeCommand command, LineworkTraceArgs args, IReadOnlyList<LineworkEntity> entities, SourceInfo source)
    {
        var filtered = LineworkQuery.Apply(entities, args);
        var segs = LineworkFlattener.Flatten(filtered.Entities);

        var useMeters = string.Equals(args.Units, "meters", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(args.Units, "m", StringComparison.OrdinalIgnoreCase);
        var scale = useMeters ? source.UnitsPerMeter : 1.0;
        var result = TraceEngine.Trace(
            filtered.Entities,
            segs,
            args.X * scale,
            args.Y * scale,
            Math.Max(args.Tolerance, 0) * scale,
            Math.Max(args.ContextRadius, 0) * scale,
            args.Handle);

        var data = new
        {
            source.Source,
            source.Document,
            source.DumpPath,
            source.Insunits,
            source.UnitsPerMeter,
            coordinateUnits = "drawing",
            queryUnits = useMeters ? "meters" : "drawing",
            result.Found,
            result.Query,
            entity = result.Entity is null ? null : ToEntityDto(result.Entity),
            result.Hit,
            result.Context,
            result.Message,
            filteredEntityCount = filtered.Entities.Count,
            filtered.IncludePatterns,
            filtered.ExcludePatterns,
            layerMatch = filtered.LayerMatch,
            layerSuffix = filtered.SuffixPatterns,
            filtered.SkippedByKind
        };

        return WriteArtifactOrSuccess(command, args, data, "linework_trace");
    }

    private static ForgeResult ExecuteTopology(ForgeCommand command, LineworkTopologyArgs args, IReadOnlyList<LineworkEntity> entities, SourceInfo source)
    {
        var filtered = LineworkQuery.Apply(entities, args);
        if (filtered.Truncated)
        {
            return ForgeResult.Failure(command.Id, "linework_too_large", filtered.TooLargeMessage ?? "Too many entities.");
        }

        var segs = LineworkFlattener.Flatten(filtered.Entities);
        var report = TopologyBuilder.Build(
            filtered.Entities,
            segs,
            Math.Max(args.VertexTolerance, 1e-6),
            Math.Max(args.JunctionTolerance, 1e-6),
            args.IncludeCrossings);

        var data = new
        {
            source.Source,
            source.Document,
            source.DumpPath,
            source.Insunits,
            source.UnitsPerMeter,
            coordinateUnits = "drawing",
            filteredEntityCount = filtered.Entities.Count,
            filtered.IncludePatterns,
            filtered.ExcludePatterns,
            layerMatch = filtered.LayerMatch,
            layerSuffix = filtered.SuffixPatterns,
            filtered.SkippedByKind,
            graph = report
        };

        return WriteArtifactOrSuccess(command, args, data, "linework_topology");
    }

    private static ForgeResult ExecuteCoverage(ForgeCommand command, LineworkCoverageArgs args, IReadOnlyList<LineworkEntity> entities, SourceInfo source)
    {
        var modelSegs = LoadModelSegments(command, args.ModelSegments, args.ModelSegmentsPath);
        if (modelSegs.Result is not null)
        {
            return modelSegs.Result;
        }

        var filtered = LineworkQuery.Apply(entities, args);
        if (filtered.Truncated)
        {
            return ForgeResult.Failure(command.Id, "linework_too_large", filtered.TooLargeMessage ?? "Too many entities.");
        }

        var segs = LineworkFlattener.Flatten(filtered.Entities);
        var report = CoverageEngine.Compare(
            filtered.Entities,
            segs,
            modelSegs.Segments!.Select(m => (m.X1, m.Y1, m.X2, m.Y2, m.Id)).ToList(),
            source.UnitsPerMeter,
            args.ModelUnitsPerMeter <= 0 ? 1 : args.ModelUnitsPerMeter,
            args.ToleranceMeters <= 0 ? 0.3 : args.ToleranceMeters,
            args.StepMeters <= 0 ? 0.5 : args.StepMeters,
            args.MinCoverageFraction <= 0 ? 0.5 : args.MinCoverageFraction);

        var data = new
        {
            source.Source,
            source.Document,
            source.DumpPath,
            source.Insunits,
            source.UnitsPerMeter,
            modelSource = modelSegs.Source,
            filteredEntityCount = filtered.Entities.Count,
            filtered.IncludePatterns,
            filtered.ExcludePatterns,
            layerMatch = filtered.LayerMatch,
            layerSuffix = filtered.SuffixPatterns,
            filtered.SkippedByKind,
            report
        };

        return WriteArtifactOrSuccess(command, args, data, "linework_coverage");
    }

    private static ForgeResult ExecuteSegments(ForgeCommand command, LineworkSegmentsArgs args, IReadOnlyList<LineworkEntity> entities, SourceInfo source)
    {
        var filtered = LineworkQuery.Apply(entities, args);
        if (filtered.Truncated)
        {
            return ForgeResult.Failure(command.Id, "linework_too_large", filtered.TooLargeMessage ?? "Too many entities.");
        }

        CadTransform? xf = null;
        string? transformSource = null;
        if (args.Transform is not null || !string.IsNullOrWhiteSpace(args.TransformPath))
        {
            if (!TransformResolver.TryResolve(args.Transform, args.TransformPath, out xf, out transformSource, out var err))
            {
                return ForgeResult.Failure(command.Id, "transform_invalid", err ?? "Could not resolve transform.");
            }
        }

        var useMeters = string.Equals(args.Units, "meters", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(args.Units, "m", StringComparison.OrdinalIgnoreCase);
        var scale = useMeters ? 1.0 / Math.Max(source.UnitsPerMeter, 1e-9) : 1.0;
        var segs = LineworkFlattener.Flatten(filtered.Entities);

        var data = new
        {
            source.Source,
            source.Document,
            source.DumpPath,
            source.Insunits,
            source.UnitsPerMeter,
            coordinateUnits = useMeters ? "meters" : "drawing",
            transformApplied = xf is not null,
            transformSource,
            transform = xf,
            segmentCount = segs.Count,
            filtered.Truncated,
            filtered.IncludePatterns,
            filtered.ExcludePatterns,
            layerMatch = filtered.LayerMatch,
            layerSuffix = filtered.SuffixPatterns,
            skipped = filtered.SkippedByKind,
            layerCounts = filtered.Entities.GroupBy(e => e.Layer).ToDictionary(g => g.Key, g => g.Count()),
            segments = segs.Select((s, i) =>
            {
                var e = filtered.Entities[s.Entity];
                var sx = s.X1 * scale;
                var sy = s.Y1 * scale;
                var ex = s.X2 * scale;
                var ey = s.Y2 * scale;
                var lenM = s.Length / Math.Max(source.UnitsPerMeter, 1e-9);
                return new
                {
                    seg = i,
                    entity = e.Id,
                    e.Handle,
                    e.Layer,
                    e.Container,
                    segIndex = s.SegIndex,
                    s = new[] { sx, sy },
                    e = new[] { ex, ey },
                    len = s.Length * scale,
                    lenM = Math.Round(lenM, 4),
                    sT = xf?.Apply(s.X1, s.Y1),
                    eT = xf?.Apply(s.X2, s.Y2)
                };
            }).ToArray()
        };

        return WriteArtifactOrSuccess(command, args, data, "linework_segments");
    }

    private static ForgeResult ExecuteCompare(ForgeCommand command, LineworkCompareArgs args, IReadOnlyList<LineworkEntity> entities, SourceInfo source)
    {
        var modelSegs = LoadModelSegments(command, args.ModelSegments, args.ModelSegmentsPath);
        if (modelSegs.Result is not null)
        {
            return modelSegs.Result;
        }

        CadTransform? xf = null;
        string? transformSource = null;
        if (args.Transform is not null || !string.IsNullOrWhiteSpace(args.TransformPath))
        {
            if (!TransformResolver.TryResolve(args.Transform, args.TransformPath, out xf, out transformSource, out var err))
            {
                return ForgeResult.Failure(command.Id, "transform_invalid", err ?? "Could not resolve transform.");
            }
        }

        var filtered = LineworkQuery.Apply(entities, args);
        if (filtered.Truncated)
        {
            return ForgeResult.Failure(command.Id, "linework_too_large", filtered.TooLargeMessage ?? "Too many entities.");
        }

        var segs = LineworkFlattener.Flatten(filtered.Entities);
        var report = CompareEngine.Compare(
            filtered.Entities,
            segs,
            modelSegs.Segments!,
            xf,
            source.UnitsPerMeter,
            args.ModelUnitsPerMeter <= 0 ? 1 : args.ModelUnitsPerMeter,
            args.ToleranceMeters <= 0 ? 0.3 : args.ToleranceMeters,
            args.StepMeters <= 0 ? 0.5 : args.StepMeters,
            args.MinCoverageFraction <= 0 ? 0.5 : args.MinCoverageFraction,
            args.MinZ,
            args.MaxZ,
            args.SearchRadiusMeters <= 0 ? 50 : args.SearchRadiusMeters);

        string? overlayWritten = null;
        long? overlayBytes = null;
        if (!string.IsNullOrWhiteSpace(args.OverlayPath))
        {
            try
            {
                LineworkSvg.WriteCompareOverlay(
                    args.OverlayPath!,
                    report.CadItems,
                    report.ModelItems,
                    $"forge_linework_compare {source.Source} ({report.CadSegCount} cad / {report.ModelSegCount} revit segs)");
                overlayWritten = Path.GetFullPath(args.OverlayPath);
                overlayBytes = new FileInfo(overlayWritten).Length;
            }
            catch (Exception ex)
            {
                return ForgeResult.Failure(command.Id, "overlay_write_failed", $"Could not write overlayPath: {ex.Message}");
            }
        }

        var data = new
        {
            source.Source,
            source.Document,
            source.DumpPath,
            source.Insunits,
            source.UnitsPerMeter,
            coordinateUnits = "meters",
            transformApplied = xf is not null,
            transformSource,
            transform = xf,
            modelSource = modelSegs.Source,
            overlayPath = overlayWritten,
            overlayBytes,
            filteredEntityCount = filtered.Entities.Count,
            filtered.IncludePatterns,
            filtered.ExcludePatterns,
            layerMatch = filtered.LayerMatch,
            layerSuffix = filtered.SuffixPatterns,
            filtered.SkippedByKind,
            report
        };

        return WriteArtifactOrSuccess(command, args, data, "linework_compare");
    }

    private sealed record ModelSegLoadResult(ForgeResult? Result, List<ModelSeg>? Segments, string? Source);

    private static ModelSegLoadResult LoadModelSegments(ForgeCommand command, PipeSegmentDto[]? inline, string? path)
    {
        var list = new List<ModelSeg>();
        PipeSegmentDto[]? raw = inline;
        var source = "inline";

        if ((raw is null || raw.Length == 0) && !string.IsNullOrWhiteSpace(path))
        {
            if (!File.Exists(path))
            {
                return new ModelSegLoadResult(
                    ForgeResult.Failure(command.Id, "model_segments_not_found", $"Model segments file not found: {path}"),
                    null, null);
            }

            try
            {
                raw = JsonSerializer.Deserialize<PipeSegmentDto[]>(File.ReadAllText(path), ForgeJson.Options);
                source = path;
            }
            catch (Exception ex)
            {
                return new ModelSegLoadResult(
                    ForgeResult.Failure(command.Id, "model_segments_invalid", $"Could not parse model segments JSON: {ex.Message}"),
                    null, null);
            }
        }

        if (raw is not null)
        {
            foreach (var dto in raw)
            {
                if (dto.TryGetSegment(out var x1, out var y1, out var x2, out var y2))
                {
                    dto.TryGetZ(out var z1, out var z2);
                    list.Add(new ModelSeg(x1, y1, x2, y2, z1, z2, dto.Id?.ToString()));
                }
            }
        }

        if (list.Count == 0)
        {
            return new ModelSegLoadResult(
                ForgeResult.Failure(
                    command.Id,
                    "missing_model_segments",
                    "No modeled pipe centerlines supplied. Pass modelSegments[] or modelSegmentsPath (JSON array of {x1,y1[,z1],x2,y2[,z2]} or {s:[x,y,z?],e:[x,y,z?]})."),
                null, null);
        }

        return new ModelSegLoadResult(null, list, source);
    }

    private static object ToEntityDto(LineworkEntity e) => new
    {
        e.Id,
        e.Handle,
        kind = e.Kind.ToString(),
        e.Layer,
        e.Container,
        pts = e.Points.Select(p => new[] { p.X, p.Y }).ToArray(),
        e.Closed,
        e.Radius,
        e.Length,
        e.SegCount
    };

    /// <summary>Write the full payload to outputPath when requested; the response then carries a summary + path.</summary>
    private static ForgeResult WriteArtifactOrSuccess(ForgeCommand command, LineworkQueryArgs args, object data, string artifactName)
    {
        if (string.IsNullOrWhiteSpace(args.OutputPath))
        {
            return ForgeResult.Success(command.Id, data);
        }

        try
        {
            var path = Path.GetFullPath(args.OutputPath);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, JsonSerializer.Serialize(data, ForgeJson.Options));
            return ForgeResult.Success(command.Id, new
            {
                artifact = artifactName,
                outputPath = path,
                bytes = new FileInfo(path).Length,
                note = "Full report written to outputPath; summary only in this response."
            });
        }
        catch (Exception ex)
        {
            return ForgeResult.Failure(command.Id, "artifact_write_failed", $"Could not write outputPath: {ex.Message}");
        }
    }
}
