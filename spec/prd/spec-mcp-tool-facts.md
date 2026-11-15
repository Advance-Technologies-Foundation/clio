# MCP tool facts: each tool says what it does

- Problem: an MCP host that runs clio for an agent decides per call whether to let it through, ask the person or
  refuse it. clio says only `readOnlyHint` / `destructiveHint` (and `(R)` / `(D)` in the `get-tool-contract` index).
  `(D)` marks `create-app` and `create-lookup` the same as `delete-app` and `execute-sql-script`, and a composite
  tool such as `manage-role` is one name for a read (`action: list`) and a delete. So hosts keep their own lists of
  clio tool names (read verbs, machine-wide tools, connection tools), and every clio release can make them wrong.
- Outcome: every MCP tool, resident or reached through `clio-run`, declares four facts, and the argument values
  that narrow them. A host classifies any call of any current tool from clio's own output, with no tool name in
  its code.
- Facts: `effect` (read | change | destroy), `rebuilds-environment`, `scope` (local | environment | machine) and
  `connections` (none | read | change); `by-argument` entries for values that narrow them (`manage-role`
  `action=list` is a read). The tool-level facts are the widest the tool does.
- Where a host reads them: a resident tool's `_meta["clio/facts"]` in `tools/list`; `get-tool-contract` with
  `detail=facts`, every tool (and deprecated alias) without `tool-names`, the named ones with them.
- Contract: additive. The standard hints keep their values, `(R)` / `(D)` keep their meaning, the line index,
  the `index-entries` objects (ClioRing) and the contracts are unchanged, so no byte budget moves. An older clio
  answers `detail=facts` with the index, which has no `facts`.
- Guard: a coverage test fails when a tool ships without its facts, when they contradict its standard hints, or
  when an argument entry names an argument the tool does not take or is wider than the tool.
- Out of scope here: which arguments name a file or folder on the host (a separate change), and a guide's
  minimum clio version (a knowledge-library change).
- Proof: unit tests over the real catalog and over synthetic tools; `tools/list` and `get-tool-contract` read
  through the real `clio mcp-server` in `clio.mcp.e2e`.
- Design: [adr-mcp-tool-facts.md](../adr/adr-mcp-tool-facts.md).
