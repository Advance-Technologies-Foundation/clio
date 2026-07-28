# update-page Designer Presence Push

## Summary

`update-page` already saves Freedom UI page bodies, but saves made through clio do not trigger the same live Designer Presence notification that Creatio designers use to warn collaborators about an out-of-date page. This small feature adds a best-effort post-save Designer Presence push for `update-page` only, without introducing `cliogate` and without changing `creatio-core` or `creatio-ui`.

## Goals

- After a successful, non-dry-run `update-page` save, clio should attempt to publish the same Designer Presence `save` event that the frontend publishes.
- The save must remain successful even when the live notification cannot be delivered.
- The first iteration must stay scoped to `update-page` only.

## Functional Requirements

| ID | Requirement | Priority |
|---|---|---|
| FR-01 | `update-page` MUST attempt a best-effort Designer Presence `save` push after a successful, non-dry-run page save. | Must |
| FR-02 | The notification path MUST reuse standard authenticated platform endpoints only (`ApplicationInfoService`, `UserInfoService`) and MUST NOT require `cliogate`. | Must |
| FR-03 | The notification path MUST reuse the browser-session/forms-auth cookie flow and MUST NOT depend on hidden `Creatio.Client` cookies. | Must |
| FR-04 | The notification path MUST support both `Terrasoft.WebSocketChannel` and `Terrasoft.SignalRChannel` transports selected from `clientConnectionClassName`. | Must |
| FR-05 | When notification prerequisites are missing or the push fails, the page save MUST still succeed and the structured response MUST include a warning. | Must |
| FR-06 | OAuth-only, credential-less, or otherwise cookie-ineligible environments MUST skip the live notification with a warning rather than failing the save. | Must |
| FR-07 | `sync-pages` and other page-save flows MUST remain unchanged in v1; only `update-page` enables the live push. | Must |

## Acceptance Criteria

- AC-01: Given a successful non-dry-run `update-page` save in a forms-auth-capable environment, when the save finishes, then clio publishes a Designer Presence broadcast with `sender="DesignerPresence_<schemaType>_<schemaName-lowercased>"`, `channelType="BroadcastMsg"`, and a body whose `users[]` carries the saving user with `mode="save"` (verified live to raise the reload toast in an open designer).
- AC-02: Given the same save in an OAuth-only or missing-credentials environment, when the save finishes, then `success=true` is preserved and the response contains a warning explaining that the live notification was skipped.
- AC-03: Given a dry-run, validation failure, conflict, or save failure, when `update-page` exits, then no Designer Presence push is attempted.
- AC-04: Given a supported live transport, when a second session listens for the page’s Designer Presence sender, then it receives a `save` event after `update-page` succeeds.

## Non-Goals

- No new CLI options or MCP arguments in v1.
- No live notification support for `sync-pages`, `create-page`, or other write flows in this iteration.
- No caption lookup beyond defaulting `schemaCaption` to `schemaName`.
