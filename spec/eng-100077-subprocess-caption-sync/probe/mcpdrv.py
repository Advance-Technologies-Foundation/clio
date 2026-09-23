"""One-shot clio MCP stdio driver: python mcpdrv.py <tool> <args-json | @file>.

Spawns the worktree build's `clio mcp-server`, calls a lazy tool through clio-run, prints the result.
For describe-business-process, `--params <ElementName>` prints only that element's parameters.
"""
import json
import os
import queue
import subprocess
import sys
import threading
import time

# The clio build whose BUNDLED CrtProcessBuilder matches the stand, or clio refuses the call on a version floor.
# Default: this repository's Release build, run from the repository root. Override with CLIO_DLL.
DLL = os.environ.get("CLIO_DLL", "clio/bin/Release/net10.0/clio.dll")


def parse_json(text):
    """The JSON value in `text`, or None when it is not JSON."""
    try:
        return json.loads(text)
    except ValueError:
        return None


class McpSession:
    """A `clio mcp-server` child process spoken to over stdio JSON-RPC."""

    def __init__(self, timeout):
        self._timeout = timeout
        self._ids = iter(range(1, 100))
        self._lines = queue.Queue()
        self._proc = subprocess.Popen(["dotnet", DLL, "mcp-server"], stdin=subprocess.PIPE, stdout=subprocess.PIPE,
                                      stderr=subprocess.DEVNULL, text=True, encoding="utf-8", bufsize=1)
        threading.Thread(target=self._pump, daemon=True).start()

    def _pump(self):
        for line in iter(self._proc.stdout.readline, ""):
            self._lines.put(line)

    def send(self, method, params=None, notify=False):
        message = {"jsonrpc": "2.0", "method": method}
        if params is not None:
            message["params"] = params
        message_id = None if notify else next(self._ids)
        if message_id is not None:
            message["id"] = message_id
        self._proc.stdin.write(json.dumps(message) + "\n")
        self._proc.stdin.flush()
        return message_id

    def _next_message(self):
        """The next JSON message on stdout, or None when a second passed without one."""
        try:
            line = self._lines.get(timeout=1).strip()
        except queue.Empty:
            return None
        return parse_json(line) if line else None

    def wait(self, message_id):
        end = time.time() + self._timeout
        while time.time() < end:
            message = self._next_message()
            if isinstance(message, dict) and message.get("id") == message_id:
                return message
        raise TimeoutError(f"no response for {message_id}")

    def close(self):
        try:
            self._proc.stdin.close()
        except OSError:
            pass
        self._proc.kill()


def run(tool, tool_args, timeout=300):
    session = McpSession(timeout)
    try:
        session.wait(session.send("initialize", {"protocolVersion": "2025-06-18", "capabilities": {},
                                                 "clientInfo": {"name": "caption-probe", "version": "0.1"}}))
        session.send("notifications/initialized", notify=True)
        return session.wait(session.send("tools/call", {"name": "clio-run",
                                                        "arguments": {"args": {"command": tool, "args": tool_args}}}))
    finally:
        session.close()


def texts(resp):
    result = resp.get("result") or {}
    out = [content["text"] for content in result.get("content", []) if content.get("type") == "text"]
    if "error" in resp:
        out.append(json.dumps(resp["error"]))
    return out


def children(node):
    """What to search next under `node`: its nested containers, plus a JSON document carried as a string value."""
    if isinstance(node, list):
        return list(node)
    found = []
    embedded = node.get("value")
    if isinstance(embedded, str) and embedded.lstrip().startswith("{"):
        parsed = parse_json(embedded)
        if parsed is not None:
            found.append(parsed)
    found.extend(value for value in node.values() if isinstance(value, (dict, list)))
    return found


def is_graph(node):
    return isinstance(node, dict) and isinstance(node.get("elements"), list)


def find_graph(root):
    """The first node under `root` that carries an `elements` list - the described process graph."""
    stack = [root]
    while stack:
        node = stack.pop()
        if is_graph(node):
            return node
        if isinstance(node, (dict, list)):
            stack.extend(children(node))
    return None


def describe_graph(resp):
    for text in texts(resp):
        root = parse_json(text)
        graph = find_graph(root) if root is not None else None
        if graph is not None:
            return graph
    return None


def print_parameters(graph, element_name):
    for element in graph["elements"]:
        if element.get("name") != element_name:
            continue
        sub = element.get("subProcess") or {}
        print("inSync:", sub.get("inSync"), "| callee:", sub.get("process"))
        for parameter in element.get("parameters", []):
            print(json.dumps({key: parameter.get(key) for key in ("name", "caption", "uid", "direction", "source",
                                                                  "value")}, ensure_ascii=False))


def main(argv):
    tool = argv[1]
    raw = argv[2]
    if raw.startswith("@"):
        with open(raw[1:], encoding="utf-8") as source:
            raw = source.read()
    resp = run(tool, json.loads(raw))
    if len(argv) > 4 and argv[3] == "--params":
        graph = describe_graph(resp)
        if graph is None:
            print("\n".join(texts(resp)))
            return 1
        print_parameters(graph, argv[4])
        return 0
    print("\n".join(texts(resp)))
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv))
