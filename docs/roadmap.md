# Roadmap

Product SemVer is independent of historical “Phase” labels in the build brief. **Shipped truth:** [capability-matrix.md](capability-matrix.md). **Aspirational catalog:** [history/build-brief.md](history/build-brief.md) §5–§7 (disclaimer: not all tools listed there are implemented).

## Map: Brief phases → product waves → SemVer

| Brief (historical) | Product wave | SemVer | Status |
|---|---|---|---|
| Phase 0 foundation | Wave 0 hygiene + foundation | 0.1.0 → 0.2.0 | Shipped |
| Phase 1 hot-path structured | Waves 1–2 publish + QA | 0.2.0 | Shipped (hot path) |
| Phase 2 headless/batch | Wave 3 scale | 0.2.0 | Shipped (`batch_run`, recipe, pack) |
| Phase 3 skill/docs | Wave 0 docs + skill | 0.2.0 | Shipped; keep syncing |
| Phase 4 evals | Eval scenarios E01–E10 | 0.2.x | Scenarios in-repo; live DWG lab private |
| — | P0 publish-ready GitHub | 0.2.x | Autoloader, dual-zip release, embedded docs, attribution |
| — | Evidence chain (PublishReceipt, PDF probes) | 0.3.0 | Differentiation vs geometry MCPs |
| — | Domain contracts (IssueSet, pack v2 gates) | 0.3.x | Fail-closed project judgment |
| — | Moat (differential receipts, sheet inventory, batch resume) | 0.4.0 | Hard to copy without AEC ops DNA |

**Already in 0.2.0 (do not re-list as 0.3.0 targets):** sync-preferring exec honesty, drawing registry, standards packs, MCP resources/prompts/profiles, viewport typed ops, page-setup sync path, Autoloader skeleton, sample pack.

## Do not build yet

Clash 3D / Civil 3D / full SSM **write** product / national “metro ontology” / generative geometry / HTTP remote multi-tenant MCP / NuGet of `Forge.Shared` / docs site / geometry megakit. Read-only sheet inventory (DST/CSV → contract) may land in 0.4.x without SSM write.

## Next public milestones

1. **v0.2.x tag** only after smoke checklist ([smoke-lab.md](smoke-lab.md)) has at least one maintainer pass, with **both** server and plugin zips on the GitHub Release.
2. **v0.3.0** — PublishReceipt + PDF probes + IssueSetContract lite + pack v2 blocking gates + actionable findings.
3. **v0.4.0** — differential receipts, read-only sheet inventory, hardened batch resume, optional screenshot **after** PDF verify.
