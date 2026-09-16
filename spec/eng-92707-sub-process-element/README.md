# ENG-92707 — Sub-process element: selection + parameter sync

Analysis and implementation plan for
[ENG-92707](https://creatio.atlassian.net/browse/ENG-92707) (component *bpms tools*; epic
[ENG-92704](https://creatio.atlassian.net/browse/ENG-92704); the re-sync precedent is
[ENG-95461](https://creatio.atlassian.net/browse/ENG-95461), not the "Task 16" the ticket names).

These six documents are written to be attached to the ticket. Read them in this order.
A seventh, the [implementation-prompt](eng-92707-sub-process-element-implementation-prompt.md), is for
whoever builds it: three sessions across the three repositories, with portable named roots rather than
one machine's paths.

| # | Document | What it settles |
|---|---|---|
| 1 | [platform-reference](eng-92707-sub-process-element-platform-reference.md) | **Who synchronizes the parameters** — the core algorithm, line by line, and what the classic designer does on top of it |
| 2 | [serialization-capture](eng-92707-sub-process-element-serialization-capture.md) | AC4 — every metadata key, decoded, mined from 420 shipped elements |
| 3 | [traps](eng-92707-sub-process-element-traps.md) | T-1…T-29, twenty-two of them **silent**. Read its correction banner first: T-25 and T-26 did not survive measurement |
| 4 | [plan](eng-92707-sub-process-element-plan.md) | Decisions D1–D12a, work packages S1–S8, verification V1–V8, estimate, Definition of Done |
| 5 | [test-plan](eng-92707-sub-process-element-test-plan.md) | Harness, the mocking recipe that has to be *adapted* rather than referenced, TC-01…TC-36 |
| 6 | [open-questions](eng-92707-sub-process-element-open-questions.md) | Q1–Q10 — **eight decided 2026-09-14** — plus the verification status: six of nine measurements closed |

**Verification status:** the research run completed **31/31 agents, zero errors** (resumed
2026-09-13); six figures the first pass got wrong are corrected in
[open-questions §B.1](eng-92707-sub-process-element-open-questions.md), and one finding was promoted to
a Blocker. On **2026-09-14** six of the nine remaining measurements were taken (corpus + live stand) and
**eight of the ten open questions were decided by the owner**. Q1 (task numbering) and Q10 (which
caption the contract echoes) are the two left, and neither changes code.

---

## The five findings that change the shape of the work

**1. The platform already implements AC3 in full.**
Assigning `ProcessSchemaSubProcess.SchemaUId` synchronously runs
`ProcessSchemaActivity.SynchronizeParameters()` — a three-phase add / drop / preserve diff against the
called process's parameters, keyed on the caller schema's `ProcessSchemaMapping` rows — and it re-runs
on **every design-time read of the schema**, so CrtProcessBuilder's own `GetDesignInstance` hands over
an element that has already converged. `PreconfiguredPageParameterSync` (545 lines) exists only because
a Pre-configured page's parameters are *not* reachable through `GetSchemaParameters()`; a sub-process's
are. **Do not port it.** The package's job is ordering, guarding and reporting.

**2. The provenance stamp is inverted relative to the Pre-configured page, and getting it wrong erases
every value silently.**
`ClearParametersSourceValue` keeps a value only when
`parameter.CreatedInSchemaUId != SourceValue.ModifiedInSchemaUId` **and** the direction is `In` or
`Variable`. So a synced parameter must keep the **callee's** schema UId as its `CreatedInSchemaUId`
(all 420 shipped elements do), while a caller-written value carries the **host** schema UId in
`ModifiedInSchemaUId`. `PreconfiguredPageParameterSync` deliberately re-stamps
`CreatedInSchemaUId = schema.UId` — correct there, fatal here. Nothing throws; the values are simply
gone by the next read.

**3. Changing the selected process is three different behaviours depending on who does it.**
The classic designer **refuses** the retarget outright when any other parameter or flow condition still
maps from the element, and otherwise confirms ("All parameters will be lost"), clears everything,
converts multi-instance back to single, and renames the element after the new callee. The server
setter has no guard at all: it drops the parameters and sets `IsValid = false` on the dependents,
which surfaces much later as a `ValidateException` at process **start**. And not re-syncing is
silent in a third way — runtime binds by parameter **name**, skips an unmatched one with no error and
no log, and never validates `IsRequired`. That third case is the whole business justification for
shipping AC3.

**4. Any sync on an already multi-instance element destroys it — a Blocker.**
`SynchronizeParametersInternal` calls `Parameters.Clear()` **before** the guard that is supposed to stop
it, so the self-reference and empty-UId protections do not apply. An element that is already
multi-instance — **61 of 416 shipped, 14.7 %** — is rebuilt as two collections plus three counters by
any code path that reaches the setter, discarding the callee's parameters and every mapped value.
`describe` afterwards reports a structurally valid element. The multi-instance refusal is therefore a
**pre-condition**, not a validation of what the caller asked for.

**5. The clio side is smaller than it looks, and the agent-facing texts are the real work.**
`descriptor` and `operations` are pass-through JSON strings, so a `subProcess` block reaches the server
with no typed clio-side model. But `ManagerMap.ResolveDataId` knows only the diagram data-id
`"callactivity"`, so a build token that is not an arm makes `validate-process-graph` report a hard
`UNKNOWN` **Error** on a process the server builds correctly. A `SubProcessBlockExpectation` turned
out **not** to be owed — measured: an unknown *type* token is refused loudly, so binding the block to
`type:"subProcess"` makes the type its own guard (plan D2a). And
**five** shipped agent-facing surfaces currently assert that sub-processes are not buildable, including
`ValidateProcessGraphTool.cs:50` and `docs/McpCapabilityMap.md` (twice on one line), plus five
`clio-knowledge` articles that document `callActivity` as read-only. The MCP review list also has to
cover **eight** process-designer tools carrying two distinct `[RequiresPackage]` floors, not the three
obvious ones.

---

## When to use a sub-process at all

The ticket answers *how*; [plan §2a](eng-92707-sub-process-element-plan.md) answers *when*, measured
over 402 shipped caller→callee edges. The headline corrects the intuitive answer: **decomposition, not
reuse** — 78 % of called processes have exactly one caller, and the extreme cases split a lifecycle into
nine named stages. Reuse is 22 % and stays inside one package 84 % of the time. And **15 % of shipped
sub-processes are loop bodies** (multi-instance) — the one shape this ticket refuses, which the guidance
has to say out loud.

---

## Evidence base

| Source | Scale |
|---|---|
| Creatio core — `C:/Projects/Creatio2/TSBpm/Src/Lib` | `ProcessSchemaSubProcess`, `ProcessSchemaActivity`, `ProcessSchemaParameter`, `SubProcessProxy`, `SubProcessClassGenerator`, `BaseProcessSchemaManager`, plus the platform's own 9-method fixture |
| Classic designer — `C:/Projects/PackageStore/CrtProcessDesigner/branches/7.8.0` | `SubProcessPropertiesPage` (26 kB) and `RootUserTaskPropertiesPage` |
| NUI client resources | `process-subprocess-schema.js` — palette UId, size, properties page, serialized fields |
| Shipped corpus — `C:/Projects/PackageStore` | 1 099 packages; **262** metadata files, **420** sub-process elements, **73** packages |
| Package under change — `C:/Projects/workspace/ProcessBuilder` | The Preconfigured-page family as the architectural template; 7 existing element captures |
| MCP surface — `C:/Projects/clio` | **Eight** process-designer tools (two `[RequiresPackage]` floors), the `BlockExpectation` family, `ManagerMap`, the rebundle pins |
| Guidance — `C:/Projects/clio-knowledge` | 5 articles carrying read-only sub-process statements |
| Product docs — `academy.creatio.com` | The Sub-process element reference, the parameter-sync behaviour, and what it does **not** document |
| New designer — `C:/Projects/creatio-ui` | **Verified negative, twice** — render and re-type only, no selection, no sync, no `calledElement` |
| Platform test patterns — `C:/Projects/UnitTests` | **Zero** sub-process tests; the usable recipe is in `Terrasoft.Core.Tests` instead |

Note two corrections to the paths in the ticket's framing: the core sources are under
`C:/Projects/Creatio2/TSBpm/Src/Lib` (there is no `C:/Projects/Creatio/TSBpm` on this host), and
`C:/Projects/UnitTests` — offered as the mocking reference — contains no sub-process coverage at all.

---

## Decide before implementation starts

1. **The estimate — re-costed after the measurements and the Q1–Q10 decisions: effort 2.5–3 days,
   calendar 3–4.** The programme
   already assumes the AI writes the code, so the number is driven by what does not compress: the
   designer capture, V1–V8 on the stand, the review gates, and the ordering across **three**
   repositories (CrtProcessBuilder → clio → clio-knowledge, with the guidance release gating the clio
   merge). The ticket's 2.5 days is reachable now — but only because this analysis exists; see
   [plan §7](eng-92707-sub-process-element-plan.md).
2. ~~**The split.**~~ **Decided: no split.** Our re-sync is not a separate algorithm — the platform
   diffs and the package supplies ordering, guards and a snapshot. A `relates to` link to ENG-95461 was
   added so its shipped decisions stay discoverable (Q3).
3. ~~**AC2's dependency.**~~ **Decided: AC2 restated against what exists.** ENG-92127 gave the
   element↔process mapping and ENG-95891 gave formulas, so four mapping sources are available today;
   only ENG-91844's *specific* sources (`entityColumn`, `sysSetting`, `sysVariable`) are missing, and
   none is needed to call a sub-process (Q2).

Full list, with recommendations, in
[open-questions](eng-92707-sub-process-element-open-questions.md).

---

## Scope this analysis excludes on purpose

Multi-instance, the event/embedded sub-process, `UseLastSchemaVersion`, the Execution-method field, a
`list-processes` discovery tool, and promoting R16 into `ProcessGraphValidator` — each with its reason
in [plan §8](eng-92707-sub-process-element-plan.md).
