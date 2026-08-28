---
description: web→mobile conversion silently loses a whole tab's content when the rules file has no containers entry for a web-template container that sits inside Tabs
applies-to:
  - clio/Command/McpServer/Data/WebToMobilePageConversionRules.json
  - clio/Command/McpServer/Tools/MobilePageConverter/WebToMobileAnalysisService.cs
  - clio.tests/Command/McpServer/Tools/MobilePageConverter/WebToMobileGeneralInfoTabRegressionTests.cs
  - clio.tests/Command/McpServer/Fixtures/ServicesFormPageTabbed.live-snapshot.json
ticket: ENG-94951
date: 2026-08-28
---

**What is true** — `PruneTemplateComponents` subtracts every component the source page inherits from its
web template, and HOISTS the surviving page-authored descendants into the dropped node's parent. A
container is spared only when the rules file's `templates[].containers` maps it (or it is a component
twin / a `nonConvertingScopeContainers` entry). So a web-template container with no `containers` entry
does not merely lose its own properties — it re-parents its children one level up. When that container
is a tab inside `Tabs`, the children land directly in the mobile `crt.TabPanel`, which renders only
`crt.TabContainer` items: the content is gone from the converted page and the mobile designer shows
nothing.

That is why `PageWithTabsFreedomTemplate` maps `GeneralInfoTab` onto the mobile general tab's CONTENT
container (`GeneralTabContainer`), not onto the mobile tab. One entry covers both page shapes: a page
that kept the template's `GeneralInfoTabContainer` has it chrome-subtracted and its children hoisted
into the mapped tab, and a page that removed it puts its content there directly. Do NOT add a second
entry for `GeneralInfoTabContainer` — a second web name on the same mobile name buys nothing and makes
every by-`MobileName` lookup ambiguous (`containers` is already many-to-one: `CardContentWrapper` also
targets `GeneralTabContainer`).

**All three tabs of `PageWithTabsFreedomTemplate` are mapped, under the names the WEB template
actually uses.** Its tab elements are `GeneralInfoTab`, `FeedTabContainer` and
`AttachmentsTabContainer` — all three `crt.TabContainer`, all three mapped. There are no web
elements named `FeedTab` / `AttachmentsTab`: those exist only on the MOBILE template
(`MobilePageWithTabsFreedomTemplate`), where the tab and its content grid are separate elements.
Do not reason about a residual gap for a web `FeedTab`; it has no such element.

The residual exposure is therefore not in this template but in the general rule: ANY web-template
container inside a `crt.TabPanel`, in this or a future template family, that reaches the rules file
without a `containers` entry reproduces ENG-94951 verbatim. That is what
`CollectNonTabChildrenOfTabPanels` reports.

**Why it is this way** — chrome subtraction is name-based and has no notion of which mobile parent can
legally host which child; only the rules know a web container's mobile counterpart. Hoisting is the
correct default (it is what keeps page content alive when an inherited wrapper disappears), so the tab
case cannot be fixed inside the prune pass.

**A `containers` entry is a PLACEMENT rule, not an identity rule.** `GeneralInfoTab` →
`GeneralTabContainer` pairs two DIFFERENT mobile elements (the tab's `crt.TabContainer` and the
`crt.GridContainer` inside it). Passes that resolve placement must follow it; passes that resolve
identity must not. The page-business-rule survivor map is the one that must not: without
`IsTabToContentContainerTwin` guarding it, "hide `GeneralInfoTab`" converts into "hide
`GeneralTabContainer`", blanking the tab's body while leaving its header in the strip — an explicit
`droppedRules` entry turned into a silent wrong conversion.

`IsTabToContentContainerTwin` keys on the twin's SHAPE (a `merge` whose web type is
`crt.TabContainer` and whose mobile name differs), not on the general tab's name, so it covers
`FeedTabContainer` → `FeedContainer` and `AttachmentsTabContainer` → `AttachmentsContainer` too.
That is intentional and it is a behaviour CHANGE beyond ENG-94951's own symptom: before the ticket a
page rule targeting the web Feed tab was retargeted onto the mobile Feed tab's body; now it is
reported in `droppedRules`. The identity argument is identical for all three, and a name-keyed
exclusion would have left two silent wrong conversions behind. Pinned by
`ConvertPageBusinessRules_FeedTabToContentContainerTwin_DropsRuleForTheSameReason`, with the two
over-correction guards beside it (a same-name tab twin, and a renaming NON-tab twin, both still
convert).

**What breaks if you ignore it** — the failure is SILENT end to end. Unit coverage did not catch the
missing `GeneralInfoTab` entry because `WebToMobileConversionServiceTests` hands the analyzer a
HAND-WRITTEN container map (`TabbedContainerMap`) that carried `GeneralInfoTabContainer` →
`GeneralTabContainer` while the shipped rules had no general-tab entry at all — the tests asserted a
rules file that did not exist. `TabbedContainerMap_ShouldStayASubsetOfTheShippedRules` now forbids that
class of drift. A test that must catch this defect has to load the SHIPPED rules
(`WebToMobilePageConversionRulesCatalog.LoadBundled()`) together with a REAL web-template baseline —
with no baseline, chrome subtraction never runs and the defect is unreproducible. The rules file is
also fetched from the CDN at runtime, so a published file missing an entry reintroduces the defect with
no code change; `CollectNonTabChildrenOfTabPanels` exists only to make that visible in the guide's
`constraints`, and it cannot repair the placement. It seeds its tab-strip set with the
`MobileTabsElementName` constant on purpose: the mobile-template probe is best-effort and yields an
EMPTY type map on failure, and the guard must not disappear in the same degraded run that most needs it.
