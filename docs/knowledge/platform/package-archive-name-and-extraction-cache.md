---
description: Creatio package archive names must match the package descriptor; renaming identical bytes can retain an invalid cached extraction
applies-to:
  - clio/Command/RegisterProcessElementCommand.cs
  - clio/docs/commands/register-process-element.md
ticket: GH-1599
date: 2026-09-17
---

**What is true** — Keep the archive basename equal to the package name, for example `artifacts/issue1599/UsrCustomProcessElement.gz`. Put a validation label in the directory, not in the `.gz` filename. On Creatio 10.1.585.0, a mismatched filename caused `Install information is empty` before registration SQL ran. Renaming the same bytes was insufficient to recover the next install.

**Why it is this way** — Native `PackageZipFileStorage` extracts into a directory derived from the archive filename. `PackageFileStorage` initializes the descriptor name from that directory. With `ReuseUnzippedPackagesOnInstallApp`, extraction reuse compares archive content hashes, so a filename-only change can reuse the previous directory. Source was checked in the Creatio backend trunk; the failure and recovery were reproduced on the retained PostgreSQL non-FSM lab.

**What breaks if you ignore it** — A valid package can appear empty to dependency analysis. Rebuild a correctly named archive with a newer package descriptor stamp; do not diagnose the generated SQL from an error that occurs before script execution. Keep the published reference archive unchanged while preparing a validation copy.
