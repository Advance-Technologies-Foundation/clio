# Clio 8 migration ledger

This is an incremental migration, not a compatibility-complete Clio 8 replacement. The behavior reference is master commit `11293657cb326cb3b488c891ca9e973b5aea8c14`. Legacy source and its dependency graph are not imported. The primary checkout remains untouched.

## Inventory and acceptance

`legacy-cli-inventory.json` records 239 CLI declarations, including hidden declarations. `legacy-mcp-inventory.json` separately records 201 explicit MCP tool declarations; many overlap CLI capabilities. It is a source inventory, not a claim that all are registered or supported by Clio 10. Regenerate it with:

```shell
python scripts/inventory-legacy.py C:/Projects/clio --ref 11293657cb326cb3b488c891ca9e973b5aea8c14
```

Aliases belong to their canonical operation. A CLI inventory alone does not cover MCP-only operations, resources, prompts, shipped guidance, or Ring consumer contracts. These require their own parity checks before replacement of Clio 8. Never count a subprocess forwarding call, stub, or descriptor without an implementation as a completed port.

Each feature needs input/default evidence, external effects, result/error semantics, composition tests, primitive integration, adapter checks, and a recorded live-validation boundary. `implemented-partial` means a useful implementation exists, while legacy syntax, output, or environment coverage still differs. No entry is marked full parity yet.

## Implemented operations

All operations below are available to managed callers, CLI, and the stable MCP `execute` tool. Discover their current argument schemas through `--list`, `<operation> --help`, or MCP `list-operations`.

| Operation | Implemented behavior | Remaining qualification |
|---|---|---|
| restart | Login, restart/unload route selected for the target platform | Acceptance is not readiness; original prototype live evidence only |
| flush-redis | Login and ClearRedisDb, optional sequential restart | Does not establish every Redis key's deletion |
| ping-app | Authenticate, probe .NET application root or Framework /0/ping; optional explicit endpoint | HTTP redirects count as reachability only; no readiness claim or legacy spinner |
| call-service | GET/POST/PUT/PATCH/DELETE, body/file input, literal variables, raw response destination | JSON object variables replace legacy variable-string syntax; same-origin application paths only |
| dataservice | Raw SELECT/INSERT/UPDATE/DELETE JSON body | File input/output/variables and MCP-only execute-esq validation/output limits are not ported |
| build-workspace | Modified build or complete rebuild | Structured response; no compilation-event stream/readiness tracking |
| generate-source-code | Background, modified, required, or all; legacy flag precedence | Background acceptance is not generation completion |
| last-compilation-log | Read the persisted compilation report | Raw structured payload, not legacy formatted log lines |
| restore-configuration | Restore backup with legacy data/SQL flags | Endpoint acceptance is not recovery readiness |
| get-user-culture | Authenticated profile culture from ApplicationInfo | Does not silently substitute the system default |
| list-packages | SysPackage name/UId/maintainer/version, filter and sort | Retains legacy 10,000-row limit; no paging completeness claim |
| list-apps | SysInstalledApp summaries | Does not include install, delete, download, or detailed application inspection |
| get-pkg-version | Read local descriptor PackageVersion | JSON result envelope replaces plain text |
| set-pkg-version | Normalize numeric/suffixed version, preserve unknown metadata and UId, advance ModifiedOnUtc | No concurrent cross-process editing guarantee |
| add-package-dependency | Resolve installed identities, read full properties, merge names/optional versions by UId, save complete descriptor | Structured receipt/save result; no automatic compilation or cross-process lost-update protection |
| remove-package-dependency | Remove names case-insensitively; skip persistence for an absent dependency | Requires readable package catalog; live validation boundary recorded separately |
| activate-pkg | Send a package name and interpret each package's activation result | Unlike legacy exit 0 after partial failure, returns a nonaccepted result with successful package receipts |
| deactivate-pkg | Resolve name to installed UId, then deactivate once | Name lookup is case-insensitive; acceptance is not application readiness |
| generate-pkg-zip | Stream selected package content to gzip or a multi-package ZIP, with PDB exclusion, hierarchical ignore rules and one-branch layout | `comp-pkg` alias conflict and legacy presentation remain unresolved; archive APIs now belong to runtime-owned PrimitiveContracts |
| extract-pkg-zip | Decode gzip, ZIP or directory batches; validate all packages before publication; explicit overwrite and completed receipts | Explicit overwrite replaces interactive prompts; Windows locked-destination partial publication is tested, crash recovery is not |
| show-package-file-content | List compiled package files or return one file as structured data through ClioGate | Preinstalled ClioGate 2.0.0.47+ required; legacy aliases, project-file enrichment and live Creatio validation remain pending; see [command and delivery proof](package-file-content.md) |

Service behavior comes from the legacy command implementations, ServiceUrlBuilder, DataServiceQuery, ApplicationPackageListProvider, InstalledApplicationQueryService, and CurrentUserCultureResolver at the pinned commit. Descriptor version/timestamp behavior comes from SetPackageVersionCommand and PackageDescriptor. The legacy Creatio `.gz` package format is a custom gzip record stream, not tar; archive operations must preserve that wire format when ported.

## Layer placement in this slice

- CLI parses aliases and conventional flags, then binds values using Composition's discovered schema. It does not authenticate, select primitives, read settings, or implement command policy.
- Composition's Services module owns request shape, route selection, envelope interpretation, timeout defaults, and file-input/output sequencing. Its Packages module owns descriptor edit policy.
- Core continues to resolve the environment and pin one compatible bundle per root. There are no new command names or primitive implementations in Core.
- Primitives alone invokes the async Creatio SDK and writes files. File publication stages beside the destination and then replaces it. Existing metadata/permissions are retained where supported. This is not crash durability or cross-process coordination.
- Feature APIs, including file-writer, HTTP request budgets and archives, now live in runtime-owned PrimitiveContracts. The stable host Contracts ABI is **10.4.0.0**, introduced by the one-time contract separation. Earlier prototype ABI 10.1/10.3 notes describe historical packaging, not current feature requirements. Complete runtimes carry their own typed feature contracts; Core does not reference them. See [the boundary decision](../primitive-contract-boundary.md).

No production NuGet dependency was added for this slice. Microsoft framework APIs implement JSON and file publication; Creatio.Client remains private to Primitives.

## Errors and partial results

Known service envelopes require `success:true`; `success:false`, HTML error pages, malformed envelopes, and explicit service errors fail even with HTTP 200. OData entity properties are data rather than BaseResponse flags. Generic call-service permits text, well-formed XML and empty responses. XML validation disables DTDs and external entity resolution; HTML documents remain rejected. Ping alone permits page/redirect responses after login and returns only a reachability flag; redirects are not followed by Composition. Other services reject them. HTTP failures include the status code without remote error prose. Extreme numeric values outside supported numeric ranges are preserved as text for portable results.

Writes are not automatically retried on a lost response: the outcome is unknown. A destination-write failure after accepted remote work returns `output-write-failed` and `AcceptedSteps=["service-call"]`. Rejected remote calls do not overwrite a previous destination. Cancellation cannot reverse an external action. The existing SDK can still renew authentication and replay an unauthorized request; this is not an exactly-once guarantee.

## Next feature groups

1. Package archives, installation/export, locking, workspace and file-system synchronization.
2. Environment registration/settings editing, deployment/infrastructure, database and container capabilities.
3. Application/schema/model/data operations and generation.
4. Remaining MCP-only surfaces, guidance/resources, Ring consumer compatibility and complete legacy argument/output parity.

Port each group through the same public boundaries. Package work must not pull Docker, database SDKs, CLI, or the old static container into Core or Composition. CI selection automation remains deferred as requested.

## Review and current validation

Claude review `rev_4fe726fc162b4f91` inspected the actual files and found no Blocker/P1 architectural or safety defect. Its XML-response and localized-descriptor findings were accepted and fixed, with regression tests. Query validation now rejects unsupported encoded backslashes before login; definite SDK authentication rejection retains its classification. A root destination is explicitly rejected as an argument error. The original null-parent path also threw ArgumentNullException (already caught by Composition), rather than the claimed NullReferenceException; the explicit check and test remove ambiguity.

New Unix output files intentionally remain owner-readable/writable (0600), because raw service output may be sensitive. Existing destination modes are preserved. Users sharing output across accounts must explicitly change permissions. Windows uses inherited permissions for new files and replacement metadata for existing files. No portable umask-reading or cross-process permission machinery was added.

The installed preview 13 passed all 197 then-current tests, including 56 product tests against the installed DLL. Five read-only live calls passed on the dedicated Clio10Architecture lab: ping reported HTTP reachability, profile culture resolved, catalogs returned 180 packages and 10 applications, and the persisted compilation report was readable. This is not live verification of build/generation/restore or all Creatio/OS variants. The later review corrections have additional focused tests; their final package evidence is recorded in validation.md in this directory.

KISS check: the application invokes a discovered workflow, Core supplies one scoped bundle, the workflow calls declared capabilities and returns data. No new scheduler, registry framework, process protocol, or production NuGet dependency was introduced. A file-writing capability was necessary for actual package metadata edits and call-service destinations; the HTTP timeout field preserves existing long-running command budgets.
