# Clio guidance development framework test plan

## Static and unit validation

- Validate the canonical, Claude, and Codex skill folders with the skill validator.
- Assert redirect frontmatter/path and canonical metadata cannot drift.
- Assert guide names, URIs, catalog entries, routing rows, cross-links, safety rules, and verification boundaries.
- Assert `create-entity-schema`, `sync-schemas`, and their canonical contracts route `is-virtual` callers to `virtual-entities`.

## MCP end-to-end validation

- Start the real clio MCP process for .NET 8 and .NET 10.
- Retrieve the ESQ family and virtual-entity guides through both resource reads and `get-guidance`.
- Retrieve virtual-schema tool contracts and assert decision-point routing.

## Compatibility and review

- Run the complete MCP unit module on both target frameworks.
- Run ClioRing tests, the read-only MCP IPC proof, and Windows x64 NativeAOT publish.
- Run comprehensive quality, correctness, security, and performance review; no Blocker/High finding may remain.

## Recorded result

- MCP unit module: 2,429 passed per framework.
- Focused MCP E2E: 6 passed per framework.
- ClioRing: 152 tests passed; IPC proof passed; Windows x64 NativeAOT publish passed.
