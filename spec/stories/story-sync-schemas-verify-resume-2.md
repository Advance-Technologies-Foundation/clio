# Story 2: Honest failure on durable collision, resumed-existing degrade, and status/message consistency

**Feature**: sync-schemas-verify-resume
**FR coverage**: FR-03, FR-05, FR-06
**PRD**: [prd-sync-schemas-verify-resume.md](../prd/prd-sync-schemas-verify-resume.md)
**ADR**: [adr-sync-schemas-verify-resume.md](../adr/adr-sync-schemas-verify-resume.md)
**Status**: ready-for-dev
**Size**: M (half day)

---

## As a

developer / CI pipeline author consuming `sync-schemas` create-lookup results

## I want

a confirmed durable collision to fail honestly with the "use update-entity" hint, an unverifiable read to degrade to a distinct `resumed-existing` status with a warning, and result `Messages` to stay consistent with the result status

## So that

silently-dropped columns can no longer masquerade as "completed", and my automation never treats an unverified state as confirmed success

---

## Acceptance Criteria

- [ ] **AC-03** — Given a create-lookup op with NON-EMPTY `op.Columns` where at least one requested column is MISSING from the existing target-package schema, when the op runs, then `VerifyRequestedColumns` returns `Missing`, the tool does NOT force success (leaves `schemaApplied = false`), returns `success:false` with the existing "schema already exists — use update-entity to add columns" hint, and the registration service is NOT invoked.
- [ ] **AC-04** — Given a create-lookup op with NON-EMPTY `op.Columns` and a same-package collision where the column-verification read THROWS (or exits ≠0) for any cause, when the op runs, then `VerifyRequestedColumns` returns `Unverified`, the result status is `resumed-existing`, `success:true`, and the result carries a WarningMessage stating the requested columns were NOT verified (never a plain verified `completed`).
- [ ] **AC-06** — Given a forced-success / resumed result (Verified, empty-columns, or Unverified), when the result is finalized, then its `Messages` contain no Error-level lines carried over from the failed create attempts — only the fresh Info note (resume) or Warning (unverified).
- [ ] **AC-04-terminal** — Given the `resumed-existing` degrade path where the subsequent lookup registration itself FAILS, when the result is finalized, then the op is `failed` and `resumed-existing` does NOT stick (status derives from the final post-registration execution).

## Implementation Notes

Key file: `clio/Command/McpServer/Tools/SchemaSyncTool.cs`. Builds on Story 1's `VerifyRequestedColumns` + resume sub-branch. Wire the remaining two outcomes and the status plumbing (ADR "Resume sub-branch shape" + the paragraph after it):

- **`Missing` (FR-03)** — do NOT force success. Leave `schemaApplied = false` so the normal failure path returns `success:false` plus the same-package "schema already exists — use update-entity to add columns" collision hint; registration is NOT invoked. Optionally append a message naming the missing columns for actionability.
- **`Unverified` (FR-05)** — success-with-warning degrade:
  ```csharp
  execution = new OperationExecution(0, null,
      [new WarningMessage($"sync-schemas: '{op.SchemaName}' already exists in package '{args.PackageName}' but the requested columns could NOT be verified ({DescribeProbeFault(probeFault)}); completing registration, but the requested columns are NOT confirmed present — verify with get-entity-schema-properties or resubmit.")],
      createExecution.Attempts);
  schemaApplied = true;
  resumedExisting = true; // → forcedStatus "resumed-existing" in FinalizeResult
  ```
- **FR-06 (message consistency)** — the Verified/empty (Story 1) and Unverified branches BOTH construct a fresh `OperationExecution` carrying ONLY the new Info/Warning line; the failed create's Error-level `Messages` are dropped from any success result. Genuine diagnostics on `success:false` (the Missing / non-collision paths) are preserved.
- **Status plumbing** (ADR): `FinalizeResult` gains `string? forcedStatus = null` and sets `result.Status = success ? (forcedStatus ?? "completed") : "failed"`. Change `Classify` from an unconditional assignment to `result.Status ??= result.Success ? "completed" : "failed"` so a pre-set forced status is preserved. Thread `resumedExisting` → `forcedStatus = "resumed-existing"`. The forced status derives from the FINAL post-registration execution: if registration then fails the op is `failed` and `resumed-existing` does not stick (AC-04-terminal).
- **`DescribeProbeFault`** — returns `"transient network fault"` when `TransientNetworkFailureClassifier.IsTransient(probeFault)`, else `"the existing schema could not be read"`. Text-only use of the classifier; it MUST NOT route (routing is read-success vs read-failure). A probe failure is NEVER routed to `success:false`.
- Read-failure catch (from Story 1's helper): `catch (Exception ex) when (!McpExceptionPolicy.IsUnrecoverable(ex)) → (Unverified, [], ex)`.
- Update `docs/McpCapabilityMap.md` to document the additive caller-visible `resumed-existing` status on `sync-schemas`.

Pattern to follow: existing `FinalizeResult` / `Classify` in `SchemaSyncTool.cs`; existing collision-hint message construction.

## Test Requirements

| Type | What to test | File |
|------|-------------|------|
| Unit `[Category("Unit")]` | AC-03 missing-column → `success:false` + "use update-entity" hint + `DidNotReceive().EnsureLookupRegistration(...)`; AC-04 read throws → `status:"resumed-existing"`, `success:true`, "NOT verified" warning; AC-06 resumed/forced-success result carries no Error-level messages from the failed create; AC-04-terminal registration-fails-after-degrade → `failed`, status not stuck | `clio.tests/Command/McpServer/SchemaSyncToolTests.cs` |
| Integration `[Category("Integration")]` | None (resolver seam with substitutes) | — |
| E2E `[Category("E2E")]` | Optional manual scenario mirroring AC-03/AC-04; MCP E2E is NOT in CI — manual only | `clio.mcp.e2e/SchemaSyncToolE2ETests.cs` |

Test seam (ADR): for AC-04, `stubManager.GetSchemaProperties(Arg.Any<GetEntitySchemaPropertiesOptions>()).Throws(new HttpRequestException(...))`. For AC-03, return an `EntitySchemaPropertiesInfo` whose `Columns` omit at least one requested column.

Test naming: `MethodName_ShouldBehavior_WhenCondition` — e.g. `ExecuteCreateSchema_ShouldReturnFailureWithUpdateEntityHint_WhenRequestedColumnMissing`, `ExecuteCreateSchema_ShouldDegradeToResumedExisting_WhenColumnReadThrows`, `FinalizeResult_ShouldDropCreateErrorMessages_WhenResumed`.

## Definition of Done

- [ ] Code compiles without Roslyn analyzer warnings (CLIO001-CLIO005)
- [ ] No new CLI flags introduced (internal MCP behavior only)
- [ ] Unit tests added with `[Category("Unit")]` — never `[Category("UnitTests")]`
- [ ] `docs/McpCapabilityMap.md` documents the additive `resumed-existing` status
- [ ] PR description references this story file

## Dev Agent Record

- Implementation started:
- Implementation completed:
- Tests passing:
- Notes:
