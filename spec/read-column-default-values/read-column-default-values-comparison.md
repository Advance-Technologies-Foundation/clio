# Read Column Default Values — Comparison Matrix & Decision (FR-04)

**Feature**: read-column-default-values
**Story**: [story-read-column-default-values-4.md](../stories/story-read-column-default-values-4.md)
**FR**: FR-04 · **PRD AC**: AC-04 · **ADR**: [adr-read-column-default-values.md](../adr/adr-read-column-default-values.md)
**Jira**: [ENG-91318](https://creatio.atlassian.net/browse/ENG-91318) (epic ENG-85256)
**Inputs**: [current-path](read-column-default-values-current-path.md) (FR-01) · [odata-probe](read-column-default-values-odata-probe.md) (FR-02) · [e2e-scenario](read-column-default-values-e2e-scenario.md) (FR-03)

---

## 0. Decision

**HYBRID (ADR Option B) — keep the designer-service read path as the readback backbone and enrich the display value inside clio.** OData `$metadata` (Option A) is **rejected on evidence**: it carries no column defaults at all. Phase B is **warranted** — the gap is confirmed.

- **D7 (enrichment transport): OData data endpoint** — `GET odata/{ReferenceSchema}({guid})?$select={displayColumn}` via `IApplicationClient`. Live-verified working (E2E §2.1). **DataService `SelectQuery` is the fallback** for OData-disabled environments.
- **OQ-04 (enrichment default-on vs opt-in): default-on with fail-soft.** Observed cost is one extra OData round-trip on top of a sub-second designer call — acceptable for an agent-facing single-call readback.
- **OQ-01 (other teams via OData): unanswered** — no team confirmation captured; PRD fallback applied (decide on empirical evidence + risk note, §5).

---

## 1. Scored matrix — read-path direction (D1–D6)

Legend: ✅ good · ⚠️ partial · ❌ fails.

| Criterion | A: Adopt OData `$metadata` | **B: Designer path + clio enrichment (CHOSEN)** | C: `SysSchema` rows via OData |
|-----------|----------------------------|------------------------------------------------|-------------------------------|
| **D1** Predicate completeness (a/b/c/d) | ❌ **0 of 4** — CSDL carries *no* defaults at all (probe §0–§1); not even (a)/(b) | ⚠️→✅ **3 of 4 today** (a,b,c via `default-value-config` + `reference-schema-name`), **4 of 4 after enrichment** (d added by resolver) | ⚠️ a/b/c recoverable from raw `DefValue` blob; d still needs a second query |
| **D2** Auth/permission | n/a (nothing to read) | ✅ designer call + OData read both run under clio's existing creds (both proven live); 403/empty distinguishable → honest markers | ⚠️ `SysSchema`/`SysEntitySchemaColumn` are NUI-restricted; ESQ often denied |
| **D3** Env coverage (FW `0/` + Core) | ❌ facet absent on FW (verified); Core unverified but same emission code | ✅ designer path already serves both rows in production; enrichment via OData (data) or `SelectQuery` fallback | ⚠️ depends on OData + restricted-table access |
| **D4** Version stability | ⚠️ CSDL is standards-based but the *content* (no defaults) is the blocker, not stability | ✅ same designer contract the product UI uses; released in 8.0.2.47 | ❌ reverse-engineers persisted schema internals; brittle across versions |
| **D5** Alignment w/ other teams (OQ-01) | ❓ assumed but **falsified for metadata** — `$metadata` can't be what they read for defaults | ✅ the *data-read + `$expand`* and *post-insert* patterns (the only OData angles that yield a display value) are exactly the enrichment this option performs | ❓ would match only if "other teams" read metadata tables — unconfirmed |
| **D6** Impl cost in clio | ❌ new CSDL/XML parsing surface for zero D1 gain | ✅ reuses released DTO mapping; additive nullable fields + one fail-soft resolver | ❌ highest — new restricted-table query layer + duplicate mapping |
| **Verdict** | **Reject** | **Choose** | **Reject** |

**Decision rule applied** (ADR §"Decision rule"): "keep the designer path unless OData `$metadata` strictly dominates D1 without regressing D2/D3/D4." OData `$metadata` scores **0/4 on D1** — the opposite of dominating. Therefore the designer path is kept; OData contributes only as the Phase B **enrichment data transport** (the "hybrid" definition), not as the readback source.

## 2. Enrichment transport (D7)

| Option | Evidence | Status |
|--------|----------|--------|
| A: OData data endpoint `GET odata/{Ref}({guid})?$select={display}` | Live-verified: `$expand` returned `UsrColor.Name="Green"` for the default GUID (E2E §2.1); `odata-read` runs under clio creds in production | **Chosen** |
| B: DataService `SelectQuery` POST | Always available (no OData dependency); maps security errors cleanly | **Fallback** for OData-disabled envs (AC-ERR rows) |
| C: Designer-service display endpoints | Enumerate per data-value-type, not record-by-GUID | Rejected (contract mismatch) — unchanged from ADR |

Display column = referenced schema's `primary-display-column-name` (already exposed by the designer read path; `Name` for our lookup). Resolve once per referenced schema per command (in-memory cache).

## 3. Confirmed gaps → DRAFT-AC mapping (N-03 / SM-03)

| Gap (evidence) | Confirmed? | DRAFT-AC |
|----------------|-----------|----------|
| **Readback returns GUID with no referenced-record display value** — predicate component (d) missing (E2E §0, Step 5: `default-value-config.value` = bare GUID, no `Green`) | ✅ **Yes** | **DRAFT-AC-05** → Phase B story 6 (readback enrichment: `display-value` + `record-resolution`) |
| **Write accepts a `Const` lookup GUID with no existence validation** — `Resolve()` passes `Const` through unchanged (current-path §4.2); modify succeeded with no table check (E2E Step 4) | ✅ **Yes** | **DRAFT-AC-06** → Phase B story 7 (write-side Const-GUID validation) |
| Persisted `defValue.Value` is a **plain GUID string**, not a structured object with display metadata (E2E Step 5; OQ-03) | ✅ Yes | Confirms **A-04 = false** → resolver IS required; story 6 does **not** shrink to a pure mapping change |
| Runtime correctly applies the `Const` default at insert (E2E Step 6) | ✅ Yes (works) | Clears risk **A-02** → no FR-06 escalation; story 7 stays scoped to validation only |

**Net: the gap is CONFIRMED. Phase B (stories 6, 7, 8) is in scope.** FR-05/FR-06/FR-07 do **not** close as "not needed".

### Side finding (not a feature gap — separate ticket)

The ADR-prescribed FR-02 probe vehicle `clio call-service` is **broken in clio 8.1.0.58** on any `appsettings.json` with duplicate environment URIs: every invocation fails with `Sequence contains more than one matching element` (a `.Single()` over environments), even with explicit `-u/-l/-p` (probe doc §2.1). This blocked the prescribed probe; an authenticated `curl` was used as a transparent fallback (zero production-code change). **Recommend a separate clio bug ticket** for `call-service` environment resolution. Not a `read-column-default-values` DRAFT-AC.

## 4. Honest predictions — validated vs overturned

| ADR prediction | Outcome |
|----------------|---------|
| "CSDL `DefaultValue` is primitive-only; `$metadata` can't carry a referenced record's display value" | **Overturned in the stronger direction**: CSDL carries **no** `DefaultValue` facet for *any* column on this platform (0 in 1.43 MB) — not even primitive. OData `$metadata` is wholly unusable for defaults. |
| "Component (d) always requires a data query someone must issue; the design question is whether clio issues it internally" | **Validated**: the display value is obtainable only via a data read + `$expand` of an actual record (E2E §2.1). Hybrid = clio issues it internally. |
| A-04 "designer DTO may already carry display metadata" | **False** — plain GUID only. Resolver required. |
| A-02 "SaveSchema may mangle plain-GUID `defValue.Value`" | **False** — stored and runtime-applied correctly. |

## 5. OQ-01 fallback (D5) & risk note

No other team confirmed which OData mechanism they use. Empirically, OData `$metadata` cannot supply column defaults, so "other teams use OData" can only refer to (1) data-read + `$expand` to resolve a lookup FK's display value, or (2) post-insert observation of the runtime-applied default — **both of which are data-plane techniques the chosen hybrid performs internally.** Reading `SysSchema` metadata tables via OData (interpretation 3) is possible but D2/D4-hostile and unconfirmed. **Risk note:** if a follow-up reveals other teams rely on a documented OData metadata annotation absent here (e.g. a newer platform `$metadata` extension), revisit D1 for Option A — but on all 8.x evidence captured, that annotation does not exist.

## 6. Environment-matrix coverage caveat

All evidence is from **one** matrix row — `studio0614`, **.NET Framework**, OData v4. No `.NET Core` instance was reachable (all 10 registered envs are `IsNetCore=false`). Findings are platform-code-level (CSDL emission; `EntitySchemaColumnDef`; insert pipeline) and expected to hold on `.NET Core`, but that is **not asserted as verified**. Second-row execution is the N-02 trigger if any behavior later proves host-dependent.

## 7. ADR status update (performed)

Per ADR §A4 on-completion instruction, the ADR has been updated: **Status → Accepted**, final option **B (Hybrid)** recorded, **D7 → OData data endpoint (SelectQuery fallback)**, **OQ-04 → enrichment default-on/fail-soft**. See [adr-read-column-default-values.md](../adr/adr-read-column-default-values.md) §"Decision outcome (FR-04 — Accepted)".

## 8. AC traceability

| AC | Where |
|----|-------|
| AC-04 (matrix scored D1–D7, one chosen option, every rejection reasoned, evidence refs) | §1, §2 |
| N-03 / SM-03 (each gap → DRAFT-AC by ID) | §3 |
| OQ-01 fallback recorded | §5 |
| D7 + OQ-04 resolved | §0, §2 |
| ADR status updated | §7 + ADR |
