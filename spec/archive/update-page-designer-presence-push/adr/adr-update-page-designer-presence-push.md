# ADR: update-page Designer Presence Push

## Status

Accepted

## Context

Creatio already emits `ChangedSchema` during normal schema saves, but the visible designer popup is driven by a different live channel: Designer Presence. `clio update-page` saves the page successfully yet does not publish that Designer Presence event, so active designers are not warned that the page was changed outside their session.

The repo constraints for this feature are:

- no `cliogate`
- no `creatio-core` or `creatio-ui` changes
- save must stay fail-open
- scope is `update-page` only

## Decision

Add a best-effort `IPageDesignerPresenceNotifier` inside clio and invoke it only from the successful, non-dry-run `update-page` path.

The notifier:

1. Reuses `IBrowserSessionService` + `ICreatioAuthClient` to obtain forms-auth cookies.
2. Reads `ApplicationInfoService.svc/GetApplicationInfo` to resolve `serviceUrl` and `clientConnectionClassName`.
3. Reads `UserInfoService.svc/GetCurrentUserInfo` to build the user payload.
4. Publishes the message-channel envelope that the frontend Designer Presence listener filters on.
5. Returns warnings instead of errors when prerequisites are missing or the publish fails.

The `update-page` command and MCP tool remain additive: no new public arguments, only a possible success warning.

### Wire contract (verified live on a studio stand, 2026-06-12)

The front-end `DesignerPresenceService` subscribes with
`event.header.sender === "DesignerPresence_<schemaType>_<schemaName-lowercased>"` and reacts to
`body.users[].mode === "save"` from a session id other than its own. To trigger the visible
"… just updated …, reload" toast, the notifier emits a **direct broadcast** that mirrors what the
server itself fans out — NOT a client-publish to the server channel:

- `Header.Sender` = `DesignerPresence_<schemaType>_<schemaName.ToLower()>` (per-schema, not the bare `DesignerPresence` channel).
- `Header.ChannelType` = `BroadcastMsg`. A `ServerMsg` client-publish only reaches the server-side presence handler, which re-broadcasts solely to already-registered designer sessions and is silently dropped for an external (non-browser) publisher. A `BroadcastMsg` is fanned out verbatim to every connected channel, so it reaches open designers directly and matches the listener.
- `Header.BodyTypeName` = `System.String` (body is a JSON string).
- `Body` = `{ schemaName, schemaType, schemaCaption, users: [ { sessionId, id, name, contactId, contactName, photoId, email, mode: "save" } ] }`. `mode` lives inside each `users[]` element (server-event shape), and the `sessionId` must differ from the designer session being notified, or the listener filters it out as its own.

This supersedes the earlier draft that published a client-message
(`Sender="DesignerPresence"`, `ServerMsg`, single top-level `user`/`mode`) — that shape relies on
server-side presence re-broadcast which does not fire for an external publisher.

## Consequences

### Positive

- Active designers receive the same save-warning flow for clio `update-page` saves.
- No privileged backend endpoint is required.
- CLI and MCP `update-page` stay aligned through one notification chokepoint.

### Negative

- The feature depends on forms-auth cookies; OAuth-only environments cannot participate in v1.
- The push is best-effort and intentionally not transactional with the page save.
- A dedicated transport publisher is needed because `Creatio.Client` supports listen-only, not send.
- The broadcast reaches only sessions currently connected to the message channel; a designer that opens the page after the save is unaffected (acceptable — they load the fresh schema anyway).

## Rejected Alternatives

### Add a new cliogate endpoint

Rejected because the user explicitly requested a solution without `cliogate`, and the platform already exposes enough standard endpoints plus the live channel itself.

### Hook the feature into all page-save flows immediately

Rejected for v1 because the approved scope is `update-page` only. Other flows can opt in later once the notification path is proven.
