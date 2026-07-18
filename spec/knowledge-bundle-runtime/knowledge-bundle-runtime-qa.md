# Knowledge bundle runtime QA

## Focused unit matrix

- Valid signed bundle activates from cold state.
- Frozen ESQ articles match the expected URI and byte content.
- Tampered manifest signature rejects and retains active.
- Unknown/wrong key rejects and retains active.
- Missing, duplicate, unexpected, truncated, traversal, length-mismatched, and digest-mismatched
  entries reject and retain active.
- Incompatible Clio or MCP-tool-contract range rejects and retains active.
- Equal/lower sequence rejects and retains active; higher sequence activates.
- Cold lookup is `unavailable`; active missing article is `not-found`.

## Later MCP integration matrix

Once `get-guidance` is wired to the runtime, extend `clio.mcp.e2e` to assert the unchanged article
response and the typed unavailable diagnostic through the real stdio MCP process.
