---
description: From CrtProcessBuilder 1.6.6.30 the SERVER refuses a build/modify/save-as-new-version payload carrying a key its contract does not declare (read-only describe fields included), so clio must never send an optional key "an older server simply ignores", and clio deliberately has no payload-key check of its own
applies-to:
  - clio/CrtProcessBuilder/CrtProcessBuilder.gz
  - clio/Command/CreateBusinessProcessCommand.cs
  - clio/Command/ModifyBusinessProcessCommand.cs
  - clio/Command/ModifyProcessAsNewVersionCommand.cs
ticket: ENG-95244
date: 2026-09-25
---

**What is true** — CrtProcessBuilder 1.6.6.30+ refuses a write whose payload carries an undeclared or wrongly-cased
key at any level: nothing is saved, and the message names each misspelled key's path with the key meant, and lists
the fields copied from describe once per place - those a write takes elsewhere with where (setFilter,
setConnections, addMapping), the read-only ones to remove - so one retry fixes them all. clio relays that text as
exit code 1 (modify also names the operation); it checks MCP tool ARGUMENTS, never payload keys. Two gaps remain: a
key next to the `request` wrapper passes in silence, and on a host where the check reports itself unavailable the
write goes ahead with a warning that it was not checked. Details: the package's `.ai/specs/ENG-95244-unknown-request-keys.md`.

**Why it is this way** — in the package, every client is covered and no copy of the contract drifts; a clio-side
check (clio#1673) was built and closed for that reason and because it weakened to a warning on version skew.

**What breaks if you ignore it** — a clio change that sends a new optional key "because an older CrtProcessBuilder
just drops it" (the reasoning in the `confirmLayoutChange` comment in `ModifyBusinessProcessCommand.cs`) is REFUSED
by any current package that does not declare it: declare it in the package and rebundle first. Convergence keeps
clio off older numbers, but the two package lines stamp crossing numbers, so an environment "newer" by number can
still lack the key. The per-block read-back guards (`FlowLabelExpectation` and the others) are only partly
redundant now: their "dropped block" half still decides on the unavailable path and across lines, and their
value warnings (no record filter, `addElement` ignoring `accessRights`) never depended on keys.
