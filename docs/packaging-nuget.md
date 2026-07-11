# Packaging & NuGet

## Current (0.2.1)

- GitHub Release: dual zip — `765T-Forge.Server-win-x64.zip` + `765T-Forge.Plugin.zip` (draft until plugin attached).
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
3. Plugin still ships as zip / Autoloader bundle — AutoCAD seat required.

See [roadmap.md](roadmap.md) and [getting-started.md](getting-started.md).
