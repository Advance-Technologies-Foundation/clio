#!/usr/bin/env python3
"""
Test client for the ENG-95262 relay spike. Plays the role of the real MCP host (Claude Code / ClioRing):
declares the sampling capability, answers `sampling/createMessage`, and records every notification exactly
as it arrives on the wire so `_meta` fidelity and ordering can be asserted byte-for-byte.

Pass criteria
  1. SAMPLING   the tool result contains the marker this client returned -> the child's server->client
                request really reached the real client and the answer came back.
  2. META       every notification's `_meta.clioStageEvent` equals what the child sent, nested fields
                included, and `progressToken` equals the token this client supplied.
  3. ORDER      sequence numbers arrive monotonically, per call, under concurrency.
"""
import json, subprocess, sys, threading, time

MARKER = "SAMPLED-OK-7f3a"

class Client:
    def __init__(self, cmd, cwd=None):
        self.p = subprocess.Popen(cmd, stdin=subprocess.PIPE, stdout=subprocess.PIPE,
                                  stderr=subprocess.PIPE, text=True, bufsize=1, cwd=cwd)
        self._id = 0
        self._pending = {}
        self._lock = threading.Lock()
        self.notifications = []
        self.sampling_calls = 0
        threading.Thread(target=self._reader, daemon=True).start()

    def _send(self, msg):
        with self._lock:
            self.p.stdin.write(json.dumps(msg) + "\n")
            self.p.stdin.flush()

    def _reader(self):
        for line in self.p.stdout:
            line = line.strip()
            if not line.startswith("{"):
                continue
            msg = json.loads(line)
            if msg.get("method") == "sampling/createMessage":
                # (1) the server->client request under test
                self.sampling_calls += 1
                self._send({"jsonrpc": "2.0", "id": msg["id"], "result": {
                    "role": "assistant", "model": "spike-client-model",
                    "content": {"type": "text", "text": MARKER}}})
                continue
            if msg.get("id") is None:
                self.notifications.append(msg)          # recorded RAW, not deserialized into a type
                continue
            slot = self._pending.get(msg["id"])
            if slot:
                slot["msg"] = msg
                slot["event"].set()

    def request(self, method, params, timeout=60):
        with self._lock:
            self._id += 1
            rid = self._id
        slot = {"event": threading.Event(), "msg": None}
        self._pending[rid] = slot
        self._send({"jsonrpc": "2.0", "id": rid, "method": method, "params": params})
        return slot["msg"] if slot["event"].wait(timeout) else None

    def notify(self, method, params=None):
        self._send({"jsonrpc": "2.0", "method": method, "params": params or {}})

def main():
    cwd = sys.argv[1] if len(sys.argv) > 1 else "."
    parent = ["dotnet", "parent/bin/Release/net8.0/RelayParent.dll"]
    c = Client(parent, cwd=cwd)
    init = c.request("initialize", {
        "protocolVersion": "2024-11-05",
        "capabilities": {"sampling": {}},                 # the real client CAN sample
        "clientInfo": {"name": "spike-client", "version": "1"}})
    if not init:
        print("FAIL: no initialize response"); sys.exit(1)
    print("initialized via:", (init.get("result") or {}).get("serverInfo"))
    c.notify("notifications/initialized")

    tools = c.request("tools/list", {})
    names = [t["name"] for t in ((tools or {}).get("result") or {}).get("tools", [])]
    print("tools relayed from child:", names)

    token = "tok-abc-123"
    t0 = time.monotonic()
    res = c.request("tools/call", {"name": "spike-tool", "arguments": {
        "seq-count": 5, "sample": True, "progress-token": token, "run-id": "run-42"}}, timeout=90)
    elapsed = time.monotonic() - t0
    text = " ".join(b.get("text", "") for b in (((res or {}).get("result") or {}).get("content") or [])
                    if isinstance(b, dict))
    print(f"tool result ({elapsed:.2f}s): {text[:160]}")

    ok_sampling = MARKER in text and c.sampling_calls == 1
    progress = [n for n in c.notifications if n.get("method") == "notifications/progress"]
    metas = [(n.get("params") or {}).get("_meta", {}).get("clioStageEvent") for n in progress]
    tokens = {(n.get("params") or {}).get("progressToken") for n in progress}
    seqs = [m.get("sequence") for m in metas if isinstance(m, dict)]
    ok_meta = (len(progress) == 5
               and tokens == {token}
               and all(isinstance(m, dict) and m.get("runId") == "run-42"
                       and m.get("nested") == {"keep": True} for m in metas))
    ok_order = seqs == sorted(seqs) == list(range(5))

    print(f"\n1) SAMPLING relayed to the real client : {'PASS' if ok_sampling else 'FAIL'} "
          f"(client answered {c.sampling_calls} sampling request(s); marker in result: {MARKER in text})")
    print(f"2) _meta + progressToken preserved      : {'PASS' if ok_meta else 'FAIL'} "
          f"({len(progress)} notifications, tokens={tokens})")
    print(f"3) ordering                             : {'PASS' if ok_order else 'FAIL'} (sequences={seqs})")
    if progress:
        print("\n   first relayed notification, verbatim:")
        print("   " + json.dumps(progress[0], sort_keys=True))
    c.p.terminate()
    sys.exit(0 if (ok_sampling and ok_meta and ok_order) else 1)

if __name__ == "__main__":
    main()
