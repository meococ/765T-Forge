# Competitive benchmark — AutoCAD MCP servers

Factual comparison of 765T-Forge against the public AutoCAD MCP landscape, with
Autodesk's official Revit MCP as the design bar. Every row links to the repo or doc the
claim comes from. Verified 2026-11-01; upstream repos move — recheck before quoting
numbers externally.

## Table

| Project | Tools | Typed + structured output | Safety model | QA / read-back | Plot / publish | Toolsets | Transports | Install | Tests / CI | Licence |
|---|---|---|---|---|---|---|---|---|---|---|
| **765T-Forge** | 123 total; **19 default** via toolsets | `outputSchema` + `structuredContent` on all tools; zero boolean schemas; strict-schema CI test | Named pipe + required token, denylist, dryRun, byte-verified backup, JSONL audit, overwrite acks + MCP elicitation, unsafe-ops off by default | `forge_qa_preflight` gate, layer/xref/annotation-clash audits, readback-after-timeout | Real DSD multi-sheet publish + named bookmarks + per-layout PNG + plot presets | Runtime enable + `list_changed`; `FORGE_TOOLSETS`/`--toolsets` | stdio + named pipe; accoreconsole for headless batch | Build + `NETLOAD`; release zips; **`dnx` prepared, not published** | 830 tests; CI server green, plugin on self-hosted runner | MIT |
| [U-C4N/Autocad-MCP](https://github.com/u-c4n/autocad-mcp) (`autocad-mcp-pro`) | ~240 advertised | — | Output-dir restriction | audit/verify tools advertised | export DXF/PDF advertised | — | stdio; COM live + ezdxf headless backends | `pip install autocad-mcp-pro` / `uvx` | — | [repo](https://github.com/u-c4n/autocad-mcp) |
| [puran-water/autocad-mcp](https://github.com/puran-water/autocad-mcp) (593★) | 8 consolidated | — | — | entity inspect/query | — | — | stdio; AutoCAD LT File-IPC (AutoLISP dispatcher) + ezdxf | `uv sync` + APPLOAD dispatcher | community PRs open | MIT |
| [daobataotie/CAD-MCP](https://github.com/daobataotie/CAD-MCP) (660★) | 35 | FastMCP structured output + annotations | — | query→inspect→correct loop advertised | — | — | stdio + **Streamable HTTP** (v2.0) | clone + pip | — | MIT |
| [AnCode666/multiCAD-mcp](https://github.com/AnCode666/multiCAD-mcp) (145★) | 7 unified → 56 commands | — | — | — | — | — | stdio; COM across AutoCAD/ZWCAD/GstarCAD/BricsCAD | `uv sync` + pywin32 | 181 tests advertised | Apache-2.0 |
| [moisesbritez92/autocad-mcp](https://github.com/moisesbritez92/autocad-mcp) | 45 | — | — | query/measure tools | — | — | HTTP localhost:12345 plugin + accoreconsole headless | clone + NETLOAD | — | — |
| [beiming183-cloud/AutoCAD-MCP](https://github.com/beiming183-cloud/AutoCAD-MCP) | 12 consolidated | — | managed output workspace | audit + geometry DRC (gaps, crossings, tangency) | plot_pdf + render_preview with SHA-256 | — | stdio; file IPC + ezdxf | clone | tests badge | — |
| [Slacker-LLC/autocad-mcp](https://github.com/Slacker-LLC/autocad-mcp) | ~30 scoped | — | `ACAD_MCP_OUTPUT_ROOT` write confinement; no arbitrary exec | query_entities | — | — | stdio only; COM on AutoCAD 2026 | clone | — | — |
| [Erfouni/AutoCAD-AI](https://github.com/Erfouni/AutoCAD-AI) | 24 | — | `send_command` gated by env var | entity query + capture_view | export_pdf | — | **HTTP + tunnel** to cloud assistants | clone | — | — |
| [ahmetcemkaraca/AutoCAD_MCP](https://github.com/ahmetcemkaraca/AutoCAD_MCP) (aka BarryMcAdams) | 7 working (+25 dev) | — | — | — | — | — | stdio; pyautocad COM | `uv sync` | — | MIT |
| [vigneshpbmenon/autocad-mcp-server](https://github.com/vigneshpbmenon/autocad-mcp-server) | 6 | — | — | — | — | — | stdio; COM | `uv sync` | — | MIT |
| [NCO-1986/AutoCAD_mcp](https://github.com/NCO-1986/AutoCAD_mcp) | ~21 | — | — | — | plot_to_pdf | — | plugin | clone + NETLOAD | — | — |

**Design bar — [Autodesk Revit MCP](https://help.autodesk.com/cloudhelp/ENU/ADSKMCP-RevitMcp/files/ADSKMCP_RevitMcp_setting_up_revit_mcp_server_html.html)**
(Technical Preview, Revit 2027.2): first-party stdio exe; tool surfaces partitioned by
permission tier (`--tool-permission=StableReadOnly`, `--tool-min-permission=Stable`,
Experimental preview) — the same shape Forge's toolsets + unsafe-ops gate implement.
Related: [APS sample Revit MCP](https://github.com/autodesk-platform-services/aps-sample-mcp-server-revit-automation)
(remote HTTP + SSA JWT, MIT sample) is Autodesk's cloud-side pattern Forge's deferred
remote-transport note mirrors.

## Where Forge leads

- **Production workflow depth**: sheet-set publish (DSD + bookmarks), publish preflight,
  titleblock campaigns, QA audit trail — the community servers are draw-first.
- **Safety surface**: required-token pipe, dry-run, byte-verified backup, audit log,
  overwrite acknowledgement *with MCP elicitation*, unsafe-ops off — nobody else ships
  more than env-gated exec + output-dir confinement.
- **MCP framework completeness**: toolsets with `list_changed`, per-tool `outputSchema`
  + `structuredContent` + titles, progress notifications, elicitation, tasks,
  completions, live drawing resources. daobataotie v2.0 is the only other server
  advertising structured output + progress.

## Where Forge honestly lags

- **Published install**: puran-water (593★) and daobataotie (660★) install with one
  command (`uv sync` / pip); `dnx Forge765T.Server` is prepared, not published.
- **Multi-CAD**: multiCAD-mcp and daobataotie cover ZWCAD/GstarCAD/BricsCAD; Forge is
  AutoCAD-only.
- **Remote transport**: daobataotie ships Streamable HTTP; Forge documents localhost/auth
  risk in a design note only (deliberate).
- **Headless DXF authoring**: puran-water / U-C4N / beiming183 offer an ezdxf backend
  with no AutoCAD; Forge's headless path is accoreconsole batch, not a full backend.

Sources: linked repos above; Forge rows verified against `src/` + `tests/` on
`origin/master` (this checkout) at the date in the header.
