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
| Who compiles | nobody; the DLL is loaded as-is | the TARGET environment, during installation — substantially slower than a plain package install, by an amount that is a property of the target (configuration size, host, load) and not of clio. Deliberately no figure: `AGENTS.md` sends agents here, and a range on an agent surface stops being an estimate — one was read out of the MCP tool description and repeated to a user as a promise |
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

Re-measured on 2026-08-06 (net472, `ts1-web01-15837616`), which added one property worth knowing: the
comparison is **"differs", not "is later"**. Installing an archive whose `ModifiedOnUtc` is EARLIER than the
recorded one still rewrites the row — the recorded version went `1.1.0.2` -> `1.1.0.1` and the install
reported success. So `SysPackage.Version` is not monotonic and carries no guarantee of being the highest
version ever installed; it is simply whatever the last descriptor with a different timestamp said. Do not
build a "has it been upgraded" check on the assumption that it only ever moves forward. The same run
re-confirmed the silent half: an archive declaring `1.1.0.3` with an unchanged timestamp installed with
exit 0 and left `1.1.0.2` recorded.

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

# 2. Move BOTH fields - PackageVersion and ModifiedOnUtc - on EVERY rebundle, not only when raising
#    the floor: fact 2 above is why the date is what makes the target rewrite the SysPackage row at
#    all, so pass the SAME version to re-stamp the date if the version is not changing.
#
#    The command is the way to do that in one step, and it is what the pins expect (it clears the
#    milliseconds, giving the `000` suffix the guard fixture uses as a provenance oracle). It is NOT
#    a technical requirement, and the earlier wording here ("never by editing descriptor.json") was
#    too strong: a hand edit that moves both fields works, because the platform's comparison is
#    "differs", not "is later" - measured, see fact 2. What breaks is moving the version alone.
dotnet <clio>/clio/bin/Debug/net8.0/clio.dll set-pkg-version ./packages/CrtProcessBuilder `
  --package-version X.Y.Z.W

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
- **The outcome check is per-package, and it is LIVENESS only.** `install-process-builder` asks the package's
  own ungated `Ping` whether it is serving, and fails unless it answers. That decides "did the target build it"
  on a FIRST install — with no assembly there is no type, no route, nothing to answer. It does NOT decide an
  UPGRADE: a stale assembly still answers. Reporting the version back would need a hand-maintained copy of it
  in the shipped sources, because the assembly version belongs to the platform and `descriptor.json` never
  reaches the target's build directory (both measured — see the ADR). What the check also does not establish is
  whether the CALLER may use the package; that surfaces at the caller's next call, from the guard's own
  message. A package-agnostic
  alternative would read the installation log clio already receives plus the `ConfActivityLog` `Compilation`
  record (a normal entity schema readable through DataService, carrying `Operation`, `Status`, `PackageName`,
  `CreatedOn`) — useful for a package that exposes no service of its own, and untested so far because it is
  unknown whether the platform reports a FAILED configuration build at all.
- **This is NOT a gap in `install-gate`, and copying it there would be symmetry for its own sake.**
  `install-gate` verifies neither half — it returns success once the archive is accepted, without even waiting
  for the restart it triggers — and that has been fine for years, because cliogate ships a PREBUILT assembly:
  there is no target-side compile to fail, so the state this verification exists to catch cannot arise. The
  feedback loop that covers the remaining case already works: `Program.CheckApiVersion` runs on essentially
  every environment-touching command and tells the user to run `install-gate` when the gate is absent. Adding
  a readiness wait to a command every user runs would turn some currently-passing installs into failures for
  a hypothetical benefit.

## See also

- `AGENTS.md` — ClioGate build/deploy sections, for the other bundled package.
- Confluence, [Putting a new version of a bundled package into clio](https://creatio.atlassian.net/wiki/spaces/TER/pages/4938858553)
  — the same procedure for human readers, under the delivery decision record. Keep the two in step.
- `ProcessBuilder` repo, `docs/bundling-into-clio.md` — the package-tree half of the procedure, maintained
  next to the sources.
- `spec/adr/adr-deliver-process-builder-package.md` — why source-only delivery was chosen and what it costs.
