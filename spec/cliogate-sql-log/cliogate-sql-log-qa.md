# SQL request log validation

Issue: #397. Tested on 2026-09-11 with Creatio 10.1.585, .NET 8.0.31 and PostgreSQL.

## Disposable runtime

`issue-397` at `https://k-krylov-nb.tscrm.com:40016` was newly provisioned for this issue.
The rebuilt `cliogate_netcore` 2.0.0.53 was installed and its loaded package DLL hash
matched the .NET DLL extracted from the committed archive.

The validation invoked the real `sql` CLI command and independently read the log using
dbHub MCP, source `issue_397` (database identity verified). Results:

| Request | Observed result |
|---|---|
| Long Unicode SELECT with a newline marker | All 16,103 executor characters retained exactly; 2 rows |
| Empty SELECT | Completed, 0 rows, no error |
| Invalid SQL | Completed, -1 rows, database error recorded |
| Lowercase INSERT / UPDATE / DELETE | 2 / 1 / 2 affected rows |
| Uppercase UPDATE using the reader path | 1 affected row, existing response preserved |
| DELETE matching no rows | Completed, 0 affected rows |
| SELECT through `pg_sleep(12)` | Pending record visible from dbHub while CLI still running, -1 rows |
| Same delayed request after return | Same record ID, completed, 1 row, 12,001.82 ms |

The installed native schema reports `BaseEntity` inheritance, `Id` as primary column,
`Name` as display column, `Sql` and `Error` as MaxSizeText, and operation administration
enabled. No operation grants were installed for ordinary roles. The existing gateway
`CanManageSolution` authorization check is unchanged.

A second new instance, `issue-397-clean` at `https://k-krylov-nb.tscrm.com:40019`,
had neither ClioGate nor the log schema before testing. The normal `install-gate`
command installed the exact 2.0.0.53 build-output archive and created the table.
After waiting for the installation restart, the same complete live matrix passed;
the delayed query measured 12,001.97 ms. Its DLL also matches the archive byte for byte.
No manual schema creation, dependency adjustment or prefix change was made on this instance.

## Build and unit checks

Both package identities were built for net472 and netstandard2.0. Extracted archives
contain both runtime DLLs, ATF.Repository dependencies, schema metadata/properties,
and English captions. DLL hashes match their producing build outputs.

```powershell
dotnet test cliogate.tests/cliogate.tests.csproj -c Release -f net472 -p:TargetFrameworks=net472 --filter FullyQualifiedName~SQLFunctionsTests
```

11 tests passed: exact SQL and initial-write order, empty/nonempty results, DML counts,
reader affected counts, SQL failure, initial-log failure, and completion-log failure.

```powershell
dotnet test clio.tests/clio.tests.csproj -c Release --no-build --filter 'Category=Unit&Module=Command' -- NUnit.NumberOfTestWorkers=0
```

4,517 passed, 13 skipped. The startup-update tests require `CLIO_NO_UPDATE_CHECK`
to be unset; sequential execution avoids interference from tests changing this
process-wide environment variable.

## Review boundary

Documentation and aliases reviewed across detailed help, CLI help, Commands.md and
WikiAnchors. No CLI arguments or response formats changed. MCP reviewed, no update required:
there is no dedicated raw SQL execution tool or SQL guidance trigger to update.
ClioRing compatibility reviewed, no Ring-consumed contract changed: inspected
`clio-ring/ClioRing.Ipc`, `clio-ring/ClioRing` and `clio-ring/ClioRing.Desktop/actions.json`.

The log preserves full statements by explicit request. It is an administrative diagnostic,
not tamper-proof storage or a result-memory bound. No retention worker or new log UI was added.
Live runtime proof is PostgreSQL/.NET; net472 was built, not deployed to a Framework instance.
