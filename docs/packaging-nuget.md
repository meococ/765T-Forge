# Packaging & NuGet (deferred)

## Current

- GitHub Release: `765T-Forge.Server-win-x64.zip` (CI) + `765T-Forge.Plugin.zip` (maintainer / self-hosted).
- No public NuGet yet.

## Later (`dnx` / NuGet)

When the MCP tool surface stabilizes (post 0.3):

1. Pack **Forge.Server** only as a .NET tool / MCP host package.
2. Do **not** publish `Forge.Shared` or `Forge.Plugin` to NuGet early (wire contract + Autodesk refs).
3. Document pin-by-version in client MCP configs (never `@latest` for production).

See [roadmap.md](roadmap.md).
