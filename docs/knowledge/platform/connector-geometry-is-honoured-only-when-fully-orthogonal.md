---
description: The designer draws a stored connector chain verbatim only when EVERY consecutive pair of its points shares an x or a y within 2 px - one diagonal step anywhere and the whole chain is discarded and the connector re-routed by the client, silently - so a partial chain is strictly worse than writing only the two end anchors
applies-to:
  - clio/CrtProcessBuilder/CrtProcessBuilder.gz
  - clio/Command/McpServer/Tools/ProcessDesigner/
ticket: ENG-98448
date: 2026-09-16
---

**What is true** — a sequence flow stores its path as `CI11` (the exit point), `CI10` (the interior
waypoints) and `CI12` (the entry point). The client assembles those into one waypoint list, snaps the
second point to the first (and the last-but-one to the last) when they are within 2 px, and then asks
whether the list is straight: every consecutive pair must share an x or a y. If it is, the connector is
drawn exactly as stored. If it is **not**, the list is thrown away and the client routes the connector
itself between hints derived from `CI7`/`CI8` — a two-rectangle Manhattan router with no obstacle
avoidance, which bends at the mid-x between the two shapes.

**Why it matters** — the failure is silent and total. There is no partial credit: a chain that is
orthogonal for four of its five segments is not drawn four-fifths right, it is not drawn at all. So a
writer that emits geometry must either be exactly orthogonal or emit none of the waypoints, and the
second case is not a defeat: writing `CI11`/`CI12` alone still pins where the arrow leaves and arrives,
which is better than the two points the client would otherwise pick from the shapes' top-left corners.

**How it was established** — read in the client's own adapter (`base-diagram-data-adapter.js`,
`_initializeOrthogonalSegments` / `_isConnectionStraight`) and confirmed against a live stand: a stored
`m237,172 L237,67 L551,67` is rendered byte-for-byte, and the tolerance is ±2 px on every pair with no
extra slack at the ends. Measured over the shipped 7.8.0 corpus: of 1 121 stored chains, 1 119 are
orthogonal and 2 are not.

**What to do with it** — `CrtProcessBuilder` asserts orthogonality before writing and degrades a failing
chain to anchors-only with a notice naming the flow. Never write a chain you have not checked, and never
write "most of" one.
