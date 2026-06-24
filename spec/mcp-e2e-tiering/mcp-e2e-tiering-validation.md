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

## NoEnvironment tier — FULL sweep result

```
dotnet test clio.mcp.e2e/clio.mcp.e2e.csproj -f net10.0 --no-build \
  --filter "Category=McpE2E.NoEnvironment"
```

**Outcome (full sweep, 30m45s): 15 failed / 155 passed / 170 total — NOT yet green.**
A partial run (first ~30) was all-green and was initially mistaken for the full result;
the complete sweep shows the tier still needs an iteration. The 15 failures split into
three kinds:

### A. Fixed by a sibling PR (1)
- `Tool_ShouldListFeatureFlags_WhenNoArgumentsSupplied` — fails only because this branch is
  off `master` and lacks PR #756 (ENG-92149, the MCP table-serialization fix). Passes once
  #756 is in the integration branch. Correctly classified NoEnvironment.

### B. Mis-classified host-infra → must move to Sandbox (3)
All fail with `McpProtocolException: Request failed (remote)` — they probe host infra/k8s
exactly like `assert-infrastructure` (already Sandbox). Reclassify **per-test** (the files
may also hold genuinely env-free advertise tests that should stay NoEnvironment):
- `DeployCreatio_Should_Not_Return_Scheduled_Maintenance_Response`
- `RestoreDb_Should_Return_Log_File_Path_On_Failure`
- `ShowPassingInfrastructure_Should_Return_Structured_Result`

### C. Genuine env-free contract failures (~11)
Reproducible with **no Creatio at all** — a locally fixable contract cluster, not stand-gated.
These overlap the CI "61" (e.g. the BusinessRule `_Report_Invalid_Environment` set was in
run 15628876), which partially overturns the "most of the 61 need a stand" read.
- `BusinessRuleCreate_Should_Bind_{ApplyFilter,RoleGate,SetValues,SysValue,ShowElement}_Payload_And_Report_Invalid_Environment`
  (×6; all `Expected callResult.IsError not to be True ... but found True`)
- `PageTemplatesListTool_Should_Reject_Invalid_Schema_Type`
- `PageValidateTool_Should_Accept_Inserted_Field_With_AutoProvided_Label`
- `SettingsHealth_Should_Report_Repaired_Status_When_Active_Environment_Key_Is_Invalid`
- `ToolContractGet_Should_Return_Invocation_Error_When_Args_Has_Invalid_Type`
- `PushWorkspace_Tool_Should_Advertise_Optional_SkipBackup_Argument`

These match the ENG-91830 ("return a structured error, not a protocol exception") and
ENG-91825 (env-validation-order) themes.

## Iteration applied — RESULT

The next-iteration plan below was executed in this branch:
1. Reclassified the 3 host-infra tests (B) per-test to Sandbox.
2. Fixed the ~11 contract failures (C) locally (BusinessRule env-resolution envelope,
   list-page-templates schema-type pre-validation, and test-assertion drift).

**Post-fix full sweep: 1 failed / 166 passed / 167 total** (29m47s, env-free, net10.0).
The single remaining failure is `Tool_ShouldListFeatureFlags_WhenNoArgumentsSupplied`
(group A) — fixed by PR #756, which is not in this isolated branch. The tier is therefore
green once #756 lands in the integration branch (expected 167/167). Total dropped 170→167
because the 3 host-infra tests moved to the Sandbox tier.

### Original (pre-fix) iteration plan, for reference
1. Reclassify the 3 host-infra tests (B) per-test to Sandbox. ✓
2. Fix the ~11 contract failures (C) locally. ✓
3. Re-run; tier green after #756 merges + cluster (C) fixed. ✓ (166/167; #756 pending)

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

## Acceptance gate for the NoEnvironment tier

The standing pass criterion is **`Total == Passed` AND `Skipped == 0`**, not merely
`Failed == 0`. A skip is the signature of a false-NoEnvironment test: a misclassified test
that reaches an environment calls `Assert.Ignore` and is reported as *skipped*, not *failed*,
so in a partial run a skip masquerades as a pass (exactly the trap that made the first
~30-test run look green — see the FULL sweep section above). Always run the complete
env-free filter with no stand configured and confirm the recorded `Skipped` count is `0`
before treating the tier as green. Apply the same `Skipped == 0` check to any future
NoEnvironment e2e additions.

## Notes

- `AssertInfrastructureToolE2ETests` was moved to the Sandbox tier during classification:
  its arrange is environment-free, but `assert-infrastructure` probes host infrastructure
  (Kubernetes/Docker/local DB/filesystem) and returns `InternalError` when that tooling is
  absent. The B-group above is the same class, caught by the full sweep.
