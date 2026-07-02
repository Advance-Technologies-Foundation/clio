# SPEC: Theming with AI — Clio dev flow (ENG-90636)

**Created**: 2026-06-18  
**Size estimate**: M (3-4 stories)  
**Recommended next**: /bmad-spec is sufficient — proceed to story creation (single new CLI command; MCP tool + guidance + scaffold wiring are supporting, not additional commands)

---

## Why

A coding agent driving the Creatio AI Toolkit needs to create / re-tokenize a custom theme and ship it to an environment through clio (the dev/workspace flow, Contour A). The theme **template** and **authoring guides** now ship in the npm package `@creatio/theming`, so clio must stop embedding them (the ENG-89624/90889 prototype did) and instead **delegate** authoring to the package — contributing only orchestration guidance, npm wiring, transport, and one missing capability: a **surgical theme-cache invalidation** so an updated theme becomes visible without the blunt full-Redis flush the prototype used.

## Capabilities

| ID | Intent (WHAT) | Success Signal (HOW WE KNOW) |
|----|--------------|------------------------------|
| CAP-01 | Invalidate **only** the Creatio theme cache on a target environment | `clio clear-themes-cache -e <env>` exits 0; a theme pushed/edited beforehand becomes visible after a Creatio reload, and the call does **not** flush the whole Redis DB |
| CAP-02 | Expose theme-cache invalidation as an environment-aware MCP tool | MCP `clear-themes-cache` tool resolves the target environment, executes, and returns a success result; an `clio.mcp.e2e` test drives it against a real `clio mcp-server` |
| CAP-03 | Provide an MCP guidance entry point that orchestrates the theme dev flow by delegating to the npm package | `get-guidance theming` returns text that routes the agent to `node_modules/@creatio/theming` (AI_GUIDES_INDEX → creation + design-token guides + `templates/`) and the deploy + `clear-themes-cache` steps, and does **not** restate the token catalog; an e2e discovery test asserts the entry resolves |
| CAP-04 | Make `@creatio/theming` available where the agent works | It is a `new-ui-project` devDependency (it is the `--crt-*` design-token catalog component styling consumes, per `@creatio-devkit/common`'s REMOTE_COMPONENT_STYLING guide, as well as the theme-authoring source); for a plain package the `theming` guidance instructs on-demand `npm i`. Success: a scaffolded `new-ui-project` lists `@creatio/theming`; theme-authoring guidance is reachable via `get-guidance theming` |

## Constraints

- **C1**: clio must NOT embed the theme CSS template or the design-token / creation guide content — single source of truth is the npm package. (`get-guidance theming` is a thin pointer.)
- **C2**: theme-cache clear must be **surgical**, not a full `ClearRedisDb`. A full-flush fallback is permitted only as an explicit, logged last resort.
- **C3**: follow clio patterns — CLI options kebab-case (CLIO001, build-breaking); CLI command derives from `RemoteCommand<TOptions>`; MCP tool uses the env-aware `BaseTool` execution path and registers through `McpFeatureToggleFilter.RegisterEnabledPrimitives`.
- **C4**: theme-cache invalidation uses the native `ThemeService.svc/ClearThemesCache` endpoint (R2 resolved 2026-06-18) — no ClioGate method, no cliogate bump, no `[RequiresPackage]`; auth is runtime (`CanCustomizeBranding` + `CanManageThemes`).
- **C5**: the theme descriptor is `{id, caption, cssClassName}` with **no `code` field** — satisfied by delegating to the package's `theme.json.tpl`; clio does not author a descriptor template.

## Non-goals

- Will NOT add a `new-theme` scaffold command/tool — under the Delegate model the agent copies the package template itself (drop the prototype's `new-theme` stack).
- Will NOT embed `tpl/themes/theme.css.tpl` or the `creatio-theme` / `design-tokens` guidance text/embedded resources in clio.
- Will NOT implement Contour B / server-side `ThemeService` CRUD (create/update/delete/list-themes) — separate ticket.
- Will NOT author the fonts guide or the token-usage policy in clio — they live in `@creatio/theming`.

## Success Signal

Running `clio clear-themes-cache -e <env>` returns exit 0 and invalidates only the theme cache (a previously pushed theme change is visible on reload) **without** flushing the entire Redis DB; and `clio mcp-server` exposes both the `clear-themes-cache` tool and the `get-guidance theming` entry that routes a coding agent to `@creatio/theming` for the full create/re-tokenize flow.

---

## Companion Notes

- **Open dependency — RESOLVED (2026-06-18) via resolution (a).** The platform exposes a native endpoint: `POST ThemeService.svc/ClearThemesCache` (`IThemeService`, *"Forces a refresh of the theme catalog so that the next GetAvailableThemes call observes any themes added or modified outside this service"*), returning `BaseResponse { Success, ErrorInfo }` and requiring `CanCustomizeBranding` + `CanManageThemes` (runtime auth, not a clio package gate). `KnownRoute.ClearThemesCache = 42` is wired to `ServiceModel/ThemeService.svc/ClearThemesCache`; **no ClioGate method, no cliogate bump, no `[RequiresPackage]`**, and the full-`ClearRedisDb` fallback was not needed.
- **Package:** `@creatio/theming` — canonical guides are the `THEMING_*`-prefixed files (per `AI_GUIDES_INDEX.md` + `CHANGELOG.md`); the older `THEME_CREATION_AI_GUIDE.md` / `DESIGN_TOKENS_AI_GUIDE.md` in dist are stale. `@creatio/theming` stays a `new-ui-project` devDependency — it is the design-token catalog `@creatio-devkit/common`'s REMOTE_COMPONENT_STYLING guide points to (needed for component styling) as well as the theme-authoring source; for plain packages the `theming` guidance instructs on-demand `npm i`. Theme authoring is delivered via the `theming` MCP guidance — no bespoke `AGENTS.md` theme section. Pointers reference `AI_GUIDES_INDEX.md` so they track the package's guide naming.
- **Plain-package case (CAP-04):** for a non-ui-project package, the `theming` guidance instructs the agent to drop a minimal `package.json` and `npm i @creatio/theming` so `node_modules` exists.
- **Reference patterns:** `clear-redis-db` ([clio/Command/RedisCommand.cs](clio/Command/RedisCommand.cs), `KnownRoute.ClearRedisDb = 21`), ClioGate wiring ([clio/Package/PackageUnlocker.cs](clio/Package/PackageUnlocker.cs) `CallGate`), guidance resource ([clio/Command/McpServer/Resources/WorkspaceUiProjectGuidanceResource.cs](clio/Command/McpServer/Resources/WorkspaceUiProjectGuidanceResource.cs)), catalog ([clio/Command/McpServer/Resources/GuidanceCatalog.cs](clio/Command/McpServer/Resources/GuidanceCatalog.cs)). Highest existing `KnownRoute` = 41 → new `ClearThemesCache = 42`.
