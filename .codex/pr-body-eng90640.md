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

<!-- FILL: run id, failed/passed/ignored, comparison, residual list -->

## Residual failures — env/infra/stand-gated, NOT clio code (root cause verified)

These do not move with clio code changes; they are tracked as sub-tasks:

- **ClearRedis ×4** — Redis unreachable from the CI agent (now fails fast instead of hanging) → **ENG-91829**.
- **DataForge ×3** — similarity index init; gate is ready, needs the toggle on → **ENG-92147**.
- **k8s infra ×4** — AssertInfrastructure / ShowPassingInfrastructure / DeployCreatio / RestoreDb need a real cluster → **ENG-91830**.
- **ApplicationSectionCreate_WithPlatformEntity, EntitySchema clear-default/LookupConst, PageUpdate UnAwaitedContext** — stand-seed/online; code verified correct.
- **PageSync MaxLength / Proxy** — AST detection on marker-delimited body → **ENG-91966**.

Estimated code-side floor ≈ 13.

## Job-config handoff (separate, requires TeamCity admin)

For permanently green vcs runs, the job needs (out of scope for this code PR):
1. Add `env.McpE2E__Sandbox__EnvironmentUrl=%DeployedUrl%` (+`EnvironmentPath`, +DataForge toggle) to the job config.
2. Add `--blame-hang --blame-hang-timeout 5m` to the active "Run MCP e2e" step args.
3. Re-enable + fix the disabled env-reg step 5.

## Tests

- `clio.tests`: Module=Common (RedisDatabaseSelector, CliogateHttpReadinessProbe), Module=McpServer (BaseTool, BusinessRuleTool, DataForgeTool, PageTools), ExceptionReadableMessageExtension — all green locally (net10.0).
- e2e NoEnvironment tier 166/167 green locally; sandbox-tier validated on CI (run above).

🤖 Generated with [Claude Code](https://claude.com/claude-code)
