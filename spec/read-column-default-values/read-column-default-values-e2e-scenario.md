# Read Column Default Values — Ticket-Case E2E Scenario (FR-03 evidence)

**Feature**: read-column-default-values
**Story**: [story-read-column-default-values-3.md](../stories/story-read-column-default-values-3.md)
**FR**: FR-03 · **PRD AC**: AC-03
**Jira**: [ENG-91318](https://creatio.atlassian.net/browse/ENG-91318) (epic ENG-85256)
**Phase**: A — investigation (documents only; SM-01 = empty code diff)
**Environment**: `studio0614` → `http://ts1-core-dev04:88/studioenu_15570322_0614` · **.NET Framework** (`IsNetCore=false`) · OData v4 · prefix `Usr` · package `Custom`
**Executed**: 2026-06-13 (UTC ~20:30–20:44)

> Records the ticket's lookup-default scenario executed end to end on a real environment via clio/MCP only, with each call + response and a strict pass/fail verdict against the PRD machine-verifiable predicate.

---

## 0. Verdict (TL;DR)

| Dimension | Result |
|-----------|--------|
| **Readback predicate (PRD Definitions a–d)** | **FAIL** — component (d) missing |
| **Runtime application of the Const default** | **PASS** — confirmed on a real insert |
| **Net** | Platform stores & applies the lookup `Const` default correctly; the gap is purely **readback ergonomics** — `get-entity-schema-column-properties` returns the GUID with **no referenced-record display value** |

Predicate components for the readback of `UsrEng91318Order.UsrColor` (a lookup `Const` default):

| # | Component | Present? | Value |
|---|-----------|----------|-------|
| (a) | default value source | ✅ | `Const` |
| (b) | GUID | ✅ | `d1a6ea58-6a88-4cb7-bfea-7a41caa0ae50` |
| (c) | referenced schema name | ✅ | `UsrEng91318Color` (via `reference-schema-name`) |
| (d) | display value or marker | ❌ | **absent** — no `Green`, no resolution field |

All four required → **predicate FAILs** on (d). An agent reading the column cannot tell the GUID means *"Green"* without a separate query.

---

## 1. Six normative steps (clio/MCP only — SM-02 counter)

All six steps used clio MCP tools exclusively; no manual UI step.

### Step 1 — Create the lookup entity (empty table)

`create-lookup` → `UsrEng91318Color` (BaseLookup; provides `Name`/`Description`).

Result: **partial** — `exit-code 1`:
```
Schema 'UsrEng91318Color' was created and saved, but publishing the configuration failed:
Object reference not set to an instance of an object. Until the configuration is built
(for example via compile-creatio), the schema stays invisible…
```
The metadata schema was created+saved; the **server-side auto-publish threw a NullReferenceException** (systemic on these instances — see §3). Resolved by a subsequent `compile-creatio` (full configuration build, exit 0). After the build, `odata-read UsrEng91318Color` → `{success:true, count:0}` (runtime table materialized, empty).

### Step 2 — Insert a record into the lookup + capture GUID  *(the ticket's "where does the agent get the GUID")*

`odata-create` entity `UsrEng91318Color` data `{ "Name": "Green" }`:
```json
{ "success": true, "id": "d1a6ea58-6a88-4cb7-bfea-7a41caa0ae50",
  "record": { "Id": "d1a6ea58-6a88-4cb7-bfea-7a41caa0ae50", "Name": "Green", … } }
```
**Answer to the ticket question:** the agent obtains the GUID from the OData create response `id` field (or a follow-up `odata-read` of the lookup). `d1a6ea58-6a88-4cb7-bfea-7a41caa0ae50` is reused as the default below.

### Step 3 — Add the lookup column to the target Object

- `create-entity-schema` → `UsrEng91318Order` (BaseEntity). Same partial result as Step 1 (saved; auto-publish NRE).
- `modify-entity-schema-column` action `add` → `UsrColor`, type `Lookup`, `reference-schema-name=UsrEng91318Color`. **`exit-code 0`** — `Column 'UsrColor' action 'add' completed`.

### Step 4 — Set the lookup-record `Const` default using the GUID from Step 2

`modify-entity-schema-column` action `modify` → `UsrColor`,
`default-value-config = { "source": "Const", "value": "d1a6ea58-6a88-4cb7-bfea-7a41caa0ae50" }`.
**`exit-code 0`** — `Column 'UsrColor' action 'modify' completed`. (No existence validation of the GUID against the lookup table — consistent with Story 1 §4.2.)

### Step 5 — Read back via `get-entity-schema-column-properties` (persisted `defValue`)

```json
{
  "schema-name": "UsrEng91318Order", "column-name": "UsrColor", "type": "Lookup",
  "default-value-source": "Const",
  "default-value": "d1a6ea58-6a88-4cb7-bfea-7a41caa0ae50",
  "reference-schema-name": "UsrEng91318Color",
  "default-value-config": { "source": "Const", "value": "d1a6ea58-6a88-4cb7-bfea-7a41caa0ae50" }
}
```
**Persisted `defValue` shape (OQ-03):** the `Const` value is a **plain GUID string** — `default-value-config.value` is the bare GUID, **not** a structured object with display metadata. There is **no** `display-value`, no resolved record caption, anywhere in the readback. → predicate component (d) missing → **FAIL**.

### Step 6 — Runtime verification (default actually applied)

`odata-create` entity `UsrEng91318Order` data `{ "Id": "a1b2c3d4-0000-4000-8000-000000000091" }` — **`UsrColor` deliberately NOT supplied**:
```json
{ "success": true, "id": "a1b2c3d4-0000-4000-8000-000000000091",
  "record": { "Id": "a1b2c3d4-0000-4000-8000-000000000091",
              "UsrColorId": "d1a6ea58-6a88-4cb7-bfea-7a41caa0ae50", … } }
```
**PASS** — the runtime auto-applied the `Const` default: the new record's `UsrColorId` equals the default GUID even though the caller never set it. Metadata persistence (Step 5) is therefore **not** the only thing that works — the platform genuinely honors the schema-designer `Const` default at insert time (risk A-02 cleared).

---

## 2. Supplementary evidence (non-normative)

> These observations sit outside the six clio/MCP-only steps so the SM-02 counter stays honest.

### 2.1 OData data-read CAN resolve the display value (the enrichment path) — OQ-03 / N-01

`odata-read UsrEng91318Order` filter `Id eq a1b2c3d4-…091`, select `Id,UsrColorId`, **expand `UsrColor`**:
```json
{ "Id": "a1b2c3d4-…091", "UsrColorId": "d1a6ea58-…ae50",
  "UsrColor": { "Id": "d1a6ea58-…ae50", "Name": "Green", "Description": "" } }
```
So the *"Green"* display value **is** retrievable — but only by reading an **actual data record** and `$expand`-ing the navigation, i.e. resolving a stored FK. It does **not** come from reading the column *definition*. To enrich a *default* specifically, a consumer must (1) read the schema-designer default GUID, then (2) issue a second OData read of the lookup entity filtered by that GUID. This two-step "hybrid" is the natural shape of any FR-05 enrichment (see comparison doc).

### 2.2 `SystemValue` defaults on lookup columns — OQ-05

Not exercised on a lookup column in this run. The shipped contract maps `SystemValue` defaults to system-variable GUIDs (e.g. `CurrentUserContact` → `{4F367CA9-…}`); these are designed for FK columns like `Owner`/`Contact`. The `Resolve()` path (Story 1 §4.2) resolves `SystemValue`/`Settings` aliases to canonical GUIDs but, as with `Const`, the **readback** of a `SystemValue` default returns the GUID/alias in `value-source` with **no** resolved caption. OQ-05 verdict: **not separately reproduced**; recommend a follow-up only if Phase B targets `SystemValue` lookup defaults.

---

## 3. Environment notes (N-02 platform-dependency)

- **Coverage:** ONE environment-matrix row exercised — `studio0614`, **.NET Framework** (`IsNetCore=false`). No `.NET Core` instance is registered/reachable from this workstation (all 10 registered envs are `.NET Framework` on host `ts1-core-dev04`).
- **Platform dependency:** the persisted `defValue` shape (plain GUID) and the runtime application are driven by Creatio platform code (`EntitySchemaColumnDef` + `BaseEntity` insert pipeline), not host-runtime specifics, so they are expected to be identical on `.NET Core`. **Not asserted as verified** for `.NET Core` — flagged as a second-row execution trigger if a `.NET Core` 8.x instance becomes available.
- **Systemic publish quirk:** every schema-metadata write (`create-lookup`, `create-entity-schema`) reported `auto-publish failed: Object reference not set…` with a dataforge maintenance warning `baseUri Value cannot be null`. The save always succeeded; a full `compile-creatio` (10 min) was required to materialize each schema into runtime. This reproduced identically on two separate instances (`dev04`, `studio0614`) — it is environment build-pipeline misconfiguration, **not** a defect in the default-value contract under test.
- **Timing (OQ-04 input):** the Step-5 `get-entity-schema-column-properties` readback returned in roughly normal designer-service latency (sub-second to low seconds, comparable to other designer calls); the OData enrichment read (§2.1) is one additional round-trip. Story 4 uses this to weigh enrichment default-on vs opt-in (A-05).

---

## 4. AC traceability

| AC | Where |
|----|-------|
| AC-01 (calls+responses+persisted defValue+runtime value, strict predicate verdict) | §0, §1 Steps 1–6 |
| AC-02 (six steps, clio/MCP only) | §1 |
| AC-03 (OQ-03 persisted shape in supplementary section) | §1 Step 5 + §2.1 |
| AC-04 (OQ-05) | §2.2 |
| AC-05 (N-02 platform-dependency statement) | §3 |
| AC-ERR (empty code diff) | only `spec/**` changed |

## 5. Test artifacts to clean up

Created on `studio0614` (package `Custom`) — to be removed after Phase A docs land:
`UsrEng91318Color` (+ record `Green` `d1a6ea58-…`), `UsrEng91318Order` (+ record `a1b2c3d4-…091`).
Also an orphan **unpublished** `UsrEng91318Color` on `dev04` (auto-publish failed; never built).
