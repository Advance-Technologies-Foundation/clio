---
description: every Team_Atf_ClioMcpE2eTests build reports branchName trunk because the branch under test travels in the BranchNameClio parameter, so the TeamCity branch column cannot tell a PR build from a baseline one
applies-to:
  - .github/workflows/teamcity-mcp-e2e.yml
ticket: ENG-92669
date: 2026-08-19
---

**What is true** — `Team_Atf_ClioMcpE2eTests` checks clio out from
`refs/heads/%BranchNameClio%` rather than through TeamCity branch tracking (stated in the header of
`.github/workflows/teamcity-mcp-e2e.yml`, which is what sets that parameter). The build's own logical
`branchName` is therefore unrelated to the code under test and reads `trunk` for every run, PR builds
included. The branch under test is only visible in the build's parameters (and, for runs queued by
this workflow, in the build comment, which carries the PR number and head SHA).

**Why it is this way** — TeamCity's native pull-request discovery cannot reach public github.com from
the internal server, so the branch is passed as a parameter instead. Nothing propagates that parameter
back into the build's branch identity.

**What breaks if you ignore it** — you filter or group this job's builds by branch and silently mix
PR runs with baseline runs, or you read a red PR check as a regression that trunk already had. Before
attributing a failure to a branch, compare the failing test **names** against the newest `vcs`-triggered
build of the same job (`teamcity build list --job Team_Atf_ClioMcpE2eTests`, then
`teamcity build tests <id>`); the `(N new)` count in `statusText` is relative to TeamCity's own history
of this config, not to the branch, so it is not a per-PR signal. Evidence: the workflow header for the
branch model, and repeated observation on this job — do not carry over a specific list of
already-failing tests, the baseline moves.
