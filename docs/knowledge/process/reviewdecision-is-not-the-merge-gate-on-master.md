---
description: a pull request's reviewDecision stays CHANGES_REQUESTED until the reviewer re-reviews, and it is not what blocks the merge - master's ruleset requires zero approvals but requires every review thread resolved
applies-to:
  - CONTRIBUTING.md
date: 2026-08-19
---

**What is true** — two independent things, often confused when driving a pull request green:

1. `reviewDecision` (the `gh pr view` summary field) is sticky. Once a reviewer submitted CHANGES_REQUESTED it keeps
   that value no matter how many fixes and replies land; only a new review from that person changes it. Read the
   review LIST and the thread state, not the summary field, to decide whether anything is still outstanding.
2. The actual gate on `master` is thread resolution. The active ruleset `master pull request quality gate`
   (`gh api repos/Advance-Technologies-Foundation/clio/rulesets`, checked 2026-08-19) has
   `required_approving_review_count: 0`, `require_last_push_approval: false`,
   `dismiss_stale_reviews_on_push: false` and `required_review_thread_resolution: true`.

**Why it is this way** — the approval count is deliberately not enforced by the platform; human review is a project
policy (`AGENTS.md`), not a branch rule. Note the third flag in particular: a push does NOT dismiss an existing
approval on this repository, so "the approval disappeared after I pushed" is not an explanation available here.

**What breaks if you ignore it** — you wait for `reviewDecision` to flip to APPROVED, which never happens on its own,
and report the pull request as blocked when nothing is; or you chase a missing approval as the merge blocker while
the real blocker is an unresolved review thread, which the summary field does not mention at all. Reading the
ruleset is one API call and settles it.
