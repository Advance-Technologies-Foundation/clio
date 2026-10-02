---
description: a section created or changed by clio is missing from the Creatio menu of a browser tab that was not connected over the websocket at the moment of the change; the module structure and workplace caches live per server session in Redis, and only GetData(forceGet=true) run in that session clears them
applies-to:
  - clio/Command/NavigationCacheResetter.cs
  - clio/Command/ApplicationCreateService.cs
  - clio/Command/ApplicationSectionCreateCommand.cs
  - clio/Command/ApplicationSectionUpdateCommand.cs
  - clio/Command/ResetNavigationCacheCommand.cs
ticket: ENG-101680
date: 2026-10-02
---

**What is true** — the left menu (`rest/WorkplaceNavigationPanelService/GetGroups`) and the module
structure (`rest/ConfigurationDataService/GetData`) are served from the server session cache, a Redis hash
`<sessionId>:Cache` (`ModuleStructure_<culture>_<sessionId>`, `AllWorkplaces*`, `All_Sections_<platform>`).
`CreateApp`, a section insert and a `SysModule` write clear none of it. After `CreateApp` the server
broadcasts `ConfigurationStructureChanged` to the websocket channels connected at that moment; a connected
Freedom UI Shell tab then calls `GetData` with the bare JSON body `true` (`forceGet`) in its own session,
which clears that session's entries, and reloads the left panel. A tab whose websocket was not connected at
that moment never gets the message, and its session keeps the old menu across reloads. `GetData(true)` acts
on the CALLING session only, so clio's own reset after `create-app`, `create-app-section` and
`update-app-section` (and `reset-navigation-cache`) cannot clear a browser session; the same call run from
inside the tab, with its cookies and `BPMCSRF` header, followed by a reload, does. The three commands and
`reset-navigation-cache` return that in-tab call as `next-step`.

**Why it is this way** — the platform refreshes a session's menu only through the websocket broadcast, and
clio has no websocket. No public service clears another session's cache: `GetData(true)` and
`WorkplaceService/ResetScriptCache` act on the caller's session, and `ClearRedisDb` logs out every user. The
platform feature `EnableReloadAppCacheForWorkplacesByDefault`, which would reload workplaces on every read,
is off in every product. The cache is in Redis, so an application restart does not clear it. Seen in the
ENG-101575 run (applicants-1 IIS log, 29 Sep 2026): the browser session cached the structure at 14:04 UTC,
its websocket closed at 14:04:56, `CreateApp` broadcast at 14:10:07 with no websocket open, and every later
`GetData` in that session returned the same pre-create structure until Redis was cleared.

**What breaks if you ignore it** — a new section is missing from «My applications» and its list header
shows «Page title», while every `SysModule` / `SysModuleInWorkplace` row is correct. Restarts and reloads do
not help, so the cause is easy to blame on the rows (the ENG-101575 run changed the section code and
`CardSchemaUId` for nothing). `clear-redis-db` fixes it but logs out every user of the stand. Expecting
clio's own reset to refresh the browser is the other trap: it clears only clio's session, and the tab stays
stale until the in-tab call runs or its session ends.
