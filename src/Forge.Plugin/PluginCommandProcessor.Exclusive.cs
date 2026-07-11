using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.PlottingServices;
using Forge.Shared;

namespace Forge.Plugin;

public sealed partial class PluginCommandProcessor
{
    private static ForgeResult QaPlotFingerprint(ForgeCommand command)
    {
        var fp = CapturePlotFingerprint();
        var findings = fp.EvaluateAgainstPack(StandardsPackStore.Current).ToList();
        var report = QaReport.FromFindings(ActiveDocumentPath(), findings, fp);
        return ForgeResult.Success(command.Id, new { fingerprint = fp, report }, verification: new ForgeVerification
        {
            Attempted = true,
            Passed = report.Passed,
            Message = report.Passed ? "Plot environment fingerprint OK." : "Plot environment fingerprint has blocking findings.",
            ReadBack = report
        });
    }

    private static PlotEnvironmentFingerprint CapturePlotFingerprint()
    {
        int? pstyle = null, bg = null, bgCore = null;
        try { pstyle = Convert.ToInt32(Application.GetSystemVariable("PSTYLEMODE")); } catch { }
        try { bg = Convert.ToInt32(Application.GetSystemVariable("BACKGROUNDPLOT")); } catch { }
        try { bgCore = Convert.ToInt32(Application.GetSystemVariable("BGCOREPUBLISH")); } catch { }

        var pack = StandardsPackStore.Current;
        var devices = new List<string>();
        try
        {
            var deviceList = PlotConfigManager.Devices;
            for (var i = 0; i < Math.Min(deviceList.Count, 32); i++)
            {
                devices.Add(deviceList[i].DeviceName);
            }
        }
        catch
        {
            // Best effort — capabilities tool is richer.
        }

        return new PlotEnvironmentFingerprint
        {
            ProductVersion = ForgeConstants.ProductVersion,
            ProfileHint = Application.GetSystemVariable("LOCALROOTPREFIX")?.ToString(),
            PstyleMode = pstyle,
            BackgroundPlot = bg,
            BgCorePublish = bgCore,
            DeviceNames = devices.ToArray(),
            ExpectedDevice = pack?.PlotDevice,
            ExpectedPaper = pack?.PaperSize,
            ExpectedPlotStyle = pack?.CtbPath ?? pack?.StbPath
        }.WithHash();
    }

    private static ForgeResult QaDependencyClosure(ForgeCommand command)
    {
        var nodes = new List<DependencyNode>();
        foreach (var xref in ReadXrefs())
        {
            var type = xref.GetType();
            var path = type.GetProperty("path")?.GetValue(xref)?.ToString();
            if (!string.IsNullOrWhiteSpace(path))
            {
                nodes.Add(DependencyClosure.FromPath(path, "xref"));
            }
        }

        var pack = StandardsPackStore.Current;
        if (!string.IsNullOrWhiteSpace(pack?.CtbPath))
        {
            nodes.Add(DependencyClosure.FromPath(pack!.CtbPath!, "plotstyle"));
        }

        if (!string.IsNullOrWhiteSpace(pack?.StbPath))
        {
            nodes.Add(DependencyClosure.FromPath(pack!.StbPath!, "plotstyle"));
        }

        var findings = DependencyClosure.Evaluate(nodes);
        var report = QaReport.FromFindings(ActiveDocumentPath(), findings, new { nodes });
        return ForgeResult.Success(command.Id, report, verification: new ForgeVerification
        {
            Attempted = true,
            Passed = report.Passed,
            Message = report.Passed ? "Dependency closure OK." : "Missing dependencies.",
            ReadBack = report
        });
    }

    private static ForgeResult QaDualSource(ForgeCommand command)
    {
        var args = Args<DualSourceArgs>(command);
        var registry = DrawingRegistryStore.Current;
        if (registry is null)
        {
            return ForgeResult.Failure(command.Id, "registry_not_loaded", "Load a drawing registry first.", "forge_registry_load");
        }

        var layout = args.Layout ?? LayoutManager.Current.CurrentLayout;
        var sheet = registry.Sheets.FirstOrDefault(s =>
            s.Layout is not null && s.Layout.Equals(layout, StringComparison.OrdinalIgnoreCase))
            ?? registry.Sheets.FirstOrDefault(s =>
                !string.IsNullOrWhiteSpace(args.DrawingNo)
                && s.DrawingNo.Equals(args.DrawingNo, StringComparison.OrdinalIgnoreCase));

        if (sheet is null)
        {
            return ForgeResult.Failure(command.Id, "registry_sheet_missing", $"No registry sheet for layout '{layout}'.");
        }

        var expected = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["DWG_NO"] = sheet.DrawingNo
        };
        if (!string.IsNullOrWhiteSpace(sheet.Rev))
        {
            expected["REV"] = sheet.Rev!;
        }

        if (!string.IsNullOrWhiteSpace(sheet.Title))
        {
            expected["TITLE"] = sheet.Title!;
        }

        var attrs = FindBlockAttributes(args.TitleblockBlockName, null, forWrite: false)
            .ToDictionary(a => a.Tag, a => a.Value ?? "", StringComparer.OrdinalIgnoreCase);
        var map = StandardsPackStore.Current?.TitleBlockAttrMap;
        var findings = TitleblockDualSource.Compare(expected, attrs, map);
        var report = QaReport.FromFindings(ActiveDocumentPath(), findings, new { layout, expected, live = attrs });
        return ForgeResult.Success(command.Id, report, verification: new ForgeVerification
        {
            Attempted = true,
            Passed = report.Passed,
            Message = report.Passed ? "Dual-source titleblock matches registry." : "Dual-source mismatches.",
            ReadBack = report
        });
    }

    private sealed record DualSourceArgs
    {
        public string? Layout { get; init; }
        public string? DrawingNo { get; init; }
        public string? TitleblockBlockName { get; init; }
    }

    private static ForgeResult QaModalTrap(ForgeCommand command)
    {
        int? fileDia = null, expert = null;
        try { fileDia = Convert.ToInt32(Application.GetSystemVariable("FILEDIA")); } catch { }
        try { expert = Convert.ToInt32(Application.GetSystemVariable("EXPERT")); } catch { }

        var findings = ModalTrapHints.EvaluateAutomationSysvars(fileDia, expert, commandActive: false, modalDialogLikely: false);
        var report = QaReport.FromFindings(ActiveDocumentPath(), findings, new { fileDia, expert });
        return ForgeResult.Success(command.Id, report, verification: new ForgeVerification
        {
            Attempted = true,
            Passed = report.Passed,
            Message = report.Passed ? "No modal trap indicators." : "Modal/automation traps detected.",
            ReadBack = report
        });
    }

    private static ForgeResult XrefClosure(ForgeCommand command)
    {
        var nodes = ReadXrefs().Select(xref =>
        {
            var type = xref.GetType();
            return new
            {
                name = type.GetProperty("name")?.GetValue(xref)?.ToString(),
                path = type.GetProperty("path")?.GetValue(xref)?.ToString(),
                isUnloaded = Equals(type.GetProperty("isUnloaded")?.GetValue(xref), true),
                isOverlay = Equals(type.GetProperty("isOverlay")?.GetValue(xref), true),
                pathExists = Equals(type.GetProperty("pathExists")?.GetValue(xref), true),
                status = type.GetProperty("status")?.GetValue(xref)?.ToString()
            };
        }).ToArray();

        return ForgeResult.Success(command.Id, new
        {
            document = ActiveDocumentPath(),
            nodeCount = nodes.Length,
            nodes,
            note = "Flat host xref closure; nested DWG walk is best-effort via pathExists only."
        });
    }

    private static ForgeResult XrefPinSave(ForgeCommand command)
    {
        if (command.DryRun)
        {
            return DryRun(command, new { nodeCount = ReadXrefs().Length });
        }

        var pin = new XrefClosurePin
        {
            DocumentPath = ActiveDocumentPath(),
            Nodes = ReadXrefs().Select(ToPinNode).ToList()
        };
        var path = XrefClosurePin.TrySave(pin);
        return path is null
            ? ForgeResult.Failure(command.Id, "xref_pin_save_failed", "Could not write xref pin artifact.")
            : ForgeResult.Success(command.Id, new { pinPath = path, pin });
    }

    private static ForgeResult XrefPinVerify(ForgeCommand command)
    {
        var args = Args<PinVerifyArgs>(command);
        if (string.IsNullOrWhiteSpace(args.PinPath) || !File.Exists(args.PinPath))
        {
            return ForgeResult.Failure(command.Id, "xref_pin_missing", "pinPath is required and must exist.");
        }

        var pin = XrefClosurePin.TryLoad(args.PinPath!);
        if (pin is null)
        {
            return ForgeResult.Failure(command.Id, "xref_pin_invalid", "Could not load pin JSON.");
        }

        var current = ReadXrefs().Select(ToPinNode);
        var findings = XrefClosurePin.Compare(current, pin);
        var report = QaReport.FromFindings(ActiveDocumentPath(), findings, new { pin.PinId, args.PinPath });
        return ForgeResult.Success(command.Id, report, verification: new ForgeVerification
        {
            Attempted = true,
            Passed = report.Passed,
            Message = report.Passed ? "Xref pin matches." : "Xref pin mismatch.",
            ReadBack = report
        });
    }

    private sealed record PinVerifyArgs
    {
        public string? PinPath { get; init; }
    }

    private static XrefPinNode ToPinNode(object xref)
    {
        var type = xref.GetType();
        var path = type.GetProperty("path")?.GetValue(xref)?.ToString() ?? "";
        long? length = null;
        DateTimeOffset? mtime = null;
        string? hash = null;
        try
        {
            if (File.Exists(path))
            {
                var info = new FileInfo(path);
                length = info.Length;
                mtime = info.LastWriteTimeUtc;
                hash = DependencyClosure.TryHashFile(path);
            }
        }
        catch
        {
            // Best effort.
        }

        return new XrefPinNode
        {
            Name = type.GetProperty("name")?.GetValue(xref)?.ToString() ?? "",
            Path = path,
            Length = length,
            MtimeUtc = mtime,
            ContentHash = hash
        };
    }

    private static ForgeResult TransmittalSealCreate(ForgeCommand command)
    {
        var args = Args<SealArgs>(command);
        var paths = new List<string>();
        var doc = ActiveDocumentPath();
        if (!string.IsNullOrWhiteSpace(doc))
        {
            paths.Add(doc!);
        }

        foreach (var xref in ReadXrefs())
        {
            var path = xref.GetType().GetProperty("path")?.GetValue(xref)?.ToString();
            if (!string.IsNullOrWhiteSpace(path))
            {
                paths.Add(path!);
            }
        }

        if (!string.IsNullOrWhiteSpace(args.OutputPdfPath))
        {
            paths.Add(args.OutputPdfPath!);
        }

        if (command.DryRun)
        {
            return DryRun(command, new { inputs = paths, args.ReceiptId });
        }

        var seal = TransmittalSeal.Create(paths, args.ReceiptId, args.HmacKey);
        var artifact = TransmittalSeal.TryWrite(seal, args.OutputDirectory);
        return artifact is null
            ? ForgeResult.Failure(command.Id, "seal_write_failed", "Could not write seal.json.")
            : ForgeResult.Success(command.Id, new { seal, verified = TransmittalSeal.Verify(seal, args.HmacKey) });
    }

    private sealed record SealArgs
    {
        public string? OutputPdfPath { get; init; }
        public string? ReceiptId { get; init; }
        public string? HmacKey { get; init; }
        public string? OutputDirectory { get; init; }
    }

    private static ForgeResult PublishCeremonyCheck(ForgeCommand command)
    {
        var args = Args<CeremonyArgs>(command);
        var budget = new BlastRadiusBudget
        {
            MaxSheets = args.MaxSheets ?? 50,
            MaxDestructiveExecs = args.MaxDestructiveExecs ?? 5,
            MaxPathRewrites = args.MaxPathRewrites ?? 100,
            SheetsUsed = args.SheetsUsed ?? 0,
            DestructiveExecsUsed = args.DestructiveExecsUsed ?? 0,
            PathRewritesUsed = args.PathRewritesUsed ?? 0
        };
        var findings = PublishCeremony.Evaluate(
            args.DryRunDone,
            args.PreflightPassed,
            args.IssueAcknowledged,
            args.PublishedStatusAck,
            args.RequirePublishedAck,
            budget);
        var report = QaReport.FromFindings(ActiveDocumentPath(), findings, budget);
        return ForgeResult.Success(command.Id, report, verification: new ForgeVerification
        {
            Attempted = true,
            Passed = report.Passed,
            Message = report.Passed ? "Ceremony OK." : "Ceremony blocked.",
            ReadBack = report
        });
    }

    private sealed record CeremonyArgs
    {
        public bool DryRunDone { get; init; }
        public bool PreflightPassed { get; init; }
        public bool IssueAcknowledged { get; init; }
        public bool PublishedStatusAck { get; init; }
        public bool RequirePublishedAck { get; init; }
        public int? MaxSheets { get; init; }
        public int? MaxDestructiveExecs { get; init; }
        public int? MaxPathRewrites { get; init; }
        public int? SheetsUsed { get; init; }
        public int? DestructiveExecsUsed { get; init; }
        public int? PathRewritesUsed { get; init; }
    }

    private static ForgeResult CdeGateEvaluate(ForgeCommand command)
    {
        var args = Args<CdeArgs>(command);
        var rules = new CdeGateRules
        {
            NamingRegex = args.NamingRegex ?? StandardsPackStore.Current?.DrawingNoRegex,
            RevisionScheme = args.RevisionScheme ?? StandardsPackStore.Current?.RevisionScheme
        };
        if (args.AllowedStatuses is { Length: > 0 })
        {
            rules = new CdeGateRules
            {
                AllowedStatuses = args.AllowedStatuses.ToList(),
                NamingRegex = rules.NamingRegex,
                RevisionScheme = rules.RevisionScheme,
                PublishedRequiresStatuses = args.PublishedRequiresStatuses?.ToList() ?? rules.PublishedRequiresStatuses
            };
        }

        var findings = rules.Evaluate(args.Status, args.Rev, args.DrawingNo, args.TreatingAsIssued);
        string? sidecar = null;
        if (args.WriteSidecar && !string.IsNullOrWhiteSpace(args.SidecarDirectory))
        {
            sidecar = CdeGateRules.WriteSidecar(args.SidecarDirectory!, new
            {
                args.Status,
                args.Rev,
                args.DrawingNo,
                forge = ForgeConstants.ProductVersion,
                findings
            });
        }

        var report = QaReport.FromFindings(ActiveDocumentPath(), findings, new { sidecar });
        return ForgeResult.Success(command.Id, report, verification: new ForgeVerification
        {
            Attempted = true,
            Passed = report.Passed,
            Message = report.Passed ? "CDE gate OK." : "CDE gate blocked.",
            ReadBack = report
        });
    }

    private sealed record CdeArgs
    {
        public string? Status { get; init; }
        public string? Rev { get; init; }
        public string? DrawingNo { get; init; }
        public bool TreatingAsIssued { get; init; }
        public string? NamingRegex { get; init; }
        public string? RevisionScheme { get; init; }
        public string[]? AllowedStatuses { get; init; }
        public string[]? PublishedRequiresStatuses { get; init; }
        public bool WriteSidecar { get; init; }
        public string? SidecarDirectory { get; init; }
    }
}
