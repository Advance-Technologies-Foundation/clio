"""Capture a read-only, commit-pinned CLI inventory; never count stubs as completed ports."""
import argparse
import json
import pathlib
import re
import subprocess

parser = argparse.ArgumentParser()
parser.add_argument("source", type=pathlib.Path)
parser.add_argument("--ref", default="HEAD")
args = parser.parse_args()
root = pathlib.Path(__file__).resolve().parents[1]

def git(*command):
    return subprocess.check_output(["git", "-C", str(args.source), *command], encoding="utf-8")

commit = git("rev-parse", args.ref).strip()
matches = git("grep", "-l", r"\[Verb(", commit, "--", "clio/*.cs").splitlines()
entries = []
for item in sorted(matches):
    path = item.split(":", 1)[1]
    content = git("show", f"{commit}:{path}")
    for match in re.finditer(r'^[ \t]*\[Verb\(\s*"([^"\r\n]+)"(.*?)\)\]', content, re.S | re.M):
        name, tail = match.groups()
        alias = re.search(r"Aliases\s*=\s*(?:new\s*(?:string)?\[\]\s*)?[\[{](.*?)[\]}]", tail, re.S)
        entries.append({"command": name, "aliases": re.findall(r'"([^"\r\n]+)"', alias[1]) if alias else [],
                        "source": path, "line": content.count("\n", 0, match.start()) + 1,
                        "hidden": bool(re.search(r"Hidden\s*=\s*true", tail)), "status": "pending"})
destination = root / "docs" / "porting" / "legacy-cli-inventory.json"
destination.parent.mkdir(parents=True, exist_ok=True)
if destination.exists():
    previous = {x["command"]: x for x in json.loads(destination.read_text(encoding="utf-8"))["commands"]}
    for entry in entries:
        for key in ("status", "operation", "notes"):
            if key in previous.get(entry["command"], {}):
                entry[key] = previous[entry["command"]][key]
destination.write_text(json.dumps({"sourceCommit": commit,
    "scope": "CLI declarations, including hidden verbs; not proof of Program registration or MCP-only parity",
    "commands": sorted(entries, key=lambda x: x["command"])}, indent=2) + "\n", encoding="utf-8")
print(f"Inventoried {len(entries)} CLI declarations at {commit}; {destination}")

# MCP has operations without CLI verbs. Inventory declarations separately from residency.
tool_entries = []
for item in git("grep", "-l", r"\[McpServerTool", commit, "--", "clio/Command/McpServer/Tools/*.cs").splitlines():
    path = item.split(":", 1)[1]
    content = git("show", f"{commit}:{path}")
    constants = dict(re.findall(r'const\s+string\s+(\w+)\s*=\s*"([^"\r\n]+)"', content))
    for match in re.finditer(r'^[ \t]*\[McpServerTool\s*(?:\((.*?)\))?\]', content, re.S | re.M):
        name = re.search(r'\bName\s*=\s*("[^"\r\n]+"|[\w.]+)', match[1] or "")
        expression = name[1] if name else None
        resolved = expression.strip('"') if expression and expression.startswith('"') else constants.get(expression)
        tool_entries.append({"tool": resolved, "nameExpression": expression, "source": path,
                             "line": content.count("\n", 0, match.start()) + 1, "status": "pending"})
tool_destination = destination.with_name("legacy-mcp-inventory.json")
tool_destination.write_text(json.dumps({"sourceCommit": commit,
    "scope": "MCP attribute declarations; includes resident and bridged tools. Null names need manual resolution. Not a runtime catalog or parity claim.",
    "tools": sorted(tool_entries, key=lambda entry: (entry["tool"] or "", entry["source"]))}, indent=2) + "\n", encoding="utf-8")
print(f"Inventoried {len(tool_entries)} MCP declarations; {sum(entry['tool'] is None for entry in tool_entries)} names need manual resolution.")
