# Test Plan: update-page Designer Presence Push

## Scope

Validate the best-effort Designer Presence save push added to `update-page`.

## Unit Coverage

- `PageUpdateCommand` calls the notifier only after successful non-dry-run saves.
- `PageUpdateCommand` does not call the notifier on validation failure, conflict, dry-run, or save failure.
- `PageUpdateCommand` preserves `success=true` and appends warnings when notification is skipped or fails.
- `PageDesignerPresenceNotifier` selects the WebSocket publisher when `clientConnectionClassName` is `Terrasoft.WebSocketChannel`.
- `PageDesignerPresenceNotifier` selects the SignalR publisher when `clientConnectionClassName` is `Terrasoft.SignalRChannel`.
- `PageDesignerPresenceNotifier` maps `GetCurrentUserInfo` into the exact presence user payload.
- `WebSocketMessageChannelPublisher` sends the expected envelope body and header through its transport wrapper.
- `SignalRMessageChannelPublisher` sends the expected envelope body and header through its hub wrapper.

## MCP Review

- Review `PageUpdateTool` contract/description.
- Review prompts/resources for update-page and document when no change is needed.
- Update unit tests under `clio.tests/Command/McpServer`.

## End-to-End Coverage

- Start the real MCP server.
- Subscribe from a second session to the page’s Designer Presence sender.
- Save a seeded page through `update-page`.
- Assert receipt of a `save` Designer Presence event on at least one live transport.

## Risks

- Live notification depends on forms-auth cookies and a reachable transport endpoint.
- SignalR stands may be less available than WebSocket; cover unsupported/unavailable SignalR with unit tests even if live E2E runs only on WebSocket.
