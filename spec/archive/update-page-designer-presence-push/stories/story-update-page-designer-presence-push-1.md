# Story 1: update-page best-effort Designer Presence save push

## Status

in-progress

## Story

As a clio user saving a Freedom UI page through `update-page`,
I want active designers to receive the same Designer Presence save warning they receive from the UI,
so collaborators are warned that their open page may be outdated.

## Acceptance Criteria

- `update-page` attempts a best-effort Designer Presence `save` push only after a successful, non-dry-run save.
- The message uses the same sender/body/channel contract as the frontend.
- Forms-auth prerequisites are reused from the browser-session flow; unsupported auth/transport cases surface warnings, not failures.
- `sync-pages` and other flows do not publish the notification in v1.

## Notes

- No new CLI flags or MCP args.
- Docs and MCP descriptions must mention the best-effort notification and forms-auth prerequisite.
- E2E must prove at least one live transport with a second session receiving the save event.
