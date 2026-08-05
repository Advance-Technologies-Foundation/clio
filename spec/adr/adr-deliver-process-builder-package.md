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
   `[RequiresPackage(BundledPackages.ProcessBuilderPackageName, BundledPackages.ProcessBuilderVersion, Hint = "…")]`,
   so a missing or stale package produces a refusal that names the fix.

`clio/Common/BundledPackages.cs` is the single source of truth for the name, version and archive
filename. Nothing repeats those literals.

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
2. **The command verifies the outcome against the RUNNING assembly, and the database cannot substitute.**
   Because the assembly is produced by the target rather than shipped, "installed" and "working" are
   genuinely different states. After a successful install the command asks
   `ProcessDesignService.GetVersion` — an operation that returns a constant compiled INTO the assembly —
   and fails when the reported version is older than `BundledPackages.ProcessBuilderBuildVersion`. This
   check fails CLOSED; the pre-install probe fails OPEN, because an unreachable host must not block an
   explicitly requested install.

   Two platform behaviours make this the only workable shape, both established on stands on 2026-08-05:

   - **A failed configuration build is invisible from the database.** The platform records the new version
     when it ACCEPTS the archive and keeps serving the assembly it last built successfully, so after a
     build failure the database and the running code disagree — and both sides of any database-only
     comparison come from the descriptor. `GlobalContext.FailOnError` is NOT an alternative: it switches
     install success to a log-substring match on "application installed successfully", which the observed
     package installs never emit, so setting it would report failure on a successful install.
   - **The recorded version is frozen at the first install.** Creatio does not update it when re-installing
     a package it already has (it matches by `UId`). Verified on both runtimes: after installing the
     1.1.0.0 archive, `GetVersion` reported `1.1.0.0` while `clio list-packages` still reported `1.0.0.0`.

   Hence two constants, not one. `ProcessBuilderVersion` (the `[RequiresPackage]` floor, read from the
   database) is **effectively frozen** — raising it would refuse the five gated commands forever on every
   environment that already carries the package, and in practice it can only move when the `UId` changes.
   `ProcessBuilderBuildVersion` (read from the service) moves with every rebundle. `GetVersion` is
   deliberately UNGATED on the package side: installing a package and designing processes are different
   rights, and the guard's rejection is returned as an unsuccessful envelope, i.e. indistinguishable from
   "never compiled". Packages predating `GetVersion` fall back to a `ListUserTasks` proof-of-life, which is
   weaker on both counts — it cannot tell which build answered, and it needs `CanManageProcessDesign`.
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
- The two `InternalsVisibleTo` attributes in the package source ship into the customer-compiled
  assembly. Conditioning them out was offered and not taken.
- `BundledPackages` deliberately does NOT yet hold the cliogate version, which is still spread across a
  constant in `InfoCommand`, `cliogate/descriptor.json` and a stale `cliogate/version.txt` that nothing
  writes. Collapsing that triple is separate work; do not add a fourth copy.
