---
description: With CI7 absent the designer picks which edge a connector leaves by comparing the two shapes' TOP-LEFT corners on a dominant axis, so on a diagram whose rows are closer together than its columns nearly every branch reads as "more right than down" and both arms of a gateway leave from the same pixel - no placement rule can change that test
applies-to:
  - clio/CrtProcessBuilder/CrtProcessBuilder.gz
  - clio/Command/McpServer/Tools/ProcessDesigner/
ticket: ENG-98448
date: 2026-09-16
---

**What is true** — when a flow carries no `CI7`, the client derives the exit edge from
`createOrthogonalSegment(sourcePosition, targetPosition)`, which compares `|dx| > |dy|` on the two
elements' **top-left** positions: greater horizontal delta means left/right, otherwise top/bottom. The
anchor is then `position + portOffset x size`, so an element whose `Size` is `(0, 0)` docks every
connector at its top-left CORNER while still being rendered at the client's default size.

**Why it matters** — it is the mechanism behind "both branches of this gateway leave from one point",
and it cannot be steered from the placement grid. A generated diagram with a 130 px row pitch against a
180 px column pitch makes almost every branch target "more right than down", so both arms of a two-way
split get the right edge and are drawn on top of each other. Measured over ten reference graphs: good
placement with client routing still leaves 20 shared exit points, 15 shared entry points, 43 overlapping
segments, 14 segments crossing an unrelated shape and 6 crossings. Writing the geometry takes all six to
zero. Widening the rows until the test flips would mean a row pitch above the column pitch — a
permanently taller diagram — and would still not give a loop its own return lane.

**What to do with it** — store the geometry, and size every element before computing an anchor from it.
This is also what the designer itself does: 1 826 of the 2 350 flows in the 194 designer-authored
processes of the shipped corpus (78 %) carry some of `CI7`/`CI8`/`CI10`/`CI11`/`CI12`.
