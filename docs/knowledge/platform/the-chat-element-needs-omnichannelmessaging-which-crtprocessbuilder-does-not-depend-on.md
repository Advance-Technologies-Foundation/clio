---
description: the Send chat message element (SendChatMessageUserTask) ships in OmnichannelMessaging, which CrtProcessBuilder deliberately does NOT depend on - a stand without that package refuses every sendChatMessage build naming the package, and the chat e2e cases skip rather than fail there; current Studio builds ship the package and its chat templates but no Channel row, which the e2e creates itself
applies-to:
  - clio.mcp.e2e/SendChatMessageElementToolE2ETests.cs
  - clio/CrtProcessBuilder/CrtProcessBuilder.gz
  - clio/Command/McpServer/Tools/ProcessDesigner/CreateBusinessProcessTool.cs
ticket: ENG-96034
date: 2026-11-14
---

**What is true** — the user task `SendChatMessageUserTask` (UId `eb39d4a6-55db-40ee-a8e7-705b5ad7b9c0`) and its
compile-time types (`ChatMessageType`, `OmnichannelProviders.Domain.Entities`) live in the OmnichannelMessaging
package. CrtProcessBuilder's `DependsOn` stays empty and the package code never references those types: it finds the
task by name/UId at build time and refuses a `sendChatMessage` element on a stand without it, naming
OmnichannelMessaging. `SendChatMessageElementToolE2ETests` reads the task's `SysSchema` row first and `Assert.Ignore`s
every case without it ("install OmnichannelMessaging"); the template cases also skip without a message template of
type "Chat template". Current Studio builds ship OmnichannelMessaging 7.8.0 and its three stock chat templates (checked
on two stands on 2026-11-14) but no `Channel` row. On a stand with no ACTIVE channel the fixture creates one SMS channel
without messenger settings, "Clio e2e chat channel", under `McpE2E:AllowDestructiveMcpTests` (without it the cases
that need a channel are Ignored); it keeps that channel, switches it back on if it was deactivated, and never deletes
it, because a Channel row cannot be deleted through the data services - see
`a-chat-channel-row-cannot-be-deleted-through-the-data-services.md`.

**Why it is this way** — a dependency on OmnichannelMessaging would make CrtProcessBuilder uninstallable on every
product that does not ship chat; the element is optional, the builder is not.

**What breaks if you ignore it** — the TeamCity process-designer lane runs the whole `McpE2E.ProcessDesigner`
category and goes green when the chat cases are skipped, so a regression in the chat element merges unseen if the
lane's stand lacks the package. The lane deploys Studio with `AllowDestructiveMcpTests=true`, so from ENG-96034 the
chat cases are expected to run there except the designer-parity case, whose designer-built fixture
(`UsrSendChatDesignerFixture`) exists only on the manual stand; confirm it in the lane's log rather than from the
status. Reading the skips as passes is the trap.
