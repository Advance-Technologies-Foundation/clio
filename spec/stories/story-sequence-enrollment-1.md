# Native sequence enrollment CLI and MCP

Issue: clio#1576. Status: validated; delivery pending.

- [x] Bound and validate explicit UUID inputs before network access.
- [x] Submit through the native route once without replay.
- [x] Preserve native counts, partial failures, and uncertain completion.
- [x] Return bounded readback and explicit readback failures.
- [x] Align CLI docs, MCP annotations, discovery and route tests.
- [x] Validate unit, MCP and live success/duplicate/inactive/capacity paths; exercise failure contracts.
