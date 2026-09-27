---
description: MCP tool output is redacted by SensitiveErrorTextRedactor, which masks the value after ANY key ending in "token" / "-token" / "_token" ("confirmation-token: abc" comes out masked) — a value the agent must read back (a confirmation code, an opaque handle) cannot be printed under such a key
applies-to:
  - clio/Common/SensitiveErrorTextRedactor.cs
  - clio/Command/ObjectRights/SetObjectRightsCommand.cs
  - clio/Command/McpServer/Tools/ObjectRightsToolResponse.cs
ticket: ENG-99741
date: 2026-09-27
---

**What is true** — `ObjectRightsToolResponse.From` (and the other tool responses built on
`SensitiveErrorTextRedactor`) redacts the success output as well as the error. Its credential key set
includes `(?:access|refresh|id|auth|...)?[_-]?token`, so `confirmation-token: 0123abcd` reaches the
agent with the value replaced by a placeholder. That is why `set-object-rights` prints
`confirmation-code: <hex>`, not `confirmation-token:`.

**Why it is this way** — the redactor matches key NAMES, not values: it cannot tell a session token
from a non-secret fingerprint, and widening it to know about one tool's keys would weaken it for all.

**What breaks if you ignore it** — the preview looks right in a CLI run and in unit tests that call the
command directly, but over MCP the agent reads a placeholder, sends it back with `confirm=true`, and
every confirmed call is refused as "does not match". Only an end-to-end call through the tool shows it
(`ObjectRightsToolBehaviourTests.SetObjectRights_ShouldPreviewThenApply_WithCodeFromPreviewOutput` and
the Sandbox e2e). Name any value an agent must echo back with a key that does not end in `token`,
`secret`, `key`, `auth` or `password`.
