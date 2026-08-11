# Story 5: Making a section connectable — the registration recipe and the cache-reset artifact

**Feature**: process-element-connections
**Analysis**: [process-element-connections-plan.md](../process-element-connections/process-element-connections-plan.md) §4.4
**ADR**: [adr-process-element-connections.md](../adr/adr-process-element-connections.md)
**Decisions**: D6, D10
**Status**: in-progress
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
- [x] **AC-04** — **DECIDED: no convenience tool.** Having now run all three steps by hand, the sequencing is
  not what makes this hard. Each step already refuses well on its own, and the three genuinely difficult parts
  are not sequencing at all: the prefix requirement (enforced at save), the collision that breaks the schema
  environment-wide (now guarded by AC-02, in the command where it belongs rather than in a wrapper), and the
  `ColumnUId` hand-off between steps 1 and 3 — which is a *discoverability* problem solved by naming the right
  read command, not by hiding the step.
  <br>A wrapper would also have to own two conditionals it cannot decide for the caller: whether the target
  package already has a replacing layer of the host schema (AC-05 measured only the "already has" path), and
  whether that package is non-foreign so bound data can be written at all (AC-06). A tool that guesses either
  turns a clear refusal into an opaque one, which is the opposite of this story's purpose.
  <br>So the deliverable is the RECIPE (AC-01) plus the pre-check that already shipped, and the constraint the
  AC states — schema/app-modeling surface, never the process-designer surface — stands as the rule for anyone
  who revisits this with a concrete need.
- [x] **AC-05** — **VERIFIED on krestov-test (2026-08-11), with one qualification that matters more than the
  result.** `update-entity-schema --package Custom --schema-name Activity` with an `add` operation
  (`type: Lookup`, `reference-schema-name`, `indexed: true`) added `UsrClioConnProbe`: "Schema 'Activity'
  published in 13.1s", OData rebuild requested, and the column reads back as `source: own` with
  `u-id: 91f303e6-…`, taking the Custom layer's `own-column-count` from 0 to 1.

  The qualification: the replacing `Activity` schema in `Custom` **already existed** (0 own columns,
  `extend-parent: true`), so what is proven is "adds a column to an EXISTING package layer", not "creates the
  layer". The story's worry — that this is a different, unproven operation — is therefore narrower than
  stated, but not void: on a package with no layer yet, step 1 takes a path this measurement did not exercise.
  State that conditionally in the recipe rather than implying one path.

  The new cross-package pre-check (AC-02) ran against the live environment on this call and correctly did not
  interfere — the name was free in every layer.
- [x] **AC-06** — Precondition confirmed by exercising it: `create-data-binding-db -e krestov-test --package
  Custom --schema EntityConnection` created the row AS BOUND package data (`da8351db-…`), so `Custom` on this
  stand is non-foreign and the binding half is available. The recipe must still state the precondition,
  because the failure on a foreign package is what it protects against — that half remains unexercised, and
  is named as such rather than assumed to be graceful.
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

  **Provenance closed by construction (same day, second measurement).** After AC-05 added
  `UsrClioConnProbe` and step 3 registered it, binding THAT column on the same probe element and running the
  process wrote it too (`UsrClioConnProbeId = ba5642d3-…`, with all three earlier columns unchanged). The
  ambiguity above is gone: the element was built hours before the column existed, so the platform's
  `SynchronizeActivityConnectionParameters` hook cannot have created its parameter — only
  `EnsureParameterExists` can have. So the CREATED-parameter tail is proven, not merely the dynamic one.

  **And it settles the severity of AC-03.** The platform's cached contract could not have known about a column
  registered minutes earlier, yet the runtime wrote it. So a stale cache does NOT block the write; it affects
  what the DESIGNER shows. The cache reset is therefore an ergonomics fix for a human looking at "Connected
  to", not a correctness fix for an agent-authored process — which lowers AC-03 from "the only new code" to a
  usability nicety, and is one more reason not to build the endpoint on speculation.

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

**Blocked on an OBSERVATION, not on a decision (2026-08-11).** The check above cannot be run from the CLI
today, and that is worth stating precisely because it looks runnable:

- the connections catalog reads `EntityConnection` through a plain `Select` with no cache, so `setConnections`
  resolves a freshly registered column whatever the platform cache holds — our own write path cannot observe
  the cache at all;
- the platform cache surfaces as DYNAMIC parameters on the user-task contract, and `describe` omits every
  parameter whose `Source = None`, so a parameter that exists but is unbound is indistinguishable from one
  that does not exist;
- which leaves the designer's "Connected to" block — a browser artefact behind an interactive login.

So the endpoint stays unwritten: there is no evidence yet that a Redis flush is insufficient, and writing it
on the assumption that it is would be the shipped-code-instead-of-a-sentence trade this story set out to
avoid. Two ways to unblock, in order of cost: a human opens the designer on a stand, registers a column and
watches whether the list changes before and after `clear-redis-db`; or `describe` gains an opt-in that
reports unbound connection parameters, which would make the cache observable from the CLI and is arguably
worth having on its own merits. Until one of them happens, AC-03 is an open decision with a named
experiment, not a coding task.

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
