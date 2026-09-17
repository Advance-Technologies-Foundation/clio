---
description: A Copilot pull-request review can say "generated no new comments" and still carry findings, inside a collapsed Suppressed comments block in the review BODY with no inline threads
applies-to:
  - CONTRIBUTING.md
ticket: ENG-92707
date: 2026-09-17
---

**What is true** — GitHub's Copilot reviewer classifies some of what it finds as *suppressed*. Those
findings are rendered inside a collapsed `<details>Suppressed comments</details>` block in the REVIEW
BODY, and no inline review comment is created for them. The review's own summary line then reads
"Copilot reviewed N out of N changed files in this pull request and **generated no new comments**".

Read them with the reviews endpoint and print `body`:
`gh api repos/<owner>/<repo>/pulls/<n>/reviews --jq '.[].body'`
(add `GH_HOST=creatio.ghe.com` for the package repository). The inline ones come from
`…/pulls/<n>/comments`, which is a different endpoint and, for a suppressed finding, returns nothing.

**Why it is this way** — the reviewer suppresses findings it judges low-confidence or low-severity so
they do not clutter the diff view, and reports them for the record instead.

**What breaks if you ignore it** — every "are there unresolved review comments?" check answers no. On
ENG-92707 a review submitted at 06:55 carried four findings this way; two were real guard holes on a
supported operation (a negative type check that would let a future platform start kind pass an
eligibility guard, and a reference scope the retarget guard never scanned) and they sat unanswered for a
day while three separate passes reported the pull request clean. The same shape applies to any reviewer
that writes a summary body: a thread count is not a finding count.
