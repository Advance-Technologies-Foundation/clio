---
description: the Creatio process designer has no URL you can construct - every guessed route (Shell hash, ViewModule.aspx, ProcessDesigner.aspx) fails or falls back to the desktop; it is entered from the process CARD page at #CardModuleV2/VwProcessLibPageV2/edit/<id>, and the designer's SAVE split button is where "Save new version" lives
applies-to:
  - docs/knowledge/platform/
ticket: ENG-94374
date: 2026-09-04
---

**What is true** — process versioning is created from the designer, and the designer is reached by
NAVIGATION, not by an address you assemble. Measured on a Freedom-shell stand (core 10.1.448.0):

- `#ProcessSchemaDesigner/<uid>` — the Shell renders the app list instead.
- `/0/Nui/ViewModule.aspx#...` — redirects to `/0/Shell/`, so every classic hash behind it is lost.
- `/0/ProcessDesigner.aspx`, `/0/ProcessSchemaDesigner.aspx` — HTTP 500 (the handler exists; the
  query-string contract is not what the name suggests).
- `#SectionModuleV2/VwProcessLibSection/` and `Navigation/Navigation.aspx?schemaName=VwProcessLibSection`
  — both land on the app list.

The route that works: open the process library, open the process, which is the CARD page at
`#CardModuleV2/VwProcessLibPageV2/edit/<id>`, and enter the designer from there. On a stand that serves
the classic UI directly the designer's own address turns out to be
`/0/Nui/ViewModule.aspx?vm=SchemaDesigner#process/<schemaUId>` — note `vm=SchemaDesigner`, not
`ProcessSchemaDesigner`, which is why guessing never lands.

Inside the designer, versioning is on the **SAVE split button**, not in ACTIONS:

- `Save new version (Ctrl+Alt+N)` — creates a new version, then raises a separate prompt, *Set the
  current version of the process "&lt;caption&gt;" actual?* YES / NO.
- `Save current version (Ctrl+Alt+S)` — the in-place overwrite.

ACTIONS carries only `Set as actual version`, which is the activation of an existing version.

**Why it is this way** — the Freedom shell routes its own module names and silently falls back to the
desktop for anything else, so an unknown hash produces a plausible-looking page rather than an error.
The classic designer predates that shell and is opened by client code that builds the URL itself.

**What breaks if you ignore it** — you conclude the capability is absent. That happened on this ticket:
five constructed routes failed, and the write-up went from "the designer is unreachable by URL" to "no
implementation that creates a version is reachable on this build", which was wrong — the card-page link
was sitting in a snapshot already taken. The cost was a detour to a second stand to satisfy an
acceptance criterion that the first stand could have satisfied. When a UI route is needed, open the
section and follow the product's own navigation; do not infer absence from a failed URL guess.
