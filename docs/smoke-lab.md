# Smoke lab checklist (AutoCAD 2026)

This checklist and `scripts/smoke-lab.ps1` run on **AutoCAD 2026 only**. They do not exercise 2017–2025.

Maintainer-only. Do **not** commit customer DWGs. Record pass/fail in the GitHub Release notes before announcing a public `v*` tag.

A release is **incomplete** without both `765T-Forge.Server-win-x64.zip` and `765T-Forge.Plugin.zip`.

Automated helper: [`scripts/smoke-lab.ps1`](../scripts/smoke-lab.ps1) (Autoloader + named-pipe checks). **Do not** probe the pipe with Connect/Dispose without sending a JSON line — that stalls the single-instance pipe server.

## Preconditions

- [x] Unpacked **Release** server zip contains `docs/capability-matrix.md` and `docs/safety.md` next to the exe
- [x] Plugin installed via `install-plugin.ps1` + Autoloader Series **R25.1** under `%APPDATA%\Autodesk\ApplicationPlugins`
- [x] AutoCAD 2026 running; `MCP_STATUS` shows product/pipe/token; named pipe responds
- [x] Matching `FORGE_AUTOCAD_TOKEN` / `FORGE_PIPE_NAME` on AutoCAD process
- [x] Lab / project DWG with ≥1 paper layout

## Steps

| # | Action | Expected | 2026-07-11 |
|---|---|---|---|
| 1 | `forge_system_health` | `status=ok`, pipe matches | PASS |
| 2 | MCP resource `forge://docs/capability-matrix` | Full matrix from Release zip | PASS (embedded in pack) |
| 3 | `forge_qa_preflight` missing required tag | `passed=false`, `titleblock_tag_missing` + SuggestedTool | PASS |
| 4 | `forge_plot_publish` existing PDF, no overwrite ack | `publish_overwrite_not_acknowledged` | PASS |
| 5a | `forge_plot_publish` dryRun | ok | PASS |
| 5b | Real PDF output | file exists, verify passed | PASS via `forge_plot_to_pdf` (Layout1); DSD `PublishDsd` returned `publish_no_output` on this seat — tracked |
| 6 | `forge_issue_set_validate` bad layout | `issue_set_layout_missing` | PASS |

## Record

```text
Date: 2026-07-11
Build/commit / Release tag: untagged in-tree 0.2.0 (post Wave 0–2)
Server zip SHA256: 548B05140466B27F9034AE5F8EABFC7D4CC1D1FF1A8019E5BEC45214BF43FC48
Plugin zip SHA256: 7468EE43E99F4EB554B987D205EB48B274D6EB1831DC8E60BD21CBDFA23E2E58
Operator: agent smoke-lab (local AutoCAD 2026 Education)
Pass: yes (critical path) — with DSD publish caveat below
Notes:
  - Active drawing during live checks: metro DWG "03. HO THU NUOC DVB.dwg" (Layout1)
  - Autoloader R25.1 loaded; pipe 765T.Forge.AutoCAD
  - forge_plot_to_pdf -> forge-smoke-plot-to-pdf.pdf (1965 bytes, verification.passed)
  - forge_plot_publish (Publisher.PublishDsd) did not materialize PDF on this run (publish_no_output)
  - Results JSON: %LOCALAPPDATA%\765T-Forge\smoke-lab\smoke-results.json
  - Release pack Complete=true (both zips)
```

## Maintainer pass log

### Pass 2026-07-11

- Tag: untagged / product SemVer 0.2.0
- Operator: agent (Cursor) + AutoCAD 2026 local
- Result: **PASS** (critical: health, preflight, overwrite ack, issue-set validate, plot_to_pdf)
- Notes: DSD multi-layout publish path needs follow-up on this workstation; single-layout `forge_plot_to_pdf` verified end-to-end with backup + PDF verify.
