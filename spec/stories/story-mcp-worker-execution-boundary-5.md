# Story 5: HTTP credential channel + per-client sticky isolation

**Feature**: mcp-worker-execution-boundary
**Jira**: [ENG-95262](https://creatio.atlassian.net/browse/ENG-95262)
**ADR**: [adr-mcp-worker-execution-boundary.md](../adr/adr-mcp-worker-execution-boundary.md)
**Test plan**: [tp-mcp-worker-execution-boundary.md](../test-plans/tp-mcp-worker-execution-boundary.md)
**Stage**: 5
**Status**: ready-for-dev
**Size**: L

## As a
caller authenticated to `mcp-http`

## I want
my credentials to reach a worker without ever touching a command line or a tool argument, and my sticky worker to be unreachable by anyone else

## So that
moving execution into a process does not become a way to read or borrow someone's session

## Design
- Governed by `mcp-worker-execution-boundary-credential-threat-model.md`; requirements R-1…R-7 are this story's contract.
- Channel: inherited handle or pipe written after spawn. **Never a command line** — `/proc/<pid>/cmdline` and `ps` make that world-readable to any local user (T-1). Pipe preferred over an environment variable: env blocks are inherited by grandchildren and appear in some crash dumps.
- **Sticky scope key = authenticated session/principal + normalised target + credential fingerprint** (rule 3). Environment-only scoping is a cross-client boundary violation, and status tools are credential-scoped today, so it would also regress shipped behaviour. Fingerprint follows `BuildPassthroughCacheKey` (`ToolCommandResolver.cs:316`) — **full** SHA-256, not a truncation, because "same url, different token" is the norm here.
- Worker lookup **fails closed**: unmatched key ⇒ new worker, never a closest match.
- **Target normalisation is conservative and explicit** (T-5): a documented, tested equivalence list. Over-normalising merges two targets and carries one caller's credentials to another's. When in doubt, spawn another worker — 0.7 s against a credential crossover.
- Existing smuggling rejection (`ToolCommandResolver.cs:104-116`) must keep holding: the routing key comes from resolved tenant identity, never raw tool arguments (T-2).

## Acceptance Criteria
- [ ] AC-01 — Scope key includes all three components; omitting any one fails the test (TC-U-501).
- [ ] AC-02 — Lookup fails closed (TC-U-502).
- [ ] AC-03 — Normalisation equivalence table tested **both directions** (TC-U-503).
- [ ] AC-04 — Smuggling rejections hold with routing on; the router rejects rather than routes (TC-U-504).
- [ ] AC-05 — Two concurrent callers, same environment, different principals ⇒ two distinct workers, each observing its own identity at the Creatio end (TC-E-501).
- [ ] AC-06 — Redaction: a secret marker appears nowhere in parent output, error envelopes, or worker-stderr passthrough (TC-U-505).

## Tests
Unit TC-U-501…505; E2E TC-E-501. **Full unit suite required.**
