# Test Plan: Deliver the CrtProcessBuilder package inside clio

**Feature**: deliver-process-builder-package
**Jira**: [ENG-94385](https://creatio.atlassian.net/browse/ENG-94385)
**Stories**:
[story-1 (bundle the archive)](../sprint-status.yaml) ·
[story-2 (command + MCP tool)](../sprint-status.yaml) ·
[story-3 (detect and name the remediation)](../sprint-status.yaml) ·
[story-4 (docs, contract, E2E)](../sprint-status.yaml)
**SPEC**: [spec-deliver-process-builder-package.md](../prd/spec-deliver-process-builder-package.md)
**ADR**: [adr-deliver-process-builder-package.md](../adr/adr-deliver-process-builder-package.md)
**Plan**: [deliver-process-builder-package-plan.md](../deliver-process-builder-package/deliver-process-builder-package-plan.md)
**Author**: written after implementation, from the delivered coverage
**Status**: Complete — every case below exists and passes
**Created**: 2026-08-06

---

## Why this plan reads backwards

BMAD puts the test plan before implementation. This one was written after, so it is a **map of the
delivered coverage and an honest statement of what is not covered**, not a forecast. That is worth more
here than a reconstructed prediction would be, because this feature's central risk is not a logic bug —
it is that **three of its failure modes are silent**, and a plan that only enumerated happy-path cases
would have looked complete while missing all three.

The three, which every section below is organised around:

1. A package can install and never be **compiled**. The name-based gate then reports it present while
   every service call fails.
2. A version can move in the archive and not in the environment's **recorded** version, because Creatio
   decides from the descriptor's `ModifiedOnUtc`, not from `PackageVersion` — so the `[RequiresPackage]`
   floor refuses an environment that WAS upgraded correctly.
3. The remediation command can be made **unreachable by the very gate it exists to satisfy**, on either
   surface, by one attribute.

---

## Scope

### In scope

- **Story 1** — the committed source-only archive: its identity (`UId`, name, version, `ModifiedOnUtc`),
  the load-bearing compile-marker schema, the absence of any compiled assembly, the presence of sources,
  the authorization gate inside the shipped code, and the friend-assembly condition.
- **Story 2** — `InstallProcessBuilderCommand`: install → wait out the platform's own restart → ask
  `IPackageInstallOutcomeVerifier` whether the package became operational, and every failure branch of that
  sequence. The verifier's own answer shapes are covered separately, because the interface is named for the
  question and its implementation is the part the recorded follow-up replaces. Plus the MCP tool wrapper:
  argument mapping, annotations, the response-deadline branch, and concurrency.
- **Story 3** — the `[RequiresPackage]` floor on the five process-designer surfaces, and the two absences
  that keep the remediation reachable.
- **Story 4** — the curated MCP contract, the shipped tool description, and the stand-free MCP E2E path.
- `PackageDescriptor.ConvertToModifiedOnUtc` — a `DateTimeKind` defect found while establishing silent
  failure 2, fixed at the root rather than at the call site.

### Out of scope (with reason)

- **A real install against a live environment, from the test suite.** Deliberate, and the single most
  important scope decision here. The E2E suite runs on TeamCity with `NumberOfTestWorkers=2` against one
  shared stand; this install runs a configuration build and restarts the instance, so such a test would
  break every test sharing that stand for a minute or more. `cliogate` — the other bundled package — has
  no such test either, for the same reason. Verified manually instead, on both runtimes (see Manual
  verification).
- **Whether the platform reports a FAILED configuration build.** Not merely untested — **unverified as
  behaviour**. The experiment did not run: a deliberately-broken archive was rejected earlier, at
  `AppInstallInfoResolver.ValidateInstallInfos`, before compilation. Recorded as an open question in the
  ADR; it bounds what the probe below can promise.
- **The nupkg payload.** `BundledArchive_ShouldExistInBuildOutput_…` asserts the BUILD OUTPUT path the
  install command resolves, and says so; a packaging assertion needs a packed nupkg, which the unit suite
  does not produce.
- Anything inside the package repository (`ProcessBuilder`) — its own suite owns the service's behaviour.
  clio's tests assert only what the ARCHIVE carries.

---

## Risk Assessment

| Risk | Likelihood | Impact | Mitigation |
|------|-----------|--------|-----------|
| **[HIGHEST] Installed but never compiled.** Accepting the archive and compiling it are separate events. The recorded version moves on ACCEPT, so the gate is satisfied while every `/rest/ProcessDesignService/*` call fails — and a failed build leaves the previously-built assembly serving, so nothing looks wrong. | Med | High | TC-U-02 asserts the command fails rather than reporting success when the service does not answer; TC-U-11 pins the compile-marker schema inside the committed `.gz` (losing it is what causes this state); TC-U-13 pins that no compiled assembly ships, since one would answer the probe from stale code and hide a failed build. |
| **The recorded version does not move.** `PackageStorageComposer.ApplySourcePackageChanges` compares `ModifiedOnUtc`, not `PackageVersion`, so a version bumped alone installs cleanly and leaves the recorded version behind — making the floor unsatisfiable on a correctly upgraded environment. | Med | High | TC-U-09 pins version and `ModifiedOnUtc` side by side with the archive hash, so a hand edit that moves one without the other fails in clio's suite rather than on a customer's environment. `clio set-pkg-version` writes both, so the supported path cannot produce it. |
| **The remediation is gated by what it fixes.** A `[RequiresPackage]` on its own options type would be refused by the requirement it exists to satisfy; a `[FeatureToggle]` on either the options type or the tool type would remove it from the verb parse array or from MCP registration — in both cases exactly when the five gated tools are telling callers to run it. | Low | High | Three separate absence tests (TC-U-16/17/18), because the two surfaces read different attributes, plus TC-E2E-01 which proves the CONSEQUENCE against a real server started with an empty `Features` map. |
| **Agent-facing text drifts from behaviour.** Three designs were reverted mid-flight (`GetVersion`, presence-only gates, a version short-circuit). Their claims survive in comments, XML docs, the shipped MCP contract and test `because` clauses — where a false claim does not fail anything, it just misleads. | High | Med | TC-U-20 pins that the contract does not claim the tool can tell which build is serving; TC-E2E-02 pins the same on the real MCP path plus the retracted no-restart claim; TC-U-19 pins the annotations against their description. Residual: prose has no complete oracle — a sweep for every copy of a retracted claim is a review step, not a test. |
| **The deadline branch reporting a verdict.** The MCP response deadline can fire while the install is still running, and the branch answers exit 0. If it reads as success an agent proceeds against a package that may still fail to install; if the detached run then fails, its exit code has no response to travel on. | Med | High | TC-U-14 asserts the notice states it is NOT a verdict and does not send the caller back to the installer. Residual: the stderr report of a post-deadline failure is not asserted (it is a diagnostic side effect on a detached thread). |
| **Concurrent installs.** Two installs, or an install and a `compile-creatio`, on the same environment mean two configuration builds and two restarts on one instance. | Med | Med | TC-U-15 asserts the second is refused, without resolving or running the command. |
| **Timestamp regression across the whole product.** The `DateTimeKind` fix touches `PackageDescriptor`, which every `set-pkg-version` and schema save goes through — a wrong instant here silently mis-stamps every package clio writes. | Low | High | TC-U-21..23 pin both kinds and their agreement. Full-suite run required by the shared-infrastructure rule, not the module filter. |
| Shared MCP infrastructure. The lock-free execution path added to `BaseTool` is inherited by ~20 tools. | Low | High | Full unit suite; `BaseToolTests` and `CompileCreatioToolTests` (whose reservation API was renamed) run green. |

---

## Unit Tests (`clio.tests/`)

### Story 1 — the committed archive (`clio.tests/Common/BundledProcessBuilderPackageTests.cs`)

This fixture is the only reviewability the artifact has: it is hand-produced in another repository, and a
`.gz` change otherwise renders in a diff as a byte count.

| ID | Test | Asserts |
|----|------|---------|
| TC-U-09 | `BundledArchive_ShouldMatchThePinnedHash` + `BundledArchive_ShouldCarryADescriptorMatchingBundledPackages` | SHA-256, `UId`, name, `PackageVersion` and `ModifiedOnUtc` pinned side by side — the three-pin rule that makes silent failure 2 fail here first |
| TC-U-10 | `BundledArchive_ShouldCarryTheAuthorizationGateOnTheServiceHandlers` | the `CanManageProcessDesign` operation literal, the `ConnectionType != UserType.General` half, and a floor on gate call sites |
| TC-U-11 | `BundledArchive_ShouldContainTheCompileMarkerSchema` | the one empty Source Code schema that drags the package into the target's configuration build |
| TC-U-12 | `BundledArchive_ShouldCarryThePackageSources` | sources present — the target compiles them, so their absence is fatal |
| TC-U-13 | `BundledArchive_ShouldNotCarryACompiledAssembly` | no `CrtProcessBuilder.dll`/`.pdb`, case-insensitively (Windows resolves the package assembly without regard to case) |
| TC-U-24 | `BundledArchive_ShouldNotGrantFriendAccessOnTheCustomerBuild` | every `InternalsVisibleTo` sits inside an ItemGroup conditioned on the local-only property — the pairing, not a hand-written label in another repository |
| TC-U-25 | `BundledArchive_ShouldExistInBuildOutput_AtThePathTheInstallCommandResolves` | the archive is where `IWorkingDirectoriesProvider.ExecutingDirectory` resolves it; this is the trap that invalidates local verification after a repack |

### Story 2 — the command (`clio.tests/Command/InstallProcessBuilderCommandTests.cs`)

| ID | Test | Asserts |
|----|------|---------|
| TC-U-01 | `Execute_ShouldInstallPackageAndVerifyTheServiceAnswers` | the happy path is install → readiness → probe, in that order |
| TC-U-02 | `Execute_ShouldFail_WhenPackageInstallsButServiceDoesNotAnswer` | exit 1 when the verifier reports the package not operational and has nothing more specific to say |
| TC-U-03 | `Execute_ShouldReportTheVerifierDiagnosis_WhenTheVerifierSuppliesOne` | the verifier's diagnosis SUPPRESSES the command's generic build-failure message rather than being appended — the generic text sends the reader to a build log that is clean when the failure is an authorization rejection inside a package that built fine |
| TC-U-04 | `Execute_ShouldFailWithoutProbing_WhenInstanceDoesNotBecomeReady` | no probe before the instance answers its health check — probing sooner can be answered by the outgoing app domain |
| TC-U-05 | `Execute_ShouldFailWithoutInstalling_WhenBundledArchiveIsMissing` | a broken distribution says so, and says retrying will not help |
| TC-U-06 | `Execute_ShouldReturnFailureAndSkipServiceCheck_WhenPackageInstallFails` | no probe after a failed install |
| TC-U-07 | `Execute_ShouldReportReadableMessageFirst_WhenInstallerThrows` | the readable message precedes the stack, so the WebException status is not lost |
| TC-U-08 | `Execute_ShouldResolveTheSameArchive_WhenEnvironmentIsNetCore` | one archive for every runtime — there is no per-runtime name to choose |
| TC-U-16 | `InstallProcessBuilderOptions_ShouldNotDeclareAnyPackageRequirement` | the remediation is not gated by its own package |

### Story 2 — the MCP tool (`clio.tests/Command/McpServer/InstallProcessBuilderToolTests.cs`)

| ID | Test | Asserts |
|----|------|---------|
| TC-U-13a | `InstallProcessBuilder_Should_Advertise_Stable_Tool_Name` | the name every refusal hint points at |
| TC-U-14a | `InstallProcessBuilder_Should_Resolve_Command_For_Environment_And_Return_Exit_Code` | environment-name is the only mapped argument; no MCP-supplied URI can retarget the install |
| TC-U-19 | `InstallProcessBuilder_Should_Expose_Expected_Mcp_Metadata` | `Destructive=true` (it is what ties the confirm-the-target rule to this tool), `Idempotent` justified by sequential convergence, and the description naming the package, a caller tool, the probe and `list-packages` |
| TC-U-14 | `InstallProcessBuilder_Should_Return_NonVerdict_InProgressNotice_When_ResponseDeadlineExceeded` | exit 0, "NOT a verdict", and no instruction to re-run the installer |
| TC-U-15 | `InstallProcessBuilder_Should_Refuse_When_ConfigurationBuild_AlreadyInFlight` | a duplicate is refused with exit 1, and the command is never resolved |
| TC-U-17 | `InstallProcessBuilderOptions_Should_Not_Be_FeatureGated` | a gated options type disappears from the verb parse array |
| TC-U-18 | `InstallProcessBuilderTool_Should_Not_Be_FeatureGated` | a gated primitive is filtered out of MCP registration — a different attribute read by a different surface, hence a separate test |

### Story 2 — the outcome verifier (`clio.tests/Package/ProcessDesignServiceOutcomeVerifierTests.cs`)

`IPackageInstallOutcomeVerifier` owns the question "did the package become operational after being
accepted", and is named for that question rather than for today's mechanism, because the recorded follow-up
replaces the mechanism (installation log + `ConfActivityLog`) and must not have to change the interface. The
command fixture above asserts only that the question is asked at the right moment and obeyed; every answer
shape is pinned here.

| ID | Test | Asserts |
|----|------|---------|
| TC-U-30 | `IsPackageOperational_ShouldReturnTrue_WhenServiceReturnsSuccessfulEnvelope` | only positive evidence yields true, and it carries no diagnosis |
| TC-U-31 | `IsPackageOperational_ShouldReturnFalseAndLogTheCause_WhenRouteReturnsHtml` | an HTML error page from an unbound route — the exact shape of "installed but never compiled" — fails CLOSED, and the cause is logged at error level |
| TC-U-32 | `IsPackageOperational_ShouldReturnFalseWithDiagnosis_WhenEnvelopeReportsFailure` | a parseable `success:false` proves the opposite of a build failure, so the service's own message becomes the diagnosis, names the package it was asked about, and says a re-install cannot help |
| TC-U-33 | `IsPackageOperational_ShouldReturnFalse_WhenEnvelopeIsMissing` | valid JSON from the wrong responder (proxy, login redirect) is not evidence |
| TC-U-34 | `IsPackageOperational_ShouldReturnFalse_WhenTheCallThrows` | an unreachable service is a verdict, not an escaping exception |
| TC-U-35 | `IsPackageOperational_ShouldBoundTheProbe_AndRetryIt` | the call is bounded and retried — `ExecutePostRequest` defaults to `Timeout.Infinite`, and the readiness wait the caller performs is weaker than this question |
| TC-U-36 | `Constructor_ShouldRejectNullCollaborators` | a misconfigured graph fails at construction, not mid-install |

### Story 3 — the gate (`clio.tests/Command/ProcessDesignerRequiresPackageAttributeTests.cs`)

| ID | Test | Asserts |
|----|------|---------|
| TC-U-26 | `OptionsType_ShouldDeclareVersionedProcessBuilderRequirement_WhenProcessDesignerCommand` | all five surfaces carry the floor, four-part, sourced from `BundledPackages` |
| TC-U-27 | `ValidateProcessGraphArgs_ShouldDeclareVersionedProcessBuilderRequirement_WhenStandaloneTool` | the standalone tool's own args type carries it too |
| TC-U-28 | `GetProcessSignatureOptions_ShouldNotDeclareProcessBuilderRequirement_BecauseItUsesTheBuiltInDataService` | a boundary case — this one does NOT need the package, and pinning that keeps the floor from spreading by habit |
| TC-U-29 | `DescribeProcessArgs_ShouldNotDeclareAnyPackageRequirement_BecauseGateReadsOptionsType` | the gate reads the options type, so an attribute on the args type would be inert |

### Story 4 — the curated contract (`clio.tests/Command/McpServer/ToolContractGetToolTests.cs`)

| ID | Test | Asserts |
|----|------|---------|
| TC-U-20 | `ToolContractGet_Should_Return_InstallProcessBuilder_Contract` | discoverable by name, one required argument, the flow stops at itself, and the rationale does NOT claim it can tell which build is serving. Mirrored here even though E2E pins the same text: MCP E2E is **advisory** (cannot fail a merge), and the process-designer fixtures do not run in CI at all yet — the CI-deployed stand carries no `CrtProcessBuilder` package, tracked separately. See `project-context.md` |

### The timestamp fix (`clio.tests/Package/PackageDescriptorTests.cs`)

| ID | Test | Asserts |
|----|------|---------|
| TC-U-21 | `ConvertToModifiedOnUtc_ShouldPreserveTheInstant_WhenInputIsLocal` | the pre-existing caller stays correct |
| TC-U-22 | `ConvertToModifiedOnUtc_ShouldPreserveTheInstant_WhenInputIsUtc` | the defect: dropping `Kind` made a `UtcNow` value serialize as if local |
| TC-U-23 | `ConvertToModifiedOnUtc_ShouldAgree_AcrossInputKinds` | both spellings of one instant serialize identically — the property that makes the two cases above one fix |

---

## MCP E2E (`clio.mcp.e2e/InstallProcessBuilderContractToolE2ETests.cs`)

Every case runs against a real `clio mcp-server` over stdio with an isolated `CLIO_HOME` and an **empty
`Features` map**, and none of them mutates an environment. Being stand-free is what keeps them runnable at
all: the process-designer fixtures elsewhere in this project do NOT run in CI, because the CI-deployed stand
carries no `CrtProcessBuilder` package (tracked separately). Even so, MCP E2E is an advisory check that
cannot fail a merge, which is why every load-bearing claim below is also pinned at unit level.

| ID | Test | Asserts |
|----|------|---------|
| TC-E2E-01 | `InstallProcessBuilder_Should_StayReachable_WhileProcessDesignerToolsAreGatedOff` | the consequence, not the attribute: with the feature off the server still advertises `install-process-builder` while the five gated tools are absent |
| TC-E2E-02 | `InstallProcessBuilder_Contract_Should_Describe_Arguments_And_Outcome_Verification` | the curated contract shape over the wire, plus two guards against retracted claims (no-restart, which-build-is-serving) |
| TC-E2E-03 | `InstallProcessBuilder_Should_Report_Invalid_Environment_Failure` | an unregistered environment comes back as a structured envelope with exit 1, not a transport error — and cannot silently fall back to the active environment |

---

## Manual verification (in place of the excluded live test)

Performed on two stands, .NET Framework 4.8 and .NET 8.0.29, on 2026-08-05, through both the Application
Hub and `clio push-pkg`:

- Install on an environment without the package, then `ListUserTasks` answers with the full 23-task catalogue.
- Upgrade in place with `ModifiedOnUtc` moved: the recorded version moves 1.1.0.0 → 1.1.0.1 on both runtimes.
- Upgrade with `PackageVersion` moved alone: install succeeds, recorded version stays behind. This is
  silent failure 2, reproduced deliberately and in both directions.
- Compile timing read from the server's own logs: 12–25 s steady state, ~1 min on a cold stand (NuGet
  restore), 71–78 s end to end including upload, database work and the restart.

---

## Regression scope

- **Full unit suite, not the module filter.** `clio/Common/` (`BundledPackages`) and the MCP `BaseTool` /
  `McpToolExecutionLock` shared infrastructure are both full-suite triggers under the smart-regression
  policy in `AGENTS.md`. Last run: 7991 passed, 0 failed, 25 skipped.
- `CompileCreatioToolTests` — the narrow reservation API it uses was renamed to
  `TryReserveConfigurationBuild`, since an install and a compile must exclude each other.
- `BaseToolTests` — the resolve/execute path was split so a lock-free entry could share it.
- `RemoteCommandCliogateTests` — the process-designer gate tests were extracted out of it.
- Zero `CLIO*` analyzer diagnostics required, per the analyzer-handling policy.

## Coverage gaps, stated rather than implied

1. **A failed configuration build.** Untestable until the platform's reporting behaviour is established
   (see Out of scope). Until then the probe is what stands between "installed" and "working".
2. **The archive is pinned by one whole-file hash.** Substitution is detectable and CI-enforced, but not
   *reviewable*: a per-file manifest would make a rebundle diff as an inventory. Follow-up.
3. **The post-deadline stderr report** is not asserted — a diagnostic side effect on a detached thread.
4. **Which build answered.** No test can close this; the probe cannot distinguish, by design, after the
   per-package `GetVersion` operation was reverted. A package-agnostic outcome check reading the
   installation log plus `ConfActivityLog` is the recorded follow-up, and it would serve every bundled
   package rather than this one. The seam it lands in now exists: `IPackageInstallOutcomeVerifier` is named
   for the question, so that replacement swaps the implementation and the cases above keep their meaning.
