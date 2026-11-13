---
description: on the deployed 10.2.1122 build a Send chat message that fails on the regular path (custom text or an EmailTemplate) leaves NO chat although the OmnichannelMessaging 7.8.0 source creates the chat before the send - do not promise "no chat is created" and do not promise a left-behind empty chat either; the provider-template path creates the chat only after the provider accepts
applies-to:
  - clio.mcp.e2e/SendChatMessageElementToolE2ETests.cs
  - clio/Command/McpServer/Prompts/ProcessDesigner/DescribeProcessPrompt.cs
ticket: ENG-96034
date: 2026-11-13
---

**What is true** — in the OmnichannelMessaging 7.8.0 source (PackageStore SVN r384394) the regular send path calls
`CreateNewChat` (a Completed chat) BEFORE it renders the text, resolves the recipient and calls the provider, so a
failure there would leave an empty chat; the provider-template path (ENG-98671) creates the chat only after the
provider accepted. Measured on a test stand (Creatio 10.2.1122, CrtOmnichannelApp 1.0.63) on 2026-11-13: two
regular-path runs that failed at the provider send left `OmniChat` with 0 rows before and after, although a DCM case
process on OmniChat started inside each run - the chat was created and then removed. The deployed task's
`SysSchema.ModifiedOn` (2026-10-26) is later than that SVN revision, which has no delete path, so the deployed code
likely removes the empty chat on failure (ENG-105222, Closed). "Created then removed" is inferred; the 0-row end state
is measured.

**Why it is this way** — ENG-105222 fixed "an empty chat is left behind" after the source this repository's spec read.

**What breaks if you ignore it** — guidance or a test that says "no chat is created" is false against the source and
against older builds; one that says "an empty Completed chat remains" is false on current builds. The agreed wording
(ENG-96034 Q8): on current builds a failed send leaves no chat (measured on 10.2.1122); older OmnichannelMessaging
builds may leave an empty Completed chat. Ask the Omnichannel team before tightening it.
