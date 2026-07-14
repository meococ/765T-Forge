# Roadmap

Product SemVer is independent of historical “Phase” labels in the archived build brief. **Shipped truth:** [capability-matrix.md](capability-matrix.md). **Aspirational catalog:** [archive/build-brief.md](archive/build-brief.md) §5–§7 (not shipped SoT).

## Map: Brief phases → product waves → SemVer

| Brief (historical) | Product wave | SemVer | Status |
|---|---|---|---|
| Phase 0 foundation | Wave 0 hygiene + foundation | 0.1.0 → 0.2.0 | Shipped |
| Phase 1–2 hot-path + batch | Waves 1–3 publish + QA + scale | 0.2.0 | Shipped (hot path; DSD publish **partial**) |
| Phase 3 skill/docs | Docs + skill | 0.2.0 | Shipped; keep syncing |
| Phase 4 evals | Eval scenarios E01–E10 | 0.2.x | Scenarios in-repo; live DWG lab private |
| — | P0 publish-ready GitHub + exclusive AEC gates | **0.2.1** | Technical Preview shipped |
| — | Publish determinism hardening | **0.3.0** | **In progress (Unreleased)** — trust gates + nested xref depth + DSD diagnostics; dual-seat DSD PASS pending |
| — | Moat ops DNA | **0.4.0** | Differential UX polish, screenshot after PDF, richer CDE adapters |

## Already shipped in 0.2.x (do not re-list as 0.3.0 targets)

PublishReceipt + PDF probes, IssueSetContract lite, pack v2 gates, differential receipts, sheet inventory (CSV read-only), batch resume, registry, standards packs, MCP resources/prompts/profiles, viewport typed ops, page-setup sync, Autoloader skeleton, sample pack, sync-preferring exec honesty.

## Do not build yet

Clash 3D / Civil 3D / full SSM **write** / national “metro ontology” / generative geometry / HTTP remote multi-tenant MCP / NuGet of `Forge.Shared` / docs site / geometry megakit. AccoreConsole is **not** claimed equal to desktop plot.

- Host-attested “human” booleans (`force`, `issueAcknowledged`, `dryRunDone`) as a substitute for out-of-band approval UX (code now env-gates `force`; booleans remain attested).

## Next public milestones

1. **v0.3.0** — Publish determinism + trust hardening (**Unreleased in-tree**):
   - Nested xref BFS depth report (`maxDepth`, optional `failClosed`) + pin/dependency wire-up — **code landed**; multi-seat smoke still required before claiming complete nested SoT.
   - `FORGE_ALLOW_FORCE_PUBLISH` + ceremony optional AuditId evidence + receipt `AuditId` stamp — **code landed**.
   - DSD diagnostics (`BGCOREPUBLISH`, richer fallback payload) + seat checklist — **code/docs landed**.
   - **Still required to tag:** DSD **primary** path smoke-PASS on ≥2 metro seats (see [smoke-lab.md](smoke-lab.md) seat checklist).
2. **v0.4.0** — Moat polish: differential UX, optional screenshot **after** PDF verify, richer CDE **partner** adapters (still not “we are the CDE”).

**Shipped:** **v0.2.1** Technical Preview — see CHANGELOG. **0.3.0** is Unreleased — not production-ready for unattended issue-set publish.

## Explicit non-claims (kill list)

Until evidence lands in CHANGELOG/smoke-lab:

- Do not claim production-ready / GA / unattended publish.
- Do not claim AccoreConsole ≡ desktop plot.
- Do not claim ceremony_check / cde_gate_evaluate ≡ ISO 19650 issuance or Acc/BIM360 upload.
- Do not claim nested xref closure is complete while depth is capped / best-effort open.
- Do not claim SSM write or visual QA screenshot.
- Do not tag **v0.3.0** until dual-seat DSD primary PASS is recorded.
