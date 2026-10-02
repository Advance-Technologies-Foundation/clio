---
description: a section created or changed by clio is missing from the Creatio menu because the module structure and workplace caches live per server session in Redis; clio clears its own session with ConfigurationDataService/GetData(forceGet=true), nothing else does
applies-to:
  - clio/Command/NavigationCacheResetter.cs
  - clio/Command/ApplicationCreateService.cs
  - clio/Command/ApplicationSectionCreateCommand.cs
  - clio/Command/ApplicationSectionUpdateCommand.cs
ticket: ENG-101680
date: 2026-10-02
---

**What is true** — the left menu (`rest/WorkplaceNavigationPanelService/GetGroups`) and the module
structure (`rest/ConfigurationDataService/GetData`) are served from the server session cache, a Redis hash
`<sessionId>:Cache` (`ModuleStructure_<culture>_<sessionId>`, `AllWorkplaces*`, `All_Sections_<platform>`).
`CreateApp`, a section insert and a `SysModule` write clear none of it. `GetData` with the bare JSON body
`true` (`forceGet`) clears the CALLING session's entries, reloads its workplaces and bumps the client cache
hash. `create-app`, `create-app-section` and `update-app-section` send that call through the client that made
the change, so it clears only the session that client logged in to. Whether a later clio call reads that
session depends on the stand: on 10.2.337 (eng46659, 2 Oct 2026) three consecutive clio processes and three
curl logins each got a new session with a freshly built menu. On applicants-1 (10.0.0.941) three
`get-browser-session` logins got the same session, because `BaseSessionIdManager` reuses an open session
keyed by user name + client IP + User-Agent under the `PreventMassSessionCheckIntervalMinutes` /
`PreventMassSessionCheckCount` system settings.

**Why it is this way** — the Freedom UI Shell clears its own session when the server broadcasts
`ConfigurationStructureChanged` over the websocket; clio has no websocket. The platform feature
`EnableReloadAppCacheForWorkplacesByDefault` that would reload workplaces on every read is off in every
product. The cache is in Redis, so an application restart does not clear it.

**What breaks if you ignore it** — a new section is missing from «My applications» and its list header
shows «Page title», while every `SysModule` / `SysModuleInWorkplace` row is correct. Restarts and a fresh
clio login do not help, so the cause is easy to blame on the rows (the ENG-101680 run changed the section code
and `CardSchemaUId` for nothing). `clear-redis-db` fixes it but logs out every user of the stand. Another API
session that is not on a websocket stays stale even after clio's reset, until it calls `GetData(true)` itself
or its session ends.
