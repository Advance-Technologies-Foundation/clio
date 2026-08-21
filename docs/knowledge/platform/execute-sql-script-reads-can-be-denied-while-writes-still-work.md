---
description: on a stand with DenyCustomQueryApiUsage enabled, execute-sql-script SELECTs fail with "Usage of CustomQuery.ExecuteReader is denied by application security settings" while UPDATE/INSERT/DELETE still succeed
applies-to:
  - cliogate/Files/cs/SQLFunctions.cs
  - clio/Command/SqlScriptCommand.cs
ticket: ENG-94402
date: 2026-08-19
---

**What is true** — some Creatio stands refuse READ SQL through cliogate while still accepting writes. The server
setting is `Terrasoft.Core.GlobalAppSettings.DenyCustomQueryApiUsage`; when it is on, `CustomQuery.ExecuteReader`
throws and `clio execute-sql-script` reports
`Usage of CustomQuery.ExecuteReader is denied by application security settings`. Writes are unaffected because
`SQLFunctions.ExecuteSQL` routes a script starting with `update` / `insert` / `delete` to `query.Execute()` and only
everything else (i.e. `select`) to `ExecuteReader`.

**Why it is this way** — it is an application security setting owned by the platform, not by clio or cliogate. No
clio-side change, cliogate version or `install-gate` run turns it off; the stand's own configuration decides. The
cliogate unit test has to flip the private static property by reflection to exercise the read path at all
(`cliogate.tests/SQLFunctionsTests.cs`), which is the local evidence that the gate sits above our code.

**What breaks if you ignore it** — a survey or diagnostic built on `execute-sql-script` SELECTs works on one stand
and dies on the next, and because writes keep working the failure looks like a broken query or a stale cliogate
rather than a policy. Do not spend a round on redeploying the gate. Get the data another way: DataService ESQ
(`execute-esq`), a per-record command, or the dedicated clio read commands.
