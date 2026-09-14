---
description: The CAADT (ADAC Agents tests) TeamCity configurations run only on release days, so a regression between two releases has no CI measurement to date it
applies-to:
  - docs/knowledge/McpServer/copilot-cli-sampling-answers-only-from-its-tui.md
ticket: ENG-98526
date: 2026-09-14
---

**What is true** — the configurations under the TeamCity project
`ContinuousIntegration_ADACAgentsTests` (`AdacReleaseTestsBpmsTools`, mobikit, pixel-ninjas, iCore)
are triggered per release, not on a schedule. Between the green run of 2026-09-03 (build 15969108)
and the red run of 2026-09-14 (build 16023286) the WHOLE project ran zero builds — verified through
`/app/rest/builds?locator=affectedProject:(id:ContinuousIntegration_ADACAgentsTests)`.

**Why it is this way** — each run costs an agent, a deployed stand and real model credits (roughly
600-1300 AI credits per configuration), so the suite is reserved for release validation.

**What breaks if you ignore it** — "it worked on the 3rd and hung on the 14th" is an eleven-day
blind window, not a date. ENG-98526 could not be attributed to any change on any side because of
it: the harness revision was byte-identical, clio's two revisions behaved identically, and the
client version was never recorded by the harness at all. When you need to date a behaviour flip in
these tests, do not expect CI to bisect it — reproduce the client behaviour directly instead.
