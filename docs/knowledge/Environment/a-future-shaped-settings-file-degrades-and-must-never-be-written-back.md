---
description: a settings file a newer clio wrote binds partially in an older build - one member at a time, because Json.NET error recovery skips the rest of the container - bootstrap reports settings-shape-mismatch and keeps the environments, and EVERY write path must refuse, because serializing the old model drops the section it could not bind
applies-to:
  - clio/Environment/SettingsBootstrapService.cs
  - clio/BindingsModule.cs
  - clio/Command/McpServer/Tools/SettingsHealthTool.cs
  - clio/Command/EnvManageUiCommand.cs
  - clio/Environment/ConfigurationOptions.cs
  - clio/Command/McpServer/Tools/ToolCommandResolver.cs
date: 2026-09-12
---

**What is true** — `SettingsBootstrapService.LoadUnlocked` binds the settings file **one top-level
member at a time**, each from its own single-property object, with a Newtonsoft `Error` handler that
marks member-level failures as handled and records their JSON path.

The member-at-a-time loop is not stylistic. Json.NET's recovery for a HANDLED error calls
`reader.Skip()`, which advances to the end of the **current container** — not to the end of the
member that failed. Deserializing the whole file in one pass therefore means that a failure raised
*inside* a member's own object (`"telemetry": { "enabled": { "x": 1 } }`) discards every member
declared after it, `Environments` included: five configured environments, a report saying zero, and a
message naming only `telemetry` and claiming nothing else is affected. A failure on a scalar member
does not do this, which is why it survives casual testing. A file that is
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
silently — and so does any code that rebuilds one of these objects member by member instead of
copying it (`EnvironmentSettings.Clone`, which the environment-rename flow uses). Each bag needs the two guards beside it: `JsonOverflowMembers.RemoveDeclaredMembers` on
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
