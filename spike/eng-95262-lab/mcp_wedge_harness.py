#!/usr/bin/env python3
"""
ENG-95262 acceptance harness — reproduces the tenant wedge through the REAL `clio mcp-server`.

The claim under test: when one environment-scoped tool call stalls, the per-tenant monitor is retained by
the abandoned work, so every LATER call for that environment dies at the read deadline *without ever
issuing an HTTP request*. The stub's request counter is what makes that falsifiable — a call that never
increments `select` never reached the network.

Sequence
  1. point a registered environment at the stub, stub in global stall mode
  2. call A  (list-packages)  -> stalls; returns only when its read deadline elapses
  3. call B  (list-packages, issued 1.5 s after A) -> if it dies at the deadline having sent NO request,
     the wedge is confirmed
  4. call C  (issued after A and B have both returned) -> if it also dies, the wedge is PERMANENT
  5. finally, un-stall the stub and call D -> shows whether the environment ever recovers

Run with a short deadline so the whole thing takes a minute:
  CLIO_MCP_READ_DEADLINE_SECONDS=15 python3 mcp_wedge_harness.py --env stubwedge
"""
import argparse, json, os, subprocess, sys, threading, time, urllib.request

STUB = "http://127.0.0.1:8099"

def stub(path):
    with urllib.request.urlopen(urllib.request.Request(STUB + path, method="POST" if path != "/counters" else "GET"),
                                timeout=10) as r:
        return json.loads(r.read().decode())

class McpStdio:
    """Minimal MCP stdio client: enough to initialize and issue concurrent tools/call requests."""

    def __init__(self, cmd, env):
        self.p = subprocess.Popen(cmd, stdin=subprocess.PIPE, stdout=subprocess.PIPE,
                                  stderr=subprocess.DEVNULL, env=env, text=True, bufsize=1)
        self._id = 0
        self._pending = {}
        self._lock = threading.Lock()
        threading.Thread(target=self._reader, daemon=True).start()

    def _reader(self):
        for line in self.p.stdout:
            line = line.strip()
            if not line or not line.startswith("{"):
                continue
            try:
                msg = json.loads(line)
            except json.JSONDecodeError:
                continue
            rid = msg.get("id")
            if rid is None:
                continue                      # notification (progress/log) — not needed here
            with self._lock:
                slot = self._pending.get(rid)
            if slot:
                slot["result"] = msg
                slot["event"].set()

    def send(self, method, params=None, notify=False):
        with self._lock:
            self._id += 1
            rid = self._id
        msg = {"jsonrpc": "2.0", "method": method}
        if params is not None:
            msg["params"] = params
        if not notify:
            msg["id"] = rid
            slot = {"event": threading.Event(), "result": None}
            with self._lock:
                self._pending[rid] = slot
        self.p.stdin.write(json.dumps(msg) + "\n")
        self.p.stdin.flush()
        return None if notify else slot

    def wait(self, slot, timeout):
        return slot["result"] if slot["event"].wait(timeout) else None

    def close(self):
        try:
            self.p.terminate(); self.p.wait(timeout=10)
        except Exception:
            self.p.kill()

def call(client, tool, args, label, results, delay=0.0):
    if delay:
        time.sleep(delay)
    before = stub("/counters")["select"]
    t0 = time.monotonic()
    slot = client.send("tools/call", {"name": tool, "arguments": args})
    msg = client.wait(slot, 300)
    elapsed = time.monotonic() - t0
    after = stub("/counters")["select"]
    text = ""
    if msg:
        content = (msg.get("result") or {}).get("content") or []
        text = " ".join(b.get("text", "") for b in content if isinstance(b, dict))[:150]
        if not text:
            text = json.dumps(msg.get("error") or msg.get("result"))[:150]
    results.append({"label": label, "elapsed": round(elapsed, 1),
                    "http_requests_issued": after - before,
                    "answer": text.replace("\n", " ")})

def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--env", default="stubwedge")
    ap.add_argument("--clio", default="/Users/a.kravchuk/Projects/clio/clio/bin/Debug/net8.0/clio.dll")
    ap.add_argument("--deadline", default="15")
    ap.add_argument("--tool", default="list-packages")
    ap.add_argument("--probe-tool", default=None, help="tool for call E, to test cross-tool blast radius")
    ap.add_argument("--server", default=None, help="override server command, e.g. 'python3 mcp_proxy.py --budget 12'")
    a = ap.parse_args()

    env = dict(os.environ)
    env["CLIO_MCP_READ_DEADLINE_SECONDS"] = a.deadline
    env["CLIO_MCP_RESPONSE_DEADLINE_SECONDS"] = a.deadline

    stub("/reset")
    stub("/control?stall=true")
    print(f"stub: stall=on, counters reset; read deadline = {a.deadline}s\n")

    cmd = a.server.split() if a.server else ["dotnet", a.clio, "mcp-server"]
    client = McpStdio(cmd, env)
    init = client.wait(client.send("initialize", {
        "protocolVersion": "2024-11-05",
        "capabilities": {},
        "clientInfo": {"name": "eng95262-harness", "version": "1"}}), 120)
    if not init:
        print("initialize failed"); client.close(); sys.exit(1)
    client.send("notifications/initialized", {}, notify=True)
    print("initialized:", (init.get("result") or {}).get("serverInfo"))

    args = {"args": {"environment-name": a.env}}
    results = []

    # A and B overlap; B starts 1.5 s later so A certainly owns the monitor first.
    ta = threading.Thread(target=call, args=(client, a.tool, args, f"A {a.tool} (stalls)", results))
    tb = threading.Thread(target=call, args=(client, a.tool, args, "B same tool (+1.5s)", results, 1.5))
    tc = threading.Thread(target=call, args=(client, a.probe_tool or a.tool, args, f"E other tool {a.probe_tool or a.tool} (+2s)", results, 2.0)) if a.probe_tool else None
    ta.start(); tb.start()
    if tc: tc.start()
    ta.join(); tb.join()
    if tc: tc.join()

    # C runs strictly after A and B returned: is the environment permanently wedged?
    call(client, a.tool, args, "C (after A and B returned)", results)

    # D runs with the backend healthy again: does it recover while the abandoned work still holds the lock?
    stub("/control?stall=false")
    call(client, a.tool, args, "D (backend healthy again)", results)

    print(f"\n{'call':<28}{'elapsed':>9}{'HTTP sent':>11}  answer")
    for r in results:
        print(f"{r['label']:<28}{r['elapsed']:>8}s{r['http_requests_issued']:>11}  {r['answer'][:80]}")
    print("\nstub counters:", stub("/counters"))
    print("\nReading: a call that returns at the deadline with HTTP sent = 0 never reached the network —\n"
          "it died queued behind the abandoned work. If D also fails, the wedge is permanent.")
    client.close()

if __name__ == "__main__":
    main()
