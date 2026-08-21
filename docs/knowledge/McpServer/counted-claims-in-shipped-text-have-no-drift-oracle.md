---
description: nothing checks a counted claim like "with four exceptions" in an MCP tool [Description] against the guidance article that repeats it - WorkspaceTemplateGuidanceDriftTests only guards tool-name references
applies-to:
  - clio/Command/McpServer/Tools/ProcessDesigner/DescribeProcessTool.cs
  - clio.tests/Command/McpServer/WorkspaceTemplateGuidanceDriftTests.cs
date: 2026-08-19
---

**What is true** — tool `[Description]` text carries enumerated claims about behaviour
(`DescribeProcessTool` currently says a read-back round-trips "with four exceptions"), and the same
enumeration is repeated in the published guidance article. No test compares the two, and no test checks
either count against the code. `WorkspaceTemplateGuidanceDriftTests` is the only drift guard over
shipped static guidance and its oracle is deliberately about **tool-name references** being resident or
bridged, not about the claims made around them.

**Why it is this way** — the guidance half does not live in this repository. It is published from
`clio-knowledge`, so a change that adds an exception class touches two repositories on two release
schedules and there is no single diff in which the two enumerations could be checked against each
other.

**What breaks if you ignore it** — the counts drift apart and nobody notices until something is added.
The two surfaces were already disagreeing ("three exceptions" in the guidance against "two exceptions"
in the tool description) before the change that made it visible. An agent reading a stale count either
stops looking after the exceptions it was promised, or is refused by an operation the text said would
accept its input. When you add or remove an enumerated case, grep both surfaces for the count word and
fix them in the same change set; and prefer a claim that stays true either way, because a clio change
and a package change never land together and the estate never upgrades at once.
