# Creatio-aware three-way merge lab

## Purpose

Provide a reusable acceptance lab for the test plan. The lab proves the packaged MCP behavior with
real Git commits and real Creatio-authored EntitySchema changes. It is not part of the MCP runtime:
the merge tool remains an inline-content, preview-only operation and never manages Git or lab state.

The normative assertions live in
`creatio-three-way-merge-test-plan.md`. This document is the repeatable operator procedure.

## Checked-in lab artifacts

Keep the harness deliberately small: this runbook, the normative test plan, and a sanitized receipt
for each accepted run. The first run is documented in
`creatio-three-way-merge-first-run.md`.

Do not add an agent launcher, YAML orchestration framework, one script per phase, or another MCP
tool. Add a guarded script only after a command sequence has been repeated manually and omitting the
script has caused a real rerun problem. The live lab itself is retained outside the product
repository so the two Creatio instances and three Git workspaces can be reused.

## Fixed roles

Use three independent Git clones attached to named branches. Do not use detached HEADs: the two
pretend-human actors must create named, reproducible commits.

| Role | Clone and branch | Creatio access | Git identity |
|---|---|---|---|
| `main-merge-agent` | integration clone, `main` | none while merging; instance A only for final verification | lab merge agent |
| `developer-a` | clone A, `developer-a` | instance A only | lab developer A |
| `developer-b` | clone B, `developer-b` | instance B only | lab developer B |

Set Git names and emails in each clone, never globally. Give each role a separate run-scoped clio
configuration directory containing only its allowed environment alias. This is operational
isolation, not a security boundary.

## Run record

Before mutation, create a sanitized run record outside all three clones. It contains no credentials
or connection strings. Record:

- run ID and UTC start time;
- absolute clone roots, branch names, Git identities, and starting HEADs;
- clio package/version and executable path;
- environment aliases, normalized host names, and Creatio versions;
- disposable package name/UId and EntitySchema name/UId;
- B0 archive path and SHA-256;
- every command result needed by the evidence receipt.

The run must fail before mutation unless A and B resolve to different hosts, report the same Creatio
version, all three clones are clean, and all branches start at the recorded B0 commit.

Write run state and machine evidence beneath the configured lab root, outside the three clones.
Every mutating action validates its target against that state first.

## Setup

1. Create or select one disposable Git origin.
2. Create the integration clone and the two developer clones under one run-scoped lab root.
3. Create `main`, `developer-a`, and `developer-b`; verify all three initially point to B0.
4. Configure the three workspace-scoped Git identities.
5. Configure role-specific clio homes. A must be absent from developer B's catalog and B must be
   absent from developer A's catalog.
6. In instance A create `UsrMergeProof` and `UsrMergeProofEntity` once, export and commit B0.
7. Install the exact B0 archive into instance B, unlock it, and verify package and schema UIds match
   instance A.
8. Persist the sanitized run record before either developer change.

Never create the base EntitySchema independently in both instances.

## Run

Execute the six phases in `creatio-three-way-merge-test-plan.md` in order. Stop after each phase and
record its verified postconditions before continuing.

Developer A adds `UsrDeveloperAText` through instance A, synchronizes, and authors A1. Developer B
adds `UsrDeveloperBNumber` through instance B, synchronizes, and authors B1. No package artifact is
hand-edited to manufacture the conflict.

The main merge agent fetches A1 and B1, creates the real Git conflict, extracts stages 1/2/3, and
invokes the packaged MCP server with inline content. Immediately before and after the MCP call,
capture:

- `git status --porcelain=v2`;
- `git ls-files -u`;
- working-tree and index hashes for every conflicted artifact;
- the three input blob SHAs;
- MCP status, artifact kind, diagnostics, and output SHA-256.

The before/after Git and file evidence must be identical. Only after proving MCP purity may the main
merge agent write and stage a `resolved` result.

## Support-boundary checks

Inspect `tools/list` and prove the resident description compactly names the supported and not-yet-
implemented artifact types. Inspect the curated tool contract for the exact normative matrix.

Run at least these packaged-MCP controls:

| Input | Required response |
|---|---|
| EntitySchema metadata conflict | semantic outcome; `resolved` content only when verified and marker-free |
| ProcessSchema metadata | `status=not-implemented`; diagnostic `Merge for process-schema-metadata is not implemented yet.`; no content |
| ProcessSchema resource | `status=not-implemented`; type-specific `not implemented yet` diagnostic; no content |
| C# or SQL | `status=not-implemented`; type-specific `not implemented yet` diagnostic; no content |
| Unknown schema manager | `status=unsupported`; clear diagnostic; no content |

For every non-resolved result, prove the MCP call changed neither clone files nor Git index state.

## Evidence

Produce one sanitized JSON receipt with the fields required by the test plan. Add:

- the `tools/list` support-description assertion;
- before/after MCP purity hashes;
- every `not-implemented` status and diagnostic;
- phase pass/fail state and exact failing assertion;
- reset and final-cleanup results.

Do not store secrets, full environment configuration, access tokens, or connection strings. A run
is accepted only when the receipt proves the Git merge, Creatio install/compile/read-back, record
round-trip, support boundary, MCP purity, and cleanup state.

## Reset for rerun

Reset is state-bound and fail closed. It may touch only the three recorded clone roots, recorded
branches, exact disposable package UId/name, and recorded test records.

1. Refuse reset if a path, branch, package, schema, or environment alias differs from the run record.
2. Preserve the receipt from the completed or failed run.
3. Return all clones to clean B0-based branches.
4. Remove the disposable package from both instances, then reinstall the recorded B0 archive.
5. Verify both instances again expose only the base column with the recorded UIds.
6. Start a new receipt for the rerun.

Do not uninstall either Creatio instance and do not use force deletion as recovery.

## Final cleanup

1. Verify A1, B1, the merge commit, and the final receipt are retained where intended.
2. Remove only the recorded test records and disposable package from instances A and B.
3. Remove only the recorded lab branches and clones after proving their intended commits are
   retained.
4. Verify no recorded package, schema, record, branch, or clone remains unintentionally.
5. Append the cleanup result to the receipt before removing disposable run state.

If any identity check fails, stop and report the exact mismatch. Do not broaden the cleanup target.
