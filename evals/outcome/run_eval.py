"""Outcome-eval runner.

Matrix: tasks x toolset-configs x N runs, each a FRESH devin.exe -p swe-2-high
session in its own run dir, reaching Forge only through the run-local
.devin/mcp_config.local.json -> deploy\\server-v030.

Usage: python run_eval.py [--config default|all|both] [--runs 2] [--tasks t1,t3] [--model swe-2-high]
"""
import json, os, re, shutil, subprocess, sys, time, importlib.util

HERE = os.path.dirname(os.path.abspath(__file__))
DEVIN = os.environ.get("DEVIN_EXE", "devin")
SEEDS = os.path.join(HERE, "seeds")
RUNS = os.environ.get("OUTCOME_RUNS_DIR", os.path.join(HERE, "runs"))
TIMEOUT = 900          # 15-minute wall cap
MAX_CALLS = 80         # tool-call cap — a retry loop must not burn tokens

spec = importlib.util.spec_from_file_location("grade", os.path.join(HERE, "grade.py"))
grade = importlib.util.module_from_spec(spec); spec.loader.exec_module(grade)
spec2 = importlib.util.spec_from_file_location("forgeclient", os.path.join(HERE, "forgeclient.py"))
fc = importlib.util.module_from_spec(spec2); spec2.loader.exec_module(fc)

TASKS = json.load(open(os.path.join(HERE, "tasks.json")))
COMMON = TASKS["common_rules"]

REPO = os.path.dirname(os.path.dirname(os.path.dirname(HERE)))
SERVER = os.environ.get("FORGE_SERVER_EXE",
                       os.path.join(REPO, "deploy", "server-v030", "Forge.Server.exe"))
MCP_CFG = '''{
  "mcpServers": {
    "765t-forge": {
      "command": "%s",
      "args": [],
      "env": %s
    }
  }
}
'''
PERM_CFG = '{"permissions": {"allow": ["mcp__*"]}}'

def write_run_configs(rundir, toolsets):
    dd = os.path.join(rundir, ".devin")
    os.makedirs(dd, exist_ok=True)
    env = {"FORGE_AUDIT_DIR": os.path.join(rundir, "audit").replace("\\", "\\\\")}
    if toolsets == "all":
        env["FORGE_TOOLSETS"] = "all"
    with open(os.path.join(dd, "mcp_config.local.json"), "w") as f:
        f.write(MCP_CFG % (SERVER.replace("\\", "\\\\"), json.dumps(env)))
    with open(os.path.join(dd, "config.local.json"), "w") as f:
        f.write(PERM_CFG)

def prep_rundir(task, toolsets, n):
    rundir = os.path.join(RUNS, task["id"], toolsets, f"run{n}")
    if os.path.exists(rundir):
        shutil.rmtree(rundir)
    os.makedirs(rundir)
    ext = os.path.splitext(task["seed"])[1]
    shutil.copyfile(os.path.join(SEEDS, task["seed"]), os.path.join(rundir, "seed" + ext))
    for dst, src in (task.get("files") or {}).items():
        shutil.copyfile(os.path.join(SEEDS, src), os.path.join(rundir, dst))
    prompt = task["prompt"].replace("{RUNDIR}", rundir) + "\n\n" + COMMON.replace("{RUNDIR}", rundir)
    with open(os.path.join(rundir, "prompt.txt"), "w") as f:
        f.write(prompt)
    write_run_configs(rundir, toolsets)
    return rundir

def parse_audit(rundir):
    adir = os.path.join(rundir, "audit")
    calls, tools, errors = 0, {}, 0
    if os.path.isdir(adir):
        for fn in os.listdir(adir):
            if not fn.endswith(".jsonl"):
                continue
            for line in open(os.path.join(adir, fn), encoding="utf-8", errors="replace"):
                line = line.strip()
                if not line:
                    continue
                try:
                    r = json.loads(line)
                except Exception:
                    continue
                if r.get("decisionCode") != "completed":
                    continue  # one 'allowed' + one 'completed' record per call
                calls += 1
                t = r.get("tool", "?")
                tools[t] = tools.get(t, 0) + 1
                if r.get("ok") is False or r.get("errorCode"):
                    errors += 1
    return calls, tools, errors

def parse_transcript(rundir):
    tp = os.path.join(rundir, "transcript.jsonl")
    out = {"prompt_tokens": None, "completion_tokens": None, "steps": None, "exec_hints": 0}
    if os.path.exists(tp):
        try:
            j = json.load(open(tp, encoding="utf-8"))
            m = j.get("final_metrics") or {}
            out["prompt_tokens"] = m.get("total_prompt_tokens")
            out["completion_tokens"] = m.get("total_completion_tokens")
            out["steps"] = m.get("total_steps")
            for s in j.get("steps", []):
                if s.get("source") == "agent":
                    txt = s.get("message") or ""
                    if re.search(r"\b(exec|python|lisp|\.scr|script)\b", txt, re.I):
                        out["exec_hints"] += 1
        except Exception:
            pass
    return out

def stray_files(rundir):
    """Files the agent wrote that aren't expected outputs — fallback signal."""
    ok_names = {"seed.dxf", "seed.dgn", "rooms.json", "register.json", "prompt.txt",
                "answer.dwg", "answer.dxf", "set.pdf", "transcript.jsonl"}
    out = []
    for root, dirs, files in os.walk(rundir):
        if ".devin" in root or "audit" in root:
            continue
        for fn in files:
            if fn not in ok_names and not fn.startswith("session") and \
               not fn.lower().endswith((".dwl", ".dwl2", ".bak")):
                try:
                    out.append(os.path.relpath(os.path.join(root, fn), rundir))
                except ValueError:
                    out.append(fn + " (device-name)")
    return out

def export_and_grade(rundir, task, version=1):
    """Open answer.dwg in AutoCAD via orchestrator MCP, export answer.dxf, grade."""
    if not os.path.exists(os.path.join(rundir, "answer.dwg")) and not os.path.exists(os.path.join(rundir, "set.pdf")):
        return grade.grade(rundir, task["grader"], version=version)
    p = fc.spawn({"FORGE_TOOLSETS": "all"})
    try:
        fc.init(p)
        n = [10]
        def c(name, args, t=300):
            n[0] += 1
            return fc.call(p, n[0], name, args, timeout=t)
        if os.path.exists(os.path.join(rundir, "answer.dwg")):
            c("forge_doc_open", {"path": os.path.join(rundir, "answer.dwg")})
            c("forge_doc_export_dxf", {"outputPath": os.path.join(rundir, "answer.dxf"),
                                     "overwriteAcknowledged": True})
        res = grade.grade(rundir, task["grader"], version=version)
    except Exception as e:
        res = {"grader_transport": {"pass": False, "detail": f"{type(e).__name__}: {e} [grader]"}}
    finally:
        # close all open docs so the next run sees a clean AutoCAD
        try:
            docs = fc.call(p, 99, "forge_doc_list_open", {})
            doclist = (docs.get("data") or {}).get("documents") or docs.get("documents") or []
            for i, d in enumerate(doclist):
                if not isinstance(d, dict):
                    continue
                try:
                    fc.call(p, 200 + i, "forge_doc_close", {"path": d.get("name"),
                                                          "discardAcknowledged": True})
                except Exception:
                    pass
        except Exception:
            pass
        try:
            p.kill()
        except Exception:
            pass
    return res

def run_one(task, toolsets, n, model, force=False, version=1):
    rundir0 = os.path.join(RUNS, task["id"], toolsets, f"run{n}")
    if not force and os.path.exists(os.path.join(rundir0, "result.json")):
        print(f"[{task['id']}|{toolsets}|r{n}] skip (done)")
        return json.load(open(os.path.join(rundir0, "result.json")))
    rundir = prep_rundir(task, toolsets, n)
    t0 = time.time()
    cmd = [DEVIN, "-p", "--prompt-file", os.path.join(rundir, "prompt.txt"),
           "--model", model, "--permission-mode", "dangerous",
           "--respect-workspace-trust", "false",
           "--export", os.path.join(rundir, "transcript.jsonl")]
    cap_hit = None
    proc = subprocess.Popen(cmd, cwd=rundir, stdout=subprocess.PIPE,
                            stderr=subprocess.PIPE, text=True)
    while proc.poll() is None:
        if time.time() - t0 > TIMEOUT:
            cap_hit = "time"
            proc.kill()
            break
        live_calls, _, _ = parse_audit(rundir)
        if live_calls >= MAX_CALLS:
            cap_hit = "calls"
            proc.kill()
            break
        time.sleep(5)
    try:
        out, err = proc.communicate(timeout=60)
    except subprocess.TimeoutExpired:
        proc.kill()
        out, err = proc.communicate()
    wall = time.time() - t0
    timed_out = cap_hit == "time"
    stdout_tail = (out or "")[-2000:]
    stderr_tail = (err or "")[-1000:]
    calls, tools, errors = parse_audit(rundir)
    tr = parse_transcript(rundir)
    checks = export_and_grade(rundir, task, version=version)
    passed = sum(1 for v in checks.values() if v["pass"])
    total = len(checks)
    result = {
        "task": task["id"], "config": toolsets, "run": n, "model": model,
        "runner": "devin.exe -p", "grader_version": version,
        "wall_ms": round(wall * 1000, 1), "timed_out": timed_out,
        "cap_hit": cap_hit,
        "tool_calls": calls, "tools_used": tools, "tool_errors": errors,
        "prompt_tokens": tr["prompt_tokens"], "completion_tokens": tr["completion_tokens"],
        "agent_steps": tr["steps"], "exec_message_hints": tr["exec_hints"],
        "stray_files": stray_files(rundir),
        "checks_passed": passed, "checks_total": total,
        "checks": checks,
    }
    with open(os.path.join(rundir, "result.json"), "w") as f:
        json.dump(result, f, indent=1)
    with open(os.path.join(RUNS, "results.jsonl"), "a") as f:
        f.write(json.dumps(result) + "\n")
    print(f"[{task['id']}|{toolsets}|r{n}] {passed}/{total} checks, {wall:.0f}s, {calls} calls, "
          f"cap={cap_hit}, err={errors}, tok={tr['prompt_tokens']}/{tr['completion_tokens']}, "
          f"strays={result['stray_files']}")
    return result

def main():
    configs = ["default", "all"]
    runs = 2
    task_ids = [t["id"] for t in TASKS["tasks"]]
    model = "swe-2-high"
    version = 1
    a = sys.argv[1:]
    if "--config" in a:
        configs = [a[a.index("--config") + 1]] if a[a.index("--config") + 1] != "both" else ["default", "all"]
    if "--runs" in a:
        runs = int(a[a.index("--runs") + 1])
    if "--tasks" in a:
        task_ids = a[a.index("--tasks") + 1].split(",")
    if "--model" in a:
        model = a[a.index("--model") + 1]
    if "--grader" in a:
        version = int(a[a.index("--grader") + 1])
    os.makedirs(RUNS, exist_ok=True)
    for cfg in configs:
        for tid in task_ids:
            task = next(t for t in TASKS["tasks"] if t["id"] == tid)
            for n in range(1, runs + 1):
                try:
                    run_one(task, cfg, n, model, force=("--force" in a), version=version)
                except Exception as e:
                    print(f"[{tid}|{cfg}|r{n}] RUNNER ERROR {type(e).__name__}: {e}")
                    with open(os.path.join(RUNS, "results.jsonl"), "a") as f:
                        f.write(json.dumps({"task": tid, "config": cfg, "run": n, "model": model,
                                            "runner_error": f"{type(e).__name__}: {e}"}) + "\n")

if __name__ == "__main__":
    main()
