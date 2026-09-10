---
description: rebundle-process-builder.ps1 builds the package from the crt-process-builder checkout, which resolves its platform references through TWO junctions under .application/net-framework - core-bin and bin - either of which a Creatio core reinstall silently leaves dangling, and they fail differently: a dangling core-bin takes the whole build down with ~900 "The name 'Terrasoft' does not exist in the current context" errors that read like broken package sources, while a dangling bin takes only the TEST project down with one MSB3245
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

## There are TWO junctions, and the second one fails differently

`.application/net-framework/` holds **two** links, and the repair above only names one:

| link | points at | what a dangling one costs |
|---|---|---|
| `core-bin` | the core's `Terrasoft.WebApp\bin` | the ~900-error failure above: nothing compiles |
| `bin` | a `Terrasoft.WebApp\conf\bin\<generation>` | **the package still builds.** Only the TEST project fails, with a single `MSB3245: Could not locate the assembly "Terrasoft.Configuration"` |

That second failure is the one to know about, because it arrives AFTER a clean package build — the
script has already printed the package's own `Build succeeded`. `dev-nf` promotes MSB3245 from a
warning to an error deliberately (`.build-props/env.dev-nf.props` says why), which is what makes it a
stop rather than a suite running against a missing reference. The test project reaches the assembly as
`$(TestCoreLibPath)\..\bin\Terrasoft.Configuration.dll` — through `bin`, not through `core-bin`.

The `conf\bin` generation number is per-installation and increments on every configuration compile, so
the generation the old link named will not exist on a fresh core. Pick the highest-numbered directory
that contains `Terrasoft.Configuration.dll`.

## Where the core actually moved to

The historical target above (`C:\Projects\Creatio\TSBpm\...`) is not where a current local Creatio
lives. The dev-environment bootstrap checks the core out under `.devenv\repos\core`:

```
C:\Projects\Creatio\.devenv\repos\core\TSBpm\Src\Lib\Terrasoft.WebApp.Loader\Terrasoft.WebApp\bin
C:\Projects\Creatio\.devenv\repos\core\TSBpm\Src\Lib\Terrasoft.WebApp.Loader\Terrasoft.WebApp\conf\bin\<generation>
```

`clio list-environments` reports that path for the stand as `environmentPath`, which is the quickest
way to find it without guessing: the stand and the junctions have to point at the SAME core, and a
junction still aimed at the pre-move layout is exactly the dangling case above.
