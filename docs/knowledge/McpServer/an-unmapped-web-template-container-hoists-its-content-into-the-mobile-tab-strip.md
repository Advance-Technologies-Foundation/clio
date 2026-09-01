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

That is why `PageWithTabsFreedomTemplate` maps BOTH halves of the general tab, and maps each onto its own
counterpart: `GeneralInfoTab` -> `GeneralInfoTab` (a `crt.TabContainer` on both sides) and
`GeneralInfoTabContainer` -> `GeneralTabContainer` (a `crt.GridContainer` on both sides). Where a page's
content lands then follows where the WEB page put it -- a page that kept the template's grid lands in the
grid, a page that removed it lands in the tab's own body -- and both receivers are inside the Details tab
and both are in `contentContainerTypes`.

Two TYPE-ALIGNED pairs were chosen over one cross-type pair (`GeneralInfoTab` -> `GeneralTabContainer`),
which also placed the content correctly. The reason is identity, not placement: a `containers` entry is
read by passes that resolve WHERE a child goes and by passes that resolve WHICH element something IS. A
cross-type pair is right for the first and wrong for the second -- the page-business-rule survivor map
turned "hide `GeneralInfoTab`" into "hide `GeneralTabContainer`", blanking the tab body while leaving its
header in the strip. Type-aligned pairs are honest for both, so the predicate that had to special-case the
cross-type twin does not need to exist, and a page that renamed the general tab's caption keeps it.

A cross-type pair is still shipped for `FeedTabContainer` -> `FeedContainer` and
`AttachmentsTabContainer` -> `AttachmentsContainer`, so the identity imprecision above remains for those
two: a page rule targeting the web Feed tab retargets onto the mobile feed BODY. Pinned by
`ConvertPageBusinessRules_CrossTypeTabTwin_RetargetsOntoTheTabBody` rather than fixed, because narrowing it
is a behaviour change beyond ENG-94951.

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

**Component TYPES are data, never constants in the analyser, and the rule is an ACCEPT-list.** Which
receivers can host arbitrary children is `contentContainerTypes`; which child is a tab is
`tabAreaLayers.tabComponentType`. The detection pass therefore names no component type at all — a type
that cannot host children (a `crt.TabPanel`, and it is not the only one) is handled by being ABSENT
from the list. A reject-list would have to enumerate the one bad receiver somebody already met, so the
next such type ships as a fresh silent defect; an accept-list has no such gap. This is not style: the
rules file is fetched at RUNTIME while the assembly is not, so a platform that renames a type is a
rules edit, and a constant would quietly stop matching on exactly the environments that changed.

`contentContainerTypes` is deliberately NOT `emptyContainerRemoval.removableTypes` — that list is this
one plus `crt.TabPanel`, correct for ITS purpose (an emptied strip must be removed). One shared list
would make an addition to one meaning silently change the other. The registry's `container` flag is not
a substitute either: it reads false for both `crt.GridContainer` and `crt.TabPanel`.

Two scoping facts the pass needs, both learned by getting them wrong first: it applies only to the
generic `items` slot (a menu item in a button's `menuItems`, a header in an expansion panel's `tools`
is hosted by that named slot, not by the parent's ability to hold arbitrary content), and a child whose
own type is the tab type is exempt (a strip exists to hold tabs). `MobileTabsElementName` is the one
constant kept, and it is a NAME, not a type: it lets the report survive an unreadable mobile template,
and `AssignConvertedTabIndexes` treats the same name as a constant of the mobile tabbed template
already.

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
