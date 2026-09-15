---
description: clio.mcp.e2e parse-failure payload dumps reach CI only because TestResults/ is already a published TeamCity artifact
applies-to:
  - clio.mcp.e2e/Support/Results/McpPayloadDump.cs
  - clio.mcp.e2e/Support/Results/McpResultDiagnostics.cs
  - TestResults/
ticket: "#1537"
date: 2026-09-15
---

**What is true** — `Team_Atf_ClioMcpE2eTests` publishes the repository-root `TestResults/` directory
as a build artifact. Nothing in this repository configures that; it is a TeamCity job setting, and
there is no `.teamcity/` directory here to read it from. Verified on build 16026618, whose artifact
list is `TestResults/`, `aplication_logs.zip`, `performance.zip`, `WCLogs.zip` — with `TestResults/`
carrying only its committed `.gitkeep`.

That one fact is the whole reason `TestResultsPayloadDumpSink` writes where it does. A parse-failure
dump under `TestResults/mcp-payloads/` is downloadable from the failed build with no TeamCity change
and no `##teamcity[publishArtifacts]` service message. The same directory is covered by
`.gitignore`'s `[Tt]est[Rr]esult*/` rule, so the dumps never dirty a working tree locally.

**Why it is this way** — the alternatives do not reach a reader. Allure is referenced by the project
and produces `allure-results`, but that directory is **not** among the job's published artifacts, so
an Allure attachment would be written and then thrown away with the agent. A dump in the temp
directory is invisible for the same reason. Inlining the payload in the exception message — the
design this replaced — is what forced bounding and redaction and made the diagnostic collapse under
agent load (#1537).

**What breaks if you ignore it** — silently, and only on CI. Moving the dump anywhere outside
`TestResults/` still passes every local test, because locally the file is right there on disk. On the
agent the dump is written, the test reports its path, the path is correct, and the file is
unreachable — so the failure is undiagnosable exactly when a diagnosis is needed, which is the state
issue #1384 existed to remove. The sink's repository-root walk requires BOTH `clio.slnx` and
`TestResults/` to be present for the same reason: a checkout that matched on the solution file alone
could resolve to a directory the job does not publish.
