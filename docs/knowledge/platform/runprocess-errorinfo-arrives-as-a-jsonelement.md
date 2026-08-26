---
description: ProcessStartResponse.ErrorInfo is declared as object and deserializes into a JsonElement, so re-serializing it emits {"ValueKind":N} and loses the platform's error message
applies-to:
  - clio/Command/StartProcess/ProcessArgs.cs
  - clio/Command/RunProcessCommand.cs
ticket: ENG-95791
date: 2026-08-26
---

**What is true** — `ProcessStartResponse.ErrorInfo` is typed `object`, so System.Text.Json fills it with a
`JsonElement`. A reflection-based serializer then sees only that struct's public surface and renders it as
`{"ValueKind":1}`. The platform's `errorCode` and `message` must be read member by member (or via
`GetRawText`) instead.

**Why it is this way** — the DTO is shared with `PushWorkspaceCommand`, which never reads `ErrorInfo`, so
it was never given a typed shape.

**What breaks if you ignore it** — the only actionable part of a failed run — the platform's own error
message — is replaced by `{"ValueKind":1}` in whatever the caller sees. It looks like a populated error
field, so nothing signals that the message was dropped. Observed on a live stand: a failed
`MigrateDashboardsProcess` reported exactly that before the members were read individually.
