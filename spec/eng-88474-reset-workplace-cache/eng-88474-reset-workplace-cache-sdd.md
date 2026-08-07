# ENG-88474 — Make workplace changes visible without re-login

> **Status:** ready-for-dev
> **Jira:** ENG-88474 "Ability to work with workplaces" — TODO item 4, marked *optional, 0.5 SP*
> **Verified against:** `clio` @ `feature/ENG-88474-workplaces`, `PackageStore` and `core` @ 7.8.0, 2026-08-06.
> Re-grep the anchors listed at the end before relying on them; never trust a line number.

## Product Brief

- **Problem:** A code agent creates a workplace, moves a section into it, and grants role visibility — and the
  person watching the browser sees nothing. Creatio caches the navigation structure, so the change only surfaces
  after signing out and back in. Every agent-driven workplace change in the ENG-88474 flow ends with "now log out
  and log in", which breaks the demo the parent epic exists to deliver.
- **Value:** The agent's workplace changes become observable in the same session, so the Todo-app test case in the
  parent task runs end to end without a manual interruption.
- **Success metric:** None worth instrumenting for a 0.5 SP item — the observable outcome is the ENG-88474 test case
  completing without a re-login step.
- **Priority:** Optional tail of a task already in Code Review. Do it only if it stays this small; the main
  workplace tooling has already shipped in PR #970.

## Summary

Creatio keeps the left-navigation structure in a cache, so workplace and section changes made through the API are
invisible until the user signs in again. There is already a native endpoint that drops that cache, and clio already
calls it in two other places for exactly this reason after saving a page. The fix is to make the workplace tools do
the same thing they already do after a page save — one best-effort call, no new endpoint, no ClioGate.

## Goal

After a code agent mutates a workplace (create/delete, rename, add/remove section, change role visibility), the
change becomes visible to the signed-in user without signing out and back in.

## How it works today

### The cache, and where it lives

Creatio caches navigation in `ConfigurationSectionHelper` (`PackageStore/CrtNUI`). The relevant entries:

- the *selected* workplace, key `SysWorkplaceId_{CurrentUser.Id}`, read/written through `userConnection.SessionCache`;
- the configuration/shell/module/entity script caches, keyed `{culture}_{SessionId}` in
  `SessionCache.WithLocalCaching("LoginLocalCache")`;
- the workplace and section *structure*, cached by `WorkplaceRepository` and the section repository
  (`PackageStore/Workplace` package) — both take `_sessionCache = uc.SessionCache`.

`UserConnection.SessionCache` in `core` resolves to `GetSessionCacheStore(UserKey, SessionId)`. Every one of those
caches is therefore **scoped to one user *and* one session** — alongside `ApplicationCache` (`CacheLevel.Application`)
and `WorkspaceCache` (`CacheLevel.Workspace`), which the navigation caches do not use.

### The three native endpoints, and how their scope differs

`WorkplaceService` (`PackageStore/CrtNUI`) is a full workplace API — `CreateWorkplace`, `DeleteWorkplace`,
`ChangeWorkplaceName`, `AddSectionToWorkplace`, `RemoveSectionFromWorkplace`, `RemoveAllSectionsFromWorkplace`,
`GetWorkplacesByType` — plus three cache operations that are **not** equivalent:

| Endpoint | Does | Reaches other sessions? |
|---|---|---|
| `ResetWorkplaceCache` | `ClearWorkplaceCache` + `WorkplaceManager.ReloadWorkplaces()` → workplace and section repository `ClearCache()`, all on `uc.SessionCache` | **No** |
| `ResetWorkplaceCacheByApplicationClientTypeId` | `ClearWorkplaceCache` + `ReloadWorkplacesByApplicationClientType` | **No** |
| `ResetScriptCache` | `ConfigurationSectionHelper.ClearCache(uc)` — the session script caches, **then `ResetWorkplaceCache(uc)`, then `new ConfigurationCacheHashUpdater().UpdateCacheHash()`** | **Yes**, via the last step |

`ConfigurationCacheHashUpdater.UpdateCacheHash()` does one thing:
`IClientCacheStore.SetHashValue(ConfigurationDataCacheHashKey, Guid.NewGuid())`. That is a single stored hash every
client compares against, not a per-session entry — it is the only part of any of these calls that is global.

### What Application Hub already does

`AppStartupManager.RunApp(appId)` in `core` calls its own private `ResetWorkplaceCache()` *before* running the app,
which reflectively invokes **both** `ConfigurationSectionHelper.ResetWorkplaceCache` and `ClearCache` on the
installing user's connection. That is the same pair `ResetScriptCache` exposes over HTTP — including the global hash
bump. The App Hub behaviour this ticket refers to is not a separate mechanism; it is this one.

### What clio already does

`ResetScriptCache` is **already the established clio pattern**, called best-effort after a mutation:

- `PageUpdateOptions.TryResetScriptCache()` — documented as mirroring the frontend PageDesigner post-save behaviour,
  wrapped in try/catch, explicitly never blocking a successful save;
- `AddonSchemaDesignerClient` — same endpoint, same purpose.

`ServiceUrlBuilder` has no `KnownRoute` for it; both call sites pass the literal
`"/rest/WorkplaceService/ResetScriptCache"`.

### What already ships for this item

A PoC on branch `poc/ENG-88474-reset-workplace-cache` (commit `f6baf8ee`) added a `reset-workplace-cache` command and
MCP tool over `ResetWorkplaceCache`, plus `KnownRoute.ResetWorkplaceCache = 62`. Its own commit message records the
blocker — "blocked on a live probe of whether that platform cache is session-scoped" — and proposes a ClioGate
"for all users" reset as the fallback. **The blocker is now answered from code: it is session-scoped.** The PoC
therefore cannot deliver the goal, and its fallback is unavailable because ClioGate is out of bounds.

### Delivery path

Pure clio change. `clio` ships as a dotnet tool / MCP server, so a normal clio release delivers it. No Creatio-side
package, no ClioGate, no PackageStore bundle rebuild.

## Constraints & Prior Decisions

- **ClioGate must not be used** — *why:* stated constraint for this work; target environments are not guaranteed to
  have it installed. This rules out the PoC's documented fallback.
- **No new endpoint and no new MCP tool** — *why:* the parent task's RFMT already concluded "no new tools needed",
  and `ResetScriptCache` already exists and is already called by clio for the same class of problem.
- **Best-effort, never blocking** — *why:* matches `TryResetScriptCache`; a cache reset failing must not fail an
  otherwise successful workplace mutation.

## Approach

Call `/rest/WorkplaceService/ResetScriptCache` once, best-effort, after a successful workplace mutation — the same
call `PageUpdateOptions` and `AddonSchemaDesignerClient` already make, for the same reason.

1. Add `KnownRoute.ResetScriptCache` to `ServiceUrlBuilder` and point the two existing literal call sites at it, so
   there is one definition of the route instead of three copies.
2. Add a small shared helper (mirroring `TryResetScriptCache`: try/catch, log at debug, return void) and invoke it
   after workplace create/delete/rename, section add/remove, and role-visibility changes in the ENG-88474 tooling.
3. Retire the PoC: drop the `reset-workplace-cache` command, its MCP tool, and `KnownRoute.ResetWorkplaceCache`.
   Keep nothing that calls `ResetWorkplaceCache` directly — it is the session-scoped subset and would read as a
   working feature while doing nothing cross-session.

*Rejected — a `reset-workplace-cache` command over `ResetWorkplaceCache` (the PoC):* session-scoped, so it clears the
cache of the session clio authenticated and no other. *Rejected — a ClioGate "for all users" reset:* out of bounds.
*Rejected — `ResetWorkplaceCacheByApplicationClientTypeId`:* same session scope, and it additionally requires a
client-type id the tooling has no reason to know.

## Behavior

1. A code agent performs a workplace mutation through the existing tools.
2. On success, clio issues one POST to `/rest/WorkplaceService/ResetScriptCache` with an empty body.
3. The server clears the calling session's script and workplace caches and writes a fresh
   `ConfigurationDataCacheHashKey` value.
4. Clients comparing against that hash treat their cached configuration as stale and re-fetch it.
5. If the POST fails, the failure is swallowed and logged; the mutation's own result is unaffected.

## Data & Config Model

None. No schema, setting, or persisted config changes — only an added route constant and call sites.

## Boundaries

- **Always:** reuse the existing `ResetScriptCache` route and the existing best-effort shape.
- **Ask:** before adding any new server-side endpoint, or before touching `PackageStore` / `core`.
- **Never:** introduce a ClioGate dependency. Never call `ResetWorkplaceCache` directly and describe it as removing
  the re-login requirement.

## Out of Scope

- Pushing the change to an already-open browser without any user action (see Risks — a refresh is acceptable).
- Any cross-user broadcast beyond what the native client-cache hash already provides.
- The workplace CRUD tooling itself — shipped in PR #970 and already in Code Review.

## Acceptance Criteria

1. **AC-1** — WHEN a workplace mutation completes successfully, the system SHALL POST once to
   `/rest/WorkplaceService/ResetScriptCache`.
2. **AC-2** — The route SHALL be defined once in `ServiceUrlBuilder` and referenced by every call site, with no
   remaining string literals.
3. **AC-3** — IF the reset call fails or times out, THEN the system SHALL log it and still report the mutation as
   successful. *(failure mode)*
4. **AC-4** — After an agent creates a workplace and adds a section, a signed-in user SHALL see the new workplace
   without signing out and back in.
5. **AC-5** — The build SHALL contain no `reset-workplace-cache` command, MCP tool, or
   `KnownRoute.ResetWorkplaceCache`.

## Edge Cases

- **The reset is issued while the agent is unauthenticated or the session expired** — it must not trigger a re-auth
  loop; the existing `ReauthExecutor` / `NoReauthExecutor` behaviour applies and the call stays best-effort.
- **Several mutations in one agent run** — one reset per successful mutation is acceptable; the call is cheap and
  idempotent. Do not add batching for a 0.5 SP item.
- **A mutation that partially fails** — reset only after a success, so a failed change never bumps the hash.

## Affected Components

- `clio/Common/ServiceUrlBuilder.cs` — new `KnownRoute.ResetScriptCache` + route map entry.
- `clio/Command/PageUpdateOptions.cs`, `clio/Command/AddonSchemaDesigner/AddonSchemaDesignerClient.cs` — switch from
  the literal to the route constant.
- The ENG-88474 workplace tooling — call the helper after each successful mutation.
- **Not to be revived:** `ResetWorkplaceCacheCommand`, `ResetWorkplaceCacheTool`, their tests, docs, help text, and
  `KnownRoute.ResetWorkplaceCache` — all PoC-only on `poc/ENG-88474-reset-workplace-cache`, never merged.

## Verification

- `dotnet test clio.tests --filter FullyQualifiedName~ServiceUrlBuilder` — the route resolves and no literal remains.
- Unit test on the workplace tooling: a successful mutation issues exactly one POST to the reset route; a mutation
  that throws issues none; a reset that throws leaves the mutation result successful (AC-1, AC-3).
- `grep -r "WorkplaceService/ResetScriptCache" clio/` — only the route map matches (AC-2).
- `grep -ri "reset-workplace-cache\|ResetWorkplaceCache" clio/` — no matches (AC-5).
- Manual, on a live environment (AC-4): sign in as a user in a browser; from clio, create a workplace and add a
  section; refresh the browser **without signing out**; the workplace appears.

## Risks and Assumptions

- **Assumption, and the one thing not established from code:** that bumping `ConfigurationDataCacheHashKey` is
  enough for an already-open client to pick up the new navigation on a page refresh. The hash write is verified; the
  client's reaction to it is not. The manual check in Verification settles it. If a refresh turns out to be
  insufficient, the goal "without re-login" is not reachable without a server-side change, and the item should be
  dropped rather than grown — it is optional and 0.5 SP.
- The task's phrasing is "without **re-login**", so requiring a page refresh still satisfies it. Confirm the PO
  agrees before closing.
- `ResetScriptCache` clears more than the workplace cache (all login-local script caches for the calling session).
  That is what the two existing call sites already do, so the blast radius is not new.

## Clarifications

### Q-01 · answered · BA
- **Asked by:** BA · 2026-08-06
- **Question:** The PoC left this open — is the cache behind `WorkplaceService/ResetWorkplaceCache` session-scoped?
  It proposed a ClioGate "for all users" reset if so, which is now out of bounds.
- **Answer:** Yes, session-scoped. `UserConnection.SessionCache` resolves to `GetSessionCacheStore(UserKey, SessionId)`,
  and every navigation cache — the selected workplace, the script caches, and the workplace/section repositories —
  uses it. No live probe was needed; the code settles it.
- **Basis:** code — `UserConnection.SessionCache` in `core`; `ConfigurationSectionHelper`; `WorkplaceRepository`.
- **Effect:** The PoC's approach is retired (Approach step 3). The ClioGate fallback is not needed either, because
  `ResetScriptCache` already carries the global hash bump.

## Open Questions

None.

## References

- **Jira:** ENG-88474 — https://creatio.atlassian.net/browse/ENG-88474 · parent epic ENG-85256 "AI no-code agents"
- **PR:** Advance-Technologies-Foundation/clio#970 — the workplace tooling and its guidance
- **Branches:** `feature/ENG-88474-workplaces` (main work), `poc/ENG-88474-reset-workplace-cache` (`f6baf8ee`, retired)
- **Code anchors** — grep these, not line numbers: `ResetScriptCache`, `ResetWorkplaceCache`,
  `ConfigurationSectionHelper`, `ConfigurationCacheHashUpdater`, `ConfigurationDataCacheHashKey`,
  `GetSessionCacheStore`, `WorkplaceRepository`, `AppStartupManager`, `TryResetScriptCache`, `KnownRoute`.

## Handoff

Ready for implementation. Do the manual check in Verification first — it is the only unproven link, and if it fails
the right move is to drop the item, not to grow it. Questions during implementation go into Clarifications above.
