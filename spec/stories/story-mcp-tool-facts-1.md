# Each MCP tool says what it does

Status: review
Spec: [spec-mcp-tool-facts.md](../prd/spec-mcp-tool-facts.md)
Design: [adr-mcp-tool-facts.md](../adr/adr-mcp-tool-facts.md)
Test plan: [tp-mcp-tool-facts.md](../test-plans/tp-mcp-tool-facts.md)

As an MCP host that runs clio for an agent, I can tell from clio's own output what any call does (read, change or
destroy; whether it rebuilds the environment; whether it reaches this machine; whether it touches clio's
connections), so I keep no list of clio tool names and a new clio tool works the day it ships.

Acceptance:
- Every enabled MCP tool declares `[McpToolFacts]` with all four facts, reviewed against its code.
- Composite tools and flags declare `[McpToolArgumentFacts]` for the values that narrow them; `manage-role`
  `action=list` is a read.
- A resident tool's facts are in its `_meta["clio/facts"]` in `tools/list`.
- `get-tool-contract` with `detail=facts` answers every served tool (and alias) without `tool-names`, the named
  ones with them, misses in `not-found`.
- The line index, the `index-entries` objects and the contracts are unchanged; every payload budget holds.
- `readOnlyHint` / `destructiveHint` agree with the facts; a hint that under-reported a destroy is corrected.
- The coverage test fails for a tool without facts, with a contradiction, or with an entry wider than the tool,
  and proves it over synthetic tools.
- Unit tests and the real stdio server (`clio.mcp.e2e`, no environment) pass.
