# ENG-92669 — TeamCity clio e2e/unit results as PR checks — findings & runbook

🔗 **Jira:** [ENG-92669](https://creatio.atlassian.net/browse/ENG-92669) · Parent [ENG-90640](https://creatio.atlassian.net/browse/ENG-90640)

## Goal (as clarified with Alex 2026-07-09)

When a PR is opened on `github.com/Advance-Technologies-Foundation/clio`, **two** TeamCity plans
must run automatically and surface as commit-status checks in the PR checks list (like the
existing `Build / *` and `SonarCloud Code Analysis` checks):

- `Team_Atf_ClioMcpE2eTests`
- `Team_Atf_ClioUnitTests`

Chosen approach: **TeamCity-native** — TeamCity monitors PRs and posts the status back
(Pull Requests + Commit Status Publisher build features). Phase 1 is **advisory (non-blocking)**.

## Verified current state (teamcity CLI, TeamCity 2025.11, 2026-07-09)

| Fact | `Team_Atf_ClioMcpE2eTests` | `Team_Atf_ClioUnitTests` |
| --- | --- | --- |
| GitHub VCS root | `Team_Atf_ClioGitHub` (lives in **`_Root`** project) | `Team_Atf_Https…ClioGit…Master11` (lives in **`Team_Atf`**) |
| VCS `authMethod` | `ANONYMOUS` | `ANONYMOUS` |
| Branch / spec | `refs/heads/%BranchNameClio%`, no PR spec | pinned `refs/heads/master`, no PR spec |
| Trigger | VCS trigger, branchFilter `+:<default> +:8.* +:10.*`, rule `+:root=Team_Atf_ClioGitHub:**` | VCS trigger, branchFilter `+:*` |
| Pull Requests build feature | **absent** | **absent** |
| Commit Status Publisher | **absent** | **absent** |

### The blocker (gating prerequisite)

The **only** GitHub OAuth connection reachable from `Team_Atf` up the hierarchy to `_Root`
is a **GitHub App for `creatio.ghe.com` (GitHub Enterprise)** — `_Root/PROJECT_EXT_114`,
appId `11307`, ownerUrl `https://creatio.ghe.com/engineering`.

clio's PRs live on **public `github.com`**, a different host. **There is no github.com
credential in TeamCity**, so today TeamCity physically cannot:
- read PRs from github.com (Pull Requests feature), nor
- post commit statuses to github.com PR head SHAs (Commit Status Publisher).

This is the single hard prerequisite and it needs a credential that only Alex / an admin can supply.

### What I (a.kravchuk) can vs. cannot do

- Role: **`PROJECT_ADMIN` on `p:Team_Atf`**. Both build configs are under `Team_Atf`.
- **Can do myself, once a github.com credential exists:** add the Pull Requests build feature,
  the Commit Status Publisher build feature, and a PR trigger with a path filter to both configs;
  add a `Team_Atf`-level OAuth/token connection.
- **Cannot do:** edit the shared `_Root`-scoped VCS root `Team_Atf_ClioGitHub` (no `_Root` admin).
  Largely **not needed** — the Pull Requests build feature discovers and fetches `refs/pull/*`
  at runtime, so the shared root's branch spec does not have to be hand-edited.

## Decision (Alex, 2026-07-09): **Org GitHub App for github.com**

Cleaner than a PAT — no personal account, fine-grained, no token expiry churn.

### GitHub App spec (org-owner action on github.com)

Create/register a GitHub App under the **`Advance-Technologies-Foundation`** org and install it
on the **`clio`** repo (or all repos):

- **Repository permissions:**
  - **Commit statuses: Read & write** (required — Commit Status Publisher posts here)
  - **Pull requests: Read** (required — Pull Requests feature enumerates PRs)
  - **Contents: Read** (checkout / ref discovery)
  - **Metadata: Read** (mandatory default)
- **Webhooks:** not required for polling; optional if push-based PR discovery is wanted later.
- After creation, capture for TeamCity: **App ID**, **Client ID**, **Client secret**, and a
  generated **private key (.pem)**.

### TeamCity connection (I can add this at `Team_Atf` once I have the App credentials)

- Add an OAuth connection of type **GitHub App** at project `Team_Atf` (I have PROJECT_ADMIN),
  pointing at `github.com` (owner `Advance-Technologies-Foundation`), using the App ID / Client
  ID / Client secret / private key above. This is a **separate** connection from the existing
  `creatio.ghe.com` App (`_Root/PROJECT_EXT_114`).
- Then wire the two build configs per the runbook below.

**Handoff needed from Alex / org owner:** create + install the App and hand me the 4 credentials
(App ID, Client ID, Client secret, private key). Everything after that is mine to apply.

## Runbook — apply the moment a credential lands (Phase 1)

For **each** of `Team_Atf_ClioMcpE2eTests` and `Team_Atf_ClioUnitTests`:

1. **Pull Requests build feature** (GitHub):
   - VCS root: the config's github.com root; auth = the new credential.
   - Target/base branch filter: `+:master` (only PRs targeting `master`).
   - Filter by contributor if desired; keep source-branch default.
2. **Commit Status Publisher** (GitHub):
   - Auth = the new credential; publishes `pending` → `success`/`failure` to the PR head SHA.
   - Check name: e.g. `TeamCity — MCP e2e` / `TeamCity — Unit Tests`, deep-linked to the build.
3. **Trigger / cost control** (e2e only — it deploys a full Creatio, ~44–47 min):
   - Add a PR trigger with a path filter mirroring `build.yml`: `clio/**`, `clio.mcp.e2e/**`,
     `cliogate/**` (+ `Directory.Packages.props`).
   - Enable per-PR build cancellation (cancel superseded commits).
   - Keep `DeployCreatioBuild=true`, `env.McpE2E__AllowDestructiveMcpTests=true`, `ProductName=Studio`.
   - Unit-tests job is cheap — can trigger on any PR path.
4. **Keep existing trunk/release triggers unchanged** (`+:<default>`, `8.*`, `10.*`).
5. **Non-blocking**: do NOT add to GitHub branch-protection required checks (that is Phase 3).
6. Verify on a throwaway test PR: `pending` appears, then final status, deep-linked.

## Session 2 (2026-07-09) — provisioned, tested, PIVOTED approach

**Connection provisioned** (org-owner, TeamCity Automatic/manifest): `PROJECT_EXT_118`
"TeamCity ATF", GitHub App on `github.com/Advance-Technologies-Foundation`, App ID 4256196,
bot `teamcity-atf[bot]`. App perms verified complete (`gh api /apps/teamcity-atf`):
`pull_requests:read, members:read, statuses:write, checks:write, contents:write, metadata:read`.

**Publishing PROVEN:** wired Pull Requests + Commit Status Publisher on `Team_Atf_ClioUnitTests`;
a test build posted `CLIO Unit Tests (ATF)` statuses (pending→failure, deep-linked) onto real
master commit `0589b654` on github.com. App install + Commit Status Publisher confirmed working.

**Native auto-discovery does NOT fit these configs — pivoted.** The Pull Requests feature
enumerated **0 PRs**. Ruled out: author filter (EVERYBODY→0), App perms (complete), install
(statuses posted), timing (VCS poll = 1m), branch-spec (added `+:refs/heads/*` → 71 head refs
but **0 `refs/pull/*`**). Root cause is architectural: these configs use a **parameterized-branch
model**, not TeamCity branch-tracking:
- e2e root `Team_Atf_ClioGitHub` branch = `refs/heads/%BranchNameClio%` (param `BranchNameClio`, default `master`).
- unit root pinned `refs/heads/master`, driven by `reverse.dep.*.Version` (Trunk/Release lines).
The native PR feature requires branch-tracking, so it never surfaces PRs here.

## Final approach (chosen with Alex): GitHub Actions + `BranchNameClio`

Phase 1 scoped to **e2e** (`Team_Atf_ClioMcpE2eTests`, the confirmed `BranchNameClio` lever).

- `.github/workflows/teamcity-mcp-e2e.yml` on `pull_request` (paths `clio/**`, `clio.mcp.e2e/**`,
  `cliogate/**`, `Directory.Packages.props`), self-hosted (in-network), PowerShell (Windows runner).
- Triggers `POST /app/rest/buildQueue` with `BranchNameClio=<PR head ref>` + `DeployCreatioBuild=true`,
  `env.McpE2E__AllowDestructiveMcpTests=true`, `ProductName=Studio`.
- TeamCity's **Commit Status Publisher** (already-proven) posts the result onto the PR head SHA →
  shows in PR checks. Actions only triggers.
- `concurrency` per-PR cancels superseded triggers. Advisory / non-blocking.

**Dual credential:** `TEAMCITY_TOKEN` authenticates Actions→TeamCity (GitHub secret, least-privilege);
the GitHub App authenticates TeamCity→GitHub (status posting).

### e2e Phase 1 prerequisites — status

1. **TeamCity Commit Status Publisher** on `Team_Atf_ClioMcpE2eTests` — **DONE** (`BUILD_EXT_77`,
   GitHub App connection **TeamCity ATF**, `api.github.com`); validated against PR #651.
2. **GitHub secret `TEAMCITY_TOKEN`** — **DONE**. Least-privilege token `clio-pr-trigger`
   (scope: Run build + View project on ATF; deliberately cannot cancel/stop builds), added as the
   repo secret `TEAMCITY_TOKEN`.
3. **Cost control (concurrent full-Creatio deploys)** — **DONE**. GitHub `concurrency` only de-dupes
   the sub-second trigger runs (the token can't cancel builds), so concurrency is bounded on the
   TeamCity side: `Team_Atf_ClioMcpE2eTests` → `maximumNumberOfBuilds = 1` (serializes deploys,
   prevents shared-stand corruption).
4. **Repo Actions setting (fork security, to confirm by a repo admin):** the in-file job `if:`
   (same-repo PRs + manual only) is defense-in-depth; the authoritative control is **Settings →
   Actions → General → "Require approval for all external contributors"**. Neither author nor
   assignee has repo-admin to read/set it; GitHub's default already gates first-time/outside
   contributors, so this is hardening, not a merge blocker.
5. **Validate the workflow itself** (link 1: Actions → TeamCity auth) **post-merge** via
   `workflow_dispatch` (GitHub only exposes the manual trigger once the workflow is on `master`),
   or on the first real code PR; confirm the check appears. Required post-merge step.

### Fast-follow: unit-tests (`Team_Atf_ClioUnitTests`)

Its root ignores `BranchNameClio`. To PR-build it, either parameterize its (dedicated) root like e2e
(`refs/heads/%BranchNameClio%`) or add a scoped branchSpec + pass `branchName`. Constrain its existing
`+:*` VCS trigger to default-only if a branchSpec is added, or it auto-builds every branch push.
Currently `Team_Atf_ClioUnitTests` keeps the two features from the proof (Commit Status Publisher +
inert Pull Requests) — side effect: it posts `CLIO Unit Tests (ATF)` statuses on master builds.

## Out of scope (per ticket)

- Making the check **required** in branch protection (Phase 3).
- Fixing individual e2e failures (ENG-90640) and fixture parallelization (ENG-88789).
