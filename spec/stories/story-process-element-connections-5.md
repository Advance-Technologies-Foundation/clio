# Story 5: Making a section connectable — the registration recipe and the cache-reset artifact

**Feature**: process-element-connections
**Analysis**: [process-element-connections-plan.md](../process-element-connections/process-element-connections-plan.md) §4.4
**ADR**: [adr-process-element-connections.md](../adr/adr-process-element-connections.md)
**Decisions**: D6, D10
**Status**: ready-for-dev
**Size**: M
**Repo**: `clio` (guidance + optional thin tool), `cliogate` (or clio) for the cache reset
**Depends on**: none — deliberately independent of stories 1–4

---

## As a

developer whose new app section has no `Activity` column yet

## I want

a documented, reliable way to make that section connectable

## So that

the refusal `setConnections` gives me (state (1)) names a step I can actually take

---

## Why this is a separate story

D6 puts data-model mutation outside `CrtProcessBuilder`: adding a column to `Activity` creates a replacing
schema in a package, needs `SaveSchemaDBStructure`, a declared dependency, and a product-wide registry row,
and can break `Activity` for the **whole environment** on a same-name collision. Different scope, different
reversibility, different privilege. This story owns that half — and it is mostly composition, because clio
already ships the pieces.

---

## Acceptance Criteria

- [ ] **AC-01** — The recipe is documented as a **guidance recipe** so an agent composes it, rather than
  guessing:

  | Step | Existing capability | Constraints that must be stated |
  |---|---|---|
  | 1. add the `Activity` lookup column | `update-entity-schema` (`environment-name`, `package-name`, `schema-name`, `operations`; the operation model carries `ReferenceSchemaName`/`ReferenceSchemaAlias`; publishes and rebuilds OData, no compile) | the name **must** carry the package prefix (`EntitySchema.GetIsPrefixRequired()` returns `true` unconditionally and is enforced at save); `isIndexed: true` is the product convention (`SectionWizardCasesSettings.js:424-437`) |
  | 2. declare the dependency | `add-package-dependency` on `Activity`'s owning package (`CrtCoreBase`) | required — the platform enforces it at export/install and cannot auto-apply it from configuration (the applier is `internal`) |
  | 3. registry row + binding | `create-data-binding` + `add-data-binding-row` (local package sources — identical to what the 7.x wizard emits), or the DB-first `create-data-binding-db` + `upsert-data-binding-row-db` | row: `Id` = a **fixed** guid (it is the binding key), `SysEntitySchemaUId` = `c449d832-a4cc-4b01-b9d5-8a12c42a9f89`, `ColumnUId` = the UId of the column from step 1 — read it with **`get-entity-schema-properties`** (its structured MCP output carries `u-id` per column). NOT `get-entity-schema-column-properties`: that command returns every other column property and **not the UId**, on any surface (verified on the CLI and over MCP), so the recipe as first written was not executable; `Position` may be omitted |

- [ ] **AC-02** — A **same-name pre-check** before step 1: a column of that name already contributed by
  another package breaks `Activity` for the whole environment via a codegen `ValidateException`. Refuse
  rather than attempt.
- [ ] **AC-03 (the only new code)** — **Cache invalidation** exists and is callable.
  `ProcessUserTaskSchemaManager.reset` clears both the server contract cache and the client ESQ cache;
  without it the designer keeps showing the old list **even after a compile** — this is the practical
  failure mode of the unofficial "add column → INSERT → compile" recipe. Nothing in clio does it today.
  Home: a cliogate endpoint (`[WebInvoke]`, `CheckCanManageSolution()` as the first line, `KnownRoute` +
  `/rest/CreatioApiGateway/<MethodName>`, per the AGENTS.md four-step recipe) or a thin clio tool.
- [ ] **AC-04** — If a convenience tool sequences the three steps, it lives in the **schema /
  app-modeling** surface, never the process-designer surface, and is a **composition** — it must not
  reimplement what `update-entity-schema` / `add-package-dependency` / `create-data-binding` already do.
- [ ] **AC-05** — Verified once at implementation time: that `update-entity-schema` cleanly **adds** a
  column to `Activity` specifically. Its documentation addresses **inherited**-column edits (caption and
  description only); adding a column to a base schema from another package means creating a replacing
  schema, which is a different operation and is unproven for `Activity`.
- [ ] **AC-06** — The recipe states the environment precondition that makes step 3 conditional: writing
  bound data requires a **non-foreign** target package. On a stand where the target package is installed
  rather than developed, the binding half is unavailable — say so instead of failing opaquely.
- [ ] **AC-07** — `setConnections`' state-(1) and state-(2) messages (story 3, AC-19) name **this** recipe,
  so the refusal and the remedy are connected in the agent's reading order.
- [x] **AC-08 (inherited from story 4)** — **MEASURED, and it needed no data-model change after all.** This story is the first point at which the **created-parameter
  tail at run time** can be verified, so it verifies it. Story 4 proved runtime effectiveness only for a
  perform task binding a **static** connection column; the created path needs a column that IS in the
  registry and is NOT declared by the element's user task, because `EntityConnectionBinder.ResolveColumn`
  refuses a host column that is neither. This recipe produces exactly such a column, so once it works:
  bind it with `setConnections` on a perform task, run the process, and confirm the created Activity
  carries the column — with a second, statically-bound column left unchanged as the control. The failure
  this guards is specific and already anticipated in code: a created parameter missing its data value type
  or reference entity survives the save and throws at task COMPLETION, which is why
  `EnsureParameterExists` asserts both rather than assuming them.

  **Result (krestov-test, 2026-08-11).** The stand already HAD a registered-but-non-shipped column, so the
  recipe was not needed to produce one: `UsrUsrTestApprovalElement` (`21da1fdf-…`) is one of the five
  `EntityConnection` rows for Activity, left by earlier testing. Binding it as a fixed record on the probe's
  perform task and running the process wrote it: the created Activity carries
  `UsrUsrTestApprovalElementId = b6975148-…`, with `AccountId` (mapping-sourced) and `ContactId` (a static
  column bound earlier) both unchanged as controls — three runs, each adding exactly one column and keeping
  the previous ones, so every earlier binding stays a live control.

  So a DYNAMIC connection parameter — one outside the user task's shipped contract — is materialised and
  written at task completion, which is the failure this AC guards. What the measurement canNOT distinguish,
  stated because the difference is invisible from outside: whether that parameter was created by
  `EnsureParameterExists` or by the platform's own `SynchronizeActivityConnectionParameters` hook, since
  `describe` omits parameters whose `Source = None`. Both are non-shipped parameters, so the guarded failure
  mode is covered either way; a provenance claim would need a server-side read of `CreatedInSchemaUId`.

## Implementation Notes

### AC-03's home is an open question, and the answer may be "no new code" (found while starting the story)

Two facts change the shape of AC-03, both verified from sources rather than assumed:

**1. Both caches are SERVER-side.** The plan calls the second one "the client ESQ cache", but the ESQ that
reads the registry is server-side C# — `ProcessUserTaskUtilities.CreateActivityConnectionEsq`
(`CrtProcessDesigner`), which sets `esq.CacheItemName =
BaseProcessUserTaskUtilities.ProcessUserTaskSchemaManagerCacheItemName`, and it is consumed by the public
server hook `SynchronizeActivityConnectionParameters(userConnection, target)`. So invalidation does not
require a browser; a server-side call can do it. That is better than the plan implies.

**2. But cliogate cannot reference the key.** `cliogate.csproj` compiles against `CreatioSDK`, whose `lib`
carries no `Terrasoft.Configuration` — and `BaseProcessUserTaskUtilities` is a configuration-layer type
(no source in PackageStore; the only referencing package is `CrtProcessDesigner`). So the AGENTS.md
cliogate recipe cannot name that constant at compile time.

Four candidate homes, with what disqualifies or recommends each:

| Option | Verdict |
|---|---|
| cliogate + a package dependency on `CrtProcessDesigner` | Heaviest. cliogate is installed on **every** environment clio touches, so a new dependency there is a product-wide commitment for one cache reset. |
| cliogate + reflection on the constant | Avoids the dependency but reintroduces the failure class this feature exists to remove: a renamed constant reads as "nothing to clear" and stays silent. |
| `CrtProcessBuilder` | Most likely correct. It already lives in the process-designer layer and already uses these manager types, so the constant costs it nothing new. D6 keeps *data-model mutation* out of the package; a cache reset is not one. |
| **No new code — `clear-redis-db`** | Check this FIRST. Creatio's session/application caches are Redis-backed, and clio already ships `clear-redis-db` plus `restart-web-app`. If a Redis flush invalidates both the ESQ item and the contract cache, AC-03 is satisfied by an existing capability and AC-04's "composition, never reimplementation" applies to it too. |

**What decides it:** one stand measurement — register a row, confirm the designer still shows the old list,
run `clear-redis-db`, and look again. Only if that fails does the story need an endpoint, and then
`CrtProcessBuilder` is its home. Do not write the endpoint before that check; it is the difference between
shipping code and shipping a sentence.

Registry facts an agent needs and cannot infer: the table is `EntityConnection`, keyed
`(SysEntitySchemaUId, ColumnUId, Position)`; the package-data folder the 7.x wizard produces is
`EntityConnection_<Id-with-dashes-stripped>`; the only 8.x writer in the product is
`GeneratedEntitySaver.cs:239-292`, behind the `GenAIFeatures.GenerateNextSteps` flag — which is why
creating a section in the 8.x App Hub registers **nothing** and this story exists at all.

## Definition of Done

- [ ] All AC met
- [ ] If a cliogate endpoint was added: `CheckCanManageSolution()` verified as the first line; route
  registered as `/rest/CreatioApiGateway/<MethodName>` (never `CreatioApiGatewayService`)
- [ ] Guidance PR bumps `libraryVersion` + `sequence`
- [ ] Docs and MCP review statements per policy
- [ ] Diary entry appended
