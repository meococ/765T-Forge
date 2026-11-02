"""Render artifacts/outcome-eval/baseline-<date>.md from runs/results.jsonl."""
import json, os, sys
from collections import defaultdict

HERE = os.path.dirname(os.path.abspath(__file__))
RUNS = os.path.join(HERE, "runs")

TAXONOMY = {
    "missing_tool": "capability absent from tool catalog (agent asked/reported it missing or had no way to do it)",
    "unclear_desc": "tool exists but description misled the agent into wrong use",
    "discovery": "agent failed to find an existing tool (search/enable friction)",
    "defaults": "tool default produced wrong outcome (wrong default value/behavior)",
    "bug": "tool returned wrong result / broke drawing / threw",
    "too_many_calls": "task needed excessive calls vs a batch path",
    "fake": "agent faked a CAD object with lines+text instead of a real object",
    "agent_skip": "agent gave up / saved nothing / ignored the requirement",
    "grader": "grader could not evaluate (infra, not the agent's fault)",
}

def classify(check_name, v, r):
    """Best-effort taxonomy tag for a FAILED check from evidence."""
    d = (v.get("detail") or "")
    for tag in TAXONOMY:
        if f"[{tag}]" in d:
            return tag
    if "answer.dwg missing" in d or "set.pdf missing" in d:
        return "agent_skip"
    if "grader" in d or "ungradable" in d:
        return "grader"
    # heuristic from tools used: tried but no tool exists
    if check_name in ("old_layers_removed",):
        return "missing_tool"
    return "bug"

def main(date="20261101"):
    results = []
    seen = {}
    for line in open(os.path.join(RUNS, "results.jsonl"), encoding="utf-8"):
        r = json.loads(line)
        seen[(r["task"], r["config"], r["run"])] = r
    results = list(seen.values())
    L = []
    L.append(f"# Outcome-eval baseline — {date}")
    L.append("")
    L.append("Agent under test: **Devin swe-2-high** via `devin.exe -p`, fresh session per run, "
             "run-local `.devin/mcp_config.local.json` -> the deployed Forge server "
             "(see DEPLOYED.json next to it for version+commit). "
             "MCP-only rule enforced by prompt; fallback to exec/scripts detected via transcript + stray files + audit.")
    L.append("Matrix: 8 tasks x {default, FORGE_TOOLSETS=all} x 2 runs = 32 primary runs.\n")
    L.append("| task | config | run | checks | wall s | tool calls | tools (top) | errors | prompt tok | compl tok | strays |")
    L.append("|---|---|---|---|---|---|---|---|---|---|---|")
    for r in sorted(results, key=lambda x: (x["task"], x["config"], x["run"])):
        tools = ", ".join(sorted(r.get("tools_used", {}), key=r.get("tools_used", {}).get, reverse=True)[:5])
        L.append(f"| {r['task']} | {r['config']} | {r['run']} | {r.get('checks_passed')}/{r.get('checks_total')} "
                 f"| {round(r.get('wall_ms',0)/1000)} | {r.get('tool_calls',0)} | {tools} | {r.get('tool_errors',0)} "
                 f"| {r.get('prompt_tokens')} | {r.get('completion_tokens')} | {','.join(r.get('stray_files') or [])} |")
    L.append("\n## Per-check detail\n")
    fails = defaultdict(list)
    for r in sorted(results, key=lambda x: (x["task"], x["config"], x["run"])):
        L.append(f"### {r['task']} / {r['config']} / run{r['run']}")
        for name, v in (r.get("checks") or {}).items():
            mark = "PASS" if v["pass"] else "FAIL"
            det = (v.get("detail") or "").replace("|", "\\|")
            L.append(f"- `{name}`: **{mark}** {det}")
            if not v["pass"]:
                fails[classify(name, v, r)].append(f"{r['task']}/{r['config']}/r{r['run']}:{name}")
        L.append("")
    L.append("## Failure taxonomy\n")
    for tag, desc in TAXONOMY.items():
        hits = fails.get(tag, [])
        L.append(f"- **{tag}** — {desc}: {len(hits)} fail(s)" + ("" if not hits else f" — `{'; '.join(hits[:12])}`"))
    out = os.path.join(HERE, f"baseline-{date}.md")
    open(out, "w").write("\n".join(L))
    print("wrote", out)

if __name__ == "__main__":
    main(*sys.argv[1:])
