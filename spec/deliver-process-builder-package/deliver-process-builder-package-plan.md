# Deliver the process-builder package — bundle into clio and install on demand


> **AS-PLANNED, SUPERSEDED.** This document records the plan as written and is deliberately not
> rewritten. Two things in it no longer describe the shipped code: the outcome check probes the
> package's ungated `Ping`, not `KnownRoute.ListUserTasks`, and it verifies LIVENESS only — it does not
> compare versions. See `spec/adr/adr-deliver-process-builder-package.md` for what shipped and for the
> measurements that changed it.
>
> A third since: every mention below of a `BundledPackages.ProcessBuilderVersion` constant, and of a
> `[RequiresPackage]` FLOOR equal to it, describes a design that was replaced. The shipped version is read
> out of the archive (`IBundledPackageCatalog`), the five gates are presence-only, and being behind is a
> separate convergence rule. Do not implement the constant from this document — see
> `spec/adr/adr-bundled-package-version-source-of-truth.md`.

**Ticket:** [ENG-94385](https://creatio.atlassian.net/browse/ENG-94385) — *Deliver clioprocessbuilder package — Install package bundled into clio*
**Decision record:** [ENG-91840 / Confluence 4764434460](https://creatio.atlassian.net/wiki/spaces/TER/pages/4764434460) — Option 2 ("same way as clio gate") chosen.
**Sibling ticket (NOT this plan):** [ENG-92113](https://creatio.atlassian.net/browse/ENG-92113) — CI → dfs-ts chain, owner O. Serhiichyk.
**Prior art ADR:** `spec/adr/adr-ENG-90883-backend-process-designer.md` (lists "package delivery/install wiring (ship `.gz` like `cliogate`)" as an open item — this plan closes it).

**BMAD artifacts distilled from this plan** (read those first; this file is the working record behind them):
- Requirements/ACs: [spec/prd/spec-deliver-process-builder-package.md](../prd/spec-deliver-process-builder-package.md)
- Decision + alternatives + consequences: [spec/adr/adr-deliver-process-builder-package.md](../adr/adr-deliver-process-builder-package.md)
- Tracker: `spec/sprint-status.yaml`, rows `story-deliver-process-builder-package-1..4`

This plan keeps what the distilled artifacts deliberately omit: the full experiment log, the measured
timings, and the four dead hypotheses — so they are not re-proposed.

---

## 0. Simplified scope — what we do and what we skip

The full delivery chain in the ticket is `GHE repo → CI → dfs-ts → commit into clio → clio release`.
This plan deliberately implements the **clio-side half only**, with the artifact produced by hand:

| Chain link | Status in this plan | Owner |
|---|---|---|
| Source on GHE | **Already done** — `creatio.ghe.com/engineering/cli-process-builder`, branch `main` | — |
| Rename package → `CrtProcessBuilder` | **IN SCOPE** (P0) | this ticket |
| CI build (TeamCity, composable-app style) | **SKIPPED** — manual, documented runbook instead | ENG-92113 |
| Deliver `.gz` to `\\tscrm.com\dfs-ts\…` + build registration | **SKIPPED** | ENG-92113 |
| `.gz` committed into clio + bundled in the packed tool | **IN SCOPE** (P2) | this ticket |
| clio release ships it | **IN SCOPE** (P2 — falls out of the csproj `Content` entry) | this ticket |
| clio detects the package is required and offers the install | **IN SCOPE** (P3, P4) | this ticket |
| Automatic clio↔package version alignment | **OUT** — the ticket says "will be a separate task" | separate ticket |
| Auto-install on first `create-bp` without an explicit call | **OUT** — deferred (Confluence O2.10) | deferred |
| Ship in the Creatio product (`CrtProcessBuilder`, release train) | **OUT** — Option 1, deferred end-state | deferred |
| Rename the REST service class | **EXPLICITLY NOT DONE** — see §2.2 | — |

Because CI is skipped, the artifact is produced by hand. Under the source-only decision (§3) that is
cheap and machine-independent: producing it needs only `clio` and a checkout — `set-pkg-version` if the
version changed, then `compress`. There is **no build step**, so the earlier concern that the producer
needs a machine with a deployed Creatio no longer applies. ENG-92113 still automates the hop; until then
the runbook (P1) is the contract, and P1.9 records provenance so "which source produced this archive" has
an answer.

---

## 1. What already exists (do not rebuild it)

This is the single most important input to the estimate: **the detection half is already shipped.**

| Capability | Where | State |
|---|---|---|
| Declarative package requirement | `clio/Common/RequiresPackageAttribute.cs` — `[RequiresPackage(name, version?, Hint=…)]`, class-level or bool-property-level | shipped, package-agnostic |
| Requirement enforcement engine | `clio/Common/RequiredPackageChecker.cs` — `IsInstalled` / `GetInstalledVersion` / `IsCompatible` / `EnsureRequirements`, alias map, fail-closed version compare | shipped |
| Installed-package read | `clio/Package/ApplicationPackageListProvider.cs:102-112` — plain DataService `SysPackage` SelectQuery | shipped — **no cliogate dependency** (but see the rights caveat below) |
| CLI enforcement chokepoint | `clio/Program.cs:411-415`, helper at `:683-704` | shipped |
| MCP enforcement chokepoint | `clio/Command/McpServer/Tools/BaseTool.cs:245-251`, refusal mapping at `:177-179` | shipped |
| **The requirement is already declared for this package** | `[RequiresPackage("clioprocessbuilder", …)]` at `CreateBusinessProcessCommand.cs:14`, `ModifyBusinessProcessCommand.cs:14`, `DescribeProcessCommand.cs:17`, `ListUserTasksCommand.cs:15`, `ValidateProcessGraphTool.cs:101` | shipped (presence-only) |
| Routes to the service | `clio/Common/ServiceUrlBuilder.cs:165-180, 287-290` — `KnownRoute.BuildProcess=49 … ModifyProcess=52` → `/rest/ProcessDesignService/*` | shipped |
| Reflection lock-in tests for the above | `clio.tests/RemoteCommandCliogateTests.cs:152-211` (`ProcessDesignerRequiresPackageAttributeTests`) | shipped |
| Bundled-package install template | `clio/Command/InstallGateCommand.cs` + `clio/clio.csproj:85-88` + `clio/cliogate/*.gz` | shipped (for cliogate) |

**What is missing is only:** the renamed package, a bundled `.gz`, an install command to make the
existing Hint actionable, and a version floor.

**Two caveats on the detection engine — do not state them the loose way:**

- *"No chicken-and-egg"* is true only about cliogate. The read still needs **DataService read rights on
  `SysPackage`**, which this repo's own `AGENTS.md:16-17` lists as a restricted-NUI-security object. On
  an environment where that read is denied, `GetPackages()` throws — and the two surfaces classify that
  differently: the CLI catches it and returns exit 1 with a readable message
  (`Program.cs:697-703`), while MCP falls through to `catch(Exception) → FromException` and reports an
  **auth problem as a clio bug**.
- *Detection is version-parse-dependent.* `PackageInfo.Version` is left `null` when
  `SysPackage.Version` does not parse (`PackageInfo.cs:21-23`), and `GetInstalledVersion` ends in
  `.MinBy(p => p.Version)?.Version` (`RequiredPackageChecker.cs:157-162`) — so a **matched row with an
  empty/unparseable version reports `IsInstalled == false`**, forever. No test covers this: the test
  helper always builds a parseable version (`RequiredPackageCheckerTests.cs:30-41`). See P4.0.

### 1.1 Correction to one framing in the request

> "clio должно уметь определить что функциональность требует этот пакет и предлагает клиенту поставить его"

Detection is done. For "offer", the shape is dictated by which surface the consumers live on:
**all five process-builder consumers are MCP-only** — none carries a `[Verb]` — so there is no CLI/TTY
surface on which a `[Y/N]` prompt could ever fire. `RealInteractiveConsole` additionally returns
`false` **without writing anything** when stdin is redirected (i.e. always, under MCP), and it is the
only `IInteractiveConsole` registered in production (`BindingsModule.cs:276-277`) — so an interactive
prompt would be a *silent* no-op exactly where the user most needs the explanation.

There is **no interactive-offer mechanism** in clio. There **is**, however, a structured MCP offer
idiom already shipped: `McpDurableCallToolHandler` answers a write-capable long-tail call by name with
a `CodeConfirmationRequired` + retry-via-`clio-run` envelope (`:59, :113-127, :220-256`).
`install-gate` falls into exactly that bucket (`ReadOnly=false`, non-resident).

So "offer" is delivered the way cliogate delivers it, which is what "похоже как работает пакет
cliogate" means in code:

1. the gate refuses the call with a structured, actionable message;
2. the `Hint` names the exact remediation command (`clio install-process-builder -e <env>`);
3. the MCP guidance (`deploy-lifecycle`, `process-modeling`) tells the agent to run it on that failure;
4. `get-tool-contract` carries the install tool's contract so the agent can call it.

**Step 1 was broken and is now FIXED — see P4.5c (done 2026-08-04).** The MCP refusal went
through `CommandExecutionResult.FromError` (`BaseTool.cs:177-179`), which is exit **-1**, documented in
that very file as "an *unexpected runtime failure*", with `FromValidationError` (exit 1) documented as
the factory for "a *refused precondition*". `docs/McpCapabilityMap.md:261-264` then teaches agents that
`-1` means "clio itself failed, retrying the same call won't help". **An agent obeying its own guidance
will therefore not install-and-retry** — the offer chain dies at the first link. (The capability map at
`:260` separately claims exit 1 covers "a required package not installed"; code wins, the doc is wrong.)

An interactive `[Y/N]` prompt and auto-install stay deferred (§7). Whether the refusal should also
adopt the `CodeConfirmationRequired` envelope rather than a plain validation error is an open question
(§11.6).

---

## 2. P0 — Rename in the GHE repo (`cli-process-builder`)

Repo: `C:\Projects\workspace\ProcessBuilder` → `creatio.ghe.com/engineering/cli-process-builder`.
Branch: `feature/ENG-94385-rename-crt-business-process-builder`.

### 2.1 Rename inventory (measured, not estimated)

| Target | From | To | Count |
|---|---|---|---|
| Package folder | `packages/clioprocessbuilder/` | `packages/CrtProcessBuilder/` | 1 dir |
| descriptor `Name` | `clioprocessbuilder` | `CrtProcessBuilder` | 1 |
| descriptor `ProjectPath` | `Files/clioprocessbuilder.csproj` | `Files/CrtProcessBuilder.csproj` | 1 |
| descriptor `UId` | `f100e6d2-…-41fce76a538d` | **UNCHANGED** | — |
| csproj file + `.DotSettings` | `clioprocessbuilder.csproj` | `CrtProcessBuilder.csproj` | 2 files |
| `RootNamespace` | `ClioProcessBuilder` | `CrtProcessBuilder` | 1 |
| AssemblyName | implicit (= project name) → `clioprocessbuilder.dll` | `CrtProcessBuilder.dll` | implicit |
| C# `namespace ClioProcessBuilder` | | `namespace CrtProcessBuilder` | 68 files |
| C# `namespace ClioProcessBuilder.EntryPoints.WebService` | | `CrtProcessBuilder.EntryPoints.WebService` | 1 file |
| Composition-root class `ClioProcessBuilderApp` | | `CrtProcessBuilderApp` | class + call sites |
| Test project | `tests/clioprocessbuilder/clioprocessbuilder.Tests.csproj` | `tests/CrtProcessBuilder/CrtProcessBuilder.Tests.csproj` | 1 dir + 1 proj |
| Test `namespace clioprocessbuilder.Tests` | | `CrtProcessBuilder.Tests` | 37 files |
| `MainSolution.slnx` entries | | updated paths | 2 |
| `.clio/workspaceSettings.json` | | updated package name | 1 |
| `docs/process-builder-architecture.md` / `.puml` | | updated names | 12 refs |
| `.gitignore` | | updated path glob | 1 |
| `.codex/workspace-diary.md` | historical entries | **append-only, do NOT rewrite** | 73 refs left as-is |

**`InternalsVisibleTo` is automatic:** the csproj uses `<_Parameter1>$(MSBuildProjectName).Tests</_Parameter1>`,
so renaming the project file changes the friend assembly to `CrtProcessBuilder.Tests` — the
test project's assembly name **must** be renamed in the same commit or every `internal`-touching test
breaks.

**The assembly rename is a platform requirement, not a convention.** `FileContentStorage.GetAssemblyName(packageName)`
resolves the DLL the platform loads as `<packageName>.dll`, under `Files/Bin` (net472) or
`Files/Bin/netstandard` (.NET — `UseSeparateDirectoryToLoadPackageAssemblies` defaults to `true`).
The csproj declares **no** `AssemblyName`, so the assembly name equals the csproj **file** name.
Therefore: package `Name`, csproj file name, produced DLL name and descriptor `ProjectPath` must move
together, or the platform will look for `CrtProcessBuilder.dll` and find `clioprocessbuilder.dll`.

### 2.2 UId policy — keep it, and know why

`PackageDBStorage.SavePackageDescriptor` fetches the `SysPackage` row **by UId** (+ `SysWorkspace`)
and then assigns `SysPackage.Name = package.Name`. So:

- **Keep `UId = f100e6d2-3cd0-a1d8-fbc0-41fce76a538d`** → installing the renamed package is an
  in-place upgrade that renames the existing row.
- **Changing the UId creates a SECOND package.** The old one stays installed, and two assemblies then
  declare a `ProcessDesignService` type. `CustomServicesParser` **silently skips duplicate service
  names**, so the route can bind the *stale* assembly — a nondeterministic, hard-to-diagnose failure.

**And the duplicate is silent, not loud.** `CustomServicesParser.ParseTypes` does
`if (services.ContainsKey(serviceName)) continue;` — **first registration wins**, selected by
`SysWorkspace.InitWorkspaceAssemblies` enumeration order. If both DLLs load, clio's four routes still
return 200 and `[RequiresPackage]` still passes: the symptom is **silently running the stale
assembly**, not an error. The same duplication also hits the `RefAssemblyMarker.All` DI bindings the
csproj's `PackageReferenceAssemblyAttribute("All")` produces.

**Worse: the rename may not persist at all.** `PackageDBStorage.SavePackageDescriptor` returns the
existing Id early when `!GetIsPackageDescriptorModified(package)`, and that predicate keys **only** off
`package.State` — it never diffs `Name` or `ProjectPath`. Nobody has established which `State` a
package loaded from a `.gz` install receives. If it lands in a state the predicate treats as
unmodified, the stand keeps `SysPackage.Name = clioprocessbuilder` while clio's gate looks for the new
name → **"install succeeded, feature still refused."**

These make TC-G-2 a **hard blocker, run early on a disposable stand**, not a closing check. It must
assert, by `SELECT Id, Name, UId, Version, ProjectPath FROM SysPackage WHERE UId = 'f100e6d2-…'`
before and after install:

1. the row is **renamed**, not duplicated, and `ProjectPath` follows;
2. no orphaned `Terrasoft.Configuration/Pkg/clioprocessbuilder/` with a stale
   `clioprocessbuilder.dll` — the only removal API found is
   `Package.RemoveFileContentDescriptor`; **nothing deletes the folder**. It is inert while assembly
   discovery is DB-driven, but on a file-system-design-mode stand the orphan is a re-registration
   candidate → the silent-takeover hazard above;
3. no unique-index conflict on `SysPackage.Name`; `SysPackageInInstalledApp` follows.

**Casing is load-bearing and Windows will not catch a mistake.** `FileContentStorage.GetAssemblyName`
concatenates the descriptor `Name` verbatim and `BinAssemblyExists` uses `File.Exists`, which is
**case-sensitive on Linux/.NET stands**. The current all-lowercase `clioprocessbuilder` is accidentally
immune to case drift; `CrtProcessBuilder` requires byte-exact agreement across four places —
descriptor `Name`, the csproj **file** name, the archive entry path, and descriptor `ProjectPath`. A
green net472 test on Windows will not reproduce a mismatch that breaks every containerised .NET stand.
Fix the literal string once, and verify it by listing the produced archive's entries (TC-R-5).

### 2.3 The REST route does not move — confirmed from platform source

`Global.asax.cs` builds `new ServiceRoute($"rest/{routePrefix}")`, where `routePrefix` is
`ServiceContractAttribute.Name` when set, otherwise `serviceImplType.Name`
(`CustomServicesParser.ParseTypes`; the same rule in `ServiceRoutesBuilder`).
`ProcessDesignService` carries a **bare** `[ServiceContract]`, so
`/rest/ProcessDesignService/{BuildProcess,ModifyProcess,ListUserTasks,DescribeProcess}` — and
therefore all four `KnownRoute` entries and every clio command already wired to them — survive any
package, assembly or namespace rename. The only registration hook is the assembly-level
`PackageReferenceAssemblyAttribute("All")` already in the csproj (consumed via
`RefAssemblyMarker.ServiceRoutes` / `GetServiceRoutedOnly()`), which is name-independent.

**Do not rename the service class.** TC-R-3 still proves the route empirically after the rename.

### 2.4 The local build is a development tool, not part of delivery

The csproj is **not** multi-targeted in one pass: `TargetFramework = $(CoreTargetFramework)`, defaulting
to `net472`, with `Choose` blocks routing `net472` → `Files/Bin/` and `netstandard2.0` →
`Files/Bin/netstandard/`. Configuration names are **not** `Debug`/`Release` — they select
`.build-props/env.$(Configuration).props`, which supplies `$(CoreLibPath)` for ~45 `Terrasoft.*`
`HintPath` references:

| Configuration | `CoreLibPath` source | State in this checkout |
|---|---|---|
| `dev-nf` (net472) | `.application/net-framework/core-bin` → symlink into a deployed Creatio's `Terrasoft.WebApp/bin` | works |
| `dev-n8` (netstandard2.0) | `.application/net-core` | not wired — never produced here |

**Under the source-only decision none of this is on the delivery path.** The archive ships no assembly,
the target regenerates the csproj and picks its own target framework, so `dev-n8` never has to be
buildable locally and the earlier P0.0 prerequisite (deploy a local .NET Core Creatio) is **cancelled**.
`dev-nf` remains the development loop — it is what compiles the package for its 477 unit tests — and a
local build's output must simply be cleared before compressing (P1 step 0), or it would leak an assembly
into an archive that is supposed to have none.

### 2.5 Namespace rename — a deliberate choice, not a requirement

The platform does **not** care about the C# namespace: route, assembly loading and package identity
are all namespace-independent. Renaming `ClioProcessBuilder` → `CrtProcessBuilder` across 69
package files + 37 test files is therefore optional.

**Do it anyway.** Leaving it creates a permanent name mismatch in the very package we are renaming for
naming consistency, and the change is mechanical and fully covered by the package's own unit tests.
Cost is minutes of tooling, not risk. (Recorded here because the cost/benefit was never written down.)

### 2.6 Tasks

- ~~**P0.0** stand up a local .NET 8 Creatio and wire `.application/net-core`~~ — **cancelled**: the target compiles the package, so no local netcore build is needed (§2.4, §3).
- **P0.1** Rename per the table above; `git mv` for folders/files so history follows.
- **P0.2** Rename `ClioProcessBuilderApp` → `CrtProcessBuilderApp` and its references.
- **P0.3** Rename the test project + assembly; confirm `InternalsVisibleTo` resolves.
- **P0.4** Build **both** configurations (`dev-nf`, `dev-n8`); unit tests green.
- **P0.5** Update the repo's own docs + diary entry (append).
- **P0.6** Housekeeping surfaced by the rename sweep, decide per item: empty `tests/UnitTests.slnx`
  and `tasks/*.cmd` pointing at a nonexistent `.solution/CreatioPackages.slnx` (clio-template
  leftovers) — fix, regenerate, or delete.
- **P0.7** PR into `main` with the review gates that repo requires.

---

## 3. DECISION (2026-08-05): ship SOURCE ONLY, compiled by the target

**Chosen delivery shape:** the archive carries `descriptor.json`, the package `.csproj` + build props,
`Files/Libs/*.dll` (third-party deps), all `Files/src/**` sources, and **one empty Source Code schema**
(`CrtProcessBuilderCompileMarker`) whose presence puts the package into the target's configuration
build. **No compiled assembly, no `Files/Bin`.** Descriptor stays `Type: 1` with
`ProjectPath = Files/CrtProcessBuilder.csproj`.

Validated on both runtimes (§8.1 and P1.11): .NET Framework 4.8 and .NET 8.0.29, through both the
Application Hub and `push-pkg`, verified by the service answering with the full 23-task catalog.

**What this decision buys**

- One artifact for every runtime — the platform regenerates the csproj and picks the target framework
  per host (`NetStandardCompatibilityMode = True`, `TargetFramework [net472]` observed on .NET Framework).
- **No build step in the release path at all** — no `dev-nf`/`dev-n8`, no deployed Creatio, no
  `.build-props`, no `CoreLibPath`. P0.0 is cancelled.
- Version decoupling from the core: the code compiles against the target's own `Terrasoft.*`.
- The compiled path's worst failure — shipping the wrong framework flavour, which installs, satisfies
  the name-based gate and then 404s on every call — becomes structurally impossible.
- Artifact 44 % smaller (187 KB vs 336 KB). (This line also claimed "no restart needed" — retracted, see
  the Restart row in §5.)

**What it costs — accept consciously**

- Recovery from a failed compile is an **explicit** action (`RestoreFromBackup` / `restore-configuration`),
  not a transactional rollback, so an unattended install has nobody to press it.
- The compile cost is the target's, not ours (16–35 s observed, 74 s on a slow stand) and it **recurs on
  every subsequent configuration build** on that environment.
- We no longer control the compile — the target's toolchain (SDK/Roslyn, NuGet restore, anything a
  `Directory.Build.*` in its tree injects) and its core assemblies decide whether our sources build,
  where a prebuilt DLL was immune. **Bounded much more tightly than first written, and verified:** the
  compile is per-package and standalone, so *other packages cannot reach it*. See §3.2.

  > **Retracted, kept so it is not re-derived.** An earlier revision listed "the customer's analyzer
  > settings **or a colliding type name in another package**" and "a broken configuration build on their
  > side" as exposures. The last two are false — §3.2 shows the regenerated project compiles only our own
  > sources and does not reference `Terrasoft.Configuration` or any other package.
- We cannot test **the binary the customer will run**, because it does not exist until they install.
  What we test is a *different compilation of the same sources* by a different toolchain against a
  different core, so the residual risk is an untested (toolchain × core version) combination rather than
  untested code. Two things bound it, and neither is theoretical:
  - **Install-and-verify is testable and is tested** — `InstallProcessBuilderToolE2ETests` installs the
    bundled archive on a live stand through the real MCP path and asserts the outcome (P4.12).
  - **Variant A only removes one axis of this, not the problem.** Its DLL is fixed, but the core that
    LOADS it is still the customer's, so the combination is untested there too. Compiled delivery pins
    the compiler; it does not pin the runtime.
  - And the failure directions differ in our favour: a compile failure here is **loud** (the install
    fails, and the outcome check catches it), whereas variant A's wrong-flavour DLL is **silent** until
    the first call 404s.

  > **Retracted:** the first revision said "we can never test the bits that actually run; testing shifts
  > to representative platform versions." The first clause is false as written and the second undersold
  > the mitigation — the outcome check runs on EVERY real install at EVERY customer, which is continuous
  > verification rather than CI sampling.
- **The marker schema becomes load-bearing and fails silently if lost** — without it the package
  installs, never compiles, and the gate reports it present while every call 404s. This re-opens the
  exact failure mode the decision closes, through another door.

**Three mitigations that are part of the decision, not optional extras**

1. A guard test asserting the marker schema is inside the committed archive (P2.4d).
2. A comment in the schema itself explaining why an empty class ships forever.
3. **`install-process-builder` verifies the OUTCOME, not the install call** — after installing it calls
   `ListUserTasks` and fails if the service does not answer (P3.6). This is the single most valuable
   mitigation: it makes "installed but never compiled" loud on the one path that has no restore button.

**Ranked AFTER the mitigations** (raised by the reporter, who pushed back on the marker schema being
called the main cost — correctly). The list above states each cost *before* mitigation; what remains is:

| Cost | Residual |
|---|---|
| Compile time | **Low.** The compile is 2.5–12.7 s (one package, not the configuration); the rest of the phase is csproj regeneration + `DownloadSources` + restore, inside a one-off 71–78 s install |
| Lost marker schema | **Low — guarded at the delivery boundary.** P2.4d asserts the schema is inside the committed `.gz` and runs on every clio build, so any route by which it could leave the artifact ends in a failing test before release. It was singled out for its uniquely bad failure *signature* (the gate reports the package present while every call 404s), not for its likelihood. **Do not drop the guard on the grounds that the schema is obviously load-bearing — that reasoning is what the guard exists to survive** |
| Untestable customer binary | **Low.** Verified continuously rather than sampled — the outcome check runs on every real install |
| No control over the target's toolchain | **Largest residual, still mild.** The only cost we neither control nor can pre-test with a test we own; we would hear it from a customer. The failure is LOUD (the install fails) and the environment is restorable |
| Non-transactional recovery | **Low but real.** Needs someone to act; fine interactively, a gap unattended |

That every remaining cost lands on "low, and loud when it fails" is the argument — not any single row.

**Explicitly not doing:** converting the 69 sources into Source Code schemas. The current shape —
plain files compiled through the package's own csproj — is what was validated; moving them would be a
large change with no benefit.

### 3.1 Superseded, in one paragraph: the one-vs-two-archive question

Before source-only was validated, the open question was whether to mirror cliogate's two-archive split
(`cliogate.gz` + `cliogate_netcore.gz`, differing only in descriptor `Name`/`AssemblyName`) or ship one
archive carrying both `Files/Bin` and `Files/Bin/netstandard`. The question no longer has a subject:
**the archive contains no assembly at all**, so there is nothing to split per framework, and the
`PackageAliases` entry, the `IsNetCore` branch in `GetPackagePath()` and the per-flavour descriptor flip
are all unnecessary. Two facts from that analysis remain worth keeping: cliogate's split is a *clio-side
naming choice* rather than a platform requirement (both its archives carry the same UId and both ship a
`netstandard` subfolder), and this package is `Type: 1` with a `ProjectPath` whereas cliogate is
`Type: 0` with none — so descriptor authoring was never a cliogate copy.

### 3.2 The compile is per-package and standalone — measured, not assumed

Raised by the reporter against the cost ledger above: *installing the package does not compile the whole
configuration, only this package.* Correct, and it narrows the "we no longer control the compile" cost
enough to be worth writing down precisely.

From the 16:44 run on `studioenu-15832585` (`Build.log`):

```
BuildProjects started. force=False; isNeedCompileAllPackages=False; …
  allPackages.Count()=177; packagesToCompile.Count()=1;
  sortedStandalonePackagesToCompile.Count()=1; packagesToCompile=[CrtProcessBuilder].
Package compilation summary | skipped (no compilable schemas): 0 [<none>] | compiled: 1 [CrtProcessBuilder]
```

And from the project the server itself regenerated at
`…\Terrasoft.Configuration\Pkg\CrtProcessBuilder\Files\CrtProcessBuilder.csproj`:

| Property | Value | Consequence |
|---|---|---|
| `<Compile Include>` | **2** — `Schemas/**/*.cs` and `Autogenerated/Src/**/*.cs`, both `$(RelativeCurrentPkgFolderPath)`-scoped | only OUR sources are in the compilation |
| `<ProjectReference>` | **0** | no other package's project participates |
| `Terrasoft.Configuration` | **not referenced at all** | the shared configuration assembly is not even on our reference list |
| `<Reference Include>` | 61, resolved through `$(CoreLibPath)`, plus our own `Assemblies/*/*.dll` and `Autogenerated/Lib/*.dll` | the dependency surface is the platform CORE and our own libs |

So the exposure is **the target's toolchain and its core assemblies** — not other packages. Two claims
from the first draft of the ledger are therefore false and were retracted above:

- *"a colliding type name in another package can break our install"* — it cannot; that package's code is
  not in our compilation and our output is its own assembly (`Files/Bin/CrtProcessBuilder.dll`).
- *"a broken configuration build on their side can break our install"* — it cannot; no configuration
  build is triggered (`isNeedCompileAllPackages=False`, 1 of 177 compiled) and we do not reference its output.

**Do not misread `DownloadSources` in the log.** It runs for ~170 packages, which looks like a
whole-configuration operation. It is not: it materialises autogenerated sources into each package's
`Autogenerated/Src`, and only `CrtProcessBuilder` is then compiled. Anyone re-reading these logs will hit
this and should not conclude the install rebuilds the configuration.

**This also cuts the other way, in our favour:** because the build is scoped to one package, our install
cannot break anyone else's package either — which is a second, independent answer to the
inverted-blast-radius objection in §4 (the first being the backup + `RestoreFromBackup`).

---

## 4. P1 — Producing the artifact (there is no build step)

Deliverable: a short runbook in the process-builder repo **and** a mirror in clio, because whoever
refreshes the bundled `.gz` works in the clio repo.

Under the source-only decision (§3) the whole build stage is gone. The sources **are** the payload, the
target compiles them, and producing the artifact needs nothing but `clio` and a checkout — no deployed
Creatio, no `.build-props`, no `CoreLibPath`, no SDK pinning, no per-framework leg. That also retires a
class of traps this plan hit while the compiled shape was still assumed: `dev-nf` silently matching
neither optimization PropertyGroup, an IDE rebuilding into `Files/Bin` between build and compress, and
"which SDK produces the artifact".

```bash
# 0. There must be NO compiled output in the archive. Files/Bin is the csproj's unconditional
#    OutputPath, so any local or IDE build leaves one behind — clear it before compressing.
rm -rf packages/CrtProcessBuilder/Files/Bin packages/CrtProcessBuilder/Files/obj

# 1. Only when the version actually changes — MUST stay 4-part (see P1.1)
clio set-pkg-version ./packages/CrtProcessBuilder --PackageVersion <X.Y.Z.W>

# 2. `clio compress -d` does NOT create the destination directory (see P1.2)
mkdir -p <clio-repo>/clio/CrtProcessBuilder

# 3. Compress  (verb: generate-pkg-zip, aliases comp-pkg / compress)
clio compress ./packages/CrtProcessBuilder --skip-pdb   -d <clio-repo>/clio/CrtProcessBuilder/CrtProcessBuilder.gz
```

Then commit the `.gz` in clio and let the guard tests (§5.4) check it. They assert exactly the
properties a human reviewer cannot see in a binary diff: the descriptor triple, the presence of the
compile-marker schema, and that the sources are in there.

**The local build is still useful — just not for delivery.** `dotnet build … -c dev-nf` and the package's
unit tests remain the development loop; they simply no longer produce anything that ships.

Traps that survive the simplification:

- **P1.1 A version floor must be 4-part.** `RequiredPackageChecker.IsCompatible` parses the required
  version with `Version.TryParse` and compares `installedVersion >= new PackageVersion(required, "")`.
  `System.Version("1.0.0")` yields `Revision = -1`, so a 4-part floor compares **greater** than a 3-part
  installed version and the gate would **refuse a correctly installed package**. The descriptor is
  `1.0.0.0` today; keep both sides 4-part.
- **P1.2 `clio compress -d` does not create the destination directory.** `CompressionUtilities.PackToGZip`
  opens `FileMode.Create` with no `Directory.CreateDirectory` — the only `CreateDirectory` in
  `PackageArchiver.cs` is on the *unpack* path. First-time production throws `DirectoryNotFoundException`,
  and since git does not track empty directories the folder will not exist in a fresh clone until the
  `.gz` is committed.
- **P1.4 Nothing validates the descriptor against the filename.** `PackageArchiver.Pack` never opens
  `descriptor.json` and `IPackageInstaller.Install` merely uploads the file. A `.gz` *named*
  `CrtProcessBuilder.gz` whose descriptor said something else would install, report success, and then the
  gate would report the package missing **forever**. This is what the P2.4b guard test exists for.

  > **Retracted claim, kept as a warning against re-deriving it.** An earlier revision asserted that
  > Creatio validates the archive name against the descriptor `Name`, on the strength of the Application
  > Hub refusing an archive with *"The name of your \*.gz archive does not match the name specified in the
  > descriptor.json file"*. **That message is misleading and the inference was wrong.** The client-side
  > condition (bundle `3584.*.js`, `_openConfirmDialogWithCorrectApp`) is:
  > ```js
  > const t = e.appInstallInfos || [];
  > if (t.length === 0) { … instant("AppInstallInfoDialog.OutOfSyncNames") … }
  > ```
  > — it fires when the server found **no APPLICATION** in the archive, whatever the names. Renaming the
  > file changed nothing.

- **P1.6 Archive hygiene lives in `.clio/clioignore`, not `.gitignore`.** `clio compress` honours
  `.clio/clioignore` (resolved from `packagePath.Parent.Parent/.clio/`), which is why `Files/.idea/**`
  reached the archive despite being git-ignored. `**/.idea` and `**/*.pdb` are now in that file, and
  `--skip-pdb` is belt-and-braces. Note the ignore file **already** denylists
  `Microsoft.Extensions.DependencyInjection.dll`, `Microsoft.Extensions.Http.dll` and
  `System.Text.Json.dll`, so those never ship even if a local build drops them into `Files/Bin` —
  the target uses its own.
- **P1.9 Provenance.** `ProcessBuilder/.gitignore` excludes `*.gz`, so the artifact can never be
  committed where it is produced; the hop into clio is a manual step. Record the producing commit SHA in
  the clio commit message so "which source produced this archive" has an answer.

- **P1.11 Both install surfaces accept the source-only archive.** `DefaultPackageExtractor.Extract`
  treats any `*.gz` as a single
  package and merely **copies** it into the staging directory (it does not even decompress it);
  `PackageStorage`, initialised with `SetLoadOnlyFileContentOptions()`, then reads the package out of
  that archive. `GetAppsInstallInfoWithoutAppDescriptor` explicitly synthesises one `AppInstallInfo`
  per package that has no app descriptor. So a plain package archive is a supported input, and the
  reporter confirms this package has always been installed that way.
  **RESOLVED (2026-08-05): the source-only archive installs on BOTH readers and the service works.**
  Every run below was verified by *outcome* — `/rest/ProcessDesignService/ListUserTasks` returning
  `success: true` with the full 23-task catalog — not by the install dialog's own report.

  | Artifact | `Files/Bin` | schema | Reader | Stand | Time | Outcome |
  |---|---|---|---|---|---|---|
  | `CrtProcessBuilder.gz` | present | — | Hub | studioenu-15832585 | 12 s | installs |
  | `_withbin_schema/…` | present | present | Hub | studioenu-15832585 | 25 s | installs |
  | `_nobin/…` | **absent** | present | Hub | studioenu-15832585 | — | **rejected once, then the same bytes installed** |
  | `_nobin/…` | **absent** | present | **Hub** | **studioenu-15832842 (never had the package)** | **20 s** | **installs, service answers, 23 tasks** |
  | `_nobin/…` | **absent** | present | `push-pkg` | sae-m-seeenu-15832383 | 74 s (~48 s compile) | installs, service answers, `BuildProcess`→`DescribeProcess` round-trip green (~~no restart~~ — not checked in the log at the time; see the Restart row in §5) |

  The `push-pkg` run is the airtight one: the archive contained **no assembly at all**, the server
  logged `Compiling configuration dll`, and the service answered afterwards — which is only possible
  if the target compiled the assembly itself.

  **Cost: 20–74 s, and the spread is stand performance, not approach.** The same source-only archive
  cost 20 s on one stand and 74 s on another.

  **Hypotheses raised during this investigation and now dead** — recorded so they are not re-proposed:
  archive-name vs descriptor-`Name` mismatch; "the Hub cannot install packages"; "the Hub rejects an
  archive with no assembly"; server-side staging reuse (`ReuseUnzippedPackagesOnInstallApp`) making a
  later run a false positive. Each was refuted by a subsequent run.

  **One residual unknown, deliberately not swept away:** the single early rejection of `_nobin` on
  studioenu-15832585 is unexplained and was not reproducible. It sits on the reporter's manual install
  path, so if it recurs it matters — capture the exact dialog state and stand if so.

#### The server-side mechanism, confirmed from the stand's own logs

`\\ts1-infr-web04\Creatio_Logs\AutoTest\studioenu_15832842_0805\0\Log\2026_08_05\{InstallZipPackage,Build}.log`
for two independent Hub installs (13:09 and 13:36) show the whole chain. Both compiled; both succeeded.

```
InstallZipPackage.log
  13:36:11  Application install from file started. Application name: CrtProcessBuilder. Code: CrtProcessBuilder.
  13:36:16  Compiling configuration dll
  13:36:28  Configuration build started … finished
  13:36:29  Application installed successfully                      → 17.4 s total

Build.log
  13:36:16  BuildConfigurationProjects started. force=False; packagesNamesToCompile=[CrtProcessBuilder].
  13:36:18  GenerateStandalonePackageFiles - Standalone package csproj regenerated.
            PackageName=CrtProcessBuilder; PackageUId=f100e6d2-…
  13:36:23  DownloadSources Package [CrtProcessBuilder] Path […\Pkg\CrtProcessBuilder\Autogenerated\Src]
  13:36:24  SetCustomProperties - NetStandardCompatibilityMode = True
  13:36:26  Project [CrtProcessBuilder.csproj] TargetFramework [net472] CoreTargetFramework [net472]
  13:36:28  CrtProcessBuilder -> …\Pkg\CrtProcessBuilder\Files\Bin\CrtProcessBuilder.dll
            0 Warning(s)  0 Error(s)
            Parallel package compilation finish | success: True | duration: 00:00:03.8482326
```

Five consequences, each of which retires an open worry in this plan:

1. **The marker schema is what puts the package into the build.** `packagesNamesToCompile=[CrtProcessBuilder]`
   in both runs, and `DownloadSources` materialises the schema into `Autogenerated/Src`. This is exactly
   the trigger the experiment was designed around, and it works.
2. **The server REGENERATES the standalone package csproj** (`GenerateStandalonePackageFiles`). It does
   not depend on our project file's environment assumptions — which retires the P1.0/P1.3/P1.5 worries
   about `.build-props` being absent, the `CoreLibPath` fallback and the `Libs/*.dll` `HintPath`s *for
   the server-side build*. (They still govern a local build, which source-only delivery removes.)
3. **The server chooses the target framework** — `TargetFramework [net472]` on this .NET Framework host,
   alongside `NetStandardCompatibilityMode = True`. That is the strongest available evidence that a
   .NET host would select the netstandard target itself, which substantially de-risks §11.5. Still not
   directly observed on a .NET Core stand.
4. **The assembly is produced on the stand**, clean: `→ …\Files\Bin\CrtProcessBuilder.dll`,
   0 warnings, 0 errors.
5. **The pure compile is only ~2–4 s** (`duration: 00:00:02.47` and `00:00:03.85` across the two runs).
   The ~12 s "Compiling configuration dll" phase is csproj regeneration + `DownloadSources` + restore.
   The 48 s seen on sae-m-seeenu-15832383 is that same phase on a much slower stand.

**And therefore: with the marker schema present, the assembly shipped inside the archive is dead
weight** — the server rebuilds it regardless. That is what the 12 s / 25 s pair on studioenu-15832585
was already saying: adding the schema to an archive that already had a DLL cost +13 s, because it
turned a no-compile install into a compiling one.

#### The inverted-blast-radius objection is largely answered: install is backed up and restorable

This plan repeatedly warned that server-side compilation trades a contained failure (our assembly
does not load, the environment is otherwise fine) for an uncontained one (a compile error breaks the
environment's configuration build). That risk is **materially smaller than stated**, because a
configuration backup is part of the install flow and restoring it is a first-class operation:

- **Hub.** The install progress model has explicit stages
  `Validate → CreateBackup → Install → Pending → OrderAppLicense → RestartApp → RestoreFromBackup`,
  and the failure panel exposes `restoreFromBackup` alongside `getLog` (client bundle
  `3330.*.js`). The stand's log shows the backup really happening before the install:
  `Configuration backup started.` → `Configuration backup successfully created.`
- **Server.** `PackageInstallerService.svc/RestoreFromBackup` is a published endpoint
  (`IPackageInstallerService.RestoreFromBackup`), implemented by
  `ZipPackageBackupManager.RestoreFromBackup`, which restores changed packages, app dependencies,
  inactive-package state and `PackageInInstalledApp`, and records a `RestoreConfiguration` entry in the
  configuration activity log.
- **clio.** The same capability has a verb: `restore-configuration` (aliases `restore`, `rc`) —
  "Restore configuration from last backup".

So a failed compile on install is a *recoverable* event on both surfaces, by a supported operation,
not a manual repair. What remains true — and is the honest residual — is that recovery is an explicit
action rather than an automatic transaction rollback inside the install, so a failure still needs
someone to notice and act. That is a materially weaker objection than "it can brick the environment's
configuration build", which is how earlier revisions of this plan framed it.
  **Note the verification asymmetry that hid this:** the archive was written by `clio compress` and
  checked by `clio extract-pkg-zip` — a clio→clio round trip that proves nothing about Creatio's
  readers. And the two server paths are *different readers*: `push-pkg` →
  `PackageInstallerService.svc/InstallPackage` (verified working) versus the Hub →
  `AppInstallerService` → `DefaultPackageExtractor` + `PackageStorage` (failing). Any future claim that
  an artifact "is valid" must say which reader validated it.
  **This does not block the ticket:** `install-process-builder` uses `IPackageInstaller.Install`, i.e.
  the `push-pkg` reader, which installs both the compiled and the source-only artifact successfully.
  It does block the reporter's manual Hub workflow, so it needs an answer before the source-only
  approach can be adopted.

Runbook must also state:

- **P1.5** the prerequisites: a deployed net472 Creatio wired to `.application/net-framework/core-bin`
  **and** a .NET 8 one wired to `.application/net-core`; plus **the required .NET SDK**. There is no
  `global.json` in either repo, and `clio.csproj:5-6` only adds `net10.0` when `NETCoreSdkVersion >= 10.0`
  while `build.ps1:2` hard-pins `clio/bin/Release/net10.0/clio.dll` — on an SDK-9 machine that bootstrap
  `clio.dll` never exists and every `set-pkg-version`/`compress` call fails. Under this plan the producer
  is a human on an arbitrary machine, so pin the SDK explicitly.
- **P1.6** artifact hygiene, encoded in **`.clio/clioignore`** — not `.gitignore`. `clio compress` honours
  `.clio/clioignore` (resolved from `packagePath.Parent.Parent/.clio/clioignore`), which is precisely why
  `Files/.idea/**` reaches the archive today despite being git-ignored. Current warts to exclude:
  7 `Files/.idea/**` entries including `workspace.xml`, and the 478 KB `.pdb` (`--skip-pdb`). Decide and
  record whether `Files/Libs/*.dll` must ship — it is the csproj `HintPath` source, so probably yes, at
  the cost of duplicating those two DLLs.
- **P1.7** an explicit decision that the archive publicly redistributes the package's **69
  `Files/src/**/*.cs`** source files through clio's public NuGet package. For a Creatio package that is
  normal content (file-system design mode needs it) — but it should be a decision, not an accident.
- **P1.8** the post-build assertion: unpack the produced `.gz` and confirm `descriptor.json` `Name`
  (byte-exact casing), `UId`, 4-part `PackageVersion`, **and** the presence of both
  `Files/Bin/CrtProcessBuilder.dll` and `Files/Bin/netstandard/CrtProcessBuilder.dll`
  plus `ATF.Repository.dll` / `ErrorOr.dll` in both.
- **P1.9 Provenance.** `ProcessBuilder/.gitignore:39` is `*.gz`, so the artifact **can never be committed
  where it is produced** — the hop into clio is an unscripted, unrecorded manual step. Record the
  producing commit SHA, the build configurations used, and the hygiene flags, either in the clio commit
  message or a small `provenance.json` next to the `.gz`. Without it there is no way to answer "which
  source produced this artifact".
- **P1.10** the version scheme and who owns bumps; a pointer to ENG-92113 as the automation successor.

**Local-tree staleness warning for the producer:** `CopyToOutputDirectory=Always` only refreshes the TFM
you actually rebuild, so `clio/bin/Release/net10.0/cliogate/cliogate.gz` is currently *older* than git's
copy while `net8.0` is current. Since `build.ps1` bootstraps from exactly that `net10.0` tree, a producer
can run `set-pkg-version`/`compress` with a clio whose own bundled payload is stale. (This is a local
inconvenience only — `dotnet pack --no-build` was tested and does carry the fresh bytes, so it is **not**
a release defect.)

---

## 5. P2 — Bundle the `.gz` into clio · **DONE** (commit `8e549c96`)

### 5.1 Placement, mirroring `clio/cliogate/`

```
clio/CrtProcessBuilder/CrtProcessBuilder.gz     (committed binary, 187,531 bytes, source only)
```

`clio/clio.csproj`, next to the cliogate entry:

```xml
<None Include="CrtProcessBuilder\**"/>
<Content Include="CrtProcessBuilder\**" Pack="false">
  <CopyToOutputDirectory>Always</CopyToOutputDirectory>
</Content>
```

`Content` + `CopyToOutputDirectory=Always` puts the payload at `tools/{tfm}/any/<dir>/` — exactly where
`IWorkingDirectoriesProvider.ExecutingDirectory` resolves at runtime. `NU5100` is already globally
suppressed (`clio.csproj:35`), so no pack warning.

**`Pack="false"` was measured on a real `dotnet pack` of this project**, not inferred from a repro:

| | nupkg entries | payload |
|---|---|---|
| ours, `Pack="false"` | **2** — `tools/{net8.0,net10.0}/any/` only | 375 KB |
| cliogate, no attribute | 5 — plus `content/` and two `contentFiles/{tfm}/` | 7.15 MB |

The two `tools/` copies are the only ones the install code ever reads; the other three would be dead
weight in a ~96 MB package. (Keep `NU5128` in mind: it is *not* in `NoWarn`. Harmless while cliogate
keeps its own `contentFiles` entries; relevant only if all `contentFiles` are ever removed.)

Also verified: the release workflow (`.github/workflows/reliase-to-nuget.yml:92-104,186`) runs only
`dotnet build` → `dotnet pack` → `nuget push` and **never** `build.ps1`. The `.gz` therefore **must**
be committed to git or it is silently absent from the shipped tool.

**Add the payload assertion to an existing hook.** That workflow already has a *"Verify packaged tool
version"* step (~`:106-175`) which does `dotnet tool install clio --tool-path ./tool-smoke --version $version`
then `clio info --clio` with a regex assert. Extend it with a `Test-Path` on
`tool-smoke/.store/clio/$version/clio/$version/tools/*/any/CrtProcessBuilder/CrtProcessBuilder.gz`.
This is the cheapest place to catch a payload that failed to pack.

### 5.2 Do not couple clio's build to the package source

`build.ps1` builds `cliogate` from the in-repo `cliogate/` folder. The process-builder package lives
in another repo — **`build.ps1` must not gain a build step for it**. It is bundled pre-built. (TC-D-7.)

### 5.3 One source of truth for the bundled version

The cliogate version story is three-way inconsistent today and must not be copied:
`InfoCommand._gateVersion` const `2.0.0.44` (regex-rewritten by `build.ps1`), `cliogate/descriptor.json`
`2.0.0.44`, and `clio/cliogate/version.txt` `1.1.1.2` — **stale, written by nobody**, with exactly one
reader (`Program.CheckApiVersion`), which means its upgrade-warning branch can never fire.

For the new package, introduce **one** constant and derive everything from it:

- **P2.3a** `clio/Common/BundledPackages.cs` (or a const on the new command) —
  `public const string ProcessBuilderPackageName = "CrtProcessBuilder";` and
  `public const string ProcessBuilderVersion = "<X.Y.Z.W>";`
- **P2.3b** the `[RequiresPackage]` version argument references that const (see §6.2);
- **P2.3c** `clio info` surfaces it (mirror the `--gate` branch in `InfoCommand.cs:44,64-67,76`);
- **P2.3d** **no `version.txt`** for this package — it would reproduce the stale-file bug.

### 5.4 Guard tests — DONE (`clio.tests/Common/BundledProcessBuilderPackageTests.cs`)

Nothing in the repo asserted that a bundled `.gz` exists in build output or in the nupkg, and
`BasePackageInstaller.InternalInstall` has no existence pre-check, so a missing artifact surfaced only as
a generic install failure at runtime. Four tests now close that, and each one covers a failure that is
otherwise **silent** — the package installs, the name-based gate reports it present, and only a
`ProcessDesignService` call reveals the problem, on a customer environment after release:

- **P2.4a** the archive exists at `<output>/CrtProcessBuilder/CrtProcessBuilder.gz`, i.e. exactly where
  `IWorkingDirectoriesProvider.ExecutingDirectory` resolves it. A lost `Content` entry or an uncommitted
  file fails nowhere else. (The path is derived from `AppContext.BaseDirectory`, so it is TFM-agnostic —
  a test hardcoding both `net8.0` and `net10.0` would break on an SDK-9 build, since `clio.csproj:5-6`
  collapses `TargetFrameworks` to `net8.0` only and no `global.json` pins the SDK.)
- **P2.4b** the descriptor inside the archive matches `BundledPackages` **byte-exactly** on `Name`
  (casing matters, §2.2), carries the fixed `UId`, and its `PackageVersion` equals
  `ProcessBuilderVersion`. This is the only guard against the descriptor-vs-filename divergence of P1.4,
  and it turns a forgotten version bump into a red test instead of a silent field bug.
- **P2.4d** the archive contains the `CrtProcessBuilderCompileMarker` Source Code schema and a
  `SourceCodeSchemaManager` manager name. **This is the load-bearing one under source-only delivery**:
  lose that schema and the package installs, is never compiled, produces no assembly at all, and the gate
  still reports it present. It is the mitigation the §3 decision names as non-optional.
- **P2.4e** the archive carries the sources and `class ProcessDesignService` — now that no assembly
  ships, the sources *are* the payload.

*Retired: the earlier P2.4c, which would have asserted a per-framework DLL inside the archive. There is
no assembly in the archive at all, so it has no subject.*

### 5.5 Housekeeping

- **P2.5a DONE.** `*.gz binary` added to `.gitattributes`. The existing artifacts survived
  `core.autocrlf=true` **only** because git's content sniffing finds a NUL at byte offset 3 (the gzip FLG
  byte) — luck, not design, and inconsistent with a repo that already pins byte-compared fixtures there.
- **P2.5b Measured.** With `Pack="false"` the artifact adds 375 KB across the two `tools/` TFMs. For
  comparison, `cliogate.gz` is 1,430,162 bytes in the git blob and pays 5× duplication. The nupkg is
  ~96 MiB against nuget.org's 250 MB limit — headroom either way, but no reason to pay it.
- **P2.5c Still open, by design of the simplified scope.** No CI signal detects source-vs-artifact drift:
  `build.yml:31-40` filters `clio-src` on `clio/**` and `cliogate` on `cliogate/**`, so a committed `.gz`
  under `clio/` reads as "clio source" and nothing correlates a package-source change with a stale
  committed artifact. P1.9's provenance record is the available mitigation; ENG-92113 is the real fix.
- **P2.5d Optional, cheap, not done.** The release workflow already has a *"Verify packaged tool version"*
  step (`reliase-to-nuget.yml` ~`:106-175`) that installs the packed tool and runs `clio info --clio`.
  Adding a `Test-Path` on
  `tool-smoke/.store/clio/$version/clio/$version/tools/*/any/CrtProcessBuilder/CrtProcessBuilder.gz`
  would catch a payload that failed to pack, at the last moment before publishing.

---

## 6. P3/P4 — `install-process-builder` and the detect→offer path

### 6.1 The command · **DONE** (commits `a1718541`, reworked in `4017d42c`)

`clio/Command/InstallProcessBuilderCommand.cs`, modelled on `InstallGateCommand.cs`:

| Aspect | Value |
|---|---|
| Verb | `install-process-builder`, aliases `update-process-builder`, `installprocessbuilder`. *Note: `install-clioprocessbuilder` appears in a unit-test throw string (`ValidateProcessGraphToolTests.cs:223`) — a fabricated NSubstitute message, **not** a decided verb name.* |
| Options | `InstallProcessBuilderOptions : EnvironmentNameOptions` — no options of its own, and **no `[RequiresPackage]`** (see below) |
| Feature gate | **NONE — do not gate it.** See the reasoning below; this reverses the obvious first instinct. |
| Deps (all null-checked) | `EnvironmentSettings`, `IPackageInstaller`, `IWorkingDirectoriesProvider`, `IFileSystem`, `IRequiredPackageChecker`, `IApplicationClient`, `IServiceUrlBuilder`, `ILogger` |
| Path | `Path.Combine(ExecutingDirectory, BundledPackages.ProcessBuilderPackageName, …ArchiveFileName)` — **no `IsNetCore` branch**: there is no assembly in the archive to pick a flavour of (§3) |
| Pre-check | the bundled archive must exist, else a plain error — `BasePackageInstaller` has no existence pre-check of its own, so without this a distribution that failed to carry the artifact surfaces as a generic install failure |
| Short-circuit | `IRequiredPackageChecker.IsCompatible(name, version)` → log and return 0. Prevents making a healthy environment recompile the package for nothing. **Fails OPEN**: an unreachable host or a denied `SysPackage` read must not block an explicitly requested install |
| Install | `IPackageInstaller.Install(path, settings, packageInstallOptions: **null**, reportPath: null, createBackup: true)` — `null` options keeps it on the plain `/ServiceModel/PackageInstallerService.svc/InstallPackage` route; a non-null value would switch to `/rest/ClioPackageInstallerService/Install`, which **is not implemented in cliogate** |
| Settings | fresh `EnvironmentSettings` merged from the resolved one with `DeveloperModeEnabled = false`. Field-justified: on a developer-mode environment `push-pkg`'s unlock step routes through cliogate and threw, even though the package itself installed |
| **Outcome check** | after a successful install, POST `{}` to `KnownRoute.ListUserTasks` and require a parsed `ListUserTasksResult.success == true`. **Fails CLOSED** — this IS the command's contract |
| Restart | **never requested by clio — but one happens anyway, from a different place on each runtime.** ~~none; the configuration build that compiles the package also loads the result~~ — **retracted 2026-08-05** by the live command runs: net472 (`studioenu-15832585`) logged the platform recycling *itself* (`Workspace assembly changed - Run restart application`), and .NET 8 (`studionet8enu-15832863`, web02:8530) restarted because `BasePackageInstaller` calls `IApplication.Restart()` under `DeveloperModeEnabled \|\| IsNetCore`. The earlier "no restart" reading came from *the service answered afterwards*, which is consistent with a restart having happened and finished — it was never log-verified. Consequence: `IServerReadinessWaiter` between install and probe is **not** belt-and-braces, it is required (P3 rework, commit `4017d42c` → `21adbf25`) |
| On failure | `WriteError`, return 1; the catch logs `GetReadableMessageException()` **first**, then the stack (copy `InstallGateCommand`, not `PushPackageCommand`, whose catch drops the message) |
| Compile | not requested by clio — the *target* compiles the package as part of installing it |

**Why the outcome check matters more than the restart it replaced.** Because the assembly is produced by
the target rather than shipped, "installed" and "working" are genuinely different states. If the
compile-marker schema were ever lost from the archive, the package would install, never compile, and the
name-based `[RequiresPackage]` gate would still report it present while every
`/rest/ProcessDesignService/*` call failed. The Hub can recover that class of failure through its
`RestoreFromBackup` stage; this path has no such button, so the check belongs here. It is parsed rather
than pattern-matched on purpose: the interesting failure is an IIS error page from an unbound route, which
fails `JsonDocument.Parse` and is correctly read as "not answering", whereas a substring search over HTML
could accidentally match. One attempt is deliberate — the compile finishes before the install call
returns, and a 404 from an unbound route is not transient.

*Historical note: the command originally restarted the application, faithfully cloned from the cliogate
pattern before source-only delivery was validated. `IApplication` left the constructor with it.*

#### Do NOT feature-gate this command (reversal of the obvious choice)

Gating the installer under `process-designer` — which is what consistency with the rest of the BP suite
suggests — **breaks the entire offer chain**:

- `Program.cs:1446-1449` filters a gated options type **out of the verb parse array**, so the verb
  becomes "indistinguishable from a typo". The user is told by the Hint to run
  `clio install-process-builder`, and gets an unknown-verb error.
- `HelpArtifactExporter` + `ExportFeatureToggleService` omit a gated command from all generated docs,
  and `CleanLegacyMarkdownDocs` **deletes** any `docs/commands/install-process-builder.md` committed for
  it — so the remediation command the refusal names has no documentation either.

The gate exists to hide the *experimental BP feature*. The installer is a plain package-install verb
with no experimental surface of its own; hiding it removes the remediation exactly when it is needed.
**Ship it ungated**, and let it appear in the generated docs normally. Consequence to accept: the verb
(and therefore the package name) is publicly visible before the BP feature ships. Confirmed as
§11.3 — but the alternative is a refusal message that points at nothing.

#### Short-circuit when the package is already current

`InstallGateCommand` has **no** pre-check: it always installs and always restarts. Do better —
call `IRequiredPackageChecker.IsCompatible(name, version)` first and return 0 with an
"already installed at <version>" message. This avoids restarting a healthy environment for nothing,
which matters because the restart is not free (see below).

#### The installer must never gate on the package it installs

Both chokepoints (`Program.cs:411-415`, `BaseTool.cs:245-251`) run **before** dispatch, so an options
class carrying `[RequiresPackage("CrtProcessBuilder")]` would be refused by the very
requirement it exists to satisfy. Nothing in the codebase prevents this mistake; the only guard is the
`InstallGateOptions` precedent (which carries no attributes). **Add a test** asserting
`InstallProcessBuilderOptions` carries no `[RequiresPackage]`.

#### The double-restart hazard no longer applies

`BasePackageInstaller` restarts the app itself when `DeveloperModeEnabled || IsNetCore`
(`BasePackageInstaller.cs:263-272`), so an explicit `Restart()` on top of it would have made two on a
.NET Core stand — which is what `install-gate` does today. Since this command no longer restarts at all,
the hazard is gone rather than accepted.

Delivered tasks:

- **P3.1 DONE** command + options (`InstallProcessBuilderCommand.cs`).
- **P3.2 DONE** DI: `services.AddTransient<InstallProcessBuilderCommand>()` in `BindingsModule.cs`.
- **P3.3 DONE** `Program.cs`: options type in the verb-type array **and** the dispatch arm.
- **P3.4 DONE** `CommandHelpCatalog.cs`: `IntegrationsAndTools` group + a `DescriptionOverrides` entry,
  without which the description would just echo the verb.
- **P3.5 DONE** unit tests (11): install-then-verify with `DeveloperModeEnabled == false`; the resolved
  path is identical when `IsNetCore` is true (pins §3); missing bundled archive; short-circuit when
  already current, asserting the service is **not** probed; version check fails open; install failure and
  installer exception, both asserting no service probe; service returns an IIS error page; service returns
  a `success: false` envelope; and `InstallProcessBuilderOptions` carries no `[RequiresPackage]`.
- **P3.6 DONE** the outcome check (§6.1) — the mitigation the §3 decision names as non-optional.
- **P3.7 DONE** hand-authored `clio/help/en/install-process-builder.txt`; `docs/commands/…`,
  `Commands.md` and `WikiAnchors.txt` written by hand too, deliberately — see §9 P5.1 for why the
  generator could not be used.

**Not yet verified on a stand:** the full install path. Every registered environment already carries the
package, so the live run exercised only the short-circuit (`… or higher is already installed. Nothing to
do.`, exit 0, path resolved from the build output). The install-and-verify path needs an environment that
has never had the package.

### 6.2 Make the existing gate actionable (this is "detect + offer")

- **P4.0 ANSWERED — measured on `krestov-test` (net472, 8.1.3):**
  ```
  {"name":"clioprocessbuilder","version":"1.0.0","maintainer":"Creatio",
   "uId":"f100e6d2-3cd0-a1d8-fbc0-41fce76a538d"}
  ```
  Three conclusions:
  1. **`SysPackage.Version` IS populated** for this `Type=1` standalone assembly package, so
     `PackageInfo.Version` parses, `IsInstalled` returns `true`, and **a version floor is feasible**.
     (The empty-version hazard is real in the wild — `Custom`, `CrtFilterAgent`,
     `CrtIdentityManagement` all report `""` on the same stand — just not for this package.)
  2. **The installed version is 3-part `1.0.0`**, so the P1.1 arithmetic trap is confirmed live, not
     hypothetically: a `1.0.0.0` floor compares **greater** and would refuse a correctly installed
     package. `cliogate` next to it is 4-part `2.0.0.44`. **The artifact must be restamped 4-part in
     the same change that introduces the floor.**
  3. The installed `UId` matches the descriptor, so **TC-G-2's same-UId rename is testable on this very
     stand** — but it is not disposable, so provision a throwaway one for that check.
- **P4.1** Rename the requirement at all **5** sites `clioprocessbuilder` → `CrtProcessBuilder`
  and rewrite the Hint in the cliogate house style:
  `"Run 'clio install-process-builder -e <environment>' (or call the install-process-builder MCP tool) to install/update CrtProcessBuilder."`
- **P4.2** **Upgrade presence-only → versioned.** Today every process-builder requirement is
  presence-only, so an *installed-but-stale* package is undetectable — yet the clio DTOs already
  self-diagnose "an older clioprocessbuilder" in three places
  (`IProcessDescriber.cs` — `useBackgroundMode`, `direction`, `isResult`). Now that clio owns the
  bundled artifact, a floor is both meaningful and free: `[RequiresPackage(ProcessBuilderPackageName, ProcessBuilderVersion, Hint = …)]`.
  The engine already produces the right message ("…version X or higher. Install or update…").
- **P4.3** No `PackageAliases` entry (single name, §3). Add one only under the §3 fallback.
  **Do not use the alias map as an old-name transition mechanism.** `RequiresPackageAttribute` takes a
  single name, and aliasing `CrtProcessBuilder → clioprocessbuilder` would be actively harmful:
  `RequiredPackageChecker.GetInstalledVersion` picks the **lowest** version across aliases
  (fail-closed), so a lingering old `clioprocessbuilder` would permanently defeat the version floor of
  P4.2. No transition mechanism is needed anyway — **bundling makes clio and the package atomic**: a
  clio build that names `CrtProcessBuilder` ships the matching `.gz` in the same binary. The
  only interim state is a stand carrying the old package, and there the gate correctly refuses and
  points at `install-process-builder`.
- **P4.4** Update the 5 MCP tool `[Description]` sentences
  ("Requires the ProcessDesignService (clioprocessbuilder) package…") → new name **and** the install
  pointer, so the agent-facing contract matches the runtime message.
- **P4.5** Update the reflection lock-in tests: `clio.tests/RemoteCommandCliogateTests.cs:152-211`
  (`ProcessDesignerRequiresPackageAttributeTests`) — name, version floor, and the exact Hint string;
  plus `RequiresPackageAttributeTests` and any pinned-message assertion.
- **P4.5a The lock-in has a hole that would let the rename half-land with a green suite:** the fixture's
  `[Description]` says "four" options classes, but the `[TestCase]` list at `:167-170` contains only
  **three** — `ListUserTasksOptions` is **unpinned**. A rename driven by "the tests will tell me" leaves
  `ListUserTasksCommand.cs:15` on the old name, so `list-user-tasks` refuses permanently against a
  renamed stand while `RemoteCommandCliogateTests` passes. **Add the missing `[TestCase]` first**, so the
  rename is test-driven across all four, then rename.
- **P4.5b** Also rename the package literal in the non-obvious places the sweep must not miss:
  `docs/McpCapabilityMap.md:674,680` and `clio.mcp.e2e/Support/Configuration/ProcessDesignerE2EGate.cs:14,23`.

- **P4.5c DONE (2026-08-04).** `BaseTool.cs:178` now returns `FromValidationError` (exit 1) instead of
  `FromError` (exit −1) for a `PackageRequirementException`, and `BaseToolTests.cs:129` pins the new
  value with a `because` that defends the choice rather than merely asserting failure. Nothing else
  pinned the old code: the second `PackageRequirementException` test asserts the **version** gate wins
  and never reaches the package gate, and the e2e only asserts the *absence* of the refusal message on
  an environment where the package IS installed. `docs/McpCapabilityMap.md` needed **no** change — it
  already documented exit 1 for "a required package not installed", so the code caught up with the doc.
  Full unit suite green (7970 passed, 0 failed). Original problem statement, kept for the record:
  the MCP refusal is exit **-1** via `FromError`, which the shipped guidance defines as "clio itself
  failed, retrying won't help", while `FromValidationError` (exit 1) is the documented factory for a
  refused precondition. Switch `BaseTool.cs:177-179` to `FromValidationError`.
  **This alters a shipped envelope**, so it is not a one-line change: re-validate
  `clio.tests/Command/McpServer/BaseToolTests.cs` (`:110, :140, :170, :194, :222`), correct the
  contradictory rows in `docs/McpCapabilityMap.md:260, :265-266`, and re-check any e2e consumer of the
  code. **Keep the message static** — `FromError`/`FromValidationError` surface text **without**
  `SensitiveErrorTextRedactor`, so enriching the refusal with an environment URI or connection detail
  would silently breach the audited secret-hygiene invariant; dynamic text must go through
  `FromException(redactSensitive: true)`.
  *If the reviewer prefers not to touch the shared envelope in this ticket, the fallback is to leave
  the code and fix only the capability-map rows — but then record explicitly that agents are being told
  not to retry a retryable failure.*

### 6.3 MCP surface

- **P4.6** `clio/Command/McpServer/Tools/InstallProcessBuilderTool.cs`, cloned from `InstallGateTool.cs`:
  `BaseTool<InstallProcessBuilderOptions>(null, logger, commandResolver)` (**`null` startup command** —
  environment-sensitive tools must resolve per environment), tool-name const,
  `ReadOnly=false, Destructive=false, Idempotent=true, OpenWorld=false`, kebab `JsonPropertyName`
  args record, `InternalExecute<InstallProcessBuilderCommand>(options)`.
  **The same `[FeatureToggle("process-designer")]` must be repeated on this class** — the CLI
  attribute does not gate the MCP surface.
- **P4.7** Residency: **long-tail**, like `install-gate`/`install-application`/`install-sql-schema`.
  Do **not** add it to `McpCoreToolProfile.CoreToolTypes`; `ResidentToolNames` is reflection-derived,
  so nothing else to edit. No `McpToolCompatibilityCatalog` entry (that catalog is only for
  renames/removals).
- **P4.8** **Mandatory** row in `clio.tests/Command/McpServer/PassthroughToolClassificationRegistry.cs`
  (`NotApplicable`, like `install-gate:304`) or `PassthroughToolClassificationGuardTests` fails.
  No `DurableInvocationGateCompletenessTests` entry (that baseline is for `ReadOnly=true` tools).
- **P4.9** Curated `get-tool-contract` entry in `ToolContractGetTool.cs` (dictionary row + a
  `BuildInstallProcessBuilder()` with input schema, example, Preconditions, Flow) — mirrors
  `install-gate`'s entry. Reachability is automatic; this is discoverability enrichment, and it is
  how an agent learns to call a long-tail tool after a refusal.
- **P4.10** Add the tool to `McpReadDeadlineGate`'s idempotent-server-write list (where `install-gate`
  is named, line 37) so it is not covered by the retry-safe read deadline.
- **P4.11** MCP unit tests cloned from `InstallGateToolTests.cs`: stable name,
  per-environment resolution (`FakeInstallProcessBuilderCommand` subclass with substituted deps),
  and the annotation set.
- **P4.12** **E2E is mandatory** (`AGENTS.md:179-180`). Model on
  `clio.mcp.e2e/InstallApplicationToolE2ETests.cs` — the closest install-style template
  (`Category("McpE2E.Sandbox")` + `ProcessDesignerE2EGate.CategoryName`, `AllureNUnit`, class-level
  `[NonParallelizable]` — required by `McpFixturePolicyTests` — `AllowDestructiveMcpTests` guard,
  ping-app probe, `ListReachableToolNamesAsync` contains the name, nested `["args"]` payload,
  `McpCommandExecutionParser.Extract`, success + invalid-environment pair). Note there is **no**
  `InstallGateToolE2ETests` to copy — this is new work.

### 6.4 Guidance and capability map

- **P4.13** `DeployLifecycleGuidanceResource.cs:83-86` already carries the parallel `install-gate`
  remediation step — add the process-builder step alongside it.
- **P4.14** `ProcessModelingGuidanceResource.cs:33-35` (feature-gated) — state the package
  prerequisite and the install pointer in the "read first" block.
- **P4.15** **No routing row.** `RoutingGuidanceResource.cs:21-22` deliberately withholds the
  `process-modeling` row until the feature ships; do not add one now.
- **P4.16** `docs/McpCapabilityMap.md` — §11 "Business Process Modeling" (line 669) is the one in-repo
  doc that already states the dependency; update the name/version and add the install tool. Also
  consider §2 "Application Lifecycle" (line 350) where `install-application` sits. No test guards
  this file — it is honour-system.
- **P4.17** `clio/tpl/**/AGENTS.md`: **no edit needed** — no shipped template mentions `install-gate`
  or `cliogate`. If the tool is ever named in a template, `WorkspaceTemplateGuidanceDriftTests`
  requires that a non-resident tool name share its line with `clio-run` / `get-tool-contract` /
  `get-guidance`.

---

## 7. Deferred by design (record, don't build)

| Option | Mechanism if requested | Why deferred |
|---|---|---|
| Interactive `[Y/N]` offer on the CLI | `IInteractiveConsole.Prompt` + the `--force`/`--confirm`/`-y` convention | The 5 consumers are MCP-only — no TTY surface exists to prompt on. And `RealInteractiveConsole` is the only implementation registered in production and returns `false` **silently** on redirected stdin, so a prompt would decline with no explanation under MCP/CI |
| `CodeConfirmationRequired` offer envelope on MCP | `McpDurableCallToolHandler` already implements confirm-then-retry-via-`clio-run` for write-capable long-tail tools (`:59, :113-127, :220-256`) | Richer than needed once P4.5c makes the plain refusal retryable; revisit if agents still fail to act on it (§11.6) |
| Auto-install on first `create-bp` | Intercept `PackageRequirementException` at `Program.TryGetPackageRequirementError` / `BaseTool.EnforcePackageRequirements` and chain the installer | Confluence O2.10 (+1 day); also needs a `RequiredPackageChecker` cache-invalidation path — the checker caches the package list per instance with no reset, so a detect→install→recheck loop in one process reads the pre-install snapshot and refuses **again**, which reads to the user as "the install did nothing" |
| Distinct exit code for a package refusal | mirror the Creatio-version gate's `78` in `CommandErrorCodes.cs` | Not requested. Note this is a *different* question from P4.5c, which is about using the existing precondition code instead of the unexpected-failure one |
| Automatic clio↔package version alignment | — | Ticket says separate task |

---

## 8. Acceptance checks

Mapped to the ticket's suites; the ones the simplified scope removes are marked N/A.

| ID | Check | Gate |
|---|---|---|
| TC-R-1 | Rename complete: no `clioprocessbuilder`/`ClioProcessBuilder` token outside the append-only diary | P0 |
| TC-R-2 | `descriptor.json` — `Name = CrtProcessBuilder`, `UId` **unchanged**, 4-part `PackageVersion`, `Maintainer = Creatio`, `ProjectPath = Files/CrtProcessBuilder.csproj` | P0 |
| TC-R-3 | **`/rest/ProcessDesignService/ListUserTasks` answers after the rename** (route unaffected by package/assembly/namespace rename) | P0 |
| TC-R-4 | `dev-nf` builds and the package's 477 unit tests pass. `dev-n8` remains unbuilt locally and **no longer needs to be** — the target compiles the package (§3) | P0 |
| TC-R-5 | ~~Produced DLL naming per TFM~~ **RETIRED** — no assembly ships; the target names its own output. The casing constraint it protected now lives on the descriptor `Name` (TC-R-2) | — |
| TC-B-1 | **PASS** — the `.gz` carries the sources, `Files/Libs/{ATF.Repository,ErrorOr}.dll`, the compile-marker schema and its resource, and **no** assembly, `.pdb`, `obj/` or `.idea/`. Asserted by tests P2.4b/d/e | P1 |
| TC-B-2 | **PASS** — `Files/Bin` and `Files/obj` removed before compressing, so no local build output can leak in | P1 |
| TC-C-* | dfs-ts delivery / build registration | **N/A → ENG-92113** |
| TC-D-1 | `.gz` committed at `clio/CrtProcessBuilder/` | P2 |
| TC-D-2 | `clio.csproj` `Content Include` + `CopyToOutputDirectory=Always` present | P2 |
| TC-D-3 | **PASS** — build output contains `CrtProcessBuilder/CrtProcessBuilder.gz`; asserted by test P2.4a and confirmed in `clio/bin/Debug/net8.0/` | P2 |
| TC-D-5/6 | **PASS** — descriptor `Name`/`UId`/`Version` inside the committed `.gz` match `BundledPackages`; asserted by test P2.4b. Plus TC-D-8: the compile-marker schema is present (P2.4d) | P2 |
| TC-D-7 | `build.ps1` does **not** try to build the package from source | P2 |
| TC-E-2 | **PASS** — measured on a real `dotnet pack`: 2 entries under `tools/{net8.0,net10.0}/any/CrtProcessBuilder/`, and absent from `content/` and `contentFiles/` | P2 |
| TC-E-3 | Installed global tool resolves the `.gz` at the path the command expects; asserted in the release workflow's existing "Verify packaged tool version" step | P2 |
| TC-E-4 | The `.gz` in the built output is **not stale** relative to git (per-TFM `CopyToOutputDirectory` refresh trap) | P2 |
| TC-F-1 | `clio install-process-builder -e <net472-stand>` succeeds from the **bundled** artifact | P3 |
| TC-F-6 | **PASS** — the same source-only `.gz` installed on `.NET 8.0.29` (`ts1-infr-web02:8530`) in ~35 s and the service answered with 23 tasks | P3 |
| TC-F-2 | `clio list-packages -e <env>` shows `CrtProcessBuilder` at the delivered version | P3 |
| TC-F-3 | ~~App healthy after the restart~~ **RETIRED** — the command no longer restarts; the equivalent guarantee is TC-F-4, which the command now performs itself | — |
| TC-F-4 | `list-user-tasks` returns the catalog (~23) → the service resolves | P3 |
| TC-F-5 | `create-business-process` → `describe-business-process` round-trip | P3 |
| TC-F-7 | A user without `CanManageSolution` is rejected cleanly, not crashed | P3 |
| TC-G-1 | Upgrade over install: vN → vN+1 on the same stand, service still answers afterwards. **Not run** — no version bump has been needed yet | P3 |
| TC-G-2 | **Transition (must be run on a disposable stand):** install `CrtProcessBuilder` (same UId) over an existing `clioprocessbuilder`. Assert: (a) the `SysPackage` row is renamed, not duplicated; (b) **no orphaned `Terrasoft.Configuration/Pkg/clioprocessbuilder` folder with a stale `clioprocessbuilder.dll`** — an orphan would register a second `ProcessDesignService` type and `CustomServicesParser` silently skips duplicates, so the route could bind the stale assembly; (c) `SysPackageInInstalledApp` follows. **Write the answer into the runbook.** | P1/P3 |
| TC-X-1 | Missing-package refusal names the new command and version, on both the CLI and MCP paths | P4 |
| TC-X-2 | Stale-package refusal ("version X or higher") fires when an older version is installed | P4 |
| TC-X-3 | **`SysPackage.Version` is populated and parseable** for this package on a real stand (`clio list-packages -e <env> --json`) — otherwise `IsInstalled` is permanently `false` and the offer loops | P4.0 |
| TC-X-4 | MCP refusal returns the **precondition** exit code, not the "unexpected failure" one, so an agent will install-and-retry | P4.5c |
| TC-X-5 | `list-user-tasks` is pinned by the reflection lock-in test (the currently missing 4th `[TestCase]`) | P4.5a |
| TC-X-6 | `InstallProcessBuilderOptions` carries **no** `[RequiresPackage]` — the installer must not be gated by the requirement it satisfies | P3 |
| TC-X-7 | Re-running the install when the package is already current returns 0 **without** restarting the app | P3 |
| TC-X-8 | Refusal text stays static — no environment URI or connection detail (unredacted channel) | P4.5c |
| TC-X-9 | The verb is reachable on a **default** install (not feature-gated), and `clio install-process-builder -H` shows help — i.e. the Hint does not point at an unknown verb | P3 |
| TC-H-1 | **Full** `dotnet test --filter "Category=Unit"` green — mandatory, because `Program.cs` + `BindingsModule.cs` are touched (`AGENTS.md:324-328`). A `Module=Command\|Module=McpServer` filter is **insufficient**: `HelpArtifactConsistencyTests` (`Module=Core`) and `McpFixturePolicyTests` (no Module) are invisible to it | P5 |
| TC-H-2 | No new `CLIO*` analyzer warnings in edited files | P5 |
| TC-H-3 | Workspace diary entry appended in both repos | P5 |

---

### 8.1 Verification run — 2026-08-04, `krestov-test` (net472, 8.1.3)

The renamed package was built (`-c Release`), compressed (336,302 bytes) and installed on a stand from
which the old `clioprocessbuilder` had been removed. Results:

| ID | Result | Evidence |
|---|---|---|
| TC-R-2 | **PASS** | `clio list-packages` → `CrtProcessBuilder 1.0.0.0 Creatio` — descriptor `Name` and the 4-part version reach `SysPackage` |
| TC-R-3 | **PASS** | `POST rest/ProcessDesignService/ListUserTasks` → `success: true`. **The route survived the package + assembly + namespace rename**, as predicted from `serviceImplType.Name` |
| TC-R-5 | **PASS** (net472) | build emits `Files/Bin/CrtProcessBuilder.dll` |
| TC-F-1 | **PASS** | `clio push-pkg ./packages/CrtProcessBuilder.gz -e krestov-test` → "Package installation finished", configuration build clean |
| TC-F-2 | **PASS** | as TC-R-2 |
| TC-F-3 | **PASS** | `clio restart -e krestov-test` → "Done restart-web-app"; the service answers afterwards |
| TC-F-4 | **PASS** | **23** user tasks returned — matches the expected catalog size |
| TC-F-5 | **PASS** | `BuildProcess` → `UsrClioBpCliTest1` / `49247122-aa0e-4220-8056-52805c9c3216`; `DescribeProcess` returns its elements, parameters, the `Recommendation` mapping expression and layout positions |
| TC-B-1 | **PARTIAL** | archive carries `Files/Bin` + `Files/Bin/netstandard` as paths, no `.pdb` entry, no `.idea`; but `netstandard` is **empty in content** — the leg is parked (§11.5) |
| TC-F-6, TC-F-7, TC-G-1 | not run | netcore parked; permission-negative and upgrade-over-install still open |

**Two by-products of the run worth carrying forward:**

1. **`push-pkg` failed its post-install step on this stand** — the package installed and the
   configuration built, but `UnlockMaintainerPackageInternal` → `PackageLockManager.CallGate` threw,
   because `krestov-test` has `developerModeEnabled: true` and the unlock goes through cliogate. This is
   direct field evidence for §6.1's requirement that the new command force `DeveloperModeEnabled = false`
   (as `install-gate` does), which skips that path entirely.
2. **`PushPackageCommand`'s catch printed only the stack trace, no message**, so the reason for the
   unlock failure was unrecoverable from the output. Exactly the anti-pattern §6.1 warns against — the
   new command must log `GetReadableMessageException()` first, like `InstallGateCommand`.

Left on the stand: the test process `UsrClioBpCliTest1` in package `Custom`.

Also note a URL trap for anyone reproducing this: `ServiceUrlBuilder.Build(string)` **already prepends**
`0/` for `IsNetCore = false`, so `--service-path` must be `rest/ProcessDesignService/…` without the
`0/` prefix — passing `0/rest/…` yields `0/0/rest/…` and an IIS 404 that looks exactly like a
missing service.

---

## 9. P5 — Policy obligations (non-negotiable per `AGENTS.md`)

- **P5.1 Docs.** Hand-author **only** `clio/help/en/install-process-builder.txt` (with a
  `PREREQUISITES` block, per the `listen.txt` / `lock-package.md` convention).
  `Commands.md`, `Wiki/WikiAnchors.txt` and `docs/commands/*.md` are **generated** by
  `HelpArtifactExporter` — run `clio __generate-help-artifacts` and commit its output; do **not**
  hand-edit them. Also add the verb to `CommandHelpCatalog` (P3.4) or it lands in the fallback group
  with a description that just echoes the verb.
  Because the command is **ungated** (§6.1), the generated artifacts **will** and **must** include it —
  `HelpArtifactConsistencyTests` (`Module=Core`) fails otherwise. This is the opposite of the gated case:
  the exporter uses `ExportFeatureToggleService` (every `[FeatureToggle]` type reads as disabled) and
  `CleanLegacyMarkdownDocs` **deletes** a `.md` committed for a gated command — `watch-compilation` is
  that precedent (help txt kept, `.md` + index entries removed in `36dd8f31`). If §11.3 is decided the
  other way and the command *is* gated, flip to the `watch-compilation` shape: help txt only, and do not
  commit generated entries.
- **P5.2 ClioRing gate.** Determination: **not Ring-consumed** *(for the additive part)*. Ring's clio tool surface is a closed
  hardcoded list — `list-packages, list-apps, install-gate, restart-by-environment-name,
  clear-redis-db-by-environment` (`ClioRing/ViewModels/ClioWorkflowViewModel.cs:135-141`); no
  process-designer or BP tool name appears anywhere in `clio-ring/`; `actions.json` has no such verb;
  Ring reads the `get-tool-contract` catalog dynamically (`CatalogCount > 0`) and pins no tool list.
  State in the PR: **"ClioRing compatibility reviewed, no Ring-consumed contract changed"** with those
  paths cited.
  **P4.5c included — and it does NOT fire the full gate.** An earlier draft of this plan claimed it
  would, on the grounds that the changed envelope is shared with `install-gate`, which Ring consumes.
  That was wrong, and the cross-check is cheap to state: Ring's consumed tool set is closed and hardcoded
  to `list-packages`, `list-apps`, `install-gate`, `restart-by-environment-name`,
  `clear-redis-db-by-environment` (`ClioWorkflowViewModel.cs:135-141`), plus the deploy/uninstall nested
  commands and the three preflight commands via `clio-run`. **None of those carries
  `[RequiresPackage]`** — the 13 attribute sites are `Create`/`Modify`/`Describe`/`ListUserTasks`
  process-designer commands, `ValidateProcessGraphTool`, `Feature`, `GetProcessSignature`, `Listen`,
  `Lock`/`UnlockPackage`, `DownloadPackage`, `ShowPackageFileContent` and `SqlScript`. So the
  package-refusal branch is unreachable from Ring's surface and Ring can never observe the changed exit
  code. The determination stays **"reviewed, no Ring-consumed contract changed"**, with these paths
  cited; no `ClioRing.Tests` run and no NativeAOT publish are required for this change.
- **P5.3 BMAD.** The architecture decision is already made (ENG-91840 / Confluence), so take the
  small-feature path: `/bmad-spec` → `spec/prd/spec-deliver-process-builder-package.md`, an ADR
  addendum closing the open item in `adr-ENG-90883`, stories in `spec/sprint-status.yaml`
  (`ready-for-dev` → `in-progress` → `review` → `done`), and a test plan in `spec/test-plans/`
  carrying §8.
- **P5.4 Code review gates.** Comprehensive 3-lens review before opening the PR; triaged scoped
  review per post-open commit; comprehensive review before ready-to-merge.
- **P5.5 Release notes.** `RELEASE.md` — call out the new bundled package and its version.
- **P5.6 Diary.** Append an entry to `.codex/workspace-diary.md` in **both** repos.

---

## 10. Status and remaining work

**Delivered** (branch `feature/ENG-94385-bundle-process-builder-package` in clio,
`feature/ENG-94385-rename-crt-process-builder` in `cli-process-builder`):

| # | Work | State |
|---|---|---|
| P0 | Rename `clioprocessbuilder` → `CrtProcessBuilder`; UId and REST route preserved | **done**, verified on a stand |
| — | Delivery-shape experiment: compiled vs source-only, both readers, both runtimes | **done**, §3 decided |
| P1 | Artifact production — no build step; `.clioignore` hygiene; runbook content | **done** (runbook still to be written out as a file) |
| P2 | Bundle in clio: `.gz`, csproj `Content` with `Pack="false"`, `BundledPackages`, 4 guard tests, `*.gz binary` | **done** |
| P3 | `install-process-builder`: command, DI, `Program.cs`, help catalog, 11 unit tests, help + docs | **done** |
| P4.5c | Refusal envelope: package refusal now returns the caller-actionable code, not the unexpected-failure one | **done** |
| P4.5a | The missing 4th `[TestCase]` in the reflection lock-in, so the rename cannot half-land green | **done** |

**Remaining:**

| # | Work | Days |
|---|---|---|
| P4 | Detect/offer wiring: the `[RequiresPackage]` argument renamed at 5 sites + version floor from `BundledPackages`, hints naming the new command, 5 tool `[Description]`s, lock-in tests, MCP tool + curated contract + classification row + unit tests, `deploy-lifecycle` and `process-modeling` guidance, `docs/McpCapabilityMap.md` | 2.0 |
| P4.12 | MCP E2E (no install-style sibling to copy; model on `InstallApplicationToolE2ETests`) | 1.0 |
| P5 | Generated docs, BMAD artifacts, diary, review gates, full unit suite | 1.0 |
| — | Live install-path run on an environment that has never had the package (only the short-circuit is verified so far) | 0.25 |
| — | Buffer | 0.5 |
| | **Total remaining** | **≈ 4.75 days** |

**What the source-only decision removed from the original estimate:** P0.0 (local .NET Core Creatio,
0.5 d) is cancelled outright; the two-flavour spike and its fallback budget are moot; P1 shrank from a
build recipe to two commands. P4.5c came in at ~0.5 h rather than the 0.5 d budgeted, because the
ClioRing gate turned out not to apply.

**Biggest remaining risks:** (1) P4 touches five `[RequiresPackage]` sites plus their pinned Hint strings
and tool descriptions — mechanical but wide, and one site was unpinned until P4.5a; (2) the MCP E2E has no
sibling to copy; (3) the residual unexplained Hub rejection (§P1.11) sits on the manual install path.

**The ways this can ship looking green and being broken** — collected because they share one shape: the
gate reads `SysPackage.Name`, and *nothing in clio* connects that name to a working service.

1. **The archive's descriptor disagrees with what the gate looks for** → installs, reports success, gate
   reports the package missing forever. Caught by guard test P2.4b.
2. **The compile-marker schema is lost from the archive** → the package installs, is never compiled,
   produces no assembly, and the gate reports it **present** while every call fails. This is the failure
   mode the source-only decision introduces, and it has two independent guards: test P2.4d at build time,
   and the command's own outcome check at install time.
3. **The environment accepts the package but fails to compile it** → same visible symptom as (2), but on
   the target rather than in the artifact. Caught by the command's outcome check; recoverable through the
   Hub's `RestoreFromBackup` stage or `clio restore-configuration`.
4. **Same-UId install over a stale predecessor** → the `SysPackage` row might not be renamed, or the old
   `Pkg/<oldname>` folder might be orphaned with its own assembly, in which case two
   `ProcessDesignService` types load and `CustomServicesParser` keeps whichever it enumerates first.
   Mitigated procedurally: the transition is uninstall-then-install (§11.4), not an in-place upgrade.

*Retired: "net472-only artifact on a .NET 8 stand". No assembly ships, so there is no flavour to get
wrong — the failure mode is structurally impossible rather than guarded.*

---

## 11. Open questions for the reporter

1. ~~**Final name confirmation.**~~ **DECIDED: `CrtProcessBuilder`** (D. Krestov, 2026-08-04), matching
   the Confluence record ENG-91840 / O1.1. Note that the **ENG-94385 summary still says
   "clioprocessbuiilder … CrtBusinessProcessBuilder"** — the ticket should be corrected so the descriptor
   name and the ticket agree. Everything downstream in this plan uses `CrtProcessBuilder`:
   descriptor `Name`, `Files/CrtProcessBuilder.csproj`, `CrtProcessBuilder.dll`, namespace
   `CrtProcessBuilder`, tests `CrtProcessBuilder.Tests`, bundle
   `clio/CrtProcessBuilder/CrtProcessBuilder.gz`. The CLI verb stays `install-process-builder`
   (name-independent, kebab-case).
2. **Version floor value.** What 4-part version does the first bundled artifact carry, and is a
   `[RequiresPackage]` floor acceptable now (§6.2 P4.2) or should it stay presence-only?
3. **Feature gate — recommendation reversed, needs sign-off.** This plan ships the install command and
   tool **ungated** (§6.1), because gating them removes the verb from the parse array and deletes their
   docs, making the refusal Hint point at an unknown verb. The cost is that the verb — and therefore the
   package name — becomes publicly visible before the BP feature ships. Confirm that trade is acceptable;
   the alternative is a remediation message that cannot be followed.
4. ~~**Transition on existing stands.**~~ **DECIDED BY ACTION (2026-08-04):** the reporter removed
   `clioprocessbuilder` from `krestov-test` before the first install of the renamed package, so the
   transition procedure is **uninstall-then-install**, not a same-UId in-place upgrade. That is the safe
   path anyway: it sidesteps the whole hazard class of §2.2 — the `GetIsPackageDescriptorModified`
   silent no-op, the orphaned `Pkg/clioprocessbuilder/` folder, and the duplicate-`ProcessDesignService`
   takeover. Consequence: **TC-G-2 drops from hard blocker to a documented procedure step.** It can
   still be run later (the pre-rename `packages/clioprocessbuilder.gz` was deliberately kept for
   exactly that), but nothing waits on it. The runbook must state the uninstall step explicitly.
5. **CLOSED (2026-08-05): the source-only archive works on .NET 8, and the platform picks the target
   framework itself.** Stand `http://ts1-infr-web02:8530` reports `frameworkKind: "Net"`,
   `frameworkDescription: ".NET 8.0.29"`, core 10.1.443.0. The reporter installed the source-only
   archive there through the Hub in ~35 s, and `/rest/ProcessDesignService/ListUserTasks` returns
   `success: true` with the full 23-task catalog.
   **Why that settles the target-framework question** even without that stand's build log (its logs are
   not on the `Creatio_Logs\AutoTest` share): on a .NET host `FileContentStorage.GetBinDirectoryPath`
   probes `Files/Bin/netstandard` (`UseSeparateDirectoryToLoadPackageAssemblies` defaults to true), and
   the archive shipped **no assembly at all** — so the server must have compiled a netstandard-targeted
   assembly and placed it there. Together with the observed `SetCustomProperties -
   NetStandardCompatibilityMode = True` and server-chosen `TargetFramework [net472]` on the .NET
   Framework stands, the platform demonstrably selects the target per host.
   **Consequence: no per-framework artifact, no local .NET Core Creatio, and no netcore build
   prerequisite (P0.0) are needed at all.**

   *Superseded position, kept for the record:* .NET Core Creatio
   builds do exist, and `clio deploy-creatio --platform net6` can provision one locally, so this is a
   setup task rather than an external blocker. What is true today is narrower: **none of the 15
   environments registered in clio is `isNetCore`** (`krestov-test`, where P4.0 was measured, is a
   net472 developer environment), and `.application/net-core` is not wired in the package checkout.
   Consequently TC-F-6 and the netstandard half of the artifact are **unvalidated, not blocked**.
   *Fallback if a netcore target never materialises:* ship net472-only and make the install command
   **refuse with a clear message when `EnvironmentSettings.IsNetCore` is true**, rather than installing a
   package that can never load (§3's worst failure mode).
6. ~~**Refusal-envelope fix in scope?**~~ **DECIDED: in scope, and DONE** (2026-08-04). The concern that
   drove the question — that touching a shared MCP envelope would pull in the full ClioRing gate — did not
   survive the cross-check: no Ring-consumed tool is package-gated, so Ring cannot observe the changed
   branch (see §9 P5.2). Actual cost was one production line plus one test assertion.
7. ~~**Netstandard third-party DLLs.**~~ **ANSWERED — no action:** `.clio/clioignore` already denylists
   `Microsoft.Extensions.DependencyInjection.dll`, `Microsoft.Extensions.Http.dll` and
   `System.Text.Json.dll` by name, so `clio compress` never packs them regardless of what the build
   copies into `Files/Bin/netstandard` (P1.3).
