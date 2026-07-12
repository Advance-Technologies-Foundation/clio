# Story mcp-progress-heartbeat-3 — E2E progress coverage + docs/capability map

- **Feature:** `mcp-progress-heartbeat` · **Jira:** ENG-91274 · **ADR:** `spec/adr/adr-mcp-progress-heartbeat.md`
- **Status:** ready-for-dev
- **Depends on:** story-mcp-progress-heartbeat-2

## Story
As a maintainer, I want end-to-end proof that progress notifications flow through the real
`clio mcp-server` process, and documentation that matches the new contract.

## Acceptance criteria
1. `McpServerSession.CallToolAsync` gains an overload accepting
   `IProgress<ProgressNotificationValue>` so tests can observe progress.
2. A `clio.mcp.e2e` test asserts that invoking a long-running application tool yields ≥1
   progress notification (the timeout/poll path), gated on a reachable sandbox like the
   existing destructive section tests.
3. `docs/McpCapabilityMap.md` documents the section tools and the long-running/progress
   behavior of the application family.
4. Affected `clio/docs/commands/*.md` + `clio/help/en/*.txt` (for verbs backing the tools, if
   any) reflect the progress/await note, or "docs reviewed, no update required" is recorded.

## Definition of Done
- [ ] E2E harness overload + TC-E2E-1 added.
- [ ] Capability map + command docs updated (or explicitly confirmed unchanged).
- [ ] Workspace diary entry appended.
