# Story: Install and hot-reload external knowledge

- **Status:** Review

As a Clio and Clio MCP user, I want verified knowledge installed at a visible local path so repeated
MCP sessions work without repeated downloads, coding agents can inspect the underlying files, and
an already-running MCP process observes updates without restart.

## Acceptance criteria

1. Clio persists an absent `knowledge-root-path` as the absolute `<clio-home>/knowledge` default.
2. `install-knowledge` publishes a fully verified immutable version and atomic active marker.
3. `install-knowledge` is idempotent and does not silently update an existing installation.
4. `update-knowledge` activates only a newer verified package and retains the prior version.
5. `info-knowledge` reports local paths, versions, provenance, timestamps, validation, and bounded
   update availability without treating transport failure as up-to-date.
6. `delete-knowledge` requires explicit destructive consent and cannot delete outside the managed
   root; the appsettings pointer remains.
7. MCP is a disk-only consumer and performs no NuGet request, including when the cache is absent.
8. An external update becomes visible through the same running MCP process on its next guidance
   access; no restart is required.
9. In-flight reads see one complete immutable version, never a partial or mixed generation.
10. Invalid updates, marker tampering, failed reloads, interrupted staging, and concurrent mutation
    retain or recover the last verified content.
11. A cold MCP process with no installed cache remains operational and returns typed unavailable
    guidance until `install-knowledge` succeeds.
12. Unit and real-process E2E tests use generated synthetic packages and assert mechanics only.
13. CLI help, detailed docs, command index, and MCP cache-loading documentation describe the
    implemented behavior consistently.
