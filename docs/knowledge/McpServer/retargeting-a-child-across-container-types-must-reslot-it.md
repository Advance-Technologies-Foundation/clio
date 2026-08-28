---
description: InitializeContainerChildSlots declares whatever slot a child's propertyName names and never validates it against the parent's component type, so a pass that re-parents a child across container types must reset PropertyName too - a carried-over tools slot lands on a crt.GridContainer and the tab renders empty with no error
applies-to:
  - clio/Command/McpServer/Tools/MobilePageConverter/WebToMobileAnalysisService.cs
  - clio/Command/McpServer/Tools/MobilePageConverter/MobilePageConversionGuideModels.cs
ticket: ENG-96153
date: 2026-08-28
---

**What is true** — in the web-to-mobile converter, an element-map entry's `ParentName` and
`PropertyName` are two halves of one address, and only `ParentName` is obviously mutable.
`InitializeContainerChildSlots` reads each child's `PropertyName` **verbatim** and declares that slot on
the parent — it deliberately keys on "used as parent, through the slot the child itself declares", never
on a container-type list and never on a slot-name allowlist. It therefore cannot notice that the slot is
one the parent's component type does not have.

So any pass that RE-PARENTS a child must re-slot it in the same step. `BuildTabAreaLayers` is the one
that does: it moves a tab's whole top-level content onto the synthesized Area card. A web
`crt.TabContainer` declares BOTH `items` and `tools` (its header strip), `RecurseChildArrays` emits a
`tools` child with `propertyName: "tools"`, and the Area is a `crt.GridContainer`, whose only child
collection is `items`.

**Why it is this way** — the slot pass is generic on purpose (see
`container-child-slot-declaration-must-run-after-empty-container-removal`): the differ resolves an
insert's target as `itemInfo.Item[propertyName]` and throws for any slot it cannot find, so the pass
declares whatever is asked for rather than maintaining a second list of types and slots to keep in sync
with the mobile registry. Validation was never its job; correctness of the address is the retargeting
pass's job. The mobile registry is also probed optionally (`mobileByType` may be null), so the slot pass
cannot rely on knowing the parent's contract at all.

**What breaks if you ignore it** — nothing throws, on either side. The differ finds the declared `tools`
array and inserts happily; `crt.GridContainer` simply never renders a `tools` collection. The observed
production result on `Leads_FormPage` → `UsrLeads_MobileFormPage` was a Next steps tab containing only
`GridContainer_32x0qgt` — an Area whose header label and "add step" button sat in a slot nothing reads —
empty in the mobile designer and broken in the mobile runtime, while every sibling tab (whose children
all came from `items`) converted correctly. The tell is an Area card whose `mobileValues` declares any
child collection other than `items`; `WebToMobileRealPageRegressionTests` pins exactly that.

Note the tab is thin even when correct: `crt.NextSteps` is not in the mobile component registry and
`crt.AddNextStepRequest` is not in the mobile request registry, so the converted tab keeps its header and
loses the widget itself. That is the registry's verdict, not this defect — do not "fix" it by
reintroducing the widget.
