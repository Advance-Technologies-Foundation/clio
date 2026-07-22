# ADR: MCP `mcp-http` Credential-Passthrough Tool Parity

**Status**: Proposed
**Author**: Architect Agent
**PRD**: [prd-mcp-passthrough-tool-parity.md](../prd/prd-mcp-passthrough-tool-parity.md)
**Jira**: ENG-93347 (sub-task of ENG-92790; direct predecessor ENG-93208 / branch `claude/clio-mcp-multi-tenant-73a807`; blocks ENG-92869)
**Created**: 2026-07-10 (revised after adversarial code-grounded review — see "Revision history")
**stepsCompleted**: [1, 2, 3, 4]

---

## Revision history

- **Rev 2**: An adversarial code-grounded review found two mechanically broken designs (c1's overload was
  incomplete — nested name-based calls survived it; `build-theme`'s `InternalExecute<BuildThemeCommand>`
  proposal cannot compile) and three high/medium gaps (c3's requiredness fix relied on an unconfirmed
  downstream error path; the c1 fix bypassed the FR-05 tenant-lock/in-flight-guard; the FR-06 test design
  was too coarse to prove per-scenario coverage). Every finding was independently re-verified against the
  current worktree (not taken on faith) before revising. This revision replaces the affected designs; it
  does not touch the parts of Rev 1 that were confirmed sound (bearer-safety verification, c2 mechanics,
  c3 fail-fast classification rationale, the `update-page`/`get-component-info` Pattern-A fixes,
  `McpFeatureToggleFilter.GetAttributedTypes` reuse).

---

## Branching constraint (read first, restated from the PRD)

This ADR designs work that sits **on top of** the passthrough infrastructure delivered by ENG-93208
(`CredentialContext`, `ICredentialContextAccessor`, `ToolCommandResolver.Resolve`'s passthrough branch,
`McpHttpServerCommand` middleware — all present in the current working tree). The implementation branch
for ENG-93347 **must be cut from `claude/clio-mcp-multi-tenant-73a807`**, not `master`, and its PR **must
target `claude/clio-mcp-multi-tenant-73a807`**, not `master`. None of the seam types this ADR builds on
(`IToolCommandResolver.Resolve<T>`, `ICredentialContextAccessor`, the env-arg rejection) exist on `master`;
branching from `master` produces a build that cannot compile.

---

## Context

ENG-93208 shipped a per-request credential-passthrough leg for `clio mcp-http`, but only tools that resolve
their target-scoped service through `IToolCommandResolver` actually honor the `X-Integration-Credentials`
header (`ToolCommandResolver.Resolve`, `credentialContextAccessor.Current`, `ToolCommandResolver.cs:101`;
the env-arg rejection is at `:111-118`, verified in the current tree). A closed set of resident tools — or
individual dependency-paths *within* an otherwise-compliant tool — instead reach Creatio through a
directly-injected, root/bootstrap-container-bound service (`IApplicationListService`,
`ISettingsRepository.GetEnvironment`), so the header is silently ignored: sometimes an opaque
"Environment name is required" failure, sometimes a real cross-tenant data read using stored credentials.
ENG-92869 (AI-Platform gateway integration) cannot proceed while the tool set is inconsistent and the
failure mode is indistinguishable from caller error. This ADR resolves the PRD's five open questions
(OQ-01…OQ-05) with a concrete, per-tool decision, verified directly against the source in this tree —
including a full nested-call-graph trace for the two areas where a shallow, single-call-site fix would
have been mechanically incomplete or broken.

## Decision

Bring every environment-sensitive resident-tool **dependency path** — including every **nested** call a
routed operation makes — into one of three states: **route** it through `IToolCommandResolver` (execute
against the header tenant), **fail-fast** with one uniform "not supported under credential passthrough"
error via a new centralized guard, or **document a non-tenant fallback** (the `get-component-info`
pattern). Two implementation patterns, both funneling through the existing, already bearer-safe ENG-93208
seam:

- **Pattern A (tool/service-layer resolve).** Code living in the MCP tool class itself (outside any
  per-request container) resolves `EnvironmentSettings` via `commandResolver.Resolve<EnvironmentSettings>
  (options)` and threads the **resolved settings object** — never a bare environment-name string — through
  the **entire** call graph beneath it, including every nested lookup a domain service currently repeats
  by name. The resolved settings feed the **existing**, already bearer-safe per-environment factories
  (`IApplicationClientFactory.CreateEnvironmentClient`, `IServiceUrlBuilderFactory.Create`,
  `ICurrentUserCultureResolverFactory.Create`, `IPlatformVersionResolverFactory.Create`). No new
  client-construction code path is introduced. Tools using this pattern that return a typed (non-
  `CommandExecutionResult`) response derive from `BaseTool<EnvironmentOptions>(null, logger,
  commandResolver)` — an **established pattern already in this codebase**
  (`CheckThemingAccessTool.cs:20`, `GetCreatioInfoTool.cs:20`, five more call sites) — and wrap the service
  call in `ExecuteWithCleanLog(options, () => {...})` so it gets the per-tenant execution lock and the
  FR-08 in-flight guard for free, not merely a bare `Resolve<EnvironmentSettings>` call with no lock.
- **Pattern B (typed-response, tool-resolved settings passed into the command).** For `build-theme`, whose
  options type is **not** `EnvironmentOptions`-derived and therefore cannot go through
  `InternalExecute<TCommand>` at all, the **tool** resolves `EnvironmentSettings` (when the CLI-style
  `--version`/`--environment-name` mutual-exclusion allows it) and passes the resolved settings **as an
  explicit parameter** into a new command overload — the command's constructor and its CLI-facing,
  name-based overload are untouched.

Where full routing is disproportionate for v1 (the `link-from-repository-*` family), a new
**`ICredentialPassthroughToolGuard`**, invoked from `BaseTool<T>`, rejects the call with the single uniform
error **before** any Creatio-reaching call. For the two tools where this guard is the *only* protection
(no resolver ever runs on the non-passthrough path either), the tool **explicitly validates**
`environment-name` itself for non-passthrough calls — it does **not** rely on relaxing `[Required]` and
hoping a downstream command-level validator produces an equivalent error, because that downstream path was
not built for this contract and was found, on inspection, to be an unreliable place to lean on (see OQ-03).

---

## Verification performed before committing to this design (do not re-derive, but do not blindly trust either — re-read cited lines before implementing)

1. **Root cause of class (c1) confirmed independent of container choice.** `ApplicationListService.
   GetApplications` throws on a null/blank `environmentName` (`ApplicationListService.cs:52-55`) and, when
   given a non-blank name, resolves settings via `settingsRepository.FindEnvironment(environmentName)`
   (`:57`) — by name, against whichever `ISettingsRepository` the calling container holds. Every container
   `BindingsModule.Register(...)` builds — bootstrap, registry-based, **and the ephemeral passthrough
   container** — constructs its own `SettingsRepository` fresh from the filesystem
   (`BindingsModule.cs:203`, singleton-registered at `:205`). Resolving `IApplicationListService` itself
   through `IToolCommandResolver` changes nothing on its own.
2. **Bearer-safety of the routing pattern verified end-to-end.** `IApplicationClientFactory.
   CreateEnvironmentClient` (`clio/Common/ApplicationClientFactory.cs`) implements the ENG-93208
   bearer-first branch (bearer → `NoReauthExecutor`, never `Login()`; cookie → `NotSupportedException`;
   login/password/OAuth unchanged). Every downstream factory this ADR routes through calls this exact
   method — `CurrentUserCultureResolverFactory.Create` (`:51`) and `PlatformVersionResolverFactory.Create`
   (`:44`) both call `_applicationClientFactory.CreateEnvironmentClient(settings)` directly. `IReauthExecutor`
   is registered exactly once, singleton, always `NoReauthExecutor` (`BindingsModule.cs:220`). Feeding an
   ephemeral, bearer-only `EnvironmentSettings` into any of these factories is provably as safe as the
   container's own `IApplicationClient` singleton wiring.
3. **c2 confirmed as a genuine, narrow leak.** `GetUserCultureTool.ResolveEnvironmentSettings` calls
   `settingsRepository.GetEnvironment(options)` directly (`GetUserCultureTool.cs:89`).
   `ConfigurationOptions.GetEnvironment(EnvironmentOptions)` (`clio/Environment/ConfigurationOptions.cs:
   638-652`) falls through to `FindEnvironment(null)` when `options.Environment` is blank, returning
   `Environments[ActiveEnvironmentKey]` (`:367-372`) — the configured active environment.
4. **c1's nested call graph does NOT collapse to one overload — re-verified line by line, and the gap is
   real.** Resolving `EnvironmentSettings` once at the tool boundary and calling a single new
   settings-based overload on the outer service is **not sufficient**, because every c1 service **repeats**
   a name-based lookup internally, on a call that the outer settings-based overload would otherwise leave
   untouched:
   - `ApplicationCreateService.CreateApplication` resolves caption culture via
     `captionCultureResolver.Resolve(new EnvironmentOptions { Environment = environmentName }, null)`
     (`ApplicationCreateService.cs:88`) — **after** `environmentSettings` was already resolved a few lines
     above (`:76-78`). `CaptionCultureResolver.Resolve` (`clio/Command/EntitySchemaDesigner/
     CaptionCultureResolver.cs:54`) calls `_settingsRepository.GetEnvironment(options)` — the **identical**
     active-tenant-leak method verified in point 3. A blank/passthrough name here reads the **active
     registered environment's** culture, not the header tenant — this is the same leak class as c2, hiding
     one level deep inside `create-app`/`create-app-section`/`update-app-section`.
   - `ApplicationCreateService`'s timeout/polling path calls `applicationInfoService.GetApplicationInfo
     (environmentName, ...)` (`:471, :484`) — by name again, even though `environmentSettings` was already
     resolved for the initial client construction.
   - `ApplicationSectionCreateCommand` repeats the SAME caption-culture-by-name call twice
     (`:202` for the readback culture, `:219` for the profile-validation culture) and the SAME
     `applicationInfoService.GetApplicationInfo(environmentName, ...)` nested call twice more (`:219`
     region and the polling loop at `:737`).
   - `ApplicationSectionUpdateCommand` repeats the caption-culture-by-name call (`:87`) and the
     `GetApplicationInfo(environmentName, ...)` nested call (`:93`).
   - `ApplicationSectionDeleteCommand` and `ApplicationSectionGetListCommand` each call
     `applicationInfoService.FindApplicationId(environmentName, request.ApplicationCode)` by name
     (`ApplicationSectionDeleteCommand.cs:76`, `ApplicationSectionGetListCommand.cs:74`).
   Every one of these nested calls is a **second, independent** name-based `ISettingsRepository` touch that
   a single outer `EnvironmentSettings`-based overload does not reach. The complete-call-graph fix below
   (OQ-01, c1) replaces all of them, not just the outer one.
   **Confirmed NOT a gap**: `ApplicationCreateTool`'s enrichment call
   (`enrichmentService.Enrich(args, ...)` → `ApplicationCreateEnrichmentService.Enrich` →
   `DataForgeEnrichmentBuilder.Build`) already resolves `IDataForgeContextService` via
   `commandResolver.Resolve<IDataForgeContextService>(new DataForgeTargetOptions { Environment =
   request.EnvironmentName })` (`DataForgeEnrichmentBuilder.cs:53`) — this is the PRD's own class-(b),
   already-compliant path (unconstrained `Resolve<T>`, genuinely per-request). No change needed there.
5. **`build-theme` Pattern B (as drafted in Rev 1) was mechanically broken — confirmed, not merely
   asserted.** `BuildThemeOptions` is a standalone `[Verb]`-attributed class, **not** derived from
   `EnvironmentOptions` (`BuildThemeCommand.cs:19-20`). `BaseTool.ResolveFromCallContainer` — the method
   `InternalExecute<TCommand>` depends on — throws `InvalidOperationException` for any options type that
   is not `EnvironmentOptions` (`BaseTool.cs:222-226`), so `InternalExecute<BuildThemeCommand>(options)`
   would fail at the very first call, every time. Separately, `BuildThemeTool.BuildTheme` does **not**
   currently call `InternalExecute` at all — it already uses the typed-response path
   `ExecuteWithCleanLog(() => { ...command.TryBuildTheme(options, ...)... })` (`BuildThemeTool.cs:108-119`),
   returning `BuildThemeResult`, which `InternalExecute<TCommand>`'s `CommandExecutionResult` return type
   cannot substitute for without breaking the tool's contract. `BuildThemeCommand.ResolveVersion`
   (`:251-272`) only attempts any environment lookup when `options.EnvironmentName` is explicitly non-blank
   (`hasEnvironment` at `:252`); with neither `--version` nor `--environment-name` it returns
   `LatestFallback` immediately (`:272`) — there is no environment-name slot fed by the HTTP header today,
   so merely injecting settings into the existing method changes nothing about that branch. The corrected
   design (OQ-01, build-theme, below) resolves settings **in the tool**, not by forcing the command through
   `BaseTool`'s environment-sensitive path.
6. **c3's requiredness gap is real but the mechanism proposed for it in Rev 1 was unconfirmed — corrected.**
   Only **two** of the three `link-from-repository-*` methods take `environmentName`
   (`LinkFromRepositoryByEnvironment`, `LinkFromRepositoryUnlocked`); the direct-path method instead takes a
   **required** `envPkgPath` (`LinkFromRepositoryTool.cs:49-50`) and has no `environmentName` parameter at
   all — `envPkgPath` must stay required regardless of transport (it is the only selector for the local-only
   passthrough-safe case). All three methods call `InternalExecute(options)` directly against the one
   root-bound `Link4RepoCommand` (`LinkFromRepositoryTool.cs:40,63,85`) — no resolver is ever consulted, on
   ANY transport. `Link4RepoCommand.Execute` (`Link4RepoCommand.cs:636-673`) runs `_validator.Validate
   (options)` FIRST; `Link4RepoOptionsValidator` (`:75-124`) fails with "Either path to creatio directory or
   environment name must be provided" when **both** `EnvPkgPath` and `Environment` are blank (`:83-93`) —
   this generic, non-passthrough-aware FluentValidation message is what a relaxed-`[Required]`, no-selector
   call would actually surface, **not** the uniform passthrough error this ADR requires, and — separately —
   the validator's rule does not, by itself, prove every combination is caught before the `var _ => 1` bare
   fallback at `:669` is reached (that arm is reachable in principle whenever `Unlocked` is false and both
   selectors are blank in a way the validator does not gate, e.g. if a future edit changes the validator
   without updating the switch). **Given this, relying on schema relaxation + the command's own validator
   to produce the right message for the non-passthrough case is not a sound design** — the fix must
   explicitly validate `environment-name` in the tool, not delegate it downstream. See OQ-03.

---

## Alternatives Considered

| Option | Pros | Cons | Status |
|--------|------|------|--------|
| **OQ-01: Resolve the existing domain service through `IToolCommandResolver` with no other change** | Zero new code | Provably non-executable for c1/c3 (verification #1); does not fix the nested calls either (verification #4) | Rejected |
| **OQ-01: Single outer `EnvironmentSettings` overload per c1 service, nested calls left as-is** | Smallest diff | Verification #4 shows this leaves a real active-tenant culture leak and several silent name-based reads one level down — **worse than useless**, since it would ship as "fixed" while the deepest, highest-blast-radius nested call (culture resolution) still leaks | Rejected — the complete-call-graph fix (below) is the only version of this pattern that actually closes the defect |
| **OQ-01: Service-contract addition, threading `EnvironmentSettings` through the ENTIRE call graph (outer service + every nested service/resolver it calls), Pattern A** | Minimal new client-construction surface (verification #2); reuses `BaseTool<EnvironmentOptions>(null, ...)` + `ExecuteWithCleanLog` for locking (established pattern, verification below); additive — existing name-based overloads and CLI callers untouched | Larger diff than Rev 1 assumed: 8 new overloads across 5 interfaces, not 7 | **Chosen for c1 and c2** |
| **OQ-01: `build-theme` via `InternalExecute<BuildThemeCommand>`** | — | Mechanically broken: wrong options-type constraint, wrong return type, does not change the no-probe branch (verification #5) | **Rejected, removed entirely** |
| **OQ-01: `build-theme` via tool-resolved settings passed as an explicit parameter into a new command overload (Pattern B)** | No `InternalExecute` type mismatch; command constructor and CLI overload untouched (no bootstrap/DI redesign forced); preserves `BuildThemeResult` and workspace-write locking | Requires one new overload per existing `TryBuildTheme` entry point (compute + workspace-write) | **Chosen** |
| **OQ-01: Centralized pre-binding fail-fast guard for ALL broken tools** | Fastest, safest, simplest | Leaves `list-apps` — the PRD's flagship gateway example — permanently unusable under the one transport ENG-92869 targets | Rejected as a blanket policy; **retained narrowly for c3** |
| **OQ-03 (c3): relax `[Required]` on `environment-name` and rely on `Link4RepoCommand`'s own validator for the non-passthrough case** | No new tool-level code | Verification #6: produces a generic, non-uniform error, and the "bare `return 1`" fallback arm means this path is not provably safe against future validator drift | Rejected |
| **OQ-03 (c3): guard-first, then explicit tool-level `environment-name` requiredness check for the two name-based methods** | Deterministic, tool-owned, does not depend on a downstream command's validator staying in sync | One extra explicit check per method (two methods) | **Chosen** |
| **OQ-04: reflection check that "a test exists in fixture X"** | Simple | Verification (independent, this round): a fixture with ANY unrelated `[Test]` would satisfy it — proves nothing about per-tool/per-scenario coverage | Rejected — too coarse |
| **OQ-04: registry maps each (tool, scenario) pair to an exact test method name, verified to exist and carry a test attribute** | Directly proves the specific scenario has a named test, not merely "a test exists somewhere" | More registry entries to maintain | **Chosen** |

*(OQ-02 and OQ-05 verdicts are unchanged from Rev 1 — see the Design Decisions section; they were not
challenged by the review.)*

---

## Design Decisions (per Open Question)

### OQ-01 — routing architecture, resolved per class

#### c1 (7 Application tools) — ROUTE, with the COMPLETE nested call graph replaced

The service-contract addition is **not** one overload per outer interface — it is a **settings-based
overload at every layer the call graph touches**, so that **no settings-based overload internally calls a
name-based overload**. Concretely:

```csharp
// IApplicationInfoService — TWO settings-based overloads (both are called by nested lookups
// elsewhere in the graph, not just the get-app-info tool itself).
public interface IApplicationInfoService {
    ApplicationInfoResult GetApplicationInfo(string environmentName, string? id, string? code);            // unchanged, CLI/name-facing
    ApplicationInfoResult GetApplicationInfo(EnvironmentSettings environmentSettings, string? id, string? code); // NEW
    InstalledAppSummary FindApplicationId(string environmentName, string code);                              // unchanged
    InstalledAppSummary FindApplicationId(EnvironmentSettings environmentSettings, string code);              // NEW
}

// ICaptionCultureResolver — settings-based overload that bypasses ISettingsRepository.GetEnvironment
// entirely (this is the nested leak in verification #4; it is CaptionCultureResolver's OWN active-tenant
// leak, so the fix belongs here, not in each caller).
public interface ICaptionCultureResolver {
    string Resolve(EnvironmentOptions options, string overrideCulture);      // unchanged, CLI/name-facing
    string Resolve(EnvironmentSettings settings, string overrideCulture);    // NEW — no repository call at all
}

// IApplicationListService, IApplicationCreateService, IApplicationSectionCreateService,
// IApplicationSectionUpdateService, IApplicationSectionDeleteService, IApplicationSectionGetListService —
// each gains a settings-based overload whose body is IDENTICAL to today's name-based body EXCEPT:
//   - it does not call settingsRepository.FindEnvironment(name) — the caller already supplied settings;
//   - every NESTED call it makes to IApplicationInfoService.{GetApplicationInfo,FindApplicationId} and to
//     ICaptionCultureResolver.Resolve uses the NEW settings-based overloads above, not the name-based ones.
public interface IApplicationCreateService {
    ApplicationInfoResult CreateApplication(string environmentName, ApplicationCreateRequest request);           // unchanged
    ApplicationInfoResult CreateApplication(EnvironmentSettings environmentSettings, ApplicationCreateRequest request); // NEW
}
// (IApplicationSectionCreateService / SectionUpdate / SectionDelete / SectionGetList / IApplicationListService
//  gain the analogous settings-based overload — same shape, repeated per interface during implementation.)
```

**Rule, stated explicitly so it survives implementation**: a settings-based overload may call another
settings-based overload, or a factory that takes `EnvironmentSettings` directly
(`IApplicationClientFactory.CreateEnvironmentClient`, `IServiceUrlBuilderFactory.Create`,
`ICurrentUserCultureResolverFactory.Create`) — it may **never** call a name-based overload or
`ISettingsRepository.FindEnvironment`/`GetEnvironment` directly. This is the enforceable invariant a code
reviewer checks for on every PR touching these files, not merely a design intent.

Each `ApplicationXxxTool` derives from `BaseTool<EnvironmentOptions>(null, logger, commandResolver)` —
the established, already-used pattern for a typed-response tool with no `Command<T>`
(`CheckThemingAccessTool.cs:20`, `GetCreatioInfoTool.cs:20`) — and wraps its body:

```csharp
public sealed class ApplicationGetListTool(ILogger logger, IToolCommandResolver commandResolver,
    IApplicationListService applicationListService)
    : BaseTool<EnvironmentOptions>(null, logger, commandResolver) {

    public ApplicationListResponse ApplicationGetList(ApplicationGetListArgs args) {
        EnvironmentOptions options = new() { Environment = args.EnvironmentName };
        // ExecuteWithCleanLog(options, ...) — the OPTIONS-AWARE overload — not the zero-arg one, so the
        // per-tenant lock key is derived from THIS call's target (FR-05), not the shared fallback key.
        return ExecuteWithCleanLog(options, () => {
            try {
                EnvironmentSettings settings = commandResolver.Resolve<EnvironmentSettings>(options);
                var applications = applicationListService.GetApplications(settings, args.Id, args.Code);
                return ApplicationToolHelper.CreateListResponse(/* map applications */);
            } catch (Exception ex) {
                return ApplicationToolHelper.CreateListErrorResponse(SensitiveErrorTextRedactor.Redact(ex.Message));
            }
        });
    }
}
```

Calling the **two-argument** `ExecuteWithCleanLog(EnvironmentOptions options, Func<TResponse> executor)`
(not the zero-argument overload some existing tools use, e.g. `CheckThemingAccessTool.cs:59`, which falls
back to `McpToolExecutionLock.SharedFallbackKey` and does **not** key the lock per tenant) is what closes
the FR-05 gap: `ExecuteUnderTenantLock` derives the lock key from `options` via
`commandResolver.GetTenantKey(options)` and calls `McpToolExecutionLock.MarkInUse`/`MarkAvailable` around
the whole body — the exact per-tenant lock + in-flight-guard ceremony `BaseTool.InternalExecute` gives
`Command<T>`-shaped tools, now available to a typed-response tool with no command at all.

Behavior by transport (unchanged from Rev 1, still correct — this part of the design was not
challenged): stdio/registered-env resolves via the unchanged registry branch (SM-03); header-only resolves
against the ephemeral header settings (FR-01); header + explicit `environment-name` is rejected by the
existing `HasExplicitCredentialArgs` check (closes Security mode iii / AC-06) with **no new code**.

#### c2 (`get-user-culture`) — ROUTE (unchanged from Rev 1, not challenged)

`GetUserCultureTool` gains `IToolCommandResolver`; `ResolveEnvironmentSettings` becomes
`commandResolver.Resolve<EnvironmentSettings>(options)`, replacing `settingsRepository.GetEnvironment
(options)` (`GetUserCultureTool.cs:89`). Closes Security mode ii by construction.

#### c3 (`link-from-repository-*`) — FAIL-FAST under passthrough (v1); explicit requiredness check for the non-passthrough case (revised — see OQ-03 below for the corrected mechanism)

Unchanged classification (routing is disproportionate — verification stands: the environment name doubles
as a local-package-directory selector with no passthrough equivalent). The **enforcement mechanism** for
"required unless passthrough" is corrected in OQ-03 below; it is not implemented as a bare schema relax.

#### Matrix tools

- **`update-page`**: unchanged from Rev 1 (not challenged). `ResolvePlatformVersionAsync` swaps
  `settingsRepository.GetEnvironment(...)` → `commandResolver.Resolve<EnvironmentSettings>(...)`. This tool
  has **no** blank-name early return before the settings call, so the fix genuinely reaches the resolver
  on every input shape, including header-only.
- **`get-component-info`**: unchanged from Rev 1 (not challenged). Same Pattern-A swap in
  `ResolveEnvironmentSettings`.
- **`sync-pages`**: **corrected**. `PageSyncTool.ResolvePlatformVersionAsync` has an **extra** guard clause
  not present in `PageUpdateTool`'s equivalent method: `if (resolverFactory is null || settingsRepository
  is null || string.IsNullOrWhiteSpace(environmentName)) return null;` (`PageSyncTool.cs:93`) — the
  blank-`environmentName` branch returns **before** any settings resolution is attempted, so swapping only
  the inner `settingsRepository.GetEnvironment(...)` call (as Rev 1 proposed) would never be reached on a
  header-only passthrough call; the probe would keep silently degrading to `latest` without ever consulting
  the credential context. **Fix**: drop the blank-`environmentName` short-circuit (mirror
  `PageUpdateTool`'s guard shape, which only checks for absent dependencies, not a blank name) so the
  resolver runs unconditionally when its dependencies are present, then apply the same
  `commandResolver.Resolve<EnvironmentSettings>` swap. This makes the probe genuinely routed (executes
  against the header tenant) rather than an undocumented, always-latest fallback.
- **`build-theme`**: **fully redesigned** (Pattern B, see below) — the Rev 1 `InternalExecute
  <BuildThemeCommand>` proposal is removed.

#### `build-theme` — Pattern B, corrected design

`BuildThemeCommand`'s constructor, its CLI-facing `TryBuildTheme(options, out css, out descriptor, ...)`
and `TryBuildTheme(options, workspaceDirectory, packageName, out ...)` overloads, and its by-name
`ResolveVersion(options)` path are **all left unchanged** — the CLI's offline "no environment → latest
template" behavior is untouched, and no bootstrap/no-active-environment DI redesign is forced onto the
command.

`BuildThemeTool` gains an **optional** `IToolCommandResolver? commandResolver = null` constructor
parameter (nullable, defaulted, so the existing direct-construction unit test
`new BuildThemeTool(command, Substitute.For<ILogger>())` — `BuildThemeToolTests.cs:49` — keeps compiling
and keeps passing unchanged; new tests for the passthrough-aware behavior inject a real/substitute resolver
in the same implementation slice, see the Implementation Plan). Before calling `command.TryBuildTheme`,
when `args.Version` is blank, the tool attempts to resolve settings itself:

```csharp
EnvironmentSettings? resolvedSettings = null;
if (string.IsNullOrWhiteSpace(args.Version) && commandResolver is not null) {
    try {
        resolvedSettings = commandResolver.Resolve<EnvironmentSettings>(
            new EnvironmentOptions { Environment = args.EnvironmentName });
    } catch (Exception) {
        resolvedSettings = null; // fail-soft, matches the sibling matrix tools' probe shape exactly
    }
}
return ExecuteWithCleanLog(() => {
    bool ok = writeToPackage
        ? command.TryBuildTheme(options, resolvedSettings, args.WorkspaceDirectory, args.PackageName, out ..., out ..., out string writeError)
        : command.TryBuildTheme(options, resolvedSettings, out ..., out ..., out ..., out string buildError);
    return ok ? BuildThemeResult.Successful(...) : BuildThemeResult.Failure(...);
});
```

`BuildThemeCommand` gains two **new** overloads (one per existing `TryBuildTheme` entry point) that accept
an optional `EnvironmentSettings? resolvedSettings` and use it, when non-null, **instead of** calling
`_settingsRepository.FindEnvironment(options.EnvironmentName)` inside `ResolveVersion` — i.e. a new private
`ResolveVersion(BuildThemeOptions options, EnvironmentSettings? resolvedSettings)` that:
1. Explicit `--version` still wins (unchanged, still mutually exclusive with `--environment-name`).
2. Else, if `resolvedSettings` is supplied, resolve the version via `_resolverFactory.Create(resolvedSettings)`
   directly — **no** repository call. This covers BOTH: an explicit `environment-name` on a non-passthrough
   call (the tool's `Resolve<EnvironmentSettings>` did the registry lookup — net-equivalent to today's
   behavior; see the Consequences note on `Fill` below) AND header-only passthrough (the tool's `Resolve`
   returned the ephemeral, header-derived settings — this is the new, previously header-blind case, now
   fixed).
3. Else (no version, no resolved settings — resolver threw, e.g. no environment/URI available and no
   passthrough context, or `commandResolver` is null on the CLI path), fall back to `LatestFallback` —
   **byte-for-byte the CLI's existing offline default**, since the CLI never passes `resolvedSettings` at
   all and always hits this branch exactly as it does today.

Header + explicit `environment-name` (mixed input) is handled by the SAME fail-soft catch already present
for every matrix tool's probe: `commandResolver.Resolve<EnvironmentSettings>` throws
(`HasExplicitCredentialArgs` rejection) → caught → `resolvedSettings = null` → `LatestFallback`. No
Creatio-reaching call happens on the rejected path (the resolver throws before any client/container is
built), so this is safe (closes AC-06) even though the user-facing signal is "latest" rather than an
explicit error — identical in shape to how `update-page`/`sync-pages` already handle this case, which the
review did not challenge.

### OQ-02 — header-version parity for mixed-input tools

Unchanged from Rev 1 — not challenged. Closed by the Pattern-A fix applied uniformly across the matrix
tools (including the corrected `sync-pages`/`build-theme` designs above): no silent named-registered-tenant
probe in any case.

### OQ-03 — conditional requiredness, split by mechanism (revised)

Requiredness enforcement is **not** the same mechanism for every tool — it splits along the routing
decision:

**Resolver-ROUTED tools** (c1, c2, `update-page`, `sync-pages`, `get-component-info`, `build-theme`):
remove `[Required]` from `environment-name` at the MCP schema layer. Runtime requiredness on the
**non-passthrough** path is enforced by the existing `ToolCommandResolver.ResolveSettingsAndKey`, which
throws `EnvironmentResolutionException` when `options.Environment` is blank and no explicit `Uri` was
supplied (`ToolCommandResolver.cs:172-181`) — this is sound for these tools **because they always call the
resolver**, so the throw is guaranteed to fire. On the passthrough path, the existing
`HasExplicitCredentialArgs` rejection covers "forbidden when present." No new validation layer for this
group.

**Guard-only tools** (c3 — `link-from-repository-by-environment`, `link-from-repository-unlocked`): these
tools **never call the resolver on any transport** (verification #6), so there is no automatic downstream
enforcement to lean on, and the schema-relax-only approach was found to produce a generic, non-uniform
error via `Link4RepoCommand`'s own validator rather than this ADR's uniform message. Corrected mechanism:

```csharp
public CommandExecutionResult LinkFromRepositoryByEnvironment(string environmentName, string repoPath, string packages, bool? dryRun, bool? skipPreparation) {
    CommandExecutionResult? rejection = RejectIfPassthroughUnsupported(
        LinkFromRepositoryByEnvironmentToolName,
        "Register the target environment and use the stdio path, or a non-passthrough mcp-http request.");
    if (rejection is not null) {
        return rejection;
    }
    // Not under passthrough (the guard above would have returned otherwise): environment-name is
    // required here exactly as it always has been. Enforce it explicitly — do NOT rely on
    // Link4RepoOptionsValidator's generic "path or environment" message to carry this contract.
    if (string.IsNullOrWhiteSpace(environmentName)) {
        return CommandExecutionResult.FromError(
            "environment-name is required for link-from-repository-by-environment outside credential passthrough.");
    }
    Link4RepoOptions options = new() { Environment = environmentName, RepoPath = repoPath, Packages = packages, DryRun = dryRun ?? false, SkipPreparation = skipPreparation ?? false };
    return InternalExecute(options);
}
```

`environment-name`'s MCP-schema `[Required]` is relaxed to optional on these two methods too (so a
header-only passthrough call can omit it and reach the guard rather than being rejected at the binding
layer before the guard has a chance to run) — but unlike the routed group, the **runtime** "required
outside passthrough" enforcement is an **explicit tool-level check**, not a relied-upon downstream
validator message. `envPkgPath` on `link-from-repository-by-env-package-path` **stays `[Required]`**
unconditionally (it is the only selector for that variant on every transport, including the allowed
local-only/`skip-preparation=true` passthrough case) — no relaxation, no guard interaction beyond the
existing `!SkipPreparation` condition on whether the guard fires at all.

### OQ-04 — the FR-06 guard mechanism, corrected to prove per-scenario coverage

A registry entry maps to an **exact, named test method** per **(tool, dependency-path, scenario)** tuple —
not "a fixture exists somewhere with some test in it." Scenarios are drawn from a fixed, small vocabulary
so the registry stays reviewable:

```csharp
internal enum PassthroughScenario { HeaderOnly, MixedInput, RegisteredEnvStdio, NotApplicable }

internal sealed record PassthroughCoverageEntry(
    string ToolName,
    PassthroughScenario Scenario,
    Type FixtureType,
    string MethodName);   // exact method name, e.g. nameof(ApplicationGetListToolPassthroughTests.ApplicationGetList_ShouldExecuteAgainstHeaderTenant_WhenHeaderOnly)

internal static class PassthroughToolClassificationRegistry {
    internal static readonly IReadOnlyDictionary<string, PassthroughClassification> Classification = ...; // one row per discovered tool name, including NotEnvironmentSensitive
    internal static readonly IReadOnlyList<PassthroughCoverageEntry> Coverage = [
        new("list-apps", PassthroughScenario.HeaderOnly, typeof(ApplicationGetListToolPassthroughTests), nameof(ApplicationGetListToolPassthroughTests.ApplicationGetList_ShouldExecuteAgainstHeaderTenant_WhenHeaderOnly)),
        new("list-apps", PassthroughScenario.MixedInput, typeof(ApplicationGetListToolPassthroughTests), nameof(ApplicationGetListToolPassthroughTests.ApplicationGetList_ShouldRejectCall_WhenHeaderAndEnvironmentNameBothPresent)),
        new("list-apps", PassthroughScenario.RegisteredEnvStdio, typeof(ApplicationGetListToolPassthroughTests), nameof(ApplicationGetListToolPassthroughTests.ApplicationGetList_ShouldBehaveUnchanged_WhenRegisteredEnvironmentStdio)),
        // ... one triplet per routed/guarded tool; a single NotApplicable row per not-environment-sensitive tool.
    ];
}
```

Two tests:
1. **Completeness (discovery-drift guard).** Enumerate every tool name via the existing
   `McpFeatureToggleFilter.GetAttributedTypes(assembly, typeof(McpServerToolTypeAttribute))` → assert the
   result is **exactly** `Classification.Keys`. A newly added tool with no row fails immediately.
2. **Per-scenario mapping presence (corrected — proves the SPECIFIC method, not "any test").** For every
   `Classification` entry that is not `NotEnvironmentSensitive`, assert `Coverage` contains a row for each
   of its expected scenarios (routed/guarded tools: `HeaderOnly` + `MixedInput` + `RegisteredEnvStdio`;
   fail-fast-only tools: `HeaderOnly` maps to the guard-rejection test, `RegisteredEnvStdio` maps to the
   unchanged-behavior test), and — via reflection over `FixtureType` — that `FixtureType.GetMethod
   (MethodName)` returns a non-null `MethodInfo` carrying `[Test]` or `[TestCase]`. A classification with a
   missing scenario row, or a row naming a method that does not exist or is not a test, fails the guard.
   This is what makes "a missing test must not silently pass" true at the scenario level, not just at the
   "some test exists" level the Rev 1 design left unproven.

### OQ-05 — latency

Unchanged from Rev 1 — not challenged. No latency SLA introduced; SM-03 stays functional-only.

---

## Decision matrix (FR-07 — authoritative per-tool/path record)

| Tool | Class | Decision | Mechanism | Key files touched |
|------|-------|----------|-----------|--------------------|
| `list-apps` | c1 | **Route** (full nested graph) | New settings-based `IApplicationListService` overload; `BaseTool<EnvironmentOptions>(null,...)` + `ExecuteWithCleanLog(options,...)` | `ApplicationListService.cs`, `ApplicationTool.cs`, `ApplicationToolArgs.cs` |
| `get-app-info` | c1 | **Route** | New settings-based `IApplicationInfoService.GetApplicationInfo` overload | `ApplicationInfoService.cs`, `ApplicationTool.cs`, `ApplicationToolArgs.cs` |
| `create-app` | c1 | **Route** (full nested graph) | New settings-based `IApplicationCreateService` overload; internally uses the settings-based `ICaptionCultureResolver` + `IApplicationInfoService.GetApplicationInfo` overloads for its caption-culture and polling paths; enrichment path already compliant (verification #4) | `ApplicationCreateService.cs`, `CaptionCultureResolver.cs`, `ApplicationInfoService.cs`, `ApplicationTool.cs`, `ApplicationToolArgs.cs` |
| `create-app-section` | c1 | **Route** (full nested graph) | New settings-based `IApplicationSectionCreateService` overload; internally uses settings-based caption-culture (×2 call sites) + `GetApplicationInfo` (×2 call sites, including polling) | `ApplicationSectionCreateCommand.cs`, `CaptionCultureResolver.cs`, `ApplicationInfoService.cs`, `ApplicationTool.cs` |
| `update-app-section` | c1 | **Route** (full nested graph) | New settings-based `IApplicationSectionUpdateService` overload; internally uses settings-based caption-culture + `GetApplicationInfo` | `ApplicationSectionUpdateCommand.cs`, `CaptionCultureResolver.cs`, `ApplicationInfoService.cs`, `ApplicationTool.cs` |
| `delete-app-section` | c1 | **Route** (full nested graph) | New settings-based `IApplicationSectionDeleteService` overload; internally uses settings-based `FindApplicationId` | `ApplicationSectionDeleteCommand.cs`, `ApplicationInfoService.cs`, `ApplicationTool.cs` |
| `list-app-sections` | c1 | **Route** (full nested graph) | New settings-based `IApplicationSectionGetListService` overload; internally uses settings-based `FindApplicationId` | `ApplicationSectionGetListCommand.cs`, `ApplicationInfoService.cs`, `ApplicationTool.cs` |
| `get-user-culture` | c2 | **Route** | Swap `ISettingsRepository.GetEnvironment` → `commandResolver.Resolve<EnvironmentSettings>` | `GetUserCultureTool.cs` |
| `link-from-repository-by-environment` | c3 | **Fail-fast under passthrough; explicit tool-level requiredness check otherwise** | `ICredentialPassthroughToolGuard` first, then an explicit `environmentName` blank-check (NOT schema-relax-and-hope) | `LinkFromRepositoryTool.cs`, `BaseTool.cs` |
| `link-from-repository-by-env-package-path` | c3 | **Fail-fast (v1) unless `skip-preparation=true`** | Same guard, conditioned on `SkipPreparation`; `envPkgPath` stays `[Required]` unconditionally | `LinkFromRepositoryTool.cs` |
| `link-from-repository-unlocked` | c3 | **Fail-fast under passthrough; explicit tool-level requiredness check otherwise** | Same as by-environment (always Creatio-reaching) | `LinkFromRepositoryTool.cs` |
| `update-page` | matrix | **Route** | Pattern-A fix to `ResolvePlatformVersionAsync` (no blank-name short-circuit exists here — fix reaches the resolver unconditionally) | `PageUpdateTool.cs` |
| `sync-pages` | matrix | **Route** (corrected) | Remove the blank-`environmentName` early return in `ResolvePlatformVersionAsync` (`:93`), THEN apply the Pattern-A swap; relax `[Required]` on `PageSyncArgs.EnvironmentName` (FR-05a) | `PageSyncTool.cs` |
| `get-component-info` | matrix | **Route** | Pattern-A fix to `ResolveEnvironmentSettings` | `ComponentInfoTool.cs` |
| `build-theme` | matrix | **Route** (Pattern B — corrected) | Tool resolves `EnvironmentSettings` itself (optional resolver dependency); NEW `TryBuildTheme` overloads on the command accept `EnvironmentSettings?` and skip the by-name lookup when supplied; CLI overloads/ctor untouched | `BuildThemeTool.cs`, `BuildThemeCommand.cs` |
| `clio-run` / `clio-run-destructive` | b (verified, no change) | **No change** | Dispatches within the same request context (`ClioRunTool.cs:122-127`); inherits whichever fix each retargeted tool gets | none |
| All class (a)/(b) tools, out-of-scope tools | a/b/out-of-scope | **No change** | Already compliant / not environment-sensitive | none |

---

## Key interfaces / contracts

```csharp
// New centralized fail-fast guard (c3 v1).
public interface ICredentialPassthroughToolGuard {
    bool IsPassthroughActive { get; }
    string BuildUnsupportedMessage(string toolName, string alternativeGuidance);
}

// BaseTool<T> gains a thin helper any subclass may call before executing a non-routed path.
private protected CommandExecutionResult? RejectIfPassthroughUnsupported(string toolName, string alternativeGuidance) {
    if (passthroughGuard is null || !passthroughGuard.IsPassthroughActive) {
        return null;
    }
    return CommandExecutionResult.FromError(passthroughGuard.BuildUnsupportedMessage(toolName, alternativeGuidance));
}

// c1 — settings-based overloads added at EVERY layer of the call graph (representative subset;
// ICaptionCultureResolver and IApplicationInfoService are the two interfaces every OTHER c1 service
// nests calls into, so their settings-based overloads are the ones that make the "no name-based nested
// call survives" rule enforceable).
public interface ICaptionCultureResolver {
    string Resolve(EnvironmentOptions options, string overrideCulture);   // unchanged
    string Resolve(EnvironmentSettings settings, string overrideCulture); // NEW — no ISettingsRepository call
}
public interface IApplicationInfoService {
    ApplicationInfoResult GetApplicationInfo(string environmentName, string? id, string? code);              // unchanged
    ApplicationInfoResult GetApplicationInfo(EnvironmentSettings environmentSettings, string? id, string? code); // NEW
    InstalledAppSummary FindApplicationId(string environmentName, string code);                                // unchanged
    InstalledAppSummary FindApplicationId(EnvironmentSettings environmentSettings, string code);                // NEW
}

// build-theme — Pattern B: settings passed as an explicit parameter, command constructor untouched.
public partial class BuildThemeCommand {
    // Existing overloads (CLI-facing, name-based ResolveVersion) — UNCHANGED.
    public bool TryBuildTheme(BuildThemeOptions options, out string css, out string descriptor, out IReadOnlyList<string> warnings, out string error);
    public bool TryBuildTheme(BuildThemeOptions options, string workspaceDirectory, string packageName, out string writtenPath, out IReadOnlyList<string> warnings, out string error);

    // NEW overloads — used only by the MCP tool, which resolves settings itself via IToolCommandResolver.
    public bool TryBuildTheme(BuildThemeOptions options, EnvironmentSettings? resolvedSettings, out string css, out string descriptor, out IReadOnlyList<string> warnings, out string error);
    public bool TryBuildTheme(BuildThemeOptions options, EnvironmentSettings? resolvedSettings, string workspaceDirectory, string packageName, out string writtenPath, out IReadOnlyList<string> warnings, out string error);
}

// FR-06/OQ-04 coverage registry (illustrative shape; see OQ-04 above for the full two-test design).
internal enum PassthroughScenario { HeaderOnly, MixedInput, RegisteredEnvStdio, NotApplicable }
internal sealed record PassthroughCoverageEntry(string ToolName, PassthroughScenario Scenario, Type FixtureType, string MethodName);
```

No new `IApplicationClient` implementation, no new client-construction code path, and no change to
`ToolCommandResolver.Resolve`, `CredentialContext`, or the middleware.

---

## CLI flag specification

No new CLI verb, no new CLI flag. MCP-argument-schema changes:

| Change | Detail |
|--------|--------|
| `environment-name` on 7 Application args records, `PageSyncArgs`, and the two name-based `LinkFromRepositoryTool` methods | `[Required]` removed → schema-optional. For the resolver-routed group, runtime requiredness on non-passthrough transports is enforced by the existing resolver failure path. For the two guard-only `link-from-repository-*` methods, runtime requiredness is an **explicit tool-level check** (OQ-03), not a relied-upon downstream validator message. |
| `envPkgPath` on `link-from-repository-by-env-package-path` | **Unchanged** — stays `[Required]` on every transport. |

All MCP argument names stay kebab-case; no camelCase argument is introduced anywhere in this design.

---

## Implementation Plan (revised — vertical slices: code and its own tests land together; no slice defers its tests to a later step)

Each slice is a candidate story. A slice's tests run and pass before the next slice starts, so a later
slice never sits on a base that "will be tested eventually."

1. **`ICredentialPassthroughToolGuard` + `BaseTool` helper, with its first consumer and tests together.**
   Add the guard, DI-register it, wire it into `LinkFromRepositoryTool`'s three methods (guard first, then
   the explicit `environmentName` check on the two name-based methods per OQ-03), relax `[Required]` on
   those two methods only. Ship with its own tests (guard rejection, `skip-preparation=true` bypass,
   explicit-requiredness-error, stdio/registered-env unchanged) in the SAME slice — registering the guard
   with no consumer until a later step risks a CLIO005 dead-DI finding, so the consumer lands with it.
2. **c2 fix (`get-user-culture`) + its tests.** Swap the direct `ISettingsRepository` call for
   `commandResolver.Resolve<EnvironmentSettings>`; add header-only / mixed-input / registered-env tests in
   the same slice. Smallest, highest-severity fix — closes the real active-tenant leak first.
3. **`update-page` + `get-component-info` probe fixes + their tests.** Pattern-A one-line swap in each
   `ResolvePlatformVersionAsync`/`ResolveEnvironmentSettings`; both already have the resolver injected, so
   this slice needs no new constructor wiring. Tests: header-only routes, mixed-input fail-soft-to-latest,
   registered-env unchanged.
4. **`sync-pages` probe fix (corrected) + `[Required]` relax + its tests.** Remove the blank-`environmentName`
   early return, apply the Pattern-A swap, relax the schema attribute. Tests must include a header-only
   case that asserts the probe is REACHED (not short-circuited) — the specific regression this revision
   fixes.
5. **`build-theme` redesign + command AND tool tests together.** Add the two new `TryBuildTheme` overloads
   on `BuildThemeCommand` (CLI overloads/ctor untouched — existing `BuildThemeCommandTests` stay green with
   no changes); add the optional `IToolCommandResolver?` parameter to `BuildThemeTool` (existing
   `BuildThemeToolTests.cs:49` direct construction keeps compiling/passing unchanged because the parameter
   is optional); add NEW tests in the same slice for header-only routing, mixed-input fail-soft, and
   registered-env-via-tool-resolved-settings parity with the CLI path.
6. **c1, service-contract + tool wiring, one interface pair at a time, each with its own tests:**
   - 6a. `ICaptionCultureResolver` settings-based overload + tests (this is the shared dependency every
     later c1 sub-slice needs — land it first within this group).
   - 6b. `IApplicationInfoService` settings-based overloads (`GetApplicationInfo`, `FindApplicationId`) +
     tests — the second shared dependency.
   - 6c. `list-apps` (`IApplicationListService` + tool) + tests (header-only, mixed-input, registered-env).
   - 6d. `get-app-info` (tool wiring onto 6b's overload) + tests.
   - 6e. `create-app` (`IApplicationCreateService` + tool, threading 6a/6b through the caption-culture and
     polling nested calls) + tests covering the NESTED paths specifically (culture resolution under
     passthrough, polling/readback under passthrough) — not just the initial client-creation call.
   - 6f. `create-app-section` / `update-app-section` (threading 6a/6b through their nested calls) + tests,
     same nested-path coverage requirement as 6e.
   - 6g. `delete-app-section` / `list-app-sections` (threading 6b's `FindApplicationId` overload) + tests.
7. **OQ-04 guard (FR-06) + its own tests.** Add `PassthroughToolClassificationRegistry` +
   `PassthroughCoverageEntry` rows for every tool touched in slices 1-6, plus the not-environment-sensitive
   rows from the PRD audit; add the two guard tests (completeness + per-scenario mapping-presence).
8. **MCP surface + docs review (FR-09) — final, consolidated step.** Update every touched tool's
   `[Description]`, `docs/McpCapabilityMap.md`, and any affected `help/en/*.txt` /
   `docs/commands/*.md` / `Commands.md` / guidance article, now that the full tool set from slices 1-7 is
   known and stable.
9. **CLIO analyzer / clean-build pass (FR-10) — run after every slice, not only at the end**, per the
   project's smart-regression-testing policy (targeted `Category=Unit&Module=McpServer` filter before each
   commit).

---

## Test strategy

| Layer | Framework | What to cover | File(s) |
|-------|-----------|----------------|---------|
| Unit | NUnit + NSubstitute | Every c1 tool: header-only (executes against header tenant), mixed-input (rejected), registered-env stdio (unchanged) — AND, for `create-app`/`create-app-section`/`update-app-section`, a SEPARATE test asserting the nested caption-culture resolution and polling/readback calls are ALSO header-aware (not just the initial client construction) | `clio.tests/Command/McpServer/Application*ToolPassthroughTests.cs` |
| Unit | NUnit + NSubstitute | `ICaptionCultureResolver.Resolve(EnvironmentSettings, ...)`: never calls `ISettingsRepository`; `IApplicationInfoService.{GetApplicationInfo,FindApplicationId}(EnvironmentSettings, ...)`: same | `clio.tests/Command/EntitySchemaDesigner/CaptionCultureResolverTests.cs`, `clio.tests/Command/ApplicationInfoServiceTests.cs` |
| Unit | NUnit + NSubstitute | `get-user-culture`: header-only → header tenant; mixed-input (uri/login/password or environment-name) → rejected; registered-env stdio → unchanged; no active-environment fallback even when one is configured | `clio.tests/Command/McpServer/GetUserCultureToolPassthroughTests.cs` |
| Unit | NUnit + NSubstitute | `link-from-repository-*` (all 3): passthrough → uniform guard rejection; non-passthrough with no `environment-name` → the NEW explicit tool-level error (not the generic FluentValidation message); `skip-preparation=true` on the env-package-path variant → NOT rejected; stdio/registered-env with `environment-name` supplied → unchanged | `clio.tests/Command/McpServer/LinkFromRepositoryToolPassthroughTests.cs` |
| Unit | NUnit + NSubstitute | `update-page`/`sync-pages`/`get-component-info`: header-only probe REACHES the resolver (not short-circuited) and resolves against the header tenant or fails soft to `latest`; mixed-input → rejected before any named-tenant lookup | `PageUpdateToolTests.cs`, `PageSyncToolTests.cs`, `ComponentInfoToolTests.cs` (extend) |
| Unit | NUnit + NSubstitute | `build-theme`: header-only with `commandResolver` supplied → version resolves against header tenant; mixed-input → fail-soft to latest; `commandResolver` absent (existing test construction) → unchanged `LatestFallback` behavior; CLI-path overloads (no `resolvedSettings`) → byte-for-byte unchanged | `BuildThemeToolTests.cs`, `BuildThemeCommandTests.cs` (extend both) |
| Unit | NUnit + NSubstitute | OQ-04 guard: completeness (registry == discovered tool set) AND per-scenario mapping presence (every routed/guarded tool has a named, existing, `[Test]`-attributed method for each required scenario) | `clio.tests/Command/McpServer/PassthroughToolClassificationGuardTests.cs` |
| E2E | `clio.mcp.e2e`, extending `McpHttpMultiTenantE2ETests` | Header-only and header+`environment-name` for `list-apps`, one section tool (including its nested caption-culture path), `get-user-culture`, and one `link-from-repository-*` Creatio-reaching branch | `clio.mcp.e2e/*` |
| E2E | `clio.mcp.e2e`, extending `McpHttpConcurrencyIsolationE2ETests` | Every newly routed tool gets the same two-tenant isolation proof already established for `describe-environment` | `clio.mcp.e2e/*` |
| E2E | `clio.mcp.e2e`, extending `McpHttpNoRegressionE2ETests` | stdio + `mcp-http -e <env>` behavior for every touched tool matches pre-change baseline | `clio.mcp.e2e/*` |

**Note:** MCP e2e is **not in CI yet** — the multi-tenant, concurrency-isolation, and no-regression e2e
cases run manually until the harness is promoted, same caveat as ENG-93208.

---

## Consequences

- **Positive:** `list-apps` and the rest of the c1 family become genuinely usable under passthrough,
  including their nested culture-resolution and polling paths, not just their outermost call. The real
  active-tenant leak (`get-user-culture`, and its nested twin inside `CaptionCultureResolver`) closes. All
  four matrix tools get consistent, header-aware version resolution, `sync-pages` included. `build-theme`
  gets a mechanically sound design that does not force a bootstrap/DI redesign onto the CLI command.
  `link-from-repository-*` gets a safe, honest fail-fast plus an explicit (not implicitly-relied-upon)
  requiredness contract for its non-passthrough path.
- **Trade-offs / risks:**
  - **c1's diff is larger than a shallow read suggests**: 8 new interface overloads across 5 services plus
    `ICaptionCultureResolver`, not a single outer overload each. This is the correct size for the defect,
    not scope creep — a smaller diff was tried in Rev 1 and found to leave a real leak in place.
  - **c3 remains genuinely unusable under passthrough in v1** (by design). If ENG-92869 needs it, that is a
    follow-up story with its own local-package-path design question.
  - **`BuildThemeCommand.ResolveVersion`'s registry-lookup branch, when driven by the tool's
    `commandResolver.Resolve<EnvironmentSettings>`, goes through `ResolveSettingsAndKey`'s `.Fill(options,
    interactiveConsole)` step**, whereas today's CLI/name-based `ResolveVersion` uses the found
    `EnvironmentSettings` directly with no `Fill`. Since `build-theme`'s MCP args expose no explicit
    `uri`/`login`/`password` to fill against, this is expected to be a no-op in practice, but it should be
    covered by an explicit registered-env-via-MCP-tool regression test (slice 5) rather than assumed.
  - **Schema-level `[Required]` relaxation is a compatibility-sensitive change** for any existing MCP
    client that relies on the SDK rejecting a call with no `environment-name` before the tool runs;
    mitigated by no-regression e2e coverage, called out explicitly in the PR description.
  - **The OQ-04 coverage registry is now larger** (per-scenario rows, not per-tool rows) and can drift if a
    contributor edits a tool without touching it — mitigated by the completeness + mapping-presence tests
    failing hard, run on every PR touching `clio/Command/McpServer/**` per the module-to-source test
    policy.
  - **MCP e2e is still not wired into CI** — the deepest coverage for this fix stays manual.
- **Breaking change:** **No.** Every fix is additive: existing name-based service overloads, the CLI's
  `BuildThemeCommand` overloads/constructor, and their CLI callers are untouched; stdio and
  registered-environment MCP behavior is provably unchanged for the routed group (same resolver branch,
  same fail-soft catches) and byte-for-byte unchanged for `build-theme`'s CLI path (no `resolvedSettings`
  passed, same branch as today). The only externally visible schema change (`[Required]` removed) makes
  previously-mandatory arguments optional, which cannot break an existing valid caller. No `RELEASE.md`
  migration entry is required.

## Pre-implementation Checklist

- [ ] All new/changed MCP arguments stay kebab-case
- [ ] No MediatR, no raw `HttpClient`; all new services registered via constructor injection in
      `BindingsModule`; CLIO001/CLIO005 clean
- [ ] Every c1/c2/matrix routing fix reuses the existing `IApplicationClientFactory.CreateEnvironmentClient`
      / `IServiceUrlBuilderFactory.Create` / `ICurrentUserCultureResolverFactory.Create` /
      `IPlatformVersionResolverFactory.Create` — no new client-construction path introduced
- [ ] **No settings-based overload added in this task calls a name-based overload or
      `ISettingsRepository.FindEnvironment`/`GetEnvironment` directly** — verified per file during review,
      not assumed from the interface shape
- [ ] c1 tests cover the NESTED caption-culture and polling/readback paths for `create-app`,
      `create-app-section`, `update-app-section` — not only the initial client-creation call
- [ ] `HasExplicitCredentialArgs` mixed-input rejection is exercised (not just assumed) for every routed
      tool via an explicit test (AC-06)
- [ ] `link-from-repository-*`: the non-passthrough, no-`environment-name` case is covered by a test
      asserting the NEW explicit tool-level error, not `Link4RepoOptionsValidator`'s generic message
- [ ] `link-from-repository-by-env-package-path` with `skip-preparation=true` is confirmed NOT rejected by
      the guard
- [ ] `sync-pages`'s header-only probe test asserts the resolver is actually REACHED, not short-circuited
      by a blank-name guard clause
- [ ] `build-theme`'s existing CLI-path tests (`BuildThemeCommandTests`) and existing direct-construction
      tool test (`BuildThemeToolTests.cs:49`) pass UNCHANGED; new passthrough tests are added alongside them
- [ ] Application tools use `BaseTool<EnvironmentOptions>(null, logger, commandResolver)` +
      `ExecuteWithCleanLog(options, ...)` (the options-aware overload) — not a bare
      `commandResolver.Resolve<EnvironmentSettings>` call with no lock/in-flight guard
- [ ] OQ-04 registry lists every `[McpServerToolType]` tool name, and every routed/guarded tool has a named,
      existing, `[Test]`-attributed method for each required scenario (not "a test exists somewhere")
- [ ] Error messages are user-friendly, name the tool and the alternative, and never echo header/credential
      material
- [ ] Existing MCP unit + e2e suites identified and kept green; targeted `Category=Unit&Module=McpServer`
      filter run after EVERY slice, not deferred to the end
- [ ] MCP surface + docs updated per FR-09 — or explicitly stated as "reviewed, no update required"
- [ ] Workspace diary entry appended after completion (`.codex/workspace-diary.md`)
