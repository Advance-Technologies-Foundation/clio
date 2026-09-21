# Automatic update proof

> Historical primitive-only proof. [The complete runtime proof](runtime-update-proof.md) extends this to Composition workflows.

## Scope and result

This proof updates the dynamically loaded **primitive bundle**, not the Composition, Core or adapter assemblies. Those are still part of the running application. Porting remains on hold while the update requirement is settled; this evidence must not be presented as whole-application hot replacement.

The same shipping Clio MCP process and the same SDK client connection execute V1 and then V2. A held V1 HTTP operation completes on V1 even though the background updater has downloaded V2. A later root loads the new DLL. After intentional termination, a fresh process selects V2 from the same cache with the feed offline.

A run using an actual `dotnet pack` artifact recorded:

- Original MCP PID **43184**: in-flight result **10.0.0.0**, later result **10.1.0.0**.
- Fresh MCP PID **31720**: offline result **10.1.0.0**.
- Inspectable cache: `C:\Users\k.krylov\AppData\Local\Temp\clio10-update-b58d1d0c-dc38-4850-967c-0d75ad9537d4`.

The test intentionally terminates the first process only after proving the switch. Its initial attempt waited for the stream-based test host to exit automatically; that harness expectation timed out after the successful live switch. Explicit process ownership now makes persistence testing deterministic.

## Mechanics

1. The MCP Generic Host starts one optional background service. No extra updater process or OS job is needed for this proof.
2. It reads the NuGet V3 service index, discovers PackageBaseAddress, and checks the package version list on a timer.
3. It selects stable numeric versions within the host's explicit range, also respecting a configured exact pin.
4. A package is downloaded into a unique hidden staging directory under the configured cache. Download, expansion and entry counts are bounded. Archive traversal is rejected.
5. The updater validates package identity, bundle manifest/version/ABI, assembly version and presence of dependency output. It does not execute code from staging or claim behavioral validation.
6. A directory move publishes the complete bundle. Existing version directories are immutable. Duplicate publishers may download twice; only one directory move wins.
7. Core rescans visible manifests at root selection. Existing contexts retain their selected objects and version; newly admitted roots choose the newest compatible installed bundle.
8. The on-disk bundle directories are the persistent selection source. There is no active-version marker to race or restore after restart.

Each complete check, including streamed response bodies, has a 30-second deadline. HTTP redirects are disabled; the configured source and discovered package address must serve their resources directly. A failed candidate is retried on the next interval (one hour by default), so a corrected feed can recover without restarting. Transient activation I/O failures are retried on later roots.

Activation failures still use the catalog's existing fallback behavior. A failure after command execution starts is not automatically retried or rolled back: effects may already have occurred.

## Reproduce

```powershell
dotnet test tests/Clio10.Product.Tests/Clio10.Product.Tests.csproj -c Release --filter FullyQualifiedName~AutomaticUpdateTests --logger 'console;verbosity=normal'

# Create an ordinary NuGet package containing complete prebuilt bundle output:
./scripts/pack-primitive-bundle.ps1 -BundleDirectory ./artifacts/bundles/10.1.0.0 -Version 10.1.0
$env:CLIO10_TEST_UPDATE_PACKAGE = (Resolve-Path ./artifacts/runtime-packages/Clio10.PrimitiveBundle.10.1.0.nupkg).Path
try {
    dotnet test tests/Clio10.Product.Tests/Clio10.Product.Tests.csproj -c Release --no-build --filter FullyQualifiedName~Update_keeps_process_and_pipe_alive_and_persists --logger 'console;verbosity=normal'
} finally { Remove-Item Env:CLIO10_TEST_UPDATE_PACKAGE }
```

The test hosts a controlled NuGet V3 HTTP source and a controlled Creatio HTTP endpoint. It uses real bundle DLLs and real MCP pipes, not mocks of Core. It publishes no package to NuGet.org and changes no live Creatio environment.

Eight negative cases exercise corrupt packages, incompatible ABI, archive traversal, interrupted downloads, mismatched package identity/version, duplicate archive entries and download size limits. All retain the working V1 and running MCP process and verify staging cleanup. Two positive cases verify the ordinary update and recovery after a stalled response body. All ten pass. The stalled-download run retained MCP PID 48056 while moving from 10.0.0.0 to 10.1.0.0; a fresh offline PID 41768 retained 10.1.0.0. The standard source regression suite is also run after the Core changes.

## Host configuration

Updates are off by default. Opt in for the MCP server with:

- `CLIO10_BUNDLES`: persistent cache directory, initially containing a complete working bundle directory.
- `CLIO10_UPDATE_SOURCE`: trusted NuGet V3 index URL.
- `CLIO10_UPDATE_PACKAGE`: bundle package identity, for example Clio10.PrimitiveBundle.
- `CLIO10_UPDATE_INTERVAL_SECONDS`: defaults to 3600; the proof uses 0.1.
- Optional `CLIO10_PRIMITIVE_VERSION`: exact pin; an old pin deliberately prevents advancing.

The current adapter configures the 10.x release range. A custom Generic Host can supply BundleUpdateOptions in CoreOptions. The short-lived CLI consumes the cache but does not own a polling service. Update requests use a separate HTTP client; Creatio credentials are not sent to the feed.

## Deliberate limits

- No dynamic Composition discovery or replacement. New workflows are not supplied by this bundle format.
- No package signing/trust policy, private-feed authentication, prerelease selection, general NuGet dependency restoration or NuGet.org publication. Use a trusted source; HTTPS is required except loopback test endpoints.
- The package must contain complete dependency output under bundle/. A normal library nupkg is not sufficient. This payload package supplements the ordinary reusable library package.
- Old loaded assemblies are retained until process shutdown. Automatic unloading and cache cleanup remain future work; repeated updates consume additional memory/disk.
- No cross-process command coordination, atomic multi-environment workflow, exactly-once execution or settings migration is claimed.
- The test proves continuity with an MCP SDK client. It does not test Codex/Claude UI tool-refresh behavior; the two resident tool definitions remain unchanged.

Protocol references: [NuGet service index](https://learn.microsoft.com/en-us/nuget/api/service-index) and [package content resource](https://learn.microsoft.com/en-us/nuget/api/package-base-address-resource).

## Packaged verification

The locally installed clio.10.0.0-preview.9.nupkg passed all ten automatic-update cases using the actual packed primitive payload. Its stalled-download recovery run kept PID 16596 alive through V1/V2 execution; fresh offline PID 22220 selected V2. This is local packaging evidence, not a published release.

Regression validation: Composition 41, Core 28, Primitives 9, Product 35 cases passed (113 total). The initial combined run was interrupted while awaiting slow SDK process shutdown; the remaining 25 product regressions were rerun separately and all passed. The ten update cases also passed against the installed preview.
