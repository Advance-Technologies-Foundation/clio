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
issue #1384 existed to remove.

**The root walk matches on `clio.slnx` alone**, and `TestResults/` is created on write rather than
required as a second marker. Requiring it to already exist was the more defensive-looking rule and the
wrong one: the walk starts at `AppContext.BaseDirectory`, the test assembly's `bin` folder inside the
real checkout, so the first ancestor carrying the solution file IS the published checkout and a nested
sample repository an e2e fixture creates is never an ancestor of it. What the extra marker did achieve
was to downgrade EVERY dump to an unpublished temp file whenever the agent's working directory had been
cleaned, a Swabra sweep had run, or the export carried no committed `.gitkeep` — the silent CI-only
failure this record exists to warn about, introduced by the guard against it.

**Retention and the fallback directory (#1593)** — dumps are not kept forever. The first write a
process makes into a directory sweeps that directory's `*.json` dumps last written more than 7 days
before the process started; later writes into the same directory do not sweep again. The window is
keyed on the process start, not on "before this run", so a parallel net8.0/net10.0 process never
deletes the other's fresh dumps. Outside a checkout the dump goes to a per-process
`clio-mcp-e2e-payloads-not-published-<guid>` directory in temp: it is only named at resolution and created, owner-only on
Unix, by the first write, so a run that never dumps leaves nothing behind. Because each run has its
own fallback directory, that first write sweeps the shared temp parent for prefixed siblings older than
the window. The sweep runs after the dump is written and is best-effort throughout: a sweep failure
leaves old files behind and never costs the diagnostic.
