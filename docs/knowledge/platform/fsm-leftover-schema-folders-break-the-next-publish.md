---
description: in file system mode the folders a deleted schema leaves in Pkg/<package> make the next configuration publish fail with Item with name "<deleted schema>" not found, until they are removed
applies-to:
  - clio/Package/DeletedItemFileCleaner.cs
  - clio/Command/DeleteSchemaCommand.cs
ticket: gh-1746
date: 2026-10-07
---

**What is true**: on Creatio 10.2.414 (.NET Framework, MSSQL) in file system mode,
`WorkspaceExplorerService.svc/Delete` removes a schema's database rows only. Its `Schemas/<name>/` and
`Resources/<name>.<manager>/` folders stay in `Pkg/<package>`. The leftovers cause two separate failures:

- The next `pkg-to-db` registers the deleted schema again.
- Before that import runs, the next schema save that publishes the configuration fails with
  `Item with name "<deleted schema>" not found`. Seen with `create-entity-schema` after deleting another
  schema of the same package. The schema being saved is stored, but stays unpublished.

Once the two folders were removed by hand, the next create-and-publish succeeded. Nothing else changed
between the failing and the succeeding run, and there was no restart.

**Why it is this way**: in file system mode the site builds its configuration from the package folders.
The single-item delete never touches them; only a package-level file-system export removes item folders
(`PackageFileStorage.DeletePackageItems`). As a result, the folder still declares a schema that the
database no longer has.

**What breaks if you ignore it**: `delete-schema` reports success, but the site stays broken in a way that
points elsewhere. Every later publish of any schema fails and names the deleted one, and an agent retries
the unrelated save. The same symptom is recorded for an FSM desktop lifecycle in
[`McpServer/fsm-desktop-remote-lifecycle-poisons-later-saves.md`](../McpServer/fsm-desktop-remote-lifecycle-poisons-later-saves.md).
When `delete-schema` warns that it could not remove the folders, remove them before the next save, not just
before the next `pkg-to-db`.
