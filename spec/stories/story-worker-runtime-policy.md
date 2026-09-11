# Worker runtime policy inheritance

Issue: [#1409](https://github.com/Advance-Technologies-Foundation/clio/issues/1409)
Status: review
Architecture: existing [worker execution boundary ADR](../adr/adr-mcp-worker-execution-boundary.md), frozen environment rule.

When a user explicitly configures `DOTNET_ROLL_FORWARD` for the MCP server,
its workers must receive the same value. Reuse the supervisor's environment
allowlist; do not discover/install runtimes or enable roll-forward by default.

The Linux reproduction confirms a dropped roll-forward policy, not a dropped
`DOTNET_ROOT`. The exact Mac launcher has not yet been verified.

Acceptance:
- [x] Reproduce parent initialization followed by worker startup failure.
- [x] Preserve explicit roll-forward values, including restrictive `Disable`.
- [x] Prove a real MCP worker reaches a deterministic Creatio stub after the fix.
- [x] Run Windows and Rancher Desktop Linux validation.
- [x] Complete agentic review and Ring compatibility validation.
- [ ] Request Alexandr-Kravchuk's review and live Mac tests against his environment.
- [ ] Merge only after Alex confirms the fix on his Mac.
