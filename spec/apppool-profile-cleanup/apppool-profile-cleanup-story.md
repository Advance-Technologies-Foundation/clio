# Story: best-effort IIS application-pool profile cleanup

Status: in-progress

Issue: #881

As a local Creatio operator, I want uninstall to remove the IIS virtual-account profile when possible and clearly warn without failing when Windows keeps it locked.

## Definition of done

- Production cleanup and exact IIS app-pool discovery are implemented through DI.
- Unit coverage proves all cleanup outcomes, retry count, uninstall continuation, CLI success, typed MCP vocabulary, and Ring rendering.
- MCP E2E covers warning propagation and successful completion on an explicitly opted-in disposable sandbox.
- Command docs/help/index/Wiki mapping and MCP guidance/description are reviewed and synchronized.
- ClioRing contract tests, ordered replay, full Ring tests, and Windows x64 NativeAOT publish pass.
- Final comprehensive agentic review has no unresolved Blocker or High findings.

