---
name: pr-delivery-flow
description: Update a working branch from `master`, create or refresh a pull request, monitor build and quality gates, answer and resolve AI or human review comments, and merge the PR only after all actionable threads and checks are cleared. Use for clio delivery tasks that must go end-to-end through GitHub without missing follow-up steps.
---

# PR Delivery Flow

Canonical-Source: docs/agent-instructions/pr-delivery-flow.md
Canonical-Version: 4

Follow the canonical instructions in `docs/agent-instructions/pr-delivery-flow.md`.

Skill-specific requirements:
- Do not consider the PR complete until unresolved actionable review threads are zero.
- After each push, re-check both PR comments and review thread state on the latest head commit.
- Treat AI review feedback as real review feedback: validate it, fix it if needed, reply, and resolve the thread.
- Resolve outdated review threads too if they are still unresolved and the feedback was addressed.
- Run `python .codex/skills/pr-delivery-flow/scripts/check_sonar_new_issues.py --pr <number>` on the latest head and require exit code `0`. A green quality-gate badge is insufficient: no unresolved or accepted new Sonar issues are allowed before merge.
