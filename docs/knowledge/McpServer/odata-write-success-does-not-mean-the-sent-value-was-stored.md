---
description: Creatio accepts an OData write and answers success while an entity rule stores another value; only a GET after the write shows it, so odata-update/odata-create read the sent fields back by default
applies-to:
  - clio/Command/McpServer/Tools/ODataWriteVerifier.cs
  - clio/Command/McpServer/Tools/ODataStoredValueComparer.cs
  - clio/Command/McpServer/Tools/ODataUpdateTool.cs
  - clio/Command/McpServer/Tools/ODataCreateTool.cs
ticket: ENG-107611
date: 2026-11-25
---

**What is true** — an entity rule that runs during the save (an entity event handler) can replace a value
the caller wrote, and Creatio still answers the write as a success: a PATCH with `204 No Content` and no
body, a POST with `201` and the created record. Reported in clio#1995 on Sales forecasts
(`CrtForecastMgmtApp`): `ForecastSheet.ForecastEntityId` sent as Product is stored as Contact because it
follows the sheet's `ForecastHierarchy`, and `ForecastColumn.Settings.refColumnPath` was rewritten the same
way. A GET right after the write returns the stored value. A value an asynchronous business process changes
later is NOT visible to that GET.

**Why it is this way** — the substitution is correct platform behaviour (the sheet must follow its
hierarchy); the write answer simply carries no "what was stored" for a PATCH, and the POST body can miss a
change made after the insert. The read-back of a batch filters by `Id eq … or …` in the query string, and
IIS refuses a query string over 2048 characters by default (`maxQueryString`), so `ODataWriteVerifier` keeps
each chunk under `MaxQueryLength` and drops `$select` when it would crowd the ids out.

**What breaks if you ignore it** — the caller works on a value that is not there: in clio#1995 a forecast
grid rendered all zeros and took hours to trace back to two writes that had "succeeded". Turning the
read-back off by default, or reporting a divergence as `success:false`, brings that back (a failed-looking
write is re-sent, and for `odata-create` re-sending duplicates the row — the opposite misreport of
ENG-101508). Comparing values literally reports false divergences (GUID letter case, `+02:00` vs `Z`,
`1` vs `1.0`, a date-only literal stored as server-zone midnight), which teaches the agent to ignore the
signal; `ODataStoredValueComparer` compares by the `$metadata` type for that reason. The opposite mistake
hides a real change, so a match that only an unknown fact could confirm is `Unconfirmed`, not a match: the
same GUID in another letter case or the same instant written another way when the column type is unknown
(in a text column it is another value), and a date-only literal whose stored instant is midnight in SOME
zone - the read-back does not know the server's zone, and noon UTC is both UTC+12:00 midnight and a rule's
noon. `odata-create` loads `$metadata` (once per batch, only while the budget covers it) only when such an
untyped match occurs, because the multi-megabyte CSDL is not worth reading for a batch that matches
literally. A batch read-back GET is waited for with the remaining budget, not trusted to its own timeout:
the synchronous GET re-signs-in and replays itself on an expired session with the timeout it was given.
