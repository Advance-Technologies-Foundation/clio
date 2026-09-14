# Resolve SystemValue column defaults on read-back

Issue: #1363. Status: done.
Design: [Native read-back enrichment](../adr/adr-system-value-readback.md).

As a caller, I can identify a configured system value using its stable GUID and
native caption, or inspect an explicit resolution failure, without querying SysValue.

Acceptance:

- Both package-scoped and merged reads enrich supported SystemValue column types.
- Canonical source fields remain unchanged; Const lookup enrichment is preserved.
- Expected catalog failures retain the original metadata with an honest marker.
- CLI output, MCP contract/prompt and command documentation explain the additive fields.
- Unit regressions and real MCP tests pass against a disposable environment.
- Agentic review and the PR delivery gates complete before merge.
