# ADR: Positional component exclusion in the web→mobile page converter is a data rule, not converter code

**Status:** accepted · [ENG-95081](https://creatio.atlassian.net/browse/ENG-95081)

## Context

`get-mobile-page-conversion-guide` converts a Freedom UI web page into the element map, diffs and report a
mobile page is built from. Some components convert correctly everywhere except in one position. The
motivating case: `crt.SearchFilter` sitting in a `crt.ExpansionPanel`'s `tools` strip. On the web that strip
is a wide header row; on mobile it is a compact icon-only strip, and a search field does not fit it. The
same `crt.SearchFilter` elsewhere on the same page converts fine.

The converter already had a mechanism that removes a component type — `ComponentEquivalenceRule` with no
mobile counterpart, which reports the type as unsupported. Using it here would be wrong twice over: it bans
the type across the whole page, and it tells the reading agent "this type does not exist on mobile", which
is false and invites the agent to look for a replacement that is not needed.

So the converter needed a removal that is scoped to a POSITION — a (type, host type, host property) triple —
and a report that says "excluded here", not "unsupported".

Two further constraints shaped the design:

- **A banned component reaches the element map in two different shapes.** When every member of a host's
  child array resolves to a mobile type, the traversal walks those children into their own element-map
  entries and the host's `mobileValues` carries no nested copy. When some member does not resolve, the whole
  subtree is copied verbatim into the host's property and the banned component exists only as a JSON node.
  A removal that handles one shape silently misses the other, and which shape a given page produces depends
  on the mobile component registry, not on the page.
- **Removal interacts with three things the converter already reconciles**: empty-container cascade,
  attribute pruning in `mobileViewModelConfig`, and the request-conversion summary.

## Decision

### 1. Which component is banned from where is DATA

A new `excludedComponents` section in `Command/McpServer/Data/WebToMobilePageConversionRules.json`, matching
the conventions of the `viewConfigTemplates` section added before it. One filter is three properties:

| Property | Required | Meaning |
| --- | --- | --- |
| `type` | yes | the component type to remove |
| `parentType` | yes | the host component type that scopes the search |
| `propertiesContainerName` | no | the host property that further scopes it |

The pass knows none of these values. Adding the next exclusion is a rules-file entry, and the rules file is
already the seam through which converter behaviour is tuned without a release.

**Rejected: a hardcoded check for `crt.SearchFilter` in `crt.ExpansionPanel.tools`.** It is three lines
shorter and turns every future exclusion into a code change plus a release. The rules file exists precisely
so this class of decision is data.

**Rejected: reusing `ComponentEquivalenceRule` with an empty mobile type.** Discussed above — wrong scope
(whole page) and a wrong claim in the report (unsupported vs. excluded here).

### 2. An omitted `propertiesContainerName` WIDENS the search; it does not narrow it

With the property named, the search is confined to that property's nesting of the host. With it omitted, the
search covers the host's whole subtree, under any property, at any depth.

The alternative reading — treat an omitted property as "direct children only" — was rejected because it
makes the common case (ban this type anywhere in this host) unexpressible, and because an explicit scope
being an explicit boundary is the more useful guarantee: a rule that names `tools` must never reach into
`items`, even when the same type sits there.

### 3. The pass covers both shapes, in one class, with two phases

`ExcludedComponentsPass` is a standalone class rather than another slice of `WebToMobileAnalysisService`
(already several thousand lines). It runs both phases on every invocation:

- **Phase A — entry graph.** The banned component has its own `insert` entry. It is matched by climbing the
  `ParentName` chain to an ancestor of the filter's `parentType`. When the filter names a slot, the check
  applies to the EDGE ENTERING THE HOST — the ancestor attached directly to the host must occupy that slot —
  while the banned component itself may sit levels deeper through ordinary `items` edges. A match is
  replaced in place by a `drop` entry; entries whose chain passes through a removed element are dropped as
  orphans, because an insert whose parent no longer exists would resurrect the branch.
- **Phase B — verbatim carry.** The banned component is a JSON node inside a host property. Hosts are found
  structurally at any depth (array elements only — a matching plain property value is a config object, not a
  component) and processed outermost-first, so a subtree an outer filter removed is never searched again.

**Rejected: implementing Phase A only.** Phase A is the shape a real page with a complete mobile registry
produces, and it was tempting to call Phase B dead. It is not: whether a subtree is walked depends on the
mobile registry resolving EVERY member of the array, which a custom component in the strip is enough to
break. The shape is registry-dependent, so both are supported.

### 4. Every removal is reported; the pass never strips silently

Each removal emits a `drop` element-map entry with a reason naming the rule, the type, the host and the
slot. The reason is deliberately NEUTRAL about WHY the rule exists: the mechanical fact ("rule matched") is
derivable, the motivation is not, and asserting "does not fit" would be right for this rule and misleading
for the next one.

The agent-facing half of this contract lives in the shipped guidance library, not here: the
`freedom-page-web-to-mobile-conversion` guide teaches that an `excludedComponents` drop is a POSITIONAL
exclusion and never conversion loss, so the agent must not re-insert the component or ask the user about it.
Without that, an agent reads the drop as a converter defect and undoes it — see
[clio-knowledge#76](https://github.com/Advance-Technologies-Foundation/clio-knowledge/pull/76).

### 5. Pass ordering: before `RemoveEmptyContainers`

A chrome branch this pass empties must cascade away, and the empty-container pass is what cascades. Running
after it would leave an empty container behind on the converted page. This ordering is load-bearing and
stated at the call site.

### 6. Removal is layout cleanup, not attribute cleanup

An attribute referenced only by a removed element is KEPT in `mobileViewModelConfig` — the same policy the
empty-container removal follows, and for the same reason: the element left for a positional reason, the data
behind it did not become wrong. A request binding recorded for a removed element IS reclassified into
`droppedRequests`, because the report must not claim a conversion for an element the map says not to create.

Both phases feed the removed-web-name set that carries the attribute exemption. Today that changes no
output, because the attribute-consumer walk descends `items` only and a Phase B node is by construction not
under `items`. It is done anyway so the two phases cannot diverge if that walk ever learns to descend
`tools`/`menuItems` — the failure mode there is a silently missing access gate on a converted page, not a
red test. See `docs/knowledge/McpServer/excluded-components-phase-b-names-have-no-consumer-today.md`.

Only Phase A feeds the removed-MOBILE-name set that drives request reclassification, and that asymmetry is
correct rather than an oversight: a binding is recorded only for an element the traversal walked into its
own entry, keyed on that entry's mobile name, and a Phase B node was never walked and has no mobile name.

### 7. Only `insert` entries are removal candidates; any entry can be a host

A `merge` entry describes an element the MOBILE TEMPLATE owns. A `drop` cannot un-create it, so emitting one
would report a removal that never happens. A banned type arriving as a template twin therefore survives this
pass by design; excluding template-owned chrome is the template's problem, not this pass's.

## Consequences

- The next positional exclusion is a rules-file entry plus a test — no code change.
- The rules file and the page both arrive from outside the binary (CDN / environment), so every recursion in
  the pass is bounded by a depth budget and a visited set. A malformed parent graph abandons the branch
  instead of hanging the MCP server.
- The filter `type` is compared against different type domains per phase (resolved mobile type on the entry
  graph, raw web type on the verbatim carry). They coincide for every type any bundled rule targets. A future
  rule targeting a type an equivalence rule RENAMES covers one phase only and must ship as two filter entries.
  Documented on `ExcludedComponentFilterRule.Type`; no reverse lookup is attempted, because teaching this
  pass the equivalence map to cover a case no rule has would buy back exactly the coupling the pass avoids.
- The MCP tool contract is unchanged: the new behaviour is rules data plus additional `drop` entries and
  `droppedRequests` reasons in the existing report shape.
