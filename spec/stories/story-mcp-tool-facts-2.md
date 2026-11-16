# Each MCP tool says which arguments are paths on this machine

Status: review
Spec: [spec-mcp-tool-facts.md](../prd/spec-mcp-tool-facts.md)
Design: [adr-mcp-tool-facts.md](../adr/adr-mcp-tool-facts.md) (D8)
Test plan: [tp-mcp-tool-facts.md](../test-plans/tp-mcp-tool-facts.md)

As an MCP host that keeps its agent inside one folder, I can tell from clio's own output which arguments of a call
name a file or folder on the machine clio runs on, and whether clio reads or writes there, so I check exactly those
values and stop guessing from argument names.

Acceptance:
- Every argument whose value directs clio to a file or folder on this machine is under `host-paths` in the tool's
  entry in `Command/McpServer/Data/mcp-tool-facts.json`, with `kind` and `access`, reviewed against the code;
  `list` and `or` are set where they apply.
- Every argument whose name reads like a path and is not one is under `not-host-paths` with what it is instead.
- Both are published in `tools/list` `_meta["clio/facts"]` and by `get-tool-contract` `detail=facts`, only for a
  tool that has them; a tool with neither publishes what it published before.
- The coverage test fails for a path-like argument declared in neither list, a declaration on an argument the tool
  does not take, a doubled or contradictory one, a host path without a kind or an access, an unknown `or`, and a
  look-alike without its reason; and proves each over synthetic tools.
- The index, the `index-entries` objects and the contracts are unchanged; every payload budget holds.
