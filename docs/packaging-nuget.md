# Packaging & NuGet (prepared — NOT published)

Status: the packaging surface is staged for the owner to publish. **Nothing here has
been pushed to NuGet.org, the MCP Registry, a git tag, or a GitHub Release.**

## Current distribution

- GitHub Release: `765T-Forge.Server-win-x64.zip` (CI) + `765T-Forge.Plugin.zip` (maintainer / self-hosted).
- MCP Registry manifest: [`server.json`](../server.json) at the repo root — staged, not submitted.

## Prepared `dnx` / NuGet shape

`Forge.Server` packs as a .NET tool (`PackAsTool` in the csproj):

- Package ID: **`Forge765T.Server`** (NuGet IDs should not lead with a digit).
- Tool command: **`forge-server`** — so configs read `"command": "forge-server"` or
  `dnx Forge765T.Server@<version>`.
- Package README: repo `README.md` (packed at nupkg root) carrying the registry
  ownership marker `<!-- mcp-name: io.github.meococ/765t-forge -->`.

One-line install once published:

```powershell
dnx Forge765T.Server@0.3.0 --yes   # pin a version — never @latest
```

`server.json` declares the package + env vars (`FORGE_AUTOCAD_TOKEN` required/secret,
`FORGE_PIPE_NAME`, `FORGE_TOOLSETS`, `FORGE_BACKUP_DIR`, `FORGE_AUDIT_DIR`).

## Publish checklist (owner-only, later)

1. `dotnet pack src/Forge.Server -c Release` → `Forge765T.Server.<version>.nupkg`.
2. `dotnet nuget push` to NuGet.org under the owner's account.
3. Submit `server.json` to the MCP Registry — the `mcp-name` marker in the package
   README is the ownership proof.
4. Keep `server.json` `version`/`packages[].version` in lockstep with
   `Directory.Build.props` `<Version>`.

Do **not** publish `Forge.Shared` or `Forge.Plugin` to NuGet (wire contract +
Autodesk refs). The plugin keeps shipping via `765T-Forge.Plugin.zip`.

See [roadmap.md](roadmap.md) and [versioning.md](versioning.md).
