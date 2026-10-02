# Agent-facing package dependency contract review

Status: original design assessment, followed by implementation on 2026-10-02. Issue #1729 / ENG-100222. See [implementation and runtime validation](package-dependency-explorer-validation.md) for the final contract, accepted corrections and proof. The proposal below is retained as design rationale.

## Intent and conclusion

Let a coding agent discover the correct schema/package context, explain dependencies, and assess a proposed dependency change without guessing Creatio internals or accumulating release-specific Clio workarounds.

The feature is a useful read-only foundation. Its graph supports dependency/path/export commands. Its search and verdict contracts are not sufficient for reliable agent decisions. Improve the Creatio boundary before freezing a public v1; keep Clio responsible for presentation and graph traversal. Reuse existing Creatio designer queries and validators instead of implementing another resolution engine.

## Evidence and its limits

Source revision: `1df1eb11485dd518d203ae786e2dcb0bacb8b823`, feature/ENG-100222-package-dependencies-service. Source was built with the user's Jarvis prototype and deployed into disposable `issue-1729-344` using the 10.2.344 seed. Evidence is retained under `F:\Projects\Issue-Workspaces\issue-1729`.

| Observation | Evidence | Implication |
| --- | --- | --- |
| All four methods respond successfully on the deployed branch; graph contains 181 packages and 567 edges | source-GetPackageGraph.json and other source-*.json | Actual HTTP execution, not merely source availability |
| Stock build previously returned 404 for the four methods | Earlier disposable-runtime probes | Contract versioning cannot make the feature exist on pre-feature servers |
| Broad entity-schema query `a` returns 200 rows and omits 26 matches returned by `Contact` | probe-schema-broad.json, probe-schema-exact.json | Confirmed truncation without a completeness signal |
| `Contact` returns 34 rows, only six exact layers | probe-schema-exact.json | Searching and resolving are different operations |
| Four highest exact Contact layers are outside CrtUIv2's dependency closure | Graph traversal joined to search result | Global hierarchy rank cannot establish package-context visibility |
| Unknown `matchMode`, `packageUId`, and `contractVersion:999` fields are ignored | probe-schema-exact.json, probe-ignored-contract-version.json | Sending new optional request semantics to an older server is unsafe without explicit support detection |
| `%` returns 200 rows, none containing a literal percent; invalid manager returns successful empty result | probe-wildcard.json, probe-invalid-manager.json | Search grammar and invalid-input behavior need explicit contracts |
| CrtUIv2 -> CrtNUI has 212 reasons, isUnused=false, canDrop=true | source-GetPackageDependencyReasons.json, source-GetDependencyDropImpact.json | Valid result: alternate path through CrtDesignerTools preserves all reason-target reachability; reasons are not removal blockers |

Source-confirmed gaps, not reproduced runtime failures:

- Drop validator checks parents, lookups, bindings and SQL dependencies, but not the client-unit dependency table used by the reasons endpoint. Its response documentation promises removal "without breaking anything". This promise exceeds its implementation.
- Package accessibility recurses without a visited set. Drop-impact entry does not reject a graph with cycles. A stack-overflow path is credible; supported-flow reachability and an actual process crash have NOT been demonstrated. Do not crash the deployed stand as a test: use an isolated validator fixture/process.
- Graph version token covers package/dependency aggregates, not schema changes or every application table in the response. It is not a consistent metadata snapshot or an optimistic concurrency token. Actual stale cache behavior is not yet reproduced.
- Reason queries inspect a bounded set of metadata sources and active package tables; no reasons does not establish unused code, particularly for inactive packages or dynamic references.

Relevant source, relative to the Creatio worktree: `TSBpm/Src/Lib/Terrasoft.Core/Packages/DependencyExplorer/PackageDependencyExplorer.cs:197`, `PackageDependencyExplorerDataProvider.cs:366`, `PackageGraphAlgorithms.cs:214`; `Packages/PackageValidator.cs:493` and `:1829`; `Packages/PackageDependenciesVersionToken.cs:86`; `Terrasoft.Core.ServiceModelContract/PackageDependencyExplorerDataContract.cs:194`.

## Smallest useful server surface

Keep four existing concepts, add two semantic operations, and use a small capability bootstrap:

| Operation | Required behavior | Agent benefit |
| --- | --- | --- |
| Graph | Explicit edge direction, stable IDs, cycles, missing/inactive packages; document revision scope | Explain direct/transitive/reverse paths without per-release rules |
| Schema search | Exact or literal contains mode, manager validation, deterministic ordering, bounded results with hasMore/limit | Distinguish absent from incomplete and a typo from absence |
| Resolve schema context | Package + schema + manager + purpose (initially supported reference/extend cases); reuse designer availability queries | Determine usable layer rather than select the globally highest layer |
| Dependency reasons | Known reasons and coverage, compact counts with optional detail; separate informational reasons from removal blockers | Explain the actual source of a dependency without spending the context budget on a graph dump |
| Drop impact | Existing validator plus missing metadata checks, explicit assessment and coverage, stable errors | Avoid treating an incomplete check as a universal safety guarantee |
| Add impact | Existing GetCanAddPackageDependency checks plus applicable platform constraints; report checks performed | Reject self/cyclic/Custom dependencies on the server; avoid Clio duplicating rules |

Context resolution must distinguish resolved, not visible, ambiguous, not found, and unsupported purpose/manager. Return schema/package UIds and names, selected layer and an explanation. Candidate packages may be returned when nothing is visible, but there is not always one universally "minimal" dependency: preserve ambiguity and do not silently select one by hierarchy rank. Reachability is useful evidence, not a substitute for designer semantics, write permission, or feature-dependent Custom behavior.

Start with entity-schema reference and extension semantics, whose existing query primitives have been located. Advertise those supported cases explicitly. Do not promise a generic resolver for every schema manager before its semantics are implemented and tested. Read operations must not call a designer get-or-create path that creates state.

Search must expose incompleteness from the first release. A first bounded-search version can return hasMore and require a narrower query. If complete enumeration is supported, add conventional bounded pagination with deterministic tie-breaks and documented behavior under concurrent edits; do not promise snapshot consistency or build a durable cursor service. Graph export remains the operation for full graph enumeration.

Reasons should expose a typed location where available (schema UId, column UId/name or dependency kind) alongside human detail. Do not make agents parse localized prose. Detailed reason pagination can follow once required; a summary must still report omitted detail.

## Stable contract boundary

Proposed family: `/ServiceModel/PackageService.svc/dependencies/v1/...`, registered through ordinary WCF UriTemplate metadata. Existing routing supports multi-segment templates; this new family still needs a .NET Core and .NET Framework route test before claiming support. Method-name suffixes are an acceptable equivalent if platform conventions require them; the explicit immutable major-version boundary is the important part.

Use separate v1 wire DTOs mapped from internal models. Do not serialize evolving domain models directly into a public contract. Keep existing unversioned methods for any existing feature-branch UI consumer while moving Clio to v1. Do not create v2 now.

Add one authenticated, stable capabilities operation. Illustrative response:

```json
{
  "success": true,
  "contracts": [{
    "major": 1,
    "operations": ["graph", "searchSchemas", "resolveSchema", "reasons", "dropImpact", "addImpact"],
    "features": ["schemaSearch.literalContains", "schemaContext.entityReference", "schemaContext.entityExtend"],
    "limits": {"schemaSearchMaxItems": 200, "dropImpactTimeoutSeconds": 60}
  }]
}
```

Names above are proposed, not existing endpoints or promises of implemented coverage. Advertise only real implemented operations/features. Capability discovery does not grant authorization or guarantee a subsequent request will succeed.

- Clio pins a major it implements; when it implements several, use a common supported major. Never use an arbitrary newest advertised version.
- Within v1, preserve field types, meanings, defaults, ordering guarantees, required inputs and error-code meanings. Optional response fields may be added. A newly required input or changed meaning needs v2.
- A new optional request field that changes selection or safety semantics requires a capability check. Unknown-field tolerance is not proof that the field was honored.
- Unknown major paths fail explicitly. A missing capabilities operation means this versioned agent feature is unsupported; it does NOT prove that v1 exists. Do not infer support from a Creatio build number or fall back to the weaker experimental methods.
- Retain the v1 adapter when adding v2, under a documented support policy. New internal models map back to old semantics. If semantics can no longer be honored, explicitly retire the contract rather than quietly changing its meaning.
- Clients ignore unknown presentation fields. Unknown assessment values or reason coverage must never default to safe/complete. New blockers remain blockers even when their detailed kind is unfamiliar.
- No Accept-header negotiation, per-build DTO copies, generic RPC framework, schema-download/code-generation protocol, or Clio-side server-version switchboard.

| Client / server | No feature | v1 | v1 + optional additions | v1 + v2 | v2 only |
| --- | --- | --- | --- | --- | --- |
| Clio implementing v1 | Unsupported | Works | Works with v1 semantics | Uses retained v1 | Incompatible, explicit error |
| Clio implementing v1 and v2 | Unsupported | Uses v1 supported subset | Uses v1 advertised features | May use v2 | Uses v2 |

This supports older feature-capable Creatio releases and newer ones without synchronized upgrades. It cannot support operations absent from a server. Contract retention is a product commitment, not something a version number automatically guarantees.

## Honest verdicts and errors

Prefer `assessment: blocked | noKnownBlockers | unknown` over an unrestricted canDrop guarantee. Include checkedKinds and uncheckedKinds, plus structured problems. Unknown covers timeout, incomplete data or unsupported checks; operation errors remain errors rather than empty successful arrays. A noKnownBlockers verdict never claims analysis of arbitrary dynamic code or guarantees write permission.

Client-unit impact must evaluate the graph after removing the SINGLE edge, including affected dependent packages. Reusing the current reasons endpoint unchanged is insufficient: it removes a target node from its reachability calculation and always includes that target. A redundant edge must not become blocked merely because reasons exist. Reuse its data extraction with correct post-removal reachability, not its current target-selection algorithm.

Use stable domain codes such as PACKAGE_NOT_FOUND, NOT_DIRECT_DEPENDENCY, INVALID_MANAGER, UNSUPPORTED_CONTEXT, GRAPH_CYCLE, VALIDATION_BUSY and VALIDATION_TIMEOUT. Human messages may be localized. Preserve the established BaseResponse envelope if practical; do not couple public codes to CLR exception type names. Authentication/transport failures remain distinguishable from domain failures. Busy/timeout may be retryable; not-found is not automatically retryable.

Reject cyclic graph validation before entering the unsafe recursive validator, or make that traversal cycle-safe. A guard is the smaller feature-local defense; testing must include an unrelated cycle because the validator scans packages beyond the requested pair.

Keep contractMajor separate from graphRevision. Do not relabel the current aggregate token as a full data revision. Echoing that token on a schema-dependent assessment would not establish freshness. If callers need change detection, define which data the token covers and test it; any future mutation must revalidate on the server. Avoid inventing transactional multi-request sessions for this read-only feature.

## Reasonableness checks and release gates

| Scenario | Required result |
| --- | --- |
| New server adds internal fields or changes its model | Retained v1 adapter preserves old client behavior |
| Server ignores an unknown request filter | Client never sends an unadvertised semantic feature |
| Capability endpoint missing or request denied | Unsupported and authorization failure are distinct; no guessed support |
| Manager typo / literal underscore or percent | Invalid manager is explicit; contains mode searches literals |
| More than 200 matches | Result says incomplete; absence is not inferred from an incomplete list |
| Several Contact layers, including inaccessible or inactive packages | Context resolver uses platform semantics; no global-rank shortcut |
| Two equally suitable candidate packages | Return ambiguity and evidence; do not auto-add one |
| Direct edge is redundant but has reasons | Drop impact accounts for alternative paths |
| Only client-unit metadata requires a nonredundant edge | Removal assessment reports blocker |
| Cycle, missing target, inactive source or unsupported metadata | Explicit result/coverage; no process crash or false unused claim |
| Validation is busy or times out | No positive verdict; bounded resource use and retry semantics |
| Schema/app metadata changes without graph edge changes | Do not claim current graph token proves unchanged assessment |
| Requester can view configuration but cannot modify packages | Read permission and change feasibility are not write authorization |
| Future server sends unfamiliar blocker kind | Old client retains blocked assessment and displays useful details |
| Two agents change dependencies between read and action | Read-only preview grants no concurrency guarantee; write path revalidates |

Compatibility testing must include a retained v1 client against the newer server, not only new server DTO snapshots. Test wire shape AND semantic fixtures (selection, validation, errors, coverage). Exercise real stdio MCP against the deployed server after implementation. Rebuild/redeploy changed backend sources and prove readback; current successful probes validate only the existing four methods.

## Independent review disposition

Fable 5.1 review `rev_f6244365dd95426d` completed read-only. Full report retained in the local evidence directory as fable-contract-review.md.

- Accepted: cycle defense, missing client-unit validation, honest reason scope, distinct errors, server-owned context, simple explicit version boundaries and retained old contracts.
- Refined: an explicit v1 family is preferable before first public release; method suffixes could achieve the same compatibility. No mandatory framework is needed.
- Rejected: treating contextual search/completeness as later-only work; they are central to an agent deciding which dependency it needs.
- Rejected: capability 404 means v1. Stock servers also return 404 and have no such operations.
- Rejected: graph closure alone is authoritative designer resolution; use existing availability primitives.
- Rejected: additive request fields are automatically safe, or echoing the current graph token creates a schema snapshot.
- Deferred pending a fixture: the precise cyclic-crash scenario, inactive-source output and reported downstream lookup-validation asymmetry. They are source concerns, not claimed live reproductions.

## Implementation order and KISS check

1. Freeze version/error/coverage rules and bounded search semantics on the feature branch; add cycle defense and a client-unit dependency fixture.
2. Add the smallest versioned adapter, capabilities and entity context operation using existing platform queries. Expose add-impact through the existing validator. Prove each changed behavior on the disposable source runtime.
3. Implement Clio's five issue commands/MCP tools against that contract; keep graph algorithms local and semantic decisions in Creatio. Update help, guidance and cross-version tests.

The intent is dependable dependency decisions for coding agents. The flow is capabilities -> focused query/context -> evidence/preview -> ordinary platform operation with its own validation. Necessary moving parts are a versioned DTO boundary and capability discovery. No general negotiation engine, duplicate resolver, automatic dependency mutation or durable snapshot service is needed. This review expands the original wrapper-only plan; it is a concrete proposal, not a claim that issue #1729 or the backend changes are complete.
