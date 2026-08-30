---
name: repair-clio-issue
description: Implement and validate the evidence-backed repair for a diagnosed Clio issue in clio, clio-knowledge, or affected example repositories. Use only after ownership is established and the user authorized implementation; maintain issue stage, downstream relationships, draft PR visibility, optional cross-agent consultation, and repository delivery rules.
---

# Repair Clio Issue

Implement the smallest complete repair in each repository identified by the `investigate-clio-issue` skill. Do not revisit repository ownership without new contradictory evidence.

## Preconditions

Confirm that the original issue is open, assigned to the current GitHub user, has one verified Development branch, has an evidence-backed ownership diagnosis, and has a verified `Mitigation stage = Fixing` using the `clio-issue-workflow` skill. Confirm that the original issue and every downstream issue that owns repair work have exactly one relevant enabled Issue Type and at least one relevant existing repository label verified by the investigation. If metadata is missing or contradicts the diagnosis, return to the `investigate-clio-issue` metadata gate instead of guessing during repair. Stop on mismatch rather than bypassing claim or investigation.

## Establish each repair branch

For the original Clio issue, continue on its existing `<login>/issue-<number>` branch and isolated worktree.

For a downstream issue:

1. Respect an existing assignee and active Development branch; never take over another person's work.
2. Within `Advance-Technologies-Foundation`, if unassigned, assign the current GitHub user.
3. Within `Advance-Technologies-Foundation`, create and link `<login>/issue-<downstream-number>` in that issue's repository.
4. Require explicit user confirmation before any issue, assignment, branch, fork, or pull-request write outside `Advance-Technologies-Foundation`. If write access is unavailable, prepare the proposed change and report the required owner action; do not push.
5. Fetch the remote branch, use an isolated worktree named `issue-<downstream-number>` in that repository's task-worktree area, and obey that repository's instructions.

Verify the original Clio issue's `Mitigation stage = Fixing` through the `clio-issue-workflow` skill. The original stage remains the authoritative overall status even when repair occurs downstream.

## Design and implement

1. Restate the failure and smallest sufficient end-to-end fix.
2. Do not add a routine pre-edit cross-agent review. Use one only when the user explicitly requests it or the investigation identified an unresolved high-risk ambiguity that could materially change the repair. Do not repeat an investigation consultation with the same target and focus. Verify and disposition any advice; do not treat it as implementation authority. If unavailable or quota-limited, record that and continue without retrying.
3. Implement only the confirmed repair and required tests, documentation, generated artifacts, MCP alignment, or compatibility work mandated by the affected repository.
4. Follow all repository-specific validation, review, and delivery policies.

After the first meaningful commit, run the affected repository's mandatory pre-PR review gate, then open a draft pull request immediately. Do not create an empty or placeholder commit solely to open a PR. Link the PR to its own issue and reference the original Clio issue. A cross-repository closing reference must be fully qualified as `Advance-Technologies-Foundation/clio#<number>` and may be used only when that one PR completely resolves the original; otherwise preserve the original as the coordination issue.

## Validate and review

1. Run the proportionate tests and real-boundary validation required by the change.
2. When implementation is complete and validation or final review begins, set and verify the original issue's `Mitigation stage = QA` through the `clio-issue-workflow` skill.
3. If a genuine product decision or human approval is required, set and verify `Waiting for human approval`, state the exact question in the issue or PR, and stop. After approval, return to `Fixing` or `QA` according to the remaining work.
4. Run the repository-required agentic review first, resolve blocking findings, rerun affected tests, and stabilize the complete diff.
5. If Collab is available, use one focused cross-agent review of that stable final diff: Claude when running as Codex, or Codex when running as Claude. Supply the exact revision and the load-bearing claims to challenge. Request only actionable correctness or security findings with exact evidence, not optional refactors. Verify every finding locally and record accepted, rejected, and unresolved findings.
6. Do not automatically repeat the cross-agent review after ordinary corrections. Request one narrow recheck only when an accepted Blocker/High finding caused a material change to security, destructive behavior, architecture, or a public contract. If Collab is unavailable or quota-limited, record that and continue unless the user or affected repository explicitly made cross-provider review a blocking gate. Never retry an unchanged request.
7. Do not wait for human review unless a repository rule or the user explicitly requires it.

Close an unwanted draft PR rather than claiming it was deleted. Remove only its abandoned branch and worktree when authorized and safe.

## Complete

Make the draft ready only when the repair is complete, validation is recorded, and no blocking review findings remain. For Clio pull requests, hand delivery to the repository's `pr-delivery-flow` skill when available; for other repositories, follow their equivalent delivery policy. Keep the original issue open while any required downstream issue or repair PR is unresolved. Close it only after all required PRs are merged and the reported outcome is verified.

Report each issue, repository, branch, worktree, PR, mitigation stage, validation result, cross-agent result or unavailability, final review result, and remaining external dependency.

Do not add coordination artifacts beyond those defined by the `clio-issue-workflow` skill.
