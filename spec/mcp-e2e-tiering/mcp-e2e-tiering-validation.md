# MCP E2E Tiering — Local Validation (ENG-92150)

Records the local run that proves the tier boundary. Runtime: `.NET 10` (`-f net10.0`).
Sandbox config was neutralized via environment variables so the boundary is honest
(no reachable Creatio, no destructive opt-in):

```
McpE2E__AllowDestructiveMcpTests=false
McpE2E__Sandbox__EnvironmentName=
McpE2E__Sandbox__EnvironmentPath=
McpE2E__Sandbox__SeedKeyPrefix=
```

## Build

```
dotnet build clio.mcp.e2e/clio.mcp.e2e.csproj -f net10.0
```
Result: 0 errors. (Pre-existing `CLIO005` warnings originate in `clio/`, not in the tagged
e2e test files.)

## NoEnvironment tier (fast, env-free gate)

```
dotnet test clio.mcp.e2e/clio.mcp.e2e.csproj -f net10.0 --no-build \
  --filter "Category=McpE2E.NoEnvironment"
```

Outcome: green. With the sandbox neutralized, the NoEnvironment tier passes with
**0 failures and 0 (env-gated) skips** — every result is `Passed`. The one host-infra
failure observed during classification (`AssertInfrastructure_Should_Return_Full_Structured_Result`,
`assert-infrastructure` returning `InternalError` with no Docker/k8s tooling) was reclassified
into the Sandbox tier, which restored a clean NoEnvironment run. Each test spawns a fresh
clio MCP child over stdio (~10-19s/test), so the full 171-test sweep is slow but
deterministic.

## Sandbox tier (skipped / fail-fast locally without a stand)

```
dotnet test clio.mcp.e2e/clio.mcp.e2e.csproj -f net10.0 --no-build \
  --filter "Category=McpE2E.Sandbox"
```

Outcome: does not run clean locally (as designed). With no stand configured the Sandbox
tier either self-skips with an explicit `Assert.Ignore` reason
(e.g. "Configure McpE2E:Sandbox:EnvironmentName ...") or, for the subset that calls
`EnsureSandboxIsConfigured` before checking the destructive opt-in, fails fast with the
documented actionable diagnostic ("...need an explicit sandbox environment name"). No
Creatio state is mutated. These belong on the deploy-backed step, never on the fast gate.

## Notes

- `AssertInfrastructureToolE2ETests` was moved to the Sandbox tier: its arrange is
  environment-free, but the `assert-infrastructure` tool probes host infrastructure
  (Kubernetes/Docker/local DB/filesystem) and returns `InternalError` when that tooling is
  absent. Per the classification rule, host-infra-dependent tests default to Sandbox so the
  NoEnvironment gate stays deterministically green.
