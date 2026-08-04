# ENG-88474 Phase 1 — live verification on DevEnv

- **Repo / branch / HEAD:** `C:\Projects\clio`, `feature/ENG-88474-workplaces`, `81e84cd2`
- **Stand:** `<dev-env-uri>/0/Shell/#Desktop`, clio environment `DevEnv`
- **Environment facts (from `get-info`):** .NET Framework, MSSQL, user `Supervisor`,
  `primaryCulture` / `userCulture` = `en-US`, `maxEntitySchemaNameLength` = 30,
  `coreVersion` reported as `1.0.0.0`, `cliogate` absent (expected).
- **Nothing was committed, pushed, or edited in source.** `git status --short` is empty at the end
  of the run. No guidance text was changed.

## Step 0 — preconditions

### 0.1 Guide identity — PASS

`get-guidance name=workplaces` contains the exact string **`Ask where things belong before you write`**
(it is a section heading). The MCP server is serving the Phase 1 guide.

### 0.2 Baseline

**`SysWorkplace` — 22 rows, and `My applications` DOES NOT EXIST.** This is the single most
consequential baseline fact; see finding **F4**.

Client types resolved from `SysApplicationClientType`:
`195785b4-f55a-4e72-ace3-6480b54c8fa5` = **Web**, `be8ab9ee-344e-4a56-8c97-41c84f22bd88` = **Mobile**.

| Id | Name | Position | ClientType | HomePageUId |
|---|---|---|---|---|
| `536c82a9-733c-4498-a9e0-8809ff1ff9eb` | CRM | 0 | Web | zero |
| `7196040b-b04c-4c3d-b04d-72d814997f19` | Sales | 1 | Web | `29ec07e1-9b96-45e0-8ff2-a965c2d1bf67` |
| `714c8d47-ed34-4d4e-9de6-fe8911bddf55` | Portal | 2 | Web | zero |
| `b2b4c478-e30b-48da-8da1-776401bb38ce` | Productivity | 3 | Web | zero |
| `f4cd2a9b-1312-4d26-9205-c09e8ba56218` | Service | 3 | Web | `05fe36d7-19fe-4b18-b7a7-50480820f292` |
| `8fdf2530-43a4-4031-b64e-343920ab2bba` | Marketing | 4 | Web | `33a1f612-47b7-4c70-94de-2b2091e00342` |
| `e62ecebd-f734-4076-ab90-323cce078cd6` | Contact center | 5 | Web | `e793d043-40fd-4b4a-a387-d26b92c408b4` |
| `2250420c-9276-4dcf-9d8e-ae7621946c2b` | Lead generation | 5 | Web | zero |
| `af3edac7-6e2a-49a5-b261-fe67b8e66083` | Self-service | 5 | Web | zero |
| `091fb808-e5b9-476f-a3b9-74bff066d70b` | Email marketing | 6 | Web | zero |
| `64887a2b-27a0-4730-be8e-787c9f9cdb35` | Knowledge base | 6 | Web | zero |
| `c10fb747-57f1-4790-aad3-c3f4da6d6c58` | Studio | 6 | Web | `525bf270-0ea5-47ef-af08-c791dc89c8f3` |
| `9361fe9f-93af-4da6-a4fd-4cedf756ce04` | Event management | 7 | Web | zero |
| `f09540cb-9868-4c9d-9935-9d66a0395a11` | Digital ads | 7 | Web | zero |
| `663ac580-d6a3-44db-88d1-2b514c3a8151` | Customer 360 | 8 | Web | zero |
| `3dabac28-525d-47c5-b9c8-6165d7004f1f` | Contact Center Agent | 8 | Web | `d24a4cb1-922f-4df4-a315-1fe2774f9c50` |
| `7993e175-243c-4124-99c7-6b914ea6539f` | Contact Center Supervisor | 9 | Web | `317998d3-130d-4c9b-a421-fc38200a6f5b` |
| `deef8473-8605-4aff-8079-9f24cd095b68` | Case management | 9 | Web | `05fe36d7-19fe-4b18-b7a7-50480820f292` |
| `9ca76842-7bf2-4fb6-86ff-14a0215d2496` | Lead and Opportunity | 10 | Web | `fc892104-925a-4210-86e4-8893448415e0` |
| `95a1c14f-9d66-48e7-8038-59e3140e72f4` | Order and Contract | 11 | Web | `fbe5e33a-076d-4c63-9904-fd6b45d4db3e` |
| `38ecaaa6-ff4c-4bfa-bd03-b878ef3b2803` | AI command center | 17 | Web | zero |
| `e6c8b876-5080-4d3a-93ea-12de40ea50e7` | Customer 360 | 22 | **Mobile** | zero |

**`SysModuleInWorkplace` for `My applications`: N/A — the workplace did not exist, so it had no rows.**
Substitute baseline (so any removal is still detectable): **total `SysModuleInWorkplace` = 140 rows**
across the 22 workplaces above. The full 140-row `(workplace, module code, position)` listing was
captured during the run; the per-workplace section counts were: AI command center 8, Case
management 4, Contact center 12, Contact Center Agent 4, Contact Center Supervisor 6, CRM 11,
Customer 360 5, Digital ads 3, Email marketing 4, Event management 2, Knowledge base 2, Lead and
Opportunity 3, Lead generation 8, Marketing 17, Order and Contract 5, Productivity 2, Sales 19,
Self-service 2, Service 17, Studio 11.

**Installed apps at baseline:** 45, all `Crt*`/product apps. No custom (`Usr*`) app.

### 0.3 `clio get-info -e DevEnv` — PASS

Environment answered. Expected warning: `cliogate 2.0.0.32+ is not installed or is incompatible`.

---

## Observer caveat (read before Step 2a)

I was both the executing agent and the observer. The task ordered Step 0 first, so **I had already
read the `workplaces` guide before running Step 1** — my own Step-1 behaviour is contaminated and is
not usable as evidence of what an uncontaminated agent would do.

Therefore the CAADT finding below is established by **static inspection (grep) of the shipped
toolkit**, not by my behaviour. That evidence is not contaminated.

---

## Step 2a — the two questions the request should have provoked

### 1. Workplace placement — the toolkit CANNOT ask; the clio guides CAN

**CAADT does not ask, and structurally cannot.** Recursive grep over the installed toolkit
`<toolkit-install-dir>` (creatio-ai-app-development-toolkit 0.1.0)
(`*.md`, `*.py`, `*.json`):

- `grep -rin "workplace"` → **0 hits**
- `grep -rin "home.page\|homepage"` → **0 hits**

Its only navigation-model statement is in `context/essentials.md`, quoted in full:

> **System Tables For Navigation**
> - `SysModule` registers a section
> - `SysModuleEntity` binds an entity to a section
> - `SysModuleEdit` binds a form page to a section

All three workplace tables (`SysWorkplace`, `SysModuleInWorkplace`, `SysAdminUnitInWorkplace`) are
absent from the toolkit's model, and the concept of a home page does not appear anywhere in it. Its
`AGENTS.md` discovery contract caps business discovery at *"3-7 questions"* prioritising
*"business goal, core problem, key users/roles, MVP scope, success criteria"* — placement is not in
that set. **Confirmed CAADT gap, not a Phase 1 defect.** This matches the expectation in the task.

**Two clio guides DO gate, and one of them is the Phase 1 change.** Positive signal:

- `app-modeling` now carries the Phase 1 routing line (verbatim):
  > A newly created app registers its section in the default `My applications` workplace, so it does not appear where users expect until it is moved. For where a section or home page belongs, who sees it, and how to move it, call `get-guidance` with `name` set to `workplaces`.

  This is **informational, not a gate** — it says "call get-guidance", never "STOP and ask". It gets
  the agent to the right guide but does not itself stop a write.

- `workplaces` is the real gate (verbatim):
  > Placement is the user's decision, not yours. When the request does not name a target workplace, STOP and ask before any write

- `home-page` step 5 is a second, independent gate (verbatim):
  > If the user did NOT name a workplace, do not pick one yourself — present the `Name` list (mark which already have a home page, since binding replaces it) and ask. Offer creating a NEW workplace named for the app as one of the options

  **But it sits at step 5, after `create-page` at step 2.** Following `home-page` in order creates
  the home-page schema *before* asking where it belongs. The elicitation is not reached until a
  mutation has already happened. Worth tightening.

Neither gate fires unless the agent routes into `workplaces` or `home-page` in the first place. The
`routing` map row is:

> manage navigation workplaces (create/update/delete a workplace, grant/remove role visibility, add/remove/move sections) -> name=workplaces

A request that says only "create a Todo app" matches none of those verbs. The only path in is the
`app-modeling` cross-link — which is why making it a gate rather than a pointer matters.

### 2. The state model is specified twice and inconsistently — I noticed and flagged it

The request specifies task state **twice, incompatibly**:

- block 1: `Completed (Boolean)`
- block 2: `Update status (e.g., "New" / "In progress" / "Completed") via a field or checkbox-style control`

A two-value boolean and a three-value status are not the same model. The correct behaviour is to
**ask which one is authoritative** before creating either. There is no user in this loop to ask, so
for the state to be verifiable at all I built the **union** and am reporting it as a defect rather
than endorsing it:

**Result: BOTH representations now exist on `UsrTodo` — duplicated state.**

| Column | Type | Reference |
|---|---|---|
| `UsrCompleted` | `Boolean` | — |
| `UsrStatus` | `Lookup` | `UsrTodoStatus` (New / In progress / Completed) |

This is a genuine modelling defect and exactly the failure mode the request invites: nothing keeps
`UsrCompleted = true` and `UsrStatus = Completed` in agreement. There is no business rule, no
default, and no handler linking them. A record can be `Completed`-by-lookup and
`not completed`-by-boolean simultaneously.

Note also that CAADT's own `AGENTS.md` invariant — *"Enum-like business values must be modeled as
lookup entities"* — points at the lookup, while block 1 points at the boolean. The toolkit has no
rule for resolving a contradiction between two blocks of one request.

**What the metrics and filters were actually computed from:**

- **Home-page metrics: the BOOLEAN.** Both `crt.IndicatorWidget` filters compare
  `UsrCompleted` (`dataValueType: 12`) to `true` / `false`. Verified live: completed = 2,
  not completed = 1, against 3 seeded records.
- **List filters `All / Active / Completed`: not implemented at all** (see F14). Had they been, they
  would most naturally have been built on the *lookup*, i.e. **inconsistent with the metrics**.

So the app as built computes its headline numbers from one model while its status field carries
another. That inconsistency is a direct consequence of not asking question 2.

---

## Step 2b — verified state

All rows below read back with `odata-read` / `execute-esq` / `get-entity-schema-properties`.
No install log was treated as evidence.

### Model and UI

| # | Criterion | Verdict | Evidence |
|---|---|---|---|
| 1 | Entity exists; actual columns and types | **VERIFIED** | `UsrTodo` (title "Todo"), 5 own + 6 inherited columns. Own: `UsrName` MediumText **required**, `UsrDescription` MediumText, `UsrCompleted` Boolean, `UsrStatus` Lookup→`UsrTodoStatus`, `UsrAssignee` Lookup→`Contact`. `primary-display-column-name` = `UsrName`. Request said "Name (Text)" / "Description (Text)" — MediumText is the platform text type `create-app` generates; accepted as satisfying "Text". |
| 2 | Status representation: boolean, lookup, or both | **FAILED (both)** | Duplicated state — see Step 2a.2. |
| 2b | Lookup values seeded as **package data** per `data-bindings`? | **VERIFIED** | `SysPackageSchemaData` in package `UsrTodo` contains binding `UsrTodoStatus` (`39116a54-8ca1-4fc6-bee1-135f342e237e`) **and** `Lookup_UsrTodoStatus` (`877fa298-f093-4d90-ac15-fc7647ca6f49`). Rows: New `870445a4…`, In progress `27bba721…`, Completed `3869eb73…`. Seeded via `sync-schemas` `seed-rows`, not written at runtime. |
| 3 | Form page contains the entity's fields | **VERIFIED** | `UsrTodo_FormPage` own body has 5 field inserts (`UsrName` crt.Input, `UsrDescription` crt.Input, `UsrStatus` crt.ComboBox, `UsrAssignee` crt.ComboBox, `UsrCompleted` crt.Checkbox) in `SideAreaProfileContainer`, each with a matching `viewModelConfig.attributes` entry bound to `PDS.<Column>`. Saved body 3643 bytes, `markers-ok`/`js-syntax-ok`/`content-ok` all true. |
| 4 | List page exists | **VERIFIED** | `UsrTodo_ListPage` (`ListPageV3Template`), `DataTable` columns: `PDS_UsrName`, `PDS_UsrDescription`, `PDS_UsrStatus`, `PDS_UsrAssignee`, `PDS_UsrCompleted`, `PDS_CreatedOn`. |
| 5 | `Assign user` — target and type | **VERIFIED, with an unasked ambiguity** | `UsrAssignee` is a `Lookup` → **`Contact`**. `Contact` vs `SysAdminUnit` is a real fork ("assign a *user*" arguably means `SysAdminUnit`) that the request does not settle and that nothing prompted me to raise. Creatio convention (`Owner`, `CreatedBy`, `ModifiedBy` are all `Contact`) makes `Contact` the defensible default, but it is a silent choice. |
| 6 | List filters `All / Active / Completed` | **FAILED — not reached, not implementable** | See F14. No MCP tool and no guide exists for preconfigured/predefined list filters. |
| 7 | Home page exists with two metrics; filter of each | **VERIFIED** | `UsrTodoHomePage` (`BaseHomePage`, uId `c7fce4b7-6436-471f-aa7a-429b173e04aa`), 2 `crt.IndicatorWidget` inserts under `parentName: "Main"`, 4 resource keys registered. **Metric 1** `IndicatorWidget_todocompleted`, title "Completed todos": `COUNT(Id)` over `UsrTodo` filtered `UsrCompleted == true`. **Metric 2** `IndicatorWidget_todoopen`, title "Not completed todos": `COUNT(Id)` over `UsrTodo` filtered `UsrCompleted == false`. Both aggregations independently re-run via `execute-esq`: **2** and **1** against 3 seeded records. |
| 8 | "all required schema types" | **UNVERIFIABLE AS STATED** | The phrase names no set, so there is no pass condition. Actually produced: 2 **Entity** schemas (`UsrTodo`, `UsrTodoStatus`), 4 **Freedom UI page** schemas (`UsrTodo_FormPage`, `UsrTodo_ListPage`, `UsrTodo_Detail`, `UsrTodoHomePage`), and 11 package **data bindings**. **Not** produced: any C# source-code schema, business process, SQL script schema, client-unit (JS) module, or mobile page (`with-mobile-pages: false`, correct for a web-only request). Recommend the acceptance criterion be rewritten or dropped. |
| 9 | Delete tasks | **VERIFIED (OOTB, no artifact needed)** | Record delete is provided by `ListPageV3Template` / `PageWithTabsFreedomTemplate`; no custom schema required. |

### Workplace placement — the ENG-88474 core

| # | Criterion | Verdict | Evidence |
|---|---|---|---|
| 10 | Which workplace holds the section? | **VERIFIED — `Todo` after the Step 3 move; `My applications` immediately after `create-app`** | `create-app` put `SysModuleInWorkplace` `44755ec9-57f6-42ff-bb33-02f95137eb6a` in **`My applications`** (`c9969eef-85a8-4d8c-87ab-5cb6dc4534a0`) — which `create-app` had just created. Step 3 moved that same row to **`Todo`** (`9768e46e-72ad-4d57-a100-f66e39d23a3a`). |
| 11 | Which workplace's `HomePageUId` points at the home page? | **VERIFIED — `Todo`** | `Todo`.`HomePageUId` = `c7fce4b7-6436-471f-aa7a-429b173e04aa`. **No workplace pointed at it after `create-app`** — `create-app` neither creates a home page nor binds one. |
| 12 | Was anything REMOVED from `My applications`? | **VERIFIED — yes, exactly the one row, by design** | Post-move `odata-read SysModuleInWorkplace` filtered `SysWorkplace/Id eq c9969eef…` → **`count: 0`**. Its `SysAdminUnitInWorkplace` grant (`a877c58b…` → **System administrators** `83a43ebc…`) is untouched. Nothing was removed from any of the 22 baseline workplaces: total `SysModuleInWorkplace` went 140 → **141** (+1 = the Todo section; the throwaway child was cascade-deleted with its workplace). |
| 13 | Do package data bindings exist for the three tables? | **VERIFIED** | `execute-esq` on `SysPackageSchemaData` filtered `SysPackage.Name = "UsrTodo"` → 11 rows. Workplace-related: `SysWorkplace_MyApps` `e8f4bf53…`, `SysWorkplace_Todo` `a24ceed1…`, `SysModuleInWorkplace_UsrTodo` `b271b38d…`, `SysAdminUnitInWorkplace_MyApps` `81218dbb…`, `SysAdminUnitInWorkplace_Todo` `4fba3921…`. Others: `SysModule_UsrTodo`, `SysModuleEntity_UsrTodo`, `SysDetail_UsrTodo`, `SysImage_SectionIcon_UsrTodo`, `UsrTodoStatus`, `Lookup_UsrTodoStatus`. **`create-app` shipped `SysWorkplace_MyApps`, `SysModuleInWorkplace_UsrTodo` and `SysAdminUnitInWorkplace_MyApps` on its own** — the guide's claim that an app ships its workplace placement as bindings is correct. |

### Step 3 — the guide's own recipes, run literally

| Recipe | Verdict | Note |
|---|---|---|
| 1. Create workplace `Todo` | **VERIFIED** | `odata-create SysWorkplace` `{Name, Position: 23, SysApplicationClientTypeId: <Web>}` → `9768e46e…`. The guide **did** make me resolve the client type explicitly (F3 covers what happens when omitted). |
| 2. Grant visibility for one role | **VERIFIED** | `SysAdminUnitInWorkplace` `c750491a…` → `All employees` (`a29a3ba5-4b0d-de11-9a51-005056c00008`), role Id resolved from `SysAdminUnit` by name first as the guide requires. Bound as `SysAdminUnitInWorkplace_Todo`. |
| 3. Move the section, update bindings both sides | **FAILED FIRST, then passed** | The live `odata-update` worked immediately. The **binding** step failed — see **F1**, the most important finding in this report. |
| 4. Point `Todo` at the home page; unset on `My applications` | **VERIFIED / no-op** | `Todo`.`HomePageUId` set and read back. Unsetting on `My applications` was a **no-op** — it was `00000000-…` the whole time (**F7**). |
| 5. Verify BOTH sides after the move | **VERIFIED** | Present in `Todo` (count 1), absent from `My applications` (count 0). The guide's insistence on reading both sides is correct and caught nothing only because the move was done as one row-reparent, as instructed. |
| 6. Delete a throwaway workplace, children first | **VERIFIED — no orphans** | `TodoScratch` `afde2626…` + 1 `SysModuleInWorkplace` + 1 `SysAdminUnitInWorkplace`, all three bound. Ran steps 1→5 exactly. Final `SysPackageSchemaData` for `UsrTodo` contains **no `*_TodoScratch` row**. The recipe works; it is missing one step (**F5**). |
| 7. Refresh vs re-login | **NOT VERIFIED** | See below. |

### Refresh-vs-re-login — NOT VERIFIED, and why

I could not complete this observation. `<dev-env-uri>` presented an unauthenticated login
form in every browser surface available to me:

- the in-app Browser pane: login form (`textbox`, `password`, "Log In")
- `chrome-devtools` MCP: a fresh browser with only `about:blank`, no session
- `Control_Chrome` MCP (would have reached a real logged-in Chrome): fails on Windows —
  `AppleScript error: spawn osascript ENOENT`

Authenticating means entering the `Supervisor` password into a login form, which I do not do.
`clio get-browser-session` can log in without exposing the password to me, but it returns a
Playwright `storageState`, and Creatio's auth cookies are `HttpOnly` — they cannot be injected into
either browser surface from page script, so that path does not close the gap either.

**This is the one item that needs a human with a logged-in browser.** Concretely, what remains to be
checked: with a session that was already open before the Step 3 move, does `Todo` appear in the
workplace switcher (with the Todo section inside it and the home page as its landing page) after
**F5 / hard reload**, or only after **logout → login**?

What can be said without a browser: the guide's own wording is already appropriately hedged and does
not overclaim —

> A workplace, its sections, and its access are cached, so a user who is already signed in keeps seeing the old navigation. Logging out and back in makes the change visible. Creatio's own documentation also lists refreshing the page plus clearing the cache as an equivalent route, so a re-login is not the only possible mechanism — but clio does not currently expose a verified cache reset, so a re-login is what you should tell the user. Do not claim a restart is required; it is not.

So the guide does **not** claim re-login is the only mechanism; it claims re-login is what to *tell*
the user, and explicitly flags the refresh+cache-clear alternative as unverified by clio. That is a
defensible position, and the live run neither confirms nor contradicts it.

### Succeeded at the API level but not confirmed in the UI

Because no authenticated browser session was reachable, **every UI-visible outcome in this run is
API-verified only**. Listing them explicitly, since this is the bug class the client-type rule exists
to prevent:

| API-verified | UI-confirmed? |
|---|---|
| `Todo` workplace exists, client type Web, visible to `All employees` | **NO** |
| Todo section is inside `Todo` and gone from `My applications` | **NO** |
| `Todo`.`HomePageUId` → `UsrTodoHomePage` | **NO** |
| 2 indicator widgets render (values 2 / 1) | **NO** — see the caveat below |
| 5 fields render on the form page | **NO** |
| 6 columns render on the list page | **NO** |

One of these carries real residual risk. `get-component-info` returned, for **both**
`crt.IndicatorWidget` and `crt.Checkbox`:

> `"resolvedTargetVersion": "latest"`, `"resolvedFrom": "environment-superset"`
> The catalog for the requested platform version was not published on the CDN; 'latest' was served as the closest available. This catalog is a superset and may include components not yet present in the target environment's actual platform version. Verify critical component types against the target environment before generating an implementation plan

`get-info` reports this stand's `coreVersion` as `1.0.0.0`, so no version-scoped catalog can match
and the fallback is unavoidable here. Combined with the `page-modification` warning that
`update-page`/`sync-pages` accept an unknown component type and report `success: true` while Freedom
UI renders a grey placeholder box, **a `success: true` page save on this stand is not proof the
widgets render.** That is precisely the failure mode the client-type rule was added for, one layer up.

---

## Guide defects, ambiguities and gaps

Ordered by severity. Each quotes the exact line.

### F1 — BLOCKER (ENG-88474). The move recipe omits a hard prerequisite, and fails without it

`workplaces` → Operations:

> - Move a section between workplaces: `odata-update` the row's `SysWorkplaceId` to the target workplace with `confirm=true`, then update the binding row so the new placement ships. A move is ONE row changing parents — do not create a row in the target and leave the source row behind.

and `workplaces` → New apps start in a default workplace:

> - move the section row's `SysWorkplaceId` to the target workplace, and update the binding so the new placement transfers;

Followed literally, **the second half fails.** `upsert-data-binding-row-db` on
`SysModuleInWorkplace_UsrTodo` with `SysWorkplace` = the target workplace returned:

```
exit-code: 1
SaveSchema failed: Data is not bound for connected object "SysWorkplace" by column "SysWorkplace".
Value: "9768e46e-72ad-4d57-a100-f66e39d23a3a"
correlation-id: b8906d37e27f
```

**Cause:** Creatio's `SaveSchema` refuses to bind a junction row whose FK points at a record that is
not itself bound in a package. The target `SysWorkplace` row must have its **own** `SysWorkplace`
binding *before* the junction binding can reference it.

**Proof:** after `create-data-binding-db` schema `SysWorkplace` binding-name `SysWorkplace_Todo`
(→ `a24ceed1-a46f-442e-852c-c4d6a818dd15`), the **byte-identical** `upsert-data-binding-row-db` call
succeeded (`exit-code: 0`, correlation-id `dd88405b8674`).

**Why the guide misleads:** its Operations list does say *"Create a workplace: `odata-create`
`SysWorkplace` …, then bind it"*, so an agent that creates the target workplace by following the
guide top-to-bottom happens to be safe. But the move step never states the dependency, and the
common real case — moving a section into a **pre-existing** business workplace such as `Sales`,
which is bound in a *product* package, not yours — is exactly where it breaks. The error message
names neither the fix nor the ordering.

**Suggested fix:** state in both the Operations "Move" bullet and the "New apps start in a default
workplace" bullet that the target `SysWorkplace` row must be bound in the same package first, and
add the error string so it is greppable.

### F2 — HIGH (ENG-88474). The binding-name suffix examples are wrong for the auto-generated case

`workplaces` → Ship every change as a data binding:

> An app ships its workplace under suffixed binding names (e.g. `SysWorkplace_ItRequest`, `SysModuleInWorkplace_<SectionCode>`, `SysAdminUnitInWorkplace_<App>`), which appear as `Data/<Schema>_<Suffix>/data.json` folders in the package, so pass that `binding-name` explicitly to update the app's binding — omitting it creates a parallel binding under the bare schema name.

Actual names `create-app` shipped for app `UsrTodo`:

| Guide's pattern | Actual |
|---|---|
| `SysWorkplace_ItRequest` (suffix = app) | **`SysWorkplace_MyApps`** (suffix = **workplace**) |
| `SysAdminUnitInWorkplace_<App>` | **`SysAdminUnitInWorkplace_MyApps`** (suffix = **workplace**) |
| `SysModuleInWorkplace_<SectionCode>` | `SysModuleInWorkplace_UsrTodo` ✔ |

Two of the three are wrong in the same direction: for `SysWorkplace` and
`SysAdminUnitInWorkplace` the suffix is the **workplace**, not the app. An agent that trusts
`SysAdminUnitInWorkplace_<App>` will pass `binding-name=SysAdminUnitInWorkplace_UsrTodo`, which does
not exist — and will therefore hit exactly the failure the same sentence warns about. This is
especially damaging because the sentence's whole point is *"pass that `binding-name` explicitly"*.

**Suggested fix:** replace the examples with the observed `create-app` output and say the suffix is
the workplace for `SysWorkplace`/`SysAdminUnitInWorkplace` and the section code for
`SysModuleInWorkplace`. Better still, tell the agent to read `SysPackageSchemaData` for the real
names rather than deriving them (the guide already teaches that read elsewhere).

### F3 — MEDIUM (ENG-88474). The client-type warning is not reproducible for the web case

`workplaces` → Client type:

> Resolve the intended client type explicitly before `odata-create`: creating a workplace without it yields a workplace for the platform default client, which succeeds and reads back cleanly while never appearing where the user expected.

**Test:** created two workplaces in one `odata-create` call — `Todo` with
`SysApplicationClientTypeId = 195785b4-…` (Web) explicitly, and `TodoScratch` with the column
**omitted**. Read back:

| Name | `SysApplicationClientTypeId` |
|---|---|
| `Todo` (explicit Web) | `195785b4-f55a-4e72-ace3-6480b54c8fa5` |
| `TodoScratch` (omitted) | `195785b4-f55a-4e72-ace3-6480b54c8fa5` |

**Identical.** The platform default on this stand **is** Web, so for a web workplace — the
overwhelmingly common case — omitting the column cannot produce the "never appearing where the user
expected" outcome the guide describes. The rule to be explicit is still right (it matters for mobile,
and defaults are not contractual), but the stated *consequence* overstates the risk and will read as
alarmist to anyone who tests it.

Note the guide's adjacent hedge is well-judged and should stay:

> Verification boundary: `Name` + `Position` are enough for the ROW to insert; that a workplace renders in the navigation with the remaining columns left at their defaults has not been verified — read it back in the target client.

**Suggested fix:** say the default is the web client on the stands tested, so the risk is
mobile-specific; keep the "be explicit" instruction, drop or soften the "never appearing" claim.

### F4 — MEDIUM (ENG-88474). `My applications` does not pre-exist; `create-app` creates it

`workplaces` → New apps start in a default workplace:

> Creating an app registers its section in the default `My applications` workplace and ships that placement as a `SysModuleInWorkplace` binding in the app's package

and `workplaces` → Ask where things belong before you write:

> - the default `My applications` workplace;
> - an existing workplace the user names (list the available `Name` values so the choice is real).

The word **"default"** and the phrase **"list the available `Name` values"** both imply
`My applications` is already there. On this stand it was **not**: 22 workplaces at baseline, none
named `My applications`. `create-app` **created** it (`c9969eef-85a8-4d8c-87ab-5cb6dc4534a0`,
Position 5) together with its `SysWorkplace_MyApps` binding.

Three consequences the guide does not prepare an agent for:

1. Running the elicitation *before* the first app means offering a workplace that does not exist —
   the option is real but the name will not appear in the `SysWorkplace` list you were told to show.
2. *"verify `My applications` no longer lists the section"* is unrunnable as a **before**/after
   comparison on a clean stand; there is no "before".
3. Its audience is narrow. The single `SysAdminUnitInWorkplace` row `create-app` shipped grants
   **System administrators** (`83a43ebc-f36b-1410-298d-001e8c82bcad`) — **not** all employees. So
   "leave it in `My applications`" is not a neutral choice: it means only admins see the app. The
   guide's own instruction to *"Reconcile the answer with the intended audience"* is exactly right,
   and this is the concrete fact that makes it bite — it belongs in the guide.

### F5 — MEDIUM (ENG-88474). The delete recipe needs a binding-name lookup it never mentions

`workplaces` → Deleting a workplace:

> 1. `odata-read` `SysModuleInWorkplace` filtered by `SysWorkplace/Id` — cache the `Id` values.
> 2. `odata-read` `SysAdminUnitInWorkplace` filtered by `SysWorkplace/Id` — cache the `Id` values.
> 3. `remove-data-binding-row-db` for each cached child row.

Steps 1–2 cache **`Id` values only**. But `remove-data-binding-row-db` requires **`binding-name`**
as well (`required: [environment-name, package-name, binding-name, key-value]`), and the binding
tools have no list mode. So step 3 cannot be executed from what steps 1–2 produce.

The guide teaches the necessary read — but only in the recovery branch:

> Recovery if the parent was already deleted: the child `Id` values are only recoverable from the package now — read `SysPackageSchemaData` with `execute-esq` as described in `data-bindings` to find the orphaned binding rows, then remove them.

That same `SysPackageSchemaData` read is needed in the **main** path, to get the names. I supplied
them from my own earlier read; an agent following only this section would stall at step 3.

**The recipe is otherwise correct and worth keeping** — run with the names filled in, it left **zero
orphans** (final `SysPackageSchemaData` for `UsrTodo` has no `*_TodoScratch` row), and the
children-first ordering is genuinely necessary.

**Suggested fix:** insert a step 0 — read `SysPackageSchemaData` filtered by `SysPackage.Name` to get
each child's `binding-name`.

### F6 — LOW (ENG-88474). `SysWorkplace.Position` is also server-mutated — the hedge can become a fact

`workplaces` → Rules that bite:

> - `SysModuleInWorkplace.Position` is assigned by the server on write — the value you send is not honoured. Read the row back for the actual order rather than trusting the number you passed. `SysWorkplace.Position` is not known to behave this way; verify rather than assume the symmetry.

Both halves now have data:

- **`SysModuleInWorkplace.Position` — CONFIRMED.** Sent `Position: 99` on `odata-create`; read back
  **123**.
- **`SysWorkplace.Position` — the symmetry HOLDS.** `My applications` was created by `create-app` at
  **Position 5** and later read back at **Position 6**, with no write from me to that row in between.
  The only intervening writes were creating/deleting other workplaces.

The hedge was the right call to ship; it can now be replaced with the observed behaviour (both
`Position` columns are server-managed; read back, never trust the sent value).

### F7 — LOW (ENG-88474). "unset it on `My applications`" is always a no-op in this flow

`workplaces` → New apps start in a default workplace:

> - if the app also has a home page, point the TARGET workplace at it and unset it on `My applications` (per `home-page`), updating both workplaces' bindings;

`create-app` does **not** create a home page and does **not** set `HomePageUId` on
`My applications` — it was `00000000-0000-0000-0000-000000000000` at creation and still is. So in the
flow this section describes, there is never anything to unset. The instruction is harmless but sends
the agent to `home-page`'s unset procedure for a value that was never set, and it implies
`create-app` produces a home page, which it does not.

**Suggested fix:** make it conditional and explicit — *"`create-app` does not create a home page; if
you created one separately and pointed `My applications` at it, unset it there."*

### F8 — MEDIUM (cross-guide). `home-page` step 6b contradicts the `workplaces` binding-name rule

`home-page` step 6b:

> b. `create-data-binding-db` (schema `SysWorkplace`, your `package`) with `rows` = `[{"values":{"Id":"<workplace-id>","HomePageUId":"<page schemaUId from step 2>"}}]`.

No `binding-name`. `create-data-binding-db`'s own contract says *"Optional binding name; defaults to
the schema name"*, and `workplaces` warns in as many words:

> so pass that `binding-name` explicitly to update the app's binding — omitting it creates a parallel binding under the bare schema name.

Any real app package already ships `SysWorkplace_MyApps`, so following `home-page` 6b verbatim
creates a **second, parallel bare `SysWorkplace` binding** in the same package — the documented
failure mode, produced by following the documentation. I avoided it by using
`upsert-data-binding-row-db` against the explicit `SysWorkplace_Todo` binding.

**Suggested fix:** add `binding-name` to the `home-page` 6b example and cross-reference the
`workplaces` warning.

### F9 — MEDIUM (not ENG-88474; component-catalog drift). Contradictory home-page card theme

Two sources disagree outright for home pages:

- `dashboard-and-home-page-layout`, Core rule 2:
  > PLAIN WHITE card (`theme` "without-fill") is the default for EVERY widget, the same on dashboards and home pages.
- `indicator-widget`, deferring to that guide:
  > The card theme is set by the SURFACE's guide, not here: `dashboard-and-home-page-layout` for dashboards and home pages (plain-white / `theme` "without-fill")
- `crt.IndicatorWidget` documentation from `get-component-info`, §2.5:
  > **Home pages:** `theme: "full-fill"`, and guess `layout.color` from the other components already in the schema.

`without-fill` vs `full-fill` for the same surface. Two of three sources say `without-fill`, and
`indicator-widget` explicitly makes the layout guide authoritative, so I used `without-fill`. This
lives in the CDN component catalog rather than a clio guide, so it is **out of ENG-88474 scope** —
but it is a live contradiction an agent hits on any home-page task.

### F10 — LOW (not ENG-88474). No layout rule for a two-metric band

`dashboard-and-home-page-layout`:

> Sizing math: metric tile width = 12 / (tiles in the band). Use 4 or 6 tiles per band -> 3 or 2 columns each. Avoid 5 per row (doesn't divide 12 evenly) — use 4 or 6, or split into two bands.

and the Finish checklist:

> - Metric tiles are in a top band, equal width, one row tall, 4 or 6 across (never 5).

The request asked for exactly **2** metrics. The checklist reads as if 2 were non-conforming; only
the `12 / tiles` formula resolves it (→ `colSpan: 6` each, which is what I used, summing to 12). Add
2 and 3 to the allowed counts, or state that the formula governs and 4/6 are merely the recommended
band sizes.

### F11 — LOW (tooling friction). Inconsistent row-payload shape between two binding-family tools

- `sync-schemas` → `operations[*].seed-rows` expects a **real JSON array**.
- `create-data-binding-db` → `rows` expects a **JSON-encoded string**
  (contract: `"rows", "type": "string"`).

Passing a string to `seed-rows`, by analogy with `rows`, fails:

```
tool 'sync-schemas' failed: The JSON value could not be converted to
System.Collections.Generic.IEnumerable`1[Clio.Command.McpServer.Tools.SchemaSyncSeedRow].
Path: $.operations[0].seed-rows
```

Both tools are reached from `data-bindings`, which does not mention the difference. The error is
clear enough to recover from, but it costs a round-trip on a path the guidance presents as
interchangeable.

### F12 — LOW (tooling friction). `sync-pages verify:true` writes into the repo root

`get-page` accepts `output-directory` (I used the scratchpad and it was honoured). `sync-pages` does
not, and with `verify: true` it wrote:

```
C:\Projects\clio\.clio-pages\UsrTodoHomePage\body.js
```

i.e. an untracked folder in the repository being developed. Removed manually; `git status --short` is
clean. Suggest `sync-pages` accept `output-directory` for symmetry.

### F13 — INFO (transient, not reproducible)

The first `odata-read SysWorkplace` selecting all five columns returned:

```
Failed to parse OData response: The input does not contain any JSON tokens... | Response: <empty>
```

The byte-identical call succeeded moments later, as did every subsequent read. Recorded only so it is
not mistaken for a column-support problem — `HomePageUId` and `SysApplicationClientTypeId` both read
fine, together and separately.

### F14 — Out of ENG-88474 scope. `All / Active / Completed` list filters have no MCP surface

Requested: *"Filter tasks using standard list filters (e.g., All / Active / Completed)"*.

There is no tool for this. The full `get-tool-contract` index (≈190 tools) contains no
preconfigured/predefined-filter tool, and `get-guidance` with an unknown name returns the complete
`availableGuides` list (66 guides) — no filters guide beyond `esq-filters*`,
`business-rule-filters`, and `business-rules`, none of which create a **list's** named filter tabs.
`business-rules` `apply-static-filter` restricts which records a *lookup* offers, which is a
different feature.

**Attribution: preconfigured-filter work, tracked separately. Not an ENG-88474 concern, and not a
CAADT elicitation gap either** — this is a missing capability, not a missing question.

---

## Step 2c — failure attribution

| Failure | Belongs to |
|---|---|
| **F1** move recipe omits the "bind the target workplace first" prerequisite; binding step fails | **ENG-88474 workplaces guidance** — blocker |
| **F2** binding-name suffix examples wrong for `SysWorkplace` / `SysAdminUnitInWorkplace` | **ENG-88474 workplaces guidance** |
| **F3** client-type warning not reproducible for web | **ENG-88474 workplaces guidance** |
| **F4** `My applications` presented as pre-existing; its admin-only audience undocumented | **ENG-88474 workplaces guidance** |
| **F5** delete recipe cannot reach step 3 without a binding-name read | **ENG-88474 workplaces guidance** |
| **F6** `SysWorkplace.Position` hedge can become a fact | **ENG-88474 workplaces guidance** — cosmetic |
| **F7** "unset on `My applications`" always a no-op | **ENG-88474 workplaces guidance** — cosmetic |
| **F8** `home-page` 6b omits `binding-name`, contradicting `workplaces` | **ENG-88474 scope** (`home-page` is one of the cross-linking guides) |
| `home-page` elicitation gate sits at step 5, after `create-page` at step 2 | **ENG-88474 scope** — ordering |
| `app-modeling` cross-link is a pointer, not a gate | **ENG-88474 scope** — by design; worth revisiting |
| No workplace question asked unprompted | **CAADT elicitation gate** — confirmed gap, 0 grep hits for "workplace" |
| Duplicated state (`UsrCompleted` **and** `UsrStatus`); metrics on the boolean | **CAADT elicitation gate** — no rule for a self-contradictory request |
| `Assign user` → `Contact` chosen silently over `SysAdminUnit` | **CAADT elicitation gate** — unraised ambiguity |
| **F14** `All / Active / Completed` list filters | **Preconfigured filters** — missing capability, tracked separately |
| **F9** `without-fill` vs `full-fill` for home pages | **Component catalog / CDN docs drift** — not ENG-88474 |
| **F10** no layout rule for a 2-metric band | **`dashboard-and-home-page-layout`** — not ENG-88474 |
| **F11** `seed-rows` array vs `rows` string | **clio MCP tool ergonomics** |
| **F12** `sync-pages verify` writes into the repo root | **clio MCP tool ergonomics** |
| **F13** one empty OData response | **transient** — not reproducible, no action |
| Refresh-vs-re-login not observed | **Verification-environment limitation** — needs a human with a logged-in browser |
| All UI outcomes API-verified only; `environment-superset` component catalog | **Verification-environment limitation** — see the caveat above |

---

# Addendum — second pass (Step 3 completion, official AC coverage, package inspection)

Run after the ticket's official acceptance criteria, test case and RFMT TODO were supplied, plus a
GitHub Copilot CLI session log on a different stand.

## Refresh vs re-login — RESOLVED: re-login required

A clean probe was set up: workplace `TodoRefreshTest` (Web client type, `All employees` grant,
`Contact` section) was created **after** the user's browser session was already open. User reported:

> Появилось только после ре логина

**`F5` was not sufficient; logout→login was required.** The `workplaces` guide's claim is
CONFIRMED on this stand, and RFMT TODO 4 ("make workplace available without relogin") is therefore
real engineering work, not a wording change. Probe artifacts were deleted afterwards.

## Copilot CLI evidence — discoverability, not just wording

A separate Copilot CLI session was asked (in Ukrainian) to *"remove the Contacts section from the
My applications workplace"* — a request that matches the `routing` map row **verbatim**
(`add/remove/move sections`). It failed for 5 minutes and the user cancelled. It **never called
`get-guidance` and never called a single clio MCP tool.** Sequence:

| Attempt | Outcome |
|---|---|
| `clio environments list` | `Verb 'environments' is not recognized` |
| `clio execute-sql-script` with a `SysModuleInWorkplace` join | `Usage of CustomQuery.ExecuteReader is denied by application security settings` |
| Hand-rolled DataService `SelectQuery` over HTTP, 4 variants (session, BPMCSRF, explicit Cookie header, CRT_CSRF) | `ArgumentNullOrEmptyException: Value for argument "userConnection" must be specified` every time |
| `GET /odata/SysWorkplace` | **404** — the correct .NET Framework path is `/0/odata/`, which `ServiceUrlBuilder` supplies automatically |
| `EntityDataService.svc` with Basic auth | HTML login page |
| `clio help \| grep -i "work\|module\|section\|nav"` | **no workplace verb exists** — only `workspace` commands |
| `clio delete-app-section --help` | reads *"deletes SysModuleInWorkplace records"* — a **name trap**: it deletes the section entirely, not its workplace placement |

Its log also shows `Failed to connect to MCP server 'clio'` at startup; after
`MCP Servers reloaded: 1 server connected` it still never entered the MCP.

**Conclusion that changes the ticket's shape:** the capability is unreachable to an agent that does
not already ask `get-guidance`. There is no CLI verb, `clio help` search finds nothing, and the
closest-named command does something different and destructive. Guidance text alone does not satisfy
"code agents can manage Workplaces".

## Official AC coverage — completed in this pass

| AC | Verdict | Evidence |
|---|---|---|
| create / update / delete Workplace | **VERIFIED** | Create `Todo` + `TodoScratch` + `TodoRefreshTest`; update (`HomePageUId`, `SysWorkplaceId`); delete `TodoScratch` + `TodoRefreshTest`. |
| grant / **change** / **remove** role visibility | **VERIFIED** | Granted `System administrators` (`35d56360…`) on `Todo`, bound as `SysAdminUnitInWorkplace_TodoAdmins`, then `odata-delete` + `remove-data-binding-row-db`. Read back: `Todo` retains only `All employees` (`c750491a…`), and no `_TodoAdmins` binding remains. |
| **add** / **remove** sections (explicit, not cascade) | **VERIFIED** | Added `Contact` (`1d9de5bf…`) to `Todo`, bound as `SysModuleInWorkplace_Contact`, then `odata-delete` + `remove-data-binding-row-db`. `Todo` back to exactly 1 section; `SysPackageSchemaData` back to the intended 11 rows, no orphans. |
| create / update data bindings | **VERIFIED (with F1 blocker)** | 11 bindings; every `Data/<Schema>_<Suffix>/` folder in the exported package matches a binding name 1:1. |
| move section between workplaces | **BLOCKER F1** | unchanged. |
| all changes update databindings | **BLOCKER F1 + F2 + F17** | see F17 below — new and more severe. |
| bindings work after moving to another env | **STILL NOT VERIFIED** | Package exported and inspected; the install on a second environment needs a target-env decision. |

Ordering note worth adding to the guide: for a **single** junction row, `odata-delete` →
`remove-data-binding-row-db` works fine (you already hold the `Id`). The children-first rule is
specific to the **cascade** case, where deleting the parent makes the child `Id`s unreadable.

## Package export — what actually ships

`clio download-application Todo -e DevEnv` → `Todo.zip` → `UsrTodo.gz` → extracted.
`Data/` folders match the 11 bindings exactly. Schemas shipped: `UsrTodo.Entity`,
`UsrTodoStatus.Entity`, `UsrTodo_FormPage`, `UsrTodo_ListPage`, `UsrTodo_Detail`,
`UsrTodoHomePage` (ClientUnit), `UsrTodoRelatedPage.Addon`.

`HomePageUId` **does** ship — `SysWorkplace_Todo/data.json` carries
`d3a42fb5-39c6-09fc-54d1-5071e61f145e` = `c7fce4b7-6436-471f-aa7a-429b173e04aa`. The `home-page`
guide's instruction to include `HomePageUId` in the binding row is correct and works.

### F17 — HIGH (ENG-88474). The binding projection silently drops the client type

Side-by-side of the two `SysWorkplace` bindings in the same package:

| Column (UId) | `SysWorkplace_MyApps` (by `create-app`) | `SysWorkplace_Todo` (by the guide's recipe) |
|---|---|---|
| `Id` `ae0e45ca…` | ✔ | ✔ |
| `Position` `bd12ea8e…` | ✔ `5` | ✔ `23` |
| `Name` `215e82ee…` (localizable) | ✔ (en-US, ru-RU) | ✔ (en-US = "Todo") |
| `HomePageUId` `d3a42fb5…` | — | ✔ |
| **`SysApplicationClientType` `445c4583…`** | ✔ `Web` | **ABSENT** |
| **`LoaderId` `8fe2b261…`** | ✔ `3707a058-e6a8-4d2c-99cc-d01d9e6b70c6` | **ABSENT** |
| **`Type` `dae36990…`** | ✔ `General` | **ABSENT** |

Cause is documented tool behaviour — `create-data-binding-db`: *"SaveSchema metadata is projected
from the primary key plus columns referenced by currently bound or requested rows"*. I passed
`Id`/`Name`/`Position`, then `Id`/`HomePageUId`. `SysApplicationClientType` was never in a payload,
so it is not in the projection.

**Why this matters more than F3.** The `workplaces` guide enforces the client type on the LIVE row:

> Resolve the intended client type explicitly before `odata-create`

…and says nothing about the **binding row**. So an agent that follows the guide exactly gets a
correct live workplace and a package whose workplace has **no client type, no loader, no type**.
The live row is fine only because the DB defaults are right — verified: `Todo`, `TodoRefreshTest`,
`My applications` and `Sales` all read back `LoaderId 3707a058…` / `Type General` /
`ClientType Web`, identical. On a package install into another environment, whether those defaults
are re-applied or the columns land NULL/zero is **exactly the untested question**, and a zero
`SysApplicationClientType` is the "inserts and reads back cleanly while never appearing where the
user expected" failure the client-type rule exists to prevent.

**Fix:** the guide must tell the agent to include `SysApplicationClientType` (and consider
`LoaderId`, `Type`) in the binding row, not just in `odata-create`. Recommended recipe change:
`create-data-binding-db` rows should carry `Id`, `Name`, `Position`, `SysApplicationClientType`, and
`HomePageUId` when set.

### F16 — MEDIUM (ENG-88474). `My applications` bindings are never cleaned up

The package still ships `SysWorkplace_MyApps` **and** `SysAdminUnitInWorkplace_MyApps` after the
section was moved out. So installing it on a target environment creates an **empty
`My applications` workplace** plus its `System administrators` grant — pure clutter that the user
never asked for. The `workplaces` guide's "New apps start in a default workplace" section tells you
to move the section and update the home-page bindings, but never says to **remove** the now-pointless
`My applications` bindings.

RFMT calls for exactly this:

> New application is always generated with My applications workplace. We should remove its bindings and create new.

That instruction is currently **not** in the guide. It should be a numbered step in that section.

### F18 — LOW (tooling). `create-data-binding-db` emits 28 empty-Name localization rows

`SysWorkplace_Todo/Localization/` contains **28** culture files; only `data.en-US.json` has
`"Value": "Todo"` — the other 27 (`ar-SA`, `bg-BG`, … `zh-TW`) carry `"Value": ""`. By contrast
`create-app`'s own `SysWorkplace_MyApps` ships only the 2 cultures it actually has text for
(`en-US`, `ru-RU`). Consequence: a user on a `ru-RU` profile on the target environment would get an
**empty workplace name**. Compare with `create-app`, which leaves other cultures unbound so the
platform falls back.

### F15 — MEDIUM (ENG-88474). `SysModule.Code` is not unique

`workplaces` → Operations:

> - Add / remove a section: `odata-create` / `odata-delete` a `SysModuleInWorkplace` row. Resolve the section `Id` from `SysModule` by code first.

`SysModule` filtered `Code eq 'Contact'` returns **two** rows:
`065063c9-8180-e011-afbc-00155d04320c` and `24e07446-3a40-49f5-81d1-c20cb4c0cdc4`. All **8** real
placements across `Marketing`, `CRM`, `Customer 360` ×2, `Contact center`, `Service`, `Sales`,
`Lead generation` use `24e07446…`. Picking the other one yields a junction row that inserts and
reads back with a non-zero `SysModuleId` — passing the guide's own zero-GUID assertion — but
pointing at a module that is not the live section.

The guide already warns that role **names** are not unique (*"Resolve the role `Id` from
`SysAdminUnit` by name first (names are not unique)"*). The same caveat is missing for
`SysModule.Code`, and the zero-GUID check does not catch it. Suggested fix: say codes are not
unique either, and disambiguate by checking which `SysModuleId` existing `SysModuleInWorkplace` rows
already use.

---

# Addendum 2 — cross-environment transfer executed. F17 CONFIRMED as a blocker

Target chosen by the user: `<target-env-uri>` (an internal disposable stand), registered as
`XferTarget`. `coreVersion 10.1.428.0`, `maxEntitySchemaNameLength 128` — a materially newer
platform than `DevEnv`, which makes the transfer realistic.

**Target baseline (before install):** 22 `SysWorkplace` rows — the same product set as `DevEnv`,
with **no `Todo` and no `My applications`**. Every baseline row had
`LoaderId 3707a058…`, and `Type` was `General` except `Portal` / `Self-service` = `Portal`
(so `Type` is not a constant, which matters for F17).

## Transfer result

`clio download-application Todo -e DevEnv` → `clio install-application Todo.zip -e XferTarget`
→ `Application installed successfully`. Then read back with `clio ds -t select`:

| | `Todo` (guide's binding) | `My applications` (`create-app`'s binding) |
|---|---|---|
| Name | ✔ `Todo` | ✔ |
| Position | ✔ `23` | ✔ |
| `HomePageUId` | ✔ `c7fce4b7…` | `''` (never set — F7) |
| **`SysApplicationClientType`** | **`''` EMPTY** | ✔ `Web` |
| **`LoaderId`** | **`''` EMPTY** | ✔ `3707a058…` |
| **`Type`** | **`''` EMPTY** | ✔ `General` |
| section | ✔ `UsrTodo` in `Todo` | 0 sections |
| role | ✔ `All employees` | `System administrators` |

**F17 is CONFIRMED, not hypothetical.** Package install does **not** apply DB defaults for columns
absent from the binding. The workplace arrives with no client type, so it cannot appear in the web
navigation — exactly the outcome the guide's client-type rule exists to prevent, produced by
following the guide exactly. `SysWorkplace` went 22 → **24**: `Todo` (broken) + `My applications`
(empty), so **F16 is also CONFIRMED** — the transfer ships a useless empty workplace.

Everything else transferred correctly: `HomePageUId`, the section placement, and the role grant.
So the binding model is right; the column projection is the defect.

## The fix works at package level — but only on a FIRST install

Adding the missing columns to the binding row on `DevEnv`:

```
upsert-data-binding-row-db binding-name=SysWorkplace_Todo values={
  "Id":"9768e46e-…","Name":"Todo","Position":23,
  "HomePageUId":"c7fce4b7-…",
  "SysApplicationClientType":"195785b4-…",   // Web
  "Type":"000a9225-…",                        // General
  "LoaderId":"3707a058-…"
}
```

Re-exported `SysWorkplace_Todo/data.json` now carries **all 6** columns (`Id`,
`SysApplicationClientType` "Web", `LoaderId`, `Position`, `HomePageUId`, `Type` "General") — i.e.
`create-app`'s full set plus `HomePageUId`. **This is the recipe the guide should teach.**

**But re-installing the corrected package did NOT repair the already-transferred row.** Two
independent platform mechanisms block it, and both were verified:

1. **`IsForceUpdate: false` on every non-key column.** Read from
   `Data/SysWorkplace_Todo/descriptor.json` — and identically from `SysWorkplace_MyApps`, so this is
   Creatio's default for package data, not something clio chose. Package data install inserts
   absent rows and never overwrites existing ones.
2. **A repeat install of an unchanged package applies no data at all.** Third install log is 21
   lines: `Loading packages from archive` → `Composing packages` → `Application installed
   successfully` in ~9 s, with **zero** `Lookup values … installed` lines (the first install had one
   per binding).

Deleting the broken row on the target and re-installing did **not** restore it either, for reason 2.

**Consequence for the guide:** getting the binding right on the FIRST install is the only chance.
Remediation on an already-transferred environment is a manual row fix, not a re-install. That
deserves an explicit note next to the client-type rule.

## Additional findings from this pass

### F19 — MEDIUM (clio MCP). The environment catalog is cached at server startup

`clio reg-web-app XferTarget …` succeeded and the CLI used it immediately (`get-info` returned
`coreVersion 10.1.428.0`). Every MCP call kept failing with:

> `Environment with key 'XferTarget' not found. Available environments: <the registered catalog as it was at server start> …`

for the rest of the session. A newly registered environment is invisible to the running MCP server
until it is restarted, so an agent that registers an environment cannot then use it — it must tell
the user to restart. This is also directly relevant to the ClioRing policy line *"Keep clio settings
as the only environment catalog. Refresh caches"*.

### F20 — MEDIUM (clio CLI). `clio dataservice` insert/update fail

With `-e XferTarget` and well-formed bodies:

| Operation | Result |
|---|---|
| `-t select` | works (used for all verification above) |
| `-t delete` | works (`rowsAffected: 1`) |
| `-t update` | `NullReferenceException: Object reference not set to an instance of an object.` — tried both `SysApplicationClientType` and `SysApplicationClientTypeId` column-key forms |
| `-t insert` | same `NullReferenceException` |

This mattered here because F19 had already cut off the MCP `odata-*` path, leaving no working write
route to the target environment from either surface.

### F21 — LOW (clio CLI). `install-application` takes the application name from the ZIP FILENAME

Installing the same package from `Todo2.zip` logged:

> `Application install from file started. Application name: Todo2. Code: Todo2.`

So renaming the archive created a **second application record** (`Todo2`) rather than upgrading
`Todo`. Non-obvious and easy to do accidentally when iterating on an export.

### F6 reinforced — `SysWorkplace.Position` is definitively server-managed

On the target after installs, **every** workplace was renumbered relative to its own baseline:
`AI command center` 17→21, `Case management` 9→18, `Contact Center Agent` 8→15, `Marketing` 4→5,
`Lead generation` 5→8, `Studio` 6→10. No write of mine touched those rows. The guide's hedge
(*"`SysWorkplace.Position` is not known to behave this way"*) can be replaced with the fact.

## State left on the target environment

Honest accounting, since I mutated a stand:

- `UsrTodo` package + its schemas and pages: **installed**.
- `Todo` workplace: **absent** — I deleted the broken row to test whether a re-install would
  restore it (it did not, per reason 2 above). Its section and role rows cascade-deleted with it.
- `My applications` workplace: **present, empty**, with its `System administrators` grant (F16).
- A stray `Todo2` application record from the renamed archive (F21).

To restore `Todo` on the target I need three `odata-create` calls, which are blocked by F19 —
**restarting the clio MCP server unblocks it.** `clio ds -t insert` is not a fallback (F20).

---

# Addendum 3 — the F17 fix VERIFIED on a clean first install

The first transfer target was torn down before the corrected binding could be tested there (its site
began returning HTTP 404 while the IIS host still answered 403 on the root, so the stand itself was
gone, not the tooling). A second, freshly provisioned stand was supplied — `coreVersion 10.1.438.0`,
`maxEntitySchemaNameLength 128`, registered as `XferFresh`. This is exactly what the F17 fix needed:
because every non-key binding column carries `IsForceUpdate: false`, the corrected row can only be
proven on an environment where the workplace does NOT yet exist.

**Baseline:** 22 `SysWorkplace` rows, **no `Todo`, no `My applications`** — clean.

**Package:** re-downloaded from `DevEnv` (which still carries the corrected binding).
`Data/SysWorkplace_Todo/data.json` ships **6 columns**, i.e. the full set the guide now prescribes:

| Column | Value |
|---|---|
| `Id` | `9768e46e-72ad-4d57-a100-f66e39d23a3a` |
| `SysApplicationClientType` | `195785b4-…` (Web) |
| `LoaderId` | `3707a058-…` |
| `Position` | `23` |
| `HomePageUId` | `c7fce4b7-…` |
| `Type` | `000a9225-…` (General) |

**Result after `install-application` — the decisive comparison:**

| Column on the transferred `Todo` | Old binding (3 columns) | Corrected binding (6 columns) |
|---|---|---|
| `Position` | `23` | `23` |
| `HomePageUId` | `c7fce4b7-…` | `c7fce4b7-…` |
| **`SysApplicationClientType`** | **EMPTY** | **`Web`** |
| **`LoaderId`** | **EMPTY** | **`3707a058-…`** |
| **`Type`** | **EMPTY** | **`General`** |

**F17's fix is confirmed, not merely plausible.** The recipe the guide now teaches — read the three
platform-set columns back after `odata-create` and include them in the binding row — produces a
workplace that arrives complete on another environment.

Everything else transferred correctly on the same install:

- section `UsrTodo` placed in **`Todo`** and in no other workplace (exactly one `SysModuleInWorkplace` row);
- `My applications` present with **zero sections** — the documented F16 side effect the guide now tells
  the agent to leave alone rather than "clean up";
- role grants: `Todo` → `All employees`, `My applications` → `System administrators`;
- `UsrTodoStatus` lookup seeded as package data (`New`, `In progress`, `Completed`);
- all 7 schemas: `UsrTodo` + `UsrTodoStatus` (entities), `UsrTodo_FormPage`, `UsrTodo_ListPage`,
  `UsrTodo_Detail`, `UsrTodoHomePage` (client units), `UsrTodoRelatedPage` (addon).

**Acceptance criterion "bindings are created so workplace work after moving to another env" —
VERIFIED**, replacing the earlier FAILED verdict, on the corrected binding only.

Residual limit unchanged: rendering in the navigation is still API-verified only, since no
authenticated browser session was available on either target.

## Environment note — Bash sandbox cannot reach the internal stands

`ts1-infr-web01` resolves to `10.48.12.158` from PowerShell, but clio invoked through the Bash tool
failed with `No such host is known` for every probe path. The identical command through PowerShell
succeeded immediately. All stand operations in this addendum therefore ran via PowerShell. Worth
knowing before concluding "the stand is down" from a Bash-side failure.

---

# Addendum 4 — independent reproduction by an uninvolved agent

The strongest evidence in this report was not produced by a targeted test. A separate Claude Code
session, in a different working directory, was given only the plain first-block request ("Create
Verrify1 app…") and ran it to completion against `DevEnv`. It had no knowledge of ENG-88474.

**What it was actually running** — established from its transcript, not from its summary:

- **The toolkit was never loaded.** `Skill` invocations: NONE. The only files it read were
  `memory/MEMORY.md` and two `.clio-pages/*/body.js`. No `AGENTS.md`, no runbook, no checklist.
- **clio was the RELEASED build, not this branch.** The toolkit's `.mcp.json` declares
  `"command": "clio"`, i.e. the global tool (`8.1.0.95`). Probed directly: `get-guidance
  name=workplaces` → *"Unknown guidance 'workplaces'"*. Its `routing` map has the new `branding` row
  but no workplaces row, and its `app-modeling` has no placement GATE.
- It requested 13 guides: `core-rules`, `routing`, `app-modeling`, `home-page`, the page-* set, the
  widget set, `data-bindings`. Never `workplaces` — the released `home-page` does not reference it.

So the run measured **released clio with no toolkit**. That makes it a clean baseline, and it
reproduced four findings from this report without being aimed at any of them.

## F17 reproduced — and worse than the original case

The agent bound the `Todo` workplace (`9768e46e-…`) into the `UsrVerrify1` package. Exported and
inspected:

| Binding | Columns | Contents |
|---|---|---|
| `SysWorkplace` (agent-created) | **2** | `Id`, `HomePageUId` |
| `SysWorkplace_MyApps` (`create-app`-created) | 5 | `Id`, `Position`, `LoaderId`, `SysApplicationClientType`, `Type` |

The agent's binding folder has **no `Localization` subdirectory at all**, so `Name` does not ship
either. Installing that package elsewhere would insert a workplace with no client type, no type, no
loader — and no name. The original F17 case shipped 3 columns plus `Name` via localization; this is
strictly worse.

## F2 reproduced, plus a two-package conflict

The binding landed under the **bare** schema name `SysWorkplace`, not a suffixed one — exactly the
parallel-binding pitfall. And because `UsrTodo` already shipped `SysWorkplace_Todo` for the SAME row,
one workplace row ended up bound in **two packages under two different names**. That is the
"adopting a workplace another package owns" hazard, observed rather than predicted.

## Split placement reproduced — and the guide caused it

Final live state:

| Workplace | Contents |
|---|---|
| `Todo` | section `UsrTodo` + home page **`UsrVerrify1HomePage`** |
| `My applications` | section `UsrVerrify1` |

Verrify1's page and its section were in different workplaces, so no single workplace showed a working
app. The agent was not careless — it did exactly what it was told: the guide asked only about the home
page. Worse, `home-page` step 5 in THIS branch still contained

> Choosing the workplace is a separate decision from where the sections live.

which actively instructs the split. That sentence is removed and replaced by a single combined
question (see Addendum 5). This is the one defect in this area that the branch did **not** previously
fix, and it was found only because the run was allowed to finish.

`SysWorkplace_MyApps` also appeared in both `UsrTodo` and `UsrVerrify1` — F16, twice over.

## Why the toolkit changes did not participate

The orchestrator skill's trigger is declared as:

> `description: Use when creating Creatio app Business Plans, technical implementation handoffs, or applying the approved plan through clio MCP.`

It is phrased through the toolkit's own artifacts. A request like "Create Verrify1 app. It should
have…" matches none of those words, so the skill is never selected, and clio's always-on MCP
instructions ("ALWAYS read core-rules + routing FIRST") take over. Every sibling skill in the same
toolkit is phrased through user intent instead — `branding-orchestrator` ("Use when **the user wants
to**…"), `ui-guidelines` and `mobile-page-conversion` (both "**Apply proactively**…"). The one skill
that must fire first is the only one that cannot match a natural request. Tracked as a toolkit
change, not a clio one.

## Environment state after the run

The run overwrote `Todo`'s `HomePageUId` (this report's `UsrTodoHomePage`, `c7fce4b7-…`) with
`UsrVerrify1HomePage`. DevEnv was subsequently cleaned: `UsrVerrify1` deleted, the workplace renamed
to `Ensure` with `HomePageUId` restored and the full 7-column binding re-applied, `UsrTodo` back to
its 11 bindings, and the section and role grant verified intact.

Useful side observation from that cleanup: **`delete-app` does NOT cascade to a bound row shared with
another package.** The workplace bound inside the deleted package survived with every column intact.
One observation, not a contract — but relevant to any delete recipe.

---

# Addendum 5 — the elicitation is now one question

Two changes follow directly from Addendum 4, both in the guides the released build actually reads:

**`home-page` step 5 — the option set is now prescribed, in order.** Previously it said "present the
`Name` list … and ask" plus a prose aside about offering a new workplace. The observed run listed only
existing workplaces and recommended `My applications`. Now the guide fixes the order as the
recommendation: (1) a NEW workplace named for the app, marked RECOMMENDED when scaffolding, with
"Never omit this option"; (2) the workplace(s) where the app registers its sections; (3) other
existing workplaces, each marked for home-page replacement and for foreign-package ownership. And
`My applications` must be marked administrators-only so it is not offered as a neutral default.

**Section and home page are ONE question.** The sentence that caused the split —

> Choosing the workplace is a separate decision from where the sections live.

— is gone. Both `home-page` and `workplaces` now state that for a scaffolded app the section and the
home page go in the same workplace, asked once and applied together, with `home-page` owning the page
half and `workplaces` the section half: "neither is finished alone." Separating them is allowed only
when the user explicitly asks for it.

**Foreign-package check added** to `home-page` step 5: read `SysPackageSchemaData` filtered by
`SysSchema.Name = 'SysWorkplace'` before binding a workplace your package does not own, and surface a
two-package conflict rather than creating one silently.

Test coverage added for all of it, including a `NotContain` guard on the removed "separate decision"
sentence so it cannot come back.

## Addendum 6 — moving the ask to discovery time, and prescribing the audience

Two gaps survived the TodoApp8 run. Both are about WHEN and HOW the question is asked, not whether
the guide knows the answer.

**The ask happened after the build, not at discovery.** TodoApp8 was scaffolded, its pages authored,
and only then did the agent ask where the app belonged. The requirement existed at the time — as a
GATE bullet in the `app-modeling` guide, verified present in the run's context — and was ignored.
That is the third confirmation of the same pattern in this ticket: **a prescribed, ordered option set
executes; a reference bullet does not.** The fix therefore does not reword the bullet. It moves the
requirement onto the two surfaces that are structurally earlier than any guide the agent chooses to
read:

- `core-rules` — the always-on instructions mandate reading it FIRST on every operation, so the rule
  now sits among the invariants rather than inside a domain guide the agent may reach late. It names
  the turn ("in the SAME turn you ask about the environment"), states the consequence
  (`My applications` is administrators-only), and routes to `workplaces` for the option set. It
  deliberately carries no write recipe — asserted by a `NotContain` on `SysModuleInWorkplace` so the
  two cannot drift.
- the `create-app` tool contract `preconditions` + a new anti-pattern, and the `create-app`
  `[Description]` itself. The contract is what an agent reads before its first call to a tool, and
  the description is the one surface always in context. The anti-pattern names the observed order
  verbatim: "create-app, build the pages, THEN ask which workplace the app belongs to."

`workplaces` also now states the ordering as due BEFORE `create-app` (previously "immediately
after"), and says a late ask is a defect rather than a late-but-equivalent order — the cost is that
the user re-decides work already finished.

**The audience question collapsed to two extremes.** It was prose, so the run offered
`System administrators only` versus everyone. That framing makes a blanket grant look like the only
way to let a non-administrator in — the opposite of the least-privilege rule the same guide states.
The audience question is now a prescribed option set built from the environment: read the roles off
`SysAdminUnit`, offer the role the request implies first (recommended), then the other concrete
roles, then `All employees` labelled as every user and explicitly never pre-marked as the
recommendation, with `System administrators` offered as the deliberate admin-tool answer and the one
option needing no write. `home-page` cross-links to it instead of restating it.

Coverage: unit assertions on all three surfaces (guide text, contract, tool description) plus E2E
assertions over the real stdio transport for `core-rules`, the `workplaces` article, and the
`create-app` contract — the guidance layer is only real if it survives the wire.

## Bottom line

**Phase 1 is directionally right and the guide's core model is sound.** The three-table model,
the confirmation-gate table, the "verify BOTH sides after a move" rule, the children-first delete
ordering, the `Position`-is-server-assigned warning, the `odata-read`-filters-by-navigation-path
detail and the `SysWorkplace` ≠ `SysWorkspace` ≠ `create-workspace` disambiguation all held up
against a live stand. The elicitation gate text is good. The `home-page` gate is good.

**One blocker (F1) and one high (F2) would each break an agent following the guide literally**, and
both are in the same area: shipping a move as a data binding. Fixing those two, plus the F5 missing
step, would make every recipe in the guide executable end-to-end as written.

**The `app-modeling` cross-link works as routing but not as a gate.** It got an agent to
`workplaces`, where the real gate lives. If the intent is that placement is never silently chosen,
the informational pointer is the weak link — CAADT has no workplace concept at all, so clio's
guidance is the only thing that can stop the write.

**Unfinished:** the refresh-vs-re-login observation, and UI confirmation of everything else. Both
need a logged-in browser on `<dev-env-uri>`.
