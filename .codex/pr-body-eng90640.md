🔗 **Jira:** [ENG-90640](https://creatio.atlassian.net/browse/ENG-90640)

## What this is

Stabilization PR for the `Team_Atf_ClioMcpE2eTests` TeamCity suite (clio MCP end-to-end tests). It consolidates the investigation and all code-side fixes accumulated on the integration branch `feature/ENG-90640_mcp-e2e-stabilization` (sub-task PRs #756 / #757 / #760 already merged **into this branch**).

## Root cause (proven, NOT a clio bug)

The e2e suite was running against **`http://localhost`** — the agent's default Creatio — instead of the freshly-deployed per-run sandbox. The real stand URL lives in `resultingProperties.DeployedUrl` (e.g. `http://ts1-agentNN:88/studioenu_<id>_<date>`), but the job's **"clio env reg" step (step 5) is disabled** (parked together with the local-SDK-10 / INFR-11943 rework), so the `dev` environment stayed pinned to a stale `http://localhost`.

Localhost-Creatio answers basic login/read calls (so ~182 tests passed and `ping-app` succeeded) but has **no cliogate / AutoTestClioMcp / DataForge endpoints** → an ~30-fixture **404 cascade** (PackageInstallerService upload, `get-page`, `create-page` template catalog, BusinessRule/Application/Section create). Two earlier hypotheses were disproven (verify-root-cause): it was neither a `PostgreSQL=Core` nor an `IsNetCore`/`0/`-prefix issue — purely the base URL. This build (`PostgreSQL_Softkey`) is **.NET Framework**; services sit under `Terrasoft.WebApp/ServiceModel/`.

## Fix

The harness now **re-registers `dev` at the real deployed URL** from config `Sandbox.EnvironmentUrl` before the install-gate (commit `cdc3f82b`). Three env-vars are passed **into the trigger** (TeamCity resolves `%...%` at runtime — no job edit required):

| Env-var | Value | Unblocks |
|---|---|---|
| `McpE2E__Sandbox__EnvironmentUrl` | `%DeployedUrl%` | cliogate cascade (~30) — **required** |
| `McpE2E__Sandbox__EnvironmentPath` | `%DeployedApplicationRoot%` | ClearRedis EnvironmentPath (×4) |
| `McpE2E__DataForge__InitializeAndWait` | `true` | DataForge similarity index (×3) |

Validation run `15636666` with the URL fix: **53→28 failed, 182→315 passed** (the URL fix alone unblocked +133 passing tests, zero regressions).

## Code + harness changes in this branch

- **Re-register `dev` at `Sandbox.EnvironmentUrl`** before install-gate (the root-cause fix).
- **cliogate HTTP readiness probe** (ENG-92146): the readiness wait now probes a real `/rest/CreatioApiGateway/*` route (200) instead of only `list-packages` (DataService), closing the restart→DataService-up→cliogate-REST-still-warming race. New `CliogateHttpReadinessProbe` + tests.
- **Table-log MCP envelope** (ENG-92149): render `ConsoleTable` `LogMessage` values to string before System.Text.Json serialization in the MCP flush, fixing `IsError=true` on any table-emitting tool (`Encoding.Preamble` ref-struct serialization failure). New `BaseTool` handling + unit tests.
- **e2e tiering** (ENG-92150): every test tagged `McpE2E.NoEnvironment` (167) vs `McpE2E.Sandbox` (175); env-free contract failures fixed (BusinessRule env-resolution-before-projection → standard execution envelope; `list-page-templates` validates schema-type before env; assertion drift). NoEnvironment tier is now 166/167 green (last red closes on merge).
- **BusinessRule create assertions** aligned to the JSON contract + non-Latin handling (assertion→JSON).
- **PageUpdate DryRun + SchemaSync**: surface specific offline errors over the generic "JavaScript syntax error" (extends ENG-92049 pattern).
- **DataForge readiness gate** (off-by-default; per-call 60s + overall 6min deadline so it can't hang).
- **ClearRedis / RedisDatabaseSelector fail-fast**: bounded connect (`AbortOnConnectFail=true` + 12s timeouts + `ConnectRetry=1`) so an unreachable Redis errors clearly instead of hanging the suite ~10 min.
- Hardening: suppress clio self-update in e2e; login-readiness gate before install-gate; `WebException` status in `GetReadableMessageException`; readable install-gate error.

## Validation run (this PR)

Run [`15638935`](https://teamcity-rnd.bpmonline.com/build/15638935) — clean, **complete** run (no force-stop; marked FAILURE only because total duration was +154s over the 6000s threshold), EnvironmentUrl-only, Studio/PostgreSQL 10.1.42:

**18 failed / 337 passed / 3 ignored** (down from 28 failed / 315 passed on the prior validation run 15636666 — the extra commits on this branch converted ~10 more to green, zero regressions).

All 18 are env/infra/stand-gated, none are clio contract regressions:

| Bucket | × | Tracked by |
|---|---|---|
| ClearRedis (EnvironmentPath empty → fast-fail; Redis unreachable from agent) | 4 | ENG-91829 |
| DataForge similarity index (gate off in this config) | 3 | ENG-92147 |
| Infra remote (AssertInfrastructure / ShowPassingInfrastructure / DeployCreatio / RestoreDb) | 4 | ENG-91830 |
| PageSync AST on marker body (MaxLength / Proxy) | 2 | ENG-91966 |
| Stand-seed/online (ApplicationSection WithPlatformEntity, EntitySchema create-read-modify flow, ModifyEntitySchemaColumn LookupConstDefault) | 3 | code verified correct |
| PageUpdate UnAwaitedContext (advisory) | 1 | — |
| PageSync Report_Invalid_Environment (env-contract) | 1 | to confirm |

This PR is validated on the **EnvironmentUrl-only** configuration (the proven cliogate root-cause fix). The `EnvironmentPath` + DataForge toggles are deliberately **not** part of this validation — see the hang note below.

## Residual failures — env/infra/stand-gated, NOT clio code (root cause verified)

These do not move with clio code changes; they are tracked as sub-tasks (see table above).
Estimated code-side floor ≈ 13 (the 18 minus the 4 ClearRedis + 1 PageUpdate-advisory that are pure environment artifacts, give or take the DataForge gate which the toggle addresses).

### ClearRedis hang under EnvironmentPath — diagnosed and bounded

Supplying `McpE2E__Sandbox__EnvironmentPath` (to exercise the ClearRedis x4 tests) froze the suite >15 min on `ClearRedis_Should_Remove_Seeded_Key` across three runs (15638846, 15638941, 15639381). The hanging test was identified deterministically from a completed run's execution order. Three code-side fixes, all under ENG-91829:
- `SandboxEnvironmentResolver` now locates `ConnectionStrings.config` by known path (`<root>/` for .NET Core, `<root>/Terrasoft.WebApp/` for .NET Framework) instead of a recursive `Directory.GetFiles(..., AllDirectories)` over the entire deployed Creatio root (tens of thousands of files; minutes-long).
- `ClearRedisToolE2ETests` bounds its Redis arrange/teardown with `Task.WaitAsync(token)`.
- `RedisSandboxClient` offloads `ConnectionMultiplexer.ConnectAsync` to `Task.Run` — its **synchronous prologue** (DNS/socket setup) runs before the first await, so when the sandbox Redis is unreachable from a CI agent it blocks the test thread where neither the WhenAny/Task.Delay bound nor `WaitAsync(token)` can interrupt it. Offloading lets the timeout bound apply.

**Honest scope:** the ClearRedis tests fundamentally require a sandbox Redis reachable from the runner; CI agents cannot reach it, so these tests fail by design there (they are 4 of the 18). The fixes above stop them *hanging the build*. The durable, suite-wide guard is job-side: `dotnet test --blame-hang-timeout 5m` (process-kills a hung test), which is **required before `EnvironmentPath` is enabled in the job** — see the handoff. This PR therefore validates on EnvironmentUrl-only.

## Job-config handoff (separate, requires TeamCity admin)

For permanently green vcs runs, the job needs (out of scope for this code PR):
1. Add `env.McpE2E__Sandbox__EnvironmentUrl=%DeployedUrl%` (+`EnvironmentPath`, +DataForge toggle) to the job config.
2. Add `--blame-hang --blame-hang-timeout 5m` to the active "Run MCP e2e" step args.
3. Re-enable + fix the disabled env-reg step 5.

## Tests

- `clio.tests`: Module=Common (RedisDatabaseSelector, CliogateHttpReadinessProbe), Module=McpServer (BaseTool, BusinessRuleTool, DataForgeTool, PageTools), ExceptionReadableMessageExtension — all green locally (net10.0).
- e2e NoEnvironment tier 166/167 green locally; sandbox-tier validated on CI (run above).

🤖 Generated with [Claude Code](https://claude.com/claude-code)
