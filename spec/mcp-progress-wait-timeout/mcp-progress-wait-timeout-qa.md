# MCP progress wait timeout QA plan

## Automated coverage

- Run the real MCP server with the corrupt-archive deploy scenario and require the terminal failure event.
- After a completed typed-event stream, request an impossible condition and assert an explicit timeout.
- Request the terminal condition with an unrelated progress token and assert it times out with zero captured notifications.
- Replay typed events by protocol sequence before asserting manifest-to-terminal order.
- Assert timeout diagnostics identify manifest and terminal events without including fixture credentials.
- Repeat the focused fixture on .NET 8 and .NET 10 to exercise process startup, stdio notification dispatch, and teardown.

## Environment impact

The corrupt archive fails during unzip before database, IIS, Redis, or filesystem deployment stages. No Creatio installation or uninstallation is performed.
