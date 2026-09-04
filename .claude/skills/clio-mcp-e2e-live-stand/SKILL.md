---
name: clio-mcp-e2e-live-stand
version: 1.0.0
description: >-
  Prepare a live Creatio stand so the clio.mcp.e2e suite can actually run against it, and run the
  environment-dependent tests. Use when a task hands you a stand URL and asks to run MCP e2e tests, when
  a Sandbox-category test is skipped for want of a reachable environment, or when an acceptance criterion
  is stuck at "compile-only evidence" — "прогони e2e на стенді", "ось живий стенд", "run the sandbox
  tests". Covers registering the stand correctly, the two stand prerequisites that block the whole suite,
  seeding the shared fixture page, and the exit-code trap that makes arrange steps lie.
---

# clio MCP e2e against a live stand

Turning a bare stand URL into something `clio.mcp.e2e` can run against. Most of the work is
prerequisites, not tests — and two of them block the entire suite, not one case.

## 1. Register the stand — two things that are easy to get wrong

**Use the FQDN.** A short host name (`ts1-core-dev04`) may not resolve from a macOS client even when
the corporate VPN is up; `.NET` reports `nodename nor servname provided`. Use
`ts1-core-dev04.tscrm.com`.

**Get `IsNetCore` right, and detect it rather than guess.** A `.NET Framework` stand is served as a
parent app plus a `/0` sub-application mapped to `Terrasoft.WebApp`. Check it:

```powershell
Get-WebApplication -Site '<iis-site>' | Select-Object path, physicalPath
#   /<stand>            -> C:\WebAppRoot\<stand>\
#   /<stand>/0          -> C:\WebAppRoot\<stand>\Terrasoft.WebApp     <-- .NET Framework
```

Register with an **isolated `CLIO_HOME`** so the developer's real environment catalog is never touched:

```bash
export CLIO_HOME=/tmp/cliohome-e2e && mkdir -p "$CLIO_HOME"
dotnet <repo>/clio/bin/Debug/net10.0/clio.dll reg-web-app <name> \
  -u http://<fqdn>:<port>/<stand> -l Supervisor -p Supervisor --IsNetCore false
```

Verify with something that requires a real login — `ping-app` and `ping` can succeed without auth.
`get-info` is the honest probe; it also prints `frameworkKind` and `dbEngineType`, which confirms the
`IsNetCore` choice after the fact.

## 2. Point the suite at it

`TestConfiguration.Load()` binds the `McpE2E` section from `appsettings.json` **plus environment
variables**, so drive it from the environment:

```bash
export McpE2E__Sandbox__EnvironmentName=<name>
export McpE2E__AllowDestructiveMcpTests=true
# spawned clio child processes get the DEFAULT clio home unless you forward it:
export McpE2E__ProcessEnvironmentVariables__CLIO_HOME="$CLIO_HOME"
```

That last one matters: the suite forwards `ProcessEnvironmentVariables` to every clio process it
spawns, and without it the children look in the real catalog and will not find your environment.

Categories: `McpE2E.NoEnvironment` runs anywhere; **`McpE2E.Sandbox` needs the stand**. GitHub CI does
not run `clio.mcp.e2e` at all — a locally green result is evidence for you, not for the acceptance
gate, which needs a TeamCity `Team_Atf_ClioMcpE2eTests` run.

## 3. The two prerequisites that block the whole suite

Both were found by running, not by reading, and neither is documented in the suite itself.

**`SchemaNamePrefix` must permit the fixture name FOR THE WHOLE RUN.** Four e2e files depend on a page
literally named `ClioMcp_BlankPageToSave`. On a stand with the default `SchemaNamePrefix = Usr`,
Creatio rejects `create-page` **and every later `update-page`** with *"code ... must start with the Usr
prefix"*. Relaxing it only while seeding is **not enough** — the prefix is enforced on save too, so the
arrange step succeeds and the act step fails:

```bash
dotnet <clio.dll> set-syssetting SchemaNamePrefix ClioMcp_ -e <name>   # before the run
# ... run the tests ...
dotnet <clio.dll> set-syssetting SchemaNamePrefix Usr -e <name>       # restore afterwards
```

The server's own rejection message tells you the current value, which is how you know what to restore
to when `cliogate` is absent and SQL is unavailable.

**The fixture is not self-seeding.** No test creates `ClioMcp_BlankPageToSave`; it must pre-exist in a
writable package. On a stock stand the only writable package is **`Custom`** (find it by listing
packages and filtering for a maintainer that is not `Creatio`):

```bash
dotnet <clio.dll> create-page --schema-name ClioMcp_BlankPageToSave \
  --template BlankPageTemplate --package-name Custom -e <name>
```

Leaving that page behind is a **benefit** — it makes the stand usable for the rest of the suite rather
than only for one test. Say so rather than reverting it.

## 4. The trap that makes arrange steps lie

**`clio get-page` exits 0 when it fails.** A missing schema prints
`{"success":false,"page":null,"error":"Schema 'X' not found"}` with exit code **0**. A test whose
arrange step asserts `ExitCode == 0` therefore *passes* on a stand where nothing was materialised, and
then fails later on a file-existence check pointing at the wrong cause.

Consequences worth carrying:
- when a Sandbox test fails oddly, check whether the arrange step actually produced files, not just its
  exit code;
- treat this as a class — the sibling read commands (`list-pages`, `get-schema`,
  `get-client-unit-schema`, `get-classic-page-sources`) want the same audit;
- when writing an arrange step, gate on the artifact or on `success`, never on the exit code alone.

## 5. Running and reporting

```bash
dotnet test clio.mcp.e2e/clio.mcp.e2e.csproj -c Debug -f net10.0 \
  --filter "FullyQualifiedName~<TestName>"
```

Report which category actually executed. `Assert.Ignore` is how these tests skip when no environment
answers, and an ignored test reads as "not failing" in a summary line — never quote a run that skipped
the environment-dependent case as coverage of it.

## 6. Leave the stand as you found it

- restore every system setting you changed, and record the original **before** changing it;
- keep the registration in an isolated `CLIO_HOME` so the real catalog is untouched;
- state explicitly what you deliberately left behind (the seeded fixture page) and why.

## Related

`creatio-development:clio` for general CLI usage · the ADR and test plan under
`spec/mcp-worker-execution-boundary/` for what the worker-boundary e2e tests assert ·
`run-clio-mcp-e2e` for triggering the TeamCity suite that is the actual acceptance gate.
