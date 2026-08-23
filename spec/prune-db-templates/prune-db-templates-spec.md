# prune-db-templates - SPEC

> GitHub: [#1177](https://github.com/Advance-Technologies-Foundation/clio/issues/1177)

## Intent

Let a user inspect and selectively remove only clio-managed PostgreSQL template databases from a
configured local database server, with an interactive CLI for people and explicit non-interactive
MCP operations for agents.

## Existing boundary

`restore-db` and local Creatio deployment already create reusable PostgreSQL templates by:

1. setting `pg_database.datistemplate`, and
2. writing a shared database comment in this form:

```text
sourceFile:10.0.0.751_StudioNet8_Softkey_PostgreSQL_ENU|createdDate:2026-06-22T23:18:43.5638685Z|version:1.0
```

The existing general-purpose `Postgres.DropDb` method clears the template flag and terminates active
connections. It must not be reused by this feature because pruning is required to skip databases in
use and never force-disconnect them.

## Requirements

R1. Add `clio prune-db-templates [--db <configured-server>]`. `--db` names an enabled local database
server whose `dbType` is `postgres` or `postgresql` (case-insensitive).

R2. Check interactivity before any prompt. When `--db` is omitted, select the only eligible server
automatically, prompt with Spectre.Console when several are eligible, and fail without mutation when
none are eligible. An explicit unknown, disabled, or non-PostgreSQL server is an error and must not
fall back to another server. Unknown and disabled configurations may share the same not-eligible
message because the existing settings API intentionally hides disabled entries.

R3. Inventory only databases that satisfy all of these conditions:

- `pg_database.datistemplate = true`;
- the name is not `template0` or `template1`;
- `shobj_description(oid, 'pg_database')` contains one non-empty `sourceFile`, a valid
  `createdDate`, and one non-empty `version` value.

Unmarked templates, malformed metadata, built-in templates, and ordinary databases are not
candidates. An empty successful inventory must remain distinguishable from a connection,
authentication, permission, or configuration failure.

R4. Show each candidate's source identifier, database name, and creation date. Metadata version stays
in the structured model for validation and MCP consumers but is not shown in the human-facing tables.
The CLI then performs one Spectre.Console multi-selection that shows only source identifiers ordered
by their leading Creatio version (oldest first, with unparseable versions last). It displays the
complete selected batch without metadata version and asks for one explicit confirmation. After
confirmation, a Spectre.Console progress bar advances once for every processed deletion outcome.
Escape cancels the server picker, template picker, and final confirmation without mutation.

R5. Pressing Escape at any interactive decision, an empty selection, or declined confirmation exits
without deleting anything. Final confirmation is the point of no return: after it, the best-effort
batch runs to completion rather than interrupting PostgreSQL DDL between template-flag transitions.
When stdin is redirected or otherwise non-interactive, the CLI fails closed with an actionable message
directing automation to MCP.

R6. The deletion service accepts only an explicit non-empty set of database names. Immediately
before each drop it re-reads that named database with `shobj_description` and verifies the same
managed-template predicate. The DDL uses the canonical `datname` returned by that revalidation, never
the caller's untrusted spelling. It never infers `all`, expands a pattern, or deletes a database
absent from the request.

R7. Before clearing the template flag, check `pg_stat_activity` for sessions connected to the target.
When any session exists, skip that database and report it as in use. Do not call
`pg_terminate_backend` and do not use `DROP DATABASE ... FORCE`.

R8. PostgreSQL cannot drop a database while it is marked as a template. For an eligible, unused
target, run `ALTER DATABASE <quoted-canonical-name> IS_TEMPLATE false`, issue a quoted
`DROP DATABASE` from the `postgres` database with an explicit 30-second command timeout, and, if the
drop fails while the database still exists, run `ALTER DATABASE ... IS_TEMPLATE true`. The configured
role must own the database or otherwise have the required PostgreSQL privilege. Report both the drop
failure and any recovery failure. Never report a failed drop as success. A timeout, including one
caused by a concurrent template copy lock, is a drop failure and triggers the same recovery.

R9. Report one outcome for every requested database. The batch status is:

- `complete-success` when every requested database was deleted;
- `partial-failure` when at least one was deleted and at least one was skipped or failed;
- `complete-failure` when none was deleted.

The CLI exits non-zero for partial or complete failure.

R10. Add two structured MCP tools backed by the same service as the CLI:

- `list-db-templates`: read-only and idempotent; requires `dbServerName` and returns the structured
  inventory.
- `prune-db-templates`: destructive and non-idempotent; requires `dbServerName` and a non-empty
  `databaseNames` list, revalidates every name, and returns the structured per-item and batch result.

The MCP path never invokes an interactive prompt. On the lazy MCP surface, a direct raw-name call to
the long-tail destructive tool returns clio's structured `confirmation-required` response without
deleting. Execution proceeds through the advertised `clio-run` / `clio-run-destructive` approval
surface, which dispatches the structured MCP tool directly; it does not run the interactive CLI
command.

## Design

### Shared application service

Add one `IDbTemplatePruneService` used directly by the command and MCP tool class. It owns:

- resolving and validating configured local PostgreSQL servers through `ISettingsRepository`;
- creating the existing PostgreSQL client through `IDbClientFactory`;
- inventorying managed templates;
- explicitly targeted, revalidated deletion; and
- structured inventory and batch result records.

PostgreSQL catalog access stays in `Postgres`, but the ordered safety sequence stays in
`DbTemplatePruneService` over fine-grained virtual primitives: list/revalidate managed templates,
count active sessions, set the template flag, check database existence, and drop without force. This
keeps the state transitions unit-testable with the repository's existing `Substitute.For<Postgres>()`
pattern.

The new catalog primitives do not follow the legacy `bool`/`null` swallow-and-return convention in
`Postgres`. They surface database failures to the service. Inventory maps those failures to a
structured category and actionable credential/reachability/permission guidance, while pruning
returns a per-template failure message. Therefore a successful empty inventory cannot be confused
with an inventory query that never succeeded.

SQL values are parameters. DDL uses the canonical name read during revalidation and quotes it with
PostgreSQL identifier escaping (including doubling embedded `"` characters). Revalidation reads the
shared database comment with `shobj_description`; it must not reuse the legacy `GetDatabaseComment`
method, which uses the non-shared comment function. The legacy `DropDb` behavior remains unchanged for
its existing restore/deployment callers.

### Interactive command

`PruneDbTemplatesCommand` orchestrates this fixed sequence:

```text
resolve server -> inventory -> multi-select -> review -> confirm -> delete -> summarize
```

A narrow `IDbTemplatePruneConsole` adapter owns the Spectre.Console server/template selection,
tables, and progress rendering so command tests do not depend on a real terminal. Its progress
wrapper invokes the supplied operation but contains no database or deletion decisions. The existing
`IInteractiveConsole` owns the step-zero interactivity gate and the final fail-closed confirmation.
The command contains no database-specific SQL.

The sequence is intentionally visible and can be copied or extracted when `prune-creatio` is
implemented. This issue does not introduce a generic pruning framework, generic item protocol, or
extension registry before a second consumer exists.

### MCP surface

`DbTemplatePruneTool` is a direct structured service tool rather than a wrapper around the interactive
command. This keeps MCP independent of console state while preserving one validation and deletion
implementation. The tool remains on the normal discoverable/long-tail MCP surface; it does not need a
new resident-core slot.

### Failure behavior

- Configuration and initial inventory failures return no candidates and perform no deletion.
- A requested name that disappears or stops satisfying the managed-template predicate is skipped.
- An in-use template is skipped without disconnecting it.
- A failure on one selected template does not hide outcomes for the remaining selected templates.
- Pruning is best effort: progress counts deleted, skipped, and failed outcomes while the final
  results table remains authoritative.
- Clearing `IS_TEMPLATE` is compensated when the subsequent drop fails and the database still exists.
- Error text identifies the configured server and database but does not expose credentials or a
  connection string.

## Acceptance criteria

- AC1. The zero/one/many eligible-server CLI behavior and explicit `--db` validation are covered by
  unit tests.
- AC2. Inventory parsing tests include valid metadata, malformed metadata, missing fields, built-in
  templates, unmarked templates, and a successful empty result.
- AC3. CLI tests cover non-interactive execution, cancellation, empty selection, declined
  confirmation, confirmed success, progress execution, partial failure, and complete failure.
- AC4. Service tests over fine-grained PostgreSQL substitutes prove per-name revalidation, no implicit
  expansion, in-use skipping, no forced disconnect, and template-flag restoration after a failed or
  timed-out drop. PostgreSQL integration coverage proves identifier quoting against a database name
  containing a double quote.
- AC5. MCP unit tests pin argument mapping, structured results, and read-only/destructive metadata.
- AC6. External-process MCP E2E tests cover discovery of both tools and direct execution of the
  read-only inventory tool. A direct raw-name call to the destructive tool proves the
  `confirmation-required` no-side-effect response. Invalid-request and real deletion paths use
  `clio-run-destructive`; a real deletion runs only behind the repository's explicit destructive
  sandbox opt-in and verifies the database side effect.
- AC7. The new verb has required entries in `clio/help/en/prune-db-templates.txt`,
  `clio/docs/commands/prune-db-templates.md`, `clio/Commands.md`, and `clio/Wiki/WikiAnchors.txt`.
  MCP descriptions, prompts/resources, capability documentation, templates, guidance triggers, and
  ClioRing consumers are reviewed and updated only where affected.

## Exclusions

- No `prune-creatio` implementation.
- No generic cleanup framework or plug-in architecture.
- No support for MSSQL, remote Creatio environment connection strings, arbitrary PostgreSQL
  credentials, wildcard selection, or `delete all`.
- No deletion of legacy name-only templates without valid clio metadata.
- No discovery of Kubernetes infrastructure PostgreSQL servers unless that same server is also
  configured as an enabled local database server in clio settings.
- No change to the existing restore/deployment behavior of `Postgres.DropDb`.
- No forced termination of active PostgreSQL sessions.

## Design review agreement

Codex drafted the design from the issue and repository evidence. Claude then performed an independent
read-only review through Collab. Both agree on the repository ownership, one shared service, one
interactive command, one MCP tool class, no reuse of forceful `DropDb`, compensating template-flag
restore, and no generic pruning framework.

The review refinements incorporated above are the real long-tail MCP approval path, explicit database
failure results, fine-grained testable PostgreSQL primitives, canonical revalidated identifier use,
`ALTER DATABASE ... IS_TEMPLATE`, `shobj_description` revalidation, the step-zero interactivity gate,
an explicit drop timeout, mandatory command artifacts, and the Kubernetes scope boundary.

For the progress-bar follow-up, Codex and Claude agreed to keep the existing best-effort deletion
loop and add one optional per-item completion callback plus one Spectre.Console wrapper. Progress
update failures expected from Spectre.Console are contained by the console adapter so presentation
cannot change a deletion outcome, and the wrapper completes the bar even when request-level
validation prevents per-item callbacks.

Two suggestions were deliberately not adopted because they contradict the requested scope:

- `--db` remains the CLI option because issue #1177 specifies that exact experience, even though
  `restore-db` uses `--db-server-name`.
- malformed/unmarked and failed-restore databases remain untouched and unlisted because issue #1177
  defines only valid-metadata templates as clio-managed deletion candidates. Showing or deleting other
  orphan classes would be a separate feature and would weaken the fail-closed boundary.

## KISS check

The complete flow has one shared service, one Spectre adapter, one command, and one MCP tool class. It
reuses existing settings, database-client, interactive-console, command, Spectre.Console, and MCP
primitives. Progress adds one callback and one rendering wrapper, not a progress framework. The only
recovery is the one required by PostgreSQL's template flag: restore that flag if the drop fails. No
generic orchestration or speculative recovery state is introduced.
