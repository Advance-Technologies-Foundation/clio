# Extractor: filter, invariants, candidates

Real numbers and the noise list for the extractor that the creatio-ui team will implement under `tools/component-registry-extractor/` (location and trigger details in [jenkins-pipeline-spec.md](jenkins-pipeline-spec.md)). This document confirms that the inclusion criterion `@CrtViewElement` plus exclusion folder filters give a deterministic superset of the current manually maintained `ComponentRegistry.json`.

> **Note on the v1 architecture.** The earlier research iteration assumed the extractor output goes to an NPM package (`@creatio/component-registry`) and is then merged by a separate composer-repo. Under the CDN model adopted in this branch, the extractor output goes **directly** to the academy.creatio.com CDN — see [jenkins-pipeline-spec.md](jenkins-pipeline-spec.md). The filters, invariants, and numbers below are unchanged by that shift; only the consumer of the extracted JSON differs.

## Filtering criteria

**Inclusion filter** — a class with the `@CrtViewElement` decorator.

**Exclusion filters** (parallel, all applied):
- test files: `**/*.spec.ts`, `**/*.spec.ui.ts`, `**/*.spec.tsx`, `**/*.test.ts`, `**/*.test.tsx`
- mocks: `**/*.mock.ts`, `**/mocks/**`, `**/__mocks__/**`
- the folder `apps/pkgs/**` (built artifacts, not source)
- the folder `libs/studio-enterprise/ui/interface-designer-properties-panel/**` (designer-only UI)
- the subpath `**/designtime/**` (designer config panels — all 44 `*PropertiesPanel` components are scattered across lib folders, but strictly under `designtime/`)
- standard: `**/node_modules/**`, `dist/**`

**Technically important:** the parser must ignore `@CrtViewElement` inside comments (JSDoc examples in decorator-definition files), otherwise it picks up `usr.Example` as a "pseudo-component".

## Numbers as of `creatio-ui@master`

| Segment | Count |
|---|---|
| All `@CrtViewElement` after exclusions | **192** |
| Of those already in the manual `ComponentRegistry.json` | 92 (full intersection) |
| New candidates | 100 |
| `*PropertiesPanel` remaining in the set | 0 (filtered out by `**/designtime/**`) |

**Regression:** none of the current 92 curated components disappears from the extracted set. The auto-catalog is a superset of the manual one.

**Why `**/designtime/**` is an exact filter:** 44/44 `*PropertiesPanel` components are in `**/designtime/**`; 0/192 non-PropertiesPanel are in `**/designtime/**`. No false positives, no false negatives.

## 100 new candidates (v1 publishes them as-is)

Under v1 of the CDN model there is **no AI-side curation layer** — the extractor publishes the full 192-record set directly. AI consumers will see all 100 new candidates immediately when the first GA-tag pipeline lands. This is a deliberate trade against the earlier composer-repo approach where these were going to be hidden by `aiHidden: true` entries in `overrides.json`.

A future curation stage (if measured value justifies the maintenance cost) could re-introduce an overlay on either side:
- producer-side overlay during the Jenkins job (cleanest), or
- consumer-side overlay shipped with clio.

Neither is part of v1.

The full unfiltered candidate list (without additional semantic filtering):

```
crt.ActionDashboard              crt.HtmlCodeEditor
crt.AgentInbox                   crt.Icon
crt.AllowedResults               crt.IncomingItem
crt.Angular7XDetail              crt.IncomingItems
crt.AppBackground                crt.ItemWrapper
crt.AppToolbar                   crt.List
crt.ApprovalTile                 crt.LookupQuickFilterMenuItem
crt.ArticlePreview               crt.MessageComposerSelector
crt.ArticlesList                 crt.MessageEditor
crt.AutoTranslateToggle          crt.MessageEditorBody
crt.AutoTranslateToggleButton    crt.MessageEditorInput
crt.BaseMessageComposer          crt.MessageEditorReply
crt.BaseMessageComposerSkeleton  crt.ModuleLoader
crt.BaseTimelineLabel            crt.NavigationPanel
crt.ButtonToggleGroupItem        crt.NavigationPanelItem
crt.CallConversation             crt.NextBestOffer
crt.CampaignViewer               crt.NextBestOfferItem
crt.ChannelSelector              crt.NextStepTile
crt.ChatAttachmentTile           crt.ObjectExplorer
crt.ChatComposer                 crt.OperatorState
crt.ChatDisclaimer               crt.RichTextLinkComponent
crt.ChatItem                     crt.RichTextVideoComponent
crt.ChatList                     crt.RouterOutlet
crt.ChatQuickActions             crt.SequenceGalleryItemConfig
crt.ChatTransferSelect           crt.SkipLinks
crt.ChatTransferSelectList       crt.StepBuilder
crt.ChatTyping                   crt.Switch
crt.ComboboxAction               crt.TabPanelHeader
crt.ComponentList                crt.TabPanelHeaderItem
crt.ContactProfilePanel          crt.TableBooleanCell
crt.ConversationTemplateList     crt.TableColoredCell
crt.DataTableEditDateTimeCell    crt.TableDateTimeCell
crt.DataTableEditEmailCell       crt.TableDcmStageCell
crt.DataTableEditLookupCell      crt.TableDcmStageEditingCell
crt.DataTableEditNumericCell     crt.TableEmailCell
crt.DataTableEditPhoneCell       crt.TableFileCell
crt.DataTableEditTextCell        crt.TableFileSizeCell
crt.DataTableEditWebCell         crt.TableNumericCell
crt.DeprecatedInput              crt.TablePhoneCell
crt.DeprecatedLabel              crt.TableRichTextEditorCell
crt.Description                  crt.TableSliderCell
crt.EditTypedValueCell           crt.TableTextCell
crt.EmailComposer                crt.TemplateGallery
crt.EmojiSelect                  crt.TemplateGalleryItem
crt.EntityIcon                   crt.TemplateList
crt.FileGalleryItem              crt.TemplateSelect
crt.FilterableList               crt.Timer
                                 crt.TranslateToggle
                                 crt.TranslateToggleButton
                                 crt.TypedValueCell
                                 crt.WaterfallWidget
                                 crt.WebChatPreview
                                 crt.WebTextInput
```

Three natural subcategories of "noise" are visible. They are NOT filtered in v1, but flagging them as known noise informs both AI guidance and any future curation stage:

- **Internal table cells**: `crt.DataTableEdit*Cell`, `crt.Table*Cell`, `crt.EditTypedValueCell`, `crt.TypedValueCell` — rendered by `crt.DataGrid` itself; the AI should not instantiate them directly.
- **App shell / infra**: `crt.AppBackground`, `crt.AppToolbar`, `crt.NavigationPanel*`, `crt.RouterOutlet`, `crt.ModuleLoader`, `crt.SkipLinks` — frame elements, not for business pages.
- **Deprecated / legacy**: `crt.DeprecatedInput`, `crt.DeprecatedLabel`, `crt.Angular7XDetail`, `crt.BaseTimelineLabel`, `crt.BaseMessageComposer*` — either obsolete, or base classes without standalone UX.

**Real new candidates worth attention** (after eyeballing past those three groups): ~60 components, including `crt.ButtonToggleGroupItem`, `crt.ChatComposer`, `crt.ChatList`, `crt.EmailComposer`, `crt.EmojiSelect`, `crt.HtmlCodeEditor`, `crt.Icon`, `crt.NextBestOffer`, `crt.Switch`, `crt.TabPanelHeader`, `crt.TemplateGallery`, `crt.TemplateSelect`, `crt.Timer`, `crt.TranslateToggle`, `crt.WaterfallWidget`, `crt.WebTextInput`. AI behavior in their presence will need observation post-rollout.

## Designer config panels (`*PropertiesPanel`) — filtered out

44 `*PropertiesPanel` components (designer configuration of properties in the right pane of the Interface Designer) are scattered across 27 lib folders (`approval`, `calendar`, `chat-panel`, `compact-profile`, `feed`, `folder-tree`, `message-composer`, `timeline`, ...), but all strictly under the subpath `**/designtime/**`. This glob has been added to the extractor exclusions → no `*PropertiesPanel` makes it into the extracted set; no overlay rules are needed for them.

## Implementation location (under the CDN model)

The extractor lives at `tools/component-registry-extractor/` inside the `creatio-ui` monorepo, owned by the Platform-UI team. The Jenkins pipeline that runs the extractor is described in [jenkins-pipeline-spec.md](jenkins-pipeline-spec.md). The output is a single JSON file uploaded to academy.creatio.com per-GA-tag.

This contrasts with the earlier (now abandoned) plan where:
- The extractor still lived in creatio-ui, but
- Its output was an NPM package (`@creatio/component-registry`), and
- A separate `creatio-component-registry-composer` repo merged per-version NPM snapshots into a unified NuGet bundle.

Under the CDN model, the extractor + upload happen in a single Jenkins job; there is no intermediate consumer in the chain.

## Where these filters and numbers come from

Empirical data from a pass over `creatio-ui@master` (a prior research session). Extended architectural context is in [architecture.md](architecture.md); the target consumer structure in clio is in [clio-target-structure.md](clio-target-structure.md); the producer-side contract is in [jenkins-pipeline-spec.md](jenkins-pipeline-spec.md).
