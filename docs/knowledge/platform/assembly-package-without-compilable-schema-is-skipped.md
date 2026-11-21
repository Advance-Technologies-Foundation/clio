---
description: an assembly package whose C# is only under Files/ (the add-package template) is skipped by every target-side compile on Creatio 10.x
applies-to:
  - clio/tpl/package/
  - clio/Package/PackageCreator.cs
  - clio/tpl/workspace/AGENTS.md
ticket: ENG-106256
date: 2026-11-21
---

**What is true** — On Creatio 10.0.0.171 or later, a target-side compile builds an assembly package only when it has
compilable content (a SourceCode, process or user-task schema, or a reference assembly); C# under `Files/src/cs`
does not count. `clio add-package` without `--as-app` makes exactly such a package: the package and full compiles log
`skipped (no compilable schemas)`, the install compiles nothing, no DLL is built. One `clio add-schema <Name> -t
source-code` schema fixes it (measured 2026-11, Creatio 10.2 and trunk, .NET Framework). An install also skips a
package whose pushed `Files/Bin/<Package>.dll` loads, so a locally built DLL keeps running. 8.x builds every one.

**Why it is this way** — The platform decides compilability from schemas (`PackageCompilabilityInspector`), not from
the files a csproj would glob.

**What breaks if you ignore it** — The package installs and "compiles" without an error and its services return 404;
`compile-package --wait` waits for history that is never written. The workspace template carries the caveat: keep it
in step with any change that adds a schema to `add-package`.
