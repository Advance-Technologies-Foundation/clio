---
description: a verb absent from every named set in CommandHelpCatalog is grouped by its index in the Program.CommandOption array (sourceIndex <= 22/34/51/103/111 bands), so where you insert the typeof() decides the help group and no test checks it
applies-to:
  - clio/HelpSystem/CommandHelpCatalog.cs
  - clio/Program.cs
  - clio.tests/HelpArtifactConsistencyTests.cs
ticket: ENG-95229
date: 2026-08-19
---

**What is true** — `CommandHelpCatalog.GetGroup` first consults the named sets
(`DeploymentCommands`, `DevelopmentCommands`, `WorkspaceCommands`, ...). A verb in none of them falls
through to positional bands keyed on `sourceIndex`: `<= 22` Application Management, `<= 34` Package
Management, `<= 51` Workspace, `<= 103` Development, `<= 111` Local Instance Management, otherwise
General. That index is nothing but the verb's position in the literal `Program.CommandOption` array
(`clio/Program.cs:43`), fed in by `BuildCommands`' `Select((type, index) => ...)`. The array now holds
~230 entries, so the bands describe only its first 112 elements.

**Why it is this way** — the bands are a frozen snapshot of the array's shape from before the named
sets existed. They were never rewritten, only bypassed for each family someone noticed.

**What breaks if you ignore it** — a new verb whose `typeof()` you insert next to a related one lands
in whichever band that neighbour's index falls in, and `clio help` files it under an unrelated
heading. The failure is silent: `VisibleCommands_ShouldHaveCanonicalArtifacts` only checks that docs,
`Commands.md` and a wiki anchor exist, never the resolved `GroupId`. Grouping is asserted solely by
per-family tests (`DeploymentIdentityCommands_...`, `ClassicToFreedomSchemaCommands_...`) whose `verbs`
arrays are hand-maintained. Enrol every new verb in a named set and in such a test; do not rely on
where the array put it.
