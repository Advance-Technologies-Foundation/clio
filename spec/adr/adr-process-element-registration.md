# Register existing tasks using native package scripts

Status: accepted for implementation under #1599.

Use one offline command and its thin MCP adapter. The DI registration service reads
native package/task descriptors, writes two `SqlScripts` folders and returns paths.
There is no database mutation during generation and no new deployment orchestrator.

Use native `SqlScript` descriptors with DBEngineType 2 (PostgreSQL) / 0 (SQL Server)
and InstallType 1. Deterministic folder names derive from the task UId; descriptor
UIds are generated once and retained. SQL uses GUID identities, escaped Unicode
captions, and `NOT EXISTS` against `SysProcessUserTask`.

Validate both existing dialect artifacts before writing. Never overwrite authored
content; a changed caption request returns a conflict. Missing files may be created
on a retry after an interrupted write. No custom registry or sidecar manifest is
needed because native descriptors already carry installation identity.

The reference package provides the proven PostgreSQL installation pattern. Do not
claim SQL Server runtime proof from dialect generation or static validation alone.
