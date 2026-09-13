---
description: Destructive MCP e2e fixtures must resolve the configured sandbox only - the d2 fallback would silently redirect persistent writes to an unrelated stand
applies-to:
  - clio.mcp.e2e/Support/Configuration/ReachableSandboxEnvironment.cs
  - clio.mcp.e2e/SysSettingsToolE2ETests.cs
  - clio.mcp.e2e/DataForgeToolE2ETests.cs
  - clio.mcp.e2e/EntityBusinessRuleToolE2ETests.cs
  - clio.mcp.e2e/PageBusinessRuleToolE2ETests.cs
date: 2026-09-13
---

**What is true** — `ReachableSandboxEnvironment` has two resolution entry points and they are not
interchangeable. `ResolveOrIgnoreAsync` probes the configured sandbox and falls back to the registered
`d2` environment; `ResolveConfiguredOrIgnoreAsync` probes the configured sandbox only and skips the test
when it does not answer. Every fixture gated on `McpE2E:AllowDestructiveMcpTests` must use the second one.
Read-only fixtures use the first, which is the point of the shared probe.

**Why it is this way** — `AllowDestructiveMcpTests` is authorization for the disposable stand the build
deploys and then removes, named in `McpE2E:Sandbox:EnvironmentName`. It is not authorization for whatever
other registered environment happens to be reachable from the same machine. The two decisions - "is a
stand reachable" and "may this run write to it" - look like one question and are not.

**What breaks if you ignore it** — a destructive fixture whose configured sandbox is missing or still
warming up resolves to `d2` and writes persistent state there: system settings, business rules, Data Forge
structures. Nothing errors. The run reports green, and the damage shows up later as unexplained
configuration on a shared dev stand. The same rule applies to the success-only caching in both paths: a
cached refusal would turn every destructive fixture into a skip, which also reads as a green build with
no coverage.
