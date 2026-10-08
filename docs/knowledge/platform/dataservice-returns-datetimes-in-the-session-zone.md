---
description: DataService returns a DateTime column such as CreatedOn in the session user's time zone with no offset marker, and OData only LABELS what the entity layer returns as UTC - its Z was true UTC on one stand and state and local time on another; convert DataService times with GetApplicationInfo's userTimezoneOffset
applies-to:
  - clio/Common/CompilationHistoryReader.cs
ticket: ENG-102333
date: 2026-10-08
---

**What is true** — measured on 2026-10-08 on two stands, session user in UTC+3 on both:
- DataService `SelectQuery` returned every `CreatedOn` in the session's zone, with no offset, every time:
  `2026-10-08T09:54:41.340` for a row stored as 06:54:41 UTC.
- `ApplicationInfoService.svc/GetApplicationInfo` returns that session's offset at
  `applicationInfo.sysValues.userTimezoneOffset` (180, as minutes), from `CurrentUser.GetTimeZoneOffset()`.
- OData 4 was not consistent. A 10.2.430 stand returned true UTC. A 10.1.37 stand returned true UTC at 10:28,
  then, after an application restart, local time with a `Z` (`09:54:41.34Z` for the same row).

The session's zone is its profile zone or, when that is empty, the clio host's offset sent at password login
(`password-login-timezone-follows-clio-host.md`).

**Why it is this way** — `ODataConfig.MapODataServiceRoute` calls `config.SetTimeZoneInfo(TimeZoneInfo.Utc)`,
which labels whatever the entity layer hands OData as UTC; it converts nothing. Why the entity layer started
converting after the restart was not traced.

**What breaks if you ignore it** — a Creatio time compared with a UTC clock is off by the session offset,
silently, and through OData only on some stands or after a restart: a three-hour-old row reads as three hours
in the future. Read it through DataService and subtract `userTimezoneOffset` from the same session, as
`CompilationHistoryReader` does. Comparing two DataService times with each other is safe.
