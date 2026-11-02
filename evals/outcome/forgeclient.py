"""Orchestrator-side stdio MCP client for deploy\\server-v030 (grading only)."""
import json, os, subprocess, time

_REPO = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
EXE = os.environ.get("FORGE_SERVER_EXE", os.path.join(_REPO, "deploy", "server-v030", "Forge.Server.exe"))

def spawn(extra_env=None):
    env = dict(os.environ)
    if extra_env:
        env.update(extra_env)
    return subprocess.Popen([EXE], stdin=subprocess.PIPE, stdout=subprocess.PIPE,
                            stderr=subprocess.DEVNULL, env=env, text=True)

def send(p, m):
    p.stdin.write(json.dumps(m) + "\n")
    p.stdin.flush()

def rd(p, i, timeout=120):
    deadline = time.time() + timeout
    while time.time() < deadline:
        line = p.stdout.readline()
        if not line.startswith("{"):
            continue
        m = json.loads(line)
        if m.get("id") == i and "method" not in m:
            return m
        if "method" in m and "id" in m:
            p.stdin.write(json.dumps({"jsonrpc": "2.0", "id": m["id"],
                                      "error": {"code": -32601, "message": "client decline"}}) + "\n")
            p.stdin.flush()
    raise TimeoutError(f"no reply for id {i}")

def init(p):
    send(p, {"jsonrpc": "2.0", "id": 1, "method": "initialize",
             "params": {"protocolVersion": "2025-11-25", "capabilities": {},
                        "clientInfo": {"name": "eval-grader", "version": "0"}}})
    r = rd(p, 1)
    send(p, {"jsonrpc": "2.0", "method": "notifications/initialized"})
    return r

def call(p, i, name, args, timeout=300):
    send(p, {"jsonrpc": "2.0", "id": i, "method": "tools/call",
             "params": {"name": name, "arguments": args}})
    r = rd(p, i, timeout=timeout)
    res = r.get("result") or {}
    return res.get("structuredContent") or res
