# NOTICE

## Autodesk trademarks and APIs

765T-Forge is an independent open-source project. It is **not affiliated with, endorsed by, or sponsored by Autodesk, Inc.**

AutoCAD®, ObjectARX®, Autodesk®, and related names are trademarks or registered trademarks of Autodesk, Inc. in the United States and/or other countries. All other trademarks are the property of their respective owners.

## No redistribution of Autodesk binaries

This repository does **not** redistribute Autodesk proprietary assemblies (for example `AcCoreMgd.dll`, `AcDbMgd.dll`, `AcMgd.dll`, or other AutoCAD runtime components). Plugin projects reference those assemblies from a local AutoCAD installation via `AUTOCAD_2026_ROOT` (or the default install path).

Do not commit, package, or publish Autodesk DLLs with Forge releases.

## Licensed AutoCAD required

To run the AutoCAD plugin and live MCP tools that talk to a drawing session, you need a **licensed installation of AutoCAD 2025 or 2026** (or a compatible Autodesk product that provides the referenced APIs) on Windows.

The MCP server (`Forge.Server`) and unit tests can build and run without AutoCAD when using `765T-Forge.ServerOnly.slnf`. Headless script execution via AccoreConsole also requires a local AutoCAD install that provides `accoreconsole.exe`.

## Upstream and prior art

Early product planning referenced community AutoCAD MCP / plugin experiments for architectural comparison, including:

- [moisesbritez92/autocad-2026](https://github.com/moisesbritez92/autocad-2026) — AutoCAD 2026 .NET plugin + MCP bridge patterns (named pipe / NETLOAD)
- [puran-water/autocad-mcp](https://github.com/puran-water/autocad-mcp) — AutoCAD LT / AutoLISP MCP patterns and agent skill practices

**765T-Forge’s shipped code is an independent all-C# implementation** (Forge.Server + Forge.Plugin + Forge.Shared). It is not a verbatim fork of those repositories. Patterns such as in-process AutoCAD plugins, named-pipe bridges, and AccoreConsole headless runs are common in the AutoCAD automation ecosystem.

If you believe a specific file retains third-party copyrighted material that should be attributed differently, open a private report per [SECURITY.md](SECURITY.md).

Historical planning notes (Vietnamese, aspirational — not the shipped tool list): [docs/history/build-brief.md](docs/history/build-brief.md).

## References consulted (no code copied)

The fix-smoke-20261029 wave (D1–D7) checked its API call sequences against these permissively licensed projects. **No code was copied from any of them**; each fix re-implements a documented AutoCAD API sequence in Forge's own code (`artifacts/fix-smoke-20261029/FIX-PLAN.md`, section R).

- [luanshixia/AutoCADCodePack](https://github.com/luanshixia/AutoCADCodePack) (MIT) — hatch pattern before pattern angle (D2)
- InspireFunction/IFoxCAD (MIT) — hatch build order (D2)
- ADN MgdDbg (MIT) — hatch properties on a non-resident hatch (D2; older-release behaviour, not adopted)
- [HorizunGroup/horizun-civil3d-mcp](https://github.com/HorizunGroup/horizun-civil3d-mcp) (Apache-2.0) — undo attribution in command context (D3)
- U-C4N/Autocad-MCP (MIT) and [puran-water/autocad-mcp](https://github.com/puran-water/autocad-mcp) (MIT) — unverified undo, as a pattern to avoid (D3)
- gile, Gile.AutoCAD.Extension (MIT) — `UpdateExt` before reading the extents (D5)
- [UglyToad/PdfPig](https://github.com/UglyToad/PdfPig) (Apache-2.0) — PDF ink box as the union of painted paths, glyph boxes and images (D5)
- [mozman/ezdxf](https://github.com/mozman/ezdxf) (MIT) — an ATTRIB on layer 0 takes its INSERT's resolved layer (D6)

Autodesk documentation, ADN and Through-the-Interface blog posts and forum threads were read for the documented call sequences only (ideas, not code).

## Third-party notices

Forge depends on open-source packages declared in `Directory.Packages.props` (including the Model Context Protocol C# SDK). See each package’s own license for terms.

- **pypdf** (BSD-3-Clause, requires >= 3) — invoked at runtime as a child process (not vendored, not linked) to write outline bookmarks onto merged publish PDFs (`forge_plot_publish`). The caller must have `pypdf` installed in a `python`/`py` interpreter on PATH; nothing ships inside the Forge binaries.
