---
description: an assembly package whose C# is only under Files/ (the add-package template) is skipped by every target-side compile on Creatio 10.x
applies-to:
  - clio/tpl/package/
  - clio/Package/PackageCreator.cs
  - clio/tpl/workspace/AGENTS.md
ticket: ENG-106256
date: 2026-11-21
---

**What is true** — On Creatio 10.0.0.171 or later (default features), a target-side compile builds an assembly
package (`SysPackage.Type` 1) only when it has compilable content: a SourceCode, process or user-task schema, or a
reference assembly. C# under `Files/src/cs` does not count. The package compile and the full compile log
`skipped (no compilable schemas): ... [<Package>]`, the install compiles nothing for it, and no `Files/Bin` DLL is
built. `clio add-package` without `--as-app` creates exactly such a package (`--as-app` adds a SourceCode
localization schema, which makes it compilable). Measured 2026-11 on Creatio 10.2 (.NET Framework) and a trunk
build; `clio add-schema <Name> -t source-code -p <package folder>` made the install compile it. Creatio 8.x builds
every assembly package. Separately, an install does not compile a package whose pushed `Files/Bin/<Package>.dll`
loads, so a locally built DLL shipped with the package keeps running.

**Why it is this way** — The platform's compile filter (`PackageCompilabilityInspector`, behind
`EnableOptimizedPackageCompilation` and `EnableCompileOnlyPackagesWithCompilableSchemas`) decides compilability from
schemas, not from the files a csproj would glob.

**What breaks if you ignore it** — A package made from the template installs and "compiles" without an error, and
its services return 404: nothing was built. `compile-package --wait` then waits for compilation history that is never
written and exits non-zero after its timeout. The workspace template's AGENTS.md carries the caveat; a template
change that adds a schema to `add-package` (or removes the caveat) must keep the two in step.
