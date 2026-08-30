---
name: claim-clio-issue
description: Claim an open Advance-Technologies-Foundation/clio GitHub issue before investigation or implementation by assigning the current gh user and creating its predictable linked development branch. Use only when the user authorized taking or triaging an issue; do not use for brainstorming or read-only review.
---

# Claim Clio Issue

Claiming means assigning the authenticated `gh` user. The linked branch provides navigation, not a distributed lock.

## Claim

1. Run the `clio-issue-workflow` skill's read-only workflow-field readiness check. Stop before any GitHub write when the provisioning contract is not ready.
2. Read the live issue, assignees, state, linked branches, and linked pull requests from `Advance-Technologies-Foundation/clio`.
3. Resolve the authenticated GitHub login with `gh api user --jq .login`; use that explicit login for assignment and branch naming. Never pass the `@me` shorthand to PowerShell or another shell.
4. Interpret the current state:
   - Closed issue: stop unless the user explicitly authorized reopening it.
   - No assignee and no matching branch: assign the resolved login, then continue.
   - Current user assigned and the expected branch exists: resume idempotently.
   - Current user assigned but the branch is missing: create and link it.
   - Current user assigned and a different Development branch exists: resume that branch only after verifying it belongs to the same work; otherwise stop. Do not add a second branch.
   - Another user assigned: check for that user's linked `<assignee>/issue-<number>` branch, then stop. Report an existing branch as active work or a missing branch as incomplete visibility; neither state makes the issue free.
   - Multiple assignees, or a branch without an assignee: stop and report the ambiguity.
5. After adding the resolved login, re-read assignees once. Stop and report ambiguity if another assignee appeared; do not add coordination machinery beyond this check.
6. Read the original issue's field values. Preserve an existing `Start date`; if it is empty, set it to the current date. Set `Mitigation stage` to `Investigating` in the same additive `POST` when both values need updating, then verify both stored values through the canonical field procedure in the `clio-issue-workflow` skill.
7. Name a new branch `<login>/issue-<number>`, for example `kirillkrylov/issue-1138`.
8. Create the branch from the current canonical default branch and link it through GitHub's issue Development relationship. Use `gh issue develop` when available.
9. Fetch the new remote branch before creating an isolated linked worktree named `issue-<number>` in the repository's task-worktree area. Track the remote branch and preserve the primary checkout and unrelated user changes.

Do not inspect code, diagnose the report, or create a pull request before the claim is established.

If the date or stage update, branch creation, Development linking, or worktree creation fails after assignment, leave the visible assignment and any successfully written fields in place, report the exact incomplete setup, and stop. Do not add rollback machinery.

Do not post a routine claim comment when assignee, stage, and Development already communicate the same information. Comment only when an inconsistency or permission failure needs human attention.

## Handoff

Return the issue URL, authenticated login, assignment result, start date, branch name, Development-link result, worktree path, and mitigation stage. The `investigate-clio-issue` skill may proceed only after the issue is assigned to the current user and the linked branch exists.
