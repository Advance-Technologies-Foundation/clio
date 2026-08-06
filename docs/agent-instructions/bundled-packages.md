# Bundled Creatio packages — how to put a new version into clio

clio ships two Creatio packages inside its own distribution and installs them into an environment on
request. This article is the procedure for replacing one of those archives, and the platform facts you need
in order not to break it silently.

Read it before touching any of:

- `clio/CrtProcessBuilder/*.gz`, `clio/cliogate/*.gz` — the archives themselves
- `clio/Common/BundledPackages.cs` — the identity constants
- `clio.tests/Common/BundledProcessBuilderPackageTests.cs` — the pins
- a `[RequiresPackage]` version floor

## The two packages

| | `cliogate` | `CrtProcessBuilder` |
|---|---|---|
| Ships | a **prebuilt assembly** per framework (`Files/Bin`, `Files/Bin/netstandard`) | **source only** — no assembly at all |
| Who compiles | nobody; the DLL is loaded as-is | the TARGET environment, during installation (~15–75 s) |
| Archives | one per framework, chosen by `IsNetCore` | one, for every runtime |
| Install verb | `install-gate` | `install-process-builder` |
| Source repo | in this repo (`cliogate/`), regenerable via `build.ps1` | separate `ProcessBuilder` repo, produced by hand |
| Build procedure | see the ClioGate sections in `AGENTS.md` | this article |

The asymmetry matters for review: a changed `cliogate.gz` can be checked by rebuilding it from in-repo
sources, a changed `CrtProcessBuilder.gz` cannot. That is why the latter carries pins (below).

## Platform facts you must know first

Three separate decisions, often confused. Getting them mixed up is what makes a rebundle fail silently.

**1. "New package or existing?" — decided by `UId`.**
`PackageDBStorage.SavePackageDescriptor` fetches `SysPackage` by `UId`. Not found → new row. So NEVER change
a package's `UId` to force an update: you would install a second package alongside the first, and with two
assemblies declaring the same service `CustomServicesParser` silently keeps whichever it enumerates first.

**2. "Did the descriptor change?" — decided by `ModifiedOnUtc`, NOT by `PackageVersion`.**
`PackageStorageComposer.ApplySourcePackageChanges` compares the descriptor's `ModifiedOnUtc` (and the
repository revision) and sets `IsPackageDescriptorChanged`. Without that flag,
`SavePackageDescriptor` returns early at its `GetIsPackageDescriptorModified` guard and never reaches the
`SysPackage.Version` assignment. The chain on the zip-install path is:

```
PackageInstallerService.InstallPackage(zip)
  -> PackageZipInstaller.Install
      -> ISystemPackageManager.Add / .Save
          -> SystemPackageManager.ComposesPackages -> IPackageComposer (SimplePackageComposer)
              -> PackageStorageComposer.ApplySourcePackageChanges   <- the ModifiedOnUtc comparison
  -> PackageDBStorage.SavePackageDescriptor                          <- writes SysPackage.Version
```

So: **the date decides WHETHER the row is rewritten, the version decides WHAT lands in it.** A version
bumped alone installs cleanly and leaves the recorded version behind — which is what a `[RequiresPackage]`
floor reads, so the floor then refuses commands on an environment that was upgraded correctly. Verified on
two stands (net472 and .NET 8) on 2026-08-05, in both directions.

`clio set-pkg-version` writes both fields, so this cannot happen through the supported path. Only a hand
edit of `descriptor.json` can produce it.

**3. "Did it compile?" — not answerable from the database.**
For a source-only package, accepting the archive and compiling it are separate events. The recorded version
moves on ACCEPT; a failed configuration build leaves the last successfully built assembly serving. The
database therefore cannot distinguish "installed" from "working" — which is why
`install-process-builder` probes the service after installing rather than trusting the install call.

## Procedure — replacing `CrtProcessBuilder.gz`

### First decide whether to raise the floor

`BundledPackages.ProcessBuilderVersion` is the `[RequiresPackage]` floor. Raising it forces every
environment carrying an older package to be refused until it is upgraded.

- **Service contract changed** (new operation, changed response, clio starts sending something an older
  server cannot handle) → raise it. A refusal naming the fix beats an unexplained server error.
- **Internal change, same contract** (bug fix, refactor) → leave it. The new sources still reach whoever
  installs, without forcing everyone to upgrade. Only the SHA pin changes.

### In the `ProcessBuilder` repository

```powershell
# 1. The target will have to compile these sources - make sure they do.
dotnet build MainSolution.slnx -c dev-nf
dotnet test tests/CrtProcessBuilder/CrtProcessBuilder.Tests.csproj -c dev-nf

# 2. Bump with the COMMAND, never by editing descriptor.json - it writes PackageVersion AND stamps
#    ModifiedOnUtc, and fact 2 above is why both are needed. Run it on EVERY rebundle, not only when
#    raising the floor: pass the SAME version to re-stamp the date, which is what makes the target
#    rewrite the SysPackage row at all.
dotnet <clio>/clio/bin/Debug/net8.0/clio.dll set-pkg-version ./packages/CrtProcessBuilder `
  --PackageVersion X.Y.Z.W

# 2b. SCHEMA descriptors are NOT covered by that command - it stamps the package descriptor only. Check
#     every Schemas/*/descriptor.json for a plausible ModifiedOnUtc and correct it if not. This is not
#     hypothetical: the compile-marker schema shipped for a day carrying LOCAL time in a UTC-labelled
#     field (05:42:51Z for a file written at 08:46Z, i.e. exactly the +03:00 offset), because
#     PackageDescriptor.ClearMilliseconds dropped DateTime.Kind and ToUniversalTime then treated the
#     value as local. The producing bug is fixed, so a schema saved by a current clio is correct - but a
#     descriptor written before the fix keeps its wrong value, since nothing re-stamps it.

# 3. Delete the build output. clioignore does NOT filter Files/Bin (path patterns were tried and do not
#    match), so without this the archive stops being source-only and ships a host-specific assembly -
#    which installs, satisfies the gate, and then 404s on the other runtime.
Remove-Item packages/CrtProcessBuilder/Files/Bin -Recurse -Force

# 4. Pack straight into the clio checkout.
dotnet <clio>/clio/bin/Debug/net8.0/clio.dll compress ./packages/CrtProcessBuilder `
  -d <clio>/clio/CrtProcessBuilder/CrtProcessBuilder.gz

# 5. VERIFY the archive rather than trusting step 3 - its failure is silent.
dotnet <clio>/clio/bin/Debug/net8.0/clio.dll extract-pkg-zip `
  <clio>/clio/CrtProcessBuilder/CrtProcessBuilder.gz -d <tmp>
```

Step 5 must show: exactly **2** `.dll` files, both under `Files/Libs` (`ErrorOr`, `ATF.Repository` — real
dependencies absent from the platform core; never exclude them by file name, that halves the archive and
ships source that cannot compile), **no** `CrtProcessBuilder.dll` anywhere, and the
`Schemas/CrtProcessBuilderCompileMarker` folder present.

That marker schema is load-bearing and fails silently: it is the only thing that puts the package into the
target's configuration build. Lose it and the package installs, the gate reports it present, and every
`/rest/ProcessDesignService/*` call fails.

### In this repository, in ONE commit

| Update | Where |
|---|---|
| `ProcessBuilderVersion` (only if raising the floor) | `clio/Common/BundledPackages.cs` |
| `ExpectedArchiveSha256` | `clio.tests/Common/BundledProcessBuilderPackageTests.cs` |
| `ExpectedDescriptorModifiedOnUtc` | same file |

The date pin must end in `000`. `PackageDescriptor.ConvertToModifiedOnUtc` truncates to whole seconds, so
milliseconds in it prove the descriptor was written by something other than `set-pkg-version` — a test
asserts this, because the archive shipped for a while with a stamp ending in `431` while every doc told the
next person to use the command.

The three pins sit side by side deliberately: a rebundle touches all of them, so a hand edit that moved the
version without the date fails in clio's own suite instead of on a customer's environment. Name the
producing repository's commit in the commit message — the SHA pin is the only reviewability this artifact
has, since a `.gz` change renders in a diff as nothing but a byte count.

### Then verify

```powershell
# 6. REBUILD clio first. This is the trap: the command resolves the archive from
#    IWorkingDirectoriesProvider.ExecutingDirectory - the BUILD OUTPUT directory - while `compress -d`
#    wrote to the repo path. Skip this and you will test the previous archive and conclude the wrong thing.
dotnet build clio/clio.csproj -f net8.0

# 7. Tests.
dotnet test clio.tests/clio.tests.csproj -f net8.0 --filter "Category=Unit&(Module=Command|Module=McpServer|Module=Common)"

# 8. Live, on a stand.
dotnet clio/bin/Debug/net8.0/clio.dll install-process-builder -e <env>
dotnet clio/bin/Debug/net8.0/clio.dll list-packages -e <env>
```

If `list-packages` still shows the old version after a floor bump, `ModifiedOnUtc` did not move — step 2 was
done by hand instead of with the command.

Judge the result by outcome, never by the installer's dialog: the install call returning success only proves
the archive was accepted. `install-process-builder` already probes `ProcessDesignService` for you and fails
when it does not answer.

## Invariants for any bundled package

- **Identity lives in one place.** `BundledPackages` holds the name, version and archive file name; nothing
  repeats those literals. (The cliogate version is deliberately NOT there yet — it is still spread across a
  constant in `InfoCommand`, `cliogate/descriptor.json` and a stale `cliogate/version.txt`. Collapsing that
  triple is separate work; do not add a fourth copy.)
- **The remediation command must not be gated by what it fixes.** An install verb carries neither
  `[RequiresPackage]` for its own package (it would be refused by the requirement it exists to satisfy) nor
  `[FeatureToggle]` (a gated options type is filtered out of the verb parse array, and a gated MCP primitive
  is filtered out of registration — the fix would be unreachable exactly when needed). Both absences are
  pinned by tests.
- **Never change a package `UId`.** See fact 1.
- **A restart happens, and you do not request it.** On .NET Framework the platform recycles itself once the
  workspace assembly changes; on .NET `BasePackageInstaller` issues it because the target is a .NET host.
  Either way it outlives the install call, so wait for the instance to answer its health check before
  judging anything — probing sooner can be answered by the outgoing app domain still serving the old
  assembly.

## Known gaps

- **A failed configuration build may not be reported.** Whether the platform's install response says failure
  when the build fails is UNVERIFIED: the experiment did not run because a deliberately-broken archive was
  rejected earlier, at `AppInstallInfoResolver.ValidateInstallInfos`, before compilation.
- **clio's own log check is inert.** `BasePackageInstaller` consults the installation log only under
  `GlobalContext.FailOnError` (`--fail-on-error`), and then matches "application installed successfully" —
  a phrase package installs do not emit. So the check is either off or wrong.
- **The outcome check is per-package today.** `install-process-builder` probes `ListUserTasks`, which cannot
  tell WHICH build answered and needs `CanManageProcessDesign` (installing does not grant it). A
  package-agnostic replacement belongs in clio and would serve every bundled package: the installation log
  clio already receives, plus the `ConfActivityLog` `Compilation` record — a normal entity schema readable
  through DataService, carrying `Operation`, `Status` (Success/Error/Warning), `PackageName` and `CreatedOn`.

## See also

- `AGENTS.md` — ClioGate build/deploy sections, for the other bundled package.
- Confluence, [Putting a new version of a bundled package into clio](https://creatio.atlassian.net/wiki/spaces/TER/pages/4938858553)
  — the same procedure for human readers, under the delivery decision record. Keep the two in step.
- `ProcessBuilder` repo, `docs/bundling-into-clio.md` — the package-tree half of the procedure, maintained
  next to the sources.
- `spec/adr/adr-deliver-process-builder-package.md` — why source-only delivery was chosen and what it costs.
