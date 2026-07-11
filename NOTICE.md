# NOTICE

## Autodesk trademarks and APIs

765T-Forge is an independent open-source project. It is **not affiliated with, endorsed by, or sponsored by Autodesk, Inc.**

AutoCAD®, ObjectARX®, Autodesk®, and related names are trademarks or registered trademarks of Autodesk, Inc. in the United States and/or other countries. All other trademarks are the property of their respective owners.

## No redistribution of Autodesk binaries

This repository does **not** redistribute Autodesk proprietary assemblies (for example `AcCoreMgd.dll`, `AcDbMgd.dll`, `AcMgd.dll`, or other AutoCAD runtime components). Plugin projects reference those assemblies from a local AutoCAD installation via `AUTOCAD_2026_ROOT` (or the default install path).

Do not commit, package, or publish Autodesk DLLs with Forge releases.

## Licensed AutoCAD required

To run the AutoCAD plugin and live MCP tools that talk to a drawing session, you need a **licensed installation of AutoCAD 2026** (or a compatible Autodesk product that provides the referenced APIs) on Windows.

The MCP server (`Forge.Server`) and unit tests can build and run without AutoCAD when using `765T-Forge.ServerOnly.slnf`. Headless script execution via AccoreConsole also requires a local AutoCAD install that provides `accoreconsole.exe`.

## Upstream and prior art

Early product planning referenced community AutoCAD MCP / plugin experiments for architectural comparison, including:

- [moisesbritez92/autocad-2026](https://github.com/moisesbritez92/autocad-2026) — AutoCAD 2026 .NET plugin + MCP bridge patterns (named pipe / NETLOAD)
- [puran-water/autocad-mcp](https://github.com/puran-water/autocad-mcp) — AutoCAD LT / AutoLISP MCP patterns and agent skill practices

**765T-Forge’s shipped code is an independent all-C# implementation** (Forge.Server + Forge.Plugin + Forge.Shared). It is not a verbatim fork of those repositories. Patterns such as in-process AutoCAD plugins, named-pipe bridges, and AccoreConsole headless runs are common in the AutoCAD automation ecosystem.

If you believe a specific file retains third-party copyrighted material that should be attributed differently, open a private report per [SECURITY.md](SECURITY.md).

Historical planning notes (Vietnamese, aspirational — not the shipped tool list): [docs/archive/build-brief.md](docs/archive/build-brief.md).

## Third-party notices

Forge depends on open-source packages declared in `Directory.Packages.props` (including the Model Context Protocol C# SDK). See each package’s own license for terms.
