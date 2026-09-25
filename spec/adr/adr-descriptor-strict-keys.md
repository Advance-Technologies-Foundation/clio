# ADR: Strict keys for process descriptors — where the key set comes from

**Jira**: [ENG-95244](https://creatio.atlassian.net/browse/ENG-95244), work package C of the rescope in
[comment 518188](https://creatio.atlassian.net/browse/ENG-95244?focusedCommentId=518188) (not yet agreed with
the reporter)
**Spec**: [spec-descriptor-strict-keys.md](../prd/spec-descriptor-strict-keys.md)
**Status**: Proposed
**Created**: 2026-09-25

## Context

`create-business-process`, `modify-business-process` and `modify-business-process-as-new-version` forward the
caller's JSON to CrtProcessBuilder's `ProcessDesignService` as an opaque string (`ParseDescriptor` and
`ParseOperations` only check "is a JSON object / array"). The service deserializes it into `[DataContract]`
classes, none of which implements `IExtensibleDataObject` (stated in the package's own
`Contracts/VersionContracts.cs`), so a member the contract does not declare is discarded. Matching is
case-sensitive. The result, measured on CrtProcessBuilder 1.6.6.22: every typo and every wrong-case key is
dropped in silence while the call reports success.

Refusing such a key needs the set of keys the server accepts, at every nesting level, before the POST.
That set is exactly the `[DataMember(Name = …)]` names of the write contracts. Two facts about those
contracts make the set mechanical to derive: they form a closed tree of typed classes (49 reachable from the
two write roots, 264 keys once inheritance is flattened) with no dictionary- or object-typed member, and the only inheritance is
`FilterDescriptor : FilterGroupDescriptor`.

The package is **source-only** (`adr-deliver-process-builder-package.md`): the archive clio ships,
`clio/CrtProcessBuilder/CrtProcessBuilder.gz`, contains `Files/src/cs/Contracts/*.cs` verbatim. So the
contracts clio would enforce and the bytes clio ships are the same files.

## Decision

**Option 2: the key set lives in clio as a generated JSON Schema, verified against the contract sources inside
the bundled archive.**

1. `clio/Command/ProcessModel/Schemas/process-builder-write-keys.schema.json` — a JSON Schema (draft 2020-12)
   with one `$defs` entry per reachable write contract, `additionalProperties: false`, and a `$ref` /
   `items.$ref` for every contract-typed member. Scalar members are `{}`: this schema constrains KEYS, not
   values (value types are refused loudly by the server's deserializer already). Inheritance is flattened
   into the derived definition. It is an embedded resource and names the CrtProcessBuilder version it was
   generated from.
2. A unit test (Unit lane, Module `ProcessModel`, beside `BundledProcessBuilderPackageTests` and for its
   reason) reads `Contracts/*.cs` out of the bundled archive through the production
   `ICompressionUtilities`, parses them with Roslyn (syntax only), rebuilds the schema and compares it with
   the checked-in file. A mismatch fails with the regeneration command; setting
   `CLIO_REGENERATE_PROCESS_BUILDER_KEY_SCHEMA=1` rewrites the file.
3. `IProcessDescriptorKeyValidator` walks the caller's JSON against the schema (case-sensitive) and reports
   every unknown key with its JSON path and a hint: an exact case-insensitive match first ("keys are
   case-sensitive"), then the closest key by edit distance, else the valid keys at that level.
4. `IProcessDescriptorKeyGuard` turns the report into an outcome: **refuse** before the POST when the
   environment's CrtProcessBuilder is known to be not newer than the bundled archive; **warn and send**
   when it is newer, or when either version cannot be read. The installed version is read only when an
   unknown key was found, so a clean call costs no round trip.
5. The guard runs in `CreateBusinessProcessService`, `ModifyBusinessProcessService` and
   `ModifyProcessAsNewVersionService` right after the payload is parsed — before the page-facts check, so a
   refused call touches Creatio not at all.

## Options considered (clean-slate costs)

| | Option 1 — schema generated in the package, carried by the rebundle | Option 2 — schema in clio, verified against the bundled sources |
|---|---|---|
| Source of truth | The package's `[DataContract]` classes | The same classes, read out of the archive clio ships |
| Repositories per change | package → clio-knowledge → clio, in that order | clio (plus clio-knowledge for guidance) |
| Package release | Required: a generator and drift test in the package, on BOTH active lines (`main`, `nitro/sprint-3-release`) or on whichever the next bundle is cut from | None |
| Rebundle | Required, with a version that must go UP — every environment behind the new number is refused by convergence until it re-installs | None |
| Tooling | A generator on the package side (net472 test harness, MAX_PATH-sensitive worktrees), a new archive member the rebundle script must carry and pin | A test-only Roslyn reference in `clio.tests`; `Microsoft.CodeAnalysis.CSharp` is already centrally managed |
| Drift detection | Package drift test; clio trusts whatever file the archive carries | clio's own test, run on every clio build against the exact bytes clio ships |
| What the enforced set describes | The package at its release | The package at the version clio bundles — the version convergence guarantees an environment has at least |
| Failure mode on a contract change | New member absent from clio until the next rebundle carries the regenerated file | New member fails clio's test on the rebundle that brings it; the author regenerates in the same PR |

Both options derive the set from the same classes; they differ in WHERE the derivation runs and what it
costs to move it. Option 1 buys nothing Option 2 lacks — its drift test guards the package against a file
it generates itself, while Option 2's guards clio against the bytes it actually installs — and it costs a
package release, a version bump that refuses every lagging environment, and a three-repository release
order. Option 2 is chosen.

**Hand-written allowlist in clio** (no verification) was rejected: it drifts on the first rebundle that adds a
member, and a stale allowlist REFUSES a valid key — the failure is a false refusal, the opposite of today's
silent drop, and just as invisible until someone hits it.

**Runtime JSON Schema evaluation (JsonSchema.Net)** was considered for step 3 and rejected for the runtime:
the only keyword that matters is `additionalProperties: false`, and the hint needs the allowed keys at the
failing location anyway, so an evaluator would add three runtime dependencies to clio and still need the
walk. The file stays a standard JSON Schema so another tool can consume it; the unit tests evaluate it with
JsonSchema.Net once (test-only) to prove it is one.

## Version skew

| Environment vs bundled CrtProcessBuilder | Outcome for an unknown key |
|---|---|
| Behind | Never reached — `RequiredPackageChecker` refuses the call on convergence first |
| Equal | Refused before the POST |
| Newer (a developer build pushed from a package branch) | Warning; the payload is sent unchanged |
| Either version unreadable | Warning; mirrors convergence's "cannot decide, so warn and allow" |

The two active package lines can stamp the same number on different content. An environment at the bundled
number but from the other line could accept a key clio refuses. Accepted: convergence already refuses every
number below the bundle, so an equal number is the only exposure, and it arises only for a hand-pushed
developer build.

## Consequences

- An unknown key is loud on every write path clio owns; `FlowLabelExpectation` and the other read-backs stay
  as the net for an OLDER package that lacks a key the bundle has.
- A describe read-back pasted verbatim into a write is now refused where a describe-only field
  (`resultsActivity`, `valueDisplay`, …) is present, instead of being partially dropped. The refusal names
  the path; normalizing a read-back is a non-goal.
- A rebundle that changes the write contracts fails `ProcessBuilderWriteKeySchemaTests` until the schema is
  regenerated in the same change — the forcing function that keeps the key set equal to the shipped one.
- Tool `[Description]`s do not grow (the create and modify contracts are at the payload-budget ceiling); the
  refusal message carries the explanation, and the guidance says it once.
