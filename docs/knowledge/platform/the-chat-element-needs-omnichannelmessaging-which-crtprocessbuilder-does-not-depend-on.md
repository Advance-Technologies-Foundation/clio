---
description: the Send chat message element (SendChatMessageUserTask) ships in OmnichannelMessaging, which CrtProcessBuilder deliberately does NOT depend on - a stand without that package refuses every sendChatMessage build naming the package, and the chat e2e cases skip rather than fail there, so a green process-designer lane proves nothing for chat unless its stand carries the package
applies-to:
  - clio.mcp.e2e/SendChatMessageElementToolE2ETests.cs
  - clio/CrtProcessBuilder/CrtProcessBuilder.gz
  - clio/Command/McpServer/Tools/ProcessDesigner/CreateBusinessProcessTool.cs
ticket: ENG-96034
date: 2026-11-13
---

**What is true** — the user task `SendChatMessageUserTask` (UId `eb39d4a6-55db-40ee-a8e7-705b5ad7b9c0`) and its
compile-time types (`ChatMessageType`, `OmnichannelProviders.Domain.Entities`) live in the OmnichannelMessaging
package. CrtProcessBuilder's `DependsOn` stays empty and the package code never references those types: it finds the
task by name/UId at build time and refuses a `sendChatMessage` element on a stand without it, naming
OmnichannelMessaging. `SendChatMessageElementToolE2ETests` reads the task's `SysSchema` row first and `Assert.Ignore`s
every case without it ("install OmnichannelMessaging"); the template and run cases also skip without an active
`Channel` row or a message template of type "Chat template".

**Why it is this way** — a dependency on OmnichannelMessaging would make CrtProcessBuilder uninstallable on every
product that does not ship chat; the element is optional, the builder is not.

**What breaks if you ignore it** — the TeamCity process-designer lane runs the whole `McpE2E.ProcessDesigner`
category and goes green when the chat cases are skipped, so a regression in the chat element merges unseen if the
lane's stand lacks the package. Whether it carries OmnichannelMessaging and a chat template was an open question at
ENG-96034 (Q13, deferred); until it is answered, the live proof for chat is a manual run on a stand that has them.
Reading the skips as passes is the trap.
