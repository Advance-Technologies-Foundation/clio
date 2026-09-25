---
description: a [Newtonsoft.Json.JsonIgnore] property on EnvironmentSettings is not read from appsettings.json and its key is deleted on the next save, so a hand-written credential key silently vanishes
applies-to:
  - clio/Environment/ConfigurationOptions.cs
  - clio/Common/JsonOverflowMembers.cs
ticket: none
date: 2026-09-25
---

**What is true** — `appsettings.json` is read and written with Newtonsoft. A member marked
`[Newtonsoft.Json.JsonIgnore]` is never bound on read. The JSON key does not survive in the
`AdditionalData` overflow bag either: `JsonOverflowMembers.RemoveDeclaredMembers` removes every
name the contract declares, and ignored properties are part of the contract. The next save of ANY
environment therefore rewrites the file without that key. `AccessToken` was such a member until
issue #1624. It now keeps its runtime value in the ignored property and binds the file's value
through a private `StoredAccessToken` member with the same JSON name.

**Why it is this way** — the ignore attribute was added so that per-request passthrough secrets
(mcp-http `AccessToken`, `Cookie`) can never be written to disk or printed by `ShowSettingsTo`. The
same attribute also blocks reading, and nothing reports a key that was skipped.

**What breaks if you ignore it** — an environment whose entry holds only an `AccessToken` resolved
to settings with no credentials at all. The stdio MCP server and the CLI then sent a forms login with
no `UserName` (earlier, a login as `Supervisor`), and `list-pages` still answered `success:true`
against a permissive backend. Making such a member persistable by removing the attribute breaks the
other guarantee: a passthrough token assigned at runtime is written into `appsettings.json` by the
next save.
