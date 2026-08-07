# ADR: Deliver the process-builder package inside clio as source, compiled by the target

- **Status:** Accepted
- **Date:** 2026-08-05
- **Jira:** ENG-94385
- **Feature:** `deliver-process-builder-package`
- **Supersedes (in part):** the delivery section of [adr-ENG-90883-backend-process-designer.md](adr-ENG-90883-backend-process-designer.md)
- **Detail:** [deliver-process-builder-package-plan.md](../deliver-process-builder-package/deliver-process-builder-package-plan.md)
  carries the full experiment log, cost ledger and the four dead hypotheses. This ADR records only the
  decision and why the alternatives lose.

## Context

The process-designer capability (`create-business-process`, `modify-business-process`,
`describe-business-process`, `list-user-tasks`, `validate-process-graph`) calls
`/rest/ProcessDesignService/*`. That service is implemented in a Creatio package that exists on no
stand by default, so every one of those commands fails on a fresh environment with an error that does
not say what to do about it.

`cliogate` already solves the same shape of problem: a privileged backend package that clio itself
ships and installs on request, with `[RequiresPackage]` on the commands that need it. The question is
not whether to copy that pattern — it is what the shipped artifact should BE.

The constraint that makes this non-trivial: the package contains C# that compiles against
`Terrasoft.*`, and the two supported hosts are .NET Framework 4.8 and .NET 8. A compiled assembly is
therefore host-specific, while the `[RequiresPackage]` gate can only check a package NAME and VERSION
in `SysPackage`.

## Decision

**Ship the package as SOURCE ONLY — no compiled assembly in the archive — and let the target
environment compile it during installation.**

Concretely:

1. `clio/CrtProcessBuilder/CrtProcessBuilder.gz` is committed to the repo and copied to the output
   directory by the csproj. It is produced by hand; there is no build step in the release path.
2. The archive contains one otherwise-empty Source Code schema, `CrtProcessBuilderCompileMarker`,
   whose only job is to put the package into the target's configuration build.
3. On install the server regenerates the package csproj (`GenerateStandalonePackageFiles`), sets
   `NetStandardCompatibilityMode = True`, chooses the `TargetFramework` for its own host, and produces
   the DLL on the stand.
4. `clio install-process-builder` (MCP tool `install-process-builder`) installs it, waits out the
   restart, and then verifies the OUTCOME rather than the install call.
5. The five consuming commands carry
   `[RequiresPackage(BundledPackages.ProcessBuilderPackageName, Hint = "…")]`,
   so a missing package produces a refusal that names the fix, and a separate convergence rule refuses a
   stale one.

`clio/Common/BundledPackages.cs` is the single source of truth for the name and archive filename. Nothing
repeats those literals.

> **Superseded in part.** As first built this ADR also put the shipped VERSION in that class and used it as
> the `[RequiresPackage]` floor, so points 5 and the paragraph above read differently. That arrangement
> conflated three separate concepts and was replaced; see
> [adr-bundled-package-version-source-of-truth.md](adr-bundled-package-version-source-of-truth.md). The
> rest of this ADR — source-only delivery, the compile marker, the outcome check — stands.

## Alternatives considered

### A. Compiled assembly, one archive per framework

Ship `Files/Bin/*.dll` (net472) and `Files/Bin/netstandard/*.dll`, as `cliogate` does, and branch on
`EnvironmentSettings.IsNetCore` to pick an archive.

Rejected. It introduces a failure mode that is **silent and indistinguishable from success at the gate
level**: ship or select the wrong framework flavour and the package installs, `SysPackage` reports it
present, `[RequiresPackage]` is satisfied, and every `/rest/ProcessDesignService/*` call 404s. Detecting
that requires exactly the outcome probe the chosen option needs anyway — so the compiled path costs the
probe PLUS a release-time build against two cores PLUS the version-coupling between the shipped assembly
and the target's `Terrasoft.*`. The source-only path makes the failure structurally impossible instead of
detectable.

### B. Build the package in CI and attach it to the release

Rejected for this ticket by scope, not on merits: the ticket explicitly asks for the hand-added variant.
Note it does not remove the problem in A — CI still has to build against two cores and still ships a
host-specific binary. It only automates producing the artifact.

### C. Download the package at install time from a feed

Rejected. It makes `install-process-builder` depend on network reachability of a third party at the
moment of use, on a machine that is often behind a corporate proxy, and it adds a trust decision
(what is authorised to be installed into a customer's Creatio) that a committed in-repo artifact does
not.

### D. Document a manual Application Hub install

Rejected. It does not satisfy the ticket's core requirement — clio must DETECT that the functionality
needs the package and OFFER to install it. It also leaves the version-compatibility question entirely to
the user, while `IRequiredPackageChecker.IsCompatible` can answer it.

## Consequences

**Accepted costs.**

- The compile is the target's and it recurs on every install. Measured from the stands' own `Build.log`:
  12–23 s warm, and 1:09.98 on a cold stand of which 54.93 s was the NuGet restore. Pure MSBuild inside
  that is only 3–6 s; the rest is DB schema load, `DownloadSources` across dependent packages and csproj
  regeneration.
- Recovery from a failed compile is an EXPLICIT action (`RestoreFromBackup` via the Application Hub, or
  `clio restore-configuration`), not a transactional rollback. The install is taken with
  `createBackup: true` so the material to recover from exists.
- Installing triggers a restart. See below — this is a consequence, not a design choice.

**Three non-optional mitigations.** These are load-bearing; removing any one of them reintroduces a
silent failure:

1. **The compile-marker schema must stay in the archive and must stay empty.** Lose it and the package
   installs, satisfies the name-based gate, and never compiles.
   `clio.tests/Common/BundledProcessBuilderPackageTests.cs` pins its presence in the shipped `.gz`.
2. **The command verifies the outcome, because "installed" and "working" are different states.**
   The assembly is produced by the target rather than shipped, so a successful install proves the archive
   was accepted, not that anything compiled. After installing, the command asks the package's own service
   whether it is SERVING — the ungated `Ping` — and fails unless it answers. Fails CLOSED.

   The check is LIVENESS, not identity. What that decides:

   - **First install, build failed.** CAUGHT. Nothing answers: `CustomServicesParser` registers services by
     reflecting over LOADED types, so with no assembly there is no `ProcessDesignService` type and no route.
     Observed on a stand: an install logged `Configuration build finished` with no errors while the route was
     absent, and the check turned that into exit 1 with a diagnosis instead of `Done`.
   - **Re-install of an unchanged version.** Passes, correctly — the package is compiled and serving, which is
     the entire question being asked.
   - **Upgrade, build failed.** NOT caught. The previously built assembly is still loaded and answers, so old
     code keeps serving behind a passing check.

   The third row is an accepted limit, decided deliberately after the alternative was built and measured.
   Catching it requires the shipped version to be readable back out of the RUNNING code, and for a source-only
   package there is exactly one carrier for that — a hand-maintained literal in the shipped sources. Every
   other candidate was measured and eliminated:

   - **Not the assembly version.** A net472 stand: after installing 1.1.0.1 the serving build reported
     `10.1.453.0`, the platform's own version, because the platform stamps what it compiles. The package DOES
     get its own assembly (`probeAssembly = CrtProcessBuilder`, measured), and a literal `<AssemblyVersion>`
     set in the shipped `Directory.Build.targets` DOES survive — but `$(Version)` from the csproj does not, so
     the value would still be hand-maintained, in an MSBuild file instead of a `.cs` one, while additionally
     making the assembly's identity depend on winning a property-precedence race with the platform.
   - **Not generated at build time from the descriptor.** `descriptor.json` is NOT present in the target's
     build directory (measured: two independent path forms, plus `$(PkgPath)` is empty there). The platform
     reads the descriptor at install time and stores it in `SysPackage`; the configuration build never sees it.
   - **Not `SysPackage.Version`, and not the descriptor.** Both record what was ACCEPTED — precisely the state
     a failed build leaves behind.
   - **Not a before/after delta of the serving build's identity** (e.g. the module MVID). A delta answers "did
     something change", which is the wrong predicate: re-installing an unchanged version is a normal, frequent
     operation, and a delta reports failure on it.

   So the trade was: one hand-maintained duplicate of the version inside the package sources, versus detecting
   a stale build on an upgrade. The duplicate was judged the more expensive of the two — an upgrade of the
   bundled package happens under our own supervision, whereas the duplicate is a permanent obligation on every
   version bump and a silent-failure mode of its own if it drifts. **Revisit this if stale-build upgrades turn
   out to be common in practice.** Callers are told the limit: the MCP tool description, the CLI help, and the
   command docs all state that after an upgrade the proof is the functionality working.

   The refusal that sends callers here still has two causes — the package is ABSENT (first install) or OLDER
   than the floor (upgrade) — and the version that decides which lives in the descriptor, reaching clio through
   `SysPackage` and the `[RequiresPackage]` floors. That path is unchanged and needs no carrier.

   The check is also UNGATED on the package side, and that replaced an earlier design where the probe was a
   gated functional call. The install command's question is "did the build take", not "may this caller design
   processes"; the second surfaces at the caller's next call, from the guard's own message, which names the
   right. Conflating them made one verdict out of two problems with different fixes.

   **A per-package version endpoint was built, reverted, reinstated, and finally DROPPED.** Its history is
   worth keeping because each turn was driven by a measurement, and the final answer contradicts the one this
   ADR previously recorded:

   1. Built as `GetVersion`, reporting a version compiled into the assembly — the only thing that detects a
      failed upgrade.
   2. Reverted on three arguments, then reinstated as `GetApiVersion` once each was examined. Two of those
      arguments still hold: `SysPackage.Version` and the `ConfActivityLog` `Compilation` record answer "what was
      accepted" and "did a compilation happen", never "which build is serving now"; and a functional probe
      decides only the first install.
   3. The third argument — "`cliogate` already ships exactly this, so the pattern is the house standard" — turned
      out to be WRONG, and that is what unwound the rest. cliogate reports
      `typeof(CreatioApiGateway).Assembly.GetName().Version`, and it can do so because cliogate ships a
      PREBUILT assembly: we compile it, the target only copies it, and the attribute survives untouched. A
      source-only package has no assembly we control. The parity is not merely superficial — it is
      unavailable. (Confirming this also explained a failed experiment: setting
      `<GenerateAssemblyInfo>false</GenerateAssemblyInfo>` on the package, exactly as cliogate's csproj does,
      built successfully on a stand and left the service route ABSENT — because MSBuild materializes the
      `<AssemblyAttribute Include="Terrasoft.Core.Attributes.PackageReferenceAssemblyAttribute">` item only
      through the `GetAssemblyAttributes` path, which that switch disables. The package's own compiled assembly
      lost the attribute Creatio recognizes it by.)
   4. Dropped, and replaced by the ungated `Ping`. With no assembly of our own, the reported version could only
      come from a hand-maintained literal in the shipped sources — a permanent obligation on every version bump,
      with its own silent-failure mode if it drifts — bought against detecting a stale build on an upgrade of
      a package that is upgraded under our own supervision. See decision 2 above for the full elimination of
      every alternative carrier, each measured on a stand.

   The `ConfActivityLog` route stays worth building for a package that exposes no service of its own, and is
   still untested because it is unknown whether the platform reports a FAILED configuration build at all.

   Two related things are worth recording so they are not re-derived:

   - **`GlobalContext.FailOnError` is not a substitute.** It switches clio's install-success decision to a
     log-substring match on "application installed successfully", which the observed package installs never
     emit — setting it would report failure on a successful install. That is a clio-side defect
     (`BasePackageInstaller`), not a platform one.
   - **Whether a failed configuration build is reported at all is UNVERIFIED.** The experiment that would
     settle it did not run: the deliberately-broken archive was rejected earlier, at
     `AppInstallInfoResolver.ValidateInstallInfos`, before compilation.

3. **The restart must be waited out before the outcome is judged.** Installing a package whose assembly
   changed restarts the instance, and the restart comes from a DIFFERENT place on each runtime: on .NET
   Framework the platform recycles itself once the workspace assembly changes; on .NET
   `BasePackageInstaller` issues it because `IsNetCore` is true. Either way it outlives the install call —
   observed on net472: restart logged 16:44:57,419, install returned 16:44:57,842, `Application_Start`
   16:44:58,735. The command therefore reuses `IServerReadinessWaiter`, whose `InitialDelay` exists
   precisely because the previous app domain may still answer briefly after a restart request. Retrying
   the service probe instead would admit a false pass on an upgrade, answered by the outgoing domain
   serving the old assembly.

**One archive serves every runtime.** Verified with the same bytes on .NET Framework 4.8 and .NET 8, via
both the Application Hub and `push-pkg`.

**The remediation must not be gated.** `install-process-builder` deliberately carries neither
`[RequiresPackage]` (it would be refused by the requirement it exists to satisfy) nor `[FeatureToggle]`
(a gated options type is filtered out of the verb parse array, and a gated MCP primitive is filtered out
of registration — so the fix would be unreachable exactly when it is needed, while the five tools that
name it stay gated behind `process-designer`). Both absences are pinned by tests.

**Order of checks matters for agents.** A user asking to create a process on an environment without the
package must not be told to install it and then told the process cannot be built anyway. The
process-modeling guidance carries an explicit ORDER OF CHECKS paragraph: establish the buildable slice
first, propose the install second.

## Open questions

- `install-process-builder` is documented publicly (CLI verb, `help`, `docs/commands`) while
  `process-designer` — the capability it unblocks — is off by default. The alternative considered was
  gating only the MCP tool and keeping the CLI verb open; that would invalidate the "not feature-gated"
  test and split the two surfaces. Carried, not resolved.
- The two `InternalsVisibleTo` attributes were conditioned out of the customer build (ProcessBuilder
  `3d6783b`), and a guard test pins that the shipped project no longer carries an unconditioned visibility
  group. Closed.
- **Version bumps require the descriptor's `ModifiedOnUtc` to move too**, and that is the one operational
  rule this delivery adds. Creatio treats `ModifiedOnUtc` — not `PackageVersion` — as "this descriptor
  changed" (`PackageStorageComposer.ApplySourcePackageChanges` → `IsPackageDescriptorChanged` →
  `PackageDBStorage.SavePackageDescriptor`'s guard), so a version moved alone installs cleanly and leaves the
  RECORDED version behind — and that recorded version is exactly what the convergence rule compares, so a
  correctly upgraded environment keeps being told it is behind. `clio set-pkg-version` writes both fields, so
  the rule costs nothing when the supported command is used; because this archive is hand-produced, the guard
  fixture pins the date, the version and the archive SHA-256 side by side. Not an open question — a
  documented constraint. (Originally worded in terms of a `[RequiresPackage]` floor; there is none on this
  package any more — see the superseding ADR.)
- **Whether a failed configuration build is reported at all is unverified.** Partly mitigated for THIS
  package and NOT closed: an install whose build produces no assembly is detected, because the package's own
  route then cannot answer — observed on a stand, where the platform logged `Configuration build finished`
  with no errors while the route was absent. What stays undetected is a failed build on an UPGRADE, where the
  previously built assembly keeps answering; see decision 2 for why that limit was accepted. The question
  remains fully open for a package-agnostic check, which is what a bundled package exposing no service of its
  own would need. The seam exists: `IPackageInstallOutcomeVerifier` is named
  for the question, `ProcessDesignServiceOutcomeVerifier` for today's mechanism, so such a check swaps an
  implementation rather than changing the command.
- **A version-based skip via the database is viable and deliberately unbuilt.** (Superseded in part: the
  OPPOSITE direction was built later — an install that would move the recorded version BACKWARDS is now
  refused unless `--force` is passed. The hazard argued below applies to that guard too: an environment
  that ACCEPTED a newer version but never compiled it is refused the older-but-working archive, and
  `--force` — the only way through — is unreachable from MCP.) A skip via the SERVICE is NOT
  viable, and that is by design: `Ping` answers "this package is compiled and serving", not "which build", so
  it cannot tell a current assembly from a stale one and would skip an install that is needed. The database
  half of the original argument, however, was RETRACTED: that the recorded package version is inert.
  It is not — the `SysPackage` row is rewritten whenever the descriptor's `ModifiedOnUtc` moves (see the
  constraint above), so `IRequiredPackageChecker.IsCompatible` could gate the install and save a needless
  configuration build on an up-to-date environment. Left unbuilt because it is a behaviour change with its own
  failure mode: the recorded version says what was ACCEPTED, and for a source-only package accepted is not
  compiled, so a skip would decline to fix an environment that recorded the right version and never built it.
  Building it needs the package-agnostic outcome check above first.
- `BundledPackages` deliberately does NOT hold the cliogate version. The full analysis of what cliogate's
  several version-shaped values actually are — and which of them are genuine duplication versus correctly
  different quantities — lives in one place, the remarks on `clio/Common/BundledPackages.cs`. Read it there
  before touching any of them; earlier copies of that analysis in this file were wrong about
  `cliogate/version.txt`.
