# MCP E2E Tiering — Specification (ENG-92150)

## Problem

The `clio.mcp.e2e` suite carries ~60 persistent trunk failures. The root cause for most of
them is not a product defect but an **infrastructure dependency**: a large share of the
end-to-end tests require a fully stood-up live Creatio (product deploy + `cliogate`
installed + initialized services + seeded packages `AutoTest,AutoTestClioMcp`). That bring-up
only happens inside the TeamCity job (`Team_Atf_ClioMcpE2eTests`). Off-CI — on a developer
machine or in a fast pre-merge check — those tests cannot be reproduced or validated, so
tickets close on merge rather than on green, and regressions in the env-free behavior
(tool discovery, input-schema shape, argument binding, wording, deserialization) are masked
by the noise of env-dependent failures.

## Goal of this change (ENG-92150, scoped)

Make the suite **separable by infrastructure tier** so the env-free subset can be run fast
and deterministically by anyone, and so whoever owns the TeamCity job can later gate the
two tiers on different steps. This change is **purely additive** (NUnit category traits only)
— no test logic, no product code, no GitHub Actions workflow changes.

## Tier model

Two tiers, mutually exclusive, assigned by what a test's **arrange path requires**:

| Tier | Category string | Requires live Creatio? | Arrange signal |
|------|-----------------|------------------------|----------------|
| NoEnvironment | `McpE2E.NoEnvironment` | No | Starts the clio MCP server child process and asserts tool discovery / input-schema shape / advertisement / wording / config parsing / deserialization / invalid-environment failure shape. Arrange does **not** require a reachable environment, destructive opt-in, or `cliogate`. |
| Sandbox | `McpE2E.Sandbox` | Yes | Arrange reaches Creatio/Redis/`cliogate`: `requireReachableEnvironment: true` / `requireEnvironment: true`, `requireDestructiveOptIn` / `AllowDestructiveMcpTests`, `EnsureCliogateInstalledAsync`, `SandboxEnvironmentResolver`, `RegisteredClioEnvironmentSettingsResolver`, `RedisSandboxClient`, `SeededApplicationResolver`, `EnsureSandboxIsConfigured`, reads `EnvironmentPath` / `ConnectionStrings.config`, or runs `install-gate` / `reg-web-app` as part of arrange. |

### Classification rule (deterministic, arrange-gate based)

A test is **Sandbox** if and only if its arrange path (the test body plus the helper methods
it transitively calls, in-file) touches any sandbox signal listed above. Otherwise it is
**NoEnvironment**. Ambiguous cases default to **Sandbox** (the safer tier — a misclassified
Sandbox test simply skips locally; a misclassified NoEnvironment test would fail the fast
gate).

NoEnvironment tests stay green with no sandbox configured because they assert the *degraded*
contract: tool advertisement, JSON-schema shape, argument binding, and the structured
**failure** returned for an unknown/unreachable environment (using a random invalid
environment name) — none of which needs a live Creatio.

### Tagging mechanism

- **Uniform fixtures** (all tests in one tier): a single `[Category("McpE2E.<tier>")]` on the
  `[TestFixture]` class.
- **Mixed fixtures** (both tiers present): `[Category("McpE2E.<tier>")]` on each `[Test]`
  method.
- Tags are **additive only** — no existing category, `[AllureXxx]`, `[Description]`, or other
  attribute was removed or renamed. The legacy/implicit "E2E" categorization is unaffected.

### Measured tier counts

Counts measured on branch `feature/ENG-92150_mcp-e2e-tiering` (`net10.0`):

- 346 e2e tests across 60 fixtures. Uniform fixtures carry a single class-level
  `[Category]`; mixed fixtures carry a per-test `[Category]`.
- **NoEnvironment**: 171 tests — run with no sandbox configured: all pass, 0 fail, 0 env-gated skips.
- **Sandbox**: 175 tests — they do **not** run clean locally without a stand. Most
  self-skip (`Assert.Ignore`) with an explicit reason; a subset that calls
  `EnsureSandboxIsConfigured` before checking the destructive opt-in **fails fast** with
  actionable diagnostics when the sandbox config is absent (this is the documented
  "fail fast" contract in `clio.mcp.e2e/AGENTS.md`, not a regression — it reproduces on
  `master` with an empty sandbox config). Either way they belong on the deploy-backed step,
  never on the fast NoEnvironment gate.

(Exact run output recorded in `mcp-e2e-tiering-validation.md`.)

## Reproducible bring-up flow for the Sandbox tier

> UNVALIDATED here: this sequence requires the proprietary Creatio product ZIP, which is not
> available off the TeamCity job. It is documented from the clio command contracts; run it
> where a product build is present (the TeamCity job, or a developer with the ZIP). Do not
> treat it as a tested script.

```bash
# 1. Stand up local infrastructure (Postgres/Redis containers, etc.)
clio deploy-infrastructure

# 2. Deploy a local (non-k8s) Creatio from the product build ZIP
clio deploy-creatio --ZipFile <build>.zip

# 3. Register the deployed site as a clio environment named "dev" and point it at the
#    on-disk install root. Registering --ep (EnvironmentPath) also closes the ClearRedis
#    EnvironmentPath gap (ENG-91829): SandboxEnvironmentResolver reads ConnectionStrings.config
#    from this path to obtain the Redis and DB connection strings.
clio reg-web-app -n dev --ep <install-root>

# 4. Install cliogate (privileged backend the destructive tools depend on)
clio install-gate -e dev

# 5. Seed the packages the sandbox tests expect
#    (push/install AutoTest and AutoTestClioMcp into the dev environment)
#    e.g. clio push-pkg <AutoTest.gz> -e dev ; clio push-pkg <AutoTestClioMcp.gz> -e dev
```

Then point the suite at the sandbox via the `McpE2E` config (see `clio.mcp.e2e/AGENTS.md`):
`McpE2E:Sandbox:EnvironmentName=dev`, `McpE2E:Sandbox:SeedKeyPrefix=clio-mcp-e2e`,
`McpE2E:AllowDestructiveMcpTests=true`. The resolver derives `EnvironmentPath`,
`ConnectionStrings.config`, URL, login, password, and `IsNetCore` from the registered
`dev` environment.

## Gating plan in TeamCity

> GitHub Actions is explicitly out of scope for ENG-92150 (decision). The TeamCity job
> config is owned elsewhere and is not editable from this change — this section is the
> documented design, to be applied by whoever holds job-config permissions.

- **Fast, env-free, merge-relevant step** — runs without the ephemeral deploy:
  ```
  dotnet test clio.mcp.e2e/clio.mcp.e2e.csproj --filter "Category=McpE2E.NoEnvironment"
  ```
  Deterministic and green with no sandbox; suitable as a blocking pre-merge step.
- **Sandbox step** — keep on the existing deploy-backed pipeline that runs
  `deploy-infrastructure` → `deploy-creatio` → `reg-web-app` → `install-gate` → seed:
  ```
  dotnet test clio.mcp.e2e/clio.mcp.e2e.csproj --filter "Category=McpE2E.Sandbox"
  ```
- Net effect: the env-free contract is protected on every merge regardless of the
  flaky/expensive deploy, while the Sandbox tier remains where the live stand exists.

## Cross-references

- **ENG-90640** — parent: stabilize the MCP e2e suite / trunk-failure baseline.
- **ENG-92146 / ENG-92147** — per-cluster Sandbox-tier fixes (the env-dependent failures).
- **ENG-91829** — ClearRedis `EnvironmentPath` gap; closed in the bring-up by `reg-web-app --ep`.
- **ENG-91830** — related Sandbox-tier cluster fix.
- **ENG-88789** — e2e parallelization (most fixtures are `[NonParallelizable]` today).

## Non-goals

- No change to test logic, assertions, or arrange behavior.
- No new harness/sandbox provisioning code.
- No GitHub Actions workflow edits.
- No TeamCity job-config edits (documented only).
