# Bundled Creatio packages — how to put a new version into clio

clio ships two Creatio packages inside its own distribution and installs them into an environment on
request. This article is the procedure for replacing one of those archives, and the platform facts you need
in order not to break it silently.

Read it before touching any of:

- `clio/CrtProcessBuilder/*.gz`, `clio/cliogate/*.gz` — the archives themselves
- `clio/Common/BundledPackages.cs` — the identity constants
- `clio/Common/BundledPackageCatalog.cs` — the reader that answers what the distribution carries
- `clio/Common/BundledPackageConvergence.cs` — the rule that decides an environment is behind
- `clio.tests/Common/BundledProcessBuilderPackageTests.cs` — the pins
- a `[RequiresPackage]` version literal

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
bumped alone installs cleanly and leaves the recorded version behind — which is the version clio compares
against, so an environment upgraded correctly still looks out of date. Verified on two stands (net472 and
.NET 8) on 2026-08-05, in both directions.

The mirror case is the one that catches people out, because nothing about it looks wrong: **date moved,
version unchanged.** The row IS rewritten — with the same version. The sources land, the target compiles
them, everything works. But clio decides whether to ask an environment to update by comparing versions, so
every environment that already has the package compares as converged and is never asked. The rebundle
reaches new installs only, silently. This is why the version must move on every rebundle, and why
`rebundle-process-builder.ps1` refuses to run without a higher one.

Re-measured on 2026-08-06 on an internal net472 stand, which added one property worth knowing: the
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

### The version moves on every rebundle

There is no "decide whether to raise it" step any more, and no floor constant to raise. clio reads the
shipped version out of the archive itself (`IBundledPackageCatalog`) and compares it against what the
environment recorded; that comparison is the entire delivery mechanism. So:

- **raising it is the only way a change reaches an existing environment** — see fact 2 for what happens
  when it does not. This is what the rule protects, so it also bounds the rule: **before the FIRST release**,
  while the version exists only on the delivering branch and no environment carries it, there is nothing to
  propagate to and re-cutting under the same version is correct — only `ModifiedOnUtc` has to move. The
  script still refuses; do that one by hand (the manual steps below) and record why in the commit message. A
  flag for it would be a permanent hole bought for a one-off;
- **lowering it now REFUSES**, it does not merely fail to propagate: `install-process-builder` reads the
  environment's recorded version and will not move it backwards without `--force`. So a rebundle that
  lowers the version, or an older clio pointed at a stand that already carries a newer package, is turned
  away on every such environment;
- **raising it costs nothing to maintain.** Nothing on the clio side has to be kept in step with it. That
  used to be false: the version was also the `[RequiresPackage]` floor, so raising it forced a refusal on
  every environment until upgraded, which is why the old guidance reserved it for contract changes. Both
  the floor and that reason are gone.

An explicit `[RequiresPackage("CrtProcessBuilder", "X.Y.Z.W")]` literal is a separate and much rarer thing:
add one in the commit where a command starts calling an operation an older server does not have. It states
what the CODE needs, not what environments should converge to. A guard test asserts the shipped archive
satisfies every such literal — class-level and property-level — so clio can never demand a version it does
not itself carry.

**A server-side change with a SECURITY character needs a literal, always.** A new permission check, a
tightened validator, a fixed authorization hole: those must be a `[RequiresPackage]` version, never left to
convergence. Convergence warns and proceeds when it cannot read the archive, and it is a delivery policy
rather than a gate — only the literal fails closed.

### Two branches, two restamps — the second merger RE-CUTS, it does not pick a version

When two branches each rebundle, each carries its own `descriptor.json` bump, and if neither branch is a
descendant of the other's restamp those bumps are independent. Merging them conflicts on that file.

**Resolve it by re-cutting from the merged tree. Never by choosing one side's version.**

This conflict is worth its own rule because of a property the others here do not have: **both resolutions
look correct.** Take the other branch's line and the archive holds one branch's bytes under a number that
promises both; take your own and you get the same thing mirrored. Nothing downstream disagrees with you —
the pins are refreshed FROM the archive you just produced, so they are self-consistent whatever is in it,
and `ExpectedProducingCommit` names a commit that genuinely was HEAD when the bytes were packed. Every
test passes, the provenance is honest about the commit, and the version is still a claim about content
that nobody made. Compare the failure modes above, where at least one resolution is visibly worse.

The rule that prevents it is the same one the whole section rests on, applied to the merge: **a version
moves because the CONTENT changed, not because circumstances did.** A stand that has moved ahead, a peer
who took the next number, a rebase that changed nothing in the sources — none of those is a reason to cut
a new number, and none is a reason to reuse one either. Re-cutting from the merged tree is what makes the
number mean "these bytes" again, and it is cheap: the script does the whole thing in one call.

A corollary, learned the expensive way: **claim the number before you cut, not after.** Two archives were
produced under `1.4.0.9` on one day by two branches that fork off each other, and the number is burned —
a gap in the sequence is always cheaper than a number that names two different sets of bytes. `1.4.0.12`
and `.14` are skipped for the same reason.

And do not read a stand's installed version as the sequence's high-water mark. It records what someone
last installed, which may be a branch that never merges. The sequence is owned by the branches, not by
the environment.

### One call — `rebundle-process-builder.ps1`

The whole procedure, from the repository root:

```powershell
pwsh ./rebundle-process-builder.ps1 -PackageRepoPath <ProcessBuilder checkout> -Version 1.0.1.0
```

`-Version` is required and must be HIGHER than the version currently in the descriptor. The script refuses
before touching anything otherwise — an equal version publishes to nobody, and a lower one is worse:
`SysPackage.Version` is not monotonic (fact 2), so it would pin environments below what clio ships.

Add `-Configuration` / `-Framework` when more than one clio build output exists. The script refuses to
guess, because whichever it picks is the one that receives the new archive; the others keep the previous
one and an install run from them ships it. It names them all at the end.

What it does beyond running the steps below:

- computes the pins **from the archive it just produced**, so "the pins are stale" stops being a
  reachable state;
- reads the archive back and checks the inventory — exactly two DLLs and both from `Files/Libs`, the compile
  marker present, the package's own assembly absent, and nothing outside the allowed top-level set (in
  particular no `SqlScripts/` or `Data/`, which the target EXECUTES at install time). The guard fixture now
  pins all of that too, so a bad rebundle fails CI as well; the value of having it here is that the script
  stops you BEFORE the archive and its pins are committed, and before step 6 rewrites the SHA pin from the
  very archive that is wrong;
- verifies `ModifiedOnUtc` actually MOVED, rather than merely being present;
- rebuilds clio, and reports every other build output that now holds a different archive or none.

It deliberately does NOT commit. Step 8 — committing both repositories and naming the producing commit
in the clio message — is a judgement call and stays with you.

### Without the script

The script requires `pwsh`. The steps below are what it runs, and they are the fallback on a host without
PowerShell — the same arrangement `AGENTS.md` uses for `cliogate`'s `build.ps1`. Read them anyway: they
carry the REASONS, and a script that fails is only useful to someone who knows what each step protects.

> **`X.Y.Z.W` means four plain numbers — no `-rc`, no `-dev`, no suffix of any kind.** The script cannot emit
> one (`[version]::TryParse` rejects it); by hand you can, so the rule is enforced twice more downstream:
> `InstallProcessBuilderCommand` REFUSES to install a distribution whose bundled version carries a suffix,
> and the `ExpectedArchiveVersion` pin in `BundledProcessBuilderPackageTests` refuses to let one be
> committed. `BundledPackageConvergence` neither refuses nor compares it — it warns that the distribution
> cannot be compared and allows the call through.
>
> That split is deliberate and both halves were arrived at by getting it wrong first, so do not "simplify" it:
>
> - **The reader must not refuse.** Enforcing the rule in `IBundledPackageCatalog.TryGetVersion` was tried and
>   reverted. A refusal there comes back as `false`, which already means "I could not read it", and the install
>   command answers that by installing anyway — so the downgrade guard went blind and a suffixed distribution
>   could roll a shared environment back from any version, undetected.
> - **Convergence must not refuse either.** That was tried too. `PackageVersion` ranks an empty suffix BELOW a
>   non-empty one (GA < rc), so a bundled `1.0.1.0-rc` makes an environment recording the GA `1.0.1.0` — and
>   every lower version — read as behind. Convergence refused every gated call and named the install as the
>   remedy; the install refused the same distribution as malformed; and `--force` is unavailable over MCP. The
>   whole process-designer surface dead, with no in-band way out, over a defect in clio.
>
> A suffix on the version an ENVIRONMENT records is fine and is simply ignored.

### In the `ProcessBuilder` repository

```powershell
# 1. The target will have to compile these sources - make sure they do.
dotnet build MainSolution.slnx -c dev-nf
dotnet test tests/CrtProcessBuilder/CrtProcessBuilder.Tests.csproj -c dev-nf

# 2. Move BOTH fields - PackageVersion and ModifiedOnUtc - and move the version UP. Fact 2 is why:
#    the date is what makes the target rewrite the SysPackage row at all, and the version is what
#    clio compares to decide whether an environment needs updating. Move only the date and the
#    change reaches nobody who already has the package; move only the version and it installs
#    without being recorded.
#
#    The command is the way to do that in one step, and it is what the pins expect (it clears the
#    milliseconds, giving the `000` suffix the guard fixture uses as a provenance oracle). It is NOT
#    a technical requirement, and the earlier wording here ("never by editing descriptor.json") was
#    too strong: a hand edit that moves both fields works, because the platform's comparison is
#    "differs", not "is later" - measured, see fact 2.
dotnet <clio>/clio/bin/Debug/net8.0/clio.dll set-pkg-version ./packages/CrtProcessBuilder `
  --package-version X.Y.Z.W

# 2b. SCHEMA descriptors are NOT covered by that command - it stamps the package descriptor only. Check
#     every Schemas/*/descriptor.json for a plausible ModifiedOnUtc and correct it if not. This is not
#     hypothetical: the compile-marker schema shipped for a day carrying LOCAL time in a UTC-labelled
#     field (05:42:51Z for a file written at 08:46Z, i.e. exactly the +03:00 offset), because
#     PackageDescriptor.ClearMilliseconds dropped DateTime.Kind and ToUniversalTime then treated the
#     value as local. The producing bug is fixed, so a schema saved by a current clio is correct - but a
#     descriptor written before the fix keeps its wrong value, since nothing re-stamps it.

# 2c. COMMIT the restamped descriptor.json in THIS repository, in the same change as the rebundle. Not
#     housekeeping: the clio-side SHA-256 pin names a commit of this repo as where the archive can be
#     reproduced from, and step 2 rewrote descriptor.json in your checkout. Leave it uncommitted and the
#     named commit still carries the OLD stamp - so anyone following the reference rebuilds a different
#     archive, gets a different hash, and the guard test fails on a branch nobody touched. This has already
#     happened once: the archive shipped /Date(1786345127000)/ while the referenced commit said
#     /Date(1786075660000)/, and it was found in review rather than by any check.
git -C <ProcessBuilder> add packages/CrtProcessBuilder/descriptor.json
git -C <ProcessBuilder> commit -m "<ticket> rebundle to X.Y.Z.W"

# 3. Delete the build output. clioignore does NOT filter Files/Bin (path patterns were tried and do not
#    match), so without this the archive stops being source-only and ships a host-specific assembly -
#    which installs, satisfies the gate, and then 404s on the other runtime.
Remove-Item packages/CrtProcessBuilder/Files/Bin -Recurse -Force

# 4. Pack straight into the clio checkout. --skip-pdb matches what the script passes: today step 3 has
#    already removed the only .pdb there is, so the flag changes nothing about the output - but the archive
#    is pinned BYTE-FOR-BYTE by SHA-256, and the two paths have to produce the same bytes for that pin to
#    mean anything. Any .pdb that ever appears outside Files/Bin would otherwise make them diverge.
dotnet <clio>/clio/bin/Debug/net8.0/clio.dll compress ./packages/CrtProcessBuilder --skip-pdb `
  -d <clio>/clio/CrtProcessBuilder/CrtProcessBuilder.gz

# 5. VERIFY the archive rather than trusting step 3 - its failure is silent.
dotnet <clio>/clio/bin/Debug/net8.0/clio.dll extract-pkg-zip `
  <clio>/clio/CrtProcessBuilder/CrtProcessBuilder.gz -d <tmp>
```

Step 5 must show: exactly **2** `.dll` files, both under `Files/Libs` (`ErrorOr`, `ATF.Repository` — real
dependencies absent from the platform core; never exclude them by file name, that halves the archive and
ships source that cannot compile), **no** `CrtProcessBuilder.dll` anywhere, the
`Schemas/CrtProcessBuilderCompileMarker` folder present **and no other schema folder**, and **nothing outside
`descriptor.json` / `Files` / `Schemas` / `Resources`** — in particular no `SqlScripts/` and no `Data/`, which
the target EXECUTES at install time (this install passes no `PackageInstallOptions`, so the platform's own
defaults apply). The clio-side guard fixture pins all of that as well, so a bad archive fails CI whichever
path produced it; checking here is what stops you before the archive and its pins are committed.

That marker schema is load-bearing and fails silently: it is the only thing that puts the package into the
target's configuration build. Lose it and the package installs, the gate reports it present, and every
`/rest/ProcessDesignService/*` call fails.

### In this repository, in ONE commit

| Update | Where |
|---|---|
| `ExpectedArchiveSha256` | `clio.tests/Common/BundledProcessBuilderPackageTests.cs` |
| `ExpectedDescriptorModifiedOnUtc` | same file |
| `ExpectedArchiveVersion` | same file |

**No PRODUCTION constant to update** — that is the point of the current design: clio reads the shipped
version from the archive, so nothing in the product can fall out of step with it. `ExpectedArchiveVersion`
is a test-side pin with no runtime consumer, and it exists for the same reason the SHA pin does — a `.gz`
renders in a diff as a changed byte count, so without that line a reviewer cannot see whether the version
moved. The script writes all three from the archive it produced.

The date pin must end in `000`. `PackageDescriptor.ConvertToModifiedOnUtc` truncates to whole seconds, so
milliseconds in it prove the descriptor was written by something other than `set-pkg-version` — a test
asserts this, because the archive shipped for a while with a stamp ending in `431` while every doc told the
next person to use the command.

Name the producing repository's commit in the commit message — the SHA pin is the only reviewability this
artifact has, since a `.gz` change renders in a diff as nothing but a byte count.

### Then verify

```powershell
# 6. REBUILD clio first. This is the trap: the command resolves the archive from
#    IWorkingDirectoriesProvider.ExecutingDirectory - the BUILD OUTPUT directory - while `compress -d`
#    wrote to the repo path. Skip this and you will test the previous archive and conclude the wrong thing.
#
#    RESTART any long-running `clio mcp` afterwards. It caches the archive's version for the life of the
#    process, so it keeps reporting - and comparing against - the version it read at startup.
dotnet build clio/clio.csproj -f net8.0

# 7. Tests.
dotnet test clio.tests/clio.tests.csproj -f net8.0 --filter "Category=Unit&(Module=Command|Module=McpServer|Module=Common)"

# 8. Live, on a stand.
dotnet clio/bin/Debug/net8.0/clio.dll install-process-builder -e <env>
dotnet clio/bin/Debug/net8.0/clio.dll list-packages -e <env>
```

`list-packages` must show the NEW version. If it still shows the old one, `ModifiedOnUtc` did not move —
step 2 was done by hand instead of with the command — and the environment will keep being told it is behind
on every gated call, because that recorded version is exactly what the convergence rule compares.

`clio info` must also show the new version on the `process-builder` line. It reads the archive through the
same catalog the install and the convergence rule use, so if it still prints the old one, the rebuild in
step 6 did not happen or landed in a different build output.

Judge the result by outcome, never by the installer's dialog: the install call returning success only proves
the archive was accepted. `install-process-builder` already probes `ProcessDesignService` for you and fails
when it does not answer.

## Invariants for any bundled package

- **Identity lives in one place, and the version lives in another.** `BundledPackages` holds the name and
  archive file name; `IBundledPackageCatalog` reads the version out of the archive. Do not add a version
  constant back — the archive is a content file that can be replaced without recompiling clio, so a
  constant describes bytes it may no longer be shipping. Three build outputs held three different archives
  under one constant while this was being built; see
  `spec/adr/adr-bundled-package-version-source-of-truth.md`. cliogate is deliberately not in the catalog
  yet; before touching any of its several version-shaped values, read the remarks on
  `clio/Common/BundledPackages.cs` — that is the single place the analysis lives, and a duplicate of it
  elsewhere had already drifted into being wrong.
- **What the code requires and what environments converge to are different statements.**
  `[RequiresPackage]` is the first: written by whoever writes the calling code, in their own commit, and it
  refuses. Convergence is the second: derived from the archive, applied to any package clio ships, and it
  also refuses — but with a different message, because the reader's reason for acting differs even though
  the remedy does not. Neither belongs inside the other.
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
