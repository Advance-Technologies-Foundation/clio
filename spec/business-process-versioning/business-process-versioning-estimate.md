# Business process versioning — estimate and defer rationale

> Jira: [ENG-94374](https://creatio.atlassian.net/browse/ENG-94374) · ticket estimate: **~1 day**
> Companions: [`…-research.md`](business-process-versioning-research.md) ·
> [`…-plan.md`](business-process-versioning-plan.md) · [`…-traps.md`](business-process-versioning-traps.md)

**Unit.** "AI-hours" = wall-clock hours with an AI agent writing the code, **including** the human review/rework
loop and deploy + manual verification on a live stand. A working day is 7 focused hours. This is the same unit as
the *"AI Est."* column on the
[Task list Confluence page](https://creatio.atlassian.net/wiki/spaces/TER/pages/4758143001), whose own stated
assumption is: *"the AI writes the code, so coding time is small; each estimate is dominated by code review + unit
/ e2e tests + QA by a tester."*

---

## 1. Bottom line

| Scope | AI-hours | Days | vs ticket |
|---|---|---|---|
| **Stage A — recommended ENG-94374 deliverable** (version read-back + history, clio-side only) | **21** | **~3** | **~3×** |
| Stage B — move the read server-side (optional) | +13.5 | ~2 | — |
| Stage C — create a version (deferred) | 33 + spike contingency | ~4.5 | — |
| Stage D — activate a version (deferred) | 14–18.5 | ~2.5 | — |
| **Everything the ticket literally asks for (A + C + D)** | **~67–70** | **~9–10** | **~9–10×** |

Two independent adversarial reviews converged on these numbers from different directions — one auditing technical
correctness against the platform source, one auditing delivery obligations against the two repos' mandatory
process. Their bottom lines (67–70 h and 69 h) agree to within 3 %.

---

## 2. Why the ~1-day estimate is wrong — precisely

### 2.1 The scope was reclassified without anyone noticing

~1 day was carved out of ENG-91852's **Task 14 — "Process properties (versioning / Methods / Usings)"**, which
scoped versioning as *process properties* — i.e. writing fields on a schema.

`Version` and `IsActiveVersion` **are not writable inputs**:

* the active version is a **computed ordering**, not a stored flag
  (`BaseProcessSchemaManager.cs:575-586`: user property → schema property → `PackagePosition` desc → `Version`
  desc → `Name`);
* creating a version is **a new schema write with a cloned graph and a rewritten UId set**.

That reclassification is what drags in a second repository, an exactly-pinned wire contract on both sides, a
mandatory version-bumped rebundle, and a correctness proof only a live stand can give.

### 2.2 There is no server API to wrap

The estimate implicitly assumes a `CreateVersion` endpoint exists. **It does not.** The platform composes a
version **client-side** and saves through the ordinary schema-save path. The nearest server clone,
`SaveClonedSchema`, is `protected` and deliberately **resets** versioning; `CloneSchemaUsingMetaData` is `private`
and implements **Copy**, not **Version**.

So what looked like plumbing is **re-implementing a platform algorithm** whose correctness can only be established
by diffing against the product's own output on a running environment.

### 2.3 This surface carries ~11 h of fixed overhead per change, regardless of code size

| Obligation | h |
|---|---|
| BMAD PRD + ADR + story + test plan + sprint entry (**no artefacts exist for ENG-94374**) | 2.5 |
| `clio-knowledge` guidance PR (**third repo**) + `libraryVersion` / `sequence` bump + local fixture re-pin | 2.0 |
| ClioRing compatibility gate — inspection + written verdict | 0.5 |
| Two mandatory comprehensive agentic review gates + rework | 3.0 |
| Workspace diaries in **both** repos | 0.5 |
| Docs sweep (`McpCapabilityMap` §11, `install-process-builder` md+txt, ProcessBuilder architecture md + puml) | 1.5 |
| Pin / registry sweep (passthrough classification row, `RequiresPackage` cases, gate-call-sites, op count, E2E tier + Allure) | 1.0 |

The Stage A *code* is roughly 150 lines plus tests — under an hour of AI writing. **The overhead is 10× the
code.**

### 2.4 Two facts nobody could have known when the ticket was written

* **`clio.mcp.e2e` buys zero CI signal for this family.** Per `project-context.md`, the process-designer fixtures
  do not run in CI because `CrtProcessBuilder` is not installed on that stand. E2E here is manual verification,
  not a gate — anything load-bearing must be mirrored at unit level.
* **The guidance article lives in a third repository** (`clio-knowledge`) with its own PR and sequence bump.

---

## 3. The item nobody can compress: the live-stand proof

`ProcessBuilder/tests/CrtProcessBuilder/ProcessSchemaRepositoryTests.cs:15-17` records that the create/save
lifecycle uses **non-virtual `SchemaManager` APIs and is covered at E2E only**.

Every claim about `Clone()` carrying the full body, about the `GetMetaItems` sweep leaving elements *owned*
rather than *inherited*, about the caption restore, and about localizable resources surviving is settled by
**build → `install-process-builder --force` → configuration build → restart → inspect in the real designer**.
There will be several cycles. Budget **8 h**, not 5.

### 3.1 Why the create estimate carries a contingency

The adversarial technical review found **two blockers that invalidate the "clone + two-field rewrite" model** —
`SaveExtraProperties` never persisting instance-set properties, and the `GetMetaItems` walk missing `Parameters`
and `ExecutionContexts` ([BLOCKER 1 and 2](business-process-versioning-traps.md), both verified verbatim against
the platform source in this session).

**Both fail silently. Both are invisible to unit tests and to the response payload.** Each costs at least one
additional deploy/verify cycle to discover and correct, and the second may force a fallback to a
serialize–rewrite–deserialize implementation.

The honest framing for the follow-up ticket is therefore: **a 4 h spike (C0) first, then an estimate.** 33 h is
the number *if the spike resolves cleanly*; do not commit to it before C0 lands.

---

## 4. Recorded decision to defer (the ticket explicitly permits this)

> *"Deliverable: … — **or a recorded decision to defer** with rationale."*

**Ship Stage A. Defer create-version and activate-version to a follow-up ticket.**

### Rationale to record on ENG-94374

1. **There is no server API to call.** Creating a version is a client-side transformation in the platform. The
   package would have to hand-roll the metadata clone. That is not "a build/modify op" — it is a
   re-implementation of a platform algorithm.
2. **The riskiest part is untestable by construction.** A version whose `CreatedInOwnerSchemaUId` or
   `ExtendParent` is wrong **saves, compiles, renders in the designer and runs** — and diverges only later. The
   only real proof is a field-by-field diff against a designer-created version on a live stand.
3. **Activation is the highest-blast-radius operation the toolkit would own, and the platform hides its partial
   failures.** `SetActiveVersionItem` re-saves every sibling schema inside one transaction and **logs-and-swallows
   the deactivation failure of every version it is turning off** (`BaseProcessSchemaManager.cs:518-519`). On a
   family spanning a delivered and an editable package that leaves **two versions flagged active**, with the
   winner decided by package ordering. Shipping it without the runtime proof would give agents a button that
   changes what a customer's automation executes, with a success message that can be wrong. Additionally, the
   native platform endpoint has **no authorization check at all**
   ([BLOCKER 3](business-process-versioning-traps.md)).
4. **Delete-a-version is out of scope permanently, not just deferred.** The product offers it nowhere
   (`ProcessVersionsDetail` disables Add/Edit/Copy/Delete; Academy: the history *"cannot be edited"*), and the
   platform's delete **cancels every running instance** of that schema. "Manage version history" in this domain
   means **read and activate**, never delete.
5. **The read half removes a live defect today.** `describe-business-process --process-name <root>` currently
   returns the graph of a version that is **not running**, and says nothing about it — because describe resolves
   by name and each version has a distinct schema name. That is a wrong answer being served now. Fixing it is
   worth more than adding a write path, and it is the prerequisite for ever verifying one.

### Two structural facts to record so they are not relitigated

* **The version family is FLAT, not a chain.** Every version's `ParentSchemaUId` is the **root**. The ticket's
  payload shows v1 → v0 only because v0 *is* the root; v2 will also point at v0, never at v1.
* **The write path must be a new operation NAME, never a new field on `ModifyProcessRequest`.** No contract
  implements `IExtensibleDataObject`, so an environment one package release behind would **drop the unknown
  member in silence** and edit the current version in place while reporting success.

### Suggested ticket split

| Ticket | Scope | Estimate |
|---|---|---|
| **ENG-94374** (keep) | Version read-back + history, clio-side | **21 AI-h ≈ 3 days** |
| **New follow-up** | Create + activate, gated on the C0 spike | **4 h spike, then ~46 AI-h ≈ 6.5 days** |

> Do not split it any other way. **Create-without-activate is a strictly worse product than not shipping** — it
> mints schemas that become candidates in the active-version ordering with no supported way to make them run.

---

## 5. Calibration against this backlog's own history

From the [Task list](https://creatio.atlassian.net/wiki/spaces/TER/pages/4758143001):

| Task | AI Est. (days) | Fact (SP) |
|---|---|---|
| 1 — Merge the prototype | 4 | 5 |
| 4 — Data source filters | 6 | 3 |
| 5 — Add/modify process parameters | 3 | 3 |
| 10 — Signal start: tracked columns | 1.5 | 3 |

The AI estimate has swung **both** ways on this backlog — over by 2× on filters, under by 2× on signal start. The
pattern: tasks that stayed inside clio came in at or under estimate; tasks that had to establish platform
behaviour empirically came in over. **Versioning is squarely in the second category**, which is the main argument
for taking the read half — which does not — as this ticket's deliverable.
