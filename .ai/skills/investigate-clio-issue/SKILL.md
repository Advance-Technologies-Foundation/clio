---
name: investigate-clio-issue
description: Investigate a claimed Clio GitHub issue and determine whether the defect belongs to clio, clio-knowledge, or a repository referenced by clio-knowledge. Use after the claim-clio-issue skill and before implementation, including when downstream issues and GitHub blocking relationships must communicate ownership.
---

# Investigate Clio Issue

Prove the failure boundary before changing code. Keep the original Clio issue assigned to its coordinator and use it as the workflow's visible hub.

## Preconditions

Confirm that the original issue is open, assigned to the current GitHub user, linked to the expected Development branch, and has a verified `Mitigation stage = Investigating` using the canonical procedure in the `clio-issue-workflow` skill. Stop on conflicting ownership or an unverified stage.

## Diagnose ownership

1. Read the full issue, comments, labels, type, relationships, Development links, and acceptance evidence.
2. Reproduce or trace the reported behavior at the real failure boundary before proposing a fix.
3. Inspect the relevant Clio implementation, tests, documentation, MCP surface, templates, and consumers.
4. When guidance is involved, inspect the current `clio-knowledge` source and bundle metadata rather than assuming the content lives in Clio.
5. Follow referenced examples to their actual source repositories. Verify repository identity and current source before attributing ownership.
6. Classify each confirmed cause as one of:
   - Clio executable behavior or delivery mechanics.
   - `clio-knowledge` content, routing, or metadata.
   - A referenced example repository.
   - Creatio platform behavior outside these repositories.
   - Configuration or usage rather than a product defect.
   - Insufficient evidence.
7. Form the smallest evidence-backed diagnosis and explicit acceptance criteria.

If the Collab MCP server is available, engage the other coding agent after forming the initial diagnosis: Claude when running as Codex, or Codex when running as Claude. Ask for an independent, read-only challenge to the root cause, repository ownership, missing evidence, and smallest sufficient repair. Verify its claims locally and distinguish accepted, rejected, and unresolved findings. If Collab is unavailable, record that and continue; cross-agent consultation is conditional, not a blocker.

## Route downstream work

For every repository that owns required work:

1. Search for an existing issue before creating a duplicate.
2. When no suitable issue exists, create one with the original Clio issue link, evidence, acceptance criteria, and exclusions when the target is owned by `Advance-Technologies-Foundation`. Require explicit user confirmation before writing to any third-party, customer-owned, or personal repository.
3. Mark the original Clio issue as `blocked by` the downstream issue with `gh issue edit ORIGINAL --add-blocked-by DOWNSTREAM_URL`. Use `relates to` only when the downstream work is informative rather than required.
4. Add a concise diagnosis comment to the original issue with the owning repository and downstream issue link. Do not duplicate the full downstream issue body.

Do not use parent-child relationships unless the original issue intentionally represents a larger planned body of work. For multiple required repositories, the original issue may be blocked by multiple downstream issues.

Verify dependencies with `gh issue view --json blockedBy,blocking`. If a required relationship cannot be created, report the exact permission or platform failure and add a concise cross-link comment when authorized; do not silently claim the dependency exists.

For non-repository outcomes:

- Creatio platform defect: link the authorized external tracker if one exists; otherwise set `Waiting for human approval` and state the escalation needed.
- Configuration or usage: publish the evidence-backed explanation; close only when the user or repository policy authorizes closure.
- Insufficient evidence: continue investigating while safe evidence remains available. If reporter or human input is required, set `Waiting for human approval` and ask the exact question.

When no Clio code change is required, do not manufacture a Clio commit or pull request. The blocking relationship becomes the navigation path to the real work.

## Handoff

Set and verify the original issue's stage using the `clio-issue-workflow` skill:

- `Fixing` when implementation is authorized and ready to begin.
- `Waiting for human approval` when investigation is complete but a human decision, permission, or missing answer blocks progress.
- `Investigating` only while evidence gathering can still continue without human input.

Return the diagnosis, evidence, affected repositories, existing or created downstream issues, relationships added, proposed repair boundary, cross-agent contribution or unavailability, and unresolved questions.
