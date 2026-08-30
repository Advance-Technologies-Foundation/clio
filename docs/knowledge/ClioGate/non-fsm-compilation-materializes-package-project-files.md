---
description: compiling an assembly package with file design mode disabled materializes source and a generated package csproj under Terrasoft.Configuration/Pkg/<package>/Files
applies-to:
  - cliogate/Files/cs/CreatioApiGateway.cs
  - clio/Command/ShowPackageFileContentCommand.cs
date: 2026-08-30
---

**What is true** — Creatio compilation materializes assembly-package auxiliary files under
`Terrasoft.Configuration/Pkg/<package>/Files` even when file design mode is disabled. The generated
files include `<package>.csproj`, so the ClioGate package-file endpoints can read the compiled
project directly from that directory; this behavior does not require a database fallback.

**Why it is this way** — file design mode controls the developer synchronization workflow, while
assembly-package compilation still creates the project and source files the compiler consumes.
Creatio documents that generated project as an auxiliary compilation file.

**What breaks if you ignore it** — treating a non-FSM package as database-only sends the reader to
unrelated schema-storage tables, duplicates Creatio's compilation projection, and can return a
project different from the one the platform actually compiled. Reading the materialized `Files`
directory is the authoritative path after compilation.
