# Test plan: mcp-progress-heartbeat

- **Feature:** `mcp-progress-heartbeat` · **Jira:** ENG-91274 · **ADR:** `spec/adr/adr-mcp-progress-heartbeat.md`

## Risk assessment

| Risk | Likelihood | Impact | Mitigation / test |
|------|-----------|--------|-------------------|
| Heartbeat never fires (cadence bug) → timeout persists | med | high | TC-U-1, TC-E2E-1 |
| No-op path regressed (beats sent when no token) → noise / protocol misuse | low | med | TC-U-3 |
| `work` exception masked or heartbeat task leaks | med | high | TC-U-4 |
| Beat send failure breaks tool execution | low | high | TC-U-5 |
| Tool response shape changed (broke AI contract) | low | high | TC-U-6, existing `ApplicationToolTests` regression |
| Client without progress support breaks | low | high | TC-U-3 + existing E2E validation tests still green |

## Regression scope
- `clio.tests/Command/McpServer/**` (unit) — module `McpServer`.
- `clio.tests/Command/McpServer/ApplicationToolTests.cs` — must stay green (no contract drift).
- `clio.mcp.e2e/Application*E2ETests.cs` — existing validation/readback tests must stay green.
- Targeted command: `dotnet test --filter "Category=Unit&Module=McpServer"`.

## Unit test cases (clio.tests/Command/McpServer)

| ID | Title | Expected |
|----|-------|----------|
| TC-U-1 | `RunAsync_ShouldEmitBeats_WhenWorkExceedsInterval` | with a 50 ms interval and ~200 ms `work`, beat count ≥ 1 (≈3); result returned |
| TC-U-2 | `RunAsync_ShouldReturnWorkResult_WhenWorkCompletes` | returned value equals `work()` output |
| TC-U-3 | `RunAsync_ShouldNotEmitBeats_WhenProgressTokenIsNull` | zero beats; `work` still executed and result returned |
| TC-U-4 | `RunAsync_ShouldPropagateException_AndStopHeartbeat_WhenWorkThrows` | original exception rethrown; no beats after completion; no unobserved task |
| TC-U-5 | `RunAsync_ShouldSwallowBeatFailures_WhenSinkThrows` | sink throwing does not surface; `work` result returned |
| TC-U-6 | `ApplicationSectionCreate_ShouldReturnStructuredResult_WhenInvoked` (wiring) | response shape unchanged; backend service invoked once |
| TC-U-7 | `ApplicationSectionGetList_ShouldReturnStructuredResult_WhenInvoked` (wiring) | response shape unchanged; backend service invoked once |

## E2E test cases (clio.mcp.e2e)

| ID | Title | Expected |
|----|-------|----------|
| TC-E2E-1 | `ApplicationTool_Should_Stream_Progress_For_LongRunning_Call` | invoking a long-running application tool with an `IProgress` sink yields ≥1 `ProgressNotificationValue`; gated on reachable sandbox (`Assert.Ignore` otherwise), same pattern as existing destructive section tests |

## Notes
- E2E suite is **not** in CI yet (per project-context.md) — TC-E2E-1 is run manually / on demand.
- Determinism: TC-U-1 uses a generous tolerance (≥1 beat) to stay stable on slow CI agents.
