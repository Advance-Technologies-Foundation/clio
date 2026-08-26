# PRD: Schema-level export and import

- Issue: [#1113](https://github.com/Advance-Technologies-Foundation/clio/issues/1113)
- ADR: [adr-schema-level-export-import.md](../adr/adr-schema-level-export-import.md)

## Problem

A fix confined to one schema cannot be delivered as one schema. `pull-pkg` / `push-pkg` is the only
transfer path and it carries the entire package, so shipping a one-schema fix to a customer production
site risks overwriting unrelated customization that exists only there.

Addon schemas (`AddonSchemaManager` — business rules, related pages) are the worst case: clio can write
some of them (`create-page-business-rules`) but cannot read any of them.

## Users and scenario

A support or delivery engineer has reproduced a defect on a customer environment, fixed exactly one
schema on a dev stand, and must hand that fix over as a small artifact a reviewer can read and an
operator can apply without touching anything else.

## Requirements

| # | Requirement |
|---|---|
| R1 | Export one schema from an environment, identified by name and — when the name is ambiguous — package. |
| R2 | Export covers every schema kind the platform supports, addons included. |
| R3 | The exported artifact carries the schema metadata, its properties and its localization resources. |
| R4 | The artifact is reviewable: a human can read what is being shipped without running a tool. |
| R5 | Import writes such an artifact into a named package on another environment, creating or replacing the schema. |
| R6 | Import preserves the schema identity (`UId`), so the target holds the same schema rather than a divergent copy. |
| R7 | An ambiguous name is reported with the matching packages, never silently resolved to one layer. |
| R8 | Import refuses by default when a same-named schema is owned by a different package, and says which. |
| R9 | Import offers a dry run that reports create-versus-replace without writing. |
| R10 | Both operations are available as CLI commands and as MCP tools. |

## Out of scope

- Changing how `delete-schema --remote` resolves a schema name. It is the same class of defect (R7) and
  the issue names it, but it is a behaviour change on a destructive command and belongs in its own PR.
- Compiling or updating the database structure after an import. The operator runs the existing commands;
  import reports when one is likely needed.
- Exporting more than one schema per call.

## Acceptance criteria

- AC1 — `clio export-schema <Name> -p <Package> -e <Env> [-d <Folder>]` writes a bundle folder and reports
  the schema identity it exported.
- AC2 — Exporting an addon schema succeeds and the bundle carries its `LocalizableValues`.
- AC3 — Exporting a name that exists in several packages without `-p` fails and lists those packages.
- AC4 — `clio import-schema <Path> -p <Package> -e <Env>` recreates a deleted schema with its original `UId`.
- AC5 — `import-schema --dry-run` reports the planned action and changes nothing.
- AC6 — Importing when the name is owned by a different package fails naming that package, and succeeds
  with `--allow-new-layer`.
- AC7 — Both commands are exposed as MCP tools, `export-schema` read-only and `import-schema` destructive.
- AC8 — Against an environment whose cliogate is older than `2.0.0.46`, both commands report the
  requirement rather than failing obscurely.
