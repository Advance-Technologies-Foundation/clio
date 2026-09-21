# ENG-92707 — Implementation prompt (one autonomous session)

One session, three repositories, one forced order, **three pull requests**. Designed to run to the end
without asking anything: every decision this work needs was already taken and is recorded with its
evidence. Questions that still arise are **written down and deferred to the final report**, never
raised mid-run.

Paths here are written against **named roots** you resolve once (§0.1), never against one machine's
layout. The analysis documents this prompt points at do carry absolute paths — those are evidence
provenance ("measured here, on this host"), not instructions.

---

## §0 — Context

### 0.1 Resolve the roots first

Only the first three are needed to build. The last two are for **re-verifying** claims and re-running
corpus measurements; the analysis already carries their results, so without them you lose the ability
to re-check, not the ability to deliver.

| Root | What it is | Required? | Recognise it by |
|---|---|---|---|
| `PKG_ROOT` | the CrtProcessBuilder package checkout | **yes** | `packages/CrtProcessBuilder/descriptor.json` |
| `CLIO_ROOT` | the `clio` checkout — MCP surface, bundled archive, this analysis | **yes** | `clio/Command/McpServer/Tools/ProcessDesigner/` |
| `KNOWLEDGE_ROOT` | the `clio-knowledge` checkout — shipped guidance | **yes** | `bundle-source.json` at the root |
| `CORE_SRC` | Creatio platform sources (`TSBpm/Src/Lib`) | no | `Terrasoft.Core/Process/ProcessSchemaSubProcess.cs` |
| `PACKAGE_STORE` | the 7.8.0 configuration-package corpus | no | `CrtProcessDesigner/branches/7.8.0/Schemas/SubProcessPropertiesPage/` |

```bash
for base in "$HOME/Projects" /c/Projects /d/Projects "$HOME/src"; do
  [ -d "$base" ] || continue
  find "$base" -maxdepth 4 -name descriptor.json -path '*packages/CrtProcessBuilder/*' 2>/dev/null
  find "$base" -maxdepth 3 -name bundle-source.json 2>/dev/null
  find "$base" -maxdepth 5 -name ProcessSchemaSubProcess.cs 2>/dev/null
done
```

`CORE_SRC` is not where an outsider expects: on the machine the analysis was written, `TSBpm/Src/Lib`
lived under `C:/Projects/Creatio2`, while `C:/Projects/Creatio` was a deployed site with no `TSBpm` at
all. **Probe for the file, never for the folder name.**

### 0.2 Read before touching anything

In `CLIO_ROOT/spec/eng-92707-sub-process-element/` (branch
`feature/ENG-92707-sub-process-element`), in this order:

1. `…-platform-reference.md` — **who** synchronizes the parameters
2. `…-traps.md` — T-1…T-29, twenty-two silent, one a Blocker
3. `…-plan.md` — D1–D12a, S1–S8, V1–V8, estimate, Definition of Done
4. `…-open-questions.md` — what was decided and why, and the three measurements left
5. `…-test-plan.md` — TC-01…TC-36 and the mocking recipe

The same facts are in `CLIO_ROOT/docs/knowledge/` as five grep-able records (`subprocess-*`).

### 0.3 The four facts that decide the design

1. **The platform already does the diff.** Assigning `ProcessSchemaSubProcess.SchemaUId` runs
   add/drop/preserve against the callee's parameters, and re-runs on **every design-time read**. Do
   **not** port `PreconfiguredPageParameterSync`. The job is ordering, guarding and reporting.
2. **The provenance stamp is inverted** relative to the Pre-configured page. A synced parameter keeps
   the **callee's** schema UId in `CreatedInSchemaUId`; a caller-written value carries the **host**
   schema UId in `SourceValue.ModifiedInSchemaUId`. The page's rule erases every value, silently.
3. **Blocker (T-25).** Any sync of an *already* multi-instance element flattens it — the guard sits one
   method too deep. 61 of 416 shipped elements. Refuse on `IsMultiInstanceModeEnabled` **before any
   path that can reach the setter**.
4. **Assign `SchemaUId` only after the element is attached** and has a `UId` (T-1, T-5, T-26). Every
   existing user-task handler assigns inside `Create()`; copying that shape throws.

### 0.4 The order, and why it cannot be reshuffled

```
PKG_ROOT (CrtProcessBuilder)  →  CLIO_ROOT (clio)  →  KNOWLEDGE_ROOT (clio-knowledge)
```

* The guidance **release must be published before** the clio PR that depends on new article content
  merges — the library ships as a GitHub Release asset.
* The bundled version must go **up**; raising it mid-review blocks the reviewer, not the author
  (`RequiredPackageChecker` throws on a convergence refusal).
* An install resolves the archive from clio's **build output**, so `clio compress` verifies nothing
  until clio is rebuilt.

### 0.5 State the analysis left behind

| Fact | Consequence |
|---|---|
| Pinned archive **1.6.1.9**, producing commit `ee5188ef404dfae299a373f1d67adfa9bb13df3b` | Bring `PKG_ROOT` there first; raise `-Version` from it |
| `ExpectedOperationContractCount` = **7**, `ExpectedAuthorizationGateCallSites` = **5** | Hand-maintained; the rebundle script does not touch them |
| A dev stand may carry `UsrTc92707CopyCaller` → `UsrTc92707CopyCallee` in `Custom` | A safe modifiable caller/callee pair. `UsrTc92707ProbeB` is scrap |
| That stand's `Custom` has **69 schemas marked modified locally** | Package installs silently skip them — this bites the e2e fixture |
| Guidance pin **1.14.9** | `git pull` `KNOWLEDGE_ROOT` before editing, or you edit text clio does not serve |

---

## §1 — Autonomy contract

**Do not ask questions during the run.** Everything needed was decided; where it was not, decide
yourself and record it.

**When something is ambiguous:**

1. Decide using, in order: the analysis documents → the repository's existing precedent (the
   Pre-configured page family is the template) → the most conservative option that refuses loudly
   rather than proceeding silently.
2. Append an entry to `CLIO_ROOT/spec/eng-92707-sub-process-element/…-deferred-questions.md`
   (create it): **the question, the decision taken, the reason, and what would have to be true for the
   other choice to win.**
3. Continue. Do not stop, do not ask, do not leave a TODO in code.

**A measurement that contradicts a load-bearing decision is not a reason to stop either.** If S1 shows
D1, D5 or T-25 is wrong, record it as the first entry in the deferred file, take the documented
fallback (for D1 that is the Pre-configured page's own synchronizer), and carry on. Note the estimate
moves; say so in the final report.

**The only hard stops** — stop, report, and wait:

* a destructive action on shared state that the plan does not call for (dropping a package, deleting
  shipped schemas, force-pushing a shared branch);
* credentials or a stand you have no access to, where no read-only alternative exists;
* a merge into `master` — **never merge**, in any repository. Open the PR and hand it over.

**Also never:** rebundle with an unchanged or lowered `-Version`; touch the two hand-maintained
security counts without checking the shipped sources; edit a repository that is not one of the three.

---

## §2 — The run

### Stage 1 — the package (`PKG_ROOT`)

**S1 — measure three things before designing around them.** Six of the nine measurements are already
closed (open-questions §B.3); these three need the package test project built, which is itself the
known time sink (core-bin junction, `-c dev-nf`).

* T-25: sync an already multi-instance element; confirm it is flattened.
* T-1 / T-26: assign `SchemaUId` on a detached element whose callee declares a parameter; repeat via
  `Clone()`. Both should throw.

Write each result into the deferred file as evidence, whichever way it goes.

**S2 — collaborators**, cloned in shape from the Pre-configured page family:
`SubProcessElementHandler`; `SubProcessElementIdentity` (must exclude `TriggeredByEvent` — the event
sub-process inherits the same CLR class); `ISubProcessReader` (resolve by UId, **name or caption**;
parameters as a separate memoized call; `null` = unreadable ≠ none). Register in
`CrtProcessBuilderApp.Init` **before** `UserTaskElementHandler` — `ResolveBuildType` takes the first
`CanBuild` match — and update the composition test.

**S3 — the applier**, post-graph:

1. resolve the callee; refuse "not found"; distinguish "could not read"; refuse an ambiguous caption
   naming the candidates (D11);
2. refuse self-reference; refuse a retarget with live dependents, naming them; refuse
   `IsMultiInstanceModeEnabled`; refuse R16 (Q6: enforced — 269 of 269 shipped callees comply);
3. snapshot parameters + mapping rows — **the only window** (T-7);
4. assign `SchemaUId`, attached and with a `UId`;
5. diff; stamp caller-written values per D5; prune a stranded mapping row (T-27);
6. return a report; render it through `IProcessDesignNotices`.

**S4 — contracts.** A `subProcess` block bound **strictly** to `type:"subProcess"` (D2a — this is what
removes the need for a `BlockExpectation`); an `EnsureBlockMatchesHandler` arm; rewrite the
`NotSupportedException` tail that says sub-processes are not buildable; `setElement` support with
`resync`; `DescribeSubProcessInfo` carrying the callee reference, `buildType` and `inSync` — the synced
parameters already come back untouched.

**S5 — tests.** TC-01…TC-22 from the test plan. `[TestFixture(Category = "UnitTests"), Category("PreCommit")]`,
`[Description]` per test, AAA, `because:` on load-bearing assertions.

**Then: review loop (§3) → commit → push → open PR #1.**

### Stage 2 — clio (`CLIO_ROOT`)

Branch from the analysis branch so the spec travels with the code.

* `Command/ProcessModel/Schema.cs` — the `"subprocess"` arm in `ManagerMap.ResolveDataId` **and** a
  `[TestCase]`. Without it `validate-process-graph` rejects a graph the server builds.
* `ProcessGraphValidator` — R16 as an **Error**, with tests.
* `IProcessDescriber.cs` — a typed `DescribedSubProcess`.
* **Five** agent-facing texts: both tool `[Description]`s, `ValidateProcessGraphTool.cs`, the prompt
  text, `docs/McpCapabilityMap.md` (twice in one line).
* `clio.mcp.e2e` — a sub-process fixture. Its callee is an ordinary process: build it with
  `create-business-process`; no sub-process element needed.
* Rebundle: `pwsh ./rebundle-process-builder.ps1 -PackageRepoPath <PKG_ROOT> -Version X.Y.Z.W`, then
  **rebuild clio**. Move the two security counts by hand if the service surface changed.
* `dotnet test clio.tests/clio.tests.csproj --filter "Category=Unit&(Module=ProcessModel|Module=McpServer)"`.

**No `[RequiresPackage]` floor and no `SubProcessBlockExpectation`** (Q8, measured) — valid only while
the block binds to the type token.

**Then: review loop (§3) → commit → push → open PR #2.**

### Stage 3 — guidance and verification (`KNOWLEDGE_ROOT`)

`git pull` first. Rewrite the sub-process sentences in the five articles — they currently say
`callActivity` is read-only. Add:

* **the "when"** (plan §2a): decomposition first — 78 % of shipped callees have exactly one caller —
  sharing second and usually intra-package, and a plain "not for iterating a collection" so the D9
  refusal is predictable;
* the **ESQ discovery route** over `VwProcessLib`, and the `odata-read` dead end (T-29);
* the high-risk line **split** into create-allowed / rewire-risky, with its reasons (D12a).

Bump `libraryVersion` + `sequence`; re-pin `curated-knowledge-names.json` in clio if the name set moved.

Run **V1–V8** (plan §5) on a stand. Schema writes strictly sequential — a parallel burst trips IIS
rapid-fail on a .NET Framework stand.

**Then: review loop (§3) → commit → push → open PR #3.**

---

## §3 — The review loop (every stage, every iteration)

Before opening each PR:

1. Run **`/creatio-code-review`** over that repository's full diff against its base branch.
2. Fix every **Blocker** and **High** finding. Medium and Low: fix, or record in the deferred file why
   not — never leave one silently unaddressed.
3. **Re-run `/creatio-code-review` after each fix iteration**, over the new diff. Findings introduced
   by a fix are the reason this loop exists.
4. Repeat until a run produces no Blocker and no High.
5. Only then commit, push and open the PR. State in the PR body: which review tier ran, how many
   iterations, and what was deliberately not fixed.

A PR is opened **ready for review**, not draft — the analysis PR (#1560) is the draft one.

Each PR body must also carry, verbatim where the wording is prescribed:

* "MCP reviewed" (or "MCP reviewed, no update required") — AGENTS.md;
* "ClioRing compatibility reviewed, no Ring-consumed contract changed", with the inspected paths;
* the targeted test filter that was run;
* the Jira key and a link to the analysis folder.

---

## §4 — After the three PRs: manual test cases into Jira

Write them to `CLIO_ROOT/spec/eng-92707-sub-process-element/…-manual-test-prompt.md` **and** post them
to [ENG-92707](https://creatio.atlassian.net/browse/ENG-92707) as a comment (the
`jira-manual-test-cases` skill owns the TC-0X format: Preconditions / Steps / Expected result).

Two things make this format specific to this ticket:

**Each case is a task for an AI, written in business language.** No element type, no tool name, no
argument, no schema name, no UId — the case says what a person wants, and the run tests whether the
agent can get there. *"Build a process that, when an order is created, calls the existing order-approval
process and passes it the order id."* A case that must name an internal is marked **adversarial** and
says why.

**Each case states its expected result at both levels, separately:**

| Level | What to write |
|---|---|
| **Design time** — what the human sees in the designer | The element renders as a call activity with the callee's caption; the properties card shows the called process and its parameter grid; the mapped value appears in the parameter; the element's caption follows the callee |
| **Runtime** — what actually happens when it runs | The callee instance starts and receives the mapped input; the output reaches the caller's parameter; `SysProcessLog` shows both processes; a renamed callee parameter silently delivers nothing (the negative case, per the runtime-binding record) |

Declare for every case which of the three observation levels it reaches — **Stored** (metadata
written), **Design time** (the designer opened it), **Runtime** (it ran) — and where it stops. A case
that passes at Stored level with no designer ever opened has **not** proved the designer accepts it.

Cover at minimum: select a callee; map a value in; read an output back; re-sync after the callee gained
and lost a parameter; retarget to a different callee; the three refusals (self-reference, live
dependents, multi-instance); and the silent runtime mismatch.

---

## §5 — The final report

Only here do questions surface. Produce:

1. **Three PR links**, with the review iteration count for each.
2. **The deferred-questions file** — every decision taken alone, its reason, and what would flip it.
   This is the agenda for the human, and it is the only place questions are allowed to appear.
3. **What was measured in S1** and whether it confirmed or contradicted D1 / D5 / T-25.
4. **What is left**: anything refused, out of scope, or blocked — and Q1 (the Roadmap page's task
   numbering, which needs an HTML-format edit because a markdown round-trip would destroy its 46
   smartlinks) and Q10 (which caption the contract echoes), both still open and neither touching code.
5. **Estimate vs actual**, against the planned 2.5–3 days of effort.
