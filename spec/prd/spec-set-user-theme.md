# SPEC: Apply created theme to the current user's profile (`set-user-theme`)

- **Jira:** [ENG-93302](https://creatio.atlassian.net/browse/ENG-93302) — "Branding agent. New theme is saved to user's profile"
- **Parent epic:** ENG-26797 — Theming. Ability to manage Creatio appearance using no-code tools
- **Builds on:** ENG-91387 (theming server flow: `create-theme`/`list-themes`/… CLI + MCP), ENG-90636 (dev flow + advisor), consolidated design in `spec/adr/adr-theming.md`
- **Status:** requirements draft (pre-implementation)

## 1. Problem statement

The branding-agent flow (advise palette → build CSS → `create-theme`) ends at theme
*creation*. The only "apply" mechanism clio offers today is the **global**
`DefaultTheme` system setting (all users, explicit confirmation required per ADR
D-D6). There is no way to apply the freshly created theme to the person who asked
for it. ENG-93302 closes that gap:

> After creating a new theme the agent automatically sets it to the current user's
> profile, so all the user has to do is refresh the page.

## 2. Actors

| Actor | Role in this feature |
|---|---|
| **End user** (no-code creator / business analyst) | Runs the branding agent against an environment using **their own Creatio account**; expects the new theme active after a page refresh. |
| **Coding agent** (Claude Code / any MCP client) | Orchestrates the theming flow via clio MCP tools; after a successful `create-theme` it applies the theme to the current user. |
| **clio** (this repo) | Provides the new capability: CLI command + MCP tool + updated agent guidance + docs. |
| **Creatio server (10.0+)** | Persists the choice: DataService `UpdateQuery` on virtual entity `SysUserProfile` → `SysProfileData["Theme"]`; Angular Shell reads it at startup. |
| **Creatio AI Toolkit** (UC team) | Potential downstream consumer of the same clio capability. **Open question — see §9.** |

**Scoping decision (confirmed with task owner):** "current user" = the account clio
authenticates with for the environment. In the branding-agent scenario the person
runs clio/MCP under their own Creatio credentials, so authenticated user == end
user. Setting a theme for a *different* user is out of scope.

## 3. Server contract (verified against Creatio 10 source)

Verified in `C:\Projects\Autodeploy\creatio10` (`Terrasoft.Configuration\Pkg\CrtBase`,
`CrtNUI`, `CrtUIv2` and the Angular Shell bundle):

1. `SysUserProfile` is a **virtual entity** (no table). A DataService `SelectQuery`
   on it returns exactly **one row — the current user's profile** (the registered
   `SysUserProfileQueryExecutor` ignores filters and resolves
   `UserConnection.CurrentUser`). The row `Id` **equals the current user's
   SysAdminUnit Id**.
2. Applying a theme = DataService `UpdateQuery`:
   - `rootSchemaName`: `SysUserProfile`
   - `columnValues`: `Theme` = **the theme's `Id`** (as returned by `list-themes`,
     e.g. `custom-theme-1`, `abc`) — **NOT the `cssClassName`**. Verified live
     2026-07-15 against localhost: the Freedom UI Shell calls
     `themeRegistryService.getThemeById(<profile Theme value>)` and maps it to the
     theme's cssClassName for the body class. Writing the cssClassName is stored
     faithfully but `getThemeById` returns nothing → the Shell silently falls back
     to the default (`freedom-theme`). (Correction to the original source-only note
     that said the column stores the cssClassName.)
   - filter: `Id` == current user's SysAdminUnit Id (obtained from step 1)
   - **read-back caveat:** the server stores whatever string is sent without
     validating it against a real theme, so the read-back verification confirms the
     *write landed* (catches the `ChangeTheme`-off silent no-op) but cannot catch a
     *wrong-but-stored* value — correctness depends on writing the Id.
3. Server side, `SysUserProfileEntityEventListener.OnSaving` persists it as
   `SysProfileData["Theme"] = {"ThemeId":"<cssClassName>"}`.
4. Shell startup resolution order: **profile `Theme` (id) → `DefaultTheme`
   SysSetting → built-in default**. The theme is read **only at page load**, so an
   already-open session keeps its theme until refreshed; a fresh load/refresh picks
   up the change (verified live 2026-07-15: changing the profile did not alter an
   open tab's body class until reload). A refresh is sufficient — no cache flush.
5. Server-side gates on the write (throw / no-op if unmet):
   - feature **`ChangeTheme`** enabled (silent no-op when off — the write "succeeds"
     but nothing is saved);
   - license operation **`CanCustomizeBranding`** (throws);
   - system operation **`CanChangeOwnTheme`** (throws; granted to *All employees*
     and *All external users* out of the box).
6. Setting `Theme` to an empty string resets the user to the `DefaultTheme`
   fallback.
7. No dedicated service endpoint exists for this (nothing in `ThemeService.svc`);
   the generic DataService path above is the product's own mechanism (identical to
   the request the Freedom UI profile page sends).

## 4. Functional requirements

- **FR-1** New CLI command **`set-user-theme`** applies a theme to the current
  (authenticated) user's profile on the target environment.
  - Accepts the theme by **name/caption or cssClassName or Id**; resolves it via the
    existing `ListThemes` surface and errors clearly when no match is found
    (listing available themes in the error message).
  - Sends the `SysUserProfile` `UpdateQuery` with the resolved theme **`Id`** (see §3 — the column
    stores the Id; writing the `cssClassName` is stored faithfully but the Shell silently falls back to
    the default). An ambiguous non-id selector (a caption or cssClassName shared by more than one theme)
    is reported with the candidate ids rather than silently applying the first match.
  - Verifies success and reports the applied theme (`caption`, `cssClassName`) and
    the hint "refresh the page to see it".
- **FR-2** Support **reset**: an explicit option (e.g. `--reset`) writes an empty
  `Theme`, returning the user to the `DefaultTheme`/built-in fallback. Mutually
  exclusive with a theme argument.
- **FR-3** New MCP tool **`set-user-theme`** paired with the command
  (environment-aware `BaseTool` pattern), same argument surface, marked as a
  state-changing (write) tool but **not** requiring extra confirmation — it affects
  only the authenticated account (unlike global `DefaultTheme`).
- **FR-4** **Automatic application in the agent flow:** `ThemingGuidanceResource`
  (`docs://mcp/guides/theming`) is updated so that after a successful no-code
  `create-theme` the agent calls `set-user-theme` with the new theme **by default**
  (the ENG-93302 acceptance criterion), telling the user to refresh. **Opt-out:**
  if the user's request indicates they do not want to switch (e.g. "create it but
  don't apply it yet", preparing themes for others), the agent skips the apply
  step and says how to apply later. The global `DefaultTheme` step remains a
  separate, confirmation-gated option (ADR D-D6 unchanged).
- **FR-7** The apply operation is exactly one profile write (plus verification
  read-back). The two extra calls the profile page fires after save —
  `SysUserProfileRefreshService/RefreshCurrentUser` and
  `WorkplaceService/ResetWorkplaceCache` — are **not needed** for a theme-only
  change and are not called: verified in source that they refresh
  culture/timezone/format session state and the workplace cache respectively,
  never the Theme key, and the theme read path is uncached
  (`BaseUserProfileResourceHandler.IsCacheable => false` — fresh `SysProfileData`
  select per request).
- **FR-5** Failure diagnostics map the three server gates to actionable messages:
  missing `CanCustomizeBranding` license, denied `CanChangeOwnTheme` operation, and
  the `ChangeTheme` feature being off (detected via read-back: after the update,
  `SelectQuery` the profile and compare `Theme`; a silent no-op indicates the
  feature is disabled).
- **FR-6** Version gating consistent with the rest of the theming surface: the
  command targets Creatio **10.0.0+** (same gate as ENG-93296 applied to
  `ThemeService`).

## 5. Non-functional requirements

- **NFR-1** Reuse existing plumbing: DataService query building (`clio/Query/`),
  `IApplicationClient`, theming service classes under `clio/Command/Theming/`.
  No ClioGate change needed — the write is a normal-privilege DataService call the
  product itself performs (per AGENTS.md, ClioGate is only for permission-bypass
  cases).
- **NFR-2** DI-first (`CLIO001`): behavior class behind an interface, registered in
  `BindingsModule`.
- **NFR-3** Kebab-case option names; docs and MCP artifacts updated per the
  repository maintenance policies.

## 6. Design decision — "Tool or Command?" (the Krylov question)

**Recommendation: both, as a pair — a standalone `set-user-theme` CLI command plus
its MCP tool; the "automatic" behavior lives in the MCP guidance, not inside
`create-theme`.**

| Option | Verdict |
|---|---|
| **A. Standalone command + MCP tool (recommended)** | Matches every existing clio convention (each theming capability is a command↔tool pair). Keeps `create-theme` single-purpose. The "automatic" semantics belong to the *flow*, which is owned by `ThemingGuidanceResource` — exactly where ADR D-D6 already placed the apply/default question. Also usable standalone (apply an *existing* theme, or dark-mode toggling). |
| B. `--apply` flag on `create-theme` | One round-trip, but couples creation with application, complicates rollback semantics (created but not applied? applied but creation of CSS failed?), and gives no way to apply an existing theme. Could be added later as sugar; not needed for the AC. |
| C. Guidance-only (agent uses generic DataService/`update-sys-setting` tools) | Technically possible but fragile: the agent would have to hand-craft the `UpdateQuery` envelope and know the SysAdminUnit-Id trick; no CLI parity; no targeted diagnostics for the three server gates. Rejected. |

## 7. Touch points (what changes where)

| Area | Files |
|---|---|
| Command | `clio/Command/Theming/SetUserThemeCommand.cs` (new) + options class; service (e.g. `IUserThemeApplier`) in `clio/Command/Theming/`; registration in `BindingsModule.cs` / `Program.cs` |
| MCP tool | `clio/Command/McpServer/Tools/SetUserThemeTool.cs` (new) |
| MCP guidance | `clio/Command/McpServer/Resources/ThemingGuidanceResource.cs` — add "apply to current user" step after no-code create; keep global-default section separate |
| Docs | `clio/help/en/set-user-theme.txt`, `clio/docs/commands/set-user-theme.md`, `clio/Commands.md`, `clio/Wiki/WikiAnchors.txt`, `docs/McpCapabilityMap.md` |
| Tests | `clio.tests/Command/...` (BaseCommandTests fixture, Module=Command), `clio.tests/Command/McpServer/...`, **mandatory** `clio.mcp.e2e` coverage |
| ADR | `spec/adr/adr-theming.md` — amend D-D6: per-user apply is now first-class; global default unchanged |

**ClioRing gate:** verify whether Ring consumes any theming tool (search
`clio-ring/ClioRing.Ipc`, `actions.json`); expected outcome "no Ring-consumed
contract changed" since this is an additive new tool, but the check and the
statement in the PR are mandatory per AGENTS.md.

## 8. Test outline

- **Unit (command):** theme resolution by caption/cssClassName/id; unknown theme →
  error listing available themes; `--reset` writes empty value; correct
  `UpdateQuery` envelope (rootSchemaName, column, filter on Id from the
  `SelectQuery`); gate-failure mapping (license / operation / silent no-op).
- **Unit (MCP):** tool↔command mapping, argument surface, environment-aware
  execution, destructive classification.
- **E2E (clio.mcp.e2e):** create theme → set-user-theme → read back profile shows
  new cssClassName; reset path.
- **Manual (QA):** on a 10.0 instance — run the branding-agent prompt end-to-end,
  refresh page, theme is active; user without `CanChangeOwnTheme` gets the
  actionable error; `ChangeTheme` feature off → actionable error, not false success.

## 9. Open questions / dependencies

1. **AI Toolkit scope (investigated, decision pending):** "AI Toolkit" = the
   **Creatio AI App Development Toolkit** (CAADT, internally aka ADAC) — a coding-agent
   plugin (Claude Code / Codex / Copilot / Cursor) owned by the **UC team**.
   Internal repo `creatio.ghe.com/engineering/ai-driven-app-creation`, public release
   `github.com/Creatio-Platform/creatio-ai-app-development-toolkit`. It is a thin
   orchestration layer (skills `creatio-app-orchestrator`, `creatio-schema-naming`,
   `creatio-ui-guidelines` + context/runbooks) whose execution goes entirely through
   the **clio MCP server** (resident tools natively, everything else via `clio-run`;
   tool contracts resolved through `get-tool-contract`, guidance through clio's
   `get-guidance`).
   Toolkit `main` contains no theming content, **but** (verified 2026-07-15) an
   **unmerged branch `feature/ENG-91387-theme-orchestrator-skill` by Dmytro Nahaiko
   (d.nagaiko)** in the public repo adds exactly the routing piece: a
   `creatio-theme-orchestrator` skill (theming entrypoint: brand intake → guided
   palette conversation → build → apply) plus an AGENTS.md routing rule sending
   branding requests to it. No PR is open for it yet. Consequences:
   - The skill **delegates the color flow, build steps, tool sequence, and license
     preconditions to clio's `get-guidance name="theming"`** — so our FR-4 guidance
     change (auto-apply via `set-user-theme` + opt-out) propagates to CAADT
     automatically for everything the skill defers to clio.
   - **One section of the skill hard-codes the old apply model** ("Build and
     apply"): it tells the agent the created theme is *"not yet visible — nothing
     changes until it is made the default"* and asks whether to set the global
     default. That was true before ENG-93302; once `set-user-theme` ships it is
     wrong (per-user apply is the new default path, refresh suffices). The section
     must be rewritten to: apply to the current user's profile by default (skip on
     create-only intent), keep "make it the default for everyone" as the separate
     opt-in decision — or better, delegate apply semantics to clio's guidance like
     the rest of the skill does.
   - Action: coordinate with d.nagaiko so the skill branch and this clio change
     land consistently (ideally the skill text defers apply behavior to the clio
     guide, making the toolkit immune to future flow changes). The skill itself
     stays UC/toolkit-owned; no change in this repo's scope.
2. **Krylov consultation — RESOLVED (2026-07-15):** both surfaces confirmed —
   add the CLI command and support it in MCP (option A in §6); restrict to
   Creatio **10.0.0+** using clio's existing version-limitation mechanism
   (`RequiresCreatioVersion`, same floor as the rest of the theming surface —
   already FR-6). Implementation unblocked.
3. **`check-theming-access` extension (optional):** should it also report
   `canChangeOwnTheme` so agents can pre-check before attempting the write? Cheap
   to add; decide during implementation.
4. **Minimum Creatio version:** confirm `SysUserProfile.Theme` exists on the same
   10.0.0 baseline as `ThemeService` (verified present in the 10.0 source; older
   versions out of scope like the rest of the theming surface).
