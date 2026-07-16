# get-info target classification - STORY

## Story

As a clio user or MCP agent, I want `get-info` to distinguish invalid, unavailable,
authentication-failed, non-Creatio, and malformed Creatio targets so I receive one actionable error
without misleading ClioGate guidance or leaked transport/parser details.

## Implementation tasks

1. Add scoped required-probe classification and safe diagnostics to `GetCreatioInfoCommand`.
2. Keep ClioGate compatibility and both enrichment calls non-fatal after base success.
3. Add focused Command-module unit coverage for every issue acceptance case.
4. Align `describe-environment` MCP description/guidance, unit tests, and external-process E2E.
5. Update all `get-info` documentation surfaces and verify aliases/options.
6. Run Command/McpServer tests, targeted MCP E2E, and the ClioRing compatibility/AOT gate.

## Definition of done

- All acceptance criteria in the SPEC pass.
- No normal or debug output contains raw bodies or secret-bearing values.
- Docs and MCP surfaces are aligned.
- Comprehensive agentic review has no unresolved Blocker or High finding.
- PR is ready, assigned, and armed for auto-merge.
