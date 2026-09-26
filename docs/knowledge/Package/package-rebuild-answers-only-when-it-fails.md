---
description: RebuildPackage answers with a success:false/CSxxxx body when the C# does not compile, but drops the connection without an answer when it succeeds; the 5 s history settle returns before the build ends
applies-to:
  - clio/Package/PackageBuilder.cs
ticket: 1633
date: 2026-09-26
---

**What is true** — measured on a live stand (Creatio core 10.2.254, .NET Framework, MSSQL) with a
C# schema in `Custom`:

- With a `CS0246` in the schema, `ServiceModel/WorkspaceExplorerService.svc/RebuildPackage` answered
  HTTP 200 with `{"success":false,"buildResult":1,"errors":[{"errorNumber":"CS0246","fileName":…,
  "line":5,"column":26,…}]}`. On a busier second run the same failure came back as
  `success:false, buildResult:0, errors:[]` with the reason in `errorInfo.message` (static-content
  generation errors), while the `CompilationHistory` row still carried the CS0246 in
  `ErrorsWarnings`. So the response and the history each hold part of the verdict.
- With the error fixed, the same request got **no answer**: the connection was dropped, as it is for
  a configuration build (see `configuration-build-sends-no-http-response.md`).
- `api/ConfigurationStatus/GetLastCompilationResult` IS updated by a package rebuild (it showed the
  CS0246 after the failed rebuild and `success:true` after the fixed one), but it carries no
  timestamp.
- After the failed rebuild (and deleting the broken schema), the next two `Custom` rebuilds were
  answered `success:false` with `errorInfo.message` "Could not find file
  …\Terrasoft.Configuration\bin\Terrasoft.Configuration.dll"; one `compile-configuration`
  restored the file and the package rebuild succeeded again. The failure message therefore says the
  new code was not loaded, not that the old assembly file is intact on disk.
- The history rows of one package rebuild (`Terrasoft.Configuration.Dev.csproj`, then
  `Terrasoft.Configuration.ODataEntities.csproj`) arrived 8 s apart, and the default
  `compile-package` settled 5 s after the first one - before the second was written.

**Why it is this way** — a successful build reloads the runtime, which tears down the connection its
answer would travel on; a failed build does not reload, so the server stays up and answers.

**What breaks if you ignore it** — discarding the response body (what `PackageBuilder` did until
issue #1633) turns a C# compile error into `Done` and exit 0 whenever the answer arrives before the
history settles. Treating the dropped connection as a failure breaks every successful `--wait`
build. And a 5 s settle cannot mean "built": a probe or restart run right after it can still hit
the previous assembly (issue #1632). That is why `--wait` ends at once only on a FAILURE answer, and
after a success answer or a dropped request keeps observing until history has been quiet for the
45 s window: the reporter's .NET 8 host appears to answer before its build is done, which this stand
could not show. The window starts only after the first history row: a success answer followed by
silence ended a waited build after 45 s while the reported build takes 60-120 s (PR #1688 review),
so with no row at all the wait ends in a timeout, not an inferred success.
