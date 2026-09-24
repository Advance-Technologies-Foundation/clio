---
description: describe-business-process reports each element's position and size and each flow's geometry chain, which is what makes a layout claim checkable without opening the designer - and reading a row off a position REQUIRES the size, because elements of different heights share a row by its centre line
applies-to:
  - clio/Command/ProcessModel/IProcessDescriber.cs
  - clio/Command/McpServer/Tools/ProcessDesigner/
  - clio/CrtProcessBuilder/CrtProcessBuilder.gz
ticket: ENG-95890
date: 2026-09-16
---

**What is true** — the read-back carries three diagram members: `position` (a shape's TOP-LEFT corner,
`"X;Y"`), `size` (`"W;H"`), and per flow a `geometry` of `start`, `points[]`, `end`, `exitSide` and
`entrySide`. All three are absent on a `CrtProcessBuilder` that predates them, and all three are
READ-ONLY by construction: no `create-business-process` or `modify-business-process` argument carries
any of them, and the server re-derives the whole layout on every save.

**Why the size is not decoration** — a diagram row is
`(Y + ceil(H / 2) - CenterY) / BranchStep`. Elements sit on a row by sharing its CENTRE line, not its
top edge, so a 31-px event and a 55-px task on one row have different `Y`. Without the height a caller
reading the diagram back cannot tell which row anything is on, which is the question most layout claims
turn out to be about.

**Why the geometry is not decoration** — every criterion about a connector is a statement about the
stored chain: orthogonal, anchored on a border, distinct exits per branch of a gateway, no segment
through an unrelated shape. Before these members the only way to check one was a person opening the
designer and looking, which is the situation ENG-95890/ENG-98448 exist to end.

**One trap** — a field an agent can SEE is a field it will try to set. The tool description and the
prompt both say the three are read-only for that reason; echoing them back into a modify changes nothing
and is not an error, it is just noise.
