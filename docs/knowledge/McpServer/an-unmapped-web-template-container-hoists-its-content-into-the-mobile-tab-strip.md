---
description: web→mobile conversion silently loses a whole tab's content when the rules file has no containers entry for a web-template container that sits inside Tabs
applies-to:
  - clio/Command/McpServer/Data/WebToMobilePageConversionRules.json
  - clio/Command/McpServer/Tools/MobilePageConverter/WebToMobilePageConversionRulesModels.cs
  - clio/Command/McpServer/Tools/MobilePageConverter/MobilePageConversionGuideTool.cs
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
`GeneralInfoTabContainer` -> `GeneralTabContainer` (a `crt.GridContainer` on both sides).

A third field settles where the CONTENT goes: `childrenTo`. The tab entry declares
`childrenTo: GeneralTabContainer`, so a page that REMOVED the template's grid and a page that KEPT it
converge on the same mobile container -- the one the acceptance criterion names. Without it the two shapes
diverge, because a type-aligned tab twin sends its children into the tab body while the grid twin sends
them into the grid, and two web pages that render identically would produce different mobile trees.
`mobile` answers WHICH element this is (identity: the page-business-rule survivor map, the caption);
`childrenTo` answers WHERE its children go (placement: the element-map walk). One field could not answer
both, which is why this mapping was reworked three times before the two questions were separated. A
`childrenTo` naming an element the mobile template does not have falls back to the twin and is reported --
the rules are CDN-fetched, and a typo there would otherwise park a whole tab's content under a name that
does not exist.

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
`CollectUnhostablePlacements` reports.

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

**Component TYPES are data, never constants in the analyser — and "not on the accept-list" is NOT the
test.** Two rules lists decide it, and it is their DIFFERENCE that means "a container this converter
knows, which cannot hold arbitrary children": a receiver is reported only when
`emptyContainerRemoval.removableTypes` recognises its type as a layout container AND
`contentContainerTypes` does not list it as content-hosting. Today that difference is exactly
`crt.TabPanel`.

The accept-list ALONE was tried first and is wrong for a detector. It names four types while the
mobile registry ships ten more with an `items` slot (`crt.Scaffold`, `crt.Gallery`, `crt.Timeline`,
`crt.List`, `crt.FileList`, `crt.ComboBox`, `crt.QuickFilterGroup`, `crt.Sort`,
`crt.CommunicationOptions`, plus any partner `usr.*` container), so "absent from the list" reported a
confident loss for every legitimate host it happened not to name — and this report tells the caller to
STOP, which turns a false positive into a halted, correct conversion. The registry cannot break the tie
either: `crt.TabPanel` declares `items` exactly like `crt.Gallery` does; what differs is that a strip's
items are tabs, which no machine-readable field says. An accept-list is right for the placement
FALLBACK (where you must know where you CAN put something); a detector needs the safe default, which is
"say nothing about a type the rules do not recognise".

The two lists are not shared — each keeps its own meaning, and `removableTypes` is correctly this list
plus `crt.TabPanel` for its own purpose. A future non-hosting type is declared by adding it to
`removableTypes` and leaving it out of `contentContainerTypes`. Neither type is named in the analyser.

Two scoping facts the pass needs, both learned by getting them wrong first: it applies only to the
generic `items` slot (a menu item in a button's `menuItems`, a header in an expansion panel's `tools`,
is hosted by that named slot, not by the parent's ability to hold arbitrary content), and a child whose
own type is `tabAreaLayers.tabComponentType` is exempt (a strip exists to hold tabs).
`MobileTabsElementName` is the one constant kept, and it is a NAME, not a type: it lets the report
survive an unreadable mobile template, and `AssignConvertedTabIndexes` treats the same name as a
constant of the mobile tabbed template already. The rules are CDN-fetched with the bundled copy as the
FAILURE fallback only, so a successfully fetched OLDER file has `containers` but neither type list —
the pass falls back to the bundled lists rather than switching itself off in the one situation it
exists for.

**What breaks if you ignore it** — the failure is SILENT end to end. Unit coverage did not catch the
missing `GeneralInfoTab` entry because `WebToMobileConversionServiceTests` hands the analyzer a
HAND-WRITTEN container map (`TabbedContainerMap`) that carried `GeneralInfoTabContainer` →
`GeneralTabContainer` while the shipped rules had no general-tab entry at all — the tests asserted a
rules file that did not exist. `TabbedContainerMap_ShouldStayASubsetOfTheShippedRules` now forbids that
class of drift. A test that must catch this defect has to load the SHIPPED rules
(`WebToMobilePageConversionRulesCatalog.LoadBundled()`) together with a REAL web-template baseline —
with no baseline, chrome subtraction never runs and the defect is unreproducible. The rules file is
also fetched from the CDN at runtime, so a published file missing an entry reintroduces the defect with
no code change; `CollectUnhostablePlacements` exists only to make that visible in the guide's
`constraints`, and it cannot repair the placement. It seeds its tab-strip set with the
`MobileTabsElementName` constant on purpose: the mobile-template probe is best-effort and yields an
EMPTY type map on failure, and the guard must not disappear in the same degraded run that most needs it.
