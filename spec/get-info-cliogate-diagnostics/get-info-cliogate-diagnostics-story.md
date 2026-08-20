# get-info cliogate diagnostics - STORY

## Story

As a clio user or MCP agent, I want `get-info` to trust a working cliogate endpoint and explain a
failed endpoint with the detected version, so I do not receive false install-gate guidance.

## Implementation tasks

1. Make `GetSysInfo` the capability probe and demote version metadata to failed-probe diagnosis.
2. Add Command regression tests for alias skew, below-floor, installed-but-unreadable, and
   detection-failure outcomes.
3. Align CLI docs, MCP description/unit/E2E contract, and published guidance.
4. Run targeted Command/MCP tests plus ClioRing and Windows x64 NativeAOT compatibility gates.

## Definition of done

- All acceptance criteria pass.
- Final Claude and comprehensive agentic reviews have no unresolved Blocker or High findings.
- PR is ready, assigned, and armed for auto-merge.
