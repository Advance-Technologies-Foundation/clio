# Keep valid contracts in mixed batches

Status: review
Issue: https://github.com/Advance-Technologies-Foundation/clio/issues/1408
Related issue: https://github.com/Advance-Technologies-Foundation/clio/issues/1406
Design: [Partial results](../adr/adr-tool-contract-partial-results.md)

As an MCP caller, I can request several plausible tool names and receive every
resolved contract with diagnostics for each missing name in the same response.

Acceptance:
- Valid contracts survive unknown names before or after them.
- Each distinct miss includes its own suggestions.
- Existing all-miss errors, catalog discovery, and malformed-name validation remain.
- Successful responses without misses omit the additive field.
- The four documented OAuth workflow commands and MCP tools are available without opt-in.
- IdentityService deployment and technical-user creation retain their feature gate.
- Focused unit and real stdio tests pass; Ring compatibility checks pass.
- Mandatory agentic review and delivery checks complete.
