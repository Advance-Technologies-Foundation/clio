# Validation - previous preview 4 evidence

Historical record. Current API, test counts and review decisions are in [design-convergence.md](design-convergence.md).

## Current revision

Working tree: `C:\Projects\clio-10-prototype`, branch `prototype/clio-10-layering`. HEAD remains the legacy baseline; the new solution is uncommitted. The primary Clio checkout is unchanged by this work.

74 source tests passed: 31 Composition, 19 Core, 9 primitive transport, 15 product. The final Release solution run passed 70 tests across all four projects. Four subsequent test-only additions (encoded/absolute traversal and invalid child input) passed in targeted Composition and Primitives runs. No compilation errors or warnings remain.

```powershell
dotnet test Clio10.slnx -c Release --nologo --verbosity quiet
dotnet test tests/Clio10.Composition.Tests/Clio10.Composition.Tests.csproj -c Release --nologo --verbosity quiet
dotnet pack Clio10.slnx -c Release -o artifacts/packages -p:Version=10.0.0-preview.4
dotnet tool install clio --version 10.0.0-preview.4 --tool-path artifacts/tool-preview4 --configfile scripts/local-packages.config --add-source artifacts/packages
```

The tool and reusable libraries packed successfully. Five shipping-product CLI/MCP tests also pass against the installed package's Clio10.dll. The CLIO10_TEST_PRODUCT test override expects the managed DLL, not clio.exe; passing the shim initially caused five harness failures, corrected by selecting the installed DLL. The shim itself performed the live checks below. Ten partner/argument adapter tests run a separate test-only PartnerHost, because the production tool does not bundle the example partner.

## Architectural evidence

- Contracts-only partner assembly: no Core, Composition, Primitives, Creatio SDK or MCP dependency.
- Real files read through a typed filesystem capability; managed caller receives a TextSummary record.
- Unchanged generic CLI and MCP adapters discover/execute a separately compiled partner workflow in --local mode, without URL or credentials.
- Nested workflow calls receive independent replacement arguments and share one pinned session. Omitted child input does not inherit parent input.
- Deep snapshotting protects running input against caller mutation. Invalid schemas, excessive depth, unsupported values and non-finite numbers fail before Core session creation.
- Decimal JSON precision, typed numeric arrays, duplicate nested JSON keys and empty file-path failure are tested.
- V1 HTTP-only and V2 HTTP+filesystem bundles have the same assembly name in separate load contexts. Capability/version selection rejects incompatible exact requests.
- Actual loopback SDK authentication and HTTP failure paths; initial successful login is reused within a session.
- Queued cancellation leaves the original gate owner intact. Async disposal finishes before releasing ownership.

## Live Creatio

Dedicated instance: Clio10Architecture, Creatio 10.1.585/.NET8/PostgreSQL. Provisioned previously through the clio-creatio-lab skill at Instance scope; ignored receipt under artifacts/lab-receipt.json. Other issue environments were not touched.

Preview 4's installed launcher executed flush-redis and restart through CLI -> Composition -> Core -> async Creatio SDK. Both returned HTTP acceptance with `success:true`. Earlier validation also established authenticated get-info afterward and exact local V1/V2 execution; those earlier observations are not a new readiness check after the latest restart.

These checks prove integration on this stand. They do not prove every Redis key vanished, that process replacement completed, all OS/framework variants work, or MCP reconnection behavior. Live calls used CLI; real MCP wiring is tested against loopback targets. The lab remains for follow-up.

## Review

Seven local reviewer lenses found no Blocker/High in the revised implementation. Accepted advisory findings were corrected: targetless adapters, numeric conversion, snapshot array handling, malformed MCP input, invalid file paths, diagnostic snapshots and queued cancellation coverage. No extra layer or coordination framework was added.

Claude's prior review rev_13802082333f4870 identified the four structural limitations addressed here. Follow-up review rev_a1e0b75001b54cf4 completed after reading the actual files: no Blocker/P1/P2, and all four structural concerns closed. Its P3 observations and our assessment are recorded in extensibility-review-corrections.md. Claude did not run tests. It read some files before the final documentation and lifetime-test updates, so its reported count of 67 is an earlier snapshot. Earlier review records in architecture-review-2026-09-05.md are historical.

## Limits

CI redesign, NuGet publication, automatic acquisition, hot replacement, plugin-folder discovery and cross-process coordination remain deferred. All roots for a target are serialized inside one Core. Partners and local bundles are trusted code, not sandboxed. Fixture loading does not prove conflicting private/native dependencies. Payload serializability is the workflow author's contract. Generic MCP execute is conservatively destructive; it replaces experimental resident tools and does not prove live agent tool-catalog refresh. No Clio 8 public contract changed.
