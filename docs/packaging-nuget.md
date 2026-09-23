# Packaging & NuGet

## Current

- **In-tree product SemVer:** **0.3.0** Unreleased — do not publish a stable NuGet/MCP registry version until tagged (see [versioning.md](versioning.md)).
- **Last tagged Technical Preview:** **0.2.1** — GitHub Release dual zip: `765T-Forge.Server-win-x64.zip` + `765T-Forge.Plugin.zip` (draft until plugin attached).
- SBOM: `765T-Forge.Server.sbom.cdx.json` on release.
- MCP registry manifest: [`.mcp/server.json`](../.mcp/server.json).
- `Forge.Server` is `PackAsTool` (`PackageId=765T.Forge.Server`, command `765t-forge`).

### Pack locally

```powershell
dotnet pack .\src\Forge.Server\Forge.Server.csproj -c Release -o artifacts\nuget
# Optional install:
# dotnet tool install -g --add-source artifacts\nuget 765T.Forge.Server
```

Pin versions in MCP client configs — never `@latest` for production.

## Rules

1. Pack **Forge.Server** only as a .NET tool / MCP host package.
2. Do **not** publish `Forge.Shared` or `Forge.Plugin` to NuGet (wire contract + Autodesk refs).
3. Plugin still ships as zip / Autoloader bundle — AutoCAD seat required. The bundle carries two components (`net8.0-windows` for AutoCAD 2025–2026, `net462` for 2017–2024). The legacy component is prepared but not yet compiled against AutoCAD 2017 reference assemblies, so no release may claim 2017–2024 support until `scripts/verify-plugin-series.ps1 -Series 2017` has passed and is recorded.

See [roadmap.md](roadmap.md) and [getting-started.md](getting-started.md).
