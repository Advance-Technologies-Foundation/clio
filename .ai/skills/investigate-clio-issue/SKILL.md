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

Do not routinely spend a cross-provider review during investigation. Engage the other coding agent only when the user explicitly requests it or a concrete high-risk ambiguity could materially change repository ownership or the repair: destructive/security/authentication behavior, concurrency, migration, public protocol compatibility, or unfamiliar Creatio platform behavior. Claude is the consultant when running as Codex; Codex is the consultant when running as Claude. Ask for a narrow, independent challenge to the current root cause and smallest repair, with concrete evidence rather than a broad audit. Verify its claims locally and distinguish accepted, rejected, and unresolved findings. If Collab is unavailable or quota-limited, record that and continue without retrying; investigation consultation is optional and is not a blocker.

## Normalize issue metadata

After the diagnosis is evidence-backed and before handing work to repair or another owner:

1. Read the repository's current labels and the organization's enabled Issue Types. Do not rely on remembered names or ids.
2. Set exactly one Issue Type that matches the diagnosis. Use `Bug` for a confirmed product defect and `Task` for actionable non-defect work when those enabled types apply; otherwise select the relevant enabled type without inventing one.
3. Ensure the issue has at least one relevant existing repository label. For a confirmed Clio defect, apply the existing `bug` label as well as Issue Type `Bug`. Add labels that communicate the confirmed classification or affected area, remove labels contradicted by the diagnosis, and preserve other still-relevant labels.
4. Re-read the issue and verify both the Issue Type and labels. Treat a successful write without matching readback as a failure.

Missing metadata is allowed during intake and investigation, but a completed investigation cannot hand off to `Fixing`, downstream repair, or closure until this gate passes. If the evidence cannot support a relevant type or label, do not guess; keep the issue in `Investigating` or set `Waiting for human approval`, state the exact classification question, and report the metadata gate as unresolved.

## Route downstream work

For every repository that owns required work:

1. Search for an existing issue before creating a duplicate.
2. When no suitable issue exists, create one with the original Clio issue link, evidence, acceptance criteria, and exclusions when the target is owned by `Advance-Technologies-Foundation`. Require explicit user confirmation before writing to any third-party, customer-owned, or personal repository.
3. Before handoff, apply the same metadata normalization and readback gate to every existing or created downstream issue that owns repair work. If its repository has no relevant enabled Issue Type or existing label, or the workflow lacks authority to set them, stop and report the exact metadata blocker rather than starting repair from an unclassified issue.
4. Mark the original Clio issue as `blocked by` the downstream issue with `gh issue edit ORIGINAL --add-blocked-by DOWNSTREAM_URL`. Use `relates to` only when the downstream work is informative rather than required.
5. Add a concise diagnosis comment to the original issue with the owning repository and downstream issue link. Do not duplicate the full downstream issue body.

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

Return the diagnosis, evidence, verified Issue Type and labels, affected repositories, existing or created downstream issues, relationships added, proposed repair boundary, cross-agent contribution or unavailability, and unresolved questions.
