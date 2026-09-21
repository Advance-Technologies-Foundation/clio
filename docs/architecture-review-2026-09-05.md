# Clio 10 architecture review — 5 September 2026

> Historical pre-hardening review. The implementation has since addressed the workflow registry, nested context, SDK, environment and bundle-selection gaps. See [current validation and final review](validation.md) and [current architecture](architecture.md). CI remains deferred by request; this report below preserves the original findings, not the current completion status.

## Verdict

**Conditional go for the architecture; not yet ready to scale contributions or publish this working snapshot.** Keep the product, adapters, composition, Core and primitives separation. Correct the extension and context boundaries before expanding the two-command proof into dozens of capabilities. Another layer is not needed.

The goal is a clean foundation for 50–100 human/agent contributors, selective testing, and future partner compositions. Multiple primitive versions and process-local coordination are required. Cross-process coordination and automatic updates are excluded. Partners are trusted code, not sandboxed tenants.

## Scope and evidence

Reviewed current working files in `C:\Projects\clio-10-prototype`, branch `prototype/clio-10-layering`. HEAD `11293657cb326cb3b488c891ca9e973b5aea8c14` is the old baseline, not this implementation. The large legacy deletion is deliberate and was not treated as a regression review. Implementation was paused for this review; stale docs and CI references are identified separately from structural findings.

Seven independent local perspectives covered maintainability, security, performance, testing, correctness, intent and KISS. Claude independently inspected this directory using Read/Grep/Glob in Collab review `rev_a6b111a20b59453c`, model `claude-fable-5-1`. Its response cited actual source and fixture details. Claude did not execute tests.

Codex reran `dotnet test Clio10.slnx -c Release --nologo`: **22 passed** (11 composition, 2 loopback integration, 4 Core, 5 product). Product tests run the built executable through CLI and real stdio MCP. No live Creatio instance or cloud CI was exercised. Local verification is Windows only. No implementation changes were made during the review.

## Findings

### High: the workflow surface is closed, so ordinary feature additions change shared contracts

Evidence: `src/Contracts/Contracts.cs:16` defines a two-value operation enum; `src/Composition/CreatioComposition.cs:32` has a central switch; `src/Composition/CompositionRegistration.cs:26` registers one composition implementation. CLI and MCP also carry operation mappings.

This is acceptable for the two-command prototype, but is a growth decision to resolve before broad contributions. An independently developed operation cannot be added through this invocation contract without changing vendor Contracts. That creates merge contention and turns ordinary workflow additions into broad shared-contract test runs.

Smallest correction: capability-owned workflow classes with stable operation identifiers and local registration. Keep command-specific request/result definitions with their capability where practical. A small operation registry is sufficient; no mediator or general plugin platform is needed. Surface-specific mappings can remain in adapters, but should not require one central file for every command. Prove a partner operation can register alongside vendor operations without editing their enum or dispatcher.

### High for workflow reuse: nested composition needs one context owner

Evidence: `src/Composition/CreatioComposition.cs:14` unconditionally opens a context; `src/Core/ClioCore.cs:47` waits on a non-reentrant target semaphore.

An outer deployment holding a context and awaiting vendor restart on the same target waits on its own gate. Releasing the context between substeps avoids that wait but loses whole-workflow version pinning and coordination. Current standalone operations do not trigger this problem.

Smallest correction: the root invocation owns the operation context; reusable child steps accept that context explicitly. Child capability requirements must be satisfied by the already pinned bundle or fail before that step. Do not introduce ambient context or reentrant lock machinery. Test a partner workflow that performs custom work, calls a vendor operation, and continues under the same context.

### High for multi-version reliability: unrelated broken candidates can block a working version

Evidence: `src/Core/PrimitiveCatalog.cs:23-34` loads and constructs every recursively found bundle before compatibility filtering at `:38-43`. Any activation error aborts the whole catalog.

A missing dependency or incompatible future bundle can stop an explicitly selected healthy release from running. Constructor execution also precedes the advertised compatibility checks. Loading every installed version adds unnecessary startup work.

Smallest correction: discover lightweight candidate metadata, select compatible candidates, then activate the selected bundle. Retain explicit diagnostics for invalid candidates and ambiguous matching identities. Add a valid-plus-broken bundle test. Compatibility declarations establish API expectations, not trust: local DLLs still execute with process privileges.

### High delivery defect: the CI packaging path is obsolete

Evidence: `.github/workflows/validate.yml:21` packs the absent `src/Client/Clio10.Cli.csproj`; the product is `src/Clio10/Clio10.csproj`.

CI cannot complete packaging as written. This is unfinished wiring after the product split, not evidence against the architecture. Pack the actual product and exercise CLI plus MCP against the installed artifact. The current all-platform CI has not been run remotely.

### Medium: capability compatibility is currently HTTP-specific

Evidence: `src/Core/CoreOptions.cs:10` carries versions only; `PrimitiveCatalog.cs:39` hardcodes the HTTP capability; `src/Contracts/Bundles.cs` exposes one HTTP-shaped primitive.

Filesystem/database workflows cannot declare their needs without changing the central selector and session surface. Before those capabilities land, add explicit required capabilities and decide a small typed capability-access contract. Core should check capabilities generically while retaining ownership of the factory ABI compatibility rule. Do not solve arbitrary dependency graphs.

### Medium: Core's expected failures escape the structured result

Evidence: `ClioCore.cs:44` and `PrimitiveCatalog.cs:43` throw for unknown environments and missing compatible bundles. `src/Cli/CliAdapter.cs` catches cancellation only.

Expected configuration/selection failures can become an unhandled exception instead of the documented result/exit contract. Introduce a narrow typed resolution failure and translate it consistently at the application boundary; do not catch every exception as a business failure. Test unavailable versions through both adapters.

### Medium: a test-only shortcut bypasses session lifetime

Evidence: `ClioCore.cs:28,33,49-50` accepts a raw primitive and bypasses `OpenSession`. The singleton Core can return the same replacement object for every target while reporting the selected bundle's version.

Replace that shortcut with substitutes for the existing bundle/session factory. Tests and real execution should use the same lifecycle. This also prevents users copying the override example into a host and accidentally sharing session state between environments.

### Medium: the application-path guard is bypassable

Evidence: `src/Primitives/HttpPrimitive.cs:16-24` rejects literal `..`, but .NET resolves `%2e%2e/outside` against `https://example.com/app/` as `https://example.com/outside`. Codex reproduced that URI resolution locally.

Current commands use fixed paths, so this concerns the reusable primitive surface. Resolve first, validate normalized origin and application-path containment, then send the validated URI. Test encoded traversal, valid nested paths and application-root handling. PrimitiveRequest's generated diagnostic formatting also exposes its body, including login credentials; redaction should be the default before logging middleware is added.

## Team scalability

The project references enforce useful coarse ownership:

```text
Clio10 product -> CLI / MCP -> Composition -> Core -> Primitives
                                      shared Contracts
```

Within those layers, capability ownership is still needed. Use deployment/package/environment directories and corresponding workflow/test registrations. Do not create a project per command or require every contributor to modify Core.

Automate the few critical boundary rules. The current test rejects selected assembly references, but does not prevent filesystem/process/environment access inside workflow code. Extend checks to the intended project and API dependencies without bringing back a large analyzer framework. Keep documentation aligned with actual ownership; current AGENTS/README/CONTRIBUTING lag Core.

Core currently holds a startup environment snapshot. It does not yet implement persisted settings resolution, registration or synchronization. Public composition setup offers one `default` target. Centralize that configuration path before contributors duplicate it. The implementation is raw asynchronous HttpClient, not the existing Creatio client's async interface requested during discussion; that remains a deviation to resolve before claiming parity.

## CI and test selection

Layer boundaries make selection possible, but do not implement it. The present workflow runs the entire suite on three OSes for push and pull request. Keep the cheap suite broad while it is small. Target expensive external integration first, using changed capabilities and reverse dependencies. Unknown paths should fall back conservatively.

| Changed area | Minimum checks | Additional coverage |
|---|---|---|
| Documentation only | Documentation checks | Build/install if executable instructions or package metadata change |
| One composition capability | Its behavior cases and boundary checks | Matching loopback and CLI/MCP contract cases |
| Primitive capability | Direct primitive tests and dependent workflows | Loader/session tests where relevant; supported OS and affected real-environment cases |
| Core | Core and affected composition cases | Loopback/product tests; OS matrix for loading, paths or lifetime |
| Shared Contracts | All cheap tests and consumer builds | Loading, protocol and package compatibility |
| CLI adapter | CLI parsing, output and failure cases | Installed CLI round trip |
| MCP adapter | Tool schema, failure and cancellation cases | Real MCP round trip against installed product |
| Product routing/build/dependencies/CI | Entire small suite | Both installed CLI and MCP surfaces; package validation |

The current "Primitives.Tests" project references Composition and tests the whole workflow. Keep those valuable tests but identify them as integration tests; add direct primitive tests so their owner can localize failures. Current project-level selection still runs all workflows in the Composition project. Capability-level mapping is the later refinement needed for a large team.

Do not claim a removed project reference eliminates the need for consumer integration tests. Do not infer from mocked composition tests that every real Creatio configuration behaves identically. Run real environment tests only when explicitly authorized, and retain release-level integration evidence.

## Claude assessment: accepted and qualified

Claude's verdict was **conditional go**. Accepted: central operation edits obstruct team growth, compatibility/session APIs are HTTP-specific, expected errors lack a consistent boundary, direct primitive tests are missing, and current packaging/docs are unfinished. Its real-source citations confirm access to the implementation.

Qualified: Claude rated Core's concrete default-bundle dependency as a primary violation. It is a coupling trade-off, not a proven contradiction of the latest Core-owned-defaults requirement; its cited AGENTS rule is stale. Injecting built-in bundle factories instead of hardwiring HttpPrimitiveBundle would improve reuse, but should not force clients to assemble their own defaults or add another package gratuitously.

Qualified: Claude's matrix would skip some loader tests after removing a reference and grouped product changes with CLI-only checks. Test impact follows behavior too; product routing changes require both surfaces, and primitive compatibility changes may still require Core integration checks.

Qualified: tools do not need a fresh CreatioTools instance per call to isolate scopes; its method creates a scope each time and uses no mutable per-call instance state. Core-disposal-during-active-work is a contract/lifecycle test to add, not a demonstrated MCP shutdown defect. Runtime unloading and incompatible private dependency versions have not been proved.

## Small next milestone

1. Register one external operation beside vendor operations without changing vendor Contracts.
2. Run an outer workflow plus reused vendor step under one context, proving no self-wait and stable selection.
3. Execute both loaded versions against loopback with distinguishing behavior and genuinely different private dependencies; reject wrong contracts/missing capabilities and survive an unrelated invalid candidate.
4. Fix product packing and exercise both adapters from the installed package; map Core resolution failures consistently.
5. Update ownership documentation and add focused dependency checks and an honest test-impact table.

Keep the layers. Resolve these seams before inviting broad parallel implementation. Do not add distributed coordination, a general plugin platform, or an updater as part of this review.
