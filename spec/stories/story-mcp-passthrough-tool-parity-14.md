# Story 14: `build-theme` — Pattern B redesign (tool-resolved settings passed into a new command overload)

**Feature**: mcp-passthrough-tool-parity
**FR coverage**: FR-03, FR-10
**PRD**: [prd-mcp-passthrough-tool-parity.md](../prd/prd-mcp-passthrough-tool-parity.md)
**ADR**: [adr-mcp-passthrough-tool-parity.md](../adr/adr-mcp-passthrough-tool-parity.md)
**Status**: ready-for-dev
**Size**: L (full day)

---

## As a

CI pipeline author (AI-Platform gateway operator)

## I want

`build-theme` (`BuildThemeTool`/`BuildThemeCommand`) to resolve its platform version against the header
tenant when possible, and to fall back to the documented `LatestFallback` — never a silent, unauthenticated
"newest template" guess — under passthrough, while its CLI path stays observably unchanged

## So that

the one genuinely header-blind, environment-version-sensitive matrix tool gets a mechanically sound fix
(the `InternalExecute<BuildThemeCommand>` design was proven broken in the ADR's review — verification #5)
without forcing a bootstrap/DI redesign onto the CLI command

---

## Acceptance Criteria

- [x] **AC-01** (PRD AC-04, decision-matrix "Route — Pattern B, corrected") — Given authorized passthrough
  header-only with a blank `--version`, when `build-theme` runs, then the **tool** attempts
  `commandResolver.Resolve<EnvironmentSettings>(new EnvironmentOptions { Environment = args.EnvironmentName
  })`; if it succeeds, the command resolves the platform version via `_resolverFactory.Create(resolvedSettings)`
  against the **header** tenant — not the newest-template guess that is header-blind today.
- [x] **AC-02** — Given the resolver throws (no environment/URI available, or the mixed-input rejection
  fires), when `build-theme` runs, then it catches the exception, sets `resolvedSettings = null`, and falls
  back to `LatestFallback` — the same documented fallback marker the CLI's offline path produces (fail-soft,
  matching the sibling matrix tools' probe shape exactly).
- [x] **AC-03** — **Mixed input (PRD AC-06).** Given authorized passthrough with both the header and an
  explicit `environment-name` naming a different registered environment, when `build-theme` runs, then the
  resolver throws (`HasExplicitCredentialArgs` rejection) before any Creatio-reaching call, `resolvedSettings`
  is null, and the result is `LatestFallback` — no named-tenant probe occurs (closes AC-06 even though the
  user-facing signal is "latest" rather than an explicit error, matching `update-page`/`sync-pages`'s
  documented shape).
- [x] **AC-04** (PRD AC-09 / SM-03, ADR verification #5 regression guard) — Given the existing direct-
  construction unit test `new BuildThemeTool(command, Substitute.For<ILogger>())` (`BuildThemeToolTests.cs:49`,
  no resolver supplied), when it runs, then it passes **unchanged** — the new `IToolCommandResolver?
  commandResolver = null` constructor parameter must be optional and default to `null` so this test keeps
  compiling and passing.
- [x] **AC-05** — **Observable parity for the CLI path.** Given the CLI's `TryBuildTheme(options, out ...)` /
  `TryBuildTheme(options, workspaceDirectory, packageName, out ...)` overloads (no `resolvedSettings`
  argument), when they are called from the CLI, then their behavior is observably unchanged: same results
  for the same inputs, same `ResolveVersion(options)` by-name path, same repository/factory call sequence,
  same `LatestFallback` marker on the offline branch — and **all existing `BuildThemeCommandTests` pass
  without modification**.
- [x] **AC-06** — Given a registered environment supplied via the MCP tool (non-passthrough, `environment-name`
  present), when `build-theme` runs, then the tool's `commandResolver.Resolve<EnvironmentSettings>` performs
  the registry lookup and the version resolves against it directly — **explicitly covered by a regression
  test**, not assumed, per the ADR's Consequences note about `ResolveSettingsAndKey`'s `.Fill` step (since
  `build-theme`'s MCP args expose no explicit `uri`/`login`/`password` to fill against, this is expected to be
  a no-op, but the test proves it).
- [x] **AC-07** — **Explicit `--version` precedence.** Given a non-blank `--version` (any transport, any
  header state), when `build-theme` runs, then the explicit version wins and **no settings-resolution attempt
  is made at all** (the tool's `Resolve<EnvironmentSettings>` guard is `string.IsNullOrWhiteSpace(args.Version)`)
  — `--version` stays mutually exclusive with `--environment-name`, unchanged.
- [x] **AC-ERR** — **Error semantics respect the ENG-93208 middleware boundary (PRD AC-ERR).**
  (a) A **malformed/unusable** header is out of scope: the middleware returns HTTP 400 **before** any tool
  is invoked (`McpHttpServerCommand.cs:317`) — the tool is never entered and must not add handling for it.
  (b) Given a **valid** header where the version resolution fails, the probe fails soft to `LatestFallback`
  (AC-02); given a **valid** header where the build itself fails, the tool returns
  `BuildThemeResult.Failure(...)` with `SensitiveErrorTextRedactor`-redacted text — no secret material leaks.

## Implementation Notes

**The Rev-1 `InternalExecute<BuildThemeCommand>` design is rejected and removed** — `BuildThemeOptions` is
not `EnvironmentOptions`-derived, and `BaseTool.ResolveFromCallContainer` throws `InvalidOperationException`
for any non-`EnvironmentOptions` type (`clio/Command/McpServer/Tools/BaseTool.cs:222-226`);
`InternalExecute<TCommand>`'s `CommandExecutionResult` return type also cannot substitute for the tool's
`BuildThemeResult` contract (ADR verification #5). Use **Pattern B** instead:

```csharp
// BuildThemeTool — optional resolver, nullable + defaulted:
public BuildThemeTool(BuildThemeCommand command, ILogger logger, IToolCommandResolver? commandResolver = null) { ... }

EnvironmentSettings? resolvedSettings = null;
if (string.IsNullOrWhiteSpace(args.Version) && commandResolver is not null) {
    try {
        resolvedSettings = commandResolver.Resolve<EnvironmentSettings>(
            new EnvironmentOptions { Environment = args.EnvironmentName });
    } catch (Exception) {
        resolvedSettings = null; // fail-soft
    }
}
return ExecuteWithCleanLog(() => {
    bool ok = writeToPackage
        ? command.TryBuildTheme(options, resolvedSettings, args.WorkspaceDirectory, args.PackageName, out ..., out ..., out string writeError)
        : command.TryBuildTheme(options, resolvedSettings, out ..., out ..., out ..., out string buildError);
    return ok ? BuildThemeResult.Successful(...) : BuildThemeResult.Failure(...);
});
```

```csharp
// BuildThemeCommand — TWO new overloads, existing ones untouched:
public bool TryBuildTheme(BuildThemeOptions options, EnvironmentSettings? resolvedSettings, out string css, out string descriptor, out IReadOnlyList<string> warnings, out string error);
public bool TryBuildTheme(BuildThemeOptions options, EnvironmentSettings? resolvedSettings, string workspaceDirectory, string packageName, out string writtenPath, out IReadOnlyList<string> warnings, out string error);
```

New private `ResolveVersion(BuildThemeOptions options, EnvironmentSettings? resolvedSettings)`: (1) explicit
`--version` still wins; (2) else, if `resolvedSettings` supplied, resolve via `_resolverFactory.Create(
resolvedSettings)` directly, no repository call; (3) else `LatestFallback` — the CLI never passes
`resolvedSettings` at all and always hits this branch exactly as it does today.

Key files: `clio/Command/McpServer/Tools/BuildThemeTool.cs`, `clio/Command/Theming/BuildThemeCommand.cs`
(path verified against the current tree).
Pattern to follow: sibling matrix tools' fail-soft catch shape (`update-page`/`sync-pages`, Stories 11/12) —
identical mixed-input handling, applied here via the tool-resolved-settings parameter instead of an inline
swap.

## Test Requirements

| Type | What to test | File |
|------|-------------|------|
| Unit `[Category("Unit")]` | Header-only with `commandResolver` supplied: version resolves against header tenant; mixed-input: fail-soft to `LatestFallback`; `commandResolver` absent (existing direct-construction test): unchanged `LatestFallback` behavior; registered-env-via-tool: regression test per AC-06; explicit `--version`: no resolution attempt (AC-07) | `BuildThemeToolTests.cs` (extend) |
| Unit `[Category("Unit")]` | New `TryBuildTheme` overloads: `resolvedSettings` supplied uses `_resolverFactory.Create` directly (no repository call); `resolvedSettings` null falls back to `LatestFallback`; existing name-based overloads/CLI path unchanged | `BuildThemeCommandTests.cs` (extend) |
| Integration `[Category("Integration")]` | none required | — |
| E2E `[Category("E2E")]` | Owned by **Story 15**: two-tenant isolation for the newly routed version path + stdio/`-e` no-regression. Manual only — MCP e2e not in CI | `clio.mcp.e2e/` (Story 15) |

Test naming: `MethodName_ShouldBehavior_WhenCondition`

## Definition of Done

- [x] All `CLIO*` diagnostics clean in changed files — including CLIO005 (FR-10)
- [x] Targeted tests green before commit: `dotnet test clio.tests/clio.tests.csproj --filter
  "Category=Unit&(Module=McpServer|Module=Command)" --no-build` (ADR slice 9)
- [x] All new CLI flags are kebab-case (no new CLI flags in this story; `BuildThemeCommand`'s CLI overloads/
  constructor are unchanged)
- [x] Unit tests added with `[Category("Unit")]` — never `[Category("UnitTests")]`
- [x] Existing `BuildThemeCommandTests` and `BuildThemeToolTests.cs:49` pass unchanged (see Notes for the
  3 pre-existing `BuildThemeToolTests` cases that were *updated*, not left byte-identical, because Pattern B
  changes their observable behavior — flagged for architect review, not silently done)
- [ ] PR description references this story file (no PR opened in this session — implementation only, per
  work order)

## Dev Agent Record

- Implementation started: 2026-07-11
- Implementation completed: 2026-07-11
- Tests passing: `dotnet test clio.tests/clio.tests.csproj -c Release -f net10.0 --filter
  "Category=Unit&FullyQualifiedName~BuildTheme" --no-build` → 52/52 passed (32 `BuildThemeToolTests`,
  20 `BuildThemeCommandTests`). Full gate `dotnet test clio.tests/clio.tests.csproj -c Release -f net10.0
  --filter "Category=Unit&(Module=McpServer|Module=Command)" --no-build` → 4196 passed, 0 failed, 30 skipped
  (a transient unrelated failure in `GetUserCultureToolPassthroughTests.cs`, a concurrent story-12 agent's
  in-progress file, self-resolved on re-run and is untouched by this story).
- Notes:
  - Implemented Pattern B exactly as specified: `BuildThemeTool` gained an optional, defaulted
    `IToolCommandResolver? commandResolver = null` constructor parameter; `BuildThemeCommand` gained two new
    `TryBuildTheme` overloads (core + workspace-write) taking `EnvironmentSettings? resolvedSettings`, plus a
    new private `ResolveVersion(options, resolvedSettings)` overload. The existing `ResolveVersion(options)`
    (CLI, by-name) and existing `TryBuildTheme` overloads are byte-identical/untouched.
  - **Deviation (flagged, not silent):** 3 pre-existing `BuildThemeToolTests` cases
    (`BuildTheme_ShouldResolveVersionFromEnvironment_WhenEnvironmentNameProvided`,
    `BuildTheme_ShouldReturnFailure_WhenEnvironmentVersionUnresolved` →
    `BuildTheme_ShouldFailSoftToLatestFallback_WhenEnvironmentVersionUnresolved`,
    `BuildTheme_ShouldReturnFailure_WhenEnvironmentNotRegistered` →
    `BuildTheme_ShouldFailSoftToLatestFallback_WhenEnvironmentNotRegistered`) previously asserted the TOOL
    hard-fails on an unresolvable/unregistered `environment-name` via `_settingsRepository.FindEnvironment`
    called directly from the (old, no-resolver) tool construction. Under Pattern B the tool's ONLY environment
    knowledge is `IToolCommandResolver` (AC-02's catch-all `catch (Exception)` fails soft to `LatestFallback`
    for ANY resolver failure, not just the mixed-input rejection), so a raw 2-arg tool construction with no
    resolver — or a resolver that throws — can no longer reproduce the old by-name hard-fail; it now succeeds
    via `LatestFallback`. This is mandated by AC-02 and the ADR's own explicit DoD/checklist scoping
    ("`BuildThemeToolTests.cs:49`" — singular, the constructor-pattern line only — not the whole file); the
    CLI's equivalent hard-fail behavior remains fully covered, unmodified, in `BuildThemeCommandTests`
    (`Execute_ShouldFail_WhenEnvironmentVersionUnresolved`, `Execute_ShouldFail_WhenEnvironmentNotRegistered`).
    I updated these 3 tests' bodies (kept/adjusted names) to assert the new fail-soft behavior instead of
    leaving them failing or deleting them. Architect: please confirm this reading of AC-04/ADR checklist
    scoping is correct.
  - Added `IToolCommandResolver`-based MCP passthrough tests: header-only (AC-01), mixed-input fail-soft
    (AC-02/AC-03), no-resolver regression guard (AC-04), registered-env-via-tool (AC-06, folded into the
    updated environment-name test above), explicit `--version` precedence (AC-07).
  - Added `BuildThemeCommandTests` coverage for both new `TryBuildTheme` overloads: resolvedSettings supplied
    → `_resolverFactory.Create` called directly, `ISettingsRepository.FindEnvironment` never called;
    resolvedSettings null → `LatestFallback`, neither repository nor factory called; explicit `--version`
    still wins over a supplied resolvedSettings.
  - Updated `BuildThemeArgs.EnvironmentName`'s MCP `[Description]` to document the new fail-soft-to-latest
    behavior (was previously silent on this), since it is an observable behavior change for AI callers under
    the MCP tool surface.
  - MCP docs/guidance reviewed (`ThemingGuidanceResource.cs`, `docs/McpCapabilityMap.md`,
    `help/en/build-theme.txt`, `docs/commands/build-theme.md`): none describe the tool-level hard-fail
    semantics that changed, so no update required beyond the `[Description]` tweak above.
