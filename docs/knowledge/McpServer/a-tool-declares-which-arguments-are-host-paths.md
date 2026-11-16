---
description: a tool's entry in Command/McpServer/Data/mcp-tool-facts.json lists under host-paths every argument whose value is a file or folder on the machine clio runs on (kind file/folder/file-or-folder, access read/write/read-write, list, or name/url) and under not-host-paths every argument that only reads like a path, with why; a name joined into a path is validated as a plain name in the command, never declared instead
applies-to:
  - clio/Command/McpServer/McpToolFacts.cs
  - clio/Command/McpServer/McpToolFactsReader.cs
  - clio/Command/McpServer/Data/mcp-tool-facts.json
  - clio.tests/Command/McpServer/McpToolFactsCoverageTests.cs
date: 2026-11-15
---

**What is true** — next to its four facts, a tool's catalog entry may carry `host-paths` and `not-host-paths`.
`host-paths` names every argument whose value directs clio to a file or folder on this machine (a full path, or
one relative to clio's working folder), with its `kind` and the widest `access` clio has there; `list` marks a
value that holds several paths, `or` a value that may instead be a registered name or a URL. `not-host-paths`
names every argument that reads like a path and is not one here, with what it is instead. Both are published with
the other facts, in `tools/list` `_meta["clio/facts"]` and by `get-tool-contract` `detail=facts`.

**Why it is this way** — clio's MCP server runs as the person, outside the sandbox a host keeps its agent in, so
an `output-file` lets the agent write where the sandbox would not. A host keeps such a path inside its folder,
and for that it must know which arguments are paths. Guessing from the name is wrong both ways: `set-logo`'s
`logo` is a local image, `get-package-file`'s `file-path` is a path on the environment, `ignore-profile` is a
flag.

**What breaks if you ignore it** — `McpToolFactsCoverageTests` fails for an argument whose name reads like a path
and is in neither list. It cannot catch a path under another name: read the command and declare it. Do not
declare a name that the command joins into a path (a package name, a schema name): validate it as a plain name
there, as `DataBindingPathGuard.RequirePlainName` does, or `..` leaves the folder and the declaration only
documents the hole. Where clio falls back to when a path argument is omitted is not declared. Design:
`spec/adr/adr-mcp-tool-facts.md`, D8.
