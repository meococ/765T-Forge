"""Re-parse audit + re-grade all completed runs; rebuild results.jsonl + baseline table."""
import json, os, sys, importlib.util

HERE = os.path.dirname(os.path.abspath(__file__))
RUNS = os.environ.get("OUTCOME_RUNS_DIR", os.path.join(HERE, "runs"))
spec = importlib.util.spec_from_file_location("grade", os.path.join(HERE, "grade.py"))
grade = importlib.util.module_from_spec(spec); spec.loader.exec_module(grade)
spec2 = importlib.util.spec_from_file_location("run_eval", os.path.join(HERE, "run_eval.py"))
rv = importlib.util.module_from_spec(spec2)
rv.__name__ = "run_eval"
try:
    spec2.loader.exec_module(rv)
except Exception:
    rv = None

def regrade_run(rundir, task_id, version=1):
    checks = grade.grade(rundir, task_id, version)
    calls, tools, errors = 0, {}, 0
    adir = os.path.join(rundir, "audit")
    if os.path.isdir(adir):
        for fn in os.listdir(adir):
            if fn.endswith(".jsonl"):
                for line in open(os.path.join(adir, fn), encoding="utf-8", errors="replace"):
                    line = line.strip()
                    if not line:
                        continue
                    try:
                        r = json.loads(line)
                    except Exception:
                        continue
                    if r.get("decisionCode") != "completed":
                        continue
                    calls += 1
                    t = r.get("tool", "?")
                    tools[t] = tools.get(t, 0) + 1
                    if r.get("ok") is False or r.get("errorCode"):
                        errors += 1
    return checks, calls, tools, errors

def main():
    tasks = {t["id"]: t for t in json.load(open(os.path.join(HERE, "tasks.json")))["tasks"]}
    out = []
    for tid in sorted(os.listdir(RUNS)):
        tdir = os.path.join(RUNS, tid)
        if not tid.startswith("t") or not os.path.isdir(tdir):
            continue
        for cfg in sorted(os.listdir(tdir)):
            cdir = os.path.join(tdir, cfg)
            if cfg not in ("default", "all") or not os.path.isdir(cdir):
                continue
            for rn in sorted(os.listdir(cdir)):
                rundir = os.path.join(cdir, rn)
                rp = os.path.join(rundir, "result.json")
                if not os.path.exists(rp):
                    # synthesize a result for crashed runners (artifacts exist)
                    r = {"task": tid, "config": cfg, "run": int(rn.replace("run", "")),
                         "model": "swe-2-high", "runner": "devin.exe -p",
                         "wall_ms": None, "synthesized": True}
                else:
                    r = json.load(open(rp))
                checks, calls, tools, errors = regrade_run(rundir, tasks[tid]["grader"])
                checks_v2, _, _, _ = regrade_run(rundir, tasks[tid]["grader"], version=2)
                tp = os.path.join(rundir, "transcript.jsonl")
                if os.path.exists(tp):
                    try:
                        tj = json.load(open(tp, encoding="utf-8"))
                        m = tj.get("final_metrics") or {}
                        r["prompt_tokens"] = m.get("total_prompt_tokens")
                        r["completion_tokens"] = m.get("total_completion_tokens")
                        r["agent_steps"] = m.get("total_steps")
                        if not r.get("wall_ms"):
                            ts = [s.get("timestamp") for s in tj.get("steps", []) if s.get("timestamp")]
                            if len(ts) >= 2:
                                from datetime import datetime
                                a = datetime.fromisoformat(ts[0].replace("Z", "+00:00"))
                                b = datetime.fromisoformat(ts[-1].replace("Z", "+00:00"))
                                r["wall_ms"] = round((b - a).total_seconds() * 1000, 1)
                    except Exception:
                        pass
                r["checks"] = checks
                r["checks_passed"] = sum(1 for v in checks.values() if v["pass"])
                r["checks_total"] = len(checks)
                r["checks_v2"] = checks_v2
                r["checks_v2_passed"] = sum(1 for v in checks_v2.values() if v["pass"])
                r["checks_v2_total"] = len(checks_v2)
                r["tool_calls"], r["tools_used"], r["tool_errors"] = calls, tools, errors
                json.dump(r, open(rp, "w"), indent=1)
                out.append(r)
                print(f"{tid}|{cfg}|{rn}: v1 {r['checks_passed']}/{r['checks_total']} | v2 {r['checks_v2_passed']}/{r['checks_v2_total']} | {calls} calls")
    # dedupe into results.jsonl by (task,config,run) keeping last
    seen = {}
    for r in out:
        seen[(r["task"], r["config"], r["run"])] = r
    with open(os.path.join(RUNS, "results.jsonl"), "w") as f:
        for r in seen.values():
            f.write(json.dumps(r) + "\n")

if __name__ == "__main__":
    main()
