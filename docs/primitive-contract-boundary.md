# Runtime-owned primitive contracts

## Why the boundary changed

Porting archives required successive host Contracts ABIs even though Core never invoked archive methods. That made ordinary feature growth require a host upgrade. The host should understand invocation and lifetime, while Composition and Primitives agree on feature APIs inside their complete runtime release.

`Clio10.Contracts` now contains the stable host boundary: invocation, metadata, results, execution context, capability identities, bundle factories and lifetime. `Clio10.PrimitiveContracts` contains HTTP, filesystem, file publication and archive interfaces plus their DTOs. Composition, Primitives and partner compositions use it. Core has no reference to it. There are no additional NuGet dependencies or independently updated capability packages.

## Loading and compatibility

Complete-runtime loading shares only the host Contracts assembly. Each runtime resolves its own feature contracts and implementations in its own load context. Core carries opaque capability objects and checks declared capability identity; the runtime performs the typed cast using its own interface identity. Existing exact host-ABI rejection remains in place. Managed dependencies declared in the release dependency manifest must be present before publication or activation; unresolved private assemblies cannot fall back to copies shipped by the host. Only explicit shared contracts and installed core framework assemblies may bind outside the release.

Moving public types out of the former combined assembly is itself a breaking relocation, so this blueprint uses host ABI **10.4.0.0**. Earlier packaged ABIs remain immutable and need one product upgrade to enter this design. Future feature API changes do not advance the host ABI. Changing actual host services still does.

Static embedding and the earlier primitive-only update mode have an intentionally different boundary: the caller contains the workflows and therefore needs matching typed feature interfaces. Composition explicitly supplies `CoreOptions.SharedCapabilityAssemblies`; Core validates their assembly versions and shares those exact assemblies only in static mode. Complete-runtime mode ignores that list. A custom static host explicitly supplies any additional typed capability assemblies it consumes. It cannot gain a changed compile-time API through a primitive-only update.

This is assembly/version isolation for trusted code, not a plugin security boundary. Composition and Primitives update together. Partners joining a runtime must target that runtime's feature API. No dependency solver, reflection-based command protocol or per-capability update service is introduced.

## Proof

`FeatureContractUpdateTests.New_and_changed_capability_interfaces_update_without_host_restart` starts the shipping MCP product and a NuGet V3 loopback feed:

1. V1 starts a workflow and is held in an external call.
2. The feed delivers V2, which introduces `Clio10.FeatureFixture` and `IReleaseFile`. Neither the installed product nor V1 contains this assembly.
3. V2's new workflow executes actual asynchronous file write/read through that interface. V1 finishes using its original workflow and children.
4. The feed delivers V3. The same-named feature assembly changes from 2.0.0.0 to 3.0.0.0, removes `WriteReadAsync` and replaces it with `PublishAsync` using new input/result types.
5. V3 executes the new signature successfully through the same MCP connection and PID. The product executable hash remains unchanged.
6. A fresh process with the feed stopped executes V3 from the persistent cache.

The fixture feature library is referenced only by its runtime fixtures, never by the product or Core. A separate architecture test checks that Core and host Contracts do not reference the production primitive-contract assembly.

Run:

```powershell
dotnet test tests/Clio10.Product.Tests/Clio10.Product.Tests.csproj -c Release --filter FullyQualifiedName~New_and_changed_capability
```

Initial source proof passed on Windows, MCP PID 34908; log: `artifacts/feature-evolution-test.log`. This proves runtime capability evolution, not host replacement, independent partner hot loading, agent-initiated rediscovery or automatic unloading. No live Creatio environment was modified.

KISS check: the existing loader, dispatcher, scopes and updater remain. The change relocates feature types into one runtime-owned contract library and preserves explicit static embedding compatibility.


## Current package verification

Final source regression: **299 tests passed**. The locally installed preview 20 passed five product checks, including real runtime 10.8.0.0 archive operations and the V1/V2/V3 fixture update on one MCP PID (22052). See `artifacts/feature-contract-regression-final.log` and `artifacts/feature-contract-installed-proof.log`. This proves both source and installed delivery paths. The older preview packages and their contract assemblies were not replaced.

## Independent review

Claude review `rev_ecaaeab2299445fb` found no P1/high defects in the contract split. Its P2 proof gap was accepted: deleting a dependency listed in deps.json only exercised preflight, not the loader. The additional test removes both the private PrimitiveContracts DLL and its runtime asset declaration. The loader rejects use of host feature types. Temporarily replacing the non-borrow guard with `return null` made that exact case fail; restoring it made all four focused isolation/evolution checks pass. Logs: `artifacts/feature-contract-loader-mutation.log`, `artifacts/feature-contract-loader-restored.log`.

The full solution run above remains 299 passed; the subsequently added loader case passed separately. No production changes followed that full regression (the temporary mutation was restored byte-for-byte). Claude did not rerun tests or review the follow-up test correction.

The continuity evidence is the same live MCP client and open pipes plus a still-running process. PID/hash values are supporting identity diagnostics, not sufficient proof alone. Framework-dependent managed hosting is the supported packaging path; single-file/NativeAOT hosting remains unproven. In particular the loader's framework-directory discovery needs a separate design check for single-file hosts; no support claim is made here.

## Subsequent real feature delivery

The [package-file-content port](porting/package-file-content.md) now proves a production runtime 10.8-to-10.9 update using the unchanged installed preview 20 host. A previously absent command becomes discoverable and executes through the same MCP client and pipes, and remains usable from cache with the feed stopped. This complements the earlier new/breaking feature-interface fixtures: ordinary new behavior can reuse existing primitives without inventing a new API. The latest full regression passed 312 tests, with subsequent targeted additions recorded in that port's validation section.
