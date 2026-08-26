# get-info cliogate diagnostics - SPEC

> GitHub: [#1138](https://github.com/Advance-Technologies-Foundation/clio/issues/1138)

## Problem

`get-info` uses package-version metadata as a precondition for calling cliogate `GetSysInfo`.
The shared package checker intentionally returns the lowest version across the `cliogate` and
`cliogate_netcore` aliases, so stale metadata for an inactive alias can veto a working endpoint.
The resulting warning falsely reports cliogate as absent or incompatible.

## Requirements

R1. Treat a successful `GetSysInfo` response as the authoritative capability result.

R2. Consult installed-version metadata only after `GetSysInfo` is unavailable, and distinguish
not installed, below minimum, installed but unreadable, and version-detection failure.

R3. Name the detected installed version in below-minimum and installed-but-unreadable warnings.

R4. Preserve the base report, optional-enrichment semantics, command arguments, and exit codes.

R5. Keep CLI documentation, MCP contract, published guidance, and ClioRing consumption aligned.

## Acceptance criteria

- AC1. A working `GetSysInfo` endpoint contributes its fields and emits no warning even when
  package metadata would classify an alias below 2.0.0.32.
- AC2. A failed probe with detected 2.0.0.45 reports that version and an access/read failure, not
  that cliogate is absent.
- AC3. A failed probe with lowest detected alias 2.0.0.31 reports that alias version and the
  2.0.0.32 floor without claiming it is the active runtime package.
- AC4. An absent package or failed version lookup preserves the base report and produces an
  accurate, secret-safe warning.
- AC5. Command, MCP, E2E, ClioRing, NativeAOT, docs, and guidance checks pass.

## Exclusions

- Do not change the shared fail-closed alias policy used by hard package requirements.
- Do not add a new endpoint, retry protocol, report field, or MCP argument.
