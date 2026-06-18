# ADR: Analytics-Widgets Authoring Floor — clio-owned single source of truth, thin toolkit pointer

- **Status:** Accepted
- **Date:** 2026-06-18
- **Feature:** `analytics-widgets-floor`
- **Jira:** ENG-TBD (migration-tracking placeholder; the real ticket id is a human gate — keep it literally as ENG-TBD)

---

## Context

AI agents authoring Creatio Freedom UI analytical widgets (metric/indicator tiles and charts)
need native-looking-result guidance: the metric-band-then-chart-grid layout, the 12-column
sizing math, per-widget-type default sizes, and the plain-white default card style. Two
delivery facts shaped this decision:

- **`creatio-ui-guidelines` was rolled back** and has no home. Guidance that referenced it for
  surrounding page structure / fields / accessibility — and the form-page container-column
  rule it carried — now points at nothing.
- **A toolkit-only delivery strands non-toolkit clients.** Any MCP client that talks to clio
  directly (no ADAC toolkit installed) would get no analytics guidance. The example
  `creatio-analytics-widgets` toolkit skill also **duplicated clio's `dashboards` guide
  near-verbatim** (`references/dashboard-layout.md` mirrored `DashboardGuidanceResource`), so
  the content existed twice and would drift.

`analytics-widgets` **did not exist on `main`** — it is created net-new by this work. The
guidance invariants live in `McpGuidanceResourceTests` / `PageToolsTests` /
`GuidanceGetToolTests` (there is no `McpGuidanceForcingTests.cs` on main).

## Decision

Ship **one** clio-owned authoring floor for analytics widgets, reachable by every MCP client
that talks to clio, and reduce the ADAC toolkit skill to a **thin pointer**. The floor routes
to the existing already-owned guides instead of copying them.

### Frozen contract — identifier table (verbatim)

These identifiers are the frozen contract. Every artifact uses them byte-for-byte.

| Role | Identifier (verbatim) |
|------|------------------------|
| Guide name (`get-guidance` / `GuidanceCatalog` key; lowercase; catalog uses `StringComparer.OrdinalIgnoreCase`) | `analytics-widgets` |
| Guide URI | `docs://mcp/guides/analytics-widgets` |
| Guide `[McpServerResource]` Name | `analytics-widgets-guidance` |
| Reference URI | `docs://mcp/references/analytics-widgets/placement-contexts` |
| Reference `[McpServerResource]` Name | `analytics-widgets-placement-contexts-reference` |
| See-also guide names (MUST already exist; referenced BY NAME, never copied) | `dashboards`, `indicator-widget`, `page-modification` |
| Toolkit skill directory + `SKILL.md` frontmatter `name` (RENAMED from the example's `creatio-analytics-widgets`) | `analytics-widgets` |

### Interim banner (verbatim)

This exact text is the FIRST line of the clio Guide.Text AND the top of the toolkit
`SKILL.md` body:

> INTERIM (review by 2026-12-18): analytics-widgets is the clio-owned single source of truth for analytics-widget authoring; the ADAC toolkit ships only a thin pointer. Migration tracking: ENG-TBD. Do not duplicate this content elsewhere.

### Page-tool trigger clause (verbatim)

This exact sentence is appended AFTER the existing page-modification clause in each page tool
`[Description]`:

> If the page is a dashboard/analytics page, or you are adding metric (indicator) widgets or charts, call get-guidance with name `analytics-widgets` first - it routes to dashboards (layout), indicator-widget (payload), and placement-contexts (non-dashboard surfaces).

## Constraints (the 8 guardrails)

1. **clio is the single source of truth.** The `analytics-widgets` guide and its
   `placement-contexts` reference live in clio; the toolkit ships only a thin pointer.
2. **No duplication.** The floor references `dashboards` / `indicator-widget` /
   `page-modification` **by name** and never copies their content. The example skill's
   near-verbatim copy of `DashboardGuidanceResource` is explicitly NOT carried forward.
3. **1-skill → 3-guides routing.** The single `analytics-widgets` entry point routes to three
   guides by responsibility: `dashboards` (layout), `indicator-widget` (payload), and
   `placement-contexts` (non-dashboard surfaces); surrounding page structure routes to
   `page-modification`.
4. **Drop all `creatio-ui-guidelines` references.** It was rolled back and has no home; no
   artifact may reference it.
5. **Re-home the form-page container-column rule to `page-modification`.** The rule the
   analytics guidance used to take from `creatio-ui-guidelines` now lives in `page-modification`.
6. **Ungated, additive, pull-only.** The floor ships without a feature toggle. It only adds a
   guide + reference + a routing sentence; nothing is removed from existing flows and no agent
   is forced onto it — callers opt in by reading the guide.
7. **Frozen identifiers, verbatim.** All artifacts use the frozen-contract strings byte-for-byte
   (guide name, URIs, resource Names, skill name, interim banner, trigger clause).
8. **PR ordering.** PR-A (clio floor + trigger) lands before PR-B (thin toolkit pointer) so the
   toolkit never points at a floor that does not yet exist.

## Detailed decisions

### Drop `creatio-ui-guidelines`; re-home the form-page container-column rule

`creatio-ui-guidelines` was rolled back and has no home, so every reference to it is removed
(constraint 4). The one concrete rule the analytics guidance used to borrow from it — the
**form-page container-column rule** (which container/column an analytics widget belongs in on a
form page) — is **re-homed to `page-modification`** (constraint 5). `page-modification` already
owns container selection and surrounding page structure, so this is its natural owner; the
analytics floor routes to it rather than restating it.

### Ungated feature-toggle posture (additive, pull-only) + the override rule

The floor is **ungated**: no `[FeatureToggle]` on the guidance resource type or catalog entry.
This is safe because the change is **additive and pull-only** — it adds a guide, a reference,
and one routing sentence on page-tool descriptions; it removes nothing and changes no existing
behavior. An agent only consumes the floor if it chooses to read `analytics-widgets`.

**Override rule:** when the routed guides disagree, the responsibility owner wins for its own
domain — `dashboards` is authoritative for layout/sizing, `indicator-widget` for the runtime
payload, `page-modification` for surrounding page structure and the form-page container-column
rule. The `analytics-widgets` floor is a router and never overrides an owned guide's content;
where the floor and an owned guide appear to conflict, the owned guide governs.

### 1-skill → 3-guides routing

One entry point (`analytics-widgets`) maps to three guides by responsibility (constraint 3):

| Need | Routes to |
|------|-----------|
| Layout — 12-column grid, metric band, chart grid, sizes, plain-white style | `dashboards` |
| Runtime payload — indicator/aggregate selection, static filters | `indicator-widget` |
| Non-dashboard surfaces — home page, list analytics view, form widget area | `placement-contexts` reference (`docs://mcp/references/analytics-widgets/placement-contexts`) |
| Surrounding page structure + form-page container-column rule | `page-modification` |

### Migration ticket placeholder + exit criteria

Migration tracking is **ENG-TBD** (a deliberate placeholder — the real ticket id is a human
gate; keep it literally as ENG-TBD). The interim arrangement (clio floor + thin toolkit pointer)
is reviewed/retired when **any** of these exit criteria is met:

1. Native MCP skills reach GA (the platform can ship skills natively, removing the need for the
   toolkit pointer).
2. A second non-toolkit consumer of the floor appears (proves the clio-owned floor is the right
   home and the toolkit pointer can be reconsidered).
3. A drift incident occurs (the toolkit and clio floor diverge despite the no-duplication rule —
   forces a re-evaluation of the split).
4. The hard review date **2026-12-18** is reached (matches the interim banner).

PR-A lands before PR-B (constraint 8): the clio floor and trigger ship first; the thin toolkit
pointer ships second, after the floor it points at exists.

## Alternatives considered

1. **Toolkit-only (status quo of the example skill).** Rejected: strands non-toolkit clients
   and duplicated `dashboards` near-verbatim → guaranteed drift.
2. **Keep referencing `creatio-ui-guidelines`.** Rejected: it was rolled back and has no home;
   references would dangle. The one rule worth keeping is re-homed to `page-modification`.
3. **Gate the floor behind a feature toggle.** Rejected for this iteration: the change is
   additive and pull-only, so a gate adds operational surface (enable-before-use) without
   reducing risk. Revisit only if the floor ever starts changing existing flows.
4. **Copy the layout content into a new analytics guide.** Rejected: violates the
   no-duplication guardrail; `dashboards` already owns layout and would fork.

## Consequences

### Positive

- Every MCP client talking to clio gets the same analytics floor, regardless of the toolkit.
- Zero duplication: one owner per concern; the floor only routes.
- Reversible and reviewable: interim banner + ENG-TBD + four exit criteria + a hard review date.
- Additive/pull-only: no behavior change for flows that do not author analytics widgets.

### Negative / risks

- The toolkit pointer can still drift from the clio floor (mitigated by the no-duplication rule,
  the interim banner, and exit criterion 3).
- `ENG-TBD` is a placeholder; until a real ticket exists, migration tracking is informal
  (accepted — the ticket id is a human gate by design).
- The form-page container-column rule moves owners (`creatio-ui-guidelines` →
  `page-modification`); consumers that learned it from the old home must re-discover it via
  `page-modification`.

## Invariant coverage

`analytics-widgets` is net-new, so the floor's invariants (catalog entry resolves the guide;
guide/reference resource Names and URIs match the frozen contract; page-tool descriptions carry
the trigger clause; banner text is byte-identical across clio Guide.Text and toolkit `SKILL.md`)
are asserted in the existing guidance test homes — `McpGuidanceResourceTests`, `PageToolsTests`,
and `GuidanceGetToolTests` — not in a `McpGuidanceForcingTests.cs` (which does not exist on main).
