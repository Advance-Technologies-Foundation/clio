# prune-db-templates

## Command Type

Local Instance Management

## Name

prune-db-templates - Interactively prune clio-managed PostgreSQL templates

## Description

`prune-db-templates` inventories valid clio-managed PostgreSQL template databases
on a configured local server. It displays their source file, database name, and
creation date, then lets you select individual templates by source. Selection is
ordered by Creatio version, oldest first, so releases stay grouped. The full
selection is shown and must be confirmed before deletion.

Press `Esc` to cancel from the server picker, template picker, or final confirmation.
Cancellation exits successfully without deleting anything. After final confirmation,
the best-effort deletion batch runs to completion so PostgreSQL DDL is not interrupted
between clearing and restoring a template flag.

After confirmation, a progress bar advances for every processed template. Deletion
is best effort: a failed or in-use template is reported and processing continues
with the remaining selection. Any skipped or failed item produces a non-zero exit
code, and the final results table shows every outcome.

A database is eligible only when PostgreSQL marks it as a template and its shared
database comment contains valid clio metadata. Built-in `template0` and
`template1` are excluded. Each selected database is revalidated immediately
before deletion. Databases with active sessions are skipped; sessions are never
terminated and there is no delete-all option.

The configured PostgreSQL role must own the selected databases or otherwise have
permission to alter and drop them. The command exits with a non-zero code when
any selected template is skipped or fails.

If the process is forcibly terminated after clearing a template flag, recover
the database with `ALTER DATABASE "<database-name>" IS_TEMPLATE true` before
retrying.

## Synopsis

```bash
clio prune-db-templates [--db <SERVER_NAME>]
```

## Options

`--db`
: Configured local PostgreSQL server name. When omitted, the only eligible server
  is selected automatically; multiple eligible servers produce a selection
  prompt.

## Automation

The CLI command requires an interactive terminal. For automation, call the
read-only `list-db-templates` MCP tool first, then call the destructive
`prune-db-templates` MCP tool with the selected server and an explicit non-empty
`databaseNames` list through the normal destructive-tool approval flow.

## Examples

```bash
clio prune-db-templates
clio prune-db-templates --db local-postgres
```
