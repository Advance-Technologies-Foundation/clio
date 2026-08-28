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

**Residual gap, deliberately not closed here:** at the TAB level only the general tab is mapped. The
Feed and Attachments tabs are covered only through their *containers* (`FeedTabContainer` →
`FeedContainer`, `AttachmentsTabContainer` → `AttachmentsContainer`), which works because those
containers are themselves twins. A page that puts its own content directly under web `FeedTab` /
`AttachmentsTab`, beside the container, reproduces ENG-94951 verbatim.

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
