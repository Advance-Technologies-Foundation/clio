# Registration validation

- TC-U-01: Generate native PostgreSQL / SQL Server after-package descriptors and SQL.
- TC-U-02: Preserve all bytes and script UIds on identical retry.
- TC-U-03: Reject mismatched task/package metadata and undeclared packages before writes.
- TC-U-04: Preserve authored artifacts when a caption/content conflict is detected.
- TC-U-05: Escape apostrophes, backslashes, quoted text and Unicode captions correctly.
- TC-U-06: Verify every MCP argument maps to the offline command.
- TC-I-01: Real MCP generation without an environment; reject foreign UId, create both
  dialects, retry and compare files.
- TC-I-02: Install the generated PostgreSQL registration via a package in the retained
  non-FSM lab, verify row identity/caption and designer visibility, reinstall and
  verify no duplicate or caption overwrite.
- SQL Server installation / reinstallation: #1602, requires an existing selected
  SQL Server Creatio instance. Static SQL checks are not runtime validation.

## Verified on 2026-09-17

- Full unit suite: 13,687 passed, 25 existing skips (`dotnet test clio.tests/clio.tests.csproj --filter "Category=Unit"`).
- Real stdio MCP NoEnvironment test passed: `RegisterProcessElementToolE2ETests`.
- PostgreSQL / Creatio 10.1.585.0, retained non-FSM installation lab: native installer executed the generated PostgreSQL script; the browser User actions toolbox displayed Registration tooling probe.
- Identical archive reinstallation preserved exactly one registration and its Id. A further installation with a newer script descriptor forced SQL reexecution; an intentionally edited caption and the original registration Id remained unchanged.
- SQL Server descriptors and SQL have unit coverage only; live SQL Server validation belongs to #1602 and awaits an existing instance selection.
