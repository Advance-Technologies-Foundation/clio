---
description: clio.mcp.e2e cannot build on Apple Silicon without Rosetta because AspectInjector ships an osx-x64 binary; -p:AspectInjector_Enabled=false is the workaround
applies-to:
  - clio.mcp.e2e/clio.mcp.e2e.csproj
ticket: "#1537"
date: 2026-09-15
---

**What is true** — on an Apple Silicon Mac without Rosetta 2, `dotnet build clio.mcp.e2e` fails at
the post-compile step with:

```
AspectInjector : error AI_FAIL: Aspect Injector processing has failed. See other errors.
  .../aspectinjector/2.8.1/build/_bin/osx-x64/AspectInjector: Bad CPU type in executable
```

The C# compilation itself succeeds — the failure is entirely in AspectInjector's native weaver, which
the package ships only as an `osx-x64` binary. `clio.mcp.e2e` pulls it in transitively through
`Allure.NUnit`; `clio` and `clio.tests` do not weave aspects and build normally, which is why this
looks like a project-specific breakage rather than a machine one.

Two ways out. Either install Rosetta once (`softwareupdate --install-rosetta`), or skip the weaving
for a local run:

```bash
dotnet test clio.mcp.e2e/clio.mcp.e2e.csproj -c Debug -f net8.0 -p:AspectInjector_Enabled=false
```

Skipping it costs only the Allure `[AllureStep]` aspects, which affect the Allure report and nothing
a test asserts, so unit-shaped fixtures in this project run unchanged.

**Why it is this way** — AspectInjector 2.8.1 predates Apple Silicon support and publishes no
`osx-arm64` weaver. Nothing in this repository pins or wraps it; it arrives through Allure.

**What breaks if you ignore it** — an agent or engineer on Apple Silicon reads `1 Error(s)`, assumes
their own change broke the build, and starts bisecting a failure that has nothing to do with the
diff. The error text names AspectInjector but not the reason, and `AI_FAIL` says only "see other
errors" — the actual cause (`Bad CPU type in executable`) appears only at `-v n` or higher, so at
default verbosity the real message is not printed at all.

Two macOS-only e2e failures are unrelated to this and also environmental:
`McpServer_ShouldWarnOnStandardError_WhenCuratedArtifactCacheIsStale` and
`McpServer_ShouldInitializeWithinBudget_WhenGitDescendantRetainsRedirectedHandles` both fail with
"knowledge.root-path cannot contain symbolic links or junctions", because the macOS temp directory
resolves through the `/var` → `/private/var` symlink.
