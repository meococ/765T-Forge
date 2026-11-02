## Summary

<!-- What and why (1–3 bullets). -->

## Type

- [ ] Bug fix
- [ ] Feature / capability
- [ ] Docs
- [ ] CI / packaging
- [ ] Safety / security hardening

## Tool surface

- [ ] No tool surface change
- [ ] Tool list/flags changed — `forge://docs/capability-matrix` regenerates from the registry; update the README tool overview if groups changed

## Adding a tool checklist

<!-- Skip if N/A -->

- [ ] `ForgeMcpTools.cs` MCP method + annotations
- [ ] `ForgeToolRegistry` / `ToolMetadata.cs` entry
- [ ] `PluginCommandProcessor` case
- [ ] Tests (`ToolMetadataTests` / `SafetyPolicyTests` as needed)

## Test plan

- [ ] `dotnet test .\765T-Forge.ServerOnly.slnf` (or CI green)
- [ ] AutoCAD NETLOAD smoke (if plugin touched): `MCP_STATUS`, `forge_system_health`
- [ ] Dry-run then live path for any write tool changed

## Risk notes

<!-- Timeout/write races, denylist gaps, overwrite paths, token handling. -->
