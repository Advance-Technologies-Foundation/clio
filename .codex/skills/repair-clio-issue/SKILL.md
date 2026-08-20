---
name: repair-clio-issue
description: Implement and validate the evidence-backed repair for a diagnosed Clio issue in clio, clio-knowledge, or affected example repositories. Use only after ownership is established and the user authorized implementation; maintain issue stage, downstream relationships, draft PR visibility, optional Claude consultation, and repository delivery rules.
---

# Repair Clio Issue

Implement the smallest complete repair in each repository identified by `$investigate-clio-issue`. Do not revisit repository ownership without new contradictory evidence.

## Preconditions

Confirm that the original issue is open, assigned to the current GitHub user, has one verified Development branch, has an evidence-backed ownership diagnosis, and has a verified `Mitigation stage = Fixing` using `$clio-issue-workflow`. Stop on mismatch rather than bypassing claim or investigation.

## Establish each repair branch

For the original Clio issue, continue on its existing `<login>/issue-<number>` branch and isolated worktree.

For a downstream issue:

1. Respect an existing assignee and active Development branch; never take over another person's work.
2. Within `Advance-Technologies-Foundation`, if unassigned, assign the current GitHub user.
3. Within `Advance-Technologies-Foundation`, create and link `<login>/issue-<downstream-number>` in that issue's repository.
4. Require explicit user confirmation before any issue, assignment, branch, fork, or pull-request write outside `Advance-Technologies-Foundation`. If write access is unavailable, prepare the proposed change and report the required owner action; do not push.
5. Fetch the remote branch, use an isolated `.codex/worktrees/issue-<downstream-number>` worktree, and obey that repository's instructions.

Verify the original Clio issue's `Mitigation stage = Fixing` through `$clio-issue-workflow`. The original stage remains the authoritative overall status even when repair occurs downstream.

## Design and implement

1. Restate the failure and smallest sufficient end-to-end fix.
2. If Collab is available, ask Claude for an independent, read-only design review after the local diagnosis and before editing. Verify and disposition its advice; do not treat it as implementation authority. If unavailable, record that and continue.
3. Implement only the confirmed repair and required tests, documentation, generated artifacts, MCP alignment, or compatibility work mandated by the affected repository.
4. Follow all repository-specific validation, review, and delivery policies.

After the first meaningful commit, run the affected repository's mandatory pre-PR review gate, then open a draft pull request immediately. Do not create an empty or placeholder commit solely to open a PR. Link the PR to its own issue and reference the original Clio issue. A cross-repository closing reference must be fully qualified as `Advance-Technologies-Foundation/clio#<number>` and may be used only when that one PR completely resolves the original; otherwise preserve the original as the coordination issue.

## Validate and review

1. Run the proportionate tests and real-boundary validation required by the change.
2. When implementation is complete and validation or final review begins, set and verify the original issue's `Mitigation stage = QA` through `$clio-issue-workflow`.
3. If a genuine product decision or human approval is required, set and verify `Waiting for human approval`, state the exact question in the issue or PR, and stop. After approval, return to `Fixing` or `QA` according to the remaining work.
4. If Collab is available, ask Claude for an independent, read-only review of the final complete diff. Verify every finding locally and record accepted, rejected, and unresolved findings.
5. Run the repository-required agentic review and resolve all blocking findings before marking the PR ready.
6. Do not wait for human review unless a repository rule or the user explicitly requires it.

Close an unwanted draft PR rather than claiming it was deleted. Remove only its abandoned branch and worktree when authorized and safe.

## Complete

Make the draft ready only when the repair is complete, validation is recorded, and no blocking review findings remain. For Clio pull requests, hand delivery to `$pr-delivery-flow`; for other repositories, follow their equivalent delivery policy. Keep the original issue open while any required downstream issue or repair PR is unresolved. Close it only after all required PRs are merged and the reported outcome is verified.

Report each issue, repository, branch, worktree, PR, mitigation stage, validation result, Claude result or unavailability, final review result, and remaining external dependency.

Do not add coordination artifacts beyond those defined by `$clio-issue-workflow`.
