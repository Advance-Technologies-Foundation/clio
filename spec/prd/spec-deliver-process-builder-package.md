# SPEC: Deliver the process-builder package with clio and offer to install it (`install-process-builder`)

- **Jira:** ENG-94385
- **Feature:** `deliver-process-builder-package`
- **ADR:** [adr-deliver-process-builder-package.md](../adr/adr-deliver-process-builder-package.md)
- **Detail:** [deliver-process-builder-package-plan.md](../deliver-process-builder-package/deliver-process-builder-package-plan.md)
- **Scope:** SPEC rather than PRD — four stories, one new command, one new MCP tool, no new subsystem
  (see the routing table in `AGENTS.md`).

## 1. Problem statement

The process-designer capability calls `/rest/ProcessDesignService/*`, implemented in a Creatio package
that is present on no environment by default. Today every one of those commands fails on a fresh
environment, and the failure does not say what to do about it.

The ticket asks for the simplified variant: the package archive is added into clio **by hand** (no CI
build step), ships with clio, and clio must **detect that the functionality requires the package and
offer the client to install it** — the way `cliogate` already works.

Package name: **`CrtProcessBuilder`** (chosen over the ticket summary's `CrtBusinessProcessBuilder`).
Source of the package: the `ProcessBuilder` repo, renamed from `clioprocessbuilder` under
ENG-94385 with the package **UId preserved**, so an existing installation upgrades in place.

## 2. Actors

| Actor | Interest |
|---|---|
| Developer on the CLI | Runs `clio install-process-builder -e <env>` after a command refused, retries. |
| AI agent over MCP | Reads a refusal that names the fix, calls the `install-process-builder` tool, retries the original call. |
| Environment owner | Needs to know a configuration build and a restart happen, and how to recover if the compile fails. |

## 3. Functional requirements

**FR-01 — Bundle.** The archive ships inside the clio distribution and is copied to the output
directory. clio never downloads it. Identity (name, archive filename) comes from one place,
clio/Common/BundledPackages.cs. The VERSION deliberately does not: it is read from the archive's own descriptor at runtime by `IBundledPackageCatalog`, because the archive is a content file that can be replaced without a rebuild (see `spec/adr/adr-bundled-package-version-source-of-truth.md`; `BundledPackagesTests` fails if a version constant is reintroduced).

**FR-02 — One archive, every runtime.** The archive contains no compiled assembly; the target compiles
it during installation. No `IsNetCore` branch, no per-framework artifact. (ADR: Decision.)

**FR-03 — Install command.** `clio install-process-builder` (aliases `update-process-builder`,
`installprocessbuilder`) installs or updates the package in a registered environment. Options:
`-e/--environment` or direct `-u/-l/-p`.

**FR-04 — Bundled-artifact pre-check.** A distribution that failed to carry the archive says so plainly,
naming the expected path, instead of surfacing as a generic failure from inside the installer.

**FR-05 — No short-circuit, with two exceptions.** The command always installs, EXCEPT in two cases, both about an environment moving BACKWARDS and both overridable only from the command line: (a) the install would move the environment's recorded version backwards, and (b) this clio's own bundled version carries a pre-release suffix, which would make case (a) undetectable. Neither refusal quotes the override flag, because the refusal text reaches an MCP agent's context and the decision is a human's. There is no cheap trustworthy way to ask "is this
environment already serving what I ship", so an explicitly requested install is performed: it is invoked as
remediation, the install is backed up, and a needless run costs one configuration build.

**FR-06 — Restart is waited out, never requested.** The command issues no restart, but one happens on
both runtimes and outlives the install call. The command waits for the instance to answer its health
check before judging the result. (ADR: Consequences, mitigation 3.)

**FR-07 — Outcome verification.** After a successful install the command asks the package's own service
whether it is serving — the ungated `Ping` — and fails unless it answers. Fails CLOSED. The check is LIVENESS,
not identity: a first install whose build failed cannot answer at all (no assembly, no type, no route), and a
re-install of an unchanged version passes correctly; an UPGRADE whose build failed is deliberately NOT covered,
because the previously built assembly still answers. `SysPackage.Version` cannot substitute even for the part
that IS covered — it records what was ACCEPTED. Covering the upgrade case would require a hand-maintained copy
of the version inside the shipped sources (the assembly version belongs to the platform, and `descriptor.json`
never reaches the target's build directory — both measured); that duplicate was judged more expensive than the
case, and the limit is disclosed in the tool description, the CLI help and the command docs. The check needs NO permission: `Ping` is ungated, which is what keeps
"the build did not take" apart from "you may not design processes" — two problems with different fixes, and
only the first is this command's business. So exit code 1 here never means a missing right. A per-package
endpoint that answered "which build is serving" was built, reverted, reinstated and finally dropped (see the
ADR for the full elimination): with no assembly of clio's own, the reported version could only come from a
hand-maintained duplicate inside the shipped sources. The package-agnostic replacement (installation log +
`ConfActivityLog`, in clio) is follow-up work.

**FR-08 — Detection and offer.** The five consuming commands
(`create-business-process`, `modify-business-process`, `describe-business-process`, `list-user-tasks`,
`validate-process-graph`) carry a PRESENCE-ONLY `[RequiresPackage]` whose `Hint` names the exact remediation. "Behind" is a separate rule (`IBundledPackageConvergence`), not a floor on the attribute
These five are MCP TOOLS with no CLI verbs, and sit behind the `process-designer` feature toggle.

**FR-09 — The remediation is reachable.** `install-process-builder` carries neither `[RequiresPackage]`
nor `[FeatureToggle]`, so it cannot be filtered out by the gate it exists to satisfy.

**FR-10 — Install must not unlock maintainer packages.** The install runs with
`DeveloperModeEnabled = false`: on a developer-mode environment `push-pkg`'s unlock step routes through
cliogate and fails even when the package itself installed correctly.

**FR-11 — MCP shape.** `install-process-builder` takes one required argument, `environment-name`; flags `ReadOnly=false, Destructive=true, Idempotent=true, OpenWorld=false`. Destructive is
`true` because that flag is what clio's core-rules guidance ties "confirm the target environment with the
user first" to, and this tool runs a configuration build on a live instance and restarts it. It is long-tail
(not resident in `tools/list`), reachable through the `get-tool-contract` compact index, and runs under the
progress-heartbeat + response-deadline helper because the call is minutes long.

**FR-12 — Order of checks in agent guidance.** An agent must establish the buildable slice BEFORE
proposing the install, so a user is never told to install the package and then told the process cannot be
built anyway.

## 4. Non-functional requirements

- **NFR-01** Install is long-running because the target runs a configuration build, and every surface says
  so WITHOUT quoting a duration (help, docs, MCP tool description and contract, deploy-lifecycle and
  process-modeling guidance). Deliberately not a time budget: the elapsed time is a property of the target
  environment, not of clio, so a stated range cannot be satisfied in general — and on the MCP surface a stated
  range became a promise an agent repeated to the user. Completion is defined by the instance answering its
  health check and then the package's own service answering, never by a clock. Observed 15–75 s on the stands
  used during this work; that figure lives in the maintainer notes as an observation only.
- **NFR-02** Failure diagnostics carry the readable message FIRST (HTTP status / WebException), then the
  stack — a 401 must be distinguishable from a connect timeout.
- **NFR-03** Recovery from a failed compile is documented: the Application Hub's own restore step, or
  `clio restore-configuration` from the command line.

## 5. Acceptance criteria

- **AC-01** On an environment without the package, a process-designer command refuses with a message
  naming `install-process-builder`, and the install then makes the same command succeed.
- **AC-02** The same archive bytes install and work on .NET Framework 4.8 and on .NET 8.
- **AC-03** `install-process-builder` returns 0 only when `ProcessDesignService` answers afterwards; an
  environment that accepted the package without compiling it returns 1 with an actionable message.
- **AC-04** Re-running is safe and installs again; it never reports "nothing to do" for an environment whose
  package is present but not working.
- **AC-07** The environment's RECORDED version is what convergence compares, and it is satisfied on an environment upgraded in place — which requires
  the rebundle to move the descriptor's `ModifiedOnUtc` together with `PackageVersion` (use
  `clio set-pkg-version`), because Creatio rewrites the recorded version only when the date changed.
- **AC-05** With `process-designer` OFF, the MCP server still advertises `install-process-builder` while
  the five gated tools are invisible.
- **AC-06** `clio info` reports the bundled package version.

## 6. Touch points

| Area | Change |
|---|---|
| Bundle | `clio/Common/BundledPackages.cs`, `clio/CrtProcessBuilder/CrtProcessBuilder.gz`, csproj `Content` glob, `.gitattributes` (`*.gz binary`) |
| Command | `clio/Command/InstallProcessBuilderCommand.cs` |
| MCP | `Tools/InstallProcessBuilderTool.cs`, `ToolContractGetTool.BuildInstallProcessBuilder`, `Resources/ProcessDesigner/ProcessModelingGuidanceResource.cs`, `Resources/DeployLifecycleGuidanceResource.cs` |
| Gate | 5 `[RequiresPackage]` sites; `BaseTool` returns `FromValidationError` (exit 1) for `PackageRequirementException`, not `FromError` (−1) |
| Info | `clio/Command/InfoCommand.cs` |
| Docs | `help/en/install-process-builder.txt`, `docs/commands/install-process-builder.md`, `Commands.md`, `Wiki/WikiAnchors.txt`, `docs/McpCapabilityMap.md` §11 |

## 7. Test coverage, and the one deliberate gap

**Automated.** 11 unit tests on the command (including the readiness-wait race:
`Execute_ShouldFailWithoutProbing_WhenInstanceDoesNotBecomeReady`), 4 archive-content tests pinning the
descriptor identity and the compile-marker schema in the shipped `.gz`, 4 MCP tool tests including the
"not feature-gated" invariant, lock-in tests on the presence-only requirement and the verbatim `Hint`, and 3
stand-free MCP E2E tests — discovery-while-gated-off, the curated contract (with a regression guard
against the retracted "no restart" wording), and the invalid-environment envelope.

**Deliberately NOT automated: a real install against a live stand.** Installing restarts the instance,
and the E2E suite runs `NumberOfTestWorkers=2` against one shared sandbox, where a mid-run restart
cascades across fixtures. Every tool in that suite whose real effect is a restart or a configuration
build is covered only by its stand-free path — `install-gate` has no E2E fixture at all, `compile-creatio`
and `restart-web-app` cover only their negative paths, and `deploy-creatio` deliberately feeds a corrupt
archive. A fixture written to the guard set the one genuinely-mutating fixture uses (`[Explicit]` +
`LocalOnly` + a `TEAMCITY_VERSION` refusal) would never run in CI, so it would be a script with
assertions rather than a regression gate.

The real-install path is instead proven by **live runs recorded in the plan** — net472 78 s exit 0,
.NET 8 71 s exit 0, package listed, 23 user tasks returned each time, with server-log evidence of the
configuration build and both restart mechanisms — and guarded at RUNTIME by FR-07, which is the check
that would catch the failure a real-install test would look for.

## 8. Open questions

Carried in the ADR: the public documentation of `install-process-builder` versus `process-designer` being
off by default; the `InternalsVisibleTo` attributes shipping into the customer-compiled assembly; and the
cliogate version still living in three places outside `BundledPackages`.
