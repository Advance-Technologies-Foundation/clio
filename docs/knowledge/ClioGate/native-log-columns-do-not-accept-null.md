---
description: Native Creatio integer and text columns are NOT NULL even when not required; SQL diagnostic unknown counts use -1
applies-to:
  - cliogate/Files/cs/SQLFunctions.cs
  - cliogate/Schemas/ClioSqlRequestLog/
ticket: "#397"
date: 2026-09-11
---

**What is true** — On Creatio 10.1 PostgreSQL, the designer's optional Integer,
Float and MaxSizeText columns produce NOT NULL database columns with defaults.
`ClioSqlRequestLog.RowCount` therefore uses -1 for an unavailable count, and Error
uses an empty string for success. An optional designer field does not imply SQL nullability.

**Why it is this way** — Creatio's native entity storage supplies values for these
types. Verified through information_schema after publishing and installing the log
entity. In addition, an untyped DBNull parameter is rejected by the PostgreSQL executor
before the UPDATE with `Specify data type for parameter ... with null value`.

**What breaks if you ignore it** — Successful SQL returns normally but the completion
update fails, leaving the diagnostic record permanently pending. Mocks of DBExecutor
do not enforce provider parameter typing or table constraints; validate the installed
entity on a real disposable database.
