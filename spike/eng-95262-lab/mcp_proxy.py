#!/usr/bin/env python3
"""
ENG-95262 — prototype of the PROXY (supervisor) execution model for the clio MCP server.

Idea under test: keep the MCP contract exactly as it is, but move the EXECUTION boundary. Every
environment-touching tool call runs in a short-lived child `clio mcp-server`; the parent only routes.
Because the child speaks MCP too, there is no new wire format to invent: the parent forwards
`tools/call` verbatim and returns the child's response verbatim.

What that buys, and what this prototype is meant to demonstrate:
  * a stalled call cannot poison anything — its child is killed at the budget and everything it owned
    (session, cookie container, monitor state, current directory) dies with the process;
  * the budget is enforceable even for calls whose transport has NO timeout parameter at all
    (`UploadFile` / `DownloadFile` / `install-application`), because killing a process needs no
    cooperation from the transport;
  * the next call is unaffected and the environment recovers the moment the backend does.

Deliberately NOT modelled here (they matter for the real thing, not for the claim under test):
  process reuse/pooling, the four long-running state machines whose registries live in-process,
  catalog caching beyond one snapshot, and per-tool routing policy (this prototype proxies every tool).

Usage (drop-in replacement for `dotnet clio.dll mcp-server` in the harness):
    python3 mcp_proxy.py --clio <path to clio.dll> --budget 12
"""
import argparse, json, os, subprocess, sys, threading, time

LOG = open(os.environ.get("PROXY_LOG", "/dev/null"), "a", buffering=1)

def log(msg):
    print(f"[proxy {time.strftime('%H:%M:%S')}] {msg}", file=LOG)

class Child:
    """One short-lived `clio mcp-server`. Owns its process, its Creatio session and nothing else."""

    def __init__(self, cmd, env):
        self.p = subprocess.Popen(cmd, stdin=subprocess.PIPE, stdout=subprocess.PIPE,
                                  stderr=subprocess.DEVNULL, env=env, text=True, bufsize=1)
        self._id = 0
        self._pending = {}
        self._lock = threading.Lock()
        self.notifications = []
        threading.Thread(target=self._reader, daemon=True).start()

    def _reader(self):
        try:
            for line in self.p.stdout:
                line = line.strip()
                if not line.startswith("{"):
                    continue
                try:
                    msg = json.loads(line)
                except json.JSONDecodeError:
                    continue
                rid = msg.get("id")
                if rid is None:
                    self.notifications.append(msg)          # progress/log — forwarded upstream
                    continue
                with self._lock:
                    slot = self._pending.get(rid)
                if slot:
                    slot["msg"] = msg
                    slot["event"].set()
        except Exception:                                    # pipe closed by kill(); expected
            pass
        finally:
            with self._lock:
                for slot in self._pending.values():
                    slot["event"].set()

    def request(self, method, params, timeout):
        with self._lock:
            self._id += 1
            rid = self._id
            slot = {"event": threading.Event(), "msg": None}
            self._pending[rid] = slot
        try:
            self.p.stdin.write(json.dumps({"jsonrpc": "2.0", "id": rid,
                                           "method": method, "params": params}) + "\n")
            self.p.stdin.flush()
        except Exception:
            return None
        return slot["msg"] if slot["event"].wait(timeout) else None

    def notify(self, method, params):
        try:
            self.p.stdin.write(json.dumps({"jsonrpc": "2.0", "method": method, "params": params}) + "\n")
            self.p.stdin.flush()
        except Exception:
            pass

    def kill(self):
        # The whole point: no cooperation from the transport is required. Whatever the child was
        # blocked on — a socket with no timeout, an upload with no timeout parameter — dies here.
        try:
            self.p.kill()
            self.p.wait(timeout=5)
        except Exception:
            pass

# ── long-operation routing ──────────────────────────────────────────────────────────────────────────
# The four tracked long operations keep their state in an IN-PROCESS registry, so a status poll must reach
# the SAME child that started the work — a fresh child answers "not-found". The parent therefore keeps a
# STICKY child per (environment, family): the starter tool creates it and does not kill it; the status tool
# is routed to it; it is reaped when the operation reaches a terminal state or its TTL expires.
#
# The routing key cannot be read off the raw tool name alone: these tools are non-resident, so an agent
# reaches them through `clio-run {"command": ..., "args": {...}}`. The parent must therefore unwrap the
# nested command — the concrete form of the "admission identity needs canonical resolution" review finding.
LONG_OP_FAMILIES = {
    "compile-creatio": "compile", "compile-status": "compile",
    "restart-creatio": "restart", "restart-status": "restart",
    "install-process-builder": "process-builder",
    "create-app-section": "app-section",
}
STARTERS = {"compile-creatio", "restart-creatio", "install-process-builder", "create-app-section"}
STICKY_TTL_SECONDS = 900


def canonical(params):
    """(tool name, arguments) after unwrapping a `clio-run` envelope."""
    name = params.get("name")
    args = params.get("arguments") or {}
    if name in ("clio-run", "clio-run-destructive"):
        inner = args.get("command") or (args.get("args") or {}).get("command")
        if inner:
            return inner, (args.get("args") or {})
    return name, (args.get("args") if isinstance(args.get("args"), dict) else args)


def environment_of(args):
    return (args or {}).get("environment-name") or (args or {}).get("environmentName") or "-"


class Proxy:
    def __init__(self, clio, budget):
        self.cmd = ["dotnet", clio, "mcp-server"]
        self.env = dict(os.environ)
        # An ordinary child must not inherit a read-deadline override: the parent enforces the budget by
        # killing it. A sticky (long-operation) child is the opposite case — it MUST keep clio's own
        # response deadline, because the in-progress envelope is what lets the call return while the work
        # continues in that process. Prototype finding: stripping it made a 25 s backend call block for 77 s.
        self.env.pop("CLIO_MCP_READ_DEADLINE_SECONDS", None)
        self.sticky_env = dict(self.env)
        self.env.pop("CLIO_MCP_RESPONSE_DEADLINE_SECONDS", None)
        self.budget = budget
        self.out_lock = threading.Lock()
        self.catalog = None
        self.sticky = {}                       # (env, family) -> {"child":…, "expires":…}
        self.sticky_lock = threading.Lock()

    def sticky_child(self, key, create):
        """Fetch (or create) the child that owns a long operation for this (environment, family)."""
        with self.sticky_lock:
            entry = self.sticky.get(key)
            now = time.monotonic()
            if entry and entry["expires"] > now and entry["child"].p.poll() is None:
                entry["expires"] = now + STICKY_TTL_SECONDS
                return entry["child"], False
            if entry:
                entry["child"].kill()          # expired or dead: reap before replacing
                self.sticky.pop(key, None)
            if not create:
                return None, False
            child = self.spawn(sticky=True)
            self.sticky[key] = {"child": child, "expires": now + STICKY_TTL_SECONDS}
            return child, True

    def reap_sticky(self, key):
        with self.sticky_lock:
            entry = self.sticky.pop(key, None)
        if entry:
            entry["child"].kill()
            log(f"reaped sticky child {key}")

    def spawn(self, sticky=False):
        c = Child(self.cmd, self.sticky_env if sticky else self.env)
        c.request("initialize", {"protocolVersion": "2024-11-05", "capabilities": {},
                                 "clientInfo": {"name": "clio-proxy", "version": "1"}}, 60)
        c.notify("notifications/initialized", {})
        return c

    def emit(self, msg):
        with self.out_lock:
            sys.stdout.write(json.dumps(msg) + "\n")
            sys.stdout.flush()

    def tools_list(self):
        """One catalog child, used once, then discarded. The real implementation would keep the
        catalog in the parent (it touches no environment)."""
        if self.catalog is None:
            c = self.spawn()
            resp = c.request("tools/list", {}, 60)
            c.kill()
            self.catalog = (resp or {}).get("result", {"tools": []})
        return self.catalog

    def handle_call(self, req):
        """One tool call = one child = one Creatio session = one killable failure domain.

        Exception: a tracked long operation gets a STICKY child that outlives the response, because its
        progress registry lives in that process. Its budget is also different — the tool has its own
        in-progress contract, so the parent must not kill it at the ordinary short budget."""
        started = time.monotonic()
        tool, args = canonical(req.get("params") or {})
        family = LONG_OP_FAMILIES.get(tool)
        key = (environment_of(args), family) if family else None
        sticky = False

        if family:
            child, created = self.sticky_child(key, create=tool in STARTERS)
            if child is None:                    # a status poll with no tracked operation: one-shot child
                child = self.spawn()
            else:
                sticky = True
                log(f"routed {tool} to {'new' if created else 'existing'} sticky child {key}")
            budget = self.budget * 20            # long operations own their own in-progress contract
        else:
            child = self.spawn()
            budget = self.budget

        spawn_cost = time.monotonic() - started
        remaining = max(1.0, budget - spawn_cost)
        resp = child.request("tools/call", req.get("params"), remaining)
        for n in child.notifications:                        # forward progress upstream
            self.emit(n)
        elapsed = time.monotonic() - started
        if resp is None:
            child.kill()                                     # <- the budget, enforced without consent
            if sticky:
                self.reap_sticky(key)
            log(f"killed child after {elapsed:.1f}s for {tool}")
            text = (f"MCP tool '{req.get('params', {}).get('name')}' exceeded the {self.budget:.0f}s "
                    f"budget (error-class=creatio-timeout). The worker process was terminated, so no "
                    f"work is left running and the environment is unaffected. Read-only calls are safe "
                    f"to retry; for a write, verify state before retrying.")
            self.emit({"jsonrpc": "2.0", "id": req["id"], "result": {
                "isError": True,
                "content": [{"type": "text", "text": text}],
                "structuredContent": {"success": False, "error-class": "creatio-timeout",
                                      "worker-terminated": True, "budget-seconds": self.budget}}})
            return
        terminal = sticky and self.is_terminal(tool, resp)
        if not sticky or terminal:
            child.kill()                                      # ordinary call, or the operation finished
            if terminal:
                self.reap_sticky(key)
        resp["id"] = req["id"]
        log(f"ok {tool} in {elapsed:.2f}s (spawn {spawn_cost:.2f}s, sticky={sticky}, terminal={terminal})")
        self.emit(resp)

    @staticmethod
    def is_terminal(tool, resp):
        """A status poll that reports a finished operation lets the parent release the sticky child."""
        if tool not in ("compile-status", "restart-status"):
            return False
        blocks = ((resp.get("result") or {}).get("content") or [])
        text = " ".join(b.get("text", "") for b in blocks if isinstance(b, dict)) or json.dumps(resp)
        return any(f'"status":"{state}"' in text for state in ("succeeded", "failed", "not-found"))

    def serve(self):
        for line in sys.stdin:
            line = line.strip()
            if not line.startswith("{"):
                continue
            req = json.loads(line)
            method, rid = req.get("method"), req.get("id")
            if rid is None:
                continue                                      # client notification: nothing to route
            if method == "initialize":
                self.emit({"jsonrpc": "2.0", "id": rid, "result": {
                    "protocolVersion": "2024-11-05",
                    "capabilities": {"tools": {}},
                    "serverInfo": {"name": "clio-proxy", "version": "prototype"}}})
            elif method == "tools/list":
                self.emit({"jsonrpc": "2.0", "id": rid, "result": self.tools_list()})
            elif method == "tools/call":
                threading.Thread(target=self.handle_call, args=(req,), daemon=True).start()
            else:
                self.emit({"jsonrpc": "2.0", "id": rid, "result": {}})

if __name__ == "__main__":
    ap = argparse.ArgumentParser()
    ap.add_argument("--clio", default="/Users/a.kravchuk/Projects/clio/clio/bin/Debug/net8.0/clio.dll")
    ap.add_argument("--budget", type=float, default=12.0)
    a = ap.parse_args()
    Proxy(a.clio, a.budget).serve()
