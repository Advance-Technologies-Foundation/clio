"""One-shot clio MCP stdio driver: python mcpdrv.py <tool> <args-json | @file>.

Spawns the worktree build's `clio mcp-server`, calls a lazy tool through clio-run, prints the result.
For describe-business-process, `--params <ElementName>` prints only that element's parameters.
"""
import json
import queue
import subprocess
import sys
import threading
import time

import os
# The clio build whose BUNDLED CrtProcessBuilder matches the stand, or clio refuses the call on a version floor.
# Default: this repository's Release build, run from the repository root. Override with CLIO_DLL.
DLL = os.environ.get("CLIO_DLL", "clio/bin/Release/net10.0/clio.dll")


def run(tool, tool_args, timeout=300):
    proc = subprocess.Popen(["dotnet", DLL, "mcp-server"], stdin=subprocess.PIPE, stdout=subprocess.PIPE,
                            stderr=subprocess.DEVNULL, text=True, encoding="utf-8", bufsize=1)
    q = queue.Queue()
    threading.Thread(target=lambda: [q.put(l) for l in iter(proc.stdout.readline, "")], daemon=True).start()
    ids = iter(range(1, 100))

    def send(method, params=None, notify=False):
        msg = {"jsonrpc": "2.0", "method": method}
        if params is not None:
            msg["params"] = params
        mid = None
        if not notify:
            mid = next(ids)
            msg["id"] = mid
        proc.stdin.write(json.dumps(msg) + "\n")
        proc.stdin.flush()
        return mid

    def wait(mid):
        end = time.time() + timeout
        while time.time() < end:
            try:
                line = q.get(timeout=1).strip()
            except queue.Empty:
                continue
            if not line:
                continue
            try:
                obj = json.loads(line)
            except ValueError:
                continue
            if obj.get("id") == mid:
                return obj
        raise TimeoutError(f"no response for {mid}")

    try:
        wait(send("initialize", {"protocolVersion": "2025-06-18", "capabilities": {},
                                 "clientInfo": {"name": "caption-probe", "version": "0.1"}}))
        send("notifications/initialized", notify=True)
        resp = wait(send("tools/call", {"name": "clio-run",
                                        "arguments": {"args": {"command": tool, "args": tool_args}}}))
    finally:
        try:
            proc.stdin.close()
        except OSError:
            pass
        proc.kill()
    return resp


def texts(resp):
    result = resp.get("result") or {}
    out = []
    for c in result.get("content", []):
        if c.get("type") == "text":
            out.append(c["text"])
    if "error" in resp:
        out.append(json.dumps(resp["error"]))
    return out


def describe_graph(resp):
    for t in texts(resp):
        try:
            obj = json.loads(t)
        except ValueError:
            continue
        stack = [obj]
        while stack:
            cur = stack.pop()
            if isinstance(cur, dict):
                if "elements" in cur and isinstance(cur["elements"], list):
                    return cur
                v = cur.get("value")
                if isinstance(v, str) and v.lstrip().startswith("{"):
                    try:
                        stack.append(json.loads(v))
                    except ValueError:
                        pass
                stack.extend(x for x in cur.values() if isinstance(x, (dict, list)))
            elif isinstance(cur, list):
                stack.extend(cur)
    return None


if __name__ == "__main__":
    tool = sys.argv[1]
    raw = sys.argv[2]
    if raw.startswith("@"):
        raw = open(raw[1:], encoding="utf-8").read()
    args = json.loads(raw)
    resp = run(tool, args)
    if len(sys.argv) > 4 and sys.argv[3] == "--params":
        graph = describe_graph(resp)
        if graph is None:
            print("\n".join(texts(resp)))
            sys.exit(1)
        for el in graph["elements"]:
            if el.get("name") == sys.argv[4]:
                sub = el.get("subProcess") or {}
                print("inSync:", sub.get("inSync"), "| callee:", sub.get("processName"))
                for p in el.get("parameters", []):
                    print(json.dumps({k: p.get(k) for k in ("name", "caption", "uid", "direction", "source", "value")},
                                     ensure_ascii=False))
    else:
        print("\n".join(texts(resp)))
