# Read Column Default Values — OData `$metadata` Probe (FR-02 evidence)

**Feature**: read-column-default-values
**Story**: [story-read-column-default-values-2.md](../stories/story-read-column-default-values-2.md)
**FR**: FR-02 · **PRD AC**: AC-02
**Jira**: [ENG-91318](https://creatio.atlassian.net/browse/ENG-91318) (epic ENG-85256)
**Phase**: A — investigation (documents only; SM-01 = empty code diff)
**Probed**: 2026-06-13 · `studio0614` → `http://ts1-core-dev04:88/studioenu_15570322_0614` · **.NET Framework** · OData v4 (`Edmx Version="4.0"`)

> Captured CSDL fragments from a real Creatio instance answering: does OData v4 `$metadata` expose column default values? Probed against the **same** instance and the **same** lookup-`Const` column used in the [E2E scenario](read-column-default-values-e2e-scenario.md).

---

## 0. Finding (TL;DR)

**Creatio OData v4 `$metadata` does NOT expose column default values — for any column type.**
The entire 1.43 MB CSDL document contains **zero** real CSDL `DefaultValue` facets. This is stronger than the ADR's "primitive-only" prediction: it is **none at all** on this platform's OData v4 metadata emission.

→ **OData `$metadata` is unusable as a default-value read source.** The schema-designer service (`GetSchemaDesignItem`, Story 1) remains the only path that carries the default. For the *display value* of a lookup default, only an OData **data** read with `$expand` helps (E2E doc §2.1), and that requires resolving a stored FK on an actual record — not the column definition.

---

## 1. Captured CSDL fragments

### 1.1 Lookup-`Const` column (`UsrEng91318Order.UsrColor`, default = `d1a6ea58-…`)

```xml
<EntityType Name="UsrEng91318Order">
  <Key><PropertyRef Name="Id" /></Key>
  <Property Name="Id" Type="Edm.Guid" />
  <Property Name="CreatedOn" Type="Edm.DateTimeOffset" />
  <Property Name="CreatedById" Type="Edm.Guid" />
  <Property Name="ModifiedOn" Type="Edm.DateTimeOffset" />
  <Property Name="ModifiedById" Type="Edm.Guid" />
  <Property Name="ProcessListeners" Type="Edm.Int32" />
  <Property Name="UsrColorId" Type="Edm.Guid" />          <!-- NO DefaultValue, despite Const=d1a6ea58-… -->
  <NavigationProperty Name="CreatedBy" Type="…OData.Contact" />
  <NavigationProperty Name="ModifiedBy" Type="…OData.Contact" />
  <NavigationProperty Name="UsrColor" Type="…OData.UsrEng91318Color" Partner="UsrEng91318OrderCollectionByUsrColor" />
</EntityType>
```
The column with a configured `Const` default of `d1a6ea58-6a88-4cb7-bfea-7a41caa0ae50` (verified live in the E2E doc Step 5/6) appears in CSDL as a bare `<Property Name="UsrColorId" Type="Edm.Guid" />`. **The default is entirely absent.** The lookup relationship is represented only structurally (the `UsrColor` NavigationProperty); the *which record is the default* information is not in CSDL.

### 1.2 Primitive-default coverage — and why "0 DefaultValue" is the whole answer

A `grep` for the literal string `DefaultValue` across the full CSDL returns **2** hits — and **both are column NAMES, not CSDL facets**:
```xml
<Property Name="EntityDefaultValues" Type="Edm.String" Nullable="false" />   <!-- a column literally named so -->
<Property Name="ColumnDefaultValue"  Type="Edm.String" Nullable="false" />   <!-- idem -->
```
There is **not a single** `<Property … DefaultValue="…" />` facet in the document. Since Creatio emits no CSDL `DefaultValue` for *any* column, the "configured primitive-column default" sub-case (AC-01) collapses into the same conclusion as the lookup case: even a Boolean/Text column with a `Const` default would carry **no** `DefaultValue` in `$metadata`. The hundreds of platform columns that have backend defaults (e.g. `ProcessListeners`) likewise show none. No separate primitive-default schema needed to be built to establish this — the absence is document-wide and unambiguous.

### 1.3 Referenced lookup entity (`UsrEng91318Color`)

```xml
<EntityType Name="UsrEng91318Color">
  <Key><PropertyRef Name="Id" /></Key>
  <Property Name="Id" Type="Edm.Guid" />
  …
  <Property Name="Name" Type="Edm.String" Nullable="false" />
  <Property Name="Description" Type="Edm.String" Nullable="false" />
  …
</EntityType>
```
The `Name` (display) column exists as a normal property — reachable by a **data** `$expand` (E2E §2.1), but nothing ties it to "the default of `UsrColor`".

---

## 2. Probe vehicle & exact commands (transparency)

### 2.1 ADR-prescribed vehicle is BROKEN on this config — finding

The ADR chose alternative C: `clio call-service --service-path "odata/$metadata" -m GET -d <file> -e <env>` (zero code diff, routes through `IApplicationClient`). **It does not work in clio 8.1.0.58 here:**
```
$ clio call-service --service-path 'odata/$metadata' -m GET -d /tmp/metadata.xml -e studio0614
[ERR] - Sequence contains more than one matching element
```
The error reproduced for **every** service path, and even with explicit `-u/-l/-p` (bypassing registered-env selection). Root cause: a `.Single()` over environments fails because the shared `appsettings.json` has **multiple environment keys pointing at the same URI** (`dev04`/`dev04_seeenu`/`eng90403` share one URI; `eng91212`/`eng91274-e2e`/`findapp-0613` share another). This is a real clio robustness gap — **flagged for a separate ticket** — and means the FR-02 probe vehicle the ADR assumed is not currently usable on a typical multi-alias config.

### 2.2 Fallback used: authenticated `curl` (ad-hoc evidence probe, not production code)

Because `call-service` was unavailable, the CSDL was captured with a one-off authenticated `curl` against the live env (standard Creatio forms-auth):
```bash
# 1) Login (forms auth) → cookie jar
curl -s -c cookies.txt -H 'Content-Type: application/json' -H 'ForceUseSession: true' \
  -d '{"UserName":"Supervisor","UserPassword":"***"}' \
  'http://ts1-core-dev04:88/studioenu_15570322_0614/ServiceModel/AuthService.svc/Login'
# → {"Code":0,…"RedirectUrl":"/studioenu_15570322_0614/0/Shell"}  HTTP 200; sets .ASPXAUTH + BPMCSRF

# 2) GET CSDL ($metadata) — .NET Framework uses the 0/ prefix
curl -s -b cookies.txt 'http://ts1-core-dev04:88/studioenu_15570322_0614/0/odata/$metadata' -o metadata.xml
# → HTTP 200, 1428592 bytes, <edmx:Edmx Version="4.0">…
```
> **Hard-rule note (AC-03/AC-04):** `curl` here is an *investigation probe* run from a shell to gather evidence — it is **not** a clio code path. The production hard rule ("`IApplicationClient` only; do NOT extend `ODataReadTool` to fetch `$metadata`") is fully honored: **zero production code changed** (SM-01). The `0/` prefix used is exactly what `ServiceUrlBuilder.Build` would prepend for `.NET Framework`.

---

## 3. Environment-matrix coverage (AC-02 / AC-ERR)

| Row | Instance | Reachable | `$metadata` path | DefaultValue facets |
|-----|----------|-----------|------------------|---------------------|
| .NET **Framework** | `studio0614` (`studioenu_15570322_0614`) | ✅ | `0/odata/$metadata` | **0** |
| .NET **Core** | — none registered/reachable | ❌ | (`odata/$metadata`) | **not covered** |

**AC-02 partial (AC-ERR row):** only the `.NET Framework` row is covered — all 10 registered environments are `IsNetCore=false` on host `ts1-core-dev04`; no `.NET Core` 8.x instance is reachable from this workstation. Recorded as a version-coverage limitation rather than failing the investigation. Because CSDL emission is platform-code-level (not host-runtime-level), the "0 DefaultValue facets" result is expected to hold on `.NET Core` too, but is **not asserted as verified**.

---

## 4. OQ-01 — what do "other teams" read via OData? (status)

The ticket states other teams "use OData for this purpose." Given §0–§1, OData v4 `$metadata` **cannot** be the source of column *default values* on Creatio (the facet is never emitted). The plausible interpretations of "other teams use OData":
1. **Data-read + `$expand`** to resolve a lookup FK's display value (E2E §2.1) — about *reading record data*, not reading a column's default.
2. **Post-insert observation** — create a record and observe which values the platform auto-filled (the runtime default), as in E2E Step 6.
3. Reading **`SysSchema`/`SysEntitySchemaColumn` rows via OData** (the metadata tables) — would expose the raw `DefValue` blob, equivalent to what `GetSchemaDesignItem` already returns.

**OQ-01 outreach: still open** — no confirmation captured from another team as to which of (1)–(3) they mean. Its fallback (assume interpretation (1)/(2): OData adds *display-value resolution on data*, not default-value metadata) is applied in the [comparison doc](read-column-default-values-comparison.md), per the ADR (OQ-01 fallback resolves in story 4, not here).

---

## 5. AC traceability

| AC | Where |
|----|-------|
| AC-01 (CSDL for lookup-Const + primitive, explicit DefaultValue presence + version) | §1.1, §1.2, §0 |
| AC-02 (both matrix rows) | §3 (FW covered; Core = AC-ERR row) |
| AC-03 (exact command recorded; no raw HttpClient in *production*) | §2.1, §2.2 |
| AC-ERR (`$metadata` availability / version coverage as matrix row) | §3 |
| AC-04 (SM-01: ODataReadTool NOT extended; zero code diff) | §2.2 note |
