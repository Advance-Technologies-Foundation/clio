---
description: a stand test that needs a hand-arranged process diagram can stage one by editing an element's position in SysSchema.MetaData and restarting the app, because no clio argument carries positions and shipped hand-arranged processes are not writable
applies-to:
  - clio.mcp.e2e/ModifyBusinessProcessToolE2ETests.cs
ticket: ENG-104758
date: 2026-10-27
---

**What is true** — no clio tool sets an element position, and a process built by `create-business-process`
is laid out on the engine's own grid (x = 60 + 180·column, row centre 200 + 130·row), so it never reads as
hand-arranged. A designer-built process stops being one at its first builder save before CrtProcessBuilder
1.6.6.125, which re-laid out every edit. The shipped hand-arranged processes (for example
`Copilot_GetCaseExternalMessages` in `CrtCaseCopilot`) live in packages installed from an archive, and the
platform refuses a save into them ("installed from the file archive").

To get a writable hand-arranged diagram on a stand: build a disposable process in `Custom`, replace one
element's `"x;y"` position inside `SysSchema.MetaData`, and run `restart-web-app`. The column holds the
metadata JSON as plain ASCII bytes, so
`CAST(REPLACE(CAST(MetaData AS varchar(max)), '"420;314"', '"700;360"') AS varbinary(max))` round-trips;
check `CAST(CAST(MetaData AS varchar(max)) AS varbinary(max)) = MetaData` and that the old value occurs
once first. `describe-business-process mode=full` then reports the moved position, and a structural
`modify-business-process` is refused as `diagramWasArrangedByHand`. Measured on 2026-10-27 on a .NET
Framework stand with MSSQL and Creatio 10.1.37.

**Why it is this way** — the layout-change gate compares stored positions with the engine's grid, and the
only other way to move an element is a drag in the process designer, which needs a browser session.

**What breaks if you ignore it** — a test of the hand-arranged case run on a freshly built process passes
whatever the server does, because nothing on it is off the grid; the e2e suite's own no-redraw test has
exactly that limit and says so. The restart drops the schema manager's cached instance; whether the moved
position is visible without it was not measured, so skipping it risks a run that never exercises the gate.
