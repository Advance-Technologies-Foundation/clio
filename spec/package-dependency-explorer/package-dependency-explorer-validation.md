# Package dependency v1 implementation and validation

## Delivered behavior

Creatio retains the four original feature methods and adds a read-only contract below
`ServiceModel/PackageService.svc/dependencies/` (prefix `0/` on .NET Framework):

| Route | Purpose |
| --- | --- |
| `capabilities` | `contracts[]`, each with major, operations, features and maximum search size |
| `v1/graph` | Detached v1 graph DTOs; topology revision, explicitly not a schema snapshot |
| `v1/schemas/search` | Exact or literal contains, case-insensitive, deterministic order, `hasMore` |
| `v1/schemas/resolve` | Entity reference/extension visibility through existing designer queries |
| `v1/reasons` | Registered metadata reasons, `hasKnownReasons`, checked/unchecked coverage |
| `v1/drop-impact` | Existing platform validator plus registered client-module/downstream reachability |
| `v1/add-impact` | Existing platform add rules, structured cycle/custom errors, explicit already-present result |

Clio adds six matching CLI/MCP operations: `get-pkg-dependencies`, `pkg-dependency-path`,
`pkg-dependency-why`, `find-pkg-by-schema`, `export-pkg-graph`, `check-pkg-dependency`.
Its existing entity-designer diagnostic now subtracts transitive reachability. Unsupported
servers leave that diagnostic explicitly uncertain instead of silently using direct-only data.

Contract majors belong to the API, not to Creatio release numbers. Future versions must retain
v1 semantics and DTOs while publishing a separate v2 adapter for breaking changes. Optional
features are advertised per major; new request semantics are never inferred from ignored JSON
fields. Clio ignores unknown response fields, rejects incompatible majors/unknown verdicts and
never interprets capability 404 as support. No negotiation framework or release-number table.

## Runtime evidence (2026-10-02)

The implementation was built with the user's Jarvis source-build helper and deployed over the
10.2.344 seed to disposable `issue-1729-344` (.NET 10, PostgreSQL). Stock `issue-1729`
(10.2.301) supplies the unsupported-server comparison. Original feature base:
`1df1eb11485dd518d203ae786e2dcb0bacb8b823`.

The local implementation is rebased onto Alex's PR #1494 cache-invalidation fix
`e26c74e5827c81e1e4c411a348d58240a4056491`; our local Core commit is `9a28073ea6f`.
That upstream change adds inactive packages, installed applications, memberships, application
dependencies and the maintainer to the graph cache token. It changes no endpoint contracts.
The v1 adapter reuses this cache and Clio treats the graph revision as an opaque value.

| Question | Original observation | New runtime observation |
| --- | --- | --- |
| Exact ownership | Contact returned 34 rows, six exact | Contact and lowercase contact return the six exact rows |
| Search completeness | Broad query silently stopped at 200 | `a --contains --limit 3` returns three rows and `hasMore: true` |
| Literal search | `%` returned 200 unrelated rows | `% --contains` returns zero rows, `hasMore: false` |
| Package context | Global rank includes inaccessible layers | Contact in CrtUIv2 resolves designer-selected UId `16be3651-8fe2-4159-8dd0-a803d4683dd3` |
| Redundant edge | CrtUIv2 -> CrtNUI has 212 reasons but canDrop=true | Reasons remain informational; removal has no known blockers because an alternate path exists |
| Registered client import | Legacy package validator has no client-table check (source evidence) | Sole-path removal blocked with `ClientUnitRequire`, consumer schema and helper name |
| Alternate path | Potential false positive if every reason treated as essential | Adding a bridge path makes direct-edge removal have no known blockers |
| Downstream consumer | Must inspect packages above the edited edge | Bridge -> Leaf removal blocked by Consumer in Root, which depends on Bridge |
| Raw AMD body only | No registered dependency row produced by body save | Explicitly excluded from completeness; never claim safe-to-delete |
| Add cycle | Human text alone is insufficient | `blocked`, `CircularDependency`, correct package UId and no bogus schema UId |
| Existing edge | No actual proposed change | Stable `ALREADY_DEPENDENCY` error |
| Read-only proof | Success status alone insufficient | Before/after dependency arrays equal after previews; eight real stdio MCP tests pass |
| Old runtime | Original/new routes unavailable | Real MCP reports unsupported v1 without legacy fallback |

The client-module fixture consists of three disposable packages: Leaf -> CrtCoreBase,
Bridge -> Leaf, Root -> Bridge, with a Helper module in Leaf and Consumer in Root. Consumer's
AMD body imports Helper. A **registered** reference was added through the platform's existing
`DataService/json/SyncReply/ClientUnitSchema` contract (`dependencies` maps name to schema UId).
Read the schema first with `ClientUnitSchemaRequest`; preserve its fields and use an empty
`resources` map for this otherwise empty module. The read response's resource paths are not
the GUID-keyed save contract. This is fixture setup only, not a new product endpoint.

First test Root -> Leaf as the sole path, then add Root -> Bridge -> Leaf and retest. After
removing the redundant Root -> Leaf edge, preview Bridge -> Leaf: it must name the downstream
Consumer as a blocker. The final fixture remains on the disposable stand for review; no preview
removed that required edge. Evidence logs are retained under the issue workspace's evidence
folder, including `fixture-registered-removal.log`, `fixture-alternate-removal.log`,
`fixture-downstream-final.log`, `fixture-graph-before.log`, `fixture-graph-after.log`,
`fixture-add-cycle-final.log`, and `mcp-v1-e2e.log`.

## Executable verification

- Creatio Core: 282 passed, one existing skip, filtered to `Packages.DependencyExplorer` and
  `PackageValidatorTestCase`. Includes downstream alternate paths, cycles, inactive/missing
  scope, already-present edges and structured problem identity.
- Creatio ServiceModel: 54 passed for `PackageServiceTestCase`, including actual DataContract
  serialization, reference/extension context, authorization and sanitized failures.
- .NET Framework build: `dotnet build TSBpm/Src/Lib/Terrasoft.Core.ServiceModel/Terrasoft.Core.ServiceModel.csproj -f net472 -p:NetFrameworkOnly=true` succeeds.
- Clio: `dotnet test clio.tests/clio.tests.csproj -f net10.0 --filter "TestCategory=Unit"`:
  14,957 passed, 25 existing skips. Unknown/additive fields, v1+v2 coexistence, scoped capabilities,
  transitive filtering, command mapping and both route prefixes are covered.
- Live MCP: set `CLIO_DEPENDENCY_E2E_ENVIRONMENT` to the source stand and
  `CLIO_DEPENDENCY_E2E_OLD_ENVIRONMENT` to the stock stand, then
  `dotnet test clio.mcp.e2e/clio.mcp.e2e.csproj -f net10.0 --filter FullyQualifiedName~PackageExplorerToolE2ETests`:
  eight passed, none skipped. Fresh real stdio child, semantic assertions and edge readback.
- Guidance producer suite: 214 passed. Guidance remains a companion draft until reviewed;
  it explicitly gates this workflow on available Clio tools AND server-advertised contract v1.
- ClioRing compatibility reviewed: `dotnet test clio-ring/ClioRing.Tests/ClioRing.Tests.csproj -c Release`:
  157 passed. `dotnet publish clio-ring/ClioRing.Desktop/ClioRing.Desktop.csproj -c Release -r win-x64 --self-contained true -p:PublishAot=true`:
  succeeded. The published native executable's `--ipc-proof --clio-dll <changed-clio.dll>
  --env issue-1729-344 --out <report.md>` passed connection, catalog, read-only environment
  description, child shutdown and respawn. The consumer discovers the full catalog through
  `ClioRing.Ipc/ClioIpcClient.cs`; no Ring implementation coupling was introduced.

## Review and remaining boundaries

Seven local review perspectives and Fable (`rev_fb9a87b473b24f85`) examined the implementation.
Accepted corrections: per-major capabilities, semaphore/timeout coverage, client-only metadata
query, honest JavaScript limits, scoped inactive/missing guards, package error kinds/identity,
explicit already-present add, sanitized unexpected errors. Focused re-review found no blocker.

Verified live only on PostgreSQL/.NET 10. .NET Framework builds and both URL forms pass tests;
Oracle, SQL Server and .NET Framework **runtime behavior is not certified**. Future v2
coexistence is a contract test, not a deployed future Creatio release. Inactive/missing guards,
permission denial and timeout/busy paths have focused tests rather than a destructive live
fixture. Dynamic code/source imports and write authorization remain outside impact coverage.
No atomic snapshot or guarantee against concurrent edits is promised; a real write revalidates.

KISS check: capabilities -> focused query or designer context -> evidence/preview -> existing
mutation command. Creatio owns semantic checks; Clio owns traversal/presentation. There is no
automatic mutation, duplicated designer engine, durable cursor or general version framework.
The Creatio branch is local only; these changes are not merged or released.
