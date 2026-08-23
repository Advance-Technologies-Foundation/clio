---
description: git fetch failing with "couldn't find remote ref <branch>" usually means the pull request auto-merged and GitHub deleted the head branch - the repository has allow_auto_merge and delete_branch_on_merge both enabled
applies-to:
  - CONTRIBUTING.md
date: 2026-08-19
---

**What is true** — `Advance-Technologies-Foundation/clio` is configured with both
`allow_auto_merge: true` and `delete_branch_on_merge: true` (verify with
`gh api repos/Advance-Technologies-Foundation/clio --jq '{allow_auto_merge, delete_branch_on_merge}'`).
Because `CONTRIBUTING.md` lets a contributor or agent enable auto-merge on a ready pull request, a
branch you are still working on locally can be merged and deleted on the remote without any
further action from you - typically the moment the last required check turns green.

**Why it is this way** — auto-merge exists so a green pull request lands without a maintainer
waiting on the last check, and branch deletion on merge keeps the remote branch list usable in a
repository with this much parallel agent activity. Both are repository settings, not per-pull-request
choices, so they apply to a branch whether or not its author expected the merge to happen yet.

**What breaks if you ignore it** — the failure arrives as a git error, not as a merge notification:
a push is rejected as non-fast-forward, and the follow-up `git fetch origin <branch>` then fails
with "couldn't find remote ref". That reads as a broken local checkout, so the reflex is to
re-create or force-push the branch name, which either resurrects a merged branch or opens a second
pull request for work that already shipped. Before touching the branch, check the pull request
state (`state`, `merged`, `merged_at`); when it is merged, cherry-pick the commits that are not in
it onto a fresh branch off current `origin/master`.
