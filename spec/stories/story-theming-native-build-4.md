# Story 4: `build-theme` surface — CLI verb + flat MCP tool + DI/Program wiring (toggled, dark until go-live)

**Feature**: theming-native-build (ENG-90636, epic ENG-26797 — Theming with AI, clio dev flow; native-build + bundled-template continuation)
**FR coverage**: D1, D2, D6, R-02, R-03, R-12, R-14, R-17, OQ-03 — the `build-theme` CLI + MCP surface
**PRD**: design substance in the approved migration plan ([adr-theming-native-build.md](../adr/adr-theming-native-build.md) §Context)
**ADR**: [adr-theming-native-build.md](../adr/adr-theming-native-build.md) — **Accepted**; refinements R-01…R-18 are authoritative and supersede the pre-review body
**Test plan**: [tp-theming-native-build.md](../test-plans/tp-theming-native-build.md)
**Status**: ready-for-dev
**Size**: L (full day)

> **Depends on Stories 2 + 3** (the `IThemeCssBuilder` math + the bundled `IThemeTemplateProvider`). Adds the
> `build-theme` **CLI verb** (local `Command<BuildThemeOptions>`, output modes incl. the workspace
> `theme.json` per R-17/OQ-03) and the **FLAT** `BuildThemeTool` (**`ComponentInfoTool`-style — NOT
> `BaseTool`**, per R-02: constructor-injects `BuildThemeCommand` and delegates both build and write to it).
> Both gated by **`[FeatureToggle("theming")]`** on the **options class AND the tool type** (R-12
> toggle-only, **dark until the surface is complete + go-live approved**). The MCP tool has the **same two
> output modes** as the verb: compute (returns `css`) and workspace-write (`workspaceDirectory`+`packageName`
> ⇒ writes `theme.css`+`theme.json` into `<ws>/packages/<pkg>/Files/themes/<cssClassName>/`, returns `path`
> with no CSS payload — token cost, D1). Safety flags
> `ReadOnly=false/Destructive=false/Idempotent=true/OpenWorld=false` (`ReadOnly=false` because the
> workspace-write mode writes local files; it still never mutates an environment — D1/R-17). DI in BindingsModule + Program
> wiring (**full-suite trigger**) + **mandatory `clio.mcp.e2e`** (NOT in CI — manual).

---

## As a

developer (workspace/dev flow) and AI coding agent (no-code/server flow) producing theme CSS in-process

## I want

a `build-theme` CLI verb and a flat MCP tool that read the bundled template, run the ported math, and return the
`theme.css` string

## So that

theme CSS is produced deterministically in clio — written into workspace package files (CLI `--output` **or
the MCP tool's workspace-write mode**, which keeps the large CSS out of the agent context) or piped into
`create-theme-by-environment`'s `css-content` (MCP compute mode) — without shelling out to Node, composing
with both flows (D1)

---

## Acceptance Criteria

- [ ] **AC-01 (R-02, flat tool)** — Given `BuildThemeTool`, when inspected, then it is a `[McpServerToolType]`
  that **does NOT derive from `BaseTool`** and **constructor-injects `BuildThemeCommand`** directly (assert via
  reflection on base type) — the `ComponentInfoTool` shape, **not** the `BaseTool`/`IToolCommandResolver` pattern
  (`BaseTool.ResolveFromCallContainer` throws for any non-`EnvironmentOptions` type, so `BaseTool` is wrong here).
  **No `BaseTool.cs` edit.** It returns `BuildThemeResult { success, css?, descriptor?, path?, warnings?, error? }`
  (`css`/`descriptor` in compute mode; `path` in workspace-write mode). (R-02; TC-U-11.)
- [ ] **AC-02 (arg mapping + safety flags + workspace-write mode)** — Given the tool, when invoked, then `primary`,
  `secondary`, `accent`, `success`, `error`, `css-class-name`, `heading-font`, `body-font`, `font-weights`,
  optional `version`/`environment-name`, **and optional `workspaceDirectory`+`packageName`** map onto the build;
  it delegates to `BuildThemeCommand` (which reads the bundled template and calls `IThemeCssBuilder.Build`). With
  `workspaceDirectory`+`packageName` omitted it returns `css`+`descriptor`; with both given (workspaceDirectory a
  **fully-qualified absolute path**, packageName an existing package) it writes `theme.css`+`theme.json` into
  `<ws>/packages/<pkg>/Files/themes/<cssClassName>/` (resolved via `IWorkspacePathBuilder`) and returns `path`
  (no CSS payload — token cost, D1); one-without-the-other, a non-absolute workspaceDirectory, a non-workspace
  directory, or a missing package is a graceful `success:false`. The `[McpServerTool]` flags are
  **`ReadOnly=false, Destructive=false, Idempotent=true, OpenWorld=false`** and the `[Description]` routes the
  agent to `get-guidance theming`. (D1, R-03; TC-U-12.)
- [ ] **AC-03 (R-17 env-effect semantics)** — Given the safety flags, when documented, then the tool
  **never mutates an environment** (the optional `--environment-name` only *resolves the template version*, R-03).
  `ReadOnly=false` reflects the **local filesystem** write of the workspace-write mode, not an environment effect;
  reading the bundled template and the version probe remain read-only. (R-17; TC-U-12.)
- [ ] **AC-04 (feature toggle — both surfaces)** — Given `[FeatureToggle("theming")]`, when registration
  runs, then it is on **both** `BuildThemeOptions` (the `[Verb]` options class) **and** the
  `[McpServerToolType] BuildThemeTool`; with the toggle **off**, `McpFeatureToggleFilter.RegisterEnabledPrimitives`
  (`IEnumerable<Type>`) does **not** register the tool and the verb is filtered at parse + dispatch; with it
  **on**, both appear. The predicate is `IFeatureToggleService.IsEnabled(typeof(BuildThemeTool))`
  (case-insensitive key) — never a `Type[]`/`*FromAssembly` path. (D6, R-12; TC-U-13, TC-U-33.)
- [ ] **AC-05 (CLI stdout mode)** — Given `build-theme --primary <hex> --css-class-name <name>` with `--output`
  omitted, when the command runs, then it reads the bundled template, calls `IThemeCssBuilder.Build`, writes the
  `theme.css` string to stdout, and exits 0 — **no environment touched** except an optional version-resolve
  when `--environment-name` is given (R-03). (D1, OQ-03; TC-U-30.)
- [ ] **AC-06 (workspace-write mode, R-17/OQ-03)** — Given `--output <dir>` (CLI) **or the MCP tool's
  `workspaceDirectory`+`packageName`**, when it runs, then it writes **both** `theme.css` (built) and `theme.json` (filled from the bundled
  `theme.json.tpl` — Story 3, accepting `--id`/`--caption`; auto-UUID / css-class-name when omitted). The MCP
  **compute** mode (feeding `create-theme`'s `css-content`) never gets `theme.json` — `create-theme` takes
  id/caption/cssClassName as separate args (D5/OQ-03). (D1, OQ-03, R-17; TC-U-31, TC-I-01.)
- [ ] **AC-07 (version resolution — `--version` xor `--environment-name`)** — Given the version flags,
  when the command runs, then they are **mutually exclusive** (both ⇒ error; neither ⇒ highest bundled);
  `--environment-name` resolves via `ISettingsRepository.FindEnvironment` (unregistered ⇒ clear error) → `IPlatformVersionResolverFactory`/`PlatformVersionResolver` (unresolvable/"latest" ⇒ highest bundled). (R-03; TC-U-32.)
- [ ] **AC-08 (CLI flags, kebab-case)** — Given the flag set, when inspected, then all long names are
  kebab-case (CLIO001): `--primary` (required), `--secondary`/`--accent`/`--success`/`--error` (optional,
  derived/defaulted), `--css-class-name` (required, `^[A-Za-z][A-Za-z0-9_-]*$`, ≤100), `--heading-font`/
  `--body-font`/`--font-weights` (optional; default Montserrat 400/500/600 ⇒ no `@import`), `--id`/`--caption`
  (optional, theme.json fields), `--output` (optional; dir ⇒ workspace mode, omitted ⇒ stdout), and optional
  `--version` / `--environment-name` (mutually exclusive, R-03). (CLI flag table.)
- [ ] **AC-09 (too-old/invalid version handling)** — Given a `--version` below the lowest bundled
  template (the provider throws `ArgumentException`), then the CLI returns a graceful `Error: …` + non-zero exit
  and the tool catch-all returns `BuildThemeResult { Success=false, Error=<the too-old message> }` — **no** unhandled
  exception on the JSON-RPC channel. (R-14, D5, R-02; TC-U-14.)
- [ ] **AC-10 (DI + Program wiring — full-suite trigger)** — Given the composition root, when wired, then
  `IThemeCssBuilder`, `IThemeTemplateProvider`, `BuildThemeCommand`, and `BuildThemeTool` are registered in
  `BindingsModule.cs`, and `typeof(BuildThemeOptions)` is added to `Program.cs`'s `CommandOption` list with a
  dispatch arm — verifying how a local (no-environment) command is dispatched in `Program.cs` rather than
  assuming the `EnvironmentOptions` path (R-02 caveat). **This change touches `BindingsModule.cs` + `Program.cs`
  ⇒ the full `Category=Unit` suite is mandatory (smart-regression rule 4).** (D2.)
- [ ] **AC-ERR** — Given an MCP call with an empty required arg or an invalid hex, or a CLI invocation with the
  same, when it runs, then it returns a graceful failure — MCP → `BuildThemeResult { success:false, error }`;
  CLI → `Error: …` + non-zero exit — not an unhandled exception (no bare `catch (Exception)`).

## Implementation Notes

The math (`IThemeCssBuilder`, Story 2) and the bundled template provider (`IThemeTemplateProvider`, Story 3) are inputs.
**Review MCP artifacts per the AGENTS.md MCP maintenance policy; use the `create-mcp-tool` skill for the tool
and `test-mcp-tool` for the tests.** The verb is gated, so it is **omitted from generated public docs until the
toggle flips on** (Story 8) — that is the deliberate toggle/docs-omission interaction (Story 6 writes the docs;
they land publicly at go-live).

**Key file (create): `clio/Command/Theming/BuildThemeCommand.cs`** (namespace `Clio.Command.Theming`; ADR
Implementation Plan, D1, D2, R-03, R-12, OQ-03) — `BuildThemeOptions` (`[Verb("build-theme")]`,
**`[FeatureToggle("theming")]`**) + `BuildThemeCommand : Command<BuildThemeOptions>` (local — no
environment); resolve version (`--version` xor `--environment-name`) → read bundled template via
`IThemeTemplateProvider` → `IThemeCssBuilder.Build` → `--output`/stdout; workspace mode also writes
`theme.json` (from the bundled `theme.json.tpl`). Verify local-command dispatch in `Program.cs` (R-02 caveat —
do **not** assume the `EnvironmentOptions` path).

**Key file (create): `clio/Command/McpServer/Tools/BuildThemeTool.cs`** (ADR Implementation Plan, D1, D6, R-02,
R-03, R-17) — `[McpServerToolType]` + **`[FeatureToggle("theming")]`**; a **flat** `build-theme` tool
(`ComponentInfoTool` shape — constructor-injects `BuildThemeCommand` and delegates build + write to it, **NOT**
`BaseTool`, no resolver machinery); returns `BuildThemeResult { success, css?, descriptor?, path?, warnings?,
error? }`; flags `ReadOnly=false/Destructive=false/Idempotent=true/OpenWorld=false`; `[Description]` →
`get-guidance theming`; optional `version`/`environment-name` (R-03) **and optional `workspaceDirectory`+`packageName`**
(⇒ write `theme.css`+`theme.json` into `<ws>/packages/<pkg>/Files/themes/<cssClassName>/`, return `path`, no CSS
payload — token cost, D1). The tool validates `workspaceDirectory` as a fully-qualified absolute path + `packageName`
as an identifier (mirror `CreateUiProjectTool`); `BuildThemeCommand` resolves + validates the workspace/package via
`IWorkspacePathBuilder` (`RootPath`/`IsWorkspace`/`BuildPackagePath`) before writing.

**Key file (create): `clio/Command/McpServer/Tools/` model `BuildThemeResult`** — `record { bool success;
string css; string descriptor; string path; IReadOnlyList<string> warnings; string error; }` (mirrors
`ComponentInfoTool`'s structured result + `ListThemesResult`; `warnings` follows `ApplicationDataForgeResult.Warnings`;
`css`/`descriptor` omitted in workspace-write mode, `path` omitted in compute mode).

**Key files (modify): `clio/BindingsModule.cs`** — register `IThemeCssBuilder` (if not in Story 2),
`IThemeTemplateProvider` (if not in Story 3), `BuildThemeCommand`, `BuildThemeTool` (**full-suite trigger**).
**`clio/Program.cs`** — add `typeof(BuildThemeOptions)` to the `CommandOption` list + a dispatch arm
(**full-suite trigger**).

**Key file (create): `clio.mcp.e2e/BuildThemeToolE2ETests.cs`** (MANDATORY per AGENTS.md MCP policy — NOT in
CI, manual): the real `clio mcp-server` advertises `build-theme` with the four flags (`ReadOnly=false`) + the
`get-guidance theming` description when the toggle is enabled in the harness (bundled template); it is
**absent** when the toggle is off; invoking with a valid `primary` + `css-class-name` (compute mode) returns
`BuildThemeResult { success:true, css:<valid theme.css> }`; invoking with `workspaceDirectory`+`packageName` writes
`theme.css`+`theme.json` into the package and returns `BuildThemeResult { success:true, path:<theme dir> }` (no CSS payload).

**Registration:** gated MCP types flow through `McpFeatureToggleFilter.RegisterEnabledPrimitives`
(`IEnumerable<Type>`). Do **NOT** pass a `Type[]` (binds to the generic `WithX<T>(T)` overload — registers
nothing) and do **NOT** revert to `*FromAssembly` (project-context.md + AGENTS.md MCP caveat). Do not add an
abstract/open-generic exclusion.

Pattern to follow: `ComponentInfoTool` (the flat constructor-injected tool + version-resolution +
`resolvedFrom` caveat + error→`success:false` — the **correct** prior art, R-02); `ListThemesTool`
(structured-result shape); `BuildThemeOptions`/`Program.cs` local-command dispatch (verify, R-02);
`clio/Command/McpServer/AGENTS.md` (MCP rules — the `BaseTool` exemption that `ComponentInfoTool` exercises).

## Test Requirements

| Type | What to test | File |
|------|-------------|------|
| Unit `[Category("Unit")]` `[Property("Module","McpServer")]` | **TC-U-11** flat `ComponentInfoTool`-style tool shape (NOT `BaseTool`; ctor-injects `BuildThemeCommand`; no `BaseTool.cs` edit); **TC-U-12** arg mapping → build, safety flags `false/false/true/false`, description → guidance, R-17 env-effect note, **workspace-write mode (`workspaceDirectory`+`packageName`) writes `theme.css`+`theme.json` into the package and returns `path` (no CSS); non-absolute workspaceDirectory / non-workspace / missing package / one-without-the-other → graceful `success:false`** | `clio.tests/Command/McpServer/BuildThemeToolTests.cs` |
| Unit `[Category("Unit")]` `[Property("Module","McpServer")]` | **TC-U-13** feature-toggle gating on the tool type (off → not registered via `RegisterEnabledPrimitives`; on → registered; never `Type[]`/`*FromAssembly`); **TC-U-14** too-old/invalid `version` (`ArgumentException`) → graceful `success:false` (no throw to the protocol) | `BuildThemeToolTests.cs` |
| Unit `[Category("Unit")]` `[Property("Module","Command")]` | **TC-U-30** stdout mode (no `--output`); **TC-U-31** workspace-dir mode writes `theme.css` + `theme.json`; **TC-U-32** `--version` xor `--environment-name` (both ⇒ error; neither ⇒ highest bundled); **TC-U-33** `[FeatureToggle("theming")]` on `BuildThemeOptions` + dispatch-chokepoint gating | `clio.tests/Command/BuildThemeCommandTests.cs` |
| Integration `[Category("Integration")]` `[Property("Module","Command")]` | **TC-I-01** workspace-dir output writes real `theme.css` + `theme.json` (byte-for-byte / substituted id/caption/cssClassName); **TC-I-02** the bundled `tpl/themes/{version}/` template is read end-to-end through the CLI (no env var, no network) | `BuildThemeCommandTests.cs` (Integration cases) |
| E2E `[Category("E2E")]` (manual, NOT in CI) | **TC-E2E-01** real `clio mcp-server` advertises `build-theme` with `ReadOnly=false/Destructive=false/Idempotent=true/OpenWorld=false` + `get-guidance theming` description (toggle on, bundled template); compute mode produces valid `theme.css`; **workspace-write mode (`workspaceDirectory`+`packageName`) writes `theme.css`+`theme.json` into the package and returns `path`**; **absent** when toggle off | `clio.mcp.e2e/BuildThemeToolE2ETests.cs` |

- Command fixtures derive from `BaseCommandTests<BuildThemeOptions>`, resolve the SUT from `Container`,
  register substitute `IThemeCssBuilder` + `IThemeTemplateProvider` in `AdditionalRegistrations`, `ClearReceivedCalls`
  in teardown. The flat MCP-tool fixture mirrors `ComponentInfoToolTests` (constructor-injected substitutes —
  **NOT** the `BaseTool`/`IToolCommandResolver` pattern).
- MCP work is **incomplete without `clio.mcp.e2e` coverage** — unit mapping tests alone are insufficient per
  AGENTS.md / the `test-mcp-tool` skill. **Flag in the test plan / PR: MCP E2E is NOT in CI — manual only.**
  TC-E2E-01 is the **only** check that catches a silent-no-op (`Type[]`/`*FromAssembly`) registration.
- `[Category("Unit")]`/`[Category("Integration")]`/`[Category("E2E")]` (never `[Category("UnitTests")]`);
  naming `MethodName_ShouldBehavior_WhenCondition` (e.g.
  `BuildThemeTool_ShouldNotDeriveFromBaseTool_WhenInspected`,
  `RegisterEnabledPrimitives_ShouldOmitBuildThemeTool_WhenToggleOff`,
  `Execute_ShouldWriteThemeCssAndThemeJson_WhenOutputIsWorkspaceDir`).
- AAA + a `because` on every assertion + `[Description]` on every test; Integration cases under the OS temp dir,
  UTF-8, deleted in teardown.

## Definition of Done

- [ ] Code compiles without Roslyn analyzer warnings (CLIO001-CLIO005); all CLI long names kebab-case
- [ ] `BuildThemeTool` is **flat** (`ComponentInfoTool`-style — NOT `BaseTool`), constructor-injects `BuildThemeCommand`, returns `BuildThemeResult { success, css?, descriptor?, path?, warnings?, error? }`, **no `BaseTool.cs` edit** (R-02)
- [ ] `[FeatureToggle("theming")]` on **both** `BuildThemeOptions` **and** the `[McpServerToolType] BuildThemeTool`; MCP via `RegisterEnabledPrimitives` (`IEnumerable<Type>`) — never `Type[]`/`*FromAssembly` (D6, R-12)
- [ ] Safety flags `ReadOnly=false/Destructive=false/Idempotent=true/OpenWorld=false` (`ReadOnly=false` = local write in workspace-write mode); description → `get-guidance theming`; R-17 env-effect note documented (never mutates an environment; `--environment-name` probe read-only)
- [ ] CLI: stdout mode, workspace-dir mode (`theme.css` + `theme.json`, OQ-03), `--version` xor `--environment-name`; local-command dispatch verified in `Program.cs` (R-02 caveat)
- [ ] MCP tool: compute mode (returns `css`) + workspace-write mode (`workspaceDirectory`+`packageName` ⇒ writes `theme.css`+`theme.json` into `<ws>/packages/<pkg>/Files/themes/<cssClassName>` via `IWorkspacePathBuilder`, returns `path`, no CSS payload; non-absolute workspaceDirectory / non-workspace / missing package / one-without-the-other → graceful `success:false`)
- [ ] Too-old/invalid version → graceful `success:false` (R-02/D5)
- [ ] DI registered in `BindingsModule.cs`; `typeof(BuildThemeOptions)` added to `Program.cs` `CommandOption` + dispatch arm
- [ ] **Full `Category=Unit` suite run** (BindingsModule/Program touched — rule 4) in addition to targeted `Module=Theming|McpServer|Command`
- [ ] Unit + Integration tests `[Category("Unit")]`/`[Category("Integration")]` — never `[Category("UnitTests")]`
- [ ] `clio.mcp.e2e` `build-theme` coverage added (toggle-on advertise `ReadOnly=false` + toggle-off absent + compute-mode CSS + workspace-write mode writes files into the package and returns `path`) — flag: NOT in CI, manual
- [ ] Targeted run: `dotnet test --filter "Category=Unit&(Module=Theming|Module=McpServer|Module=Command)"` + full suite (e.g. `Validated: dotnet test --filter "Category=Unit"`)
- [ ] MCP reviewed: state the outcome per the AGENTS.md MCP maintenance policy
- [ ] `.codex/workspace-diary.md` entry appended
- [ ] PR description references this story file

## Dev Agent Record

{Left blank — filled by dev agent during implementation}
- Implementation started:
- Implementation completed:
- Tests passing:
- MCP E2E (manual) run:
- Notes:
