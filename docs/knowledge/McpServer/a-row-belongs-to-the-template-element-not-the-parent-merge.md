---
description: a merge property whose slot already holds a NAMED element is discarded by the mobile differ, so the grid-to-list row must be emitted as its own merge entry on the template's crt.ListItem rather than as itemLayout inside the parent crt.List's merge values - the difference is invisible, the list just renders empty
applies-to:
  - clio/Command/McpServer/Tools/MobilePageConverter/WebToMobileAnalysisService.cs
  - clio/Command/McpServer/Data/WebToMobilePageConversionRules.json
ticket: ENG-91859
date: 2026-09-07
---

**What is true** — when the mobile list template already provides the `List` and its row, the row a
grid → list conversion produces belongs on the row ELEMENT, addressed by name, not on the parent.
`itemLayout` inside a merge of the parent `crt.List` is a silent no-op: the differ drops a merge
property whose slot already holds a named element, and every OOTB mobile list template fills that
slot (`.clio-pages/{AIEmailResponseRule,Opportunities}_MobileListPage` carry
`itemLayout: {name: "ListItem", type: "crt.ListItem", …}`). The neighbouring mistake — addressing
`itemLayout` as a viewConfigDiff child SLOT — is worse and louder: `crt.List` is not a container, so
the client answers "is not a container for other items" and the whole schema fails to build.

The element's own name comes from the template probe
(`CollectSlotElementsByOwner`, keyed `<owner>/<slot>`), never from a pattern: a template may name its
row anything, and merging by a name the template does not have is the same silent no-op one level up.
When the probe knows no element for the slot there is nothing to merge onto, and the twin degrades to
an advisory merge instead of guessing.

**Why it is this way** — a structural twin (the target element is a DIFFERENT component from the web
one) has no counterpart to copy, so what the rules file declares for it is new structure. Structure
that is a separate named element is not a property of its parent, and the merge protocol addresses
elements by name; the parent's values are only for what the parent itself declares.

**What breaks if you ignore it** — nothing reports it. `update-page` saves, `validate-page` passes,
and the converted list renders with no row. The shipped guidance states the same rule
(`get-guidance freedom-page-web-to-mobile-conversion`), which is the other half of the check: if the
code and that article ever disagree about where the row goes, one of them is wrong and an agent
follows the article.
