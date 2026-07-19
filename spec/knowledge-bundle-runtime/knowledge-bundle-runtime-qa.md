# Persistent knowledge installation QA plan

## Unit cases

- **TC-U-01:** Missing `knowledge-root-path` resolves to `<clio-home>/knowledge` and is persisted.
- **TC-U-02:** Configured absolute root is preserved; relative, filesystem-root, and unsafe roots
  fail before mutation.
- **TC-U-03:** Install stages, verifies, extracts, writes provenance, publishes the immutable
  version, then atomically commits the marker.
- **TC-U-04:** Same-version install is idempotent; an existing different active version directs the
  caller to update.
- **TC-U-05:** Update selects only a higher stable package, preserves the previous identity, and
  prunes only versions no longer referenced by active/previous.
- **TC-U-06:** Transport, signature, compatibility, digest, path, bounds, or extraction failure
  leaves the marker and existing version directories unchanged.
- **TC-U-07:** Concurrent process mutation is serialized by `knowledge.lock`; bounded lock timeout
  returns a human-readable failure.
- **TC-U-08:** A stale staging directory is removed only while holding the lock and cannot redirect
  deletion outside the root.
- **TC-U-08a:** A non-empty root without the Clio ownership sentinel is never adopted or recursively
  cleaned; symbolic links and junctions in managed paths fail closed.
- **TC-U-08b:** A stale update cannot publish after a concurrent delete or marker change.
- **TC-U-09:** Info reports installed, missing, valid, invalid, update-available, up-to-date, and
  update-unknown states without exposing trust material.
- **TC-U-10:** Delete without explicit consent has no side effects; confirmed delete removes only
  managed children and retains the configured root setting.
- **TC-U-11:** Runtime loads the active disk bundle, falls back to previous on cold active failure,
  deactivates after marker deletion, and returns typed unavailable when neither verifies.
- **TC-U-12:** Marker identity unchanged avoids bundle re-read; changed marker verifies a complete
  candidate before atomic swap.
- **TC-U-13:** Failed hot reload retains the prior in-memory snapshot and records a safe diagnostic.
- **TC-U-13a:** An unchanged marker is retried after a transient read or activation failure.
- **TC-U-13b:** A damaged same-version materialization is rebuilt from the verified archive before
  install/update reports success.
- **TC-U-14:** CLI option mapping, exit codes, logging, and DI registration are covered for all four
  commands; MCP tool review confirms lifecycle commands remain an explicit local CLI surface.

## Real-process MCP E2E cases

- **TC-E-01:** Start with an isolated `CLIO_HOME`, install synthetic package v1, and assert visible
  settings/root/version artifacts plus successful synthetic guidance retrieval through both
  `get-guidance` and MCP `resources/read`; all external resource URIs remain advertised.
- **TC-E-02:** Start a second real MCP process against the same home with the fake feed offline and
  prove it serves v1 from disk without a network request. With no installation, external resources
  remain advertised while `resources/read` fails with typed `guidance-unavailable`.
- **TC-E-03:** Keep one MCP process running, update v1 to synthetic v2 through a separate Clio
  process, and prove the same MCP process serves v2 on its next request.
- **TC-E-04:** Offer invalid v3, prove update fails, and prove the same MCP process and a fresh MCP
  process retain v2.
- **TC-E-05:** Invoke `info-knowledge` as a separate CLI process and assert local version/path plus
  remote update state using mechanics-only metadata.
- **TC-E-06:** Invoke CLI deletion with explicit consent in the isolated home and prove the running
  MCP process becomes unavailable on the next read without contacting NuGet. The fail-closed
  unconfirmed path remains a command unit test so E2E never waits for terminal input.

## Content ownership rule

Fixtures synthesize a complete stable route catalog with arbitrary deterministic article bytes.
Tests may assert identities, versions, digests, paths, state transitions, request counts, error
codes, and message types. They must not assert canonical guidance wording, headings, examples, or
real `clio-knowledge` snapshots.

## Required validation

- Targeted `Command` and `McpServer` unit filters under every affected target framework.
- Focused `clio.mcp.e2e` knowledge fixtures on .NET 8 and .NET 10.
- ClioRing compatibility inspection and tests if `clio-run` output or error envelopes change.
- Comprehensive pre-push and final agentic review over the complete PR diff.

## Validation result (2026-07-19)

- Focused knowledge unit suite: 45 passed on .NET 8 and 45 passed on .NET 10.
- Full unit suite: 6,739 passed and 25 skipped on each of .NET 8 and .NET 10.
- Mechanics-only knowledge MCP E2E: 3 passed on .NET 8 and 3 passed on .NET 10.
- Comprehensive correctness, security, and maintainability review: no Blocker or High findings
  remain.
- ClioRing compatibility reviewed: no Ring-consumed contract changed in `clio-ring/ClioRing.Ipc`,
  `clio-ring/ClioRing`, or `clio-ring/ClioRing.Desktop`.
