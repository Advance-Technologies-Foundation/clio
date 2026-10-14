---
description: odata-create/update/delete FK hint (DescribeStructuredODataWriteError / AppendStructuredODataWriteError) is a separate write extractor appended after SensitiveErrorTextRedactor.Redact; it never changes record-created, side-effect or retry-guidance; which PG 23503 / MSSQL 547 wordings were measured live; the database-name and key-value groups are match-only
applies-to:
  - clio/Common/CreatioResponseError.StructuredDetail.cs
  - clio/Command/McpServer/Tools/ODataCreateTool.cs
  - clio/Command/McpServer/Tools/ODataKeyedWrite.cs
  - clio/Command/McpServer/Tools/ToolContractGetTool.cs
  - clio.mcp.e2e/ODataWriteForeignKeyHintE2ETests.cs
ticket: ENG-101507, GH-1699
date: 2026-09-30
---

**What is true** — the write tools use their OWN extractor, `CreatioResponseError.DescribeStructuredODataWriteError`,
not the read one. It shares the message walk, the 2,048-character cap and the match timeout with
`DescribeStructuredODataError`, but it reports foreign-key facts only and no `error.code`, so a write body
without an FK wording yields `null` and the write result is byte-identical to what it was before GH-1699.
`AppendStructuredODataWriteError` adds the hint AFTER `SensitiveErrorTextRedactor.Redact`: the hint is clio's
own sentence around bounded ASCII identifiers, so the redactor has nothing to remove from it. The hint names a
cause and a diagnostic next step only: odata-create keeps `record-created` null and its retry guidance, and
odata-update/odata-delete keep `side-effect` `unknown`. The hint says when the response does not name the
foreign-key column or the referenced table rather than guessing one, and it does not claim an OData mapping
defect: the rejected write may be an entity event handler's. The still-referenced advice ("Inspect the
referencing rows ... Do not delete or re-point records without authorization") is operation-neutral on
purpose, because the PG `update or delete on table` and MSSQL `(DELETE|UPDATE) ... REFERENCE` wordings match a
key update as well as a delete.

Provenance of the wordings:

| Wording | Evidence |
|---|---|
| MSSQL 547 INSERT/UPDATE `FOREIGN KEY constraint` | matched live on dev-local2 by odata-create and odata-update (PATCH) of a Contact with a missing AccountId |
| MSSQL 547 DELETE `REFERENCE constraint` | matched live on dev-local2 by odata-delete of an Account a Contact references |
| MSSQL key-UPDATE `REFERENCE`, both `SAME TABLE` variants | SQL Server message template only, not measured |
| PG 23503 `insert or update on table` headline | GH-1699 raw-POST report, and a live PostgreSQL run (cb660693f) that carried NO DETAIL line |
| PG DETAIL `Key (AccountId)=(...) is not present in table "..."`, PG `update or delete on table` | PostgreSQL message format, unit fixtures only |

The raw server sentence is never surfaced, so "matched live" means the hint appeared, not that the text was
captured.

**Why it is this way** — the read facts advise on select, filters and order-by, which a write never sends, and
an FK violation can come from a post-insert handler writing ANOTHER row after this one persisted, so "not
inserted" is still not known when the hint appears.

**What breaks if you ignore it** — merging the two extractors puts read advice on write failures and changes
the result of every unrecognized write body. Letting the hint set `record-created` false invites a duplicate
insert. The MSSQL database name (`"[^"\r\n]{1,128}"`) and the PG key value (`\([^()\r\n]{1,64}\)`) are matched
but deliberately NOT captured: widening either into a capture group, or any identifier group into "anything
between the quotes", copies tenant data and server prose into the MCP transcript and silently reopens the
issue #1333 leak (`../Common/server-prose-never-reaches-a-non-debug-diagnostic-field.md`). A wording that
drifts from these patterns silently falls back to the bare headline; the e2e fixture is the check.
