# Handoff — update the Confluence research article (Approach 1 is now implemented)

> Paste-ready brief for a FRESH session whose sole task is to update the Confluence page.
> Everything needed is below; deeper detail lives in the "Authoritative sources" at the end.

## The task
Update Confluence page **4702928908** — "Research: Add business process generation via AI instructions"
(space TER) — `https://creatio.atlassian.net/wiki/spaces/TER/pages/4702928908`.
- cloudId: `creatio.atlassian.net`
- Current version when this brief was written: **22** (last edit 2026-06-15: "Approach 1 §3 record package name").
- Tools: `mcp__atlassian__getConfluencePage` (read, `contentFormat:"html"`), `mcp__atlassian__updateConfluencePage`
  (write; bump version to 23, add a version message). **Read the live page first** — it may have moved past v22.
  Preserve the 3-approach structure, the metadata-example code block, and the embedded images (they still
  prove "don't hand-generate metadata"). Edit surgically; do not clobber unrelated sections.

## The core delta to land
The article frames **Approach 1** as researched + estimated (≈8–13 wks, "requires new backend development",
"design proceeds via the BMAD pipeline"). **That is now stale: Approach 1 is built (a working slice) and
validated live.** The update should move Approach 1 from *planned* → *implemented (slice) + proven on a stand*,
add a concrete capability/status section, revise the estimate framing (what's DONE vs the remaining cost
center), and record the new dedicated repo.

## Hard facts to cite

### Where the code lives now
- **Server package (the backend designer):** repo **`creatio.ghe.com/engineering/cli-process-builder`** (GHE),
  Creatio package **`clioprocessbuilder`**, service class **`ProcessDesignService`** (WCF). Local checkout:
  `C:\Projects\workspace\ProcessBuilder\packages\clioprocessbuilder`. (It started as an untracked package inside
  a Creatio FSD checkout; it now has its own tracked GHE repo — worth noting as the §3 placement made concrete.)
- **clio side (intent/orchestration + MCP):** `Advance-Technologies-Foundation/clio`, branch
  `feature/ENG-90883-approach1-backend-designer`, **PR #715** (https://github.com/Advance-Technologies-Foundation/clio/pull/715) —
  merged up to date with master, SonarCloud Quality Gate green.
- **Live environment used for validation:** krestov-test = `http://d_krestov_n.tscrm.com:1026` (.NET Framework / FSM stand).

### What is BUILT and working today (the new "status" content)
Server `ProcessDesignService` exposes 4 WCF operations (BodyStyle=Wrapped, `/rest/ProcessDesignService/<Method>`,
each gated by `CheckCanManageSolution`): **BuildProcess, ModifyProcess, ListUserTasks, DescribeProcess**.
clio surfaces them as 4 verbs + MCP tools: **create-business-process, modify-business-process, list-user-tasks,
describe-process** (plus `validate-process-graph` from the common core).

Capability matrix — **DONE**:
- Declarative descriptor → real, interpretable `ProcessSchema` built + saved server-side (no client metadata).
- Elements: **Simple start**, **Signal start** (record trigger: add / modify / delete — the platform-native
  alternative to a page save handler), **End**, **User task** (generic + any task from `list-user-tasks`, incl.
  custom; `readData`/`performTask` aliases; dedicated-palette-element specialization via `ManagerItemUId`).
- **Sequence flows**; **process-level parameters**; **element-parameter mappings** (bind to a process parameter,
  a constant, or a raw formula expression).
- **Automatic diagram layout** (topological longest-path: start leftmost, end rightmost, no overlap).
- **Edit existing processes** (`modify-business-process`): addElement / removeElement / addFlow / removeFlow,
  applied in one atomic SaveSchema; optional lane targeting.
- **Universal read-back** (`describe-process`): element runtime type, the specific user-task schema name, signal
  trigger, and value-bearing parameters — typed from the real object model (no GUID map), incl. custom tasks.

Capability matrix — **NOT yet built** (maps to the research "cost center" + beyond):
- Data-operation **filters & column config** (the escaped `FilterGroup`/`ParameterExpression` metadata in the
  article's example) — still the cost center, not implemented.
- Gateways, conditional/default flows, timer/message start, intermediate events, sub-process.
- Signal is **single-value only** (added | modified | deleted) — the designer's trigger dropdown is single-event;
  no combined "added or modified".

### Live proof (cite as evidence the slice works end-to-end)
On krestov-test, via the clio MCP, with no hand-written metadata:
1. `create-business-process` built **UsrTestRunButtonOnSave** — Signal start on `UsrTestRunButton` (on=modified) →
   Perform task ("Задача") → End. Verified by `describe-process` (signal entity/change, task type
   `ActivityUserTask`, flows, auto-layout positions).
2. `modify-business-process` inserted a second Perform task ("Задача 2") between the task and End (4 ops, one
   atomic save); auto-layout shifted End rightmost; re-verified by `describe-process`.

### Estimate reframe (for §3 "Estimate" / §6 comparison)
Research phases that are now effectively DONE: **2a structural designer** (start/end/user-task/sequence-flow,
lanes, validate, save), **2c signal trigger**, and the **clio + MCP glue**. What remains is the original
**cost center — 2b element configuration: data-operation filters + column mapping (FilterGroup/ParameterExpression)** —
plus gateways and **2d hardening** (versioning/permissions/edge cases/docs). So the "AC-covering slice minus deep
filters" is achieved and proven; the long pole is unchanged (filter/mapping serialization).

### Key technical findings worth a short "what we learned" note (optional, valuable)
- Mappings are applied by **assigning `ProcessSchemaParameter.SourceValue`** (the setter auto-syncs
  `schema.Mappings`), exactly as the visual designer does; a process-parameter reference is a formula token
  `[#…[Parameter:{uid}]#]`; set `Source` before `Value`.
- The signal trigger persists via the **MetaType property mechanism** (`WaitingEntitySignal` + `EntitySchemaUId`
  + `EntitySignal`), not the element's compact metadata — so assigning C# properties + SaveSchema is enough.
- Build = `CreateSchema` → construct on the instance → `SaveSchema`; edit = `DesignSchema` + `GetDesignInstance`
  → apply ops → `SaveSchema` (atomic; a failed op aborts the whole edit).
- **FSD caveat (important for the article's "hot deploy" claim):** on a file-design-mode stand a freshly built
  process is saved to the file system (the designer sees it) but is **not runtime-active until loaded FS→DB and
  published** — the signal won't physically fire until then. On a non-FSM (DB) stand SaveSchema is immediately
  runnable, so the "hot-deploy, no restart" claim holds for DB-mode environments.

## Suggested edit plan (surgical)
1. **§3 Approach 1** — add a subsection **"Implementation status (2026-06-xx)"** after the "Decision" block:
   state that the slice is built and validated (capability matrix DONE vs NOT-yet), name the dedicated repo
   `cli-process-builder` / package `clioprocessbuilder`, link PR #715, and the live-proof example.
2. **§3 Estimate table** — annotate which phases are delivered (2a/2c/glue) and that the remaining estimate is the
   2b filter/mapping cost center + 2d hardening (numbers themselves don't need to change — the *framing* does).
3. **§6 side-by-side** — for Approach 1, change "Where new work lands / Time to first value / Reliability" cells
   to reflect a delivered, deterministic-API slice (no longer purely prospective).
4. **§7 Recommendation** — update the Approach-1 bullet from "scope it" to "slice delivered & validated; remaining
   work is the filter/mapping cost center."
5. Leave Approaches 2 & 3, the "how BPs are built today" section, and the metadata example **as-is** (still valid).
6. Optionally add the FSD/hot-deploy nuance where the article claims zero-downtime hot deploy.

## Authoritative sources (read these in the new session if more detail is needed)
- `C:\Projects\clio\spec\process-design-service\process-design-service-state.md` — the running state/handoff
  (full feature list, contract, code-review follow-ups).
- `C:\Projects\clio\.codex\workspace-diary.md` — clio-side engineering diary (latest entries: merge, review fixes).
- `C:\Projects\workspace\ProcessBuilder\.codex\workspace-diary.md` — server-package diary (the 8 code-review fixes).
- The two source files: `…\clioprocessbuilder\Files\cs\ProcessDesignService.cs` + `ProcessDesignContracts.cs`.

## Out of scope for the article (dev hygiene, not research content)
The recall code-review and its fixes (clio quick-wins committed; 8 server findings fixed in `cli-process-builder`;
open clio follow-ups: describe-DTO `uid` sync, validate-vs-build vocabulary alignment) are tracked in the state-doc
— not needed in the research article unless you want a one-line "code-reviewed & hardened" note.
