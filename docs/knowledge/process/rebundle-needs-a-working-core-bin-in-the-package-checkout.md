---
description: rebundle-process-builder.ps1 builds the package from the crt-process-builder checkout, which resolves its platform references through .application/net-framework/core-bin - a junction a Creatio core reinstall silently leaves dangling, after which every build fails with ~900 "The name 'Terrasoft' does not exist in the current context" errors that read like broken package sources
applies-to:
  - rebundle-process-builder.ps1
  - docs/agent-instructions/bundled-packages.md
ticket: ENG-91853
date: 2026-09-06
---

**What is true** — the rebundle script's first real step is `dotnet build MainSolution.slnx -c dev-nf`
inside the crt-process-builder checkout, and that build resolves every `Terrasoft.*` reference through
`<checkout>/.application/net-framework/core-bin`. On a developer machine that path is a **junction**
into a local Creatio installation (historically
`C:\Projects\Creatio\TSBpm\Src\Lib\Terrasoft.WebApp.Loader\Terrasoft.WebApp\bin`). Reinstalling or
moving that Creatio core deletes the target and leaves the junction behind, pointing at nothing.

**The failure does not say so.** The build emits roughly 900 `error CS0103: The name 'Terrasoft' does
not exist in the current context`, spread across ordinary package files, and the script then prints
`Package build failed. Shipping sources the target cannot compile installs a package that never
works.` Every visible symptom points at the package sources. Nothing mentions the junction, and
`Test-Path` on a dangling junction returns **true**, so an existence check does not find it either —
`Get-Item -Force` and reading `.Target` / `.LinkType` is what does.

**Why it is this way** — `.application/` is gitignored (it is the local Creatio app, not source), so
the junction is per-machine state no clone or checkout restores, and nothing validates it before the
compiler is invoked.

**What breaks if you ignore it** — a rebundle is impossible and the reason looks like a broken branch.
The dangerous version is the one that is *not* blocked: giving up on the script and building by hand
with `-p:CoreLibPath=` / `-p:TestCoreLibPath=` overrides produces an assembly but skips everything
else the script does — the restamp, the four pin refreshes, the archive inventory check and the clio
rebuild — which is exactly the drift
[bundled-packages.md](../../agent-instructions/bundled-packages.md) exists to prevent.

**The repair** is local and takes a minute: delete the dangling junction and put a real directory in
its place holding the platform assemblies (the current Creatio installation's `Terrasoft.WebApp\bin`,
plus `System.Net.Http.Json` and `System.Text.Json` if the build asks for them). Then run the canonical
one call. Do not commit anything: `.application/` is ignored, and the repair is machine state.

```powershell
$link = "<checkout>\.application\net-framework\core-bin"
(Get-Item -LiteralPath $link -Force).Target   # dangling if this path no longer exists
```
