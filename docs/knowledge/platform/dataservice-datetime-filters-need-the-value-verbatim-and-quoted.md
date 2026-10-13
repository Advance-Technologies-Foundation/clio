---
description: a DateTime read from DataService and sent back as a filter value must keep its exact text and be a quoted JSON string; JObject.Parse turns it into a culture-formatted DateTime without milliseconds that silently selects the wrong rows
applies-to:
  - clio/Command/DataServiceSelectResponse.cs
  - clio/Command/RuntimeVerification/CreatedRecordWatcher.cs
ticket: ENG-103024
date: 2026-10-12
---

**What is true** — DataService returns a DateTime as `"2026-10-12T23:24:50.907"` (session time zone,
milliseconds, no offset) and accepts a DateTime filter parameter only as a quoted JSON string inside the
string: `"value": "\"2026-10-12T23:24:50.907\""`; the unquoted form fails the query. `JObject.Parse`
converts the returned text into a `DateTime` whose `ToString()` is culture-formatted (`10/12/2026 23:24:50`),
drops the milliseconds, and is then read back by the server as day-first or month-first depending on its
culture - no error, just other rows. Read such rows with `DataServiceSelectResponse.ReadRowsVerbatim`.

**Why it is this way** — Newtonsoft's default `DateParseHandling.DateTime`, and the Terrasoft client's
`encodeDate` convention for DateTime parameters.

**What breaks if you ignore it** — measured on 2026-10-12: a watch whose baseline was `10/12/2026` read as
10 December found no new activity, while one whose baseline was `10/5/2026` happened to work.
