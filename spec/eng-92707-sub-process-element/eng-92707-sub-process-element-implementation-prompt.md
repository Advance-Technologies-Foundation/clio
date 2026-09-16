# ENG-92707 — Implementation prompts

Three sessions, three repositories, one forced order. §0 is written to be **pasted into every
session**; A, B and C are the sessions themselves.

Everything here is portable: paths are written against **named roots** you resolve once (§0.1), never
against one machine's layout. The analysis documents this prompt points at do carry absolute paths —
those are evidence provenance ("measured here, on this host"), not instructions, and they are meant to
stay that way.

---

## §0 — Shared context (paste into every session)

### 0.1 Resolve the roots first

Fill this in once per machine. Only the first three are needed to implement; the last two are for
**re-verifying** claims and re-running corpus measurements, and the analysis already carries their
results — if you do not have them, you lose the ability to re-check, not the ability to build.

| Root | What it is | Required? | Recognise it by |
|---|---|---|---|
| `CLIO_ROOT` | the `clio` checkout — MCP surface, bundled archive, this analysis | **yes** | `clio/Command/McpServer/Tools/ProcessDesigner/` exists |
| `PKG_ROOT` | the CrtProcessBuilder package checkout | **yes** | `packages/CrtProcessBuilder/descriptor.json` exists |
| `KNOWLEDGE_ROOT` | the `clio-knowledge` checkout — shipped guidance | **yes** | `bundle-source.json` at the root |
| `CORE_SRC` | Creatio platform sources (`TSBpm/Src/Lib`) | no | `Terrasoft.Core/Process/ProcessSchemaSubProcess.cs` exists |
| `PACKAGE_STORE` | the 7.8.0 configuration-package corpus | no | `CrtProcessDesigner/branches/7.8.0/Schemas/SubProcessPropertiesPage/` exists |

A probe that works on any layout — adjust the search roots to taste:

```bash
for base in "$HOME/Projects" /c/Projects /d/Projects "$HOME/src"; do
  [ -d "$base" ] || continue
  find "$base" -maxdepth 4 -type d \
       \( -name ProcessDesigner -path '*McpServer/Tools/*' \
       -o -name CrtProcessBuilder -path '*/packages/*' \) 2>/dev/null
  find "$base" -maxdepth 3 -name bundle-source.json 2>/dev/null
done
```

`CORE_SRC` is **not** where an outsider expects: on the machine the analysis was written, the checkout
carrying `TSBpm/Src/Lib` was `C:/Projects/Creatio2`, while `C:/Projects/Creatio` was a deployed site
with no `TSBpm` at all. Probe for the file, never for the folder name.

### 0.2 Read these before touching anything

In `CLIO_ROOT/spec/eng-92707-sub-process-element/`, in this order:

1. `eng-92707-sub-process-element-platform-reference.md` — **who** synchronizes the parameters
2. `eng-92707-sub-process-element-traps.md` — T-1…T-29, twenty-two of them silent, one a Blocker
3. `eng-92707-sub-process-element-plan.md` — D1–D12a, S1–S8, V1–V8, estimate, Definition of Done
4. `eng-92707-sub-process-element-open-questions.md` — what was decided, and the three measurements left

The same facts are also in `CLIO_ROOT/docs/knowledge/` as five grep-able records
(`subprocess-*`), which is how a session that never hears about this folder will still find them.

### 0.3 The four things that decide the design

1. **The platform already does the diff.** Assigning `ProcessSchemaSubProcess.SchemaUId` runs
   add/drop/preserve against the callee's parameters, and re-runs on **every design-time read**. Do
   **not** port `PreconfiguredPageParameterSync`. Our job is ordering, guarding and reporting.
2. **The provenance stamp is inverted** relative to the Pre-configured page. A synced parameter keeps
   the **callee's** schema UId in `CreatedInSchemaUId`; a caller-written value carries the **host**
   schema UId in `SourceValue.ModifiedInSchemaUId`. The page's rule erases every value here, silently.
3. **Blocker (T-25).** Any synchronization of an *already* multi-instance element flattens it — the
   guard sits one method too deep. 61 of 416 shipped elements are in that state. Refuse on
   `IsMultiInstanceModeEnabled` **before any path that can reach the setter**.
4. **Assign `SchemaUId` only after the element is attached** and has its own `UId` (T-1, T-5, T-26).
   Every existing user-task handler assigns inside `Create()`; copying that shape throws.

### 0.4 The order across three repositories, and why it is forced

```
PKG_ROOT  (CrtProcessBuilder)  →  CLIO_ROOT  (clio)  →  KNOWLEDGE_ROOT  (clio-knowledge)
     session A                      session B                 session C
```

* The guidance **release must be published before** the clio PR that depends on the new article
  content merges — the library ships as a GitHub Release asset, and a merged-but-unreleased article is
  unreachable.
* The bundled package version must go **up**, and raising it mid-review blocks the reviewer rather
  than the author: `RequiredPackageChecker` throws on a convergence refusal.
* An install resolves the archive from clio's **build output**, so `clio compress` verifies nothing
  until clio is rebuilt.

### 0.5 State this analysis left behind

| Fact | Consequence |
|---|---|
| Pinned archive: `ExpectedArchiveVersion` **1.6.1.9**, producing commit `ee5188ef404dfae299a373f1d67adfa9bb13df3b` | Bring `PKG_ROOT` to that commit before starting; raise `-Version` from there |
| `ExpectedOperationContractCount` = **7**, `ExpectedAuthorizationGateCallSites` = **5** | Hand-maintained, not refreshed by the rebundle script. Two research passes reported 5 and 3 — both wrong |
| A dev stand carries `UsrTc92707CopyCaller` → `UsrTc92707CopyCallee` in `Custom` | A safe, modifiable caller/callee pair for T-25 and runtime checks. `UsrTc92707ProbeB` is scrap |
| That stand's `Custom` has **69 schemas marked modified locally** | Every package install silently skips them — this will bite the e2e fixture |
| Guidance pin **1.14.9**; a local `clio-knowledge` checkout may be older | Pull before editing, or you edit text clio does not serve |

---

## Session A — the package (`PKG_ROOT`, ~1 d)

**Goal:** a buildable sub-process element with its guards, its drift report, and its tests.

**S1 — reproduce three things before designing around them.** The other six measurements are already
done (open-questions §B.3); these three need the package test project built, which is itself the
known time sink (core-bin junction, `-c dev-nf`).

* T-25: take a shipped multi-instance element, run one sync, confirm it is flattened.
* T-1 / T-26: assign `SchemaUId` on a detached element whose callee declares a parameter; repeat via
  `Clone()`. Both should throw.

**S2 — the collaborators**, cloned in shape from the Preconfigured-page family:
`SubProcessElementHandler`, `SubProcessElementIdentity` (must exclude `TriggeredByEvent` — the event
sub-process inherits the same CLR class), `ISubProcessReader` (resolve by UId **or name or caption**,
parameters as a separate memoized call, `null` = unreadable ≠ none). Register in
`CrtProcessBuilderApp.Init` **before** `UserTaskElementHandler` — `ResolveBuildType` takes the first
`CanBuild` match — and update the composition test.

**S3 — the applier**, running post-graph:

1. resolve the callee; refuse "not found"; distinguish "could not read";
2. refuse self-reference; refuse a retarget with live dependents (name them); refuse
   `IsMultiInstanceModeEnabled`; warn on R16;
3. snapshot parameters + mapping rows — **this is the only window**;
4. assign `SchemaUId` (attached, with a `UId`);
5. diff, stamp caller-written values per D5, prune a stranded mapping row (T-27);
6. return a report; render it into `IProcessDesignNotices`.

**S4 — contracts.** A `subProcess` block bound **strictly** to `type:"subProcess"` (D2a — this is what
removes the need for a `BlockExpectation`); `EnsureBlockMatchesHandler` arm; rewrite the
`NotSupportedException` tail that says sub-processes are not buildable; `setElement` support;
`DescribeSubProcessInfo` with the callee reference, `buildType` and `inSync` — the synced parameters
already come back untouched.

**S5 — tests.** `eng-92707-sub-process-element-test-plan.md`, TC-01…TC-22.

**Done when:** package suite green under `-c dev-nf`, composition test updated, `-Version` raised.

---

## Session B — clio (`CLIO_ROOT`, ~0.7 d)

* `Command/ProcessModel/Schema.cs` — the `"subprocess"` arm in `ManagerMap.ResolveDataId`, plus a
  `[TestCase]`. Without it `validate-process-graph` rejects a graph the server builds (see the
  knowledge record).
* `ProcessGraphValidator` — R16 as an **Error** (Q6: measured, 269 of 269 shipped callees comply).
* `IProcessDescriber.cs` — a typed `DescribedSubProcess`.
* **Five** agent-facing texts: both tool `[Description]`s, `ValidateProcessGraphTool.cs`, the prompt
  text, `docs/McpCapabilityMap.md` (twice in one line).
* `clio.mcp.e2e` — a sub-process fixture. Its callee is an ordinary process: build it with
  `create-business-process`, no sub-process element needed.
* Rebundle: `pwsh ./rebundle-process-builder.ps1 -PackageRepoPath <PKG_ROOT> -Version X.Y.Z.W`, then
  **rebuild clio**. Move the two security counts by hand if the service surface changed.
* Targeted suites: `dotnet test clio.tests/clio.tests.csproj --filter "Category=Unit&(Module=ProcessModel|Module=McpServer)"`.

**No `[RequiresPackage]` floor and no `SubProcessBlockExpectation`** — Q8, measured. That holds only
while the block binds to the type token.

---

## Session C — guidance, verification, PRs (~0.8 d)

* `KNOWLEDGE_ROOT`: rewrite the sub-process sentences in the five articles; they currently say
  `callActivity` is read-only. Add the **when** (plan §2a): decomposition first — 78 % of shipped
  callees have exactly one caller — sharing second and usually intra-package, and a plain "not for
  iterating a collection" so the D9 refusal is predictable. Add the ESQ discovery route and the
  `odata-read` dead end. Keep the element on the high-risk list with the line split into
  create-allowed / rewire-risky (D12a). Bump `libraryVersion` + `sequence`; re-pin
  `curated-knowledge-names.json` in clio if the name set moved.
* **V1–V8 on a stand** (plan §5). Schema writes strictly sequential — a parallel burst trips IIS
  rapid-fail on a .NET Framework stand.
* Three PRs, in the order of §0.4, each into its repository's default branch. State "MCP reviewed" and
  "ClioRing compatibility reviewed, no Ring-consumed contract changed" in the clio PR.

---

## Why three sessions

They are three repositories with three release trains and one forced order; a single session would
either interleave them or hold context it cannot use. The split also matches where the risk lives:
session A carries the Blocker and both NRE paths, B is mostly mechanical but has the one clio-side
trap that silently rejects valid graphs, and C is where the work becomes reachable by an agent at all.
