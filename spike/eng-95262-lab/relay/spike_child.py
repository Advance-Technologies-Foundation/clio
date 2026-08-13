#!/usr/bin/env python3
"""
Synthetic MCP child for the ENG-95262 relay spike.

Stands in for a `clio mcp-server` worker and exercises exactly the three relay properties that the
Python proxy prototype could not test, because the risk lives in the C# SDK, not in clio's business logic:

  1. a server->client REQUEST (`sampling/createMessage`) issued from inside a tool call — this is what
     `update-page` / `sync-pages` do via `server.SampleAsync`, and what silently degrades to
     `Skipped=true` if the parent does not relay it to the REAL client;
  2. notifications carrying `_meta.clioStageEvent` and a progress token that must arrive byte-identical
     (ClioRing correlates on the exact token and buffers by (runId, sequence));
  3. notification ORDER under concurrency — sequence numbers must arrive monotonically per call.

Tool: `spike-tool` {"seq-count": n, "sample": true|false, "progress-token": <token>}
Returns text describing what the sampling round-trip produced, so the client can assert it really ran.
"""
import json, sys, threading

OUT_LOCK = threading.Lock()
PENDING = {}
NEXT_ID = [1000]

def emit(msg):
    with OUT_LOCK:
        sys.stdout.write(json.dumps(msg) + "\n")
        sys.stdout.flush()

def request(method, params, timeout=30):
    with OUT_LOCK:
        NEXT_ID[0] += 1
        rid = NEXT_ID[0]
    slot = {"event": threading.Event(), "msg": None}
    PENDING[rid] = slot
    emit({"jsonrpc": "2.0", "id": rid, "method": method, "params": params})
    return slot["msg"] if slot["event"].wait(timeout) else None

def handle_call(req):
    args = ((req.get("params") or {}).get("arguments") or {})
    seq_count = int(args.get("seq-count", 3))
    token = args.get("progress-token")
    run_id = args.get("run-id", "run-1")

    # (2) + (3): ordered notifications carrying the exact progress token and a nested _meta block
    for i in range(seq_count):
        emit({"jsonrpc": "2.0", "method": "notifications/progress", "params": {
            "progressToken": token, "progress": i + 1, "total": seq_count,
            "message": f"stage {i + 1}",
            "_meta": {"clioStageEvent": {"runId": run_id, "sequence": i,
                                         "stage": f"s{i}", "nested": {"keep": True}}}}})

    sampled = "not-requested"
    if args.get("sample", True):
        # (1) the server->client request the whole architecture depends on
        resp = request("sampling/createMessage", {
            "messages": [{"role": "user", "content": {"type": "text", "text": "relay probe"}}],
            "maxTokens": 16})
        if resp is None:
            sampled = "NO-RESPONSE"
        elif "error" in resp:
            sampled = "ERROR:" + json.dumps(resp["error"])[:120]
        else:
            content = ((resp.get("result") or {}).get("content") or {})
            sampled = content.get("text", json.dumps(resp.get("result"))[:120])

    emit({"jsonrpc": "2.0", "id": req["id"], "result": {
        "content": [{"type": "text", "text": json.dumps({"sampled": sampled, "notifications": seq_count})}]}})

def main():
    for line in sys.stdin:
        line = line.strip()
        if not line.startswith("{"):
            continue
        msg = json.loads(line)
        if "result" in msg or "error" in msg:          # a response to OUR request (sampling)
            slot = PENDING.pop(msg.get("id"), None)
            if slot:
                slot["msg"] = msg
                slot["event"].set()
            continue
        method, rid = msg.get("method"), msg.get("id")
        if rid is None:
            continue
        if method == "initialize":
            emit({"jsonrpc": "2.0", "id": rid, "result": {
                "protocolVersion": "2024-11-05",
                "capabilities": {"tools": {}},
                "serverInfo": {"name": "spike-child", "version": "1"}}})
        elif method == "tools/list":
            emit({"jsonrpc": "2.0", "id": rid, "result": {"tools": [{
                "name": "spike-tool",
                "description": "relay probe: emits ordered _meta notifications and requests sampling",
                "inputSchema": {"type": "object", "properties": {
                    "seq-count": {"type": "integer"}, "sample": {"type": "boolean"},
                    "progress-token": {}, "run-id": {"type": "string"}}}}]}})
        elif method == "tools/call":
            threading.Thread(target=handle_call, args=(msg,), daemon=True).start()
        else:
            emit({"jsonrpc": "2.0", "id": rid, "result": {}})

if __name__ == "__main__":
    main()
