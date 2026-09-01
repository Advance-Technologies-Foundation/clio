---
description: web→mobile conversion silently loses a whole tab's content when the rules file has no containers entry for a web-template container that sits inside Tabs
applies-to:
  - clio/Command/McpServer/Data/WebToMobilePageConversionRules.json
  - clio/Command/McpServer/Tools/MobilePageConverter/MobilePageConversionGuideModels.cs
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

**A container twin must be PLACED beside the content re-homed next to it.** A mobile
`crt.GridContainer` positions children by `layoutConfig` alone (see
`grid-containers-position-by-layoutconfig-not-item-order.md`), so once this fix puts page content into
`GeneralTabContainer`, the template's own `AreaProfileContainer` — a merge twin the adaptive pass used
to skip — became the single unplaced child of a grid whose every other child had a cell, and stopped
rendering. The adaptive pass therefore places twins too. It may place one ONLY where the MOBILE
template nests it: the recorded parent comes from the WEB tree and the two nestings can be inverted —
web `Tabs` sits inside `CardContentWrapper` (which maps to `GeneralTabContainer`) while on mobile
`GeneralTabContainer` sits inside `Tabs`. Trusting the web nesting placed the tab strip inside its own
descendant, twice, because `Tabs` and `CardToggleTabPanel` share one mobile name. With no mobile parent
map available the pass places no twin at all: an unplaced twin renders exactly as it does today, a
wrongly placed one does not. That map is a property of the mobile TEMPLATE, so it must be supplied
unconditionally: it was once passed only when the rule declared positional (`:top`/`:bottom`) entries,
and since only `PageWithTabsFreedomTemplate` has any, twin placement was dead for every other template
family.

**`IsTabToContentContainerTwin` keys on the twin SHAPE, not on a name.** It therefore also covers
`FeedTabContainer` → `FeedContainer` and `AttachmentsTabContainer` → `AttachmentsContainer` (both are
`crt.TabContainer` on the web template and both rename), which is deliberate. Its known limitation: it
does not look at the MOBILE type, so a future `containers` entry that renames a tab to a *tab*
(`UsrTab` → `UsrMobileTab`, both `crt.TabContainer` — one element, one identity) would lose its page
rules instead of retargeting them. Unreachable on the shipped rules today. Do NOT "fix" it by adding
`MobileType != crt.TabContainer` as-is: `MobileType` falls back to the WEB type when the mobile-template
probe failed, so that predicate would reopen the general-tab hole in exactly the degraded run the rest
of this record is about. Gate any narrowing on the mobile type having actually been read.

**Component TYPES are data, never constants in the analyser.** The tab type and the tab-strip type both
come from `tabAreaLayers` (`tabComponentType`, `tabPanelComponentType`), the same section
`BuildTabAreaLayers` already reads. That is not style: the rules file is fetched at RUNTIME while this
assembly is not, so a platform that renames either type is a rules edit — a constant here would quietly
stop matching and the tab-strip report would go silent on exactly the environments that changed. An
explicit null/empty on either type switches the pass off rather than falling back to a guess, matching
what the same value already does for `BuildTabAreaLayers`. `MobileTabsElementName` is the one
deliberate exception and it is a NAME, not a type: it seeds the strip set so the report survives an
unreadable mobile template, and `AssignConvertedTabIndexes` treats the same name as a constant of the
mobile tabbed template for the same reason.

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
