# ADR: sync-schemas create-lookup resume — verify against intent

**Status**: Proposed
**Author**: Architect Agent
**PRD**: [prd-sync-schemas-verify-resume.md](../prd/prd-sync-schemas-verify-resume.md)
**Created**: 2026-07-20
**Jira**: ENG-93374
**stepsCompleted**: [1, 2, 3, 4]

---

## Context

The `sync-schemas` MCP tool's `ExecuteCreateSchema` (`clio/Command/McpServer/Tools/SchemaSyncTool.cs`, L295–373) added an idempotent-resume branch under ENG-93374 (PR #910). At L326–342, when a create-lookup returns a non-zero exit with no thrown exception AND a same-named schema is found in the **target** package, the branch forces `execution = new OperationExecution(0, …)` and reports `success:true, status:"completed"`. That single signal ("same-named schema exists in my package") conflates two realities: a genuine resume (the schema was really created with the requested columns, only the response was lost) and a durable collision (the schema pre-existed for an unrelated reason and the requested columns were never applied). In the durable case the branch silently drops the requested columns — e.g. creating `UsrColors` with a new `UsrHexCode` column against an already-existing `UsrColors` reports "completed" while `UsrHexCode` was never applied. A design decision is needed for how to distinguish the two before forcing success, and how to behave when that distinction cannot be established.

This is a fix to existing, unreleased code inside PR #910, not a greenfield feature. There is no new CLI verb or flag — the change is internal MCP-tool result semantics.

## Decision

Gate the same-target-package resume on **verifying the requested columns against the schema's actual columns** (hybrid B+C — verify-against-intent). Inside the existing `isLookup` + same-package sub-branch:

1. **Empty `op.Columns`** → resume exactly as today (no read, no extra round-trip). (FR-01)
2. **Non-empty `op.Columns`** → issue exactly one read-only round-trip that reuses the resolver to read the existing schema's columns (`GetEntitySchemaPropertiesCommand.GetSchemaProperties`), then route on **read-success vs read-failure**:
   - **Read succeeds, every requested column present** (case-insensitive subset) → legitimate resume → `success:true`, `status:"completed"`. (FR-04)
   - **Read succeeds, ≥1 requested column absent** → durable collision → leave the create failure intact → `success:false` with the existing "schema already exists — use update-entity to add columns" hint; registration is NOT invoked. (FR-03)
   - **Read fails (throws, or exit≠0), for any cause** → cannot verify → degrade to the new distinct status `resumed-existing`, `success:true`, carrying a warning that the requested columns were **NOT** verified. (FR-05)

The `success`/`status` value is always derived from the **final, post-registration** execution: `resumed-existing` sticks only when the whole op (including the outstanding lookup registration) reaches exit 0; if registration then fails, the op is `failed`.

**Discriminator rationale (why read-success/read-failure, not the transient axis):** a "column missing" signal can only come from a *successful* read that returns the column list; a probe fault never yields a missing-column answer, only "no answer." Routing every read failure to `resumed-existing` therefore cannot swallow a real missing-column signal (satisfies Assumption A-02 by construction) and is a superset of FR-05's "transient" case. `TransientNetworkFailureClassifier` is used only to enrich the warning *text* ("transient network fault" vs "could not read the existing schema"), never to route. A probe failure is **never** routed to `success:false`: that path asserts columns are missing, which a read failure does not establish, and doing so would reintroduce the ENG-93374 resume-loop.

## Alternatives Considered

| Option | Pros | Cons | Status |
|--------|------|------|--------|
| **A — gate on the create's final transient classification** (resume only when the last failure was classified transient) | Reuses the existing `TransientNetworkFailureClassifier`; zero extra round-trips | The transient/durable classification describes the *failure mode*, not *server state*. A durable collision can surface with a transient-looking error, and a lost-response resume is indistinguishable from a durable failure at the transport layer — the exact conflation the PRD problem statement names. Cannot tell whether *your* columns landed. | Rejected: classifies the transport, not the applied state |
| **A′ — gate on `Attempts > 1`** (resume only when the create was retried) | Trivial; no extra round-trip | Retry count only means the create ran under transient conditions; it says nothing about whether attempt 1 applied the schema+columns. Durable collisions also trigger retries; a lost-response resume can occur on a single-attempt timeout. Neither confirms nor denies column application. | Rejected: retry count is orthogonal to server state |
| **B+C — verify-against-intent** (read actual columns, compare to requested intent; degrade to `resumed-existing` when the read cannot run) | Directly answers "did my columns land?"; honest `success:false` only on a *confirmed* durable collision; never fabricates verified success when the read fails; additive `resumed-existing` status | One extra read-only round-trip on the same-package non-empty-columns collision path (bounded, under the existing lock) | **Chosen** |

Rejected because out of scope (Non-goals): convergent "ensure" semantics (read → apply delta → verify) — tracked in **ENG-93807**; and auto-applying columns / `update-entity` on a durable collision — the tool returns the hint and lets the caller decide.

## Implementation Plan

> Project rule (`project-context.md`): **no MediatR** — this stays inside the existing `Command<TOptions>` + service pattern. No `Request`/`Handler` files.

### Files to create

| File | Purpose |
|------|---------|
| _(none)_ | The change is a private method + result-classification tweak inside `SchemaSyncTool.cs`; tests extend the existing fixture. No new production or test files. |

### Files to modify

| File | Change description |
|------|-------------------|
| `clio/Command/McpServer/Tools/SchemaSyncTool.cs` | Rework the same-package resume sub-branch in `ExecuteCreateSchema` (L326–342); add a private `VerifyRequestedColumns` helper + a small result enum/record; thread a `forcedStatus` through `FinalizeResult`; make `Classify` preserve a pre-set status; fix resume-path messages (FR-06). |
| `clio.tests/Command/McpServer/SchemaSyncToolTests.cs` | Add unit tests for AC-01…AC-06, AC-ERR and the FR-09/AC-05 different-package regression; add a `GetEntitySchemaPropertiesCommand` test seam (real command + substituted `IRemoteEntitySchemaColumnManager`). |
| `docs/McpCapabilityMap.md` | Document the caller-visible `resumed-existing` status on `sync-schemas` (additive). |

### Key interfaces / contracts

```csharp
// Read path — reuse the resolver, exactly as FindEntitySchemaCommand is used for the collision probe.
// GetEntitySchemaPropertiesCommand.GetSchemaProperties(options) returns EntitySchemaPropertiesInfo
// with IReadOnlyList<EntitySchemaPropertyColumnInfo>? Columns (each has string Name).
// Requested column identity comes from CreateEntitySchemaColumnArgs.ResolveName().

private enum ColumnVerification { Verified, Missing, Unverified }

// One read-only round-trip; NOT wrapped in RunAttempts (single probe, like TryGetCollisionInfo).
// Reads the merged/effective schema (no --package filter): lookup columns are own-to-target-package,
// so name-presence in the merged view is equivalent to the target-package layer, and merged is simpler.
private (ColumnVerification Outcome, IReadOnlyList<string> Missing, Exception? ProbeFault)
    VerifyRequestedColumns(SchemaSyncOperation op, SchemaSyncArgs args) {
    IReadOnlyList<string> requested = (op.Columns ?? [])
        .Select(c => c.ResolveName())
        .Where(n => !string.IsNullOrWhiteSpace(n))
        .Select(n => n!)
        .ToList();
    if (requested.Count == 0) {
        return (ColumnVerification.Verified, [], null); // FR-01 fast-path (caller checks emptiness first)
    }
    try {
        GetEntitySchemaPropertiesOptions readOptions = new() {
            Environment = args.EnvironmentName, SchemaName = op.SchemaName
        };
        EntitySchemaPropertiesInfo props = commandResolver
            .Resolve<GetEntitySchemaPropertiesCommand>(readOptions)
            .GetSchemaProperties(readOptions);
        var existing = new HashSet<string>(
            (props.Columns ?? []).Select(c => c.Name), StringComparer.OrdinalIgnoreCase); // FR-08
        IReadOnlyList<string> missing = requested
            .Where(r => !existing.Contains(r)).ToList();
        return missing.Count == 0
            ? (ColumnVerification.Verified, [], null)   // FR-04
            : (ColumnVerification.Missing, missing, null); // FR-03
    } catch (Exception ex) when (!McpExceptionPolicy.IsUnrecoverable(ex)) {
        return (ColumnVerification.Unverified, [], ex); // FR-05 — read-failure degrade
    }
}
```

Resume sub-branch shape (replacing L330–342), collision already probed once and reused:

```csharp
if (collision is not null
    && string.Equals(collision.ExistingPackageName, args.PackageName, StringComparison.OrdinalIgnoreCase)) {
    bool hasColumns = op.Columns?.Any() == true;
    ColumnVerification outcome = ColumnVerification.Verified; // empty-columns → resume as today (FR-01)
    IReadOnlyList<string> missing = [];
    Exception? probeFault = null;
    if (hasColumns) {
        (outcome, missing, probeFault) = VerifyRequestedColumns(op, args);
    }
    switch (outcome) {
        case ColumnVerification.Verified:
            // FR-04/FR-01: fresh info note ONLY — drop the failed create's Error lines (FR-06)
            execution = new OperationExecution(0, null,
                [new InfoMessage($"sync-schemas: '{op.SchemaName}' already exists in package '{args.PackageName}'; skipping re-create and completing lookup registration.")],
                createExecution.Attempts);
            schemaApplied = true;
            break;
        case ColumnVerification.Unverified:
            // FR-05: success-with-warning, distinct status; fresh warning ONLY (FR-06)
            execution = new OperationExecution(0, null,
                [new WarningMessage($"sync-schemas: '{op.SchemaName}' already exists in package '{args.PackageName}' but the requested columns could NOT be verified ({DescribeProbeFault(probeFault)}); completing registration, but the requested columns are NOT confirmed present — verify with get-entity-schema-properties or resubmit.")],
                createExecution.Attempts);
            schemaApplied = true;
            resumedExisting = true; // → forcedStatus "resumed-existing" in FinalizeResult
            break;
        case ColumnVerification.Missing:
            // FR-03: do NOT force success. Leave schemaApplied=false; the normal failure path
            // returns success:false + the same-package "use update-entity to add columns" hint.
            // (Optional) append a message naming the missing columns for actionability.
            break;
    }
}
```

`FinalizeResult` gains `string? forcedStatus = null`; it sets `result.Status = success ? (forcedStatus ?? "completed") : "failed"` so `resumed-existing` survives only on a truly-successful (post-registration) op. `Classify` changes from an unconditional assignment to `result.Status ??= result.Success ? "completed" : "failed"` so the forced status is preserved. `DescribeProbeFault` returns `"transient network fault"` when `TransientNetworkFailureClassifier.IsTransient(probeFault)` else `"the existing schema could not be read"` (text-only use of the classifier).

### CLI flag specification

| Flag | Type | Required | Description |
|------|------|----------|-------------|
| _(none)_ | — | — | No new or modified CLI flags. Internal MCP-tool behavior only (all existing `sync-schemas` flags remain kebab-case; CLIO001 unaffected). |

### Test strategy

| Layer | Framework | What to cover | File |
|-------|----------|--------------|------|
| Unit | NUnit 4.5.1 + FluentAssertions + NSubstitute | AC-01 empty-columns resume; AC-02 all-present resume; AC-03 missing-column → `success:false` + "use update-entity" hint + `DidNotReceive().EnsureLookupRegistration`; AC-04 read throws → `status:"resumed-existing"`, `success:true`, "NOT verified" warning; AC-06 resumed/forced-success result has no Error-level messages from the failed create; AC-ERR non-collision failure unchanged; **FR-09/AC-05** different-package collision → create invoked once, `Success==false`, `DidNotReceive().EnsureLookupRegistration(...)` | `clio.tests/Command/McpServer/SchemaSyncToolTests.cs` |
| Integration | — | None required — the behavior is exercised through the resolver seam with substitutes; no new file/DB/IIS surface. | — |
| E2E | clio.mcp.e2e | Optional manual scenario mirroring AC-03/AC-04 against a real environment in `clio.mcp.e2e/SchemaSyncToolE2ETests.cs`. **MCP E2E is not in CI — manual execution only.** | `clio.mcp.e2e/SchemaSyncToolE2ETests.cs` |

**Test seam (no production `virtual` needed):** resolve a real `GetEntitySchemaPropertiesCommand(stubManager, logger)` where `stubManager` is `Substitute.For<IRemoteEntitySchemaColumnManager>()`; `stubManager.GetSchemaProperties(Arg.Any<GetEntitySchemaPropertiesOptions>()).Returns(info)` for the read-success cases and `.Throws(new HttpRequestException(...))` for the read-failure/AC-04 case. This mirrors the existing `FakeFindEntitySchemaCommand` pattern with zero production-shape changes.

**FR-09 note:** the existing `SchemaSync_CreateLookup_Should_Include_CollisionInfo_When_Schema_Exists_In_Different_Package` test only asserts collision-info is populated; it does NOT pin the guard behavior. Add a dedicated regression asserting create-invoked-once + `DidNotReceive().EnsureLookupRegistration` + `Success==false` for the different-package case, so a future refactor cannot silently start skipping create for foreign-package collisions.

## Consequences

- **Positive**: restores honest `success:false` on a durable collision (SM-01) so silently-dropped columns can no longer masquerade as "completed"; preserves the ENG-93374 interrupted-resume fix for the empty-columns and all-present cases; never fabricates verified success when the read cannot run (SM-02, `resumed-existing` + warning); keeps result `Messages` consistent with status (SM-03, no failed-attempt Error lines in a success result).
- **Trade-offs**: one extra read-only round-trip on the same-package non-empty-columns collision path (bounded, single attempt, under the existing per-tenant lock; OQ-03). `resumed-existing` is a new status value consumers must tolerate (additive, A-04). Verification is presence-by-name only — a column that exists with the wrong type/length still counts as present (OQ-02; type/shape convergence is ENG-93807, out of scope).
- **Breaking change**: **No.** The behavior change lives entirely inside unreleased PR #910; the new `resumed-existing` status is additive and no CLI surface changes. `docs/McpCapabilityMap.md` is updated to document the status (not RELEASE.md — nothing shipped changes).

### Open-question resolutions

- **OQ-01** — `resumed-existing` is `success:true` + warning + distinct status, not `success:false`. Justification: failing an interrupted resume on an unverifiable-but-existing schema would regress the ENG-93374 resume fix and risk a resume loop (Goal-1 counter). "Not blind success" is carried by the distinct **status** and the explicit **warning**, per AC-04, while the `success` bool preserves resume.
- **OQ-02** — presence-by-name, case-insensitive, subset check (extra existing columns allowed); per-column type/length comparison is out of scope for Level 1.
- **OQ-03** — one additional read-only round-trip, taken only on a same-package collision with non-empty columns, under the same per-tenant lock; the collision probe remains the single reused `FindEntitySchema` call (no second probe).

## Pre-implementation Checklist

- [x] No new CLI options (none added; existing remain kebab-case — CLIO001 unaffected)
- [x] No MediatR — change stays in the existing `SchemaSyncTool` / resolver + service pattern (project-context rule)
- [x] Error/warning messages are user-friendly strings (resume note + "columns NOT verified" warning)
- [x] Existing tests identified that may be affected: `SchemaSync_CreateLookup_Should_Complete_Registration_When_Schema_Already_Exists_In_Target_Package` (must keep passing — it uses empty columns, hits the FR-01 fast-path), and `..._Exists_In_Different_Package` (unchanged; FR-07 guard preserved)
- [x] MCP tool behavior change documented in `docs/McpCapabilityMap.md` (`resumed-existing` status)
- [x] `resumed-existing` status derived from the final post-registration execution (never sticks on a later registration failure)
