# Migration validation

Worktree: `C:\Projects\clio-10-prototype`, branch `prototype/clio-10-layering`. Legacy behavior reference: `11293657cb326cb3b488c891ca9e973b5aea8c14`. These files are uncommitted prototype work. No NuGet publication, PR, or merge was performed; the primary checkout's existing edits were preserved.

## Package and runtime proof

The tool and reusable libraries pack as `10.0.0-preview.14`. The tool is installed separately at `artifacts/tool-preview14`; the user's global Clio installation is untouched.

```powershell
dotnet pack Clio10.slnx -c Release -p:Version=10.0.0-preview.14 -o artifacts/packages
dotnet tool install clio --version 10.0.0-preview.14 --tool-path artifacts/tool-preview14 --configfile scripts/local-packages.config --add-source artifacts/packages
```

A separately published **actual vendor runtime**, version 10.2.0.0, loads into that installed host and discovers all 14 operations. It executed live ping and list-packages, and a local set-pkg-version/get-pkg-version round trip. All four results identified runtime 10.2.0.0. This complements the V1/V2 fixture tests: the new release includes the real ported workflows and file-writing capability. `dotnet publish` now copies bundle.json as well as the dependency output.

```powershell
dotnet publish src/Runtime/Clio10.Runtime.csproj -c Release -p:Version=10.2.0 --artifacts-path artifacts/runtime-release-build/10.2.0 -o artifacts/published-runtime-check/10.2.0
./scripts/pack-primitive-bundle.ps1 -BundleDirectory artifacts/published-runtime-check/10.2.0 -Version 10.2.0 -PackageId Clio10.RuntimeBundle
```

The complete payload is `artifacts/runtime-packages/Clio10.RuntimeBundle.10.2.0.nupkg`. Its Contracts assembly remains ABI 10.1.0.0 despite the new runtime version. Earlier ABI 10.0.0.0 hosts require a host upgrade; these changes do not make host replacement automatic. Same-ABI update/pinning/persistence tests continue to exercise real running MCP processes and pipes. An MCP SDK client is not proof of a particular agent application's tool-catalog refresh.

## Live evidence

Dedicated target: Clio10Architecture, retained from the earlier architecture lab. No other issue environment was used. Only read-only service calls were made in this migration slice.

| Call | Observed result |
|---|---|
| ping-app | Login accepted; root returned a redirect; reported `http-reachable` without readiness claims |
| get-user-culture | Authenticated profile culture returned |
| list-packages | 180 package records returned |
| list-apps | 10 installed application records returned |
| last-compilation-log | Persisted compilation report returned |
| call-service GET odata/$metadata | Accepted XML response, 1,463,256 characters, through installed preview 14 |

No remote build/generation/restore was triggered. Loopback and controlled-response tests cover their routes and semantics. Live .NET Framework, Linux/macOS file behavior, and exhaustive legacy argument/output parity remain unverified. Package metadata tests use real local files, not mocks.

## Automated results

Final preview 14 run: **205 passed, zero failed** â€” Composition 98, Core 35, Primitives 15, Product 57. Product tests used the installed package's managed DLL via `CLIO10_TEST_PRODUCT`; test-only partner hosts remain separate fixtures. Product coverage includes real stdio discovery/execution, conventional CLI options and package paths, failed updates, same-process runtime activation, pinned in-flight work and offline persistence.

```powershell
$env:CLIO10_TEST_PRODUCT = (Resolve-Path artifacts/tool-preview14/.store/clio/10.0.0-preview.14/clio/10.0.0-preview.14/tools/net10.0/any/Clio10.dll).Path
dotnet test Clio10.slnx -c Release
Remove-Item Env:CLIO10_TEST_PRODUCT
```

Local execution log: `artifacts/porting-preview14-regression.log`. Package/publish checks and the live OData XML check are additional to these automated tests. A first packaging attempt overlapped running product tests and encountered Windows file locks; rerunning packaging after those processes exited succeeded. Publishing and verification now use separate output directories where simultaneous access is needed.

## Review assessment

Claude `rev_4fe726fc162b4f91` read the actual implementation and found no Blocker/P1. The accepted XML, readable metadata, and error-classification findings were fixed and regression-tested. Private permissions for newly created Unix output files remain an intentional documented choice. The later publish-manifest correction was validated by publishing and executing the actual runtime; it was not part of Claude's original read.

See README.md in this directory for the accepted/rejected finding details and scope limits. This proves the first migration slice, not the remaining Clio 8 features.

## Package services: preview 15

The next slice adds dependency editing and package activation/deactivation: 18 operations are now discoverable. Tool and library packages packed as `10.0.0-preview.15`; the tool is installed at `artifacts/tool-preview15`. No global tool update or publication occurred.

Validation performed:

- Full solution regression: **227 passed** (Composition 116, Core 35, Primitives 15, Product 61), recorded in `artifacts/porting-package-regression.log`.
- Final Composition run after adding malformed-activation receipt, missing-target and Framework-activation coverage: **121 passed**.
- Installed preview 15: all **17** then-current `ServiceProductTests` passed. Two more CLI/MCP deactivation cases were added afterward; all **4** cases in `Package_dependency_adapters` passed against the installed tool. These runs are overlapping coverage, not additive full-suite counts.
- Actual Creatio `Clio10Architecture`: removing a randomly named absent dependency from `Custom` returned `changedCount:0`, retained `CrtCore`, and did not save. Remote package mutations and activation were not exercised live.

Actual vendor runtime **10.3.0.0** was published and packed as `artifacts/runtime-packages/Clio10.RuntimeBundle.10.3.0.nupkg`, keeping Contracts ABI 10.1.0.0. The previously installed **preview 14 host** loaded it and discovered all 18 operations. More strongly, `scripts/verify-package-runtime.ps1` started that old host with vendor runtime 10.2.0.0, staged 10.3.0.0, then observed 14 → 18 operations and invoked the new dependency workflow through the **same MCP connection and PID**. The host executable hash stayed unchanged. The live no-op result reported runtime 10.3.0.0.

Receipt: `artifacts/package-runtime-proof-8719b0abf9a340a2a73ae337e37bb132/proof.json`. This proof stages already published bundles; HTTP/NuGet acquisition, interrupted updates, pinning and offline persistence remain covered by `AutomaticUpdateTests`. It does not claim a particular agent application's resident tool catalog refresh or automatic host replacement.

```powershell
dotnet pack Clio10.slnx -c Release -p:Version=10.0.0-preview.15 -o artifacts/packages -m:1
dotnet tool install clio --version 10.0.0-preview.15 --tool-path artifacts/tool-preview15 --configfile scripts/local-packages.config --add-source artifacts/packages
dotnet publish src/Runtime/Clio10.Runtime.csproj -c Release -p:Version=10.3.0 --artifacts-path artifacts/runtime-release-build/10.3.0 -o artifacts/published-runtime-check/10.3.0 -m:1
./scripts/pack-primitive-bundle.ps1 -BundleDirectory artifacts/published-runtime-check/10.3.0 -Version 10.3.0 -PackageId Clio10.RuntimeBundle
```

Claude review `rev_206509f2923b4f01` found no blocking or P1/P2 defects. Its coverage finding was fixed; generic rejected-save diagnostics remain an explicit presentation parity gap, described in [package services](package-services.md). The review did not include the later standalone runtime-proof script or the two added deactivation adapter cases; those were executed locally.

## Single-package archives: preview 17

Twenty operations are available in locally installed preview 17. The two archive operations support Creatio gzip records, vendor content selection, one-branch layouts, workspace/package/nested ignore files, optional PDB exclusion, and explicit replacement after complete validation. ZIP and directory batches remain unimplemented; this is not full archive or Clio 8 parity.

The archive capability uses Contracts ABI 10.2.0.0. Preview 15 and its ABI 10.1.0.0 runtimes are retained unchanged. A host upgrade is required for this new shared API; no cross-ABI live update is claimed. Complete runtime 10.5.0.0 contains the same twenty operations and the Ignore parser dependency.

Validation:

- Full solution before review corrections: 265 passed (127 Composition, 35 Core, 38 Primitives, 65 Product), including automatic update/pinning/persistence tests. Log: `artifacts/archive-layer-tests.log`.
- After review corrections: all 40 Primitives and 129 Composition tests pass; the two archive product tests pass against the rebuilt source product.
- The two archive product tests also pass against installed preview 17 in both modes: bundled implementation and dynamically loaded actual runtime 10.5.0.0. Logs: `artifacts/archive-final-installed-tests.log` and `artifacts/archive-final-runtime-tests.log`. These tests exercise CLI aliases and multiple operations on one MCP connection, including refused and explicit overwrite.
- Pack and local tool install pass; tool: `artifacts/packages/clio.10.0.0-preview.17.nupkg`; complete runtime: `artifacts/runtime-packages/Clio10.RuntimeBundle.10.5.0.nupkg`. No external publication or live Creatio mutation was performed for archives.

```powershell
dotnet pack Clio10.slnx -c Release -p:Version=10.0.0-preview.17 -o artifacts/packages -m:1
dotnet tool install clio --version 10.0.0-preview.17 --tool-path artifacts/tool-preview17 --configfile scripts/local-packages.config --add-source artifacts/packages
dotnet publish src/Runtime/Clio10.Runtime.csproj -c Release -p:Version=10.5.0 --artifacts-path artifacts/runtime-release-build/10.5.0 -o artifacts/published-runtime-check/10.5.0 -m:1
./scripts/pack-primitive-bundle.ps1 -BundleDirectory artifacts/published-runtime-check/10.5.0 -Version 10.5.0 -PackageId Clio10.RuntimeBundle
$env:CLIO10_TEST_PRODUCT = (Resolve-Path artifacts/tool-preview17/.store/clio/10.0.0-preview.17/clio/10.0.0-preview.17/tools/net10.0/any/Clio10.dll).Path
dotnet test tests/Clio10.Product.Tests/Clio10.Product.Tests.csproj -c Release --no-build --filter FullyQualifiedName~ArchiveProductTests
$env:CLIO10_RUNTIME_COMPOSITION = 'true'
$env:CLIO10_BUNDLES = (Resolve-Path artifacts/published-runtime-check).Path
$env:CLIO10_PRIMITIVE_VERSION = '10.5.0.0'
dotnet test tests/Clio10.Product.Tests/Clio10.Product.Tests.csproj -c Release --no-build --filter FullyQualifiedName~ArchiveProductTests
Remove-Item Env:CLIO10_TEST_PRODUCT, Env:CLIO10_RUNTIME_COMPOSITION, Env:CLIO10_BUNDLES, Env:CLIO10_PRIMITIVE_VERSION
```

Claude `rev_8470141003374af1` found no P1 blocker. The P2 rejection of trusted ancestor links was corrected: parents are resolved before staging, while links inside selected package content and at publication targets remain forbidden. A real directory-link test passes on Windows. The P3 missing-input classification and ignore precedence/overwrite test gaps were also fixed. Claude did not run tests or re-review these local corrections. Mac/Linux execution and rename-failure fault injection remain unverified.

The KISS check remains straightforward: adapters bind arguments, Composition selects files and interprets results, Primitives streams and publishes. Core remains unaware of package commands. The only new parser dependency implements actual legacy ignore requirements; no updater, routing protocol or coordination layer was added. MCP reviewed: the stable discovery/execute bridge needs no new resident tools, and both archive workflows are covered through real stdio.

## ZIP and directory batches: preview 19

The same two archive operations now support `compress --packages First,Second`, ZIP extraction, and directory batches. CLI hidden legacy option spellings and default working-directory extraction are covered. Composition chooses package names and selection; the existing archive primitive handles bounded container I/O and publication. No new package dependency or Core command registry was added.

Batch API additions advance Contracts to ABI 10.3.0.0. Local preview 19 and complete runtime 10.7.0.0 use that ABI; earlier published-on-disk versions remain unchanged. No public NuGet release or live Creatio action occurred.

Evidence:

- The initial full solution run passed 285 cases and failed two rejected-update cases during Windows temporary-cache deletion, after the operation assertions had passed. Optional cleanup now catches access-denied file locks as well as I/O locks. All nine rejected-update cases pass on rerun (`artifacts/archive-batch-update-recheck.log`). The initial full log is `artifacts/archive-batch-tests.log`; it is not represented as a wholly green run.
- Final focused runs pass all 58 primitive tests and 136 Composition tests. Coverage includes an independently produced gzip/ZIP format, cumulative limits, malformed size declarations, large metadata, destination classification and per-package ignore isolation.
- Four installed-product archive cases pass for CLI/MCP and single/batch inputs (`artifacts/archive-batch-final-installed-tests.log`). Four more executions of those cases pass with runtime 10.7.0.0 explicitly selected (`artifacts/archive-batch-final-runtime-tests.log`); assertions verify the returned runtime version, preventing a silent host fallback from counting as proof.
- A real Windows lock on the second package causes a later publication failure. The first package remains published with its receipt, while the second package retains its original data. Both adapters preserve `AcceptedSteps` and `failedDestination`. This does not prove crash recovery or failure during the subsequent restoration rename.
- Tool: `artifacts/packages/clio.10.0.0-preview.19.nupkg`. Runtime: `artifacts/runtime-packages/Clio10.RuntimeBundle.10.7.0.nupkg`. Pack, local install and separate runtime publish pass.

```powershell
dotnet pack Clio10.slnx -c Release -p:Version=10.0.0-preview.19 -o artifacts/packages -m:1
dotnet tool install clio --version 10.0.0-preview.19 --tool-path artifacts/tool-preview19 --configfile scripts/local-packages.config --add-source artifacts/packages
dotnet publish src/Runtime/Clio10.Runtime.csproj -c Release -p:Version=10.7.0 --artifacts-path artifacts/runtime-release-build/10.7.0 -o artifacts/published-runtime-check/10.7.0 -m:1
./scripts/pack-primitive-bundle.ps1 -BundleDirectory artifacts/published-runtime-check/10.7.0 -Version 10.7.0 -PackageId Clio10.RuntimeBundle
$env:CLIO10_TEST_PRODUCT = (Resolve-Path artifacts/tool-preview19/.store/clio/10.0.0-preview.19/clio/10.0.0-preview.19/tools/net10.0/any/Clio10.dll).Path
$env:CLIO10_RUNTIME_COMPOSITION = 'true'
$env:CLIO10_BUNDLES = (Resolve-Path artifacts/published-runtime-check).Path
$env:CLIO10_PRIMITIVE_VERSION = '10.7.0.0'
dotnet test tests/Clio10.Product.Tests/Clio10.Product.Tests.csproj -c Release --no-build --filter FullyQualifiedName~ArchiveProductTests
Remove-Item Env:CLIO10_TEST_PRODUCT, Env:CLIO10_RUNTIME_COMPOSITION, Env:CLIO10_BUNDLES, Env:CLIO10_PRIMITIVE_VERSION
```

Claude review `rev_a8cb4e37b0384573` found no P1/P2 defects. Its P3 destination classification was corrected, and filename restrictions were clarified. The additional tests above address its ordinary publication-failure, declared-length, directory-byte-budget and metadata-budget proof gaps. Its remaining uncertainty about platform list allocation was checked against the .NET 10 source linked in [archive semantics](package-archives.md). Claude did not run tests or re-review the local follow-up corrections.

KISS check: this adds ZIP framing around the existing gzip codec and returns completed receipts from sequential publication. There is no new scheduler, transaction coordinator or loader. The small metadata-budget stream prevents a concrete allocation problem that a post-parse entry-count check cannot prevent. Generic MCP discovery/execute remain unchanged and are exercised by the adapter tests. The full Clio 8 parity goal remains open, including the legacy alias collision and presentation differences recorded in the ledger.

## Primitive contract boundary validation

Feature interfaces now ship in runtime-owned `Clio10.PrimitiveContracts`; resident Contracts ABI is 10.4.0.0 after the one-time relocation. See [the design and proof](../primitive-contract-boundary.md).

- Final solution regression: **299 passed** (Composition 136, Core 36, Primitives 58, Product 69). Command: `dotnet test Clio10.slnx -c Release -m:1 --logger 'console;verbosity=normal'`. Log: `artifacts/feature-contract-regression-final.log`.
- Installed tool: `artifacts/packages/clio.10.0.0-preview.20.nupkg`, installed only under `artifacts/tool-preview20`. All seven reusable library packages, including PrimitiveContracts, packed successfully.
- Separate actual runtime: `artifacts/published-runtime-check/10.8.0`, payload `artifacts/runtime-packages/Clio10.RuntimeBundle.10.8.0.nupkg`.
- **Five installed-product checks passed**: gzip/ZIP CLI/MCP round trips on actual runtime 10.8.0.0, and the new-capability/breaking-capability update proof against fixture releases. Log: `artifacts/feature-contract-installed-proof.log`.
- Same installed MCP PID **22052** loaded V2's previously unknown capability and V3's incompatible method/DTO contract, performed actual filesystem I/O, preserved the active V1 call, and retained V3 on a fresh offline launch. Executable SHA256 stayed `004EE30D2396FBA21E191698F266CCD68017E6FFF3E8B2CF27F81252A18F63AA`.
- Missing Composition or feature-contract DLLs fail before execution and fall back to a healthy older release; no default-host feature fallback is permitted.
- No live Creatio environment was modified. Primary checkout changes remain the original Directory.Packages.props and EnvironmentSettingsTests.cs edits.

MCP reviewed: stable discovery/execute tool definitions remain unchanged and the real stdio SDK path is tested. This is not a claim that an agent independently refreshes resident tool definitions. Clio 8 and Ring-consumed contracts remain untouched.

Claude review `rev_ecaaeab2299445fb` found no high-severity defect. Its loader-isolation coverage finding was addressed with an undeclared-missing-dependency test and a mutation check: permitting default-context borrowing fails that test, restoring the loader passes it. Four focused loader/evolution checks passed after restoration. This adds one case beyond the 299-test full run without further production changes. Single-file hosting remains unproven; continuity rests on the same live MCP connection, not merely PID/hash diagnostics.
# Runtime 10.9 follow-up

The installed preview 20 host acquired runtime 10.9 from a NuGet loopback feed while serving runtime 10.8. It discovered and executed the newly ported `show-package-file-content` workflow without restarting its MCP connection, then reused the cached runtime in a fresh offline process. See [the command and proof](package-file-content.md), including reproduction commands and Claude's review. Full solution regression: 312 passed; two additional composition cases, two normal MCP cases and the explicit installed-release proof passed separately. Core, host Contracts and adapters did not change for this port. The ledger now has 21 implemented-partial CLI entries and 218 pending entries; no full-parity claim is made.
