---
description: no GitHub Actions lane runs clio.mcp.e2e.csproj, so a Category("Unit") policy or guard test placed in clio.mcp.e2e has no pre-merge gate however cheap and environment-free it is
applies-to:
  - .github/workflows/
  - clio.mcp.e2e/
ticket: ENG-92558
date: 2026-08-30
---

**What is true** — `.github/workflows/build.yml` builds `clio.tests.dll` and runs its existing
`Category!=Integration` and `Category=Integration` predicates through GitHub-only shards; it also
runs `Clio.Analyzers.Tests.csproj`. No workflow in `.github/workflows/` invokes
`clio.mcp.e2e.csproj`. A test in that project is therefore never executed by a pull-request check,
even when it is marked `[Category("Unit")]`, needs no environment and runs in milliseconds - and
`clio.mcp.e2e` does contain such tests today.

**Why it is this way** — the project was built around a live Creatio stand and is driven from
TeamCity, so it was never wired into the GitHub lanes. Whether TeamCity currently executes the
project's unit-category tests, and on which branches, is a separate question this record does not
answer: read the build configuration, do not assume.

**What breaks if you ignore it** — you write an invariant guard (a fixture-policy assertion, a
naming or seriality rule, a contract-drift oracle), see it pass locally, and believe the rule is now
enforced. It is not enforced on any pull request: a change that violates it merges green, and the
violation is only discovered later, or never. Put a guard that must block a merge in `clio.tests`,
where the unit lane will run it; keep in `clio.mcp.e2e` only what genuinely needs that project's
harness.
