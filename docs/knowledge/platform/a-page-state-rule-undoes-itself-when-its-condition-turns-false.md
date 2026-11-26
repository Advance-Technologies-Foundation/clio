---
description: a Freedom UI page business rule that makes an element read-only, required or hidden undoes itself when its condition turns false, so an explicit inverse rule is redundant - the basis of the inverse-redundant warning of create/update-page-business-rules
applies-to:
  - clio/Command/BusinessRules/PageBusinessRuleRedundancy.cs
ticket: ENG-107305
date: 2026-11-24
---

**What is true** — measured on a stand (core 10.2.959) on 2026-11-23 for ENG-107260 (a): a page rule
"when Name = lock, make field A read-only and hide field B" was undone as soon as Name changed to another
value: A became editable again and B was shown again, with no inverse rule on the page. Not checked: an
element that is read-only, required or hidden by default (in the page body), where the runtime might
restore the rule's state instead of the default.

**Why it is this way** — the runtime applies a page state rule as a state that holds while the condition
holds, not as a one-shot assignment. `set-values` and filter actions are one-shot and do not undo
themselves.

**What breaks if you ignore it** — an agent that follows "create an inverse rule for every state rule"
doubles the rule count of a migrated page with rules that only restore what the runtime already restores
(one reviewed page had 5 such pairs). If the default-state case turns out to differ, the
`inverse-redundant` warning in `PageBusinessRuleRedundancy` must exclude elements that are not in their
default state, and its message must change with it.
