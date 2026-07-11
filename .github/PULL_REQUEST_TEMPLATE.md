## Summary

<!-- What and why (1–3 bullets). -->

## Type

- [ ] Bug fix
- [ ] Feature / capability
- [ ] Docs / examples / skill
- [ ] CI / packaging
- [ ] Safety / security hardening

## Capability matrix

- [ ] No tool surface change
- [ ] Updated `docs/capability-matrix.md` (implemented / partial / planned)
- [ ] Updated `CHANGELOG.md` under Unreleased
- [ ] Updated `skills/765t-forge/SKILL.md` (if agent workflow changed)

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
