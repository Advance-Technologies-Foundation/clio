---
description: a settings file a newer clio wrote binds partially in an older build - bootstrap reports settings-shape-mismatch and keeps the environments, and EVERY write path must refuse, because serializing the old model drops the section it could not bind
applies-to:
  - clio/Environment/SettingsBootstrapService.cs
  - clio/BindingsModule.cs
  - clio/Command/McpServer/Tools/SettingsHealthTool.cs
  - clio/Environment/ConfigurationOptions.cs
  - clio/Command/McpServer/Tools/ToolCommandResolver.cs
date: 2026-09-12
---

**What is true** — `SettingsBootstrapService.LoadUnlocked` deserializes with a Newtonsoft `Error`
handler that marks member-level bind failures as handled and records their JSON path. A file that is
valid JSON but carries a section this build cannot bind therefore loads in a **degraded** mode:
status `issues-detected`, issue code `settings-shape-mismatch`, environments intact,
`CanExecuteEnvTools` still true. Only an unbindable `Environments` collection is still `broken`.

Both caller-facing messages branch on the issue **code**, not on the status:
`BindingsModule.BuildBootstrapDiagnosticMessage` (the CLI startup warning) and
`ToolCommandResolver` (the MCP error). Neither may append the "fix or delete it" advice to a shape
mismatch.

In that mode **nothing may write the file**. Two sites enforce it: the bootstrap skips
`ApplyMigrations`' save, and `SettingsRepository.LoadLatestSettings` throws before any
`UpdateSettings` mutation reaches disk. Neither refusal is expressible as a status check any more —
the status is deliberately no longer `broken`, so both key off the issue **code**.

**Why it is this way** — the failure that produced this (issue #1462) is a background self-update
rewriting `appsettings.json` under a resident MCP worker running the previous build. The old binary
can read every environment in that file; it just cannot bind the newer `autoupdate` shape. Refusing
the whole file cost the user every environment for the rest of the session, and the message sent
them to hand-edit a file that was correct.

**What this does NOT cover** — the protection is one level deep and deliberately partial. A member a
newer clio ADDS is not a mismatch at all (Json.NET ignores unknown members); it survives only because
`Settings`, `EnvironmentSettings`, `AutoUpdateSettings` and `AutoUpdatePolicy` carry
`[JsonExtensionData]` overflow bags — a type WITHOUT one still loses unknown members on the next save,
silently. Each bag needs the two guards beside it: `JsonOverflowMembers.RemoveDeclaredMembers` on
deserialization (a READ-ONLY property such as `$schema` is serialized but cannot be set, so Json.NET
puts its value in the bag and the next save writes that key TWICE), and a
`System.Text.Json.JsonIgnore`, because System.Text.Json sees the bag as an ordinary property and would
put it on the wire. And while a mismatch stands, clio writes nothing at all: `reg-web-app`,
`set-active-environment` and the automatic update schedule all fail, which is why the refusal names
`clio update-cli`.

**What breaks if you ignore it** — a write in degraded mode is silent, total and irreversible for
the section involved: the old model serializes without whatever it could not bind, so the newer
clio's settings are deleted by a save that only meant to stamp a version number. And a status-based
guard reads as if it still protects this, because it did before the degraded mode existed.
