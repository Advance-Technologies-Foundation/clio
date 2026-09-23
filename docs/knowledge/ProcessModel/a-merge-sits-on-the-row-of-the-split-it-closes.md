---
description: The generated diagram puts a merge on the row of the split it CLOSES - the deepest split every arrival descends from through at least two different branches - which retires the earlier rule that moved the merge onto a column-skipping branch's corridor, because that rule only existed to stop the client drawing the short arm through the long one
applies-to:
  - clio/CrtProcessBuilder/CrtProcessBuilder.gz
ticket: ENG-95890
date: 2026-09-16
---

**What is true** — a node with several incoming flows is placed on the row of the split it closes. The
closing split is the deepest one every arrival descends from through at least TWO different branches; a
node reached twice through the same branch closes nothing. When that cell is already taken — the trunk
can carry on past the merge that closes its split — the next candidate row is tried instead, and no node
that is already placed is ever moved to make room.

**What it replaced, and why** — an earlier rule (ENG-91853) moved the merge onto the corridor of a
branch that skipped columns. That rule existed for one reason: the client drew the skipping branch's
connector straight through whatever stood between its ends, so the corridor had to be where the merge
was. Once the package routes that connector itself — along the branch's own row and up into the merge
from below — the conflict is gone, and the corridor is where the CONNECTOR goes rather than where the
merge has to sit. Aligning with the split is also what the shipped corpus does: 70-72 % of pairable
merges sit on their split's row.

**What a reader should expect** — a diagram whose spine reads straight through instead of stepping down
one row per branch, and a merge beside the gateway that opened the branch rather than beside whichever
arm happened to be declared first.
